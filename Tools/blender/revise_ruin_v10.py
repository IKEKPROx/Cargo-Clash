"""Recompose the shipped V09 FBXs while retaining their detailed art and LODs.

Run with Blender 5.3:
  blender --background --factory-startup --python Tools/blender/revise_ruin_v10.py \
    -- --source-dir /path/to/original-v09 --render-minimap

The source directory contains the two original FBXs from commit 1b12041 (the
SHA-256 values below prevent accidentally applying the transforms twice).
On the author's Mac an untouched copy lives in Art/RuinCourtyard/
Source_V09_mac_1b12041, outside the Unity checkout. No .meta file is rewritten.
Only blockers/walls and the agreed one-metre MediumSpot2 clearance correction
are updated in RuinLayout.json. Spawn, truck, door and traversal settings remain
under the runtime integration owner.

V09 merged visible meshes into spatial LOD chunks. Select whole disconnected
islands by original building bounds and atlas region, rather than deleting
chunks. Terrain and foliage UV regions are explicitly excluded. Translation
preserves the imported corner normals, UVs, colours and material assignments.
"""
import argparse
import hashlib
import json
import math
import re
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT / 'Assets/Game/Resources/RuinCourtyard'
SOURCE_HASHES = {
    'Environment': 'ee6dda087443b824bcbde7b57f79eda3e5bc3e9dbef63c975fac73721d5c586c',
    'Breakables': '60d7fba11561b596c843545ba5b2d74c93b69765149f444f36e9d12249833345',
}
REMOVE = {'U6', 'U7', 'U8', 'U9', 'U13', 'U14', 'U15', 'U16', 'U20', 'U21', 'U23'}
BUILDING_BOUNDS = {
    'U2': [25.5, 15.2, 30.2, 17.7], 'U4': [95.1, 8.55, 99.9, 13.05],
    'U6': [106.2, 30.2, 109.4, 35.85], 'U7': [9.5, 19.1, 15.1, 23.5],
    'U8': [15.5, 3.9, 23.8, 7.64], 'U9': [110.3, 4.6, 117.3, 8.3],
    'U13': [12.25, 54.6, 15.75, 59.4], 'U14': [108.9, 55.9, 113.1, 60.1],
    'U15': [6.15, 31.5, 9.85, 38.5], 'U16': [31.6, 5.25, 36.4, 8.75],
    'U20': [79.4, 4.9, 88.6, 9.1], 'U21': [114.25, 38.6, 117.75, 43.4],
    'U23': [4.8, 7.2, 9.2, 12.8],
}
BUILDING_MOVES = {'U2': (18.15, 4.7), 'U4': (-15.525, 9.425)}
WALL_TARGETS = {
    'R2': (76, 26), 'R3': (46, 22), 'R4': (82, 22), 'R6': (76, 47),
    'R7': (73, 29), 'R8': (51, 49), 'R11': (51.5, 24), 'R12': (86, 31),
}
WALL_ORIGINS = {
    'R2': (94, 45), 'R3': (27.85, 17.3), 'R4': (97.525, 12.575),
    'R6': (106.2, 28), 'R7': (107.4, 37.95), 'R8': (13.5, 42.4),
    'R11': (20.75, 7.34), 'R12': (112.625, 21.65),
}
WALL_MOVES = {k: (v[0] - WALL_ORIGINS[k][0], v[1] - WALL_ORIGINS[k][1])
              for k, v in WALL_TARGETS.items()}
REPORT = {'baselineCommit': '1b12041', 'removedBuildingGroups': sorted(REMOVE),
          'retainedBuildingGroups': 14, 'buildingMoves': BUILDING_MOVES,
          'wallTargets': WALL_TARGETS, 'environmentEdits': [],
          'cargoMarkerAdjustments': {'MediumSpot2': {'positionUnity': [71, .46, 30],
              'reason': 'Clear the relocated R7 north fixed support.'}}}


def read_array(collection, field, width=1, dtype=np.float32):
    result = np.empty(len(collection) * width, dtype=dtype)
    collection.foreach_get(field, result)
    return result.reshape((-1, width)) if width > 1 else result


def component_data(ob):
    """Find disconnected islands without welding or altering imported geometry."""
    me = ob.data
    xyz = read_array(me.vertices, 'co', 3)
    matrix = np.array(ob.matrix_world)
    world = xyz @ matrix[:3, :3].T + matrix[:3, 3]
    edges = read_array(me.edges, 'vertices', 2, np.int32)
    labels = np.arange(len(xyz), dtype=np.int32)
    for _ in range(200):
        low = np.minimum(labels[edges[:, 0]], labels[edges[:, 1]])
        updated = labels.copy()
        np.minimum.at(updated, edges[:, 0], low)
        np.minimum.at(updated, edges[:, 1], low)
        updated = updated[updated]
        if np.array_equal(updated, labels):
            break
        labels = updated
    else:
        raise RuntimeError(f'Island labelling did not converge: {ob.name}')
    _, index = np.unique(labels, return_inverse=True)
    n = index.max() + 1
    low = np.full((n, 3), np.inf)
    high = np.full((n, 3), -np.inf)
    np.minimum.at(low, index, world)
    np.maximum.at(high, index, world)
    starts = read_array(me.polygons, 'loop_start', dtype=np.int32)
    loops = read_array(me.loops, 'vertex_index', dtype=np.int32)
    face_island = index[loops[starts]]
    uv = read_array(me.uv_layers.active.data, 'uv', 2)[starts]
    tile = np.clip((uv * 4).astype(np.int32), 0, 3)
    tile = tile[:, 0] + tile[:, 1] * 4
    histogram = np.zeros((n, 16), dtype=np.int32)
    np.add.at(histogram, (face_island, tile), 1)
    return xyz, world, index, low, high, face_island, histogram


def rebuild_without_islands(ob, xyz, keep_vertices, keep_faces):
    """Copy surviving loops explicitly so UV and split normals remain aligned."""
    old = ob.data
    original_name = old.name
    totals = read_array(old.polygons, 'loop_total', dtype=np.int32)
    keep_loops = np.repeat(keep_faces, totals)
    remap = np.full(len(xyz), -1, dtype=np.int32)
    remap[keep_vertices] = np.arange(np.count_nonzero(keep_vertices), dtype=np.int32)
    old_loop_vertices = read_array(old.loops, 'vertex_index', dtype=np.int32)
    normals = read_array(old.corner_normals, 'vector', 3)[keep_loops]
    new = bpy.data.meshes.new(old.name + ' V10')
    new.vertices.add(int(keep_vertices.sum()))
    new.vertices.foreach_set('co', xyz[keep_vertices].ravel())
    new.loops.add(int(keep_loops.sum()))
    new.loops.foreach_set('vertex_index', remap[old_loop_vertices[keep_loops]])
    new.polygons.add(int(keep_faces.sum()))
    new_totals = totals[keep_faces]
    starts = np.cumsum(np.r_[np.int32(0), new_totals[:-1]], dtype=np.int32)
    new.polygons.foreach_set('loop_start', starts)
    new.polygons.foreach_set('loop_total', new_totals)
    for material in old.materials:
        new.materials.append(material)
    for field in ('material_index', 'use_smooth'):
        values = read_array(old.polygons, field, dtype=np.int32)
        new.polygons.foreach_set(field, values[keep_faces])
    new.update(calc_edges=True)
    for layer in old.uv_layers:
        target = new.uv_layers.new(name=layer.name)
        target.data.foreach_set('uv', read_array(layer.data, 'uv', 2)[keep_loops].ravel())
        target.active_render = layer.active_render
    for layer in old.color_attributes:
        if layer.domain not in {'POINT', 'CORNER'}:
            continue
        target = new.color_attributes.new(name=layer.name, type=layer.data_type, domain=layer.domain)
        mask = keep_vertices if layer.domain == 'POINT' else keep_loops
        target.data.foreach_set('color', read_array(layer.data, 'color', 4)[mask].ravel())
    new.normals_split_custom_set(normals.tolist())
    new.update()
    ob.data = new
    if old.users == 0:
        bpy.data.meshes.remove(old)
    new.name = original_name


def revise_environment():
    actions = [(k, BUILDING_BOUNDS[k], None, .35) for k in sorted(REMOVE)]
    actions += [(k, BUILDING_BOUNDS[k], d, .35) for k, d in BUILDING_MOVES.items()]
    actions += [('R2 flanking buttresses', [93.4, 40.45, 94.65, 47.5], WALL_MOVES['R2'], .18)]
    for ob in list(bpy.context.scene.objects):
        if ob.type != 'MESH' or not ob.name.startswith('RuinChunk_'):
            continue
        xyz, world, index, low, high, face_island, uv = component_data(ob)
        midpoint = (low + high) / 2
        # Stone, painted trim, stems and stone-weather decals are atlas tiles0/3.
        # Trees/foliage tiles1/2 and the entire upper terrain atlas remain intact.
        architecture = (uv[:, 0] + uv[:, 3] > 0) & (uv[:, 1:3].sum(axis=1) == 0)
        architecture &= (uv[:, 4:].sum(axis=1) == 0) & (high[:, 2] > .06)
        # A handful of tiny ground blades/litter triangles use the solid-colour
        # atlas strip too. They belong to the ground, rather than to a facade.
        ground_litter = (uv[:, 0] > 0) & (uv[:, 3] == 0) & (uv.sum(axis=1) <= 10)
        ground_litter &= (low[:, 2] <= .025) & (high[:, 2] < .35)
        architecture &= ~ground_litter
        remove = np.zeros(len(low), dtype=bool)
        changed = False
        for label, rect, delta, halo in actions:
            a, b, c, d = rect
            selected = architecture & (midpoint[:, 0] >= a-halo) & (midpoint[:, 0] <= c+halo)
            selected &= (midpoint[:, 1] >= b-halo) & (midpoint[:, 1] <= d+halo)
            if not selected.any():
                continue
            vertex_mask = selected[index]
            entry = {'chunk': ob.name, 'group': label, 'islands': int(selected.sum()),
                     'vertices': int(vertex_mask.sum()), 'faces': int(selected[face_island].sum()),
                     'operation': 'remove' if delta is None else 'translate'}
            entry['boundsBefore'] = [low[selected].min(axis=0).tolist(), high[selected].max(axis=0).tolist()]
            entry['boundsAfter'] = None if delta is None else [
                (np.array(p) + [delta[0], delta[1], 0]).tolist() for p in entry['boundsBefore']]
            REPORT['environmentEdits'].append(entry)
            if delta is None:
                remove |= selected
            else:
                shift = np.array([delta[0], delta[1], 0.0])
                # Convert a world translation into this imported mesh's local axes.
                xyz[vertex_mask] += shift @ np.linalg.inv(np.array(ob.matrix_world)[:3, :3]).T
            changed = True
        if not changed:
            continue
        if remove.any():
            rebuild_without_islands(ob, xyz, ~remove[index], ~remove[face_island])
        else:
            ob.data.vertices.foreach_set('co', xyz.ravel())
            ob.data.update()
    # Semantic hierarchy nodes are retained for surviving structures. Chunk
    # meshes are independent of these original building origins.
    pattern = re.compile(r'^(U\d+)(?:\s|/|_)')
    for ob in list(bpy.context.scene.objects):
        if ob.type == 'MESH':
            continue
        match = pattern.match(ob.name)
        if match and match[1] in REMOVE:
            bpy.data.objects.remove(ob, do_unlink=True)
    for key, delta in BUILDING_MOVES.items():
        matching = {ob for ob in bpy.context.scene.objects
                    if ob.type != 'MESH' and re.match(r'^' + key + r'(?:\s|/|_)', ob.name)}
        assert matching, key
        for root in matching:
            if root.parent in matching:
                continue
            root.location.x += delta[0]
            root.location.y += delta[1]
    for key in ('RuinButtress_4', 'RuinButtress_5'):
        root = bpy.data.objects.get(key)
        if root is not None:
            root.location.x += WALL_MOVES['R2'][0]
            root.location.y += WALL_MOVES['R2'][1]
    marker = bpy.data.objects.get('MediumSpot2')
    assert marker is not None
    marker.location.x -= 1.0
    bpy.context.view_layer.update()


def export_group(label, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(objects))
    bpy.ops.export_scene.fbx(filepath=str(RES / f'RuinCourtyard_{label}.fbx'),
        use_selection=True, object_types={'MESH', 'EMPTY'}, bake_anim=False,
        axis_forward='-Z', axis_up='Y', path_mode='AUTO', embed_textures=False,
        add_leaf_bones=False)


def translated_rect(rect, delta):
    return {k: round(v + delta[0 if k in ('x', 'z') else 1], 6) for k, v in rect.items()}


def revise_layout(source_dir):
    path = RES / 'RuinLayout.json'
    layout = json.loads(path.read_text())
    baseline = json.loads((source_dir / 'RuinLayout.json').read_text())
    # Geometry comes from the same immutable baseline as the FBXs, so rerunning
    # the generator cannot translate an already-moved blocker a second time.
    layout['blockers'], layout['walls'] = baseline['blockers'], baseline['walls']
    blockers = []
    for row in layout['blockers']:
        match = re.match(r'^(U\d+)(?:\s|/|_)', row['name'])
        group = match[1] if match else None
        if group in REMOVE:
            continue
        delta = BUILDING_MOVES.get(group)
        if row['name'].startswith('R7 fixed stone step'):
            delta = WALL_MOVES['R7']
        elif row['name'] in {'RuinButtress_4', 'RuinButtress_5'}:
            delta = WALL_MOVES['R2']
        if delta:
            row['rect'] = translated_rect(row['rect'], delta)
        blockers.append(row)
    layout['blockers'] = blockers
    for wall in layout['walls']:
        key = wall['id']
        if key not in WALL_TARGETS:
            continue
        # Derive delta from current location, making the JSON update idempotent.
        target = WALL_TARGETS[key]
        delta = (target[0]-wall['position']['x'], target[1]-wall['position']['z'])
        wall['footprintXZ'] = translated_rect(wall['footprintXZ'], delta)
        wall['position']['x'], wall['position']['z'] = target
    spot = next(row for row in layout['mediumSpots'] if row['name'] == 'MediumSpot2')
    spot['position'] = {'x': 71, 'y': .46, 'z': 30}
    path.write_text(json.dumps(layout, ensure_ascii=False, indent=2) + '\n')
    REPORT['layoutBlockerCount'] = len(blockers)


def render_minimap():
    scene = bpy.context.scene
    atlas = bpy.data.images.load(str(RES / 'RuinAtlas.png'))
    for mat in bpy.data.materials:
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get('Principled BSDF')
        if not shader:
            continue
        texture = mat.node_tree.nodes.new('ShaderNodeTexImage')
        texture.image = atlas
        mat.node_tree.links.new(texture.outputs['Color'], shader.inputs['Base Color'])
        shader.inputs['Roughness'].default_value = .8
    for ob in scene.objects:
        if ob.type != 'MESH':
            continue
        ob.hide_render = ob.name.endswith('_Far') or 'rock stratum' in ob.name
        parent = ob.parent
        while parent:
            if re.match(r'^R\d+_(Broken|Cracked)$', parent.name):
                ob.hide_render = True
            parent = parent.parent
    camera = bpy.data.cameras.new('V10 minimap north up')
    cam = bpy.data.objects.new(camera.name, camera)
    scene.collection.objects.link(cam)
    cam.location = (62, 34.5, 180)
    cam.rotation_euler = (0, 0, 0)
    camera.type, camera.ortho_scale = 'ORTHO', 124
    scene.camera = cam
    world = bpy.data.worlds.new('V10 soft sky')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.68, .74, .80, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .7
    scene.world = world
    light = bpy.data.lights.new('V10 warm sun', 'SUN')
    light.energy, light.angle = 2.4, .18
    sun = bpy.data.objects.new(light.name, light)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(24), math.radians(-20), math.radians(-25))
    scene.render.engine = 'CYCLES'
    scene.cycles.samples, scene.cycles.use_denoising = 16, True
    scene.render.resolution_x, scene.render.resolution_y = 1736, 966
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'AgX'
    scene.render.filepath = str(RES / 'RuinMinimap.png')
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-dir', type=Path,
        default=ROOT.parents[1] / 'Art/RuinCourtyard/Source_V09_mac_1b12041')
    parser.add_argument('--render-minimap', action='store_true')
    parser.add_argument('--report', type=Path, default=Path('/tmp/ruin_v10_art_report.json'))
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    for label, digest in SOURCE_HASHES.items():
        path = args.source_dir / f'RuinCourtyard_{label}.fbx'
        assert hashlib.sha256(path.read_bytes()).hexdigest() == digest, f'Not the original V09 source: {path}'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.source_dir / 'RuinCourtyard_Environment.fbx'), use_anim=False)
    revise_environment()
    environment = set(bpy.context.scene.objects)
    export_group('Environment', environment)
    bpy.ops.import_scene.fbx(filepath=str(args.source_dir / 'RuinCourtyard_Breakables.fbx'), use_anim=False)
    breakables = set(bpy.context.scene.objects) - environment
    shared_material = bpy.data.materials.get('RuinAtlas')
    for ob in breakables:
        if ob.type == 'MESH':
            for slot in ob.material_slots:
                if slot.material and re.sub(r'\.\d+$', '', slot.material.name) == 'RuinAtlas':
                    slot.material = shared_material
    for key, delta in WALL_MOVES.items():
        pivot = next(ob for ob in breakables if ob.name == key + ' / ground pivot')
        pivot.location.x += delta[0]
        pivot.location.y += delta[1]
    bpy.context.view_layer.update()
    export_group('Breakables', breakables)
    revise_layout(args.source_dir)
    args.report.write_text(json.dumps(REPORT, ensure_ascii=False, indent=2) + '\n')
    print('V10 FBX exports and blockers/walls complete. Report:', args.report, flush=True)
    if args.render_minimap:
        render_minimap()


if __name__ == '__main__':
    main()

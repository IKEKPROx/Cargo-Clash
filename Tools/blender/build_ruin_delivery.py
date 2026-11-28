"""Turn the game's export of the Maya Ruins level into one self-contained .blend for handing over.

1. Run the game with -ccExportLevel <folder> (AutoPilot.Export.cs): it writes MayaRuins.obj/.mtl (every visible
   level mesh as built in the game, x east, y north, z up) and placements.json (cargo and truck).
2. Run this:  Blender --background --factory-startup --python Tools/blender/build_ruin_delivery.py -- <folder> <out.blend>
   It imports the level, sets up the materials, adds the layout as labelled markers (spawns, cargo spots with
   their values, the truck's delivery zone, pit recovery points, boost and ladder points, breakable walls, the
   play area), a sun and a camera, packs every texture and saves the .blend. Characters, the truck, the
   helicopter and the cargo models are not part of the map and are left out.
"""
import json
import math
import shutil
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
args = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = Path(args[0]), Path(args[1])
TEXTURES = {
    'RuinAtlas.png': ROOT / 'Assets/Game/Resources/RuinCourtyard/RuinAtlas.png',
    'RuinRoad.png': ROOT / 'Assets/Game/Resources/RuinCourtyard/RuinRoad.png',
    'RuinDecals.png': ROOT / 'Assets/Game/Resources/RuinCourtyard/RuinDecals.png',
    'UI_Hazard.png': ROOT / 'Assets/Game/UI/UI_Hazard.png',
}
# Runtime materials the export could not name a texture for (custom shader), matched by name.
BY_NAME = {'Ruin_road_surface': 'RuinRoad.png', 'Ruin_ground_paint': 'RuinDecals.png'}

bpy.ops.wm.read_factory_settings(use_empty=True)
for name, path in TEXTURES.items():
    shutil.copyfile(path, SRC / name)

# ------------------------------------------------------------------ the level
bpy.ops.wm.obj_import(filepath=str(SRC / 'MayaRuins.obj'), forward_axis='Y', up_axis='Z')
level = [o for o in bpy.context.scene.objects if o.type == 'MESH']
collection = bpy.data.collections.new('Level')
bpy.context.scene.collection.children.link(collection)
for ob in level:
    for c in ob.users_collection:
        c.objects.unlink(ob)
    collection.objects.link(ob)

vertex_colour = set()
current = None
for line in (SRC / 'MayaRuins.mtl').read_text().splitlines():
    if line.startswith('newmtl '):
        current = line.split()[1]
    elif line.strip() == '# vertexcolor' and current:
        vertex_colour.add(current)


def image(name):
    img = bpy.data.images.get(name) or bpy.data.images.load(str(SRC / name))
    return img


for mat in bpy.data.materials:
    if mat.node_tree is None:
        mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        continue
    bsdf.inputs['Roughness'].default_value = .8
    base = mat.name.split('.')[0]
    stem = base.rsplit('_', 1)[0]
    if base in vertex_colour:
        # Foliage, bark, the Maya stone tints and the guardrails carry their colour per vertex.
        attr = nodes.new('ShaderNodeVertexColor')
        # The game's vertex colours are linear; the OBJ importer reads them as sRGB, which darkens them.
        gamma = nodes.new('ShaderNodeGamma')
        gamma.inputs['Gamma'].default_value = 1 / 2.2
        links.new(attr.outputs['Color'], gamma.inputs['Color'])
        links.new(gamma.outputs['Color'], bsdf.inputs['Base Color'])
    if stem in BY_NAME:
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = image(BY_NAME[stem])
        links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
        for attr, value in (('surface_render_method', 'BLENDED'), ('blend_method', 'BLEND')):
            try:
                setattr(mat, attr, value)
            except (AttributeError, TypeError):
                pass
    for n in nodes:
        if n.type == 'TEX_IMAGE' and n.image is not None and n.image.name in ('RuinRoad.png', 'RuinDecals.png'):
            links.new(n.outputs['Alpha'], bsdf.inputs['Alpha'])

# ------------------------------------------------------------------ layout markers
layout = json.loads((ROOT / 'Assets/Game/Resources/RuinCourtyard/RuinLayout.json').read_text())
markers = bpy.data.collections.new('Layout markers')
bpy.context.scene.collection.children.link(markers)


def group(name):
    c = bpy.data.collections.new(name)
    markers.children.link(c)
    return c


def marker(col, name, p, shape='PLAIN_AXES', size=.6, yaw=None, **props):
    """p is a position in the game's layout (x east, y up, z north)."""
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = shape
    ob.empty_display_size = size
    ob.location = (p['x'], p['z'], p['y'])
    if yaw is not None:
        ob.rotation_euler = (0, 0, math.radians(-yaw))
    for k, v in props.items():
        ob[k] = v
    col.objects.link(ob)
    return ob


spawns = group('Spawns')
for team, key in (('Collector', 'collectorSpawns'), ('Interrupter', 'interrupterSpawns')):
    for k, s in enumerate(layout[key]):
        marker(spawns, f'{team} spawn {k + 1}', s['position'], 'SINGLE_ARROW', 1.0, s.get('yaw', 0), team=team)
cargo = group('Cargo spots')
for key, value, kind in (('smallSpots', 10, 'Tobacco or Cash'), ('mediumSpots', 30, 'Gold'), ('largeSpots', 100, 'Art (two carriers)')):
    for s in layout[key]:
        marker(cargo, f"{s['name']} ({value})", s['position'], 'CUBE', .3 if value == 10 else .4 if value == 30 else .55,
               s.get('yaw', 0), value=value, cargo=kind)
zone = group('Truck delivery zone')
tz, half = layout['truckZone'], layout['truckZoneHalfSize']
ob = marker(zone, 'Truck delivery zone', tz, 'CUBE', 1.0, None, note='Cargo dropped in here scores')
ob.scale = (half['x'], half['y'], 1.0)
pits = group('Pits')
for k, p in enumerate(layout['pits']):
    ob = marker(pits, f'Pit {k + 1}', {'x': (p['x'] + p['z']) / 2, 'y': -1.75, 'z': (p['y'] + p['w']) / 2}, 'CUBE', 1.0)
    ob.scale = ((p['z'] - p['x']) / 2, (p['w'] - p['y']) / 2, 1.75)
for k, p in enumerate(layout['pitReturns']):
    marker(pits, f'Pit {k + 1} recovery point', p, 'SPHERE', .5, note='Anyone below -6 m is put back here')
moves = group('Boost and ladder')
for key, label in (('boostStart', 'Boost start'), ('boostExit', 'Boost exit'), ('ladderStart', 'Ladder start'),
                   ('ladderExit', 'Ladder exit'), ('ladderAnchor', 'Ladder anchor')):
    marker(moves, label, layout[key], 'SPHERE', .4)
walls = group('Breakable walls')
for w in layout['walls']:
    f = w['footprintXZ']
    ob = marker(walls, f"Breakable wall {w['id']}", {'x': (f['x'] + f['z']) / 2, 'y': w['height'] / 2, 'z': (f['y'] + f['w']) / 2},
                'CUBE', 1.0, None, wall=w['id'], height=w['height'])
    ob.scale = ((f['z'] - f['x']) / 2, (f['w'] - f['y']) / 2, w['height'] / 2)
area = group('Play area')
a = layout['playArea']
ob = marker(area, 'Play area', {'x': (a['x'] + a['z']) / 2, 'y': 2.5, 'z': (a['y'] + a['w']) / 2}, 'CUBE', 1.0, None,
            note='Players are kept inside: x 10-115, south to the road guardrail, north to 66.4')
ob.scale = ((a['z'] - a['x']) / 2, (a['w'] - a['y']) / 2, 2.5)

# ------------------------------------------------------------------ light, sky and a view
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
bpy.context.scene.collection.objects.link(sun)
world = bpy.data.worlds.new('Sky')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.62, .78, .92, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .8
bpy.context.scene.world = world
cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
cam.location = (54.0, 44.0, 1.8)
cam.rotation_euler = (math.radians(86), 0, math.radians(69))
cam.data.lens = 24
cam.data.clip_end = 3000
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
bpy.context.scene.unit_settings.system = 'METRIC'

# Images a source FBX names but that exist nowhere (the game colours those models by material anyway).
for img in list(bpy.data.images):
    if img.source == 'FILE' and not img.packed_file and not Path(bpy.path.abspath(img.filepath)).exists():
        bpy.data.images.remove(img)
bpy.ops.file.pack_all()
print('LEVEL meshes', len(level), 'faces', sum(len(o.data.polygons) for o in level if o.name in bpy.data.objects))
OUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT), compress=True)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print('DELIVERY', OUT, 'objects', len(bpy.data.objects), 'meshes', len(meshes),
      'markers', len(markers.all_objects), 'materials', len(bpy.data.materials))

"""Read-only geometry checks against the shipped FBX, Unity import scales and colliders.

Run from any directory with Blender --background --factory-startup --python this_file.
Writes Docs/verification/ruin-scale.json; this is not a Unity/PhysX/NavMesh test.
"""
import hashlib
import json
import math
import re
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets/Game'
RES = ASSETS / 'Resources/RuinCourtyard'
OUT = ROOT / 'Docs/verification'
OUT.mkdir(parents=True, exist_ok=True)
layout = json.loads((RES / 'RuinLayout.json').read_text())
checks, inputs = [], []


def check(ok, name):
    assert ok, name
    checks.append(name)


def scalar(text, key):
    return float(re.search(r'^\s+' + key + r': ([-.0-9]+)$', text, re.M)[1])


def bounds(points):
    return ([min(p[i] for p in points) for i in range(3)],
            [max(p[i] for p in points) for i in range(3)])


def load_fbx(path):
    inputs.append(path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    return bpy.context.scene


avatar = (ASSETS / 'Prefabs/Avatar.prefab').read_text()
radius, height = scalar(avatar, 'm_Radius'), scalar(avatar, 'm_Height')
skin = scalar(avatar, 'm_SkinWidth')
step = scalar(avatar, 'm_StepOffset')
clear_radius = radius + skin
crew = {}
for body in ('Short', 'Medium', 'Tall'):
    path = ASSETS / f'Models/Crew_{body}.fbx'
    importer = Path(str(path) + '.meta')
    inputs.append(importer)
    scale = scalar(importer.read_text(), 'globalScale')
    scene = load_fbx(path)
    for ob in scene.objects:
        if ob.type == 'ARMATURE':
            ob.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    rows = []
    for ob in scene.objects:
        if ob.type == 'MESH' and (ob.name in ('Porter', 'Wrecker', 'Gloves') or '_Hat_' in ob.name):
            lo, hi = bounds([ob.matrix_world @ v.co * scale for v in ob.data.vertices])
            rows.append(dict(name=ob.name, min=lo, max=hi))
    crew[body] = dict(importScale=scale, tallestHatMetres=max(r['max'][2] for r in rows),
                      bodyHeightMetres=max(r['max'][2] for r in rows if r['name'] in ('Porter', 'Wrecker')),
                      restWidthMetres=max(r['max'][0]-r['min'][0] for r in rows if r['name'] in ('Porter', 'Wrecker', 'Gloves')))
    check(crew[body]['restWidthMetres'] < radius * 2, body + ' rest body fits controller width')
visible_height = max(row['tallestHatMetres'] for row in crew.values())

scene = load_fbx(RES / 'RuinCourtyard_Environment.fbx')
origin = scene.objects['Coordinate_Origin'].matrix_world.translation.copy()
east = (scene.objects['Coordinate_EastX'].matrix_world.translation - origin) / 5
north = (scene.objects['Coordinate_NorthZ'].matrix_world.translation - origin) / 5
up = east.cross(north).normalized()
check(abs(east.length-1) < .001 and abs(north.length-1) < .001 and abs(east.dot(north)) < .001,
      'FBX locator frame uses orthogonal metre units')


def to_map(point):
    delta = point-origin
    return Vector((delta.dot(east), delta.dot(north), delta.dot(up)))


def object_bounds(ob):
    return bounds([to_map(ob.matrix_world @ v.co) for v in ob.data.vertices])


ground = next(o for o in scene.objects if o.name.startswith('Ground_Surface'))
lo, hi = object_bounds(ground)
check(abs(lo[0]) < .01 and abs(hi[0]-124) < .01 and abs(hi[1]-69) < .01 and abs(lo[1]+18) < .01,
      '124 x 69 metre play area plus 18 metre south road apron')
inv = ground.matrix_world.inverted()
for i, pit in enumerate(layout['pits']):
    x0, z0, x1, z1 = [pit[k] for k in 'xyzw']
    for x in (x0+.15, (x0+x1)/2, x1-.15):
        for z in (z0+.15, (z0+z1)/2, z1-.15):
            p = origin+east*x+north*z+up
            hit = ground.ray_cast(inv @ p, inv.to_3x3() @ -up)[0]
            check(not hit, f'Pit {i+1} ground opening at {x:.2f},{z:.2f}')
check(len([o for o in scene.objects if o.name.startswith('PitWarning_')]) == 2, 'Exactly one warning sign per pit')
pit_floors = [o for o in scene.objects if o.type == 'MESH' and o.name.endswith('pit floor')]
check(len(pit_floors) == 2, 'Both pit floor meshes exist')
for ob in pit_floors:
    lo_floor, hi_floor = object_bounds(ob)
    check(abs(hi_floor[2]+3.5) < .15, ob.name+' is approximately 3.5 metres below ground')
for group in ('collectorSpawns', 'interrupterSpawns', 'smallSpots', 'mediumSpots', 'largeSpots'):
    for spot in layout[group]:
        p = spot['position']
        actual = to_map(scene.objects[spot['name']].matrix_world.translation)
        check((actual-Vector((p['x'], p['z'], p['y']))).length < .01, spot['name']+' locator matches Unity layout')

static = []
for block in layout['blockers']:
    if block['round']:
        c = block['circle']
        static.append((block['name'], (c['x']-c['z'], c['y']-c['z'], c['x']+c['z'], c['y']+c['z']), block['minY'], block['maxY']))
    else:
        static.append((block['name'], tuple(block['rect'][k] for k in 'xyzw'), block['minY'], block['maxY']))
for wall in layout['walls']:
    low = .57 if wall['id'] == 'R7' else 0
    static.append((wall['id'], tuple(wall['footprintXZ'][k] for k in 'xyzw'), low, low+wall['height']))


def capsule_clear(x, z, feet=0, obstacles=None):
    for name, (x0,z0,x1,z1), low, high in (static if obstacles is None else obstacles):
        if high <= feet+step or low >= feet+visible_height:
            continue
        distance = math.hypot(max(x0-x, 0, x-x1), max(z0-z, 0, z-z1))
        if distance < clear_radius-.001:
            return False, name
    return True, ''


def obb_overlaps_rect(cx, cz, hx, hz, yaw, rect):
    c,s = math.cos(yaw),math.sin(yaw)
    ux,uz = Vector((c,-s)),Vector((s,c))
    box = [Vector((cx,cz))+ux*x+uz*z for x in (-hx,hx) for z in (-hz,hz)]
    a,b,c,d = rect
    fixed = [Vector((x,z)) for x in (a,c) for z in (b,d)]
    for axis in (ux,uz,Vector((1,0)),Vector((0,1))):
        p,q = [v.dot(axis) for v in box],[v.dot(axis) for v in fixed]
        if max(p) <= min(q)+.002 or max(q) <= min(p)+.002:
            return False
    return True


cargo_sizes = {}
for label, group in [('Small','smallSpots'),('Medium','mediumSpots'),('Large','largeSpots')]:
    path = ASSETS / f'Prefabs/Cargo_{label}.prefab'
    inputs.append(path)
    match = re.search(r'm_Size: \{x: ([.0-9]+), y: ([.0-9]+), z: ([.0-9]+)\}', path.read_text())
    sx,sy,sz = map(float,match.groups())
    cargo_sizes[label] = [sx,sy,sz]
    for spot in layout[group]:
        p = spot['position']
        floor = p['y']-sy/2
        expected_floor = 2.5 if p['y'] > 2 else 0
        check(abs(floor-expected_floor) < .02, spot['name']+' rests on its intended floor')
        for name, rect, low, high in static:
            if high <= floor+.005 or low >= p['y']+sy/2:
                continue
            check(not obb_overlaps_rect(p['x'],p['z'],sx/2,sz/2,math.radians(spot['yaw']),rect),
                  spot['name']+' cargo spawn clearance vs '+name)
        for pit in layout['pits']:
            check(not obb_overlaps_rect(p['x'],p['z'],sx/2,sz/2,math.radians(spot['yaw']),tuple(pit[k] for k in 'xyzw')),
                  spot['name']+' outside pit opening')


for key in ('collectorSpawns', 'interrupterSpawns'):
    for spot in layout[key]:
        p = spot['position']
        clear, name = capsule_clear(p['x'], p['z'])
        check(clear, spot['name']+' character clearance '+name)
        for pit in layout['pits']:
            x0,z0,x1,z1 = [pit[k] for k in 'xyzw']
            check(math.hypot(max(x0-p['x'],0,p['x']-x1),max(z0-p['z'],0,p['z']-z1)) >= clear_radius,
                  spot['name']+' outside pit rim')
for key in ('boostExit', 'ladderExit'):
    p = layout[key]
    clear, name = capsule_clear(p['x'], p['z'], p['y'])
    check(clear, key+' body/head clearance '+name)
    margin = min(p['x']-56.4, 71.6-p['x'], p['z']-37.38, 44.62-p['z'])-clear_radius
    check(margin >= .1, key+' entire padded footprint on upper deck')

# Mesh bounds of the actual open doors; physical leaf bounds are checked below too.
scene = load_fbx(RES / 'RuinCourtyard_OperableDoors.fbx')
door_meshes = []
for ob in scene.objects:
    if ob.type == 'MESH':
        lo, hi = object_bounds(ob)
        door_meshes.append((ob.name, (lo[0],lo[1],hi[0],hi[1]),lo[2],hi[2]))
for door in layout['doors']:
    for leaf_name, hinge_name in zip(door['leaves'], door['hinges']):
        leaf, hinge = scene.objects[leaf_name], scene.objects[hinge_name]
        c = to_map(leaf.matrix_world.translation)
        h = to_map(hinge.matrix_world.translation)
        along = Vector((c.x-h.x,c.y-h.y)).normalized()
        # Runtime BoxCollider: width/2 along the leaf, .28 m thick.
        ex = abs(along.x)*door['width']/4+abs(along.y)*.14
        ez = abs(along.y)*door['width']/4+abs(along.x)*.14
        ey = door['height']*.44
        door_meshes.append((leaf_name+' runtime collider', (c.x-ex,c.y-ez,c.x+ex,c.y+ez),c.z-ey,c.z+ey))

large = (ASSETS / 'Prefabs/Cargo_Large.prefab').read_text()
size_match = re.search(r'm_Size: \{x: ([.0-9]+), y: ([.0-9]+), z: ([.0-9]+)\}', large)
box_x, box_y, box_z = map(float, size_match.groups())
grip = box_x/2+.55
formation = 2*(grip+clear_radius)
vaults = []
for spot, door in zip(layout['largeSpots'], layout['doors']):
    p = spot['position']
    check(abs(spot['yaw']+90) < .01, spot['name']+' long axis aligned with door')
    check(door['width'] > max(box_z, 2*clear_radius)+.2, door['id']+' width accommodates box and carriers')
    check(door['height'] > visible_height+.2, door['id']+' clears tallest hat')
    obstacles = static + door_meshes
    for i in range(71):
        z = p['z']-i*.1
        # At yaw -90, the box long axis and both carriers lie along map north/south.
        for name, (x0,z0,x1,z1), low, high in obstacles:
            if high <= p['y']-box_y/2+.005 or low >= p['y']+box_y/2:
                continue
            check(not (p['x']+box_z/2 > x0 and p['x']-box_z/2 < x1 and z+box_x/2 > z0 and z-box_x/2 < z1),
                  f"{spot['name']} box egress sample {i} vs {name}")
        for offset in (-grip, grip):
            clear, name = capsule_clear(p['x'], z+offset, obstacles=obstacles)
            check(clear, f"{spot['name']} carrier egress sample {i} vs {name}")
    # Conservative circular clearance around each initial formation (not a rotation feature).
    for name, (x0,z0,x1,z1), low, high in static:
        if high <= step or low >= visible_height:
            continue
        check(math.hypot(max(x0-p['x'],0,p['x']-x1),max(z0-p['z'],0,p['z']-z1)) >= formation/2,
              spot['name']+' padded formation clearance vs '+name)
    vaults.append(dict(id=door['id'], clearWidth=door['width'], clearHeight=door['height'],
                       tallestHatHeadroom=door['height']-visible_height, egressSamples=71))

# Import all delivered FBX categories and ensure every replicated visual state is present.
scene = load_fbx(RES / 'RuinCourtyard_Breakables.fbx')
for wall in layout['walls']:
    for state in ('Intact', 'Broken'):
        check(any(o.name.startswith(wall['id']+'_'+state) for o in scene.objects), wall['id']+' '+state+' mesh exists')
check(any(o.name.startswith('R7_Cracked') for o in scene.objects), 'R7 intermediate cracked mesh exists')
scene = load_fbx(RES / 'RuinCourtyard_Vines.fbx')
for prefix in ('Vine_Coil', 'Vine_SlowPatch', 'Vine_Ladder'):
    check(any(o.name.startswith(prefix) for o in scene.objects), prefix+' mesh exists')
cargo_meshes = {}
for label, expected in cargo_sizes.items():
    scene = load_fbx(ASSETS / f'Models/Cargo_{label}.fbx')
    lo_mesh,hi_mesh = bounds([ob.matrix_world @ v.co for ob in scene.objects if ob.type == 'MESH' for v in ob.data.vertices])
    size = [hi_mesh[i]-lo_mesh[i] for i in range(3)]
    collider_xyz = [expected[0],expected[2],expected[1]]
    outset = [max(0,hi_mesh[i]-collider_xyz[i]/2,-lo_mesh[i]-collider_xyz[i]/2) for i in range(3)]
    cargo_meshes[label] = dict(blenderXYZ=size,colliderUnityXYZ=expected,decorativeOutsetBlenderXYZ=outset)
    # Existing handles, corner guards and the face protrude beyond the physics body.
    check(max(outset) < .1, label+' decorative mesh protrudes less than 10 cm beyond its collider')
    if label == 'Large':
        check(grip-radius > max(abs(lo_mesh[0]),abs(hi_mesh[0])), 'Large chest handles clear the co-carriers')
        for door in layout['doors']:
            check(door['width'] > size[1]+.2, door['id']+' clears full large cargo visual including face/trim')
image = bpy.data.images.load(str(RES / 'RuinMinimap.png'))
check(abs(image.size[0]/image.size[1]-124/69) < .001, 'Minimap pixel aspect matches map metres')

inputs += [RES/'RuinLayout.json', ASSETS/'Prefabs/Avatar.prefab', ASSETS/'Prefabs/Cargo_Large.prefab', RES/'RuinMinimap.png']
summary = sorted(set(re.sub(r' sample \d+ vs .*', ' sampled clearances', re.sub(r' vs .*', '', c)) for c in checks))
report = dict(kind='Blender FBX and serialized source geometry checks', unityExecuted=False,
              controller=dict(height=height,radius=radius,skinWidth=skin,stepOffset=step), crew=crew,
              map=dict(width=124,depth=69,area=124*69,southRoadApron=18,terraceHeight=2.5),
              cargoMeshes=cargo_meshes,largeCargo=dict(size=[box_x,box_y,box_z],gripCentreOffset=grip,paddedFormationLength=formation),
              vaults=vaults, checksPassed=len(checks), checkGroups=summary,
              sha256={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs})
(OUT / 'ruin-scale.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
print('RUIN SCALE PASS:',len(checks),'geometry checks;',json.dumps(crew),'; formation metres:',formation)

"""Add two independent, rounded stone switchback stairs without modifying any V09 FBX.

Run with Blender --background --factory-startup --python-exit-code 1 --python this_file.
Blender X/Y are map X/Z, Blender Z is height. Exported frame locators let Unity map
that space explicitly. Collider and walking-path locators are the runtime contract.
"""
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Game/Resources/RuinCourtyard/RuinCourtyard_PitStairs.fbx'
REPORT = ROOT / 'Builds/Verification/pit-stairs-model.json'
PITS = [('West', (24.0, 34.0, 33.0, 45.0)), ('East', (89.75, 28.0, 97.0, 40.0))]
FLOOR, RISE, TREAD, WIDTH, GAP = -3.5, .25, .65, 1.8, .25
WALL_CORRIDOR, START_OFFSET, TURN_DEPTH = 1.6, 2.7, 3.2

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1


def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = .78
    return mat


STONE = material('PitStair_Stone', (.7137255, .6901961, .6039216))
CAP = material('PitStair_Cap', (.8745098, .8470588, .7176471))
SHADE = material('PitStair_Shade', (.6117647, .5843137, .5098039))
YELLOW = material('PitStair_Safety', (.8509804, .6627451, .1568627))


def marker(name, p):
    ob = bpy.data.objects.new(name, None)
    scene.collection.objects.link(ob)
    ob.location = (p[0], p[2], p[1])
    return ob


def box(name, rect, low, top, mat, bevel=.035):
    x0, z0, x1, z1 = rect
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0+x1)/2, (z0+z1)/2, (low+top)/2))
    ob = bpy.context.object
    ob.name = name
    ob.dimensions = (x1-x0, z1-z0, top-low)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    ob.data.materials.append(mat)
    mod = ob.modifiers.new('Soft toy stone corners', 'BEVEL')
    mod.width = min(bevel, (top-low)*.3)
    mod.segments = 4
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Keep a valid non-degenerate UV/tangent layout, outside the atlas leaf-lighting region.
    if ob.data.uv_layers.active:
        for loop in ob.data.uv_layers.active.data:
            loop.uv = loop.uv*.08+Vector((.07, .60))
    return ob


marker('PitStairs_Coordinate_Origin', (0, 0, 0))
marker('PitStairs_Coordinate_East', (5, 0, 0))
marker('PitStairs_Coordinate_North', (0, 0, 5))
marker('PitStairs_Coordinate_Up', (0, 5, 0))
reports = []
for name, pit in PITS:
    x0, z0, x1, z1 = pit
    left = x0+WALL_CORRIDOR
    right = left+WIDTH+GAP
    start = z0+START_OFFSET
    turn = start+7*TREAD
    a, b = left+WIDTH/2, right+WIDTH/2
    colliders = []
    meshes = []

    def slab(label, rect, top, floor=False):
        number = len(colliders)
        low = FLOOR-.1
        colliders.append(dict(name=label, rect=list(rect), bottom=low, top=top))
        marker(f'PitStairs_{name}_Box_{number:02}_Min', (rect[0], low, rect[1]))
        marker(f'PitStairs_{name}_Box_{number:02}_Max', (rect[2], top, rect[3]))
        # Masonry courses give the tall returning flight a readable solid stone structure.
        cap_low = max(low, top-.09)
        if cap_low > low+.015:
            courses = max(1, math.ceil((cap_low-low)/.48))
            for row in range(courses):
                y0 = low+(cap_low-low)*row/courses
                y1 = low+(cap_low-low)*(row+1)/courses-.008
                split = 2 if rect[2]-rect[0] > 2.5 else 1
                for col in range(split):
                    w = (rect[2]-rect[0])/split
                    r = (rect[0]+col*w+.006, rect[1]+.006, rect[0]+(col+1)*w-.006, rect[3]-.006)
                    meshes.append(box(f'{name} {label} masonry {row}.{col}', r, y0, y1,
                                      SHADE if (row+number)%4 == 0 else STONE))
        meshes.append(box(f'{name} {label} tread', rect, cap_low, top, CAP, .025))
        if not floor and number in (1, 7, 9, 15, 16):
            for side in (rect[0]+.16, rect[2]-.28):
                strip = (side, (rect[1]+rect[3])/2-.06, side+.12, (rect[1]+rect[3])/2+.06)
                meshes.append(box(f'{name} tread exit inlay', strip, top-.007, top+.003, YELLOW, .008))

    slab('bottom landing', (left, z0+.2, left+WIDTH, start), FLOOR, floor=True)
    for i in range(7):
        slab(f'lower flight {i+1}', (left, start+i*TREAD, left+WIDTH, start+(i+1)*TREAD), FLOOR+(i+1)*RISE)
    slab('turn landing', (left, turn, right+WIDTH, turn+TURN_DEPTH), FLOOR+7*RISE)
    for i in range(7):
        slab(f'upper flight {i+1}', (right, start+(6-i)*TREAD, right+WIDTH, start+(7-i)*TREAD), FLOOR+(8+i)*RISE)
    slab('ground exit landing', (right, z0, right+WIDTH, start), 0)

    path = [(a, FLOOR, start-.65)]
    path += [(a, FLOOR+(i+1)*RISE, start+(i+.5)*TREAD) for i in range(7)]
    path += [(a, FLOOR+7*RISE, turn+TURN_DEPTH/2), (b, FLOOR+7*RISE, turn+TURN_DEPTH/2)]
    path += [(b, FLOOR+(8+i)*RISE, start+(6.5-i)*TREAD) for i in range(7)]
    path += [(b, 0, start-.65), (b, 0, z0-.8)]
    for i, p in enumerate(path):
        marker(f'PitStairs_{name}_Path_{i:02}', p)

    assert len(colliders) == 17 and len(path) == 19
    assert all(x0 <= r['rect'][0] < r['rect'][2] <= x1 and z0 <= r['rect'][1] < r['rect'][3] <= z1 for r in colliders)
    assert max(abs(path[i+1][1]-path[i][1]) for i in range(len(path)-1)) <= RISE+.00001
    assert RISE <= .3 and WIDTH >= 1.8 and TREAD >= .65
    assert abs(right-left-(WIDTH+GAP)) < .00001
    assert turn+TURN_DEPTH < z1-.5
    assert x1-(right+WIDTH) >= 1.7
    bpy.ops.object.select_all(action='DESELECT')
    for ob in meshes: ob.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    bpy.context.object.name = f'PitStairs_{name}_Stonework'
    reports.append(dict(pit=name, bounds=list(pit), rise=RISE, tread=TREAD, clearWidth=WIDTH,
                        flightSeparation=WIDTH+GAP, wallAccessCorridor=WALL_CORRIDOR,
                        remainingOpenSide=x1-(right+WIDTH), turnLandingDepth=TURN_DEPTH,
                        noStackedFlights=True, colliders=colliders, path=path))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(OUT), use_selection=True, object_types={'MESH','EMPTY'},
                         use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                         bake_anim=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, path_mode='STRIP')
REPORT.parent.mkdir(parents=True, exist_ok=True)
triangle_count = 0
for ob in scene.objects:
    if ob.type == 'MESH':
        ob.data.calc_loop_triangles()
        triangle_count += len(ob.data.loop_triangles)
REPORT.write_text(json.dumps(dict(pits=reports, totalSteps=28, totalColliders=34,
                                 pathPointsPerPit=19, triangleCount=triangle_count),
                            indent=2)+'\n')
print('PIT STAIRS: 2 side-by-side switchbacks, 14 x .25 m rises each, .65 m treads, 1.8 m clear width.')

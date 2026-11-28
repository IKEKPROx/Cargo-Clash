"""One stair into each pit that splits in two: a central flight down from the south rim onto a landing that
spans the pit, then a flight down each side wall to the floor, with Maya balustrades (alfardas) on the open
edges. Nothing in the V09 FBX changes; make_ruin_maya.py keeps its pit vines off these flights (PIT_SPLIT).

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
FLOOR, RISE, TREAD = -3.5, .25, .5
CENTRE_TREAD = .6                   # deeper than a body is wide, so nobody perches on the next edge
LANDING = -1.0                      # the landing that spans the pit, 4 rises below the rim
# A stone threshold just outside the rim, a little proud of the ground, so the top step lands on stone and not
# on the rim's uneven edge (a two-person carry only steps up .3 m). The central rises share the extra height.
THRESHOLD_TOP, THRESHOLD_DEPTH = .1, 1.4
CENTRE_RISE = (THRESHOLD_TOP-LANDING)/4
CENTRE_W = 2.6                      # central flight, balustrades included
SIDE_W = {'West': 2.6, 'East': 2.3}  # each side flight, its balustrade included
ALFARDA_W, ALFARDA_H = .38, .45
LANDING_DEPTH = 4.45                # rim to the landing's north edge
# The two-person crate is carried with one person ahead of the other (the crate's yaw is -90), so the
# landing is deep enough for both on it and clear of both balustrades while they turn to the centre.

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
    mod.segments = 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Keep a valid non-degenerate UV/tangent layout, outside the atlas leaf-lighting region.
    if ob.data.uv_layers.active:
        for loop in ob.data.uv_layers.active.data:
            loop.uv = loop.uv*.08+Vector((.07, .60))
    return ob


def wedge(name, rect, low, top_south, top_north, mat):
    """A balustrade: a box whose top slopes from top_south (rect's low z) to top_north."""
    x0, z0, x1, z1 = rect
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0+x1)/2, (z0+z1)/2, .5))
    ob = bpy.context.object
    ob.name = name
    for v in ob.data.vertices:
        v.co.x = x0 if v.co.x < 0 else x1
        v.co.y = z0 if v.co.y < 0 else z1
        v.co.z = low if v.co.z < .5 else (top_south if v.co.y < (z0+z1)/2 else top_north)
    ob.location = (0, 0, 0)
    ob.data.materials.append(mat)
    mod = ob.modifiers.new('Soft stone corners', 'BEVEL')
    mod.width = .03
    mod.segments = 3
    bpy.ops.object.modifier_apply(modifier=mod.name)
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
    cx = (x0+x1)/2
    side_w = SIDE_W[name]
    landing_n = z0+LANDING_DEPTH
    flight_n = landing_n+9*TREAD           # north edge of the side flights' bottom tread
    colliders = []
    meshes = []

    def slab(label, rect, top, low=FLOOR-.1, visible=True):
        number = len(colliders)
        colliders.append(dict(name=label, rect=list(rect), bottom=low, top=top))
        marker(f'PitStairs_{name}_Box_{number:02}_Min', (rect[0], low, rect[1]))
        marker(f'PitStairs_{name}_Box_{number:02}_Max', (rect[2], top, rect[3]))
        if not visible:
            return
        cap_low = max(low, top-.09)
        if cap_low > low+.015:
            rows = max(1, math.ceil((cap_low-low)/.48))
            for row in range(rows):
                y0 = low+(cap_low-low)*row/rows
                y1 = low+(cap_low-low)*(row+1)/rows-.008
                split = max(1, round((rect[2]-rect[0])/1.4))
                stagger = .5 if row % 2 else 0
                edges = [rect[0]] + [rect[0]+(c+stagger)*(rect[2]-rect[0])/split for c in range(1, split)] + [rect[2]]
                for col in range(len(edges)-1):
                    r = (edges[col]+.006, rect[1]+.006, edges[col+1]-.006, rect[3]-.006)
                    meshes.append(box(f'{name} {label} masonry {row}.{col}', r, y0, y1,
                                      SHADE if (row+col+number) % 4 == 0 else STONE))
        meshes.append(box(f'{name} {label} tread', rect, cap_low, top, CAP, .025))

    def inlay(label, rect, top):
        meshes.append(box(f'{name} {label} safety inlay', rect, top-.007, top+.003, YELLOW, .008))

    # The landing spans the pit from the south wall, so no hole is left beside the central flight.
    slab('landing', (x0, z0, x1, landing_n), LANDING)
    inlay('landing edge', (x0+side_w+.1, landing_n-.16, x1-side_w-.1, landing_n-.06), LANDING)

    # Central flight down from the rim, three treads onto the landing, balustrades both sides.
    c0, c1 = cx-CENTRE_W/2, cx+CENTRE_W/2
    slab('rim threshold', (c0, z0-THRESHOLD_DEPTH, c1, z0), THRESHOLD_TOP, low=-.35)
    inlay('rim nosing', (c0+ALFARDA_W+.12, z0-.13, c1-ALFARDA_W-.12, z0-.05), THRESHOLD_TOP)
    for j in range(3):
        top = THRESHOLD_TOP-(j+1)*CENTRE_RISE
        slab(f'centre tread {j+1}', (c0+ALFARDA_W, z0+j*CENTRE_TREAD, c1-ALFARDA_W, z0+(j+1)*CENTRE_TREAD), top, low=LANDING-.02)
    for side, rx in (('west', c0), ('east', c1-ALFARDA_W)):
        rect = (rx, z0, rx+ALFARDA_W, z0+3*CENTRE_TREAD)
        meshes.append(wedge(f'{name} centre alfarda {side}', rect, LANDING-.02, THRESHOLD_TOP+ALFARDA_H, THRESHOLD_TOP-3*CENTRE_RISE+ALFARDA_H, STONE))
        # The wedge is the visible stone; these steps, never above it, are its collider.
        for j in range(3):
            slab(f'centre alfarda {side} {j+1}', (rx, z0+j*CENTRE_TREAD, rx+ALFARDA_W, z0+(j+1)*CENTRE_TREAD), THRESHOLD_TOP-(j+1)*CENTRE_RISE+ALFARDA_H, low=LANDING-.02, visible=False)

    # A flight down each side wall, the balustrade on its open (inner) edge, starting one tread down.
    for side in ('west', 'east'):
        outer, inner = (x0, x0+side_w) if side == 'west' else (x1, x1-side_w)
        wx0, wx1 = sorted((outer, inner-ALFARDA_W if side == 'west' else inner+ALFARDA_W))
        ax0, ax1 = sorted((inner, inner-ALFARDA_W if side == 'west' else inner+ALFARDA_W))
        for k in range(9):
            top = LANDING-(k+1)*RISE
            slab(f'{side} tread {k+1}', (wx0, landing_n+k*TREAD, wx1, landing_n+(k+1)*TREAD), top)
        inlay(f'{side} foot', (wx0+.12, flight_n-.14, wx1-.12, flight_n-.06), LANDING-9*RISE)
        rect = (ax0, landing_n+TREAD, ax1, flight_n)
        meshes.append(wedge(f'{name} {side} alfarda', rect, FLOOR-.02, LANDING-RISE+ALFARDA_H, LANDING-9*RISE+ALFARDA_H, STONE))
        for k in range(1, 9):
            slab(f'{side} alfarda {k+1}', (ax0, landing_n+k*TREAD, ax1, landing_n+(k+1)*TREAD),
                 LANDING-(k+1)*RISE+ALFARDA_H, visible=False)

    # The authored walk: up the west flight in the west pit and the east flight in the east one, then the
    # landing to the centre and the central flight to the ground south of the rim.
    use = 'west' if name == 'West' else 'east'
    a = x0+(side_w-ALFARDA_W)/2 if use == 'west' else x1-(side_w-ALFARDA_W)/2
    turn_z = z0+3.2
    path = [(a, FLOOR, flight_n+.55)]
    path += [(a, LANDING-(k+1)*RISE, landing_n+(k+.5)*TREAD) for k in range(8, -1, -1)]
    path += [(a, LANDING, turn_z), (cx, LANDING, turn_z)]
    path += [(cx, THRESHOLD_TOP-(j+1)*CENTRE_RISE, z0+(j+.5)*CENTRE_TREAD) for j in range(2, -1, -1)]
    path += [(cx, THRESHOLD_TOP, z0-.6), (cx, 0, z0-THRESHOLD_DEPTH-.6)]
    for i, p in enumerate(path):
        marker(f'PitStairs_{name}_Path_{i:02}', p)

    assert all(x0 <= r['rect'][0] < r['rect'][2] <= x1 and z0-THRESHOLD_DEPTH <= r['rect'][1] < r['rect'][3] <= z1 for r in colliders)
    assert max(abs(path[i+1][1]-path[i][1]) for i in range(len(path)-1)) <= max(RISE, CENTRE_RISE)+.00001
    assert CENTRE_RISE <= .28
    # Crate carriers stand .9 m ahead and behind the crate: on the landing they clear the central treads'
    # balustrades (south) and the side balustrades (north), and at the foot the north wall.
    assert turn_z-.9-.45 > z0+3*CENTRE_TREAD and turn_z+.9+.3 < landing_n and turn_z+.9+.45 < landing_n+TREAD
    assert path[0][2]+.9+.45 < z1
    assert x1-x0-2*side_w >= 2.6
    bpy.ops.object.select_all(action='DESELECT')
    for ob in meshes: ob.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    bpy.context.object.name = f'PitStairs_{name}_Stonework'
    reports.append(dict(pit=name, bounds=list(pit), rise=RISE, tread=TREAD, landing=LANDING,
                        landingNorth=landing_n, flightFoot=flight_n, floorBetweenFlights=x1-x0-2*side_w,
                        colliders=colliders, path=path))

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
REPORT.write_text(json.dumps(dict(pits=reports, triangleCount=triangle_count),
                            indent=2)+'\n')
print('PIT STAIRS: split stairs, rim -> landing -> both side walls, 14 x .25 m rises, .5 m treads.')

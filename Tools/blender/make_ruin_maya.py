"""Maya landmarks, extra ruins, the jungle backdrop and painted road textures for the Ruin Courtyard.

Run from the repository root with Blender 5.3:
  /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
      --python Tools/blender/make_ruin_maya.py

Writes into Assets/Game/Resources/RuinCourtyard/:
  RuinCourtyard_Maya.fbx  visible geometry in the shared V09 coordinate frame (Blender X east, Y north)
  RuinMaya.json           blockers for the six structures inside the play area, the road height
                          profile and the tyre-track paths, read by RuinCourtyard.Maya.cs
  RuinRoad.png            tileable asphalt with lane paint, cracks, patches and gravel shoulders
  RuinDecals.png          strips for tyre scrub, grass ruts and worn safety paint
  RuinMinimap.png         the V10 minimap with the inside structures drawn on (from Tools/blender/data)

The environment FBX is only read, to set everything on the existing ground. Nothing else is changed.
Stone uses the limestone tiles of RuinAtlas.png, so it matches the existing buildings. Terrain, foliage
and bark are vertex coloured; RuinCourtyard.Maya.cs switches those materials to vertex colour.
"""
import json
import math
import random
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT / 'Assets/Game/Resources/RuinCourtyard'

PLAY = (0.0, 0.0, 124.0, 69.0)
PLAY_AREA = (10.0, -6.2, 115.0, 66.4)     # where players can go now: x 10-115, south to the guardrail, north to 66.4
TERRAIN = (-42.0, -48.0, 166.0, 112.0)
JUNGLE = (-14.0, -26.0, 138.0, 84.0)      # the jungle front, well outside the cordon
CENTRE = Vector((62.0, 32.0))
ROAD_SOUTH, ROAD_NORTH, ROAD_MID = -15.3, -6.7, -11.0
ROAD_REACH = (-420.0, 540.0)
HELI_NORTH = (70.0, 102.0)                 # the helicopter arrives over this strip from the north
HELI_SOUTH = (24.0, 82.0)                  # and leaves south over this one
CELL = 3.0                                 # metres of stone per atlas tile repeat

# RuinAtlas.png tiles as (u, v, width, height).
LIMESTONE, CREAM, MOSSY, GREYSTONE = 0, 1, 2, 3
TILES = {
    LIMESTONE: (.75, .125, .125, .125),
    MOSSY: (.875, .125, .125, .125),
    GREYSTONE: (.75, 0.02, .125, .105),
    CREAM: (.875, 0.02, .125, .105),
}

rng = random.Random(4122)


def smoothstep(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


# --------------------------------------------------------------------------- terrain sampling

class Ground:
    """First-hit heights of the shipped environment, ignoring trees and rocks by taking the lowest of a ring."""

    def __init__(self):
        bpy.ops.import_scene.fbx(filepath=str(RES / 'RuinCourtyard_Environment.fbx'), use_anim=False)
        verts, polys = [], []
        for ob in list(bpy.data.objects):
            if ob.type != 'MESH' or ob.name.endswith('_Near') or 'rock stratum' in ob.name:
                continue
            me = ob.data
            co = np.zeros(len(me.vertices) * 3, dtype=np.float64)
            me.vertices.foreach_get('co', co)
            co = co.reshape(-1, 3)
            m = np.array(ob.matrix_world)
            co = co @ m[:3, :3].T + m[:3, 3]
            base = len(verts)
            verts.extend(map(tuple, co))
            for p in me.polygons:
                polys.append([base + i for i in p.vertices])
        self.bvh = BVHTree.FromPolygons(verts, polys)
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        for me in list(bpy.data.meshes):
            bpy.data.meshes.remove(me)
        self.cache = {}

    def first(self, x, y):
        hit = self.bvh.ray_cast(Vector((x, y, 80.0)), Vector((0, 0, -1)), 200.0)
        return None if hit[0] is None else hit[0].z

    def at(self, x, y, radius=1.2):
        x = min(max(x, TERRAIN[0] + .3), TERRAIN[2] - .3)
        y = min(max(y, TERRAIN[1] + .3), TERRAIN[3] - .3)
        key = (round(x, 1), round(y, 1), radius)
        if key in self.cache:
            return self.cache[key]
        hs = []
        for i in range(9):
            a = i * math.tau / 8
            r = 0 if i == 8 else radius
            h = self.first(x + math.cos(a) * r, y + math.sin(a) * r)
            if h is not None:
                hs.append(h)
        h = min(hs) if hs else 0.0
        self.cache[key] = h
        return h


def inside(rect, x, y, margin=0.0):
    return rect[0] - margin <= x <= rect[2] + margin and rect[1] - margin <= y <= rect[3] + margin


# --------------------------------------------------------------------------- mesh building

class Part:
    """One bmesh with UVs, a per-face stone style and per-corner colours."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.col = self.bm.loops.layers.float_color.new('Color')
        self.style = self.bm.faces.layers.int.new('style')

    def box(self, centre, size, yaw=0.0, inset=0.0, style=LIMESTONE, tilt=None):
        """An oriented box (frustum when inset > 0) standing on centre.z."""
        sx, sy, sz = size[0] * .5, size[1] * .5, size[2]
        bottom = [(-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)]
        top = [(x - math.copysign(inset, x), y - math.copysign(inset, y)) for x, y in bottom]
        m = Matrix.Translation(Vector(centre)) @ Matrix.Rotation(yaw, 4, 'Z')
        if tilt is not None:
            m = m @ tilt
        vb = [self.bm.verts.new(m @ Vector((x, y, 0))) for x, y in bottom]
        vt = [self.bm.verts.new(m @ Vector((x, y, sz))) for x, y in top]
        faces = [(vb[3], vb[2], vb[1], vb[0]), (vt[0], vt[1], vt[2], vt[3])]
        faces += [(vb[i], vb[(i + 1) % 4], vt[(i + 1) % 4], vt[i]) for i in range(4)]
        for f in faces:
            face = self.bm.faces.new(f)
            face[self.style] = style
        return vb + vt

    def blob(self, centre, radii, colour, subdiv=1, wobble=.22, seed=0):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=1.0)
        vs = res['verts']
        off = Vector((seed * 7.31, seed * 3.17, seed * 1.93))
        for v in vs:
            d = v.co.normalized()
            k = 1 + wobble * noise.noise(d * 1.7 + off)
            v.co = Vector(centre) + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * k
        faces = {f for v in vs for f in v.link_faces}
        shade = .82 + .18 * rng.random()
        c = (colour[0] * shade, colour[1] * shade, colour[2] * shade, 1.0)
        for f in faces:
            for loop in f.loops:
                # Darker underneath reads as self-shadowed foliage.
                under = .55 + .45 * smoothstep(-1, 1, (loop.vert.co.z - centre[2]) / max(radii[2], .1))
                loop[self.col] = (c[0] * under, c[1] * under, c[2] * under, 1.0)
                loop[self.uv].uv = (.5, .5)
        return vs

    def trunk(self, base, height, r0, r1, colour, lean=(0, 0)):
        m = Matrix.Translation(Vector(base) + Vector((0, 0, height * .5)))
        m = m @ Matrix.Rotation(lean[0], 4, 'X') @ Matrix.Rotation(lean[1], 4, 'Y')
        res = bmesh.ops.create_cone(self.bm, cap_ends=False, segments=6, radius1=r0, radius2=r1,
                                    depth=height, matrix=m)
        faces = {f for v in res['verts'] for f in v.link_faces}
        for f in faces:
            for loop in f.loops:
                loop[self.col] = colour
                loop[self.uv].uv = (.5, .5)
        top = m @ Vector((0, 0, height * .5))
        return top

    def finish_stone(self, bevel=.05):
        bm = self.bm
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type='OFFSET', segments=2,
                            profile=.5, affect='EDGES', clamp_overlap=True)
        lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
        hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
        # Cut on the tile grid, so each face fits inside one repeat of its atlas tile.
        for axis in range(3):
            k = math.floor(lo[axis] / CELL) + 1
            while k * CELL < hi[axis]:
                no = Vector((0, 0, 0))
                no[axis] = 1
                co = Vector((0, 0, 0))
                co[axis] = k * CELL
                bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                                       dist=1e-4, plane_co=co, plane_no=no)
                k += 1
        bm.normal_update()
        for f in bm.faces:
            n = f.normal
            ax = (abs(n.x), abs(n.y), abs(n.z))
            if ax[2] >= max(ax[0], ax[1]):
                pick = (0, 1)
            elif ax[0] >= ax[1]:
                pick = (1, 2)
            else:
                pick = (0, 2)
            c = f.calc_center_median()
            ou = math.floor(c[pick[0]] / CELL) * CELL
            ov = math.floor(c[pick[1]] / CELL) * CELL
            u0, v0, du, dv = TILES[f[self.style]]
            for loop in f.loops:
                p = loop.vert.co
                lu = min(max((p[pick[0]] - ou) / CELL, 0), 1)
                lv = min(max((p[pick[1]] - ov) / CELL, 0), 1)
                loop[self.uv].uv = (u0 + (.03 + lu * .94) * du, v0 + (.03 + lv * .94) * dv)
                loop[self.col] = (1, 1, 1, 1)

    def to_object(self, name, material):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for poly in me.polygons:
            poly.use_smooth = True
        if material == 'MayaStone':
            # Two-segment bevels turn 45 degrees per step: keep them soft, like the existing rounded stone.
            me.set_sharp_from_angle(angle=math.radians(50))
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        me.materials.append(get_material(material))
        return ob


MATERIALS = {}


def get_material(name):
    if name not in MATERIALS:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (1, 1, 1, 1)
        MATERIALS[name] = mat
    return MATERIALS[name]


LEAF_GREENS = [(.055, .12, .035), (.08, .15, .04), (.11, .17, .045), (.045, .1, .04), (.13, .16, .05)]
BARK = (.16, .12, .08, 1.0)


def bush(part, x, y, z, size):
    for i in range(rng.randint(1, 3)):
        r = size * (.55 + .5 * rng.random())
        part.blob((x + rng.uniform(-size, size) * .5, y + rng.uniform(-size, size) * .5, z + r * .45),
                  (r, r * (.8 + .4 * rng.random()), r * .7), rng.choice(LEAF_GREENS), seed=rng.random() * 50)


# --------------------------------------------------------------------------- vines

VINE_STEM = (.1, .14, .05, 1.0)
VINE_PARTS = []                     # [stems, leaves] while the courtyard is being built
# Each pit's split stair (make_ruin_pit_stairs.py): a central flight 2.6 m wide down from the south rim, a
# landing 1 m down from the south wall to 4.45 m in, then a flight down each side wall, 9 treads of .5 m.
PIT_SPLIT = dict(centre_w=2.6, landing=-1.0, landing_depth=4.45, treads=9, tread=.5, rise=.25,
                 side_w=(2.6, 2.3), threshold=(.1, 1.4), centre_rise=.275, centre_tread=.6)


def pit_stair_top(pit, x, y):
    """Height of the split stair's stone just inside the pit wall at (x, y), or None where the wall is clear."""
    x0, y0, x1, y1 = pit
    s = PIT_SPLIT
    if y < y0 + s['landing_depth']:
        return s['landing']
    foot = y0 + s['landing_depth'] + s['treads'] * s['tread']
    if y < foot + .3 and (x < x0 + .5 or x > x1 - .5):
        k = min(s['treads'] - 1, int((y - y0 - s['landing_depth']) / s['tread']))
        return s['landing'] - (k + 1) * s['rise']
    return None


def tube(part, points, radius, colour, sides=5, taper=.55):
    rings = []
    for i, p in enumerate(points):
        a, b = Vector(points[max(i - 1, 0)]), Vector(points[min(i + 1, len(points) - 1)])
        t = (b - a).normalized()
        ref = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
        u = t.cross(ref).normalized()
        v = t.cross(u).normalized()
        r = radius * (1 - taper * i / max(1, len(points) - 1))
        rings.append([part.bm.verts.new(Vector(p) + (u * math.cos(k * math.tau / sides) + v * math.sin(k * math.tau / sides)) * r)
                      for k in range(sides)])
    for k in range(len(rings) - 1):
        for j in range(sides):
            f = part.bm.faces.new((rings[k][j], rings[k][(j + 1) % sides], rings[k + 1][(j + 1) % sides], rings[k + 1][j]))
            for loop in f.loops:
                loop[part.col] = colour
                loop[part.uv].uv = (.5, .5)


def vine(points, radius=.035, leafiness=.6, leaf_size=(.09, .16)):
    """A stem with leaves along a path, wavering a little."""
    if not VINE_PARTS:
        return
    stems, leaves = VINE_PARTS
    pts = []
    for i in range(len(points) - 1):
        a, b = Vector(points[i]), Vector(points[i + 1])
        n = max(1, int((b - a).length / .3))
        for k in range(n):
            q = a + (b - a) * (k / n)
            q += Vector((noise.noise(q * 1.3) * .06, noise.noise(q * 1.3 + Vector((5, 0, 0))) * .06, 0))
            pts.append(q)
    pts.append(Vector(points[-1]))
    tube(stems, pts, radius, VINE_STEM)
    for q in pts:
        for _ in range(2 if leafiness > 1 else 1):
            if rng.random() >= min(leafiness, .95):
                continue
            size = rng.uniform(*leaf_size)
            off = Vector((rng.uniform(-.1, .1), rng.uniform(-.1, .1), rng.uniform(-.05, .05)))
            leaves.blob(q + off, (size, size * .7, size * .32), rng.choice(LEAF_GREENS), subdiv=1, wobble=.1,
                        seed=rng.random() * 99)


def drape_pyramid(cx, cy, z0, half_x, half_y, tiers, tier_h, shrink, stair_side, count):
    """Vines from the upper ledges down the battered faces, tier by tier, never over the stair."""
    stair = Matrix.Rotation(stair_side, 3, 'Z') @ Vector((0, -1, 0))
    sides = [n for n in (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((-1, 0, 0))) if n.dot(stair) < .5]
    for _ in range(count):
        n = rng.choice(sides)
        lat = Vector((-n.y, n.x, 0))
        normal_half, lateral_half = (half_y, half_x) if abs(n.y) > .5 else (half_x, half_y)
        top = rng.randint(max(0, tiers - 2), tiers - 1)
        bottom = 0 if rng.random() < .55 else rng.randint(0, top)
        t = rng.uniform(-.8, .8) * (lateral_half - top * shrink)
        pts = []
        for j in range(top, bottom - 1, -1):
            d = normal_half - j * shrink
            zt = z0 + (j + 1) * tier_h
            w = t + rng.uniform(-.15, .15)
            base = Vector((cx, cy, 0)) + lat * w
            pts.append(base + n * (d - min(.35, shrink * .5)) + Vector((0, 0, zt + .03)))
            pts.append(base + n * (d - tier_h * .05 + .05) + Vector((0, 0, zt - .04)))
            pts.append(base + n * (d + .06) + Vector((0, 0, z0 + j * tier_h + .08)))
        vine(pts)


def pit_vines(pits):
    """Untidy clumps of vine creeping over the pit rims and hanging down the walls, in place of the neat rim
    plants: clumps at random places, each vine wandering over the ground before it drops, some along the rim."""
    for k, (x0, y0, x1, y1) in enumerate(pits):
        perimeter = 2 * ((x1 - x0) + (y1 - y0))
        for _ in range(int(perimeter / 3.2)):
            # A random point on the rim and the outward direction there.
            d = rng.uniform(0, perimeter)
            w, h = x1 - x0, y1 - y0
            if d < w:
                rim, out = Vector((x0 + d, y0, 0)), Vector((0, -1, 0))
            elif d < w + h:
                rim, out = Vector((x1, y0 + d - w, 0)), Vector((1, 0, 0))
            elif d < 2 * w + h:
                rim, out = Vector((x1 - (d - w - h), y1, 0)), Vector((0, 1, 0))
            else:
                rim, out = Vector((x0, y1 - (d - 2 * w - h), 0)), Vector((-1, 0, 0))
            along = Vector((-out.y, out.x, 0))
            centre = (x0 + x1) / 2
            if out.y < -.5 and abs(rim.x - centre) < PIT_SPLIT['centre_w'] / 2 + 1.4:
                continue
            root = rim + out * rng.uniform(.8, 2.6) + along * rng.uniform(-1.0, 1.0)
            for _ in range(rng.randint(2, 4)):
                # Wander from the root to a point on the rim, then over it and down.
                edge = rim + along * rng.uniform(-1.2, 1.2)
                path = [root + Vector((rng.uniform(-.3, .3), rng.uniform(-.3, .3), .04))]
                for t in (.35, .7):
                    q = root.lerp(edge, t) + along * rng.uniform(-.6, .6) + out * rng.uniform(-.2, .3)
                    path.append(q + Vector((0, 0, .04)))
                depth = rng.uniform(.8, 3.3)
                # Never down into the stair: stop short of the landing or the tread below.
                inner = edge - out * .3
                top = pit_stair_top((x0, y0, x1, y1), inner.x, inner.y)
                if top is not None:
                    depth = min(depth, -top - .12)
                path += [edge + out * .12 + Vector((0, 0, .05)), edge - out * .05 + Vector((0, 0, -.15)),
                         edge - out * .1 + along * rng.uniform(-.3, .3) + Vector((0, 0, -depth * .5)),
                         edge - out * .08 + along * rng.uniform(-.4, .4) + Vector((0, 0, -depth))]
                vine(path, radius=rng.uniform(.022, .045), leafiness=1.4, leaf_size=(.1, .22))
            if rng.random() < .45:
                # A runner along the rim.
                start = rim + out * rng.uniform(.1, .4)
                run = along * rng.uniform(1.5, 3.5) * rng.choice((-1, 1))
                vine([start + Vector((0, 0, .04)), start + run * .5 + out * rng.uniform(-.1, .3) + Vector((0, 0, .05)),
                      start + run + Vector((0, 0, .04))], radius=.025, leafiness=1.2, leaf_size=(.08, .18))
        for side, rect in (('W', (x0 - 2.2, y0 - 2.2, x0 + .3, y1 + 2.2)), ('E', (x1 - .3, y0 - 2.2, x1 + 2.2, y1 + 2.2)),
                           ('S', (x0 - 2.2, y0 - 2.2, x1 + 2.2, y0 + .3)), ('N', (x0 - 2.2, y1 - .3, x1 + 2.2, y1 + 2.2))):
            clearing(f'Pit {k + 1} rim plants {side}', *rect, foliage=True, margin=0)


# --------------------------------------------------------------------------- architecture

def stair_flight(stone, cx, cy, face, yaw, run_start, run_end, z0, top_z, width, steps, base=-.2):
    """A flight of steps with a stepped balustrade on each side, running from run_start to run_end in front of
    (cx, cy) along face. Each step is exactly one tread deep and runs a little into the balustrades, and the
    balustrades are all one stone, so no two visible faces lie in the same plane: overlapping steps of two
    styles used to show as a patchy, flickering side."""
    tread = (run_start - run_end) / steps
    rise = (top_z - z0) / steps
    for s in range(steps):
        d = run_start - (s + .5) * tread
        p = face @ Vector((0, -d, 0))
        stone.box((cx + p.x, cy + p.y, z0 + base), (width + .2, tread + .004, (s + 1) * rise - base), yaw=yaw,
                  style=CREAM if s % 2 else LIMESTONE)
        for side in (-1, 1):
            q = face @ Vector((side * (width * .5 + .45), -d, 0))
            stone.box((cx + q.x, cy + q.y, z0 + base), (.9, tread + .004, (s + 1) * rise - base + .45), yaw=yaw,
                      style=LIMESTONE)


def stepped_pyramid(stone, leaf, cx, cy, z0, half_x, half_y, tiers, tier_h, shrink, stair_side,
                    stair_w, temple=None, comb=None, crumble=.0, overgrowth=.4, skirt=2.5,
                    climb=None, slope=None, temple_shift=0.0):
    """A Maya stepped pyramid: battered tiers with a moulding, one stair, a temple and optional roof comb.

    stair_side is the direction of the stair as an angle; 0 faces -Y (south), pi/2 faces +X, and so on.
    slope (degrees) lays the stair out at that pitch. climb names the structure and adds walkable
    collision for it: a stacked box per tier and a ramp under the stair.
    """
    face = Matrix.Rotation(stair_side, 4, 'Z')
    stone.box((cx, cy, z0 - skirt), (half_x * 2 + .4, half_y * 2 + .4, skirt + .02), style=MOSSY)
    for i in range(tiers):
        hx, hy = half_x - i * shrink, half_y - i * shrink
        z = z0 + i * tier_h
        lower = MOSSY if i < 2 else LIMESTONE
        stone.box((cx, cy, z), (hx * 2, hy * 2, tier_h * .82), inset=tier_h * .12, style=lower)
        stone.box((cx, cy, z + tier_h * .82), (hx * 2 - tier_h * .1, hy * 2 - tier_h * .1, tier_h * .18),
                  style=CREAM)
        if crumble > 0:
            for corner in range(4):
                if rng.random() < crumble:
                    sx = 1 if corner in (1, 2) else -1
                    sy = 1 if corner >= 2 else -1
                    s = rng.uniform(.6, 1.4)
                    stone.box((cx + sx * (hx + .3), cy + sy * (hy + .3), z), (s, s * .8, s * .55),
                              yaw=rng.random() * 3, style=MOSSY)
        if overgrowth > 0:
            ledge_x, ledge_y = hx - shrink * .5, hy - shrink * .5
            for _ in range(int(overgrowth * (hx + hy) * .35)):
                t = rng.random() * 4
                side = int(t)
                f = t - side
                px = [(-1 + 2 * f), 1, (1 - 2 * f), -1][side] * ledge_x
                py = [-1, (-1 + 2 * f), 1, (1 - 2 * f)][side] * ledge_y
                local = face.inverted() @ Vector((px, py, 0))
                if abs(local.x) < stair_w * .7 and local.y < 0:
                    continue
                bush(leaf, cx + px, cy + py, z + tier_h, rng.uniform(.6, 1.3))
    top_z = z0 + tiers * tier_h
    top_hx, top_hy = half_x - (tiers - 1) * shrink, half_y - (tiers - 1) * shrink
    # The stair runs from the ground in front of the base to the top platform.
    front = half_y if abs(math.sin(stair_side)) < .5 else half_x
    top_front = top_hy if abs(math.sin(stair_side)) < .5 else top_hx
    run_end = top_front
    run_start = run_end + (top_z - z0) / math.tan(math.radians(slope)) if slope else front + (top_z - z0) * .38
    if climb:
        clearing(climb, cx - half_x, cy - half_y, cx + half_x, cy + half_y)
        drape_pyramid(cx, cy, z0, half_x, half_y, tiers, tier_h, shrink, stair_side,
                      max(3, int((half_x + half_y) * tiers * .22)))
        for i in range(tiers):
            hx, hy = half_x - i * shrink, half_y - i * shrink
            block(f'{climb} tier {i + 1}', cx - hx, cy - hy, cx + hx, cy + hy, z0 + (i + 1) * tier_h)
        tread = (run_start - run_end) / max(4, int((top_z - z0) / .42))
        foot = face @ Vector((0, -(run_start + tread * .5), 0))
        top = face @ Vector((0, -run_end, 0))   # flush with the summit edge, so there is no lip
        clearing(f'{climb} ramp', min(cx + foot.x, cx + top.x) - stair_w, min(cy + foot.y, cy + top.y) - stair_w,
                 max(cx + foot.x, cx + top.x) + stair_w, max(cy + foot.y, cy + top.y) + stair_w)
        RAMPS.append({'name': f'{climb} ramp', 'width': round(stair_w + 1.6, 3),
                      'foot': {'x': round(cx + foot.x, 3), 'y': round(z0, 3), 'z': round(cy + foot.y, 3)},
                      'top': {'x': round(cx + top.x, 3), 'y': round(top_z + .03, 3), 'z': round(cy + top.y, 3)}})
    # Solid down to the ground, so no step floats over the battered tiers.
    stair_flight(stone, cx, cy, face, stair_side, run_start, run_end, z0, top_z, stair_w,
                 max(4, int((top_z - z0) / .42)))
    if temple:
        tw, td, th = temple
        local = Matrix.Translation((cx, cy, top_z)) @ face @ Matrix.Translation((0, temple_shift, 0))
        # Back and side walls, a front with three doorways, a corbelled roof.
        def at(x, y, z):
            return local @ Vector((x, y, z))
        stone.box(at(0, td * .5 - .35, 0), (tw, .7, th), yaw=stair_side, style=CREAM)
        for sx in (-1, 1):
            stone.box(at(sx * (tw * .5 - .35), 0, 0), (.7, td, th), yaw=stair_side, style=CREAM)
        pier = tw / 7
        for k in (-3, -1, 1, 3):
            stone.box(at(k * pier, -td * .5 + .35, 0), (pier, .7, th * .72), yaw=stair_side, style=CREAM)
        stone.box(at(0, -td * .5 + .35, th * .72), (tw, .7, th * .28), yaw=stair_side, style=CREAM)
        stone.box(at(0, 0, th), (tw + .5, td + .5, .45), yaw=stair_side, style=LIMESTONE)
        stone.box(at(0, 0, th + .45), (tw - .3, td - .3, th * .35), inset=th * .12, yaw=stair_side, style=LIMESTONE)
        stone.box(at(0, -td * .5 - .1, th * .45), (tw * .6, .25, th * .22), yaw=stair_side, style=MOSSY)
        roof_top = th + .45 + th * .35
        if comb:
            cw, ch = comb
            for row in range(4):
                z = roof_top + row * ch / 4
                stone.box(at(-cw * .3, .3, z), (cw * .35, 1.1 - row * .15, ch / 4 * .7), yaw=stair_side, style=CREAM)
                stone.box(at(cw * .3, .3, z), (cw * .35, 1.1 - row * .15, ch / 4 * .7), yaw=stair_side, style=CREAM)
                stone.box(at(0, .3, z + ch / 4 * .55), (cw * (1 - row * .12), 1.0 - row * .15, ch / 4 * .45),
                          yaw=stair_side, style=LIMESTONE)
        for _ in range(3):
            p = at(rng.uniform(-tw, tw) * .4, rng.uniform(-td, td) * .4, roof_top)
            bush(leaf, p.x, p.y, p.z - .2, rng.uniform(.4, .8))
        # Vines down both side walls and the back, from the roof's edge.
        out = (face @ Vector((1, 0, 0))).normalized()
        for sx in (-1, 1):
            for _ in range(2):
                y = rng.uniform(-td * .35, td * .35)
                top = at(sx * (tw * .5 + .25), y, th + .45)
                n = out * sx
                drop = rng.uniform(.55, .95) * th
                vine([top - n * .3 + Vector((0, 0, .05)), top + n * .06, top + n * .08 + Vector((0, 0, -drop * .5)),
                      top + n * .06 + Vector((0, 0, -drop))], radius=.03, leafiness=1.2, leaf_size=(.08, .16))
    return top_z


def stela(stone, x, y, z0, yaw=0.0, h=2.6):
    stone.box((x, y, z0 - .3), (1.3, .9, .52), yaw=yaw, style=MOSSY)
    stone.box((x, y, z0 + .2), (.95, .42, h), inset=.05, yaw=yaw, style=CREAM)
    stone.box((x, y, z0 + .2 + h), (.82, .36, .22), inset=.08, yaw=yaw, style=CREAM)
    # Shallow carved panels on both faces.
    m = Matrix.Rotation(yaw, 4, 'Z')
    for side in (-1, 1):
        for row in range(3):
            o = m @ Vector((0, side * .23, 0))
            stone.box((x + o.x, y + o.y, z0 + .55 + row * .7), (.62, .06, .5), yaw=yaw, style=LIMESTONE)


def altar(stone, x, y, z0, r=.8):
    for k in range(8):
        a = k * math.tau / 8
        stone.box((x + math.cos(a) * r * .55, y + math.sin(a) * r * .55, z0 - .2), (r * .75, r * .75, .85),
                  yaw=a, style=MOSSY)
    stone.box((x, y, z0 + .62), (r * 1.6, r * 1.6, .18), yaw=.39, inset=.06, style=CREAM)


def wall_fragment(stone, leaf, x, y, z0, length, yaw, h=2.2):
    m = Matrix.Rotation(yaw, 4, 'Z')
    n = max(2, int(length / 1.6))
    for i in range(n):
        o = m @ Vector(((i - (n - 1) * .5) * length / n, 0, 0))
        # Uneven, but never below the blocker's height h: no invisible wall above a low stone.
        hh = h * (1 + .25 * rng.random()) if i in (0, n - 1) else h * (1 + .08 * rng.random())
        stone.box((x + o.x, y + o.y, z0 - .3), (length / n + .02, .9, hh + .3), yaw=yaw,
                  style=MOSSY if rng.random() < .35 else LIMESTONE)
    bush(leaf, x, y, z0 + h * .8, .9)


# --------------------------------------------------------------------------- the layout

BLOCKERS = []
RAMPS = []
CLEARINGS = []


def clearing(name, x0, y0, x1, y1, foliage=False, margin=.6):
    """Ground the runtime strips of the shipped trees, rocks and plants: under a structure, or (foliage only)
    the over-neat plants along the pit rims."""
    CLEARINGS.append({'name': name, 'rect': {'x': round(x0 - margin, 3), 'y': round(y0 - margin, 3),
                                             'z': round(x1 + margin, 3), 'w': round(y1 + margin, 3)},
                      'foliageOnly': foliage})


def block(name, x0, y0, x1, y1, h):
    BLOCKERS.append({'name': name, 'rect': {'x': round(x0, 3), 'y': round(y0, 3), 'z': round(x1, 3), 'w': round(y1, 3)},
                     'circle': {'x': 0, 'y': 0, 'z': 0}, 'minY': 0.0, 'maxY': round(h, 3), 'round': False})


def interior(stone, leaf):
    """Six structures inside the play area, on open grass away from routes, cargo, spawns and vines.

    The four stepped ones can be climbed: each stair is a 33 degree ramp, gentle enough for the character
    controller, and every tier top and summit is solid underfoot.
    """
    # South-west: a three-tier shrine with an open summit, ramp facing east.
    # The play area was drawn in to x 10-115 and z <= 66.4 (PLAY_AREA); the structures that stood in the strips
    # given up moved in with it.
    top = stepped_pyramid(stone, leaf, 14.5, 5.0, 0.0, 3.5, 3.5, 3, 1.3, .7, math.pi * .5, 1.8,
                          overgrowth=.3, skirt=.3, climb='Maya shrine SW', slope=33)
    altar(stone, 14.5, 5.0, top, r=.5)
    block('Maya shrine SW altar', 13.9, 4.4, 15.1, 5.6, top + .85)
    # West: two stelae and an altar.
    stela(stone, 13.1, 33.5, 0.0, yaw=math.pi * .5)
    stela(stone, 13.1, 37.6, 0.0, yaw=math.pi * .5, h=2.2)
    altar(stone, 15.4, 35.5, 0.0)
    block('Maya stela W1', 12.5, 32.8, 13.7, 34.2, 3.0)
    block('Maya stela W2', 12.5, 36.9, 13.7, 38.3, 2.6)
    block('Maya altar W', 14.5, 34.6, 16.3, 36.4, .85)
    clearing('Maya stelae W', 12.5, 32.8, 16.3, 38.3)
    # North-west: a four-tier pyramid, ramp facing south.
    stepped_pyramid(stone, leaf, 17.0, 58.5, 0.0, 4.5, 4.0, 4, 1.35, .75, 0.0, 1.8,
                    overgrowth=.5, skirt=.3, climb='Maya pyramid NW', slope=33)
    # East: a temple platform, ramp facing south, the three-door shrine set back to leave a summit terrace.
    top = stepped_pyramid(stone, leaf, 107.0, 52.0, 0.0, 4.0, 3.5, 3, 1.5, .5, 0.0, 2.0,
                          temple=(3.6, 2.4, 2.4), overgrowth=.3, skirt=.3, climb='Maya temple E', slope=33,
                          temple_shift=1.2)
    block('Maya temple E shrine', 107.0 - 1.8, 52.0 + 1.2 - 1.2, 107.0 + 1.8, 52.0 + 1.2 + 1.2, top + 3.4)
    # South-east: a low plaza with three stelae.
    stone.box((110.0, 6.0, -.3), (7.0, 4.5, .9), inset=.08, style=MOSSY)
    for k, x in enumerate((107.8, 110.0, 112.2)):
        stela(stone, x, 6.0 + (.8 if k == 1 else 0), .6, h=2.4 if k == 1 else 2.0)
    altar(stone, 110.0, 4.6, .6, r=.6)
    block('Maya plaza SE', 106.5, 3.75, 113.5, 8.25, .6)
    clearing('Maya plaza SE', 106.5, 3.75, 113.5, 8.25)
    for k, x in enumerate((107.8, 110.0, 112.2)):
        y = 6.0 + (.8 if k == 1 else 0)
        block(f'Maya stela SE{k + 1}', x - .65, y - .45, x + .65, y + .45, 3.4)
    # North-east: a small shrine with an open summit, ramp facing west (east is the edge, south the temple).
    stepped_pyramid(stone, leaf, 109.5, 59.5, 0.0, 2.75, 2.75, 3, 1.2, .5, -math.pi * .5, 1.6,
                    overgrowth=.3, skirt=.3, climb='Maya shrine NE', slope=33)
    palace(stone, leaf)
    terrace_stair(stone, leaf)
    corner_walls(stone, leaf)
    roadside_props()


SOLIDS = []
WALLS = []


def solid(name, x, y, z0, sx, sy, sz, yaw_radians=0.0):
    """An oriented blocker in Unity space (yaw clockwise from above) for an existing prop."""
    SOLIDS.append({'name': name, 'centre': {'x': round(x, 3), 'y': round(z0 + sz * .5, 3), 'z': round(y, 3)},
                   'size': {'x': round(sx, 3), 'y': round(sz, 3), 'z': round(sy, 3)},
                   'yaw': round(-math.degrees(yaw_radians), 2)})


def roadside_props():
    """Collision for the barricades, cones and the loose axle that now stand inside the widened play area."""
    for x in (4, 13, 22, 34.5, 41, 47.5, 54, 73, 79.5, 86, 91, 100, 109, 118):
        solid(f'Roadside barricade {x}', x, -2.02, 0, 3.16, .57, 1.12)
    solid('Knocked barricade W', 58.1, -2.48, 0, 3.16, .57, 1.12, -.36)
    solid('Knocked barricade E', 68.25, -2.49, 0, 3.16, .57, 1.12, .43)
    solid('Loose wheel axle', 69.3, -4.33, 0, 2.2, 1.9, .74)
    for x, y in ((57.1, -3.4), (68.4, -3.55), (70.6, -.45), (53.4, 10.05), (94.5, 9.1)):
        solid(f'Traffic cone {x}', x, y, 0, .5, .5, .8)


def corner_walls(stone, leaf):
    """Low ruined walls close the two south corners between the cordon and the road guardrail."""
    for x in (PLAY_AREA[0] - .45, PLAY_AREA[2] + .45):
        wall_fragment(stone, leaf, x, -2.85, 0.0, 5.7, math.pi * .5, h=1.4)


def palace(stone, leaf):
    """A colonnaded palace front to the truck's right, on a platform reached by a short stair."""
    cx, cy, w, d, ph = 96.5, 12.6, 10.0, 6.0, .8
    stone.box((cx, cy, -.3), (w, d, ph + .3), inset=.06, style=MOSSY)
    stone.box((cx, cy, ph - .12), (w - .3, d - .3, .14), style=CREAM)
    # Stair down the south face.
    run = ph / math.tan(math.radians(30))
    for s in range(2):
        # Each step its own half of the run, running into the cheek walls, so their sides never show.
        stone.box((cx, cy - d * .5 - run * (1 - s * .5) + run * .25, -.2), (3.3, run * .5 + .004, (s + 1) * ph * .5 + .2),
                  style=CREAM if s else LIMESTONE)
    for side in (-1, 1):
        stone.box((cx + side * 1.85, cy - d * .5 - run * .5, -.2), (.6, run, ph + .35), style=LIMESTONE)
    RAMPS.append({'name': 'Maya palace stair ramp', 'width': 3.4,
                  'foot': {'x': cx, 'y': 0.0, 'z': round(cy - d * .5 - run - .25, 3)},
                  'top': {'x': cx, 'y': round(ph + .03, 3), 'z': round(cy - d * .5, 3)}})
    # A back wall of niches and doorways, low side walls and a front colonnade with lintels.
    back = cy + d * .5 - .4
    # The back wall runs the full width; the side walls stop against its front face, a little inside its ends,
    # and the four niche piers stand clear of them, so no two walls share a face or poke through each other.
    stone.box((cx, back + .15, ph), (w, .5, 2.6), style=LIMESTONE)
    for k in (-3.0, -1.0, 1.0, 3.0):
        stone.box((cx + k, back - .2, ph), (.9, .35, 2.6), style=CREAM)
    stone.box((cx, back - .2, ph + 2.6), (w + .12, .9, .5), inset=.05, style=CREAM)
    stone.box((cx, back - .1, ph + 3.1), (w - 1.0, .7, .45), inset=.12, style=LIMESTONE)
    side_front, side_back = cy - 1.0, back - .1
    for side in (-1, 1):
        stone.box((cx + side * (w * .5 - .43), (side_front + side_back) * .5, ph), (.8, side_back - side_front, 1.6),
                  style=MOSSY)
        stone.box((cx + side * (w * .5 - .43), (side_front + side_back) * .5, ph + 1.6),
                  (.9, side_back - side_front + .06, .14), style=CREAM)
    columns = (-4.1, -2.5, 2.5, 4.1)
    for k in columns:
        stone.box((cx + k, cy - d * .5 + .7, ph), (.7, .7, 2.5), inset=.04, style=LIMESTONE)
        stone.box((cx + k, cy - d * .5 + .7, ph + 2.5), (.9, .9, .25), style=CREAM)
    for k0, k1 in ((-4.1, -2.5), (2.5, 4.1)):
        stone.box((cx + (k0 + k1) * .5, cy - d * .5 + .7, ph + 2.75), (k1 - k0 + .9, .8, .4), style=CREAM)
    # Vines round two columns and hanging from both lintels.
    for k in (columns[0], columns[3]):
        x, y = cx + k, cy - d * .5 + .7
        # Clear of the column's corners (.49 m out) and the leaves round the stem.
        vine([Vector((x + .72 * math.cos(a), y + .72 * math.sin(a), ph + .1 + a * .28)) for a in
              [i * .55 for i in range(17)]], leaf_size=(.07, .12))
    for k0, k1 in ((-4.1, -2.5), (2.5, 4.1)):
        x = cx + (k0 + k1) * .5 + rng.uniform(-.3, .3)
        y = cy - d * .5 + .7 - .65            # a hand's width in front of the lintel, leaves and all
        vine([Vector((x, y + .65, ph + 3.17)), Vector((x, y + .1, ph + 3.12)), Vector((x, y, ph + 3.0)),
              Vector((x + .1, y - .03, ph + 2.2)), Vector((x, y - .05, ph + 1.5))], leaf_size=(.07, .12))
    # One fallen lintel on the platform, and plants in the corners.
    stone.box((cx - 1.0, cy + .2, ph), (2.2, .7, .4), yaw=.35, style=MOSSY)
    for x, y in ((cx - w * .5 + .6, back - .6), (cx + w * .5 - .6, back - .6)):
        bush(leaf, x, y, ph, .7)
    clearing('Maya palace', cx - w * .5, cy - d * .5 - run - .3, cx + w * .5, cy + d * .5)
    block('Maya palace platform', cx - w * .5, cy - d * .5, cx + w * .5, cy + d * .5, ph)
    block('Maya palace back wall', cx - w * .5, back - .4, cx + w * .5, cy + d * .5, ph + 3.55)
    for side in (-1, 1):
        x0 = cx + side * (w * .5 - .4)
        block(f'Maya palace side wall {side}', x0 - .4, cy - 1.0, x0 + .4, back - .1, ph + 1.6)
    for k in columns:
        block(f'Maya palace column {k}', cx + k - .4, cy - d * .5 + .3, cx + k + .4, cy - d * .5 + 1.1, ph + 3.15)
    block('Maya palace fallen lintel', cx - 2.2, cy - .4, cx + .2, cy + .8, ph + .4)


# The truck's front-left wheel came off (it lies beyond the wall) and its axle hangs 0.93 m up at about
# (60.64, 9.25): Truck.fbx measured, placed at (63, 6.5) and turned 180 degrees. OuterWall_1, the shipped ruin
# wall it ran into, ends at x 59.8.
TRUCK_AXLE = (60.64, 9.25, .93)


def truck_wreck_wall(stone, leaf):
    """The broken end of the wall the truck hit, with the front corner of the truck wedged on its rubble: the
    wall steps down from its full height, a slab lies across fallen blocks right under the axle, and loose
    stones lie round it."""
    ax, ay, az = TRUCK_AXLE
    # The wall's broken end, in its own line (z 11 to 12.15), stepping down towards the truck.
    for k, (x0, x1, top) in enumerate(((59.8, 60.35, 1.42), (60.35, 60.85, 1.08), (60.85, 61.25, .7))):
        stone.box(((x0 + x1) * .5, 11.575 + r_off(k), -.1), (x1 - x0 - .02, 1.1, top + .1), yaw=.03 * (k - 1),
                  style=MOSSY if k % 2 else LIMESTONE)
    # Fallen blocks under the corner and a slab across them, its top at the axle, a little out of level.
    for x, y, sx, sy, h, yaw in ((ax - .35, ay - .3, .8, .7, .58, .3), (ax + .25, ay + .25, .7, .8, .56, -.4),
                                 (ax - .2, ay + .45, .6, .6, .5, .9), (ax + .3, ay - .4, .6, .55, .45, 1.3)):
        stone.box((x, y, -.08), (sx, sy, h + .08), yaw=yaw, style=MOSSY)
    # 3 cm proud of the axle's underside, so the axle is wedged in the stone rather than resting on air.
    stone.box((ax, ay, az - .33), (1.15, 1.25, .36), yaw=.22, style=LIMESTONE,
              tilt=Matrix.Rotation(math.radians(3), 4, 'Y'))
    # A wall block knocked over and lying against the pile.
    stone.box((ax - .95, ay + .9, -.05), (1.1, .55, .55), yaw=.6, style=LIMESTONE,
              tilt=Matrix.Rotation(math.radians(-12), 4, 'X'))
    block('Truck wreck rubble', ax - .7, ay - .7, ax + .7, ay + .75, az - .02)
    block('Truck wreck wall end', 59.8, 11.0, 61.25, 12.15, 1.42)
    clearing('Truck wreck rubble', ax - 1.6, ay - 1.2, ax + 1.3, ay + 1.7, margin=0)


def r_off(k):
    return (-.04, .03, -.02)[k]


def terrace_stair(stone, leaf):
    """A broad stair up the north face of the central terrace, so everyone can walk up without a boost."""
    cx, top_y, top_z, w = 64.0, 45.5, 2.47, 4.0
    run = top_z / math.tan(math.radians(30))
    stair_flight(stone, cx, top_y, Matrix.Rotation(math.pi, 4, 'Z'), math.pi, run, 0.0, 0.0, top_z, w, 6)
    bush(leaf, cx - w * .5 - 1.4, top_y + run + .6, 0, .8)
    RAMPS.append({'name': 'Terrace north stair ramp', 'width': w + 1.6,
                  'foot': {'x': cx, 'y': 0.0, 'z': round(top_y + run + .25, 3)},
                  'top': {'x': cx, 'y': round(top_z + .03, 3), 'z': top_y}})


NEW_WALLS = [
    # id, centre x, centre y, along x?  None now: the rampart's gates are the shipped R3, R5 and R4, spread
    # about 18 m apart, and the layout keeps only those and the two vault side walls (R9, R10).
]


# A ruined rampart across the middle of the courtyard, z 21.6-22.4 from x 30 to 93.5, between the interrupters'
# side (north) and the truck. It joins the old gate frames (U2, U3, U4) and the R2 buttress, so the three
# breakable walls in those frames, R3, R5 and R4, are the only ways through. With all three standing the
# interrupters go round an end to reach the truck and the collectors round an end to reach the vaults; each
# broken gate is a shortcut for both sides.
RAMPART_H = 2.2
RAMPART = [
    ('West end', (30.0, 21.6, 43.65, 22.4)),
    ('R3 to R5', (48.35, 21.6, 60.3, 22.4)),
    ('R5 frame gap', (61.2, 21.6, 63.1, 22.4)),
    ('R5 to buttress', (66.5, 21.6, 75.4, 22.4)),
    ('Buttress to R4', (76.65, 21.6, 79.575, 22.4)),
    ('East end', (84.375, 21.6, 93.5, 22.4)),
]


def rampart(stone, leaf):
    """The rampart's stonework: four courses of blocks with a broken top, moss low down, rubble at its feet,
    plants on top and vines over it. Each piece is one blocker at its full height."""
    for name, (x0, y0, x1, y1) in RAMPART:
        along_x = x1 - x0 >= y1 - y0
        length, depth = (x1 - x0, y1 - y0) if along_x else (y1 - y0, x1 - x0)
        courses = 4
        for course in range(courses):
            z = -.15 + course * (RAMPART_H + .15) / courses
            ch = (RAMPART_H + .15) / courses
            t = 0.0 if course % 2 == 0 else rng.uniform(.3, .7)
            cuts = [0.0]
            while cuts[-1] < length - .3:
                cuts.append(min(length, cuts[-1] + rng.uniform(.9, 1.6) * (t + .5 if len(cuts) == 1 else 1)))
            cuts[-1] = length
            for a, b in zip(cuts, cuts[1:]):
                h = ch - .03
                if course == courses - 1 and rng.random() < .3:
                    h += rng.uniform(.12, .3)          # a proud block: the top is never below the blocker
                mid = (a + b) * .5
                cx, cy = (x0 + mid, (y0 + y1) * .5) if along_x else ((x0 + x1) * .5, y0 + mid)
                size = (b - a - .04, depth + rng.uniform(-.04, .04), h) if along_x else \
                       (depth + rng.uniform(-.04, .04), b - a - .04, h)
                stone.box((cx, cy, z), size, style=MOSSY if course == 0 or rng.random() < .2 else LIMESTONE)
        for _ in range(int(length / 3.5)):
            u = rng.uniform(.5, length - .5)
            cx, cy = (x0 + u, (y0 + y1) * .5) if along_x else ((x0 + x1) * .5, y0 + u)
            bush(leaf, cx, cy, RAMPART_H - .25, .45)
        block(f'Rampart {name}', x0, y0, x1, y1, RAMPART_H)
        # Clear the plants and rocks on its line, but stop short of its ends: the old gate frames and the R2
        # buttress it joins are shipped models with their own blockers, and must stay.
        if along_x:
            clearing(f'Rampart {name}', x0 + .6, y0 - .3, x1 - .6, y1 + .3, margin=0)
        else:
            clearing(f'Rampart {name}', x0 - .3, y0 + .6, x1 + .3, y1 - .6, margin=0)


def building_vines():
    """Vines over the tops and down the outer faces of the courtyard's buildings: the shipped ruins and vaults
    (their blockers give the walls), the central terrace and the palace."""
    layout = json.loads((RES / 'RuinLayout.json').read_text())
    skip = ('tier', 'ramp', 'stair', 'stelae', 'Rampart', 'Truck', 'column', 'R7', 'Terrace upper', 'landing',
            'OuterWall', 'Buttress', 'canopy', 'lintel', 'fallen')
    walls = [b for b in layout['blockers'] + BLOCKERS
             if not b.get('round') and 1.3 <= b['maxY'] <= 6 and not any(k in b['name'] for k in skip)]
    for b in walls:
        r = b['rect']
        x0, y0, x1, y1, top = r['x'], r['y'], r['z'], r['w'], b['maxY']
        if min(x1 - x0, y1 - y0) < .3 or (x1 - x0) * (y1 - y0) < 1.5:
            continue
        faces = ((Vector((0, -1, 0)), x0, x1, y0, True), (Vector((0, 1, 0)), x0, x1, y1, True),
                 (Vector((-1, 0, 0)), y0, y1, x0, False), (Vector((1, 0, 0)), y0, y1, x1, False))
        for n, a, c, at_, along_x in faces:
            length = c - a
            for _ in range(int(length / 4.5 + rng.random() * .8)):
                u = rng.uniform(a + .3, c - .3)
                p = Vector((u, at_, 0)) if along_x else Vector((at_, u, 0))
                lat = Vector((1, 0, 0)) if along_x else Vector((0, 1, 0))
                drop = rng.uniform(.5, .95) * top
                edge = p + Vector((0, 0, top))
                vine([edge - n * .35 + Vector((0, 0, .05)), edge + n * .05 + Vector((0, 0, .02)),
                      edge + n * .07 + lat * rng.uniform(-.3, .3) + Vector((0, 0, -drop * .5)),
                      edge + n * .06 + lat * rng.uniform(-.4, .4) + Vector((0, 0, -drop))],
                     radius=rng.uniform(.022, .038), leafiness=1.2, leaf_size=(.08, .17))


def rampart_vines():
    """Vines over the top of the rampart and down its faces."""
    for name, (x0, y0, x1, y1) in RAMPART:
        along_x = x1 - x0 >= y1 - y0
        length = max(x1 - x0, y1 - y0)
        for _ in range(int(length / 2.2)):
            u = rng.uniform(.3, length - .3)
            for side in (-1, 1):
                if rng.random() < .45:
                    continue
                cx, cy = (x0 + u, (y0 + y1) * .5) if along_x else ((x0 + x1) * .5, y0 + u)
                n = Vector((0, side, 0)) if along_x else Vector((side, 0, 0))
                lat = Vector((1, 0, 0)) if along_x else Vector((0, 1, 0))
                top = Vector((cx, cy, RAMPART_H - .05))
                drop = rng.uniform(.8, 2.0)
                face = .45
                vine([top - n * .2 + Vector((0, 0, .06)), top + n * (face - .05) + Vector((0, 0, .02)),
                      top + n * (face + .03) + lat * rng.uniform(-.3, .3) + Vector((0, 0, -drop * .5)),
                      top + n * (face + .04) + lat * rng.uniform(-.4, .4) + Vector((0, 0, -drop))],
                     radius=rng.uniform(.02, .035), leafiness=1.1, leaf_size=(.08, .17))


def breakable_walls():
    """Six more breakable walls: an intact and a rubble state under one origin, like R1 to R12."""
    objects = []
    for wid, cx, cy, along_x in NEW_WALLS:
        length, depth, height = 3.6, .8, 2.0
        yaw = 0.0 if along_x else math.pi * .5
        origin = bpy.data.objects.new(f'{wid} / breakable origin', None)
        origin.location = (cx, cy, 0)
        bpy.context.scene.collection.objects.link(origin)
        intact, broken = Part(), Part()
        m = Matrix.Rotation(yaw, 4, 'Z')
        for course in range(3):
            n = 3 if course % 2 == 0 else 4
            for k in range(n):
                seg = length / n
                o = m @ Vector(((k - (n - 1) * .5) * seg, 0, 0))
                h = height / 3 * (.82 if course == 2 and k in (0, n - 1) else 1.0)
                intact.box((o.x, o.y, course * height / 3), (seg - .04, depth, h - .03), yaw=yaw,
                           style=MOSSY if course == 0 else LIMESTONE)
        o = m @ Vector((0, -depth * .5, height * .45))
        intact.box((o.x, o.y, o.z), (length * .6, .08, .35), yaw=yaw, style=CREAM)
        for k in range(9):
            o = m @ Vector((rng.uniform(-.5, .5) * length, rng.uniform(-.9, .9), 0))
            sz = rng.uniform(.35, .7)
            broken.box((o.x, o.y, -.05), (sz, sz * .8, rng.uniform(.18, .42)), yaw=rng.random() * 3, style=MOSSY)
        for side in (-1, 1):
            o = m @ Vector((side * (length * .5 - .3), 0, 0))
            broken.box((o.x, o.y, -.05), (.6, depth, .55), yaw=yaw, style=LIMESTONE)
        for part, state in ((intact, 'Intact'), (broken, 'Broken')):
            part.finish_stone()
            ob = part.to_object(f'{wid}_{state}', 'MayaStone')
            ob.parent = origin
            objects.append(ob)
        objects.append(origin)
        x0, x1 = (cx - length * .5, cx + length * .5) if along_x else (cx - depth * .5, cx + depth * .5)
        y0, y1 = (cy - depth * .5, cy + depth * .5) if along_x else (cy - length * .5, cy + length * .5)
        clearing(f'{wid} wall', x0, y0, x1, y1, margin=.4)
        WALLS.append({'id': wid, 'position': {'x': cx, 'y': 0, 'z': cy},
                      'footprintXZ': {'x': x0, 'y': y0, 'z': x1, 'w': y1}, 'height': height})
    return objects


def stela_group(stone, name, x, y, count, along_x, heights):
    for k in range(count):
        o = (k - (count - 1) * .5) * 1.9
        sx, sy = (x + o, y) if along_x else (x, y + o)
        yaw = 0.0 if along_x else math.pi * .5
        stela(stone, sx, sy, 0.0, yaw=yaw + rng.uniform(-.12, .12), h=heights[k % len(heights)])
        hx, hy = (.7, .5) if along_x else (.5, .7)
        block(f'{name} {k + 1}', sx - hx, sy - hy, sx + hx, sy + hy, heights[k % len(heights)] + .45)
    span = (count - 1) * .95 + .8
    if along_x:
        clearing(name, x - span, y - .6, x + span, y + .6)
    else:
        clearing(name, x - .6, y - span, x + .6, y + span)


def ruin_wall(stone, leaf, name, x, y, length, along_x, h):
    wall_fragment(stone, leaf, x, y, 0.0, length, 0.0 if along_x else math.pi * .5, h=h)
    hx, hy = (length * .5, .5) if along_x else (.5, length * .5)
    hx, hy = (length * .5, .45) if along_x else (.45, length * .5)
    block(name, x - hx, y - hy, x + hx, y + hy, h)
    # The loose blocks of a fragment lie within 1.6 m of it.
    clearing(name, x - hx - 1.2, y - hy - 1.6, x + hx + 1.2, y + hy + 1.6, margin=0)


def inner_ruins(stone, leaf):
    """Smaller ruins on open ground inside the cordon, each with collision."""
    stela_group(stone, 'Maya stelae W2', 12.5, 21.5, 3, False, (2.4, 2.0, 2.7))
    stela_group(stone, 'Maya stelae N', 56.0, 53.2, 2, True, (2.2, 2.6))
    stela_group(stone, 'Maya stelae E', 112.0, 14.0, 3, False, (2.0, 2.5, 2.2))
    ruin_wall(stone, leaf, 'Maya ruin wall S', 28.5, 3.2, 6.0, True, 1.9)
    ruin_wall(stone, leaf, 'Maya ruin wall N', 83.0, 61.5, 6.0, True, 2.2)
    ruin_wall(stone, leaf, 'Maya ruin wall NW', 25.0, 62.0, 5.0, True, 1.7)
    stepped_pyramid(stone, leaf, 15.0, 29.0, 0.0, 3.0, 3.0, 3, 1.1, .6, math.pi * .5, 1.6,
                    overgrowth=.6, skirt=.3, climb='Maya mound W', slope=33)
    stepped_pyramid(stone, leaf, 97.0, 50.0, 0.0, 3.5, 3.5, 3, 1.2, .6, 0.0, 1.8,
                    overgrowth=.6, skirt=.3, climb='Maya mound E', slope=33)


def inner_landmarks(stone, leaf):
    """The two tall landmarks, inside the cordon and climbable."""
    # The great pyramid, north-west of the central terrace, stair facing east.
    top = stepped_pyramid(stone, leaf, 31.0, 52.0, 0.0, 8.0, 4.5, 5, 1.5, .6, math.pi * .5, 2.4,
                          temple=(4.4, 3.0, 3.4), overgrowth=.45, skirt=.3, climb='Maya great pyramid', slope=33,
                          temple_shift=1.4)
    block('Maya great pyramid shrine', 31.0 - 1.4 - 1.5, 52.0 - 2.2, 31.0 - 1.4 + 1.5, 52.0 + 2.2, top + 5.0)
    # The steep temple with a roof comb, east of the courtyard, stair facing west.
    top = stepped_pyramid(stone, leaf, 107.5, 32.0, 0.0, 6.0, 6.0, 4, 1.5, .9, 0.0, 2.4,
                          temple=(4.4, 3.2, 4.0), comb=(4.5, 7.0), overgrowth=.45, skirt=.3,
                          climb='Maya tall temple', slope=33, temple_shift=.8)
    block('Maya tall temple shrine', 107.5 - 2.2, 32.0 + .8 - 1.6, 107.5 + 2.2, 32.0 + .8 + 1.6, top + 4.0 + .45 + 1.4 + 7.0)


# --------------------------------------------------------------------------- jungle and mountains

# (centre x, y, half x, half y, margin) of anything outside the cordon the jungle must leave room for. The
# landmarks stand inside the courtyard now, so the jungle closes in all round.
LANDMARKS = []


def in_corridor(x, y):
    # No tree within 16 m of the road's centre line anywhere, so no crown hangs over the asphalt or the rails.
    if abs(y - ROAD_MID) < 16:
        return True
    if HELI_NORTH[0] < x < HELI_NORTH[1] and y > JUNGLE[3]:
        return True
    return False


def landmark_clear(x, y, extra=0.0):
    for cx, cy, hx, hy, m in LANDMARKS:
        if abs(x - cx) < hx + m + extra and abs(y - cy) < hy + m + extra:
            return True
    return False


def jungle_front_radius(theta):
    """Distance from CENTRE to the jungle front rectangle along theta."""
    d = Vector((math.cos(theta), math.sin(theta)))
    ts = []
    for k, lo, hi in ((0, JUNGLE[0], JUNGLE[2]), (1, JUNGLE[1], JUNGLE[3])):
        if abs(d[k]) > 1e-6:
            ts.append(((hi if d[k] > 0 else lo) - CENTRE[k]) / d[k])
    return min(t for t in ts if t > 0)


def road_height(ground, x):
    xc = min(max(x, TERRAIN[0] + .3), TERRAIN[2] - .3)
    # The highest ground anywhere across the asphalt (each sample is a ring minimum, so trees don't count):
    # near the terrain's edge it rises, and a road laid at its lowest point showed the grass through.
    hs = []
    for k in range(11):
        y = ROAD_SOUTH - .3 + (ROAD_NORTH - ROAD_SOUTH + .6) * k / 10
        low = ground.at(xc, y, .6)
        hit = ground.first(xc, y)
        # The surface itself where it is ground; a tree or a rock on it (well above the ring minimum) is not.
        hs.append(hit if hit is not None and hit < low + .8 else low)
    return max(hs) + .04


def jungle_surface(ground, road):
    """One vertex-coloured heightfield: canopy rising behind the front, clearings, then far mountains."""
    segments = 300
    offsets = [0, 1.2, 2.6, 4.2, 6.2, 8.6, 11.5, 15, 19, 24, 30, 37, 45, 55, 67, 81, 98, 118, 142, 170,
               205, 245, 290, 345, 410, 490, 580, 690, 820, 980, 1150]
    verts, cols, faces = [], [], []
    for i in range(segments):
        theta = i * math.tau / segments
        r0 = jungle_front_radius(theta)
        d = Vector((math.cos(theta), math.sin(theta)))
        for k, off in enumerate(offsets):
            p = CENTRE + d * (r0 + off)
            x, y = p.x, p.y
            in_terrain = inside(TERRAIN, x, y, -.5)
            base = ground.at(x, y, 1.6)
            if not in_terrain:
                # Continue the outer berm, gently rising.
                out = max(abs(x - min(max(x, TERRAIN[0]), TERRAIN[2])), abs(y - min(max(y, TERRAIN[1]), TERRAIN[3])))
                base += out * .012 + 1.2 * noise.noise(Vector((x / 90, y / 90, .3)))
            # The grid coarsens with distance (spacing between rays here): the band kept under the road widens with
            # it, so no big triangle reaching across the road edge can rise through the asphalt.
            spacing = (r0 + off) * math.tau / segments
            band_in, band_out = 9.0 + spacing, 15.0 + 2 * spacing
            # (The jungle only starts beyond the jungle front, so this never touches the courtyard.)
            road_band = abs(y - ROAD_MID) < band_out
            if road_band:
                under = road(x) - .6
                base = under + (base - under) * smoothstep(band_in, band_out, abs(y - ROAD_MID))
            canopy = 12.0 + 4.5 * noise.noise(Vector((x / 26, y / 26, 1.7))) + 2.5 * noise.noise(Vector((x / 9, y / 9, 4.1)))
            rise = smoothstep(.5, 7.5, off)
            cap = 1.0
            road_dist = abs(y - ROAD_MID)
            if not inside(JUNGLE, x, y) and (x < JUNGLE[0] or x > JUNGLE[2]):
                cap = min(cap, smoothstep(band_in, band_out + 4, road_dist))
            if HELI_NORTH[0] - 10 < x < HELI_NORTH[1] + 10 and y > JUNGLE[3] - 2:
                edge = min(x - HELI_NORTH[0], HELI_NORTH[1] - x)
                cap = min(cap, .18 + .82 * smoothstep(0, 10, -edge))
            if HELI_SOUTH[0] - 10 < x < HELI_SOUTH[1] + 10 and y < JUNGLE[1] + 2:
                edge = min(x - HELI_SOUTH[0], HELI_SOUTH[1] - x)
                cap = min(cap, .4 + .6 * smoothstep(0, 10, -edge))
            for cx, cy, hx, hy, m in LANDMARKS:
                ex = max(abs(x - cx) - hx, abs(y - cy) - hy)
                cap = min(cap, smoothstep(m, m + 7, ex))
            if in_terrain and off < 1:
                h = base - (0 if road_band else .25)
            else:
                h = base + rise * canopy * cap - (.25 if in_terrain and not road_band else 0)
            r = (p - CENTRE).length
            valley = smoothstep(12, 90, road_dist)
            ridge = 1 - abs(noise.noise(Vector((math.cos(theta) * 3.1, math.sin(theta) * 3.1, .7))))
            mountains = smoothstep(240, 460, r) * (55 + 95 * ridge ** 2 + 25 * noise.noise(Vector((x / 160, y / 160, 2.2))))
            mountains *= .25 + .75 * valley
            # Nothing of the mountains on the road itself: it runs on to the horizon in its own cutting.
            mountains *= smoothstep(band_in, band_out + 30, road_dist)
            h += mountains * (1 - .45 * smoothstep(800, 1150, r))
            verts.append((x, y, h))
            n = .5 + .5 * noise.noise(Vector((x / 14, y / 14, 7.7)))
            green = [LEAF_GREENS[0][c] * (1 - n) + LEAF_GREENS[2][c] * n for c in range(3)]
            soil = (.11, .13, .055)
            t = rise * cap
            c = [soil[j] * (1 - t) + green[j] * t for j in range(3)]
            if mountains > 1:
                m = smoothstep(1, 70, mountains)
                c = [c[j] * (1 - m * .35) + (.05, .08, .06)[j] * m * .35 for j in range(3)]
            cols.append((*c, 1.0))
    rings = len(offsets)
    for i in range(segments):
        j = (i + 1) % segments
        for k in range(rings - 1):
            a, b = i * rings + k, j * rings + k
            faces.append((a, a + 1, b + 1, b))
    me = bpy.data.meshes.new('Maya_JungleSurface')
    me.from_pydata(verts, [], faces)
    me.update()
    for poly in me.polygons:
        poly.use_smooth = True
    attr = me.color_attributes.new('Color', 'FLOAT_COLOR', 'POINT')
    attr.data.foreach_set('color', [c for col in cols for c in col])
    me.uv_layers.new(name='UVMap')
    ob = bpy.data.objects.new('Maya_JungleSurface', me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(get_material('MayaTintLeaf'))
    return ob


def tree(part_leaf, part_bark, x, y, z, height, spread, emergent=False):
    lean = (rng.uniform(-.05, .05), rng.uniform(-.05, .05))
    trunk_h = height * (.78 if emergent else .55)
    top = part_bark.trunk((x, y, z - .3), trunk_h + .3, spread * (.09 if emergent else .12), spread * .05, BARK, lean)
    g = rng.choice(LEAF_GREENS)
    if emergent:
        for k in range(5):
            a = k * math.tau / 5 + rng.random()
            part_leaf.blob((top.x + math.cos(a) * spread * .55, top.y + math.sin(a) * spread * .55, top.z + rng.uniform(0, 1.2)),
                           (spread * .6, spread * .6, spread * .25), g, seed=rng.random() * 90)
        return
    n = rng.randint(3, 5)
    for k in range(n):
        a = rng.random() * math.tau
        r = spread * rng.uniform(.0, .45)
        s = spread * rng.uniform(.45, .7)
        part_leaf.blob((top.x + math.cos(a) * r, top.y + math.sin(a) * r, top.z + rng.uniform(-.5, 1.6) * spread * .4),
                       (s, s, s * .85), g, seed=rng.random() * 90)


def forest(ground):
    leaf, bark = Part(), Part()
    # Two staggered rows along the jungle front, then tall emergents behind.
    contour = []
    x0, y0, x1, y1 = JUNGLE
    perimeter = [((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))]
    for (ax, ay), (bx, by) in perimeter:
        length = math.hypot(bx - ax, by - ay)
        n = int(length / 4.2)
        nx, ny = (by - ay) / length, -(bx - ax) / length   # outward for this winding
        for k in range(n):
            t = (k + rng.random()) / n
            contour.append((ax + (bx - ax) * t, ay + (by - ay) * t, nx, ny))
    count = 0
    for row, depth in enumerate((-1.5, 3.5, 9.0)):
        for x, y, nx, ny in contour:
            if row == 2 and rng.random() < .4:
                continue
            px = x + nx * (depth + rng.uniform(-1.5, 1.5)) + rng.uniform(-1.2, 1.2)
            py = y + ny * (depth + rng.uniform(-1.5, 1.5)) + rng.uniform(-1.2, 1.2)
            if in_corridor(px, py) or landmark_clear(px, py, -1.0):
                continue
            if HELI_SOUTH[0] < px < HELI_SOUTH[1] and py < JUNGLE[1] + 2:
                h = rng.uniform(5.5, 7.5)
            else:
                h = rng.uniform(8.5, 15.5)
            tree(leaf, bark, px, py, ground.at(px, py, .8), h, h * rng.uniform(.38, .5))
            count += 1
    for _ in range(70):
        theta = rng.random() * math.tau
        r = jungle_front_radius(theta) + rng.uniform(10, 110)
        p = CENTRE + Vector((math.cos(theta), math.sin(theta))) * r
        if in_corridor(p.x, p.y) or landmark_clear(p.x, p.y, 2.0):
            continue
        if HELI_SOUTH[0] - 6 < p.x < HELI_SOUTH[1] + 6 and p.y < JUNGLE[1]:
            continue
        h = rng.uniform(19, 27)
        base = ground.at(p.x, p.y, 1.0) if inside(TERRAIN, p.x, p.y) else ground.at(p.x, p.y, 1.0) + 1.0
        tree(leaf, bark, p.x, p.y, base, h, rng.uniform(6.5, 9.5), emergent=True)
        count += 1
    print('MAYA trees', count)
    return leaf.to_object('Maya_Leaves_Forest', 'MayaTintLeaf'), bark.to_object('Maya_Bark_Forest', 'MayaTintBark')


# --------------------------------------------------------------------------- road guardrails

GUARD_NORTH, GUARD_SOUTH = -6.2, -15.8       # rail lines, half a metre off each asphalt edge
MAP_SOUTH_EXTRA = 6.5                        # metres the minimap reaches south of z = 0, to the guardrail
GUARD_REACH = (-150.0, 270.0)
BREAK = (55.0, 64.5)                         # where the truck went through the north rail
# The four shipped trees that stood on the rail lines are cleared away (ROADSIDE_TREES), so the rails run
# unbroken along the whole road.
TREE_GAPS = {GUARD_NORTH: [], GUARD_SOUTH: []}
ROADSIDE_TREES = [(34.0, -5.0), (89.8, -4.5), (35.4, -16.2), (88.4, -16.45)]
RAIL_COLOURS = [(.42, .44, .45, 1.0), (.27, .28, .29, 1.0), (.33, .3, .27, 1.0)]
W_BEAM = [(0, -.155), (.05, -.13), (.08, -.085), (.035, -.02), (.035, .02), (.08, .085), (.05, .13), (0, .155)]


def sweep(part, path, style=0):
    """Extrude the W-beam along path: (x, y, z, roll degrees). The beam faces the road side of the path."""
    ring = W_BEAM + [(u - .012, v) for u, v in reversed(W_BEAM)]
    rings = []
    for i, (x, y, z, roll) in enumerate(path):
        a = Vector(path[max(i - 1, 0)][:3])
        b = Vector(path[min(i + 1, len(path) - 1)][:3])
        t = (b - a).normalized()
        lat = t.cross(Vector((0, 0, 1))).normalized()
        up = lat.cross(t).normalized()
        r = Matrix.Rotation(math.radians(roll), 3, t)
        lat, up = r @ lat, r @ up
        p = Vector((x, y, z))
        rings.append([part.bm.verts.new(p + lat * u + up * v) for u, v in ring])
    n = len(ring)
    for k in range(len(rings) - 1):
        for j in range(n):
            f = part.bm.faces.new((rings[k][j], rings[k][(j + 1) % n], rings[k + 1][(j + 1) % n], rings[k + 1][j]))
            f[part.style] = style


def post(part, x, y, z, toward_road, knock=0.0, twist=0.0, height=1.05, style=1):
    """An I-section post, optionally knocked over away from the road by knock degrees."""
    tilt = Matrix.Rotation(math.radians(knock), 4, 'X') if knock else None
    yaw = 0.0 if toward_road < 0 else math.pi
    part.box((x, y, z - .35), (.11, .16, height + .35), yaw=yaw + math.radians(twist), tilt=tilt, style=style)


def guardrails(ground, road):
    part = Part()
    for k, (x, y) in enumerate(ROADSIDE_TREES):
        clearing(f'Roadside tree {k + 1}', x - 2.5, y - 2.5, x + 2.5, y + 2.5, margin=0)

    def gh(x, y):
        # Never below the road it guards (the road rides over the terrain's edge berm).
        return max(ground.at(x, y, .6), road(x) - .04) if inside(TERRAIN, x, y, -.5) else road(x) - .04

    for line, toward in ((GUARD_NORTH, -1), (GUARD_SOUTH, 1)):
        post_y = line - toward * .25
        path = []
        segments = [path]
        x = GUARD_REACH[0]
        while x <= GUARD_REACH[1] + .01:
            h = gh(x, line)
            broken = line == GUARD_NORTH and BREAK[0] < x < BREAK[1]
            gap = next((g for g in TREE_GAPS[line] if g[0] < x < g[1]), None)
            if gap:
                if path and path[-1][0] < gap[0]:
                    # Turn the rail end down to the ground before the tree, then start again after it.
                    path.append((x - 1.0, line, gh(x - 1.0, line) + .12, 0.0))
                    path = []
                    segments.append(path)
                    nx = gap[1] + .4
                    path.append((nx, line, gh(nx, line) + .12, 0.0))
            elif not broken:
                path.append((x, line, h + .55, 0.0))
                post(part, x, post_y, gh(x, post_y), toward)
                part.box((x, line - toward * .1, h + .38), (.1, .12, .18), style=1)
            elif line == GUARD_NORTH and abs(x - 56) < .1:
                # The sagging, twisted span the truck drove over, still hanging between the torn ends.
                for px, py, pz, roll in ((56.0, -6.0, .5, -15), (57.2, -5.75, .4, -30), (58.6, -5.55, .3, -45),
                                         (60.0, -5.5, .26, -55), (61.4, -5.55, .28, -50), (62.8, -5.7, .34, -35),
                                         (64.0, -5.95, .45, -15)):
                    path.append((px, py, gh(px, py) + pz, roll))
            x += 2.0
        for segment in segments:
            segment.sort(key=lambda p: p[0])
            if len(segment) > 1:
                sweep(part, segment)
    # Posts of the broken section: two knocked flat, one snapped, two leaning.
    for x, knock, twist, height in ((56.0, -80, 8, 1.05), (58.0, -86, 25, 1.05), (60.0, 0, 0, .22),
                                    (62.0, -45, -10, 1.05), (64.0, -18, 0, 1.05)):
        y = GUARD_NORTH + .25
        post(part, x, y, gh(x, y), -1, knock=knock, twist=twist, height=height, style=2)
    # A torn length of rail thrown onto the road shoulder.
    sweep(part, [(56.5, -7.0, gh(56.5, -7.0) + .08, -85), (58.5, -7.35, gh(58.5, -7.35) + .1, -80),
                 (60.6, -7.1, gh(60.6, -7.1) + .17, -70)], style=2)
    for f in part.bm.faces:
        c = RAIL_COLOURS[f[part.style]]
        for loop in f.loops:
            loop[part.col] = c
            loop[part.uv].uv = (.5, .5)
    return part.to_object('Maya_Metal_Guardrails', 'MayaTintMetal')


# --------------------------------------------------------------------------- decal paths

def track_paths(ground):
    """Two tyre tracks: braking east along the road, swerving north onto the apron and up to the truck."""
    ctrl = [(18, -12.5), (30, -12.4), (40, -12.0), (48, -11.0), (54, -9.4), (58.4, -7.4),
            (61.2, -4.8), (62.6, -2.2), (63.0, .6), (63.0, 3.4)]
    pts = []
    for i in range(len(ctrl) - 1):
        a, b = Vector(ctrl[i]), Vector(ctrl[i + 1])
        p0 = Vector(ctrl[i - 1]) if i > 0 else a
        p3 = Vector(ctrl[i + 2]) if i + 2 < len(ctrl) else b
        for s in range(8):
            t = s / 8
            # Catmull-Rom through the control points.
            p = .5 * ((2 * a) + (-p0 + b) * t + (2 * p0 - 5 * a + 4 * b - p3) * t * t + (-p0 + 3 * a - 3 * b + p3) * t ** 3)
            pts.append(p)
    pts.append(Vector(ctrl[-1]))
    tracks = []
    for side in (-1, 1):
        line = []
        for i, p in enumerate(pts):
            q = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
            n = Vector((-q.y, q.x)).normalized()
            o = p + n * side * 2.36   # the truck's wheels sit 2.36 m either side of its centre line
            line.append(o)
        # Each wheel station is a pair of dual tyres.
        for dual in (-.21, .21):
            pts3 = []
            for i, o in enumerate(line):
                q = line[min(i + 1, len(line) - 1)] - line[max(i - 1, 0)]
                n = Vector((-q.y, q.x)).normalized()
                w = o + n * dual
                on_road = ROAD_SOUTH + .2 < w.y < ROAD_NORTH - .2
                h = road_height(ground, w.x) if on_road else ground.at(w.x, w.y, .5)
                pts3.append({'x': round(w.x, 3), 'y': round(h, 3), 'z': round(w.y, 3)})
            tracks.append({'points': pts3})
    return tracks


# --------------------------------------------------------------------------- textures

def tile_noise(w, h, cx, cy, seed):
    """Tileable value noise of cx by cy cells, smoothly interpolated."""
    g = np.random.default_rng(seed).random((cy, cx))
    ys = np.arange(h) * cy / h
    xs = np.arange(w) * cx / w
    y0 = np.floor(ys).astype(int)
    x0 = np.floor(xs).astype(int)
    fy = ys - y0
    fx = xs - x0
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    a = g[y0 % cy][:, x0 % cx]
    b = g[y0 % cy][:, (x0 + 1) % cx]
    c = g[(y0 + 1) % cy][:, x0 % cx]
    d = g[(y0 + 1) % cy][:, (x0 + 1) % cx]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def fbm(w, h, cx, cy, seed, octaves=4):
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        out += amp * tile_noise(w, h, cx * 2 ** o, cy * 2 ** o, seed + o)
        total += amp
        amp *= .5
    return out / total


def save_png(path, rgba):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(path.stem, w, h, alpha=True)
    img.pixels = np.clip(rgba[::-1], 0, 1).astype(np.float32).ravel()
    img.filepath_raw = str(path)
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def draw_line(mask, pts, width, value=1.0, wrap=True):
    h, w = mask.shape
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        n = int(max(abs(bx - ax), abs(by - ay))) + 1
        for t in np.linspace(0, 1, n):
            x, y = ax + (bx - ax) * t, ay + (by - ay) * t
            r = int(math.ceil(width))
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if dx * dx + dy * dy <= width * width:
                        yy, xx = int(y) + dy, int(x) + dx
                        if 0 <= yy < h:
                            mask[yy, xx % w if wrap else min(max(xx, 0), w - 1)] = max(mask[yy, xx % w], value)


def paint_road():
    """32 m of road by 12 m across at 64 px per metre. v runs south to north, from y -17.0 to -5.0.
    A tidy, kept road: even asphalt with a fine grain, crisp solid edge lines, a crisp dashed centre line and
    straight gravel shoulders. (The truck's own skid and rut marks are separate decals.)"""
    W, H, PPM = 2048, 768, 64
    rnd = np.random.default_rng(7)
    rows = np.arange(H)[:, None] / PPM - 17.0 + np.zeros((1, W))
    blotch = fbm(W, H, 16, 6, 11)
    fine = rnd.random((H, W))
    asphalt = .29 + .018 * (blotch - .5) + .03 * (fine - .5)
    # The faintest polish along the wheel paths, just enough to read the lanes.
    for lane in (-12.85, -9.05):
        for wheel in (-.9, .9):
            asphalt -= .012 * np.exp(-((rows - lane - wheel) / .32) ** 2)
    rgb = np.stack([asphalt * .98, asphalt, asphalt * 1.03], -1)
    # Lane paint: solid edge lines just inside the asphalt, a 3 m dash every 8 m down the middle.
    paint = np.zeros((H, W))
    for y in (-7.0 - .35, -15.0 + .35):
        paint = np.maximum(paint, np.clip((.08 - np.abs(rows - y)) * PPM * .5 + .5, 0, 1))
    dash = (((np.arange(W) / PPM) % 8.0) < 3.0)[None, :]
    paint = np.maximum(paint, np.clip((.065 - np.abs(rows - ROAD_MID)) * PPM * .5 + .5, 0, 1) * dash)
    paint *= .93 + .07 * fine
    cream = np.array([.9, .89, .82])
    rgb = rgb * (1 - paint[..., None]) + cream * paint[..., None]
    # Straight gravel shoulders a metre wide, then a soft fade into the verge.
    south = rows - ROAD_SOUTH
    north = ROAD_NORTH - rows
    inner = np.minimum(south, north)
    gravel = np.stack([.5 + 0 * fine, .47 + 0 * fine, .41 + 0 * fine], -1)
    gravel *= (.92 + .16 * fine)[..., None]
    road_mask = np.clip(inner * PPM * .5 + .5, 0, 1)
    rgb = rgb * road_mask[..., None] + gravel * (1 - road_mask[..., None])
    outer = np.minimum(rows - (ROAD_SOUTH - 1.0), (ROAD_NORTH + 1.0) - rows)
    alpha = np.clip(outer * 4.0 + .5, 0, 1)
    alpha = np.maximum(alpha, road_mask)
    save_png(RES / 'RuinRoad.png', np.concatenate([rgb, alpha[..., None]], -1))


def paint_decals():
    """Four 256 px strips, each tileable along u: tyre scrub, grass rut, safety paint, soft scuff."""
    W, S = 1024, 256
    rnd = np.random.default_rng(9)
    out = np.zeros((S * 4, W, 4))
    v = (np.arange(S)[:, None] + .5) / S + np.zeros((1, W))
    u = np.arange(W)[None, :] / W + np.zeros((S, 1))
    long = fbm(W, S, 24, 1, 51)
    grain = rnd.random((S, W))
    # Tyre scrub on asphalt: rubber with tread grooves, fading in and out along the skid.
    profile = np.clip(1 - np.abs(v - .5) * 2.2, 0, 1) ** .6
    grooves = .75 + .25 * np.cos(v * math.tau * 5)
    a = profile * grooves * np.clip((long - .25) * 2.2, .15, 1) * (.75 + .25 * grain)
    out[3 * S:4 * S, :, :3] = .06
    out[3 * S:4 * S, :, 3] = a * .85
    # Rut through grass: dark soil, tread chevrons, torn green edges.
    edge = fbm(W, S, 40, 2, 61)
    dist = np.abs(v - .5) * 2 + (edge - .5) * .35
    soil = np.clip(1 - dist * 1.25, 0, 1)
    chevron = ((u * 48 + np.abs(v - .5) * 6) % 1 < .45).astype(float)
    col = np.stack([.26 + .05 * grain, .19 + .04 * grain, .12 + .03 * grain], -1)
    col *= (1 - .3 * chevron * soil)[..., None]
    torn = np.clip(1 - np.abs(dist - .82) * 6, 0, 1)
    col = col * (1 - torn[..., None]) + np.array([.33, .36, .2]) * torn[..., None]
    out[2 * S:3 * S, :, :3] = col
    out[2 * S:3 * S, :, 3] = np.clip(np.maximum(soil * 1.4, torn * .8) * (.85 + .15 * grain), 0, 1)
    # Worn yellow paint with overspray.
    wear = fbm(W, S, 64, 4, 71)
    core = np.clip(1 - np.abs(v - .5) * 2.6 + (fbm(W, S, 80, 3, 72) - .5) * .25, 0, 1)
    core = np.clip(core * 4, 0, 1)
    spray = np.clip(1 - np.abs(v - .5) * 2, 0, 1) ** 2 * .35 * (grain > .55)
    a = np.maximum(core * np.clip((wear - .22) * 3, 0, 1) * (.8 + .2 * grain), spray)
    out[S:2 * S, :, :3] = np.array([.88, .66, .13]) * (.92 + .08 * grain)[..., None]
    out[S:2 * S, :, 3] = a
    # A soft broad scuff for the outer edge of a skid.
    soft = np.clip(1 - np.abs(v - .5) * 2, 0, 1) ** 1.5 * np.clip(long * 1.4 - .2, 0, 1)
    out[0:S, :, :3] = .1
    out[0:S, :, 3] = soft * .4
    save_png(RES / 'RuinDecals.png', out)


def update_minimap():
    """Draw the inside structures onto the V10 minimap. The untouched V10 picture is kept outside Assets."""
    import shutil
    target = RES / 'RuinMinimap.png'
    base = ROOT / 'Tools/blender/data/RuinMinimap_V10.png'
    if not base.exists():
        base.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(target, base)
    img = bpy.data.images.load(str(base))
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    sx, sy = w / PLAY[2], h / PLAY[3]

    def fill(x0, y0, x1, y1, colour, alpha=1.0):
        c0, c1 = int(max(0, x0 * sx)), int(min(w, x1 * sx))
        r0, r1 = int(max(0, y0 * sy)), int(min(h, y1 * sy))
        px[r0:r1, c0:c1, :3] = px[r0:r1, c0:c1, :3] * (1 - alpha) + np.array(colour) * alpha

    for b in BLOCKERS:
        r = b['rect']
        if 'stair' in b['name']:
            continue
        fill(r['x'] + .5, r['y'] - .5, r['z'] + .5, r['w'] - .5, (.18, .24, .12), .45)
    for b in BLOCKERS:
        r = b['rect']
        x0, y0, x1, y1 = r['x'], r['y'], r['z'], r['w']
        if 'stair' in b['name']:
            fill(x0, y0, x1, y1, (.83, .82, .76))
            continue
        tiers = max(1, int(min(x1 - x0, y1 - y0) / 1.6))
        for k in range(tiers):
            t = k / max(1, tiers - 1)
            inset = k * .7
            shade = (.72 + .14 * t, .7 + .14 * t, .62 + .14 * t)
            fill(x0 + inset, y0 + inset, x1 - inset, y1 - inset, shade)
    # The Maya breakable walls, pale like the V10 picture's own (R1-R12).
    for wl in WALLS:
        f = wl['footprintXZ']
        fill(f['x'], f['y'], f['z'], f['w'], (.86, .85, .8))

    # The split stairs into the pits (make_ruin_pit_stairs.py), over the old switchbacks in the V10 picture:
    # lighter stone the higher it stands.
    sp = PIT_SPLIT

    def stone(height):
        t = (height + 3.5) / 3.6
        return (.5 + .36 * t, .48 + .35 * t, .42 + .3 * t)

    for k, pit in enumerate(json.loads((RES / 'RuinLayout.json').read_text())['pits']):
        x0, y0, x1, y1 = pit['x'], pit['y'], pit['z'], pit['w']
        cx, side, land = (x0 + x1) / 2, sp['side_w'][k], y0 + sp['landing_depth']
        floor = np.median(px[int((y1 - 1.5) * sy):int((y1 - .5) * sy), int((cx - .6) * sx):int((cx + .6) * sx)].reshape(-1, 4), axis=0)
        fill(x0, y0, x1, y1, tuple(floor[:3]))
        fill(x0, y0, x1, land, stone(sp['landing']))
        for i in range(sp['treads']):
            top = sp['landing'] - (i + 1) * sp['rise']
            fill(x0, land + i * sp['tread'], x0 + side, land + (i + 1) * sp['tread'], stone(top))
            fill(x1 - side, land + i * sp['tread'], x1, land + (i + 1) * sp['tread'], stone(top))
        rim, depth = sp['threshold']
        half = sp['centre_w'] / 2
        fill(cx - half, y0 - depth, cx + half, y0, stone(rim))
        for j in range(3):
            fill(cx - half + .38, y0 + j * sp['centre_tread'], cx + half - .38, y0 + (j + 1) * sp['centre_tread'],
                 stone(rim - (j + 1) * sp['centre_rise']))

    # Extend the picture south to the guardrail: the strip between the ruin and the road is playable now.
    extra = int(round(MAP_SOUTH_EXTRA * sy))
    grass = np.median(px[:12].reshape(-1, 4), axis=0)
    strip = np.tile(grass, (extra, w, 1))
    strip[..., :3] *= (.96 + .08 * np.random.default_rng(3).random((extra, w)))[..., None]
    def strip_fill(x0, y0, x1, y1, colour):
        r0, r1 = int(max(0, (y0 + MAP_SOUTH_EXTRA) * sy)), int(min(extra, (y1 + MAP_SOUTH_EXTRA) * sy))
        c0, c1 = int(max(0, x0 * sx)), int(min(w, x1 * sx))
        strip[r0:r1, c0:c1, :3] = colour
    strip_fill(0, GUARD_NORTH - .12, PLAY[2], GUARD_NORTH + .12, (.66, .67, .66))
    for sd in SOLIDS:
        c, z = sd['centre'], sd['size']
        if 'barricade' in sd['name'].lower():
            strip_fill(c['x'] - z['x'] * .5, c['z'] - .3, c['x'] + z['x'] * .5, c['z'] + .3, (.86, .45, .2))
    px = np.concatenate([strip, px], 0)
    h += extra
    # Only the play area (PLAY_AREA, with the strip south of z 0 down to the map's -6.5): it was drawn in.
    c0, c1 = int(round(PLAY_AREA[0] * sx)), int(round(PLAY_AREA[2] * sx))
    r1 = int(round((PLAY_AREA[3] + MAP_SOUTH_EXTRA) * sy))
    px = np.ascontiguousarray(px[:r1, c0:c1])
    h, w = px.shape[:2]
    img = bpy.data.images.new('RuinMinimap', w, h, alpha=True)
    img.pixels = px.ravel()
    img.filepath_raw = str(target)
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


# --------------------------------------------------------------------------- the breakable walls as stones

def wall_stones():
    """Each breakable wall in the layout (R3, R4, R5, R9, R10) rebuilt as a stack of separate stones over its
    shipped footprint, so the level can knock them out a batch at a time and pull them back. A stone is named
    '<id>_Stone_<n>_s<stage>': stage 1 goes with the first blast of a collector's blower, stage 2 with the
    second, stage 3 when the wall comes down. Each stone's origin is its own centre, under '<id> / stones'.
    Its own random stream leaves the rest of the level alone."""
    r = random.Random(91)
    layout = json.loads((RES / 'RuinLayout.json').read_text())
    objects = []
    for wall in layout['walls']:
        wid, f, height = wall['id'], wall['footprintXZ'], wall['height']
        x0, y0, x1, y1 = f['x'], f['y'], f['z'], f['w']
        along_x = x1 - x0 >= y1 - y0
        length, depth = (x1 - x0, y1 - y0) if along_x else (y1 - y0, x1 - x0)
        yaw = 0.0 if along_x else math.pi * .5
        root = bpy.data.objects.new(f'{wid} / stones', None)
        bpy.context.scene.collection.objects.link(root)
        root.location = ((x0 + x1) * .5, (y0 + y1) * .5, 0)
        courses = max(3, round(height / .36))
        ch = height / courses
        stones = []                                  # [course, a, b]
        for c in range(courses):
            cuts = [0.0, r.uniform(.22, .42) if c % 2 else r.uniform(.4, .65)]
            while cuts[-1] < length - .25:
                cuts.append(cuts[-1] + r.uniform(.42, .7))
            cuts[-1] = length
            if cuts[-1] - cuts[-2] < .2:
                cuts.pop(-2)
            stones += [[c, a, b] for a, b in zip(cuts, cuts[1:])]
        # Which blast takes each stone: the top course goes first, then the next one down, then the rest.
        top = [k for k, st in enumerate(stones) if st[0] == courses - 1]
        below = [k for k, st in enumerate(stones) if st[0] == courses - 2]
        r.shuffle(top)
        r.shuffle(below)
        stage = {k: 3 for k in range(len(stones))}
        first = max(2, len(top) * 2 // 3)
        for k in top[:first]:
            stage[k] = 1
        for k in top[first:] + below[:max(2, len(below) * 2 // 3)]:
            stage[k] = 2
        for k, (c, a, b) in enumerate(stones):
            part = Part()
            mid = (a + b) * .5
            centre = Vector((x0 + mid, (y0 + y1) * .5, 0)) if along_x else Vector(((x0 + x1) * .5, y0 + mid, 0))
            # A little out of true, like dry-laid stone.
            centre += (Vector((0, r.uniform(-.03, .03), 0)) if along_x else Vector((r.uniform(-.03, .03), 0, 0)))
            h = ch - r.uniform(.008, .015)
            size = (b - a - r.uniform(.008, .016), depth * r.uniform(.94, 1.0), h)
            part.box((centre.x, centre.y, c * ch), size, yaw=yaw + r.uniform(-.02, .02), inset=r.uniform(.004, .012),
                     style=MOSSY if c == 0 or r.random() < .22 else LIMESTONE)
            part.finish_stone(bevel=.035)
            ob = part.to_object(f'{wid}_Stone_{k:02}_s{stage[k]}', 'MayaStone')
            pivot = Vector((centre.x, centre.y, c * ch + h * .5))
            ob.data.transform(Matrix.Translation(-pivot))
            ob.location = pivot
            bpy.context.view_layer.update()
            ob.parent = root
            ob.matrix_parent_inverse = root.matrix_world.inverted()
            objects.append(ob)
        objects.append(root)
    return objects


# --------------------------------------------------------------------------- main

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ground = Ground()
    road_xs = list(range(int(TERRAIN[0]), int(TERRAIN[2]) + 1, 2))
    road_hs_raw = [road_height(ground, x) for x in road_xs]
    # Smoothed, but never below the ground it covers.
    road_hs = [max(road_hs_raw[max(0, i - 1):i + 2] + [sum(road_hs_raw[max(0, i - 2):i + 3]) / len(road_hs_raw[max(0, i - 2):i + 3])])
               for i in range(len(road_hs_raw))]

    def road(x):
        if x <= road_xs[0]:
            return road_hs[0]
        if x >= road_xs[-1]:
            return road_hs[-1]
        i = int((x - road_xs[0]) // 2)
        t = (x - road_xs[i]) / 2
        return road_hs[i] * (1 - t) + road_hs[min(i + 1, len(road_hs) - 1)] * t

    VINE_PARTS[:] = [Part(), Part()]
    inner_stone, inner_leaf = Part(), Part()
    interior(inner_stone, inner_leaf)
    inner_ruins(inner_stone, inner_leaf)
    truck_wreck_wall(inner_stone, inner_leaf)
    rampart(inner_stone, inner_leaf)
    inner_stone.finish_stone()
    objects = [inner_stone.to_object('Maya_Stone_Courtyard', 'MayaStone'),
               inner_leaf.to_object('Maya_Leaves_Courtyard', 'MayaTintLeaf')]

    mark_stone, mark_leaf = Part(), Part()
    inner_landmarks(mark_stone, mark_leaf)
    mark_stone.finish_stone(bevel=.08)
    objects += [mark_stone.to_object('Maya_Stone_Landmarks', 'MayaStone'),
                mark_leaf.to_object('Maya_Leaves_Landmarks', 'MayaTintLeaf')]

    layout = json.loads((RES / 'RuinLayout.json').read_text())
    pit_vines([(p['x'], p['y'], p['z'], p['w']) for p in layout['pits']])
    rampart_vines()
    building_vines()
    stems, leaves = VINE_PARTS
    objects += [stems.to_object('Maya_Bark_Vines', 'MayaTintBark'), leaves.to_object('Maya_Leaves_Vines', 'MayaTintLeaf')]
    VINE_PARTS.clear()
    # Everything after the pit vines draws from its own stream, so retuning the vines never reshuffles them.
    rng.seed(4123)

    objects.append(guardrails(ground, road))
    objects += breakable_walls()
    objects += list(forest(ground))
    objects.append(jungle_surface(ground, road))
    objects += wall_stones()

    meshes = [ob for ob in objects if ob.type == 'MESH']
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in meshes)
    print('MAYA triangles', tris)
    for ob in meshes:
        print('MAYA object', ob.name, sum(len(p.vertices) - 2 for p in ob.data.polygons))

    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(RES / 'RuinCourtyard_Maya.fbx'), use_selection=True,
                             object_types={'MESH', 'EMPTY'}, bake_anim=False, axis_forward='-Z', axis_up='Y',
                             path_mode='AUTO', embed_textures=False, add_leaf_bones=False,
                             colors_type='LINEAR', mesh_smooth_type='FACE')

    road_profile = [{'x': float(x), 'y': round(h, 3)} for x, h in zip(road_xs, road_hs)]
    data = {'blockers': BLOCKERS, 'ramps': RAMPS, 'solids': SOLIDS, 'walls': WALLS, 'clearings': CLEARINGS,
            'road': road_profile, 'roadSouth': ROAD_SOUTH, 'roadNorth': ROAD_NORTH,
            'roadReach': {'x': ROAD_REACH[0], 'y': ROAD_REACH[1]}, 'tracks': track_paths(ground)}
    (RES / 'RuinMaya.json').write_text(json.dumps(data, indent=1))
    paint_road()
    paint_decals()
    update_minimap()
    print('MAYA done', len(BLOCKERS), 'blockers')


main()

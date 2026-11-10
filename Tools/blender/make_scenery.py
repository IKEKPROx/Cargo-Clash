"""Builds the scenery of level 1 and writes one FBX per model into Assets/Game/Models.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_scenery.py

Add `-- --sheet folder` at the end to also render a check image of each model, and `--only Rock_A,Barn`
to build just those. A model's front points along +Y here, which is -Z in Unity, so with no rotation
it faces the game camera. A part whose name starts with `Plain` gets no outline in the game.
"""
import importlib
import math
import random
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Euler, Matrix, Vector
from modeling import MeshPart


def colour(r, g, b, roughness=0.75):
    """Written as sRGB like the materials in Unity. Only the check images use these."""
    return (r ** 2.2, g ** 2.2, b ** 2.2, roughness)


BARK = "Tree_Bark"
LEAF_DARK = "Leaf_Dark"
LEAF_MID = "Leaf_Mid"
LEAF_LIGHT = "Leaf_Light"
LEAF_YELLOW = "Leaf_Yellow"
PINE_DARK = "Pine_Dark"
PINE_LIGHT = "Pine_Light"
ROCK = "Rock_Base"
ROCK_LIGHT = "Rock_Light"
MOSS = "Rock_Moss"
FENCE = "Fence_White"
FENCE_CAP = "Fence_Cap"
GUARD = "Guard_Steel"
GUARD_DARK = "Guard_Groove"
GUARD_POST = "Guard_Post"
CONE = "Cone_Orange"
CONE_BAND = "Cone_Band"
FRAME = "Barrier_Frame"
AMBER = "Truck_Amber"
SIGN = "Sign_Yellow"
SIGN_DARK = "Sign_Black"
POLE = "Pole_Wood"
WIRE = "Pole_Wire"
INSULATOR = "Pole_Insulator"
HAY = "Hay"
HAY_DARK = "Hay_Dark"
TWINE = "Hay_Twine"
BARN = "Barn_Red"
BARN_DARK = "Barn_RedDark"
TRIM = "Barn_Trim"
ROOF = "Barn_Roof"
HOLE = "Barn_Hole"
STONE = "Barn_Stone"
SILO = "Silo_Steel"
SILO_BAND = "Silo_Band"
SILO_CAP = "Silo_Cap"
BOARD = "Board_White"
BOARD_FRAME = "Board_Frame"
BOARD_TEXT = "Board_Text"
BOARD_ACCENT = "Board_Accent"
BOARD_WOOD = "Board_Wood"
TUFT_LIGHT = "Tuft_Light"
TUFT_DARK = "Tuft_Dark"
FLOWER_WHITE = "Flower_White"
FLOWER_YELLOW = "Flower_Yellow"
FLOWER_PINK = "Flower_Pink"
DIRT = "Dirt_Top"
DIRT_DARK = "Dirt_Dark"
CLOUD = "Cloud_White"
TRUCK_ORANGE = "Truck_Orange"
TRUCK_ORANGE_DARK = "Truck_OrangeDark"
TRUCK_DARK = "Truck_Dark"
TRUCK_STEEL = "Truck_Steel"
TRUCK_GLASS = "Truck_Glass"

PALETTE = {
    BARK: colour(0.45, 0.31, 0.19),
    LEAF_DARK: colour(0.17, 0.43, 0.22),
    LEAF_MID: colour(0.25, 0.56, 0.25),
    LEAF_LIGHT: colour(0.4, 0.68, 0.27),
    LEAF_YELLOW: colour(0.66, 0.77, 0.27),
    PINE_DARK: colour(0.13, 0.39, 0.28),
    PINE_LIGHT: colour(0.21, 0.5, 0.32),
    ROCK: colour(0.47, 0.48, 0.52),
    ROCK_LIGHT: colour(0.6, 0.61, 0.63),
    MOSS: colour(0.42, 0.59, 0.27),
    FENCE: colour(0.95, 0.93, 0.86),
    FENCE_CAP: colour(0.36, 0.44, 0.53),
    GUARD: colour(0.74, 0.76, 0.79, 0.35),
    GUARD_DARK: colour(0.48, 0.5, 0.54, 0.4),
    GUARD_POST: colour(0.32, 0.34, 0.38, 0.5),
    CONE: colour(1.0, 0.5, 0.12),
    CONE_BAND: colour(0.97, 0.97, 0.93),
    FRAME: colour(0.2, 0.21, 0.24, 0.5),
    AMBER: colour(1.0, 0.77, 0.19, 0.3),
    SIGN: colour(1.0, 0.82, 0.1, 0.4),
    SIGN_DARK: colour(0.08, 0.08, 0.09, 0.5),
    POLE: colour(0.36, 0.26, 0.18),
    WIRE: colour(0.07, 0.07, 0.08),
    INSULATOR: colour(0.85, 0.86, 0.8, 0.3),
    HAY: colour(0.88, 0.73, 0.37),
    HAY_DARK: colour(0.67, 0.51, 0.23),
    TWINE: colour(0.4, 0.25, 0.12),
    BARN: colour(0.7, 0.17, 0.14),
    BARN_DARK: colour(0.5, 0.11, 0.1),
    TRIM: colour(0.95, 0.93, 0.87),
    ROOF: colour(0.3, 0.33, 0.38),
    HOLE: colour(0.12, 0.12, 0.15),
    STONE: colour(0.55, 0.54, 0.52),
    SILO: colour(0.75, 0.78, 0.81, 0.4),
    SILO_BAND: colour(0.5, 0.52, 0.56, 0.4),
    SILO_CAP: colour(0.6, 0.2, 0.16, 0.4),
    BOARD: colour(0.97, 0.96, 0.9),
    BOARD_FRAME: colour(0.13, 0.31, 0.58),
    BOARD_TEXT: colour(0.1, 0.16, 0.32),
    BOARD_ACCENT: colour(0.98, 0.48, 0.15),
    BOARD_WOOD: colour(0.42, 0.3, 0.2),
    TUFT_LIGHT: colour(0.46, 0.7, 0.28),
    TUFT_DARK: colour(0.29, 0.54, 0.24),
    FLOWER_WHITE: colour(0.98, 0.98, 0.94),
    FLOWER_YELLOW: colour(1.0, 0.83, 0.2),
    FLOWER_PINK: colour(0.97, 0.56, 0.72),
    DIRT: colour(0.5, 0.37, 0.24),
    DIRT_DARK: colour(0.36, 0.25, 0.16),
    CLOUD: colour(1.0, 1.0, 1.0),
    TRUCK_ORANGE: colour(0.98, 0.48, 0.15),
    TRUCK_ORANGE_DARK: colour(0.77, 0.33, 0.1),
    TRUCK_DARK: colour(0.19, 0.21, 0.23),
    TRUCK_STEEL: colour(0.68, 0.69, 0.72, 0.4),
    TRUCK_GLASS: colour(0.58, 0.8, 0.91, 0.1),
}


def signed_power(value, exponent):
    return math.copysign(abs(value) ** exponent, value)


def lump(part, center, radii, material, seed=1, bumps=0.1, rings=8, points=14, chunk=1.0):
    """A bumpy ball. `material` is a name or a function of (ring, point), with ring 0 at the top.
    A `chunk` below 1 squares it off, which makes a boulder out of a blob."""
    rng = random.Random(seed)
    waves = [(rng.randint(2, 4), rng.randint(1, 3), rng.uniform(0, math.tau), rng.uniform(0, math.tau), rng.uniform(0.5, 1.0))
             for _ in range(3)]
    center = Vector(center)
    sections = []
    for i in range(rings + 1):
        polar = math.pi * i / rings
        across = max(math.sin(polar), 0.03)
        ring = []
        for p in range(points):
            angle = math.tau * p / points
            bump = sum(w * math.sin(a * angle + phase) * math.sin(b * polar + shift) for a, b, phase, shift, w in waves)
            scale = 1.0 + bumps * 0.5 * bump * math.sin(polar)
            ring.append(center + Vector((
                radii[0] * across ** chunk * signed_power(math.cos(angle), chunk) * scale,
                radii[1] * across ** chunk * signed_power(math.sin(angle), chunk) * scale,
                radii[2] * signed_power(math.cos(polar), chunk) * scale)))
        sections.append(ring)
    part.loft(sections, material)


def shaded(top, low, split, seed=0, wave=1.1):
    """The top of a lump in one colour and the rest in another, with a wavy line between them."""
    phase = random.Random(seed).uniform(0, math.tau)

    def pick(ring, point):
        return top if ring + wave * math.sin(point * 0.9 + phase) < split else low

    return pick


def add_trunk(part, height, base, top, material, seed=0, lean=(0.0, 0.0), points=12):
    """A trunk whose foot spreads into roots."""
    phase = random.Random(seed).uniform(0, math.tau)
    rings = []
    steps = 7
    for i in range(steps + 1):
        t = i / steps
        radius = top + (base - top) * (1.0 - t) ** 3
        roots = 0.34 * max(0.0, 1.0 - t * 3.2) ** 2
        cx = lean[0] * t * t
        cy = lean[1] * t * t
        ring = []
        for p in range(points):
            angle = math.tau * p / points
            r = radius * (1.0 + roots * math.cos(5 * angle + phase))
            ring.append(Vector((cx + r * math.cos(angle), cy + r * math.sin(angle), height * t - 0.05)))
        rings.append(ring)
    part.loft(rings, material)


def build_round_tree(name, materials, top, low, seed, spread=1.65, height=6.0):
    rng = random.Random(seed)
    part = MeshPart(name)
    add_trunk(part, height * 0.56, 0.42, 0.22, BARK, seed, lean=(rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15)))
    center = Vector((0.0, 0.0, height * 0.66))
    for i in range(2):
        angle = rng.uniform(0, math.tau)
        tip = (math.cos(angle) * 1.0, math.sin(angle) * 1.0, height * 0.66)
        part.tube([(0, 0, height * 0.38), (tip[0] * 0.5, tip[1] * 0.5, height * 0.5), tip], 0.09, BARK, points=6, samples=2)
    lump(part, center, (spread * 0.92, spread * 0.92, spread * 0.82), low, seed, 0.05, rings=6, points=12)
    count = 6
    for i in range(count):
        angle = math.tau * i / count + rng.uniform(-0.22, 0.22)
        reach = spread * rng.uniform(0.62, 0.78)
        size = spread * rng.uniform(0.5, 0.62)
        where = center + Vector((math.cos(angle) * reach, math.sin(angle) * reach, spread * rng.uniform(-0.28, 0.12)))
        lump(part, where, (size, size, size * 0.86), shaded(top, low, 3.4, seed + i), seed + i + 1, 0.12)
    for i in range(3):
        angle = math.tau * i / 3 + rng.uniform(-0.3, 0.3)
        reach = spread * 0.3
        size = spread * rng.uniform(0.52, 0.62)
        where = center + Vector((math.cos(angle) * reach, math.sin(angle) * reach, spread * rng.uniform(0.42, 0.6)))
        lump(part, where, (size, size, size * 0.8), shaded(top, low, 4.8, seed + 10 + i), seed + 11 + i, 0.12)
    part.to_object(materials)


def build_tall_tree(materials):
    part = MeshPart("Tree_Tall")
    add_trunk(part, 3.0, 0.36, 0.17, BARK, 5)
    stack = ((2.5, 1.12), (3.7, 1.22), (4.9, 1.06), (6.0, 0.82), (6.85, 0.48))
    for i, (z, radius) in enumerate(stack):
        for k in range(3 if radius > 0.7 else 1):
            angle = math.tau * k / 3 + i * 1.3
            reach = radius * 0.34 if radius > 0.7 else 0.0
            where = (math.cos(angle) * reach, math.sin(angle) * reach, z)
            size = radius * 0.78 if radius > 0.7 else radius
            lump(part, where, (size, size, size * 1.05), shaded(LEAF_MID, LEAF_DARK, 3.2, i * 5 + k), i * 7 + k, 0.12, rings=7, points=12)
    part.to_object(materials)


def add_pine_tier(part, bottom, top, radius, seed, points=18):
    phase = random.Random(seed).uniform(0, math.tau)
    rings = []
    for t, factor in ((0.0, 0.92), (0.05, 1.0), (0.28, 0.72), (0.6, 0.4), (0.85, 0.16), (1.0, 0.02)):
        ring = []
        for p in range(points):
            angle = math.tau * p / points
            wave = math.sin(6 * angle + phase)
            r = radius * factor * (1.0 + 0.13 * (1.0 - t) * wave)
            z = bottom + (top - bottom) * t - 0.16 * (1.0 - t) ** 2 * (wave * 0.5 + 0.5)
            ring.append(Vector((r * math.cos(angle), r * math.sin(angle), z)))
        rings.append(ring)
    part.loft(rings, lambda ring, point: PINE_DARK if ring < 2 else PINE_LIGHT)


def build_pine(materials):
    part = MeshPart("Tree_Pine")
    add_trunk(part, 2.0, 0.34, 0.2, BARK, 9)
    for i, (bottom, top, radius) in enumerate(((1.15, 3.5, 2.05), (2.45, 4.65, 1.62), (3.65, 5.7, 1.2), (4.75, 6.75, 0.8))):
        add_pine_tier(part, bottom, top, radius, i + 3)
    part.to_object(materials)


def build_bush(name, materials, top, low, seed, flowers=None):
    rng = random.Random(seed)
    part = MeshPart(name)
    lump(part, (0, 0, 0.34), (0.68, 0.62, 0.46), low, seed, 0.08, rings=6, points=12)
    tops = []
    for i in range(4):
        angle = math.tau * i / 4 + rng.uniform(-0.4, 0.4)
        reach = rng.uniform(0.34, 0.46)
        size = rng.uniform(0.36, 0.46)
        where = (math.cos(angle) * reach, math.sin(angle) * reach, rng.uniform(0.34, 0.54))
        tops.append((where, size))
        lump(part, where, (size, size, size * 0.86), shaded(top, low, 3.2, seed + i), seed + i + 1, 0.12, rings=7, points=12)
    if flowers:
        for i in range(12):
            where, size = tops[i % len(tops)]
            angle = rng.uniform(0, math.tau)
            lean = rng.uniform(0.3, 0.75)
            spot = (where[0] + math.cos(angle) * size * lean, where[1] + math.sin(angle) * size * lean,
                    where[2] + size * 0.86 * math.sqrt(1.0 - lean * lean))
            part.ellipsoid(spot, (0.07, 0.07, 0.05), flowers, rings=3, points=6)
    part.to_object(materials)


def build_rock(name, materials, seed, radii=(0.86, 0.72, 0.5), moss=False, pebble=True):
    part = MeshPart(name)
    lump(part, (0, 0, 0.25), radii, shaded(ROCK_LIGHT, ROCK, 3.6, seed, wave=0.45), seed, 0.22, rings=10, points=20, chunk=0.72)
    if pebble:
        lump(part, (0.66, 0.52, 0.06), (0.2, 0.17, 0.14), shaded(ROCK_LIGHT, ROCK, 2.2, seed + 1), seed + 1, 0.2, rings=5, points=10, chunk=0.8)
    if moss:
        lump(part, (-0.12, 0.06, 0.25 + radii[2] * 0.86), (radii[0] * 0.5, radii[1] * 0.46, 0.09), MOSS, seed + 2, 0.3, rings=5, points=12)
    part.to_object(materials)


FENCE_SPAN = 2.4
# For long bars: the same thickness all the way, with only the very ends rounded off.
SQUARE_ENDS = [(0.0, 0.5), (0.008, 0.9), (0.02, 1.0), (0.98, 1.0), (0.992, 0.9), (1.0, 0.5)]


def add_fence_post(part, y):
    part.rounded_box((0, y, 0.52), (0.115, 0.115, 0.58), FENCE, power=4.0, rings=6, points=12)
    part.rounded_box((0, y, 1.12), (0.145, 0.145, 0.055), FENCE_CAP, power=4.0, rings=4, points=12)


def add_fence_rail(part, start, end):
    part.along(start, end, 0.055, 0.085, FENCE, power=4.0, points=12, keys=SQUARE_ENDS)


def build_fence(materials):
    """One post and the rails up to the next one. Rails run along Y."""
    part = MeshPart("Fence_Rail")
    add_fence_post(part, -FENCE_SPAN / 2)
    for z in (0.82, 0.44):
        add_fence_rail(part, (0, -FENCE_SPAN / 2, z), (0, FENCE_SPAN / 2, z))
    part.to_object(materials)


def build_fence_loose(materials):
    """The same, with the lower rail hanging off one nail."""
    part = MeshPart("Fence_Loose")
    add_fence_post(part, -FENCE_SPAN / 2)
    add_fence_rail(part, (0, -FENCE_SPAN / 2, 0.82), (0, FENCE_SPAN / 2, 0.82))
    add_fence_rail(part, (0.02, -FENCE_SPAN / 2, 0.44), (0.05, FENCE_SPAN / 2 - 0.1, 0.13))
    part.to_object(materials)


def build_fence_post(materials):
    part = MeshPart("Fence_Post")
    add_fence_post(part, 0.0)
    part.to_object(materials)


GUARD_SPAN = 4.0
GUARD_HEIGHT = 0.68


def add_guard_post(part, foot, head):
    part.along(foot, head, 0.075, 0.095, GUARD_POST, power=4.0, points=10, up=(1, 0, 0))


def build_guardrail(materials):
    part = MeshPart("Guard_Rail")
    half = GUARD_SPAN / 2
    add_guard_post(part, (0.1, -half, -0.05), (0.1, -half, GUARD_HEIGHT + 0.12))
    add_guard_post(part, (0.1, 0, -0.05), (0.1, 0, GUARD_HEIGHT + 0.12))
    part.along((0, -half, GUARD_HEIGHT), (0, half, GUARD_HEIGHT), 0.085, 0.2, GUARD, power=5.0, points=14, keys=SQUARE_ENDS)
    for side in (-1, 1):
        part.along((side * 0.082, -half, GUARD_HEIGHT), (side * 0.082, half, GUARD_HEIGHT), 0.012, 0.045, GUARD_DARK, power=5.0, points=10, keys=SQUARE_ENDS)
    part.to_object(materials)


def build_guardrail_bent(materials):
    """The piece the truck went through. It starts in line at -Y and is torn away toward -X, the crash side."""
    part = MeshPart("Guard_Bent")
    half = GUARD_SPAN / 2
    add_guard_post(part, (0.1, -half, -0.05), (0.1, -half, GUARD_HEIGHT + 0.12))
    add_guard_post(part, (0.0, -0.3, -0.05), (-0.42, -0.1, GUARD_HEIGHT - 0.04))
    keys = [(-half, 0.0, GUARD_HEIGHT, 0.085, 0.2, 5.0), (-1.1, -0.04, GUARD_HEIGHT, 0.085, 0.2, 5.0),
            (-0.2, -0.36, GUARD_HEIGHT - 0.03, 0.085, 0.19, 5.0), (0.7, -0.98, GUARD_HEIGHT - 0.14, 0.085, 0.17, 5.0),
            (1.5, -1.72, GUARD_HEIGHT - 0.36, 0.085, 0.13, 5.0), (1.75, -1.98, GUARD_HEIGHT - 0.46, 0.06, 0.07, 3.0)]
    part.lengthwise(keys, GUARD, points=14, samples=5)
    flat = Euler((math.radians(4.0), math.radians(80.0), math.radians(40.0)), "XYZ").to_quaternion()
    part.rounded_box((-2.5, 2.3, 0.07), (0.07, 0.62, 0.16), GUARD, power=4.0, rings=3, points=10, rotation=flat)
    part.to_object(materials)


def striped_board(part, z, half_width, half_height, stripes):
    rings = []
    spots = [(-half_width - 0.02, 0.6)]
    spots += [(-half_width + 2 * half_width * i / stripes, 1.0) for i in range(stripes + 1)]
    spots += [(half_width + 0.02, 0.6)]
    for x, size in spots:
        rings.append(modeling.section(Vector((x, 0.0, z)), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.05 * size, half_height * size, 5.0, 10))
    part.loft(rings, lambda ring, point: CONE if ring % 2 == 0 else CONE_BAND)


def build_barrier(materials):
    """A road-closed barricade, as wide as half the road."""
    part = MeshPart("Barrier")
    half = 2.75
    for x in (-half + 0.4, half - 0.4):
        part.along((x, 0, -0.03), (x, 0, 1.28), 0.06, 0.06, FRAME, power=4.0, points=8)
        part.along((x, -0.42, 0.0), (x, 0.0, 0.42), 0.04, 0.04, FRAME, power=4.0, points=8)
        part.along((x, 0.42, 0.0), (x, 0.0, 0.42), 0.04, 0.04, FRAME, power=4.0, points=8)
        part.rounded_box((x, 0, 0.05), (0.09, 0.5, 0.05), FRAME, power=4.0, rings=4, points=10)
        part.ellipsoid((x, 0, 1.39), (0.12, 0.12, 0.13), AMBER, rings=6, points=10)
    striped_board(part, 1.0, half, 0.17, 10)
    striped_board(part, 0.55, half, 0.17, 10)
    part.to_object(materials)


def build_sign(materials):
    """A diamond warning sign with an exclamation mark."""
    part = MeshPart("Sign_Warning")
    part.along((0, 0, -0.05), (0, 0, 2.5), 0.055, 0.055, GUARD_POST, points=10)
    turn = Euler((0.0, math.radians(45.0), 0.0), "XYZ").to_quaternion()
    part.rounded_box((0, 0.05, 2.5), (0.66, 0.025, 0.66), SIGN_DARK, power=7.0, rings=4, points=20, rotation=turn)
    part.rounded_box((0, 0.08, 2.5), (0.58, 0.02, 0.58), SIGN, power=7.0, rings=4, points=20, rotation=turn)
    part.rounded_box((0, 0.11, 2.62), (0.075, 0.012, 0.27), SIGN_DARK, power=3.0, rings=4, points=12)
    part.ellipsoid((0, 0.11, 2.2), (0.085, 0.015, 0.085), SIGN_DARK, rings=4, points=12)
    part.to_object(materials)


POLE_SPAN = 24.0
WIRE_HEIGHT = 8.86


def build_pole(materials):
    """A utility pole with its wires running along +Y to the next pole."""
    part = MeshPart("UtilityPole")
    part.upright([(-0.05, 0.0, 0.2, 0.2, 2.0), (9.3, 0.0, 0.12, 0.12, 2.0)], POLE, points=10, samples=1)
    part.rounded_box((0, 0, 8.6), (1.3, 0.075, 0.075), POLE, power=4.0, rings=4, points=10)
    for x in (-1.12, 0.3, 1.12):
        part.ellipsoid((x, 0, WIRE_HEIGHT - 0.1), (0.075, 0.075, 0.14), INSULATOR, rings=4, points=8)
    part.to_object(materials)

    wires = MeshPart("PlainWires")
    for x in (-1.12, 0.3, 1.12):
        path = [(x, 0.0, WIRE_HEIGHT), (x, POLE_SPAN * 0.5, WIRE_HEIGHT - 1.05), (x, POLE_SPAN, WIRE_HEIGHT)]
        wires.tube(path, 0.024, WIRE, points=4, samples=6)
    wires.to_object(materials)


def build_hay(materials):
    """A round hay bale lying on its side, its axis along X."""
    part = MeshPart("HayBale")
    radius = 0.82
    part.along((-0.76, 0, radius), (0.76, 0, radius), radius, radius, HAY, points=22, up=(0, 1, 0),
               keys=[(0.0, 0.03), (0.0, 0.88), (0.03, 0.97), (0.1, 1.0), (0.9, 1.0), (0.97, 0.97), (1.0, 0.88), (1.0, 0.03)])
    for side in (-1, 1):
        part.along((side * 0.74, 0, radius), (side * 0.785, 0, radius), 0.62, 0.62, HAY_DARK, points=18, up=(0, 1, 0))
        part.along((side * 0.77, 0, radius), (side * 0.81, 0, radius), 0.3, 0.3, HAY, points=14, up=(0, 1, 0))
    for x in (-0.36, 0.36):
        part.along((x - 0.025, 0, radius), (x + 0.025, 0, radius), radius + 0.012, radius + 0.012, TWINE, points=22, up=(0, 1, 0))
    part.to_object(materials)


def rounded_polygon(corners, radius, steps=3):
    """Rounds the corners of a closed outline of (x, z) points and keeps the straight parts flat."""
    outline = []
    count = len(corners)
    for i, corner in enumerate(corners):
        corner = Vector(corner)
        before = (Vector(corners[i - 1]) - corner).normalized()
        after = (Vector(corners[(i + 1) % count]) - corner).normalized()
        outline.append(corner + before * radius * 2.0)
        start = corner + before * radius
        end = corner + after * radius
        for s in range(steps + 1):
            t = s / steps
            outline.append(start.lerp(corner, t).lerp(corner.lerp(end, t), t))
        outline.append(corner + after * radius * 2.0)
    return outline


def extrude_along_y(part, outline, y_start, y_end, material, bevel=0.05):
    """Lofts a closed (x, z) outline along Y, with small bevels so the flat ends and walls stay flat."""
    center = sum(outline, Vector((0.0, 0.0))) / len(outline)

    def ring(y, scale):
        return [Vector((center.x + (p.x - center.x) * scale, y, center.y + (p.y - center.y) * scale)) for p in outline]

    part.loft([ring(y_start, 0.9), ring(y_start, 0.985), ring(y_start + bevel, 1.0),
               ring(y_end - bevel, 1.0), ring(y_end, 0.985), ring(y_end, 0.9)], material)


def build_barn(materials):
    part = MeshPart("Barn")
    width = 4.5
    length = 6.5
    wall = 4.4
    peak = 7.2
    outline = rounded_polygon([(-width, 0.0), (width, 0.0), (width, wall), (0.0, peak), (-width, wall)], 0.1)
    extrude_along_y(part, outline, -length, length, BARN)
    part.rounded_box((0, 0, 0.2), (width + 0.14, length + 0.14, 0.26), STONE, power=7.0, rings=6, points=28)

    slope = math.atan2(peak - wall, width)
    overhang = 0.42
    half_slope = math.hypot(peak - wall, width) / 2 + overhang / 2
    for sign in (-1, 1):
        turn = Euler((0.0, sign * slope, 0.0), "XYZ").to_quaternion()
        down = Vector((sign * math.cos(slope), 0.0, -math.sin(slope)))
        out = Vector((sign * math.sin(slope), 0.0, math.cos(slope)))
        middle = Vector((sign * width / 2, 0.0, (peak + wall) / 2)) + down * (overhang / 2) + out * 0.14
        part.rounded_box(middle, (half_slope, length + 0.42, 0.13), ROOF, power=7.0, rings=6, points=28, rotation=turn)
    part.along((0, -length - 0.42, peak + 0.2), (0, length + 0.42, peak + 0.2), 0.15, 0.13, TRIM, points=10, up=(1, 0, 0))

    # The front gable: painted edges, a big double door and the hayloft.
    front = length + 0.02
    for sign in (-1, 1):
        part.along((sign * width, front, 0.3), (sign * width, front, wall), 0.1, 0.05, TRIM, power=4.0, points=8, up=(0, 1, 0))
        part.along((sign * width, front, wall), (0, front, peak), 0.1, 0.05, TRIM, power=4.0, points=8, up=(0, 1, 0))
    part.rounded_box((0, front + 0.01, 1.95), (2.15, 0.035, 1.92), TRIM, power=7.0, rings=4, points=20)
    for sign in (-1, 1):
        x = sign * 1.03
        part.rounded_box((x, front + 0.04, 1.9), (0.96, 0.03, 1.76), BARN_DARK, power=7.0, rings=4, points=20)
        part.along((x - 0.84, front + 0.07, 0.3), (x + 0.84, front + 0.07, 3.5), 0.085, 0.03, TRIM, power=4.0, points=8, up=(0, 1, 0))
        part.along((x + 0.84, front + 0.07, 0.3), (x - 0.84, front + 0.07, 3.5), 0.085, 0.03, TRIM, power=4.0, points=8, up=(0, 1, 0))
    part.rounded_box((0, front + 0.01, 5.3), (0.84, 0.035, 0.76), TRIM, power=7.0, rings=4, points=20)
    part.rounded_box((0, front + 0.04, 5.3), (0.66, 0.03, 0.58), HOLE, power=7.0, rings=4, points=20)

    # Windows down both sides.
    for sign in (-1, 1):
        for y in (-3.6, 0.0, 3.6):
            part.rounded_box((sign * (width + 0.02), y, 2.65), (0.035, 0.66, 0.66), TRIM, power=7.0, rings=4, points=20)
            part.rounded_box((sign * (width + 0.05), y, 2.65), (0.03, 0.5, 0.5), HOLE, power=7.0, rings=4, points=20)
            part.rounded_box((sign * (width + 0.075), y, 2.65), (0.015, 0.5, 0.045), TRIM, power=4.0, rings=3, points=10)
            part.rounded_box((sign * (width + 0.075), y, 2.65), (0.015, 0.045, 0.5), TRIM, power=4.0, rings=3, points=10)

    # A little vent on the ridge.
    part.rounded_box((0, 0, peak + 0.62), (0.62, 0.62, 0.52), TRIM, power=6.0, rings=8, points=20)
    part.rounded_box((0, 0.63, peak + 0.62), (0.32, 0.02, 0.32), HOLE, power=6.0, rings=4, points=14)
    part.upright([(peak + 1.1, 0.0, 0.95, 0.95, 4.0), (peak + 1.3, 0.0, 0.62, 0.62, 4.0), (peak + 1.75, 0.0, 0.03, 0.03, 4.0)], ROOF, points=16, samples=2)
    part.to_object(materials)


def build_silo(materials):
    part = MeshPart("Silo")
    radius = 2.0
    height = 9.2
    part.upright([(-0.05, 0.0, radius + 0.1, radius + 0.1, 2.0), (0.25, 0.0, radius + 0.1, radius + 0.1, 2.0),
                  (0.3, 0.0, radius, radius, 2.0), (height, 0.0, radius, radius, 2.0)], SILO, points=28, samples=1)
    rings = []
    for t, size in ((0.0, 1.0), (0.08, 1.07), (0.16, 1.04), (0.4, 0.92), (0.65, 0.68), (0.85, 0.36), (1.0, 0.02)):
        z = height + t * 2.1
        rings.append(modeling.section(Vector((0, 0, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), radius * size, radius * size, 2.0, 28))
    part.loft(rings, SILO_CAP)
    for z in (1.6, 3.2, 4.8, 6.4, 8.0):
        part.along((0, 0, z - 0.05), (0, 0, z + 0.05), radius + 0.035, radius + 0.035, SILO_BAND, points=28, up=(1, 0, 0))
    part.tube([(0.2, -radius - 0.08, 0.3), (0.2, -radius - 0.08, height)], 0.03, SILO_BAND, points=6, samples=1, smooth=False)
    part.tube([(-0.2, -radius - 0.08, 0.3), (-0.2, -radius - 0.08, height)], 0.03, SILO_BAND, points=6, samples=1, smooth=False)
    part.to_object(materials)


def add_text(name, body, center, width, material, materials):
    """Text that reads correctly from +Y, measured so it fills `width`. Flat text gets no outline."""
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = body
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.extrude = 0.012
    curve.offset = 0.014
    holder = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(holder)
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(holder.evaluated_get(graph))
    measured = max(v.co.x for v in mesh.vertices) - min(v.co.x for v in mesh.vertices)
    bpy.data.objects.remove(holder)
    bpy.data.curves.remove(curve)
    mesh.transform(Euler((math.radians(90.0), 0.0, math.radians(180.0)), "XYZ").to_matrix().to_4x4() @ Matrix.Scale(width / measured, 4))
    mesh.materials.append(materials[material])
    text = bpy.data.objects.new(name, mesh)
    text.location = center
    bpy.context.scene.collection.objects.link(text)


def build_billboard(name, headline, strap, materials):
    part = MeshPart(name)
    for x in (-3.3, 3.3):
        part.along((x, -0.02, -0.05), (x, -0.02, 6.5), 0.17, 0.17, BOARD_WOOD, points=10)
        part.along((x, -1.7, -0.05), (x, -0.08, 3.4), 0.09, 0.09, BOARD_WOOD, points=8)
    part.rounded_box((0, 0.16, 4.72), (4.95, 0.07, 2.02), BOARD_FRAME, power=8.0, rings=6, points=28)
    part.rounded_box((0, 0.22, 4.72), (4.72, 0.05, 1.78), BOARD, power=9.0, rings=4, points=28)
    part.rounded_box((0, 0.27, 3.42), (4.62, 0.02, 0.42), BOARD_ACCENT, power=9.0, rings=3, points=24)
    part.to_object(materials)
    add_text("PlainHeadline", headline, (0.0, 0.3, 5.05), 8.5, BOARD_TEXT, materials)
    add_text("PlainStrap", strap, (0.0, 0.31, 3.42), 3.6, BOARD, materials)


def add_blade(part, base, lean, height, width, material):
    """A curved, pointed blade of grass."""
    base = Vector(base)
    lean = Vector((lean[0], lean[1], 0.0))
    side = Vector((-lean.y, lean.x, 0.0)).normalized()
    forward = Vector((lean.x, lean.y, 0.0)).normalized()
    rings = []
    for t, size in ((0.0, 1.0), (0.55, 0.72), (1.0, 0.05)):
        middle = base + lean * (t * t) + Vector((0.0, 0.0, height * t))
        rings.append([middle + side * width * size, middle - side * width * size, middle - forward * width * 0.5 * size])
    part.loft(rings, material)


def build_tuft(name, materials, seed):
    rng = random.Random(seed)
    part = MeshPart(name)
    for i in range(8):
        angle = rng.uniform(0, math.tau)
        reach = rng.uniform(0.02, 0.17)
        base = (math.cos(angle) * reach, math.sin(angle) * reach, -0.01)
        lean = (math.cos(angle) * rng.uniform(0.08, 0.2), math.sin(angle) * rng.uniform(0.08, 0.2))
        add_blade(part, base, lean, rng.uniform(0.26, 0.46), 0.042, TUFT_LIGHT if i % 3 else TUFT_DARK)
    part.to_object(materials)


def build_flowers(name, materials, petal, seed):
    rng = random.Random(seed)
    part = MeshPart(name)
    for i in range(5):
        angle = rng.uniform(0, math.tau)
        reach = rng.uniform(0.05, 0.3)
        x, y = math.cos(angle) * reach, math.sin(angle) * reach
        z = rng.uniform(0.2, 0.34)
        add_blade(part, (x, y, -0.01), (rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03)), z, 0.018, TUFT_DARK)
        part.ellipsoid((x, y, z), (0.085, 0.085, 0.04), petal, rings=3, points=7)
        part.ellipsoid((x, y, z + 0.025), (0.03, 0.03, 0.025), FLOWER_YELLOW if petal != FLOWER_YELLOW else BARK, rings=3, points=5)
    part.to_object(materials)


def build_debris_panel(materials):
    """A bent piece of the cargo box, lying where it came off."""
    part = MeshPart("Debris_Panel")
    keys = [(-0.56, 0.0, 0.08, 0.38, 0.025, 5.0), (-0.16, 0.01, 0.035, 0.41, 0.025, 5.0),
            (0.24, 0.0, 0.07, 0.4, 0.025, 5.0), (0.58, 0.04, 0.2, 0.34, 0.025, 5.0)]
    part.lengthwise(keys, TRUCK_ORANGE_DARK, points=14, samples=3)
    for y, z in ((-0.36, 0.085), (0.02, 0.075), (0.36, 0.125)):
        part.along((-0.34, y, z), (0.34, y, z), 0.05, 0.022, TRUCK_ORANGE, power=4.0, points=8, up=(0, 1, 0))
    part.to_object(materials)


def build_debris_bits(materials):
    """Small dark scraps and bits of windscreen, too flat to look like something to pick up."""
    rng = random.Random(14)
    part = MeshPart("Debris_Bits")
    for i in range(11):
        angle = rng.uniform(0, math.tau)
        reach = rng.uniform(0.1, 1.5)
        where = (math.cos(angle) * reach, math.sin(angle) * reach, 0.03)
        size = rng.uniform(0.06, 0.14)
        material = (TRUCK_DARK, TRUCK_STEEL, TRUCK_GLASS)[i % 3]
        lump(part, where, (size, size * rng.uniform(0.6, 1.0), 0.035), material, i + 3, 0.3, rings=4, points=7, chunk=0.7)
    part.to_object(materials)


def build_dirt(materials):
    """Soil the truck's bare axle ploughed up."""
    part = MeshPart("DirtMound")
    lump(part, (0, 0, 0.0), (1.05, 0.72, 0.36), shaded(DIRT, DIRT_DARK, 3.4, 2), 3, 0.28, rings=8, points=16)
    lump(part, (0.62, 0.34, 0.0), (0.46, 0.4, 0.24), shaded(DIRT, DIRT_DARK, 2.6, 5), 6, 0.3, rings=6, points=12)
    lump(part, (-0.66, -0.26, 0.0), (0.42, 0.36, 0.2), shaded(DIRT, DIRT_DARK, 2.6, 7), 8, 0.3, rings=6, points=12)
    part.to_object(materials)


def build_cloud(name, materials, seed):
    rng = random.Random(seed)
    part = MeshPart(name)
    lump(part, (0, 0, 0), (16.0, 8.0, 5.0), CLOUD, seed, 0.06, rings=7, points=16)
    for i in range(6):
        x = -10.0 + 4.0 * i + rng.uniform(-1.0, 1.0)
        size = rng.uniform(4.0, 6.2) * (1.0 - abs(x) / 24.0)
        lump(part, (x, rng.uniform(-2.0, 2.0), rng.uniform(1.0, 3.2)), (size * 1.3, size, size * 0.9), CLOUD, seed + i + 1, 0.08, rings=7, points=14)
    part.to_object(materials)


MODELS = {
    "Tree_RoundA": lambda m: build_round_tree("Tree_RoundA", m, LEAF_MID, LEAF_DARK, 21),
    "Tree_RoundB": lambda m: build_round_tree("Tree_RoundB", m, LEAF_LIGHT, LEAF_MID, 34),
    "Tree_RoundC": lambda m: build_round_tree("Tree_RoundC", m, LEAF_YELLOW, LEAF_LIGHT, 47),
    "Tree_Tall": build_tall_tree,
    "Tree_Pine": build_pine,
    "Bush_Green": lambda m: build_bush("Bush_Green", m, LEAF_LIGHT, LEAF_MID, 3),
    "Bush_Dark": lambda m: build_bush("Bush_Dark", m, LEAF_MID, LEAF_DARK, 8),
    "Bush_Flower": lambda m: build_bush("Bush_Flower", m, LEAF_MID, LEAF_DARK, 13, FLOWER_PINK),
    "Rock_A": lambda m: build_rock("Rock_A", m, 5),
    "Rock_B": lambda m: build_rock("Rock_B", m, 17, radii=(0.86, 0.68, 0.56), moss=True),
    "Rock_C": lambda m: build_rock("Rock_C", m, 29, radii=(0.84, 0.74, 0.46), pebble=False),
    "Fence_Rail": build_fence,
    "Fence_Loose": build_fence_loose,
    "Fence_Post": build_fence_post,
    "Guard_Rail": build_guardrail,
    "Guard_Bent": build_guardrail_bent,
    "Barrier": build_barrier,
    "Sign_Warning": build_sign,
    "UtilityPole": build_pole,
    "HayBale": build_hay,
    "Barn": build_barn,
    "Silo": build_silo,
    "Billboard_Time": lambda m: build_billboard("Billboard_Time", "ALWAYS ON TIME!", "CARGO CO.", m),
    "Billboard_Safe": lambda m: build_billboard("Billboard_Safe", "DRIVE SAFELY!", "CARGO CO.", m),
    "Tuft_A": lambda m: build_tuft("Tuft_A", m, 2),
    "Tuft_B": lambda m: build_tuft("Tuft_B", m, 6),
    "Flowers_White": lambda m: build_flowers("Flowers_White", m, FLOWER_WHITE, 1),
    "Flowers_Yellow": lambda m: build_flowers("Flowers_Yellow", m, FLOWER_YELLOW, 4),
    "Flowers_Pink": lambda m: build_flowers("Flowers_Pink", m, FLOWER_PINK, 9),
    "Debris_Panel": build_debris_panel,
    "Debris_Bits": build_debris_bits,
    "DirtMound": build_dirt,
    "Cloud_A": lambda m: build_cloud("Cloud_A", m, 3),
    "Cloud_B": lambda m: build_cloud("Cloud_B", m, 12),
}


def render_check(path):
    bpy.context.view_layer.update()
    corners = []
    for item in bpy.context.scene.objects:
        if item.type == "MESH":
            corners += [item.matrix_world @ Vector(corner) for corner in item.bound_box]
    low = Vector(min(c[i] for c in corners) for i in range(3))
    high = Vector(max(c[i] for c in corners) for i in range(3))
    modeling.setup_preview(target=(low + high) / 2, distance=(high - low).length * 1.55, angle=(155.0, 38.0), size=(360, 280), samples=10)
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def main():
    root = Path(__file__).resolve().parents[2]
    folder = None
    if "--sheet" in sys.argv:
        folder = Path(sys.argv[sys.argv.index("--sheet") + 1])
        folder.mkdir(parents=True, exist_ok=True)
    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1].split(",")

    for name, build in MODELS.items():
        if only and name not in only:
            continue
        modeling.reset_scene()
        materials = modeling.make_materials(PALETTE)
        build(materials)
        output = root / "Assets/Game/Models" / f"{name}.fbx"
        modeling.export_fbx(output)
        triangles = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.context.scene.objects if o.type == "MESH")
        print(f"Wrote {output.name}: {triangles} triangles")
        if folder:
            render_check(folder / f"{name}.png")


main()

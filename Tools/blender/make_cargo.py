"""Builds the three cargo models and writes Assets/Game/Models/Cargo_Small.fbx, _Medium and _Large.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_cargo.py

Add `-- --sheet folder` at the end to also render check images. Each model is centred on its origin
and fits the box collider in `CargoClashBuilder`. Its eyes and mouths are separate objects, so the
game can change the expression.

Each type also gets two more files. `_Damaged` holds the bodies that replace `Body` as it gets hit
(`Body_D1`, then `Body_D2`). `_Pieces` is what's left when it breaks: separate objects that start
where they were on the whole model and get thrown apart by physics.
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

CARDBOARD = "Cargo_Cardboard"
CARDBOARD_DARK = "Cargo_CardboardDark"
TAPE = "Cargo_Tape"
LABEL = "Cargo_Label"
STICKER = "Cargo_Sticker"
CRATE = "Cargo_Crate"
CRATE_FRAME = "Cargo_CrateFrame"
STEEL = "Cargo_Steel"
CHEST = "Cargo_Chest"
CHEST_DARK = "Cargo_ChestDark"
GOLD = "Cargo_Gold"
INSIDE = "Cargo_Inside"
STRAW = "Cargo_Straw"
WHITE = "Eye_White"
BLACK = "Eye_Black"

PALETTE = {
    CARDBOARD: (0.62, 0.38, 0.16, 0.8),
    CARDBOARD_DARK: (0.42, 0.24, 0.09, 0.8),
    TAPE: (0.88, 0.76, 0.48, 0.3),
    LABEL: (0.92, 0.92, 0.88, 0.5),
    STICKER: (0.75, 0.06, 0.05, 0.4),
    CRATE: (0.03, 0.42, 0.34, 0.5),
    CRATE_FRAME: (0.01, 0.2, 0.17, 0.5),
    STEEL: (0.42, 0.44, 0.48, 0.3),
    CHEST: (0.2, 0.06, 0.42, 0.35),
    CHEST_DARK: (0.09, 0.02, 0.2, 0.4),
    GOLD: (1.0, 0.62, 0.05, 0.2),
    INSIDE: (0.05, 0.03, 0.03, 0.9),
    STRAW: (0.85, 0.66, 0.25, 0.8),
    WHITE: (0.95, 0.95, 0.95, 0.2),
    BLACK: (0.01, 0.01, 0.015, 0.1),
}

# Which side of a box something is on, as (the way it faces, and two directions along that side).
FACES = {
    "top": (Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0))),
    "left": (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))),
    "right": (Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))),
    "front": (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
    "back": (Vector((0, 1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
}


def add_eye(materials, name, eye_radius, location, x_sign, sign):
    eye = MeshPart(name)
    eye.ellipsoid((0, 0, 0), (eye_radius, eye_radius * 0.34, eye_radius * 1.14), WHITE, rings=10, points=18)
    eye.ellipsoid((-x_sign * sign * -eye_radius * 0.08, sign * eye_radius * 0.3, -eye_radius * 0.12),
                  (eye_radius * 0.55, eye_radius * 0.14, eye_radius * 0.62), BLACK, rings=8, points=14)
    return eye.to_object(materials, location=location)


def add_face(materials, half, eye_radius, eye_x, eye_z, mouth_z, mouth_size, brow=False):
    """A face on the front and on the back, so one is usually in view however the box lands."""
    for side_name, sign in (("F", -1.0), ("B", 1.0)):
        y = sign * half[1]
        for eye_name, x_sign in (("L", 1.0), ("R", -1.0)):
            add_eye(materials, f"Eye_{side_name}_{eye_name}", eye_radius, (x_sign * eye_x, y, eye_z), x_sign, sign)
        mouth = MeshPart(f"Mouth_{side_name}")
        mouth.ellipsoid((0, 0, 0), (mouth_size, eye_radius * 0.2, mouth_size * 0.3), BLACK, rings=8, points=14)
        mouth.to_object(materials, location=(0, y, mouth_z))


# ---- Damage: ways of bending a finished shape, and things to add to it ----

def dent(center, radius, depth):
    """Pushes the surface in around a point, toward the middle of the box."""
    center = Vector(center)
    inward = -center.normalized()

    def move(co):
        distance = (co - center).length
        if distance >= radius:
            return co
        falloff = 1.0 - (distance / radius) ** 2
        return co + inward * depth * falloff * falloff

    return move


def crumple(amount, seed, frequency=11.0):
    """Lumpy creases all over, from a few overlapping waves."""
    rng = random.Random(seed)
    waves = []
    for _ in range(5):
        direction = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
        waves.append((direction * frequency * rng.uniform(0.7, 1.4), rng.uniform(0.0, math.tau)))

    def move(co):
        wave = sum(math.sin(co.dot(k) + phase) for k, phase in waves) / len(waves)
        return co + co.normalized() * amount * wave

    return move


def shear(amount_x, amount_y=0.0):
    """Leans the whole box over, the top moving more than the bottom."""

    def move(co):
        return Vector((co.x + co.z * amount_x, co.y + co.z * amount_y, co.z))

    return move


def jagged_ring(center, u, v, radius_u, radius_v, rng, jag, points):
    ring = []
    for i in range(points):
        angle = math.tau * i / points
        scale = 1.0 + rng.uniform(-jag, jag)
        ring.append(center + u * (math.cos(angle) * radius_u * scale) + v * (math.sin(angle) * radius_v * scale))
    return ring


def add_tear(part, half, face, offset, radius, rng, material=INSIDE, jag=0.3):
    """A ragged dark hole on one side of a box. `offset` is where, from -1 to 1 along that side."""
    normal, u, v = FACES[face]
    half_vector = Vector(half)
    center = Vector((normal.x * half_vector.x, normal.y * half_vector.y, normal.z * half_vector.z))
    center += u * (offset[0] * abs(u.dot(half_vector))) + v * (offset[1] * abs(v.dot(half_vector)))
    outer = jagged_ring(center + normal * 0.006, u, v, radius[0], radius[1], rng, jag, 16)
    under = [p - normal * 0.03 for p in outer]
    part.loft([under, outer], material)


def add_crack(part, half, face, start, heading, length, rng, width=0.012, material=INSIDE, steps=6):
    """A zigzag line across one side of a box, with a split branching off it."""
    normal, u, v = FACES[face]
    half_vector = Vector(half)
    size_u = abs(u.dot(half_vector))
    size_v = abs(v.dot(half_vector))
    origin = Vector((normal.x * half_vector.x, normal.y * half_vector.y, normal.z * half_vector.z)) + normal * 0.002

    def line(from_uv, angle, reach, count, radius):
        points = [origin + u * from_uv[0] + v * from_uv[1]]
        position = list(from_uv)
        for i in range(count):
            angle_now = angle + rng.uniform(-0.8, 0.8)
            step = reach / count
            position[0] = max(-size_u * 0.96, min(size_u * 0.96, position[0] + math.cos(angle_now) * step))
            position[1] = max(-size_v * 0.96, min(size_v * 0.96, position[1] + math.sin(angle_now) * step))
            points.append(origin + u * position[0] + v * position[1])
        part.tube(points, radius, material, points=6, smooth=False)
        return position

    middle = line((start[0] * size_u, start[1] * size_v), heading, length * 0.55, steps // 2 + 1, width)
    end = line(middle, heading + rng.uniform(-0.3, 0.3), length * 0.45, steps // 2, width * 0.75)
    line(middle, heading + rng.choice((-1.0, 1.0)) * rng.uniform(0.7, 1.1), length * 0.3, 2, width * 0.6)
    return end


def add_flap(part, position, half, angles, material):
    """A loose bit of board or card sticking out of a hole."""
    rotation = Euler(tuple(math.radians(a) for a in angles), "XYZ").to_quaternion()
    part.rounded_box(position, half, material, power=5.0, rings=6, points=16, rotation=rotation)


def add_splinter(part, start, end, radius, material):
    """A broken end of wood: thick where it's attached, coming to a point."""
    part.along(start, end, radius, radius * 0.6, material, power=3.0, points=8,
               keys=[(0.0, 1.0), (0.45, 0.8), (1.0, 0.06)])


# ---- Small: a cardboard parcel ----

def small_body(name, stage):
    half = (0.3, 0.3, 0.3)
    fine = stage > 0
    rings, points = (26, 64) if fine else (14, 36)
    body = MeshPart(name)
    body.rounded_box((0, 0, 0), half, CARDBOARD, power=7.0, rings=rings, points=points)
    # A seam around the lid, tape over it, and a shipping label on top.
    body.rounded_box((0, 0, 0.14), (0.303, 0.303, 0.012), CARDBOARD_DARK, power=7.0, rings=6, points=points)
    body.rounded_box((0, 0, 0), (0.075, 0.305, 0.305), TAPE, power=7.0, rings=rings, points=points)
    body.rounded_box((0, 0, 0), (0.305, 0.075, 0.305), TAPE, power=7.0, rings=rings, points=points)
    body.rounded_box((0.165, -0.165, 0.303), (0.095, 0.075, 0.006), LABEL, power=5.0, rings=6, points=20)
    body.rounded_box((-0.17, 0.17, 0.303), (0.06, 0.06, 0.006), STICKER, power=3.0, rings=6, points=20)

    rng = random.Random(10 + stage)
    if stage == 1:
        add_crack(body, half, "top", (-0.5, 0.45), -0.6, 0.34, rng)
        add_crack(body, half, "left", (-0.2, 0.7), -1.4, 0.3, rng)
        add_flap(body, (0.2, -0.3, 0.2), (0.07, 0.008, 0.05), (28, 0, 14), TAPE)
        body.displace(dent((0.24, -0.26, 0.3), 0.3, 0.08))
        body.displace(dent((-0.3, 0.2, -0.1), 0.24, 0.05))
    elif stage == 2:
        add_tear(body, half, "top", (-0.15, 0.2), (0.15, 0.13), rng)
        add_tear(body, half, "left", (0.3, 0.35), (0.11, 0.09), rng)
        add_tear(body, half, "right", (-0.35, -0.3), (0.08, 0.1), rng)
        add_crack(body, half, "top", (0.6, -0.6), 2.3, 0.4, rng, width=0.014)
        add_crack(body, half, "left", (-0.6, -0.5), 0.5, 0.34, rng, width=0.014)
        add_crack(body, half, "right", (0.5, 0.7), -2.0, 0.34, rng, width=0.014)
        add_flap(body, (-0.12, 0.26, 0.32), (0.13, 0.008, 0.09), (-38, 8, 10), CARDBOARD)
        add_flap(body, (0.1, 0.0, 0.33), (0.1, 0.008, 0.07), (42, -14, -22), CARDBOARD_DARK)
        add_flap(body, (0.31, -0.2, -0.02), (0.008, 0.07, 0.05), (10, 24, 16), TAPE)
        body.displace(dent((-0.05, 0.1, 0.36), 0.36, 0.17))
        body.displace(dent((0.28, -0.28, 0.3), 0.3, 0.11))
        body.displace(dent((-0.32, 0.25, -0.15), 0.28, 0.09))
        body.displace(crumple(0.014, 3))
    return body


def build_small(materials):
    half = (0.3, 0.3, 0.3)
    small_body("Body", 0).to_object(materials)
    add_face(materials, half, eye_radius=0.075, eye_x=0.135, eye_z=0.03, mouth_z=-0.13, mouth_size=0.05)
    return half


def build_small_damaged(materials):
    small_body("Body_D1", 1).to_object(materials)
    small_body("Body_D2", 2).to_object(materials)


def shard_ring(rng, half_u, half_v, jag, points=14):
    """The outline of a torn piece, around a rounded rectangle."""
    ring = []
    for i in range(points):
        angle = math.tau * i / points
        c, s = math.cos(angle), math.sin(angle)
        a = math.copysign(abs(c) ** 0.45, c) * half_u
        b = math.copysign(abs(s) ** 0.45, s) * half_v
        scale = 1.0 + rng.uniform(-jag, jag)
        ring.append(Vector((a * scale, b * scale, 0.0)))
    return ring


def add_shard(materials, name, half_u, half_v, thickness, material, rng, location, turn=(0, 0, 0), stripe=None, jag=0.16):
    """A torn flat piece. It's built lying flat around its own origin, then turned to fit where it came from."""
    part = MeshPart(name)
    rotation = Euler(tuple(math.radians(a) for a in turn), "XYZ").to_quaternion()
    ring = shard_ring(rng, half_u, half_v, jag)
    bottom = [rotation @ (p - Vector((0, 0, thickness))) for p in ring]
    top = [rotation @ (p + Vector((0, 0, thickness))) for p in ring]
    part.loft([bottom, top], material)
    if stripe is not None:
        part.rounded_box((0, 0, 0), (half_u * 0.3, half_v * 0.92, thickness * 1.5), stripe, power=6.0, rings=4, points=12, rotation=rotation)
    return part.to_object(materials, location=location)


def add_eye_pieces(materials, half, eye_radius, eye_x, eye_z):
    add_eye(materials, "Eye_L", eye_radius, (eye_x, -half[1], eye_z), 1.0, -1.0)
    add_eye(materials, "Eye_R", eye_radius, (-eye_x, -half[1], eye_z), -1.0, -1.0)


def add_bits(materials, prefix, count, spread, radius, material, rng):
    """Small lumps that were inside: packing foam, straw and so on."""
    for i in range(count):
        part = MeshPart(f"{prefix}_{i}")
        size = radius * rng.uniform(0.7, 1.3)
        part.ellipsoid((0, 0, 0), (size * 1.25, size, size * 0.7), material, rings=6, points=10)
        location = (rng.uniform(-spread, spread), rng.uniform(-spread, spread), rng.uniform(-spread, spread))
        part.to_object(materials, location=location)


def build_small_pieces(materials):
    rng = random.Random(21)
    half = 0.3
    add_shard(materials, "Panel_Top", 0.27, 0.27, 0.012, CARDBOARD, rng, (0, 0, half), stripe=TAPE)
    add_shard(materials, "Panel_Bottom", 0.27, 0.27, 0.012, CARDBOARD_DARK, rng, (0, 0, -half))
    add_shard(materials, "Panel_Left", 0.27, 0.27, 0.012, CARDBOARD, rng, (half, 0, 0), (0, 90, 0), stripe=TAPE)
    add_shard(materials, "Panel_Right", 0.27, 0.27, 0.012, CARDBOARD, rng, (-half, 0, 0), (0, 90, 0), stripe=TAPE)
    add_shard(materials, "Panel_Front", 0.27, 0.27, 0.012, CARDBOARD, rng, (0, -half, 0), (90, 0, 0))
    add_shard(materials, "Panel_Back", 0.27, 0.27, 0.012, CARDBOARD, rng, (0, half, 0), (90, 0, 0))
    add_shard(materials, "Scrap_0", 0.11, 0.08, 0.01, CARDBOARD_DARK, rng, (0.1, 0.1, 0.1), (20, 40, 0))
    add_shard(materials, "Scrap_1", 0.09, 0.1, 0.01, LABEL, rng, (-0.1, -0.05, 0.15), (60, 10, 0))
    add_eye_pieces(materials, (half, half, half), 0.075, 0.135, 0.03)
    add_bits(materials, "Foam", 6, 0.2, 0.045, LABEL, rng)


# ---- Medium: a teal crate ----

def medium_body(name, stage):
    half = (0.52, 0.46, 0.46)
    hx, hy, hz = half
    fine = stage > 0
    body = MeshPart(name)
    body.rounded_box((0, 0, 0), (hx - 0.012, hy - 0.012, hz - 0.012), CRATE, power=9.0, rings=26 if fine else 18, points=72 if fine else 44)

    # A timber frame along all twelve edges, with steel brackets on the corners.
    beam = 0.062
    x, y, z = hx - beam, hy - beam, hz - beam
    for a in (-1, 1):
        for b in (-1, 1):
            body.rounded_box((0, a * y, b * z), (hx, beam, beam), CRATE_FRAME, power=5.0, rings=8, points=16)
            body.rounded_box((a * x, 0, b * z), (beam, hy, beam), CRATE_FRAME, power=5.0, rings=8, points=16)
            body.rounded_box((a * x, b * y, 0), (beam, beam, hz), CRATE_FRAME, power=5.0, rings=8, points=16)
            for c in (-1, 1):
                if stage == 2 and a == 1 and b == -1 and c == 1:
                    continue
                body.rounded_box((a * (hx - 0.05), b * (hy - 0.05), c * (hz - 0.05)), (0.075, 0.075, 0.075), STEEL,
                                 power=4.0, rings=6, points=14)
    # Diagonal braces on the top and ends, which are the sides without a face.
    brace = (0.44, 0.042, 0.022)
    top = Euler((0.0, 0.0, math.atan2(hy, hx)), "XYZ").to_quaternion()
    if stage < 2:
        body.rounded_box((0, 0, hz - 0.012), brace, CRATE_FRAME, power=5.0, rings=6, points=16, rotation=top)
    for sign in (-1, 1):
        end = Euler((math.atan2(hz, hy) + math.pi / 2, 0.0, 0.0), "XYZ").to_quaternion()
        if stage < 2 or sign == -1:
            body.rounded_box((sign * (hx - 0.012), 0, 0), (0.022, 0.042, 0.40), CRATE_FRAME, power=5.0, rings=6, points=16, rotation=end)

    rng = random.Random(30 + stage)
    if stage == 1:
        add_crack(body, half, "top", (-0.6, 0.6), -0.7, 0.56, rng, width=0.016, material=CRATE_FRAME)
        add_crack(body, half, "left", (0.5, 0.6), -2.1, 0.46, rng, width=0.016, material=CRATE_FRAME)
        add_crack(body, half, "front", (-0.75, -0.7), 0.5, 0.3, rng, width=0.013, material=CRATE_FRAME)
        add_tear(body, half, "top", (0.35, -0.25), (0.13, 0.07), rng, jag=0.34)
        add_splinter(body, (0.42, -0.2, 0.46), (0.5, -0.32, 0.6), 0.03, CRATE)
        body.displace(dent((0.5, 0.3, 0.46), 0.42, 0.06))
    elif stage == 2:
        add_tear(body, half, "top", (0.05, 0.1), (0.3, 0.24), rng, jag=0.26)
        add_tear(body, half, "left", (-0.2, 0.1), (0.2, 0.22), rng, jag=0.28)
        add_tear(body, half, "right", (0.3, -0.2), (0.14, 0.16), rng, jag=0.3)
        add_crack(body, half, "front", (-0.8, 0.7), -0.7, 0.42, rng, width=0.018, material=CRATE_FRAME)
        add_crack(body, half, "back", (0.8, 0.7), -2.3, 0.42, rng, width=0.018, material=CRATE_FRAME)
        for start, end_point in (
            ((-0.2, -0.16, 0.46), (-0.34, -0.26, 0.66)),
            ((0.12, 0.26, 0.46), (0.3, 0.32, 0.68)),
            ((0.26, -0.1, 0.46), (0.44, -0.12, 0.58)),
            ((0.52, 0.2, 0.0), (0.72, 0.3, 0.12)),
            ((0.52, -0.2, 0.2), (0.7, -0.26, 0.3)),
        ):
            add_splinter(body, start, end_point, 0.038, CRATE)
        add_flap(body, (-0.05, -0.18, 0.5), (0.2, 0.04, 0.014), (0, 24, 18), CRATE_FRAME)
        for i in range(7):
            angle = i * 0.9
            add_splinter(body, (0.2 * math.cos(angle), 0.1 + 0.15 * math.sin(angle), 0.44),
                         (0.3 * math.cos(angle), 0.1 + 0.26 * math.sin(angle), 0.6 + 0.04 * (i % 3)), 0.012, STRAW)
        body.displace(dent((0.4, 0.34, 0.5), 0.5, 0.12))
        body.displace(dent((-0.54, -0.2, 0.1), 0.36, 0.08))
        body.displace(shear(0.1, -0.05))
    return body


def build_medium(materials):
    half = (0.52, 0.46, 0.46)
    medium_body("Body", 0).to_object(materials)
    add_face(materials, half, eye_radius=0.105, eye_x=0.2, eye_z=0.06, mouth_z=-0.19, mouth_size=0.08)
    return half


def build_medium_damaged(materials):
    medium_body("Body_D1", 1).to_object(materials)
    medium_body("Body_D2", 2).to_object(materials)


def add_beam(materials, name, half, location, turn, material, rng):
    part = MeshPart(name)
    rotation = Euler(tuple(math.radians(a) for a in turn), "XYZ").to_quaternion()
    part.rounded_box((0, 0, 0), half, material, power=5.0, rings=6, points=14, rotation=rotation)
    # Snapped ends.
    tip = rotation @ Vector((half[0], 0, 0))
    part.along(tuple(tip), tuple(tip * 1.18 + rotation @ Vector((0, rng.uniform(-0.02, 0.02), rng.uniform(-0.02, 0.02)))),
               half[1] * 0.8, half[2] * 0.8, material, power=3.0, points=8, keys=[(0.0, 1.0), (1.0, 0.1)])
    return part.to_object(materials, location=location)


def build_medium_pieces(materials):
    rng = random.Random(41)
    hx, hy, hz = 0.52, 0.46, 0.46
    # Boards from each side. The face is on the front and back, so those tear across the middle.
    add_shard(materials, "Board_Top_A", 0.46, 0.2, 0.02, CRATE, rng, (0, -0.2, hz), jag=0.1)
    add_shard(materials, "Board_Top_B", 0.46, 0.2, 0.02, CRATE, rng, (0, 0.2, hz), jag=0.1)
    add_shard(materials, "Board_Front_A", 0.46, 0.2, 0.02, CRATE, rng, (0, -hy, 0.2), (90, 0, 0), jag=0.1)
    add_shard(materials, "Board_Front_B", 0.46, 0.2, 0.02, CRATE, rng, (0, -hy, -0.2), (90, 0, 0), jag=0.1)
    add_shard(materials, "Board_Back_A", 0.46, 0.2, 0.02, CRATE, rng, (0, hy, 0.2), (90, 0, 0), jag=0.1)
    add_shard(materials, "Board_Back_B", 0.46, 0.2, 0.02, CRATE, rng, (0, hy, -0.2), (90, 0, 0), jag=0.1)
    add_shard(materials, "Board_Left", 0.4, 0.36, 0.02, CRATE, rng, (hx, 0, 0), (0, 90, 0), jag=0.12)
    add_shard(materials, "Board_Right", 0.4, 0.36, 0.02, CRATE, rng, (-hx, 0, 0), (0, 90, 0), jag=0.12)
    beam = 0.06
    add_beam(materials, "Beam_0", (hx * 0.8, beam, beam), (0, -hy, hz), (0, 0, 0), CRATE_FRAME, rng)
    add_beam(materials, "Beam_1", (hx * 0.8, beam, beam), (0, hy, -hz), (0, 0, 180), CRATE_FRAME, rng)
    add_beam(materials, "Beam_2", (hy * 0.8, beam, beam), (hx, 0, hz), (0, 0, 90), CRATE_FRAME, rng)
    add_beam(materials, "Beam_3", (hy * 0.8, beam, beam), (-hx, 0, -hz), (0, 0, -90), CRATE_FRAME, rng)
    add_beam(materials, "Beam_4", (hz * 0.8, beam, beam), (hx, hy, 0), (0, 90, 0), CRATE_FRAME, rng)
    for i, corner in enumerate(((1, 1, 1), (-1, -1, 1), (1, -1, -1))):
        part = MeshPart(f"Bracket_{i}")
        part.rounded_box((0, 0, 0), (0.075, 0.075, 0.075), STEEL, power=4.0, rings=6, points=14)
        part.to_object(materials, location=(corner[0] * (hx - 0.05), corner[1] * (hy - 0.05), corner[2] * (hz - 0.05)))
    add_eye_pieces(materials, (hx, hy, hz), 0.105, 0.2, 0.06)
    add_bits(materials, "Straw", 7, 0.26, 0.05, STRAW, rng)


# ---- Large: a purple and gold chest ----

def chest_handles(body, hx):
    for a in (-1, 1):
        # Handles at each end, where the two carriers hold on.
        x = a * (hx + 0.03)
        body.along((x, -0.2, 0.08), (x, 0.2, 0.08), 0.045, 0.045, STEEL, points=12)
        for y in (-0.2, 0.2):
            body.along((a * (hx - 0.02), y, 0.08), (x, y, 0.08), 0.05, 0.05, STEEL, points=10)


def chest_star(body, hz, center=(0.0, 0.0)):
    star = []
    for thickness in (hz - 0.002, hz + 0.022):
        ring = []
        for point in range(10):
            radius = 0.21 if point % 2 == 0 else 0.09
            angle = point * math.pi / 5 + math.pi / 2
            ring.append(Vector((center[0] + radius * math.cos(angle), center[1] + radius * math.sin(angle), thickness)))
        star.append(ring)
    body.loft(star, GOLD)


def large_body(name, stage):
    half = (1.16, 0.56, 0.53)
    hx, hy, hz = half
    body = MeshPart(name)
    rng = random.Random(50 + stage)
    corner_positions = [(a, b, c) for a in (-1, 1) for b in (-1, 1) for c in (-1, 1)]

    if stage < 2:
        fine = stage > 0
        body.rounded_box((0, 0, 0), (hx - 0.02, hy - 0.02, hz - 0.02), CHEST, power=7.0, rings=28 if fine else 18, points=88 if fine else 48)
        # The lid line, gold bands, corner guards and a star on top say this one is worth the trouble.
        body.rounded_box((0, 0, 0.17), (hx - 0.012, hy - 0.012, 0.024), CHEST_DARK, power=7.0, rings=6, points=48)
        for x in (-0.72, 0.72):
            body.rounded_box((x, 0, 0), (0.085, hy - 0.002, hz - 0.002), GOLD, power=7.0, rings=14, points=30)
        for a, b, c in corner_positions:
            if stage == 1 and (a, b, c) == (1, -1, 1):
                continue
            body.rounded_box((a * (hx - 0.1), b * (hy - 0.1), c * (hz - 0.1)), (0.115, 0.115, 0.115), GOLD,
                             power=3.2, rings=8, points=18)
        chest_handles(body, hx)
        chest_star(body, hz)
        if stage == 1:
            add_crack(body, half, "top", (0.2, 0.6), -0.9, 0.7, rng, width=0.02, material=CHEST_DARK, steps=8)
            add_crack(body, half, "front", (0.55, 0.3), -2.4, 0.5, rng, width=0.016, material=CHEST_DARK)
            add_crack(body, half, "back", (-0.55, 0.3), -0.8, 0.5, rng, width=0.016, material=CHEST_DARK)
            add_crack(body, half, "left", (-0.5, 0.6), -1.2, 0.5, rng, width=0.018, material=CHEST_DARK)
            add_tear(body, half, "top", (0.42, -0.35), (0.14, 0.07), rng, jag=0.32)
            body.displace(dent((1.1, -0.5, 0.5), 0.5, 0.09))
            body.displace(dent((-0.6, 0.56, -0.2), 0.36, 0.05))
    else:
        # The lid has been knocked loose, and the treasure inside shows.
        seam = 0.17
        low_half = (hz + seam) * 0.5
        low_centre = seam - low_half
        lower = (hx - 0.02, hy - 0.02, low_half)
        body.rounded_box((0, 0, low_centre), lower, CHEST, power=7.0, rings=24, points=88)
        body.rounded_box((0, 0, seam - 0.005), (hx - 0.09, hy - 0.09, 0.012), INSIDE, power=7.0, rings=4, points=48)
        for x in (-0.72, 0.72):
            body.rounded_box((x, 0, low_centre), (0.085, hy - 0.002, low_half + 0.002), GOLD, power=7.0, rings=14, points=30)
        for a, b, c in corner_positions:
            if c == 1 or (a, b) == (1, -1):
                continue
            body.rounded_box((a * (hx - 0.1), b * (hy - 0.1), c * (hz - 0.1)), (0.115, 0.115, 0.115), GOLD,
                             power=3.2, rings=8, points=18)
        chest_handles(body, hx)
        # A heap of gold, and coins spilling over the rim.
        body.rounded_box((0.05, 0.02, seam + 0.02), (hx * 0.78, hy * 0.72, 0.1), GOLD, power=2.4, rings=10, points=36)
        for _ in range(22):
            x = rng.uniform(-0.9, 0.95)
            y = rng.uniform(-0.42, 0.38)
            z = seam + rng.uniform(0.09, 0.14)
            tilt = Euler((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 0), "XYZ").to_quaternion()
            body.rounded_box((x, y, z), (0.065, 0.065, 0.012), GOLD, power=2.0, rings=4, points=14, rotation=tilt)

        lid = MeshPart("Lid")
        lid_half = (hz - seam) * 0.5
        lid_centre = seam + lid_half
        lid.rounded_box((0, 0, lid_centre), (hx - 0.02, hy - 0.02, lid_half), CHEST, power=7.0, rings=18, points=88)
        for x in (-0.72, 0.72):
            lid.rounded_box((x, 0, lid_centre), (0.085, hy - 0.002, lid_half + 0.002), GOLD, power=7.0, rings=10, points=30)
        for a, b in ((-1, 1), (1, 1), (-1, -1)):
            lid.rounded_box((a * (hx - 0.1), b * (hy - 0.1), hz - 0.1), (0.115, 0.115, 0.115), GOLD, power=3.2, rings=8, points=18)
        chest_star(lid, hz)
        add_crack(lid, half, "top", (-0.6, 0.5), -0.5, 0.8, rng, width=0.022, material=CHEST_DARK, steps=8)
        add_crack(lid, half, "top", (0.5, -0.6), 2.0, 0.5, rng, width=0.018, material=CHEST_DARK)
        # Tips up from the back edge, and slides sideways.
        hinge = Vector((0.0, hy, seam))
        turn = Matrix.Rotation(math.radians(-24.0), 3, "X")
        roll = Matrix.Rotation(math.radians(6.0), 3, "Y")

        def tip_up(co):
            return hinge + roll @ (turn @ (co - hinge)) + Vector((0.1, 0.02, 0.05))

        lid.displace(tip_up)
        merge_parts(body, lid)

        add_crack(body, half, "front", (-0.6, -0.1), -0.4, 0.6, rng, width=0.02, material=CHEST_DARK, steps=8)
        add_crack(body, half, "back", (0.6, -0.1), -2.6, 0.6, rng, width=0.02, material=CHEST_DARK, steps=8)
        add_crack(body, half, "left", (0.1, 0.0), -1.3, 0.34, rng, width=0.02, material=CHEST_DARK)
        add_crack(body, half, "right", (-0.1, 0.0), -1.7, 0.34, rng, width=0.02, material=CHEST_DARK)
        add_tear(body, half, "left", (-0.5, -0.45), (0.12, 0.14), rng)
        add_tear(body, half, "right", (0.45, -0.5), (0.13, 0.11), rng)
        body.displace(dent((1.14, -0.52, -0.1), 0.42, 0.1))
        body.displace(dent((-1.14, 0.5, -0.3), 0.36, 0.08))
    return body


def merge_parts(target, source):
    """Copies one part's geometry and materials into another."""
    source.bm.verts.index_update()
    copied = [target.bm.verts.new(vert.co) for vert in source.bm.verts]
    for face in source.bm.faces:
        new_face = target.bm.faces.new([copied[vert.index] for vert in face.verts])
        new_face.smooth = face.smooth
        new_face.material_index = target._slot(source.slots[face.material_index])
    source.bm.free()


def build_large(materials):
    half = (1.16, 0.56, 0.53)
    large_body("Body", 0).to_object(materials)
    add_face(materials, half, eye_radius=0.135, eye_x=0.28, eye_z=0.0, mouth_z=-0.27, mouth_size=0.11)
    return half


def build_large_damaged(materials):
    large_body("Body_D1", 1).to_object(materials)
    large_body("Body_D2", 2).to_object(materials)


def add_coin(materials, name, rng, spread):
    part = MeshPart(name)
    part.rounded_box((0, 0, 0), (0.07, 0.07, 0.014), GOLD, power=2.0, rings=4, points=16)
    location = (rng.uniform(-spread[0], spread[0]), rng.uniform(-spread[1], spread[1]), rng.uniform(-spread[2], spread[2]))
    part.to_object(materials, location=location)


def build_large_pieces(materials):
    rng = random.Random(61)
    hx, hy, hz = 1.16, 0.56, 0.53
    seam = 0.17

    lid = MeshPart("Lid")
    lid_half = (hz - seam) * 0.5
    lid.rounded_box((0, 0, 0), (hx - 0.02, hy - 0.02, lid_half), CHEST, power=7.0, rings=14, points=64)
    for x in (-0.72, 0.72):
        lid.rounded_box((x, 0, 0), (0.085, hy - 0.002, lid_half + 0.002), GOLD, power=7.0, rings=10, points=30)
    chest_star(lid, lid_half)
    lid.to_object(materials, location=(0, 0, seam + lid_half))

    add_shard(materials, "Wall_Front_A", 0.5, 0.3, 0.03, CHEST, rng, (-0.55, -hy, -0.12), (90, 0, 0), jag=0.12)
    add_shard(materials, "Wall_Front_B", 0.5, 0.3, 0.03, CHEST, rng, (0.55, -hy, -0.12), (90, 0, 0), jag=0.12)
    add_shard(materials, "Wall_Back_A", 0.5, 0.3, 0.03, CHEST, rng, (-0.55, hy, -0.12), (90, 0, 0), jag=0.12)
    add_shard(materials, "Wall_Back_B", 0.5, 0.3, 0.03, CHEST, rng, (0.55, hy, -0.12), (90, 0, 0), jag=0.12)
    add_shard(materials, "Wall_Left", 0.3, 0.46, 0.03, CHEST_DARK, rng, (hx, 0, -0.12), (0, 90, 0), jag=0.12)
    add_shard(materials, "Wall_Right", 0.3, 0.46, 0.03, CHEST_DARK, rng, (-hx, 0, -0.12), (0, 90, 0), jag=0.12)
    add_shard(materials, "Floor", 1.0, 0.46, 0.03, CHEST_DARK, rng, (0, 0, -hz), jag=0.1)
    for i, x in enumerate((-0.72, 0.72)):
        band = MeshPart(f"Band_{i}")
        band.rounded_box((0, 0, 0), (0.085, hy - 0.002, 0.05), GOLD, power=5.0, rings=6, points=24)
        band.to_object(materials, location=(x, 0, -0.45))
    for i, corner in enumerate(((1, 1, -1), (-1, -1, -1), (1, -1, -1), (-1, 1, -1))):
        part = MeshPart(f"Guard_{i}")
        part.rounded_box((0, 0, 0), (0.115, 0.115, 0.115), GOLD, power=3.2, rings=8, points=18)
        part.to_object(materials, location=(corner[0] * (hx - 0.1), corner[1] * (hy - 0.1), corner[2] * (hz - 0.1)))
    add_eye_pieces(materials, (hx, hy, hz), 0.135, 0.28, 0.0)
    for i in range(14):
        add_coin(materials, f"Coin_{i}", rng, (0.8, 0.32, 0.25))


# ---- Output ----

def export(name, builder, root):
    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    half = builder(materials)
    output = root / f"Assets/Game/Models/{name}.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)
    return half


def render_checks(name, folder):
    camera = modeling.setup_preview(target=(0, 0, 0), distance=4.6, angle=(30.0, 28.0), size=(420, 420), samples=16)
    scene = bpy.context.scene
    scale = {"Cargo_Small": 2.6, "Cargo_Medium": 4.2, "Cargo_Large": 6.4}[name.split("#")[0]]
    for view, angle in (("front", (24.0, 18.0)), ("game", (0.0, 53.0)), ("back", (200.0, 30.0))):
        modeling.aim_camera(camera, (0, 0, 0), scale, angle)
        scene.render.filepath = str(Path(folder) / f"{name.replace('#', '_')}_{view}.png")
        bpy.ops.render.render(write_still=True)


def build_stage(builder, damaged, keep):
    """For the check images: the intact model's face on one damaged body."""

    def build(materials):
        builder(materials)
        bpy.data.objects.remove(bpy.data.objects["Body"])
        damaged(materials)
        for other in ("Body_D1", "Body_D2"):
            if other != keep:
                bpy.data.objects.remove(bpy.data.objects[other])

    return build


def main():
    root = Path(__file__).resolve().parents[2]
    (root / "Assets/Game/Models").mkdir(parents=True, exist_ok=True)
    folder = None
    if "--sheet" in sys.argv:
        folder = sys.argv[sys.argv.index("--sheet") + 1]
        Path(folder).mkdir(parents=True, exist_ok=True)
    sets = (
        ("Cargo_Small", build_small, build_small_damaged, build_small_pieces),
        ("Cargo_Medium", build_medium, build_medium_damaged, build_medium_pieces),
        ("Cargo_Large", build_large, build_large_damaged, build_large_pieces),
    )
    for name, builder, damaged, pieces in sets:
        export(name + "_Damaged", damaged, root)
        export(name + "_Pieces", pieces, root)
        export(name, builder, root)
        if folder:
            render_checks(name, folder)
            for stage in ("Body_D1", "Body_D2"):
                modeling.reset_scene()
                build_stage(builder, damaged, stage)(modeling.make_materials(PALETTE))
                render_checks(f"{name}#{stage}", folder)


if __name__ == "__main__":
    main()

"""Builds the player character, its skeleton and all its animations, and writes
Assets/Game/Models/Character.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_character.py

Add `-- --sheet folder` at the end to also render check images of every animation into that folder.

Poses are described by where the hands and feet should be, and a small two-bone solver works out
the elbows and knees. To change an animation, edit its function near the bottom of this file.
Axes: -Y is the character's front, +X is its left, +Z is up, all in metres.
"""
import importlib
import math
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Euler, Quaternion, Vector
from modeling import MeshPart

TEAM = "Char_Team"
BAND = "Char_Band"
PANTS = "Char_Pants"
BELT = "Char_Belt"
BOOT = "Char_Boot"
SOLE = "Char_Sole"
SKIN = "Char_Skin"
CHEEK = "Char_Cheek"
HAIR = "Char_Hair"
WHITE = "Char_EyeWhite"
PUPIL = "Char_Pupil"
GLOVE = "Char_Glove"
HARD_HAT = "Hat_Collector"
RIVAL_HAT = "Hat_Interrupter"
HAT_BAND = "Hat_Band"
HORN = "Hat_Horn"
WOOD = "Hammer_Handle"
STEEL = "Hammer_Head"
STAR = "Char_Star"

PALETTE = {
    TEAM: (0.95, 0.32, 0.03, 0.5),
    BAND: (0.92, 0.92, 0.85, 0.3),
    PANTS: (0.05, 0.07, 0.13, 0.6),
    BELT: (0.11, 0.06, 0.03, 0.5),
    BOOT: (0.22, 0.09, 0.03, 0.5),
    SOLE: (0.02, 0.02, 0.025, 0.7),
    SKIN: (0.93, 0.56, 0.36, 0.5),
    CHEEK: (0.90, 0.33, 0.27, 0.6),
    HAIR: (0.06, 0.03, 0.02, 0.6),
    WHITE: (0.95, 0.95, 0.95, 0.2),
    PUPIL: (0.01, 0.01, 0.015, 0.1),
    GLOVE: (0.9, 0.87, 0.75, 0.5),
    HARD_HAT: (1.0, 0.68, 0.02, 0.25),
    RIVAL_HAT: (0.03, 0.025, 0.04, 0.3),
    HAT_BAND: (0.75, 0.03, 0.04, 0.4),
    HORN: (0.85, 0.78, 0.6, 0.4),
    WOOD: (0.35, 0.16, 0.05, 0.6),
    STEEL: (0.32, 0.34, 0.38, 0.3),
    STAR: (1.0, 0.8, 0.05, 0.3),
}

FPS = 30
FOOT_HEIGHT = 0.10
LIMB_SIDE = {"L": 1.0, "R": -1.0}

SHOULDER = Vector((0.355, 0.0, 0.83))
ARM_REST = Vector((0.643, 0.0, -0.766)).normalized()
ARM_SEGMENT = 0.22
HIP = Vector((0.19, 0.0, 0.40))
KNEE = Vector((0.19, 0.0, 0.25))
ANKLE = Vector((0.19, 0.0, FOOT_HEIGHT))


def side_vector(vector, side):
    return Vector((vector.x * LIMB_SIDE[side], vector.y, vector.z))


def rest_bones():
    bones = [
        ("Root", None, (0, 0, 0), (0, 0, 0.18)),
        ("Hips", "Root", (0, 0, 0.40), (0, 0, 0.56)),
        ("Spine", "Hips", (0, 0, 0.56), (0, 0, 0.72)),
        ("Chest", "Spine", (0, 0, 0.72), (0, 0, 0.90)),
        ("Head", "Chest", (0, 0, 0.90), (0, 0, 1.25)),
        ("Mouth", "Head", (0, -0.345, 0.935), (0, -0.42, 0.935)),
        ("Stars", "Head", (0, 0, 1.56), (0, 0, 1.66)),
    ]
    for side in ("L", "R"):
        shoulder = side_vector(SHOULDER, side)
        elbow = shoulder + side_vector(ARM_REST, side) * ARM_SEGMENT
        wrist = elbow + side_vector(ARM_REST, side) * ARM_SEGMENT
        hand = wrist + side_vector(ARM_REST, side) * 0.14
        sign = LIMB_SIDE[side]
        bones += [
            (f"Eye.{side}", "Head", (sign * 0.135, -0.31, 1.065), (sign * 0.135, -0.39, 1.065)),
            (f"Brow.{side}", "Head", (sign * 0.135, -0.35, 1.2), (sign * 0.135, -0.42, 1.2)),
            (f"UpperArm.{side}", "Chest", shoulder, elbow),
            (f"LowerArm.{side}", f"UpperArm.{side}", elbow, wrist),
            (f"Hand.{side}", f"LowerArm.{side}", wrist, hand),
            (f"Thigh.{side}", "Hips", side_vector(HIP, side), side_vector(KNEE, side)),
            (f"Shin.{side}", f"Thigh.{side}", side_vector(KNEE, side), side_vector(ANKLE, side)),
            (f"Foot.{side}", f"Shin.{side}", side_vector(ANKLE, side),
             side_vector(ANKLE + Vector((0, -0.18, -0.04)), side)),
        ]
    return bones


def build_armature():
    data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    made = {}
    for name, parent, head, tail in rest_bones():
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0
        if parent:
            bone.parent = made[parent]
        made[name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


# ---- Skin weights ----

def torso_weights(co):
    z = co.z
    if z < 0.50:
        return {"Hips": 1.0}
    if z < 0.66:
        t = (z - 0.50) / 0.16
        return {"Hips": 1 - t, "Spine": t}
    if z < 0.82:
        t = (z - 0.66) / 0.16
        return {"Spine": 1 - t, "Chest": t}
    return {"Chest": 1.0}


def arm_weights(side):
    shoulder = side_vector(SHOULDER, side)
    direction = side_vector(ARM_REST, side)

    def weights(co):
        along = (co - shoulder).dot(direction)
        t = max(0.0, min(1.0, (along - (ARM_SEGMENT - 0.07)) / 0.14))
        return {f"UpperArm.{side}": 1 - t, f"LowerArm.{side}": t}

    return weights


def leg_weights(side):
    def weights(co):
        t = max(0.0, min(1.0, (0.30 - co.z) / 0.10))
        return {f"Thigh.{side}": 1 - t, f"Shin.{side}": t}

    return weights


# ---- Meshes ----

def build_body():
    part = MeshPart("Body")

    def torso_paint(samples):
        def pick(ring, point):
            z = samples[ring][0]
            if z < 0.405:
                return PANTS
            if z < 0.455:
                return BELT
            if 0.585 < z < 0.665:
                return BAND
            return TEAM

        return pick

    torso_keys = [
        (0.24, 0.00, 0.02, 0.02, 2.0),
        (0.255, 0.00, 0.17, 0.15, 2.0),
        (0.30, -0.005, 0.31, 0.27, 2.1),
        (0.40, -0.015, 0.40, 0.34, 2.2),
        (0.52, -0.02, 0.44, 0.37, 2.2),
        (0.66, -0.015, 0.435, 0.365, 2.2),
        (0.78, -0.005, 0.395, 0.33, 2.2),
        (0.88, 0.00, 0.33, 0.28, 2.1),
        (0.96, 0.00, 0.23, 0.20, 2.0),
        (1.00, 0.00, 0.08, 0.08, 2.0),
        (1.005, 0.00, 0.01, 0.01, 2.0),
    ]
    samples = modeling.sample_keys(torso_keys, 4)
    rings = [modeling.section(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), max(rx, 0.005), max(ry, 0.005), p, 40)
             for z, cy, rx, ry, p in samples]
    part.bind(torso_weights)
    part.loft(rings, torso_paint(samples))

    # Belt buckle.
    part.bind({"Hips": 1.0})
    part.ellipsoid((0, -0.365, 0.43), (0.075, 0.03, 0.055), STEEL, rings=8, points=14)

    # Head, hair and face.
    part.bind({"Head": 1.0})
    part.upright([
        (0.68, 0.0, 0.02, 0.02, 2.0),
        (0.70, 0.0, 0.17, 0.16, 2.0),
        (0.76, 0.0, 0.30, 0.28, 2.1),
        (0.86, 0.0, 0.375, 0.35, 2.2),
        (0.98, 0.0, 0.385, 0.36, 2.2),
        (1.10, 0.0, 0.365, 0.34, 2.15),
        (1.20, 0.0, 0.32, 0.30, 2.1),
        (1.29, 0.0, 0.22, 0.21, 2.0),
        (1.335, 0.0, 0.08, 0.08, 2.0),
        (1.34, 0.0, 0.01, 0.01, 2.0),
    ], SKIN, points=36, samples=4)
    part.ellipsoid((0, 0.12, 1.08), (0.365, 0.27, 0.215), HAIR, rings=12, points=24)
    for sign in (-1, 1):
        part.ellipsoid((sign * 0.385, 0.0, 0.99), (0.055, 0.075, 0.085), SKIN, rings=8, points=14)
        part.ellipsoid((sign * 0.245, -0.275, 0.945), (0.065, 0.03, 0.045), CHEEK, rings=8, points=14)
    part.ellipsoid((0, -0.372, 0.995), (0.052, 0.042, 0.048), CHEEK, rings=8, points=14)

    for side in ("L", "R"):
        sign = LIMB_SIDE[side]
        part.bind({f"Eye.{side}": 1.0})
        part.ellipsoid((sign * 0.135, -0.31, 1.065), (0.09, 0.04, 0.105), WHITE, rings=10, points=18)
        part.ellipsoid((sign * 0.125, -0.342, 1.055), (0.048, 0.018, 0.06), PUPIL, rings=8, points=14)
        part.bind({f"Brow.{side}": 1.0})
        part.along((sign * 0.05, -0.35, 1.2), (sign * 0.215, -0.335, 1.2), 0.026, 0.024, HAIR, points=10)

    part.bind({"Mouth": 1.0})
    part.ellipsoid((0, -0.345, 0.935), (0.075, 0.02, 0.024), HAIR, rings=8, points=14)

    # Arms and gloves.
    for side in ("L", "R"):
        shoulder = side_vector(SHOULDER, side)
        direction = side_vector(ARM_REST, side)
        elbow = shoulder + direction * ARM_SEGMENT
        wrist = elbow + direction * ARM_SEGMENT
        part.bind({f"UpperArm.{side}": 1.0})
        part.ellipsoid(tuple(shoulder), (0.125, 0.125, 0.125), TEAM, rings=10, points=18)
        part.bind(arm_weights(side))
        rings = []
        u = direction.cross(Vector((0, 1, 0))).normalized()
        v = Vector((0, 1, 0))
        for t, radius in ((0.0, 0.10), (0.25, 0.098), (0.45, 0.092), (0.55, 0.09), (0.75, 0.085), (1.0, 0.08)):
            center = shoulder + direction * (ARM_SEGMENT * 2 * t)
            rings.append(modeling.section(center, u, v, radius, radius, 2.0, 16))
        part.loft(rings, TEAM)
        part.bind({f"Hand.{side}": 1.0})
        palm = wrist + direction * 0.085
        part.ellipsoid(tuple(palm), (0.122, 0.122, 0.125), GLOVE, rings=10, points=18)
        part.ellipsoid(tuple(palm + Vector((0, -0.105, 0.03))), (0.05, 0.055, 0.05), GLOVE, rings=8, points=12)

    # Legs and boots.
    for side in ("L", "R"):
        sign = LIMB_SIDE[side]
        x = sign * 0.19
        part.bind(leg_weights(side))
        rings = []
        for z, radius in ((0.44, 0.15), (0.38, 0.148), (0.30, 0.135), (0.25, 0.125), (0.18, 0.12), (0.10, 0.118)):
            rings.append(modeling.section(Vector((x, 0, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), radius, radius, 2.0, 18))
        part.loft(rings, PANTS)

        part.bind({f"Foot.{side}": 1.0})
        keys = [
            (-0.285, x, 0.082, 0.02, 0.02, 2.2),
            (-0.27, x, 0.082, 0.085, 0.052, 2.2),
            (-0.22, x, 0.086, 0.135, 0.078, 2.3),
            (-0.12, x, 0.092, 0.155, 0.092, 2.4),
            (0.00, x, 0.105, 0.145, 0.105, 2.4),
            (0.08, x, 0.11, 0.135, 0.11, 2.4),
            (0.125, x, 0.10, 0.09, 0.09, 2.2),
            (0.14, x, 0.095, 0.02, 0.02, 2.2),
        ]
        points = 24
        boot_samples = modeling.sample_keys(keys, 3)
        rings = [modeling.section(Vector((cx, y, z)), Vector((1, 0, 0)), Vector((0, 0, 1)), max(rx, 0.005), max(rz, 0.005), p, points)
                 for y, cx, z, rx, rz, p in boot_samples]

        def paint(ring, point, count=points):
            return SOLE if math.sin((point + 0.5) / count * 2 * math.pi) < -0.62 else BOOT

        part.loft(rings, paint)
    return part


def horn_rings(side_sign):
    path = [(0.24, 0.0, 1.34), (0.31, 0.0, 1.41), (0.37, 0.0, 1.5), (0.385, 0.0, 1.595)]
    samples = modeling.sample_keys(path, 4)
    rings = []
    count = len(samples)
    for i, (x, y, z) in enumerate(samples):
        t = i / (count - 1)
        radius = 0.085 * (1 - t) ** 0.8 + 0.004
        center = Vector((side_sign * x, y, z))
        rings.append(modeling.section(center, Vector((1, 0, 0)), Vector((0, 1, 0)), radius, radius, 2.0, 12))
    return rings


def build_hard_hat():
    part = MeshPart("HatCollector")
    part.bind({"Head": 1.0})
    part.ellipsoid((0, 0.015, 1.245), (0.345, 0.35, 0.215), HARD_HAT, rings=14, points=32)
    part.upright([
        (1.232, 0.015, 0.01, 0.01, 2.0),
        (1.235, 0.015, 0.36, 0.37, 2.2),
        (1.25, 0.015, 0.378, 0.385, 2.2),
        (1.268, 0.015, 0.372, 0.378, 2.2),
        (1.275, 0.015, 0.01, 0.01, 2.0),
    ], HARD_HAT, points=32, samples=2)
    part.ellipsoid((0, -0.29, 1.268), (0.19, 0.125, 0.026), HARD_HAT, rings=8, points=20)
    part.lengthwise([
        (-0.33, 0, 1.33, 0.01, 0.01, 2.0),
        (-0.30, 0, 1.345, 0.05, 0.04, 2.0),
        (-0.16, 0, 1.42, 0.062, 0.055, 2.0),
        (0.02, 0, 1.455, 0.066, 0.06, 2.0),
        (0.20, 0, 1.415, 0.062, 0.055, 2.0),
        (0.33, 0, 1.335, 0.05, 0.04, 2.0),
        (0.36, 0, 1.32, 0.01, 0.01, 2.0),
    ], HARD_HAT, points=14, samples=3)
    # Company badge on the front.
    part.along((0, -0.325, 1.335), (0, -0.345, 1.325), 0.05, 0.05, TEAM, power=3.5, points=16)
    return part


def build_rival_hat():
    part = MeshPart("HatInterrupter")
    part.bind({"Head": 1.0})
    part.ellipsoid((0, 0.015, 1.24), (0.35, 0.355, 0.225), RIVAL_HAT, rings=14, points=32)
    part.upright([
        (1.222, 0.015, 0.01, 0.01, 2.0),
        (1.226, 0.015, 0.35, 0.355, 2.2),
        (1.25, 0.015, 0.362, 0.367, 2.2),
        (1.285, 0.015, 0.352, 0.357, 2.2),
        (1.32, 0.015, 0.308, 0.312, 2.2),
        (1.324, 0.015, 0.01, 0.01, 2.0),
    ], HAT_BAND, points=32, samples=2)
    for sign in (-1, 1):
        part.loft(horn_rings(sign), HORN)
    return part


def build_hammer():
    """Modelled at the right wrist, with the handle running forward along -Y."""
    part = MeshPart("Hammer")
    part.bind({"Hand.R": 1.0})
    wrist = side_vector(SHOULDER + ARM_REST * (ARM_SEGMENT * 2), "R")
    x, y, z = wrist
    part.along((x, y + 0.30, z), (x, y - 0.74, z), 0.04, 0.04, WOOD, points=12)
    head_y = y - 0.80
    keys = [(0.0, 0.05), (0.015, 0.78), (0.05, 0.96), (0.13, 1.0), (0.87, 1.0), (0.95, 0.96), (0.985, 0.78), (1.0, 0.05)]
    part.along((x - 0.31, head_y, z), (x + 0.31, head_y, z), 0.215, 0.215, STEEL, points=22, keys=keys, up=(0, 1, 0))
    for offset in (-0.19, 0.19):
        part.along((x + offset - 0.04, head_y, z), (x + offset + 0.04, head_y, z), 0.228, 0.228, HAT_BAND, points=22, up=(0, 1, 0))
    return part


def build_stars():
    part = MeshPart("DizzyStars")
    part.bind({"Stars": 1.0})
    center_z = 1.56
    for i in range(4):
        angle = i * math.pi / 2
        cx = math.cos(angle) * 0.34
        cy = math.sin(angle) * 0.34
        cz = center_z + (0.035 if i % 2 == 0 else -0.035)
        rings = []
        for thickness in (-0.022, 0.022):
            ring = []
            for point in range(10):
                radius = 0.12 if point % 2 == 0 else 0.052
                a = point * math.pi / 5 + math.pi / 2
                ring.append(Vector((cx + math.cos(a) * radius, cy + thickness, cz + math.sin(a) * radius)))
            rings.append(ring)
        part.loft(rings, STAR)
    return part


def attach(obj, arm):
    obj.parent = arm
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm


# ---- Posing ----

def deg_quat(angles):
    return Euler((math.radians(angles[0]), math.radians(angles[1]), math.radians(angles[2])), "XYZ").to_quaternion()


class Pose:
    """One moment of an animation. Rotations are degrees: X leans forward, Y tilts toward the left, Z turns left."""

    def __init__(self, **values):
        self.root_loc = (0.0, 0.0, 0.0)
        self.root_rot = (0.0, 0.0, 0.0)
        self.hips_loc = (0.0, 0.0, -0.015)
        self.hips = (0.0, 0.0, 0.0)
        self.spine = (0.0, 0.0, 0.0)
        self.chest = (0.0, 0.0, 0.0)
        self.head = (0.0, 0.0, 0.0)
        # Wrists relative to the shoulder, turning with the chest, unless hands_in_body is set.
        self.hands_in_body = 0.0
        self.hand_l = (0.15, -0.03, -0.39)
        self.hand_r = (-0.15, -0.03, -0.39)
        self.elbow_l = (0.7, 0.5, -0.2)
        self.elbow_r = (-0.7, 0.5, -0.2)
        self.foot_l = (0.19, 0.0, FOOT_HEIGHT)
        self.foot_r = (-0.19, 0.0, FOOT_HEIGHT)
        self.foot_pitch_l = 0.0
        self.foot_pitch_r = 0.0
        self.knee_l = (0.06, -1.0, 0.0)
        self.knee_r = (-0.06, -1.0, 0.0)
        # The direction the hammer points, when both hands hold it.
        self.grip = None
        self.eye = 1.0
        self.mouth = 1.0
        self.brow_tilt = 0.0
        self.brow_up = 0.0
        for key, value in values.items():
            setattr(self, key, value)

    def copy(self, **changes):
        pose = Pose()
        pose.__dict__.update(self.__dict__)
        for key, value in changes.items():
            setattr(pose, key, value)
        return pose


def mix(a, b, t):
    if a is None or b is None:
        return b if t > 0.5 else a
    if isinstance(a, tuple):
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    return a + (b - a) * t


def mix_poses(a, b, t):
    pose = Pose()
    for key in a.__dict__:
        pose.__dict__[key] = mix(a.__dict__[key], b.__dict__[key], t)
    return pose


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def keyed(keys, seconds):
    """Turns (time in seconds, pose) pairs into a function of normalized time, easing between them."""

    def sample(u):
        time = u * seconds
        for i in range(len(keys) - 1):
            start, a = keys[i]
            end, b = keys[i + 1]
            if time <= end:
                return mix_poses(a, b, smooth((time - start) / max(end - start, 0.0001)))
        return keys[-1][1]

    return sample


class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.rest = {}
        self.rot = {}
        for bone in arm.data.bones:
            self.rest[bone.name] = bone.head_local.copy()
            self.rot[bone.name] = bone.matrix_local.to_quaternion()
        self.parents = {bone.name: bone.parent.name if bone.parent else None for bone in arm.data.bones}
        self.upper_dir = {s: (arm.data.bones[f"UpperArm.{s}"].tail_local - arm.data.bones[f"UpperArm.{s}"].head_local).normalized() for s in "LR"}
        self.lower_dir = {s: (arm.data.bones[f"LowerArm.{s}"].tail_local - arm.data.bones[f"LowerArm.{s}"].head_local).normalized() for s in "LR"}
        self.thigh_dir = {s: (arm.data.bones[f"Thigh.{s}"].tail_local - arm.data.bones[f"Thigh.{s}"].head_local).normalized() for s in "LR"}
        self.shin_dir = {s: (arm.data.bones[f"Shin.{s}"].tail_local - arm.data.bones[f"Shin.{s}"].head_local).normalized() for s in "LR"}
        self.previous = {}
        for pose_bone in arm.pose.bones:
            pose_bone.rotation_mode = "QUATERNION"

    def solve_limb(self, start, target, first, second, hint):
        """Two-bone solver: returns the directions of the two segments."""
        to_target = target - start
        reach = first + second - 0.002
        distance = max(min(to_target.length, reach), abs(first - second) + 0.01)
        axis = to_target.normalized() if to_target.length > 0.0001 else Vector((0, 0, -1))
        along = (first * first - second * second + distance * distance) / (2 * distance)
        height = math.sqrt(max(first * first - along * along, 0.0))
        side = hint - axis * hint.dot(axis)
        side = side.normalized() if side.length > 0.0001 else Vector((0, -1, 0))
        joint = start + axis * along + side * height
        end = start + axis * distance
        return (joint - start).normalized(), (end - joint).normalized()

    def apply(self, pose, frame):
        rest = self.rest
        delta = {"Root": Quaternion()}
        place = {"Root": rest["Root"].copy()}
        local_rotation = {}
        local_location = {}
        local_scale = {}

        def chain(name, parent, rotation, offset=None):
            delta[name] = delta[parent] @ rotation
            place[name] = place[parent] + delta[parent] @ (rest[name] - rest[parent])
            if offset is not None:
                place[name] = place[name] + offset

        local_location["Root"] = Vector(pose.root_loc)
        root_delta = deg_quat(pose.root_rot)

        chain("Hips", "Root", deg_quat(pose.hips), Vector(pose.hips_loc))
        local_location["Hips"] = Vector(pose.hips_loc)
        chain("Spine", "Hips", deg_quat(pose.spine))
        chain("Chest", "Spine", deg_quat(pose.chest))
        chain("Head", "Chest", deg_quat(pose.head))

        for side in "LR":
            hand = Vector(pose.hand_l if side == "L" else pose.hand_r)
            hint = Vector(pose.elbow_l if side == "L" else pose.elbow_r)
            chain(f"UpperArm.{side}", "Chest", Quaternion())
            shoulder = place[f"UpperArm.{side}"]
            if pose.hands_in_body > 0.5:
                target = hand
                bend = hint
            else:
                target = shoulder + delta["Chest"] @ hand
                bend = delta["Chest"] @ hint
            upper, lower = self.solve_limb(shoulder, target, ARM_SEGMENT, ARM_SEGMENT, bend)
            upper_delta = self.upper_dir[side].rotation_difference(upper)
            current = upper_delta @ self.lower_dir[side]
            lower_delta = current.rotation_difference(lower) @ upper_delta
            delta[f"UpperArm.{side}"] = upper_delta
            delta[f"LowerArm.{side}"] = lower_delta
            if pose.grip is not None and side == "R":
                delta[f"Hand.{side}"] = Vector((0, -1, 0)).rotation_difference(Vector(pose.grip).normalized())
            else:
                delta[f"Hand.{side}"] = lower_delta

            foot = Vector(pose.foot_l if side == "L" else pose.foot_r)
            knee = Vector(pose.knee_l if side == "L" else pose.knee_r)
            pitch = pose.foot_pitch_l if side == "L" else pose.foot_pitch_r
            chain(f"Thigh.{side}", "Hips", Quaternion())
            hip = place[f"Thigh.{side}"]
            thigh, shin = self.solve_limb(hip, foot, 0.15, 0.15, knee)
            thigh_delta = self.thigh_dir[side].rotation_difference(thigh)
            current = thigh_delta @ self.shin_dir[side]
            shin_delta = current.rotation_difference(shin) @ thigh_delta
            delta[f"Thigh.{side}"] = thigh_delta
            delta[f"Shin.{side}"] = shin_delta
            delta[f"Foot.{side}"] = deg_quat((pitch, 0.0, 0.0))

            delta[f"Eye.{side}"] = delta["Head"]
            sign = LIMB_SIDE[side]
            delta[f"Brow.{side}"] = delta["Head"] @ deg_quat((0.0, sign * pose.brow_tilt, 0.0))
            local_location[f"Brow.{side}"] = Vector((0, 0, pose.brow_up))
            local_scale[f"Eye.{side}"] = Vector((1.0 + (pose.eye - 1.0) * 0.25, 1.0, pose.eye))
        delta["Mouth"] = delta["Head"]
        delta["Stars"] = delta["Head"]
        local_scale["Mouth"] = Vector((1.0 + (pose.mouth - 1.0) * 0.12, 1.0, pose.mouth))

        for pose_bone in self.arm.pose.bones:
            name = pose_bone.name
            parent = self.parents[name]
            rest_rotation = self.rot[name]
            if parent is None:
                q = root_delta
            else:
                q = delta[parent].inverted() @ delta[name]
            local = rest_rotation.inverted() @ q @ rest_rotation
            previous = self.previous.get(name)
            if previous is not None and previous.dot(local) < 0.0:
                local.negate()
            self.previous[name] = local.copy()
            pose_bone.rotation_quaternion = local
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
            if name in local_location:
                pose_bone.location = rest_rotation.inverted() @ local_location[name]
                pose_bone.keyframe_insert("location", frame=frame)
            if name in local_scale:
                pose_bone.scale = local_scale[name]
                pose_bone.keyframe_insert("scale", frame=frame)

    def bake_clip(self, name, seconds, function):
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        if self.arm.animation_data is None:
            self.arm.animation_data_create()
        self.arm.animation_data.action = action
        self.previous = {}
        frames = max(int(round(seconds * FPS)), 1)
        for frame in range(frames + 1):
            self.apply(function(frame / frames), frame)
        return action, frames

    def clear_pose(self):
        if self.arm.animation_data is not None:
            self.arm.animation_data.action = None
        for pose_bone in self.arm.pose.bones:
            pose_bone.location = (0, 0, 0)
            pose_bone.rotation_quaternion = (1, 0, 0, 0)
            pose_bone.scale = (1, 1, 1)


# ---- Animations ----

TAU = 2 * math.pi


def idle(u):
    a = TAU * u
    breathe = math.sin(a)
    return Pose(
        hips_loc=(0, 0, -0.018 + 0.006 * breathe),
        hips=(0, 1.0 * math.sin(a + 0.5), 0),
        spine=(1.5 + 1.0 * breathe, 0, 0),
        chest=(-1.0 + 1.6 * math.sin(a + 0.5), 0, 1.5 * math.sin(a)),
        head=(1.5 - 1.5 * math.sin(a + 1.0), 0, 3.0 * math.sin(a)),
        hand_l=(0.15, -0.035, -0.385 + 0.006 * breathe),
        hand_r=(-0.15, -0.035, -0.385 + 0.006 * breathe),
    )


def run_foot(phase, x, stride, lift, height=FOOT_HEIGHT):
    phase %= TAU
    y = -stride * math.cos(phase)
    z = height if phase < math.pi else height + lift * math.sin(phase - math.pi)
    pitch = 12.0 - 30.0 * math.cos(phase - 0.6)
    return (x, y, z), pitch


def run(u):
    a = TAU * u
    foot_l, pitch_l = run_foot(a, 0.17, 0.2, 0.2)
    foot_r, pitch_r = run_foot(a + math.pi, -0.17, 0.2, 0.2)
    swing = -math.cos(a)
    return Pose(
        hips_loc=(0, -0.03, -0.05 - 0.04 * math.cos(2 * a)),
        hips=(8.0, 13.0 * math.sin(a), -12.0 * math.cos(a)),
        spine=(8.0, -4.0 * math.sin(a), 8.0 * math.cos(a)),
        chest=(5.0 + 2.5 * math.cos(2 * a), -4.0 * math.sin(a), 10.0 * math.cos(a)),
        head=(-16.0 - 2.5 * math.cos(2 * a), -4.0 * math.sin(a), -6.0 * math.cos(a)),
        hand_l=(0.13, -0.03 - 0.27 * swing, -0.22 + 0.09 * max(swing, 0.0)),
        hand_r=(-0.13, -0.03 + 0.27 * swing, -0.22 + 0.09 * max(-swing, 0.0)),
        elbow_l=(0.55, 0.8, -0.2),
        elbow_r=(-0.55, 0.8, -0.2),
        foot_l=foot_l,
        foot_r=foot_r,
        foot_pitch_l=pitch_l,
        foot_pitch_r=pitch_r,
        mouth=1.5,
    )


def hold_pose(u, x, y, z):
    """Upper body only. Cargo is hugged against the belly, with a hand on each side of it."""
    a = TAU * u
    return Pose(
        hands_in_body=1.0,
        hand_l=(x, y, z + 0.012 * math.sin(a)),
        hand_r=(-x, y, z + 0.012 * math.sin(a + 1.4)),
        elbow_l=(1.0, 0.3, -0.45),
        elbow_r=(-1.0, 0.3, -0.45),
        spine=(-5.0, 1.5 * math.sin(a), 0),
        chest=(-4.0 + math.sin(2 * a), 1.5 * math.sin(a + 0.6), 0),
        head=(5.0, -2.0 * math.sin(a + 1.0), 0),
        mouth=0.8,
        brow_tilt=-8.0,
    )


def carry(u):
    return hold_pose(u, 0.33, -0.5, 0.6)


def carry_big(u):
    """The crate is wider than the arms can properly reach, so the hands hold its near corners."""
    return hold_pose(u, 0.53, -0.46, 0.6)


def co_carry_pose(a):
    return dict(
        hands_in_body=1.0,
        hand_l=(0.30, -0.47, 0.62),
        hand_r=(-0.30, -0.47, 0.62),
        elbow_l=(1.0, 0.4, -0.3),
        elbow_r=(-1.0, 0.4, -0.3),
        hips=(-7.0, 0, 0),
        spine=(-3.0, 0, 0),
        chest=(5.0 + 1.0 * math.sin(4 * a), 1.2 * math.sin(2 * a), 0),
        head=(7.0, 0, 0),
        brow_tilt=-14.0,
        mouth=1.6,
    )


def co_carry_idle(u):
    a = TAU * u
    return Pose(
        hips_loc=(0, 0.03, -0.075 + 0.005 * math.sin(2 * a)),
        foot_l=(0.23, -0.06, FOOT_HEIGHT),
        foot_r=(-0.23, 0.07, FOOT_HEIGHT),
        **co_carry_pose(a),
    )


def co_carry_walk(u):
    a = TAU * u
    foot_l, pitch_l = run_foot(a, 0.22, 0.11, 0.075)
    foot_r, pitch_r = run_foot(a + math.pi, -0.22, 0.11, 0.075)
    values = co_carry_pose(a)
    values["hips"] = (-7.0, 7.0 * math.sin(a), 0)
    return Pose(
        hips_loc=(0, 0.03, -0.075 - 0.014 * math.cos(2 * a)),
        foot_l=foot_l,
        foot_r=foot_r,
        foot_pitch_l=pitch_l * 0.5,
        foot_pitch_r=pitch_r * 0.5,
        **values,
    )


RAM_POSE = Pose(
    hips_loc=(0, -0.10, -0.075),
    hips=(24.0, 0, -8.0),
    spine=(16.0, 0, -8.0),
    chest=(12.0, 0, -18.0),
    head=(-30.0, 0, 22.0),
    hand_l=(-0.10, -0.26, -0.24),
    hand_r=(-0.02, 0.32, -0.20),
    elbow_l=(0.8, 0.4, -0.3),
    elbow_r=(-0.7, 0.5, 0.1),
    foot_l=(0.17, -0.22, 0.13),
    foot_r=(-0.17, 0.28, 0.20),
    foot_pitch_l=-12.0,
    foot_pitch_r=42.0,
    mouth=2.6,
    brow_tilt=-24.0,
)


def ram(u):
    wobble = math.sin(u * TAU * 2) * 1.5
    pose = mix_poses(Pose(), RAM_POSE, smooth(u / 0.3))
    pose.chest = (pose.chest[0] + wobble, pose.chest[1], pose.chest[2])
    return pose


LYING = dict(
    root_rot=(-92.0, 0.0, 8.0),
    root_loc=(0.0, 0.45, 0.36),
    hand_l=(0.42, 0.05, 0.02),
    hand_r=(-0.42, 0.05, 0.02),
    elbow_l=(0.2, 0.2, 1.0),
    elbow_r=(-0.2, 0.2, 1.0),
    foot_l=(0.31, -0.02, 0.12),
    foot_r=(-0.31, -0.02, 0.12),
    foot_pitch_l=-30.0,
    foot_pitch_r=-30.0,
    knee_l=(0.8, -0.6, 0.0),
    knee_r=(-0.8, -0.6, 0.0),
    mouth=1.3,
)

KNOCKED_SECONDS = 1.5

knocked = keyed([
    (0.00, Pose()),
    (0.06, Pose(root_rot=(-22.0, 0, 0), root_loc=(0, 0.06, 0.09), spine=(18.0, 0, 0), chest=(12.0, 0, 0), head=(20.0, 0, 0),
                hand_l=(0.18, -0.34, 0.10), hand_r=(-0.18, -0.34, 0.10), foot_l=(0.19, -0.12, 0.17), foot_r=(-0.19, -0.18, 0.21),
                foot_pitch_l=-25.0, foot_pitch_r=-25.0, eye=1.35, mouth=3.2, brow_up=0.035)),
    (0.20, Pose(root_rot=(-62.0, 0, 0), root_loc=(0, 0.30, 0.50), spine=(-12.0, 0, 0), chest=(-8.0, 0, 0), head=(12.0, 0, 0),
                hand_l=(0.30, 0.0, 0.32), hand_r=(-0.28, -0.1, 0.34), foot_l=(0.22, -0.17, 0.20), foot_r=(-0.20, -0.24, 0.16),
                foot_pitch_l=-35.0, foot_pitch_r=-35.0, eye=1.3, mouth=3.2, brow_up=0.035)),
    (0.34, Pose(root_rot=(-92.0, 0, 0), root_loc=(0, 0.36, 0.36), hand_l=(0.36, 0.0, 0.18), hand_r=(-0.36, 0.0, 0.18),
                foot_l=(0.24, -0.15, 0.17), foot_r=(-0.24, -0.15, 0.17), eye=0.55, mouth=2.6)),
    (0.44, Pose(root_rot=(-84.0, 0, 0), root_loc=(0, 0.42, 0.50), head=(10.0, 0, 0), hand_l=(0.30, -0.24, 0.12), hand_r=(-0.30, -0.24, 0.12),
                foot_l=(0.24, -0.22, 0.15), foot_r=(-0.24, -0.22, 0.15), eye=0.7, mouth=1.8)),
    (0.56, Pose(head=(-6.0, 14.0, 0), eye=0.38, **LYING)),
    (0.95, Pose(head=(-6.0, -12.0, 0), eye=0.3, **LYING)),
    (1.10, Pose(root_rot=(-48.0, 0, 0), root_loc=(0, 0.28, 0.20), spine=(18.0, 0, 0), chest=(10.0, 0, 0), head=(12.0, 0, 0),
                hand_l=(0.24, 0.14, -0.30), hand_r=(-0.24, 0.14, -0.30), foot_l=(0.22, -0.15, 0.13), foot_r=(-0.22, -0.15, 0.13),
                foot_pitch_l=-20.0, foot_pitch_r=-20.0, eye=0.8)),
    (1.24, Pose(root_rot=(10.0, 0, 0), root_loc=(0, 0.10, 0.0), hips_loc=(0, 0, -0.10), hand_l=(0.18, -0.22, -0.18), hand_r=(-0.18, -0.22, -0.18),
                foot_l=(0.2, 0.02, FOOT_HEIGHT), foot_r=(-0.2, 0.02, FOOT_HEIGHT))),
    (1.38, Pose(root_loc=(0, 0, 0.02), hips_loc=(0, 0, -0.005), spine=(-6.0, 0, 0), head=(0, 16.0, 0), hand_l=(0.25, 0.0, -0.3), hand_r=(-0.25, 0.0, -0.3))),
    (1.50, Pose(head=(0, -6.0, 0))),
], KNOCKED_SECONDS)


def swing_pose(right_wrist, hammer_points, left_wrist, **values):
    """A big hammer in one small hand: the arms are too short to get both hands on it."""
    return Pose(
        hands_in_body=1.0,
        hand_r=right_wrist,
        hand_l=left_wrist,
        grip=tuple(Vector(hammer_points).normalized()),
        elbow_r=(-1.0, 0.3, -0.2),
        elbow_l=(1.0, 0.4, -0.3),
        brow_tilt=-22.0,
        **values,
    )


SMASH_SECONDS = 0.5
SMASH_TOP = swing_pose((-0.44, 0.06, 1.20), (-0.12, 0.35, 0.93), (0.50, -0.05, 0.55),
                       spine=(-6.0, 0, 0), chest=(-12.0, 0, -14.0), head=(-6.0, 0, 8.0), mouth=1.4)
SMASH_WIND = swing_pose((-0.44, 0.12, 1.22), (-0.10, 0.60, 0.79), (0.50, -0.14, 0.60),
                        spine=(-8.0, 0, 0), chest=(-18.0, 0, -20.0), head=(-10.0, 0, 12.0), mouth=0.8)
SMASH_HIT = swing_pose((-0.20, -0.52, 0.66), (0.16, -0.92, -0.36), (0.52, 0.22, 0.62),
                       spine=(16.0, 0, 0), chest=(30.0, 0, 16.0), head=(4.0, 0, -8.0), mouth=2.6, eye=0.8)
SMASH_BOUNCE = swing_pose((-0.20, -0.50, 0.80), (0.14, -0.96, -0.15), (0.50, 0.14, 0.60),
                          spine=(12.0, 0, 0), chest=(22.0, 0, 12.0), head=(2.0, 0, -6.0), mouth=1.8)

smash = keyed([
    (0.00, SMASH_TOP),
    (0.19, SMASH_WIND),
    (0.275, SMASH_HIT),
    (0.34, SMASH_BOUNCE),
    (0.50, SMASH_TOP),
], SMASH_SECONDS)

TOSS_SECONDS = 0.36
toss = keyed([
    (0.00, carry(0.0)),
    (0.14, Pose(hands_in_body=1.0, hand_l=(0.26, -0.5, 0.92), hand_r=(-0.26, -0.5, 0.92), elbow_l=(1.0, 0.3, -0.2), elbow_r=(-1.0, 0.3, -0.2),
                spine=(6.0, 0, 0), chest=(12.0, 0, 0), head=(-6.0, 0, 0), mouth=2.2)),
    (0.36, Pose(hands_in_body=1.0, hand_l=(0.50, -0.05, 0.46), hand_r=(-0.50, -0.05, 0.46))),
], TOSS_SECONDS)


def cheer(u):
    a = TAU * u
    jump = max(0.0, math.sin(a))
    crouch = max(0.0, -math.sin(a))
    wave = math.sin(a)
    tuck = 0.07 * jump
    return Pose(
        root_loc=(0, 0, 0.26 * jump),
        hips_loc=(0, 0, -0.015 - 0.06 * crouch),
        spine=(-3.0, 3.0 * math.sin(a * 2), 0),
        chest=(-4.0, 0, 0),
        head=(-14.0, 0, 7.0 * wave),
        hand_l=(0.22 + 0.05 * wave, -0.02, 0.37),
        hand_r=(-0.22 + 0.05 * wave, -0.02, 0.37),
        elbow_l=(1.0, 0.1, 0.0),
        elbow_r=(-1.0, 0.1, 0.0),
        foot_l=(0.19, -0.02, FOOT_HEIGHT + tuck),
        foot_r=(-0.19, -0.02, FOOT_HEIGHT + tuck),
        foot_pitch_l=25.0 * jump,
        foot_pitch_r=25.0 * jump,
        eye=1.15,
        mouth=3.0,
        brow_up=0.02,
    )


def grumble(u):
    a = TAU * u
    stomp = smooth(1 - abs(u - 0.32) / 0.2) if abs(u - 0.32) < 0.2 else 0.0
    shake = math.sin(a * 5)
    return Pose(
        hips_loc=(0, 0, -0.03 + 0.012 * stomp),
        hips=(0, -5.0 * stomp, 0),
        chest=(8.0, 3.0 * shake, 7.0 * math.sin(a)),
        head=(6.0, 5.0 * shake, -9.0 * math.sin(a)),
        hand_r=(-0.10 + 0.02 * shake, -0.14, 0.38),
        elbow_r=(-1.0, 0.3, 0.0),
        hand_l=(0.22, 0.08, -0.23),
        elbow_l=(1.0, 0.6, 0.0),
        foot_r=(-0.19, -0.04, FOOT_HEIGHT + 0.14 * stomp),
        foot_pitch_r=-20.0 * stomp,
        brow_tilt=-28.0,
        mouth=1.0 + 1.2 * stomp,
    )


# Name, seconds, function. Looping and speeds are set where the clips are imported into Unity.
CLIPS = [
    ("Idle", 2.0, idle),
    ("Run", 0.8, run),
    ("Carry", 1.0, carry),
    ("CarryBig", 1.0, carry_big),
    ("CoCarryIdle", 1.2, co_carry_idle),
    ("CoCarryWalk", 0.8, co_carry_walk),
    ("Ram", 0.34, ram),
    ("Knocked", KNOCKED_SECONDS, knocked),
    ("Smash", SMASH_SECONDS, smash),
    ("Toss", TOSS_SECONDS, toss),
    ("Cheer", 0.8, cheer),
    ("Grumble", 1.0, grumble),
]


def render_sheet(rig, actions, folder):
    """One row of check images for each animation."""
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    camera = modeling.setup_preview(target=(0, 0, 0.75), distance=4.6, angle=(30.0, 28.0), size=(360, 360), samples=10)
    scene = bpy.context.scene
    ground = bpy.data.meshes.new("Ground")
    ground.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
    ground_object = bpy.data.objects.new("Ground", ground)
    material = bpy.data.materials.new("GroundMat")
    material.diffuse_color = (0.25, 0.5, 0.15, 1.0)
    material.use_nodes = True
    material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.5, 0.15, 1.0)
    ground.materials.append(material)
    scene.collection.objects.link(ground_object)

    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1].split(",")
    for (name, action, frames) in actions:
        if only and name not in only:
            continue
        rig.arm.animation_data.action = action
        bpy.data.objects["Hammer"].hide_render = name != "Smash"
        bpy.data.objects["HatInterrupter"].hide_render = True
        bpy.data.objects["DizzyStars"].hide_render = name != "Knocked"
        for step in range(6):
            frame = int(round(step / 6 * frames))
            scene.frame_set(frame)
            scene.render.filepath = str(folder / f"{name}_{step}.png")
            bpy.ops.render.render(write_still=True)
    # A still of each hat from the game's camera angle and from the front.
    rig.clear_pose()
    bpy.data.objects["Hammer"].hide_render = True
    bpy.data.objects["DizzyStars"].hide_render = True
    for hat_name, other in (("HatCollector", "HatInterrupter"), ("HatInterrupter", "HatCollector")):
        bpy.data.objects[hat_name].hide_render = False
        bpy.data.objects[other].hide_render = True
        for view, angle in (("front", (20.0, 8.0)), ("game", (0.0, 53.0)), ("back", (160.0, 53.0))):
            modeling.aim_camera(camera, (0, 0, 0.75), 4.2, angle)
            scene.frame_set(0)
            scene.render.filepath = str(folder / f"still_{hat_name}_{view}.png")
            bpy.ops.render.render(write_still=True)


def main():
    modeling.reset_scene()
    scene = bpy.context.scene
    scene.render.fps = FPS
    materials = modeling.make_materials(PALETTE)
    arm = build_armature()

    for part in (build_body(), build_hard_hat(), build_rival_hat(), build_hammer(), build_stars()):
        attach(part.to_object(materials), arm)

    rig = Rig(arm)
    actions = []
    for name, seconds, function in CLIPS:
        action, frames = rig.bake_clip(name, seconds, function)
        actions.append((name, action, frames))
    rig.clear_pose()
    scene.frame_set(0)

    root = Path(__file__).resolve().parents[2]
    output = root / "Assets/Game/Models/Character.fbx"
    output.parent.mkdir(parents=True, exist_ok=True)
    modeling.export_rigged_fbx(output)
    print("Wrote", output)

    if "--sheet" in sys.argv:
        render_sheet(rig, actions, sys.argv[sys.argv.index("--sheet") + 1])
        print("Sheet done")


main()

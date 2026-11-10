"""Builds what you see of yourself in first person, and writes Assets/Game/Models/FirstPersonHands.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_hands.py

Add `-- --sheet folder` at the end to also render check images.

Each part of an arm is a separate rigid object and there are no animations in the file. FirstPersonRig
poses them in code, because where the hands go depends on the size of the cargo being held and the arms
bend to follow. A hand's origin is its wrist. A forearm's origin is also the wrist and it runs back to
the elbow, and an upper arm's origin is the elbow and it runs back to the shoulder. The lengths here have
to match FirstPersonRig's. Axes: -Y is forward along the fingers, +X is the model's left, +Z is up.
"""
import importlib
import math
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Vector
from modeling import MeshPart

TEAM = "Char_Team"
GLOVE = "Char_Glove"
WOOD = "Hammer_Handle"
STEEL = "Hammer_Head"
HAMMER_BAND = "Hat_Band"
VAC_BODY = "Heli_Yellow"
VAC_DARK = "Heli_Dark"
VAC_METAL = "Heli_Steel"
VAC_GLASS = "Heli_Glass"
VAC_RED = "Heli_Red"

PALETTE = {
    TEAM: (0.95, 0.32, 0.03, 0.5),
    GLOVE: (0.9, 0.87, 0.75, 0.5),
    WOOD: (0.35, 0.16, 0.05, 0.6),
    STEEL: (0.32, 0.34, 0.38, 0.3),
    HAMMER_BAND: (0.75, 0.03, 0.04, 0.4),
    VAC_BODY: (1.0, 0.62, 0.03, 0.35),
    VAC_DARK: (0.03, 0.03, 0.04, 0.5),
    VAC_METAL: (0.45, 0.47, 0.52, 0.3),
    VAC_GLASS: (0.3, 0.7, 0.9, 0.1),
    VAC_RED: (0.75, 0.05, 0.04, 0.4),
}

FOREARM_LENGTH = 0.62
UPPER_ARM_LENGTH = 0.54

# How far in front of the wrist the hammer's handle runs through a closed fist.
GRIP_Y = -0.105

# Along the hammer from the right hand's grip, which is its origin.
HEAD_Z = 0.46
LEFT_GRIP_Z = -0.27
BUTT_Z = -0.42

# The vacuum, from the right hand's grip, which is its origin, before scaling. It's modelled big and
# scaled down, because it's right in front of your eyes. FirstPersonRig has the results of these.
VACUUM_SCALE = 0.66
BARREL_Z = 0.17
FORE_GRIP_Y = -0.28
MOUTH_Y = -0.66


def add_cuff(part):
    part.lengthwise([
        (0.036, 0, 0, 0.06, 0.06, 2.0),
        (0.03, 0, 0, 0.08, 0.08, 2.0),
        (0.01, 0, 0, 0.084, 0.084, 2.0),
        (-0.015, 0, 0, 0.078, 0.078, 2.0),
        (-0.03, 0, 0, 0.064, 0.064, 2.0),
    ], GLOVE, points=20, samples=2)


def add_finger(part, points, radius):
    part.tube(points, radius, GLOVE, points=12, samples=3)
    part.ellipsoid(points[-1], (radius, radius, radius), GLOVE, rings=8, points=12)


def build_open_hand(name, thumb_side):
    """Fingers nearly straight, for holding on to the side of something."""
    part = MeshPart(name)
    add_cuff(part)
    part.rounded_box((0, -0.088, 0), (0.082, 0.088, 0.046), GLOVE, power=3.2, rings=12, points=26)
    for x, length in ((-0.052, 0.082), (0.0, 0.094), (0.052, 0.082)):
        base = (x, -0.158, 0.002)
        middle = (x, -0.158 - length * 0.5, -0.002)
        add_finger(part, [base, middle, (x, -0.158 - length, -0.016)], 0.029)
    add_finger(part, [(thumb_side * 0.066, -0.05, -0.006), (thumb_side * 0.1, -0.085, -0.004), (thumb_side * 0.128, -0.118, -0.012)], 0.031)
    return part


def build_relaxed_hand(name, thumb_side):
    """How a hand hangs when it isn't doing anything: fingers loosely curled under the palm."""
    part = MeshPart(name)
    add_cuff(part)
    part.rounded_box((0, -0.082, 0.004), (0.078, 0.082, 0.05), GLOVE, power=3.0, rings=12, points=26)
    for x, reach in ((-0.05, 0.0), (0.0, 0.008), (0.05, 0.0)):
        start = (x, -0.15, 0.004)
        knuckle = (x, -0.19 - reach, -0.014)
        tip = (x, -0.205 - reach, -0.062)
        add_finger(part, [start, knuckle, tip], 0.03)
    add_finger(part, [(thumb_side * 0.062, -0.05, -0.012), (thumb_side * 0.094, -0.09, -0.028), (thumb_side * 0.082, -0.126, -0.05)], 0.031)
    return part


def build_fist(name, thumb_side):
    """Closed around a handle that runs straight up through the fist, with the knuckles forward."""
    part = MeshPart(name)
    add_cuff(part)
    part.rounded_box((0, -0.08, 0), (0.058, 0.082, 0.07), GLOVE, power=3.2, rings=12, points=26)
    for z in (-0.046, 0.0, 0.046):
        part.ellipsoid((thumb_side * 0.012, GRIP_Y - 0.052, z), (0.054, 0.034, 0.025), GLOVE, rings=8, points=14)
    part.ellipsoid((thumb_side * 0.05, GRIP_Y - 0.012, 0.03), (0.03, 0.058, 0.03), GLOVE, rings=8, points=14)
    return part


def build_forearm(name):
    """From the wrist back to the elbow."""
    part = MeshPart(name)
    part.lengthwise([
        (0.03, 0, 0, 0.012, 0.012, 2.0),
        (0.035, 0, 0, 0.074, 0.074, 2.0),
        (0.25, 0, 0, 0.084, 0.084, 2.0),
        (FOREARM_LENGTH, 0, 0, 0.1, 0.1, 2.0),
        (FOREARM_LENGTH + 0.005, 0, 0, 0.012, 0.012, 2.0),
    ], TEAM, points=24, samples=3)
    return part


def build_upper_arm(name):
    """From the elbow back to the shoulder. The round end hides the joint however far the elbow bends."""
    part = MeshPart(name)
    part.ellipsoid((0, 0, 0), (0.104, 0.104, 0.104), TEAM, rings=12, points=20)
    part.lengthwise([
        (0.0, 0, 0, 0.012, 0.012, 2.0),
        (0.005, 0, 0, 0.098, 0.098, 2.0),
        (UPPER_ARM_LENGTH * 0.5, 0, 0, 0.11, 0.11, 2.0),
        (UPPER_ARM_LENGTH, 0, 0, 0.122, 0.122, 2.0),
        (UPPER_ARM_LENGTH + 0.005, 0, 0, 0.012, 0.012, 2.0),
    ], TEAM, points=24, samples=3)
    return part


def build_hammer():
    """Long enough for two hands. Its origin is where the right hand grips, the handle runs up +Z and the head strikes along -Y."""
    part = MeshPart("Hammer")
    part.along((0, 0, BUTT_Z), (0, 0, HEAD_Z + 0.03), 0.027, 0.027, WOOD, points=12)
    keys = [(0.0, 0.05), (0.02, 0.8), (0.07, 0.96), (0.14, 1.0), (0.86, 1.0), (0.93, 0.96), (0.98, 0.8), (1.0, 0.05)]
    part.along((0, 0.2, HEAD_Z), (0, -0.2, HEAD_Z), 0.115, 0.115, STEEL, points=26, keys=keys, up=(0, 0, 1))
    for y in (0.125, -0.125):
        part.along((0, y + 0.024, HEAD_Z), (0, y - 0.024, HEAD_Z), 0.122, 0.122, HAMMER_BAND, points=26, up=(0, 0, 1))
    return part


def build_vacuum():
    """What the collectors carry. Its origin is where the right hand grips, the grip runs up +Z and the mouth faces -Y."""
    part = MeshPart("Vacuum")
    z = BARREL_Z

    # The two handles the fists close round, straight up and down like the hammer's.
    part.along((0, 0, -0.13), (0, 0, z - 0.04), 0.033, 0.033, VAC_DARK, points=14)
    part.along((0, FORE_GRIP_Y, -0.11), (0, FORE_GRIP_Y, z - 0.04), 0.03, 0.03, VAC_DARK, points=14)

    # The body, with a band in the team's colour.
    part.lengthwise([
        (0.25, 0, z, 0.015, 0.015, 2.0),
        (0.245, 0, z, 0.085, 0.085, 2.4),
        (0.16, 0, z, 0.112, 0.115, 2.4),
        (-0.06, 0, z, 0.125, 0.13, 2.4),
        (-0.30, 0, z, 0.112, 0.115, 2.4),
        (-0.41, 0, z, 0.085, 0.088, 2.2),
        (-0.415, 0, z, 0.015, 0.015, 2.0),
    ], VAC_BODY, points=30, samples=3)
    part.along((0, 0.135, z), (0, 0.085, z), 0.13, 0.133, TEAM, power=2.4, points=30, up=(0, 0, 1))
    part.along((0, -0.205, z), (0, -0.25, z), 0.132, 0.136, VAC_DARK, power=2.4, points=30, up=(0, 0, 1))

    # The funnel it all goes into, flaring out to a red lip.
    part.lengthwise([
        (-0.39, 0, z, 0.072, 0.072, 2.0),
        (-0.47, 0, z, 0.076, 0.076, 2.0),
        (-0.57, 0, z, 0.118, 0.118, 2.0),
        (MOUTH_Y, 0, z, 0.178, 0.178, 2.0),
    ], VAC_METAL, points=30, samples=3)
    lip = [Vector((0.178 * math.cos(a / 24 * math.tau), MOUTH_Y, z + 0.178 * math.sin(a / 24 * math.tau))) for a in range(25)]
    part.tube(lip, 0.024, VAC_RED, points=10, smooth=False)
    part.along((0, MOUTH_Y + 0.004, z), (0, MOUTH_Y - 0.004, z), 0.16, 0.16, VAC_DARK, points=26, up=(0, 0, 1))
    for angle in (0.0, math.pi / 2):
        offset = Vector((math.cos(angle), 0, math.sin(angle))) * 0.15
        center = Vector((0, MOUTH_Y - 0.008, z))
        part.along(center - offset, center + offset, 0.012, 0.012, VAC_METAL, points=8)

    # The tank on top, where you can see the air swirling.
    part.upright([
        (z + 0.10, 0.0, 0.08, 0.08, 2.2),
        (z + 0.125, 0.0, 0.082, 0.082, 2.2),
        (z + 0.13, 0.0, 0.07, 0.07, 2.2),
        (z + 0.15, 0.0, 0.066, 0.066, 2.2),
    ], VAC_METAL, points=24, samples=2)
    part.upright([
        (z + 0.14, 0.0, 0.066, 0.066, 2.0),
        (z + 0.22, 0.0, 0.074, 0.074, 2.0),
        (z + 0.30, 0.0, 0.066, 0.066, 2.0),
    ], VAC_GLASS, points=24, samples=3)
    part.ellipsoid((0, 0, z + 0.31), (0.07, 0.07, 0.03), VAC_RED, rings=8, points=20)

    # A hose from the tank to the funnel, and the vent at the back.
    part.tube([(0.04, -0.02, z + 0.26), (0.10, -0.12, z + 0.22), (0.11, -0.28, z + 0.13), (0.06, -0.42, z + 0.07)], 0.02, VAC_DARK, points=10, samples=4)
    part.along((0, 0.255, z), (0, 0.235, z), 0.07, 0.07, VAC_DARK, points=22, up=(0, 0, 1))
    part.displace(lambda co: co * VACUUM_SCALE)
    return part


def render_checks(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera = modeling.setup_preview(target=(0, 0.1, 0.25), distance=2.2, angle=(30.0, 22.0), size=(720, 480), samples=16)
    row = {
        "Hand_L_Relaxed": (0.75, 0, 0), "Forearm_L": (0.75, 0, 0), "Hand_R_Relaxed": (0.45, 0, 0),
        "Hand_L_Open": (0.15, 0, 0), "Hand_R_Open": (-0.15, 0, 0), "Hand_L_Fist": (-0.45, 0, 0), "Hand_R_Fist": (-0.75, 0, 0),
        "Hammer": (-1.1, 0, 0.1), "UpperArm_L": (0.75, FOREARM_LENGTH, 0), "Vacuum": (-1.75, 0.2, 0.1),
    }
    for name, place in row.items():
        bpy.data.objects[name].location = place
    bpy.data.objects["Forearm_R"].location = (0.45, 0, 0)
    bpy.data.objects["UpperArm_R"].location = (0.45, FOREARM_LENGTH, 0)
    scene.render.filepath = str(folder / "hands.png")
    bpy.ops.render.render(write_still=True)
    modeling.aim_camera(camera, (0, 0.1, 0.25), 2.2, (150.0, 25.0))
    scene.render.filepath = str(folder / "hands_back.png")
    bpy.ops.render.render(write_still=True)
    for name, angle in (("vacuum_side", (55.0, 12.0)), ("vacuum_front", (-20.0, 15.0)), ("vacuum_back", (160.0, 25.0))):
        modeling.aim_camera(camera, (-1.75, 0.0, 0.25), 1.7, angle)
        scene.render.filepath = str(folder / f"{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    # The right hand's thumb is on the model's +X side, because +X is the model's left.
    parts = [
        build_upper_arm("UpperArm_L"), build_upper_arm("UpperArm_R"),
        build_forearm("Forearm_L"), build_forearm("Forearm_R"),
        build_relaxed_hand("Hand_L_Relaxed", -1.0), build_relaxed_hand("Hand_R_Relaxed", 1.0),
        build_open_hand("Hand_L_Open", -1.0), build_open_hand("Hand_R_Open", 1.0),
        build_fist("Hand_L_Fist", -1.0), build_fist("Hand_R_Fist", 1.0),
        build_hammer(),
        build_vacuum(),
    ]
    for part in parts:
        part.to_object(materials)

    root = Path(__file__).resolve().parents[2]
    output = root / "Assets/Game/Models/FirstPersonHands.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)

    if "--sheet" in sys.argv:
        render_checks(sys.argv[sys.argv.index("--sheet") + 1])


main()

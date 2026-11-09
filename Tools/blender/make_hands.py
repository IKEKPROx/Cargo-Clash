"""Builds what you see of yourself in first person, and writes Assets/Game/Models/FirstPersonHands.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_hands.py

Add `-- --sheet folder` at the end to also render check images.

Each sleeve and hand is a separate rigid object with its origin at the wrist, and there are no
animations in the file. FirstPersonRig poses them in code, because where the hands go depends on the
size of the cargo being held. Axes: -Y is along the fingers, +X is the model's left, +Z is up.
"""
import importlib
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Vector
from modeling import MeshPart

TEAM = "Char_Team"
BAND = "Char_Band"
GLOVE = "Char_Glove"
WOOD = "Hammer_Handle"
STEEL = "Hammer_Head"
HAMMER_BAND = "Hat_Band"

PALETTE = {
    TEAM: (0.95, 0.32, 0.03, 0.5),
    BAND: (0.92, 0.92, 0.85, 0.3),
    GLOVE: (0.9, 0.87, 0.75, 0.5),
    WOOD: (0.35, 0.16, 0.05, 0.6),
    STEEL: (0.32, 0.34, 0.38, 0.3),
    HAMMER_BAND: (0.75, 0.03, 0.04, 0.4),
}

# Where the hammer's handle runs through the fist, and how far along the handle its head sits.
GRIP_Y = -0.105
HEAD_Z = 0.5


def add_cuff(part):
    part.lengthwise([
        (0.055, 0, 0, 0.062, 0.062, 2.0),
        (0.045, 0, 0, 0.086, 0.086, 2.0),
        (0.015, 0, 0, 0.09, 0.09, 2.0),
        (-0.015, 0, 0, 0.082, 0.082, 2.0),
        (-0.03, 0, 0, 0.066, 0.066, 2.0),
    ], GLOVE, points=20, samples=2)


def add_finger(part, base, tip, radius):
    part.tube([base, tuple((Vector(base) + Vector(tip)) * 0.5 + Vector((0, 0, 0.006))), tip], radius, GLOVE, points=12, samples=3)
    part.ellipsoid(tip, (radius, radius, radius), GLOVE, rings=8, points=12)


def build_open_hand(name, thumb_side):
    """A chubby three-fingered glove, palm down with the fingers slightly curled."""
    part = MeshPart(name)
    add_cuff(part)
    part.rounded_box((0, -0.088, 0), (0.082, 0.088, 0.046), GLOVE, power=3.2, rings=12, points=26)
    for x, length in ((-0.052, 0.082), (0.0, 0.094), (0.052, 0.082)):
        base = (x, -0.158, 0.002)
        tip = (x, -0.158 - length, -0.016)
        add_finger(part, base, tip, 0.029)
    thumb = thumb_side
    add_finger(part, (thumb * 0.066, -0.05, -0.006), (thumb * 0.128, -0.118, -0.012), 0.031)
    return part


def build_fist(name, thumb_side):
    """Closed around a handle that runs straight up through the fist, with the back of the hand outward."""
    part = MeshPart(name)
    add_cuff(part)
    part.rounded_box((0, -0.08, 0), (0.058, 0.082, 0.07), GLOVE, power=3.2, rings=12, points=26)
    for z in (-0.046, 0.0, 0.046):
        part.ellipsoid((thumb_side * 0.012, GRIP_Y - 0.052, z), (0.054, 0.034, 0.025), GLOVE, rings=8, points=14)
    part.ellipsoid((thumb_side * 0.05, GRIP_Y - 0.012, 0.03), (0.03, 0.058, 0.03), GLOVE, rings=8, points=14)
    return part


def build_arm(name):
    """A sleeve from the wrist back past the elbow. It always runs off the bottom of the screen."""
    part = MeshPart(name)
    part.lengthwise([
        (0.03, 0, 0, 0.012, 0.012, 2.0),
        (0.035, 0, 0, 0.076, 0.076, 2.0),
        (0.3, 0, 0, 0.088, 0.088, 2.0),
        (0.7, 0, 0, 0.102, 0.102, 2.0),
        (1.15, 0, 0, 0.112, 0.112, 2.0),
        (1.16, 0, 0, 0.012, 0.012, 2.0),
    ], TEAM, points=24, samples=3)
    part.lengthwise([
        (0.09, 0, 0, 0.08, 0.08, 2.0),
        (0.1, 0, 0, 0.083, 0.083, 2.0),
        (0.17, 0, 0, 0.085, 0.085, 2.0),
        (0.18, 0, 0, 0.082, 0.082, 2.0),
    ], BAND, points=24, samples=2)
    return part


def build_hammer():
    """Modelled for the fist above: the handle runs up +Z, and the head strikes along -Y."""
    part = MeshPart("Hammer")
    part.along((0, GRIP_Y, -0.17), (0, GRIP_Y, HEAD_Z + 0.03), 0.028, 0.028, WOOD, points=12)
    keys = [(0.0, 0.05), (0.02, 0.8), (0.07, 0.96), (0.14, 1.0), (0.86, 1.0), (0.93, 0.96), (0.98, 0.8), (1.0, 0.05)]
    head_from = (0, GRIP_Y + 0.2, HEAD_Z)
    head_to = (0, GRIP_Y - 0.2, HEAD_Z)
    part.along(head_from, head_to, 0.115, 0.115, STEEL, points=26, keys=keys, up=(0, 0, 1))
    for y in (GRIP_Y + 0.125, GRIP_Y - 0.125):
        part.along((0, y + 0.024, HEAD_Z), (0, y - 0.024, HEAD_Z), 0.122, 0.122, HAMMER_BAND, points=26, up=(0, 0, 1))
    return part


def render_checks(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera = modeling.setup_preview(target=(0, -0.1, 0.1), distance=1.6, angle=(35.0, 25.0), size=(640, 420), samples=16)
    positions = {
        "Arm_L": (0.42, 0, 0), "Hand_L_Open": (0.42, 0, 0), "Arm_R": (-0.42, 0, 0), "Hand_R_Fist": (-0.42, 0, 0), "Hammer": (-0.42, 0, 0),
        "Hand_L_Fist": (0.42, 0, 0.45), "Hand_R_Open": (-0.42, 0, 0.45),
    }
    for name, place in positions.items():
        bpy.data.objects[name].location = place
    scene.render.filepath = str(folder / "hands.png")
    bpy.ops.render.render(write_still=True)
    modeling.aim_camera(camera, (0, -0.1, 0.1), 1.6, (120.0, 15.0))
    scene.render.filepath = str(folder / "hands_side.png")
    bpy.ops.render.render(write_still=True)


def main():
    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    # The right hand's thumb is on the model's +X side, because +X is the model's left.
    parts = [
        build_arm("Arm_L"), build_arm("Arm_R"),
        build_open_hand("Hand_L_Open", -1.0), build_open_hand("Hand_R_Open", 1.0),
        build_fist("Hand_L_Fist", -1.0), build_fist("Hand_R_Fist", 1.0),
        build_hammer(),
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

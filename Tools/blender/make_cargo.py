"""Builds the three cargo models and writes Assets/Game/Models/Cargo_Small.fbx, _Medium and _Large.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_cargo.py

Add `-- --sheet folder` at the end to also render check images. Each model is centred on its origin
and fits the box collider in `CargoClashBuilder`. Its eyes and mouths are separate objects, so the
game can change the expression.
"""
import importlib
import math
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Euler, Vector
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
    WHITE: (0.95, 0.95, 0.95, 0.2),
    BLACK: (0.01, 0.01, 0.015, 0.1),
}


def add_face(materials, half, eye_radius, eye_x, eye_z, mouth_z, mouth_size, brow=False):
    """A face on the front and on the back, so one is usually in view however the box lands."""
    for side_name, sign in (("F", -1.0), ("B", 1.0)):
        y = sign * half[1]
        for eye_name, x_sign in (("L", 1.0), ("R", -1.0)):
            eye = MeshPart(f"Eye_{side_name}_{eye_name}")
            eye.ellipsoid((0, 0, 0), (eye_radius, eye_radius * 0.34, eye_radius * 1.14), WHITE, rings=10, points=18)
            eye.ellipsoid((-x_sign * sign * -eye_radius * 0.08, sign * eye_radius * 0.3, -eye_radius * 0.12),
                          (eye_radius * 0.55, eye_radius * 0.14, eye_radius * 0.62), BLACK, rings=8, points=14)
            eye.to_object(materials, location=(x_sign * eye_x, y, eye_z))
        mouth = MeshPart(f"Mouth_{side_name}")
        mouth.ellipsoid((0, 0, 0), (mouth_size, eye_radius * 0.2, mouth_size * 0.3), BLACK, rings=8, points=14)
        mouth.to_object(materials, location=(0, y, mouth_z))


def build_small(materials):
    half = (0.3, 0.3, 0.3)
    body = MeshPart("Body")
    body.rounded_box((0, 0, 0), half, CARDBOARD, power=7.0)
    # A seam around the lid, tape over it, and a shipping label on top.
    body.rounded_box((0, 0, 0.14), (0.303, 0.303, 0.012), CARDBOARD_DARK, power=7.0, rings=6)
    body.rounded_box((0, 0, 0), (0.075, 0.305, 0.305), TAPE, power=7.0)
    body.rounded_box((0, 0, 0), (0.305, 0.075, 0.305), TAPE, power=7.0)
    body.rounded_box((0.165, -0.165, 0.303), (0.095, 0.075, 0.006), LABEL, power=5.0, rings=6, points=20)
    body.rounded_box((-0.17, 0.17, 0.303), (0.06, 0.06, 0.006), STICKER, power=3.0, rings=6, points=20)
    body.to_object(materials)
    add_face(materials, half, eye_radius=0.075, eye_x=0.135, eye_z=0.03, mouth_z=-0.13, mouth_size=0.05)
    return half


def build_medium(materials):
    half = (0.52, 0.46, 0.46)
    hx, hy, hz = half
    body = MeshPart("Body")
    body.rounded_box((0, 0, 0), (hx - 0.012, hy - 0.012, hz - 0.012), CRATE, power=9.0, rings=18, points=44)

    # A timber frame along all twelve edges, with steel brackets on the corners.
    beam = 0.062
    x, y, z = hx - beam, hy - beam, hz - beam
    for a in (-1, 1):
        for b in (-1, 1):
            body.rounded_box((0, a * y, b * z), (hx, beam, beam), CRATE_FRAME, power=5.0, rings=8, points=16)
            body.rounded_box((a * x, 0, b * z), (beam, hy, beam), CRATE_FRAME, power=5.0, rings=8, points=16)
            body.rounded_box((a * x, b * y, 0), (beam, beam, hz), CRATE_FRAME, power=5.0, rings=8, points=16)
            for c in (-1, 1):
                body.rounded_box((a * (hx - 0.05), b * (hy - 0.05), c * (hz - 0.05)), (0.075, 0.075, 0.075), STEEL,
                                 power=4.0, rings=6, points=14)
    # Diagonal braces on the top and ends, which are the sides without a face.
    brace = (0.44, 0.042, 0.022)
    top = Euler((0.0, 0.0, math.atan2(hy, hx)), "XYZ").to_quaternion()
    body.rounded_box((0, 0, hz - 0.012), brace, CRATE_FRAME, power=5.0, rings=6, points=16, rotation=top)
    for sign in (-1, 1):
        end = Euler((math.atan2(hz, hy) + math.pi / 2, 0.0, 0.0), "XYZ").to_quaternion()
        body.rounded_box((sign * (hx - 0.012), 0, 0), (0.022, 0.042, 0.40), CRATE_FRAME, power=5.0, rings=6, points=16, rotation=end)
    body.to_object(materials)
    add_face(materials, half, eye_radius=0.105, eye_x=0.2, eye_z=0.06, mouth_z=-0.19, mouth_size=0.08)
    return half


def build_large(materials):
    half = (1.16, 0.56, 0.53)
    hx, hy, hz = half
    body = MeshPart("Body")
    body.rounded_box((0, 0, 0), (hx - 0.02, hy - 0.02, hz - 0.02), CHEST, power=7.0, rings=18, points=48)
    # The lid line, gold bands, corner guards and a star on top say this one is worth the trouble.
    body.rounded_box((0, 0, 0.17), (hx - 0.012, hy - 0.012, 0.024), CHEST_DARK, power=7.0, rings=6, points=48)
    for x in (-0.72, 0.72):
        body.rounded_box((x, 0, 0), (0.085, hy - 0.002, hz - 0.002), GOLD, power=7.0, rings=14, points=30)
    for a in (-1, 1):
        for b in (-1, 1):
            for c in (-1, 1):
                body.rounded_box((a * (hx - 0.1), b * (hy - 0.1), c * (hz - 0.1)), (0.115, 0.115, 0.115), GOLD,
                                 power=3.2, rings=8, points=18)
        # Handles at each end, where the two carriers hold on.
        x = a * (hx + 0.03)
        body.along((x, -0.2, 0.08), (x, 0.2, 0.08), 0.045, 0.045, STEEL, points=12)
        for y in (-0.2, 0.2):
            body.along((a * (hx - 0.02), y, 0.08), (x, y, 0.08), 0.05, 0.05, STEEL, points=10)

    star = []
    for thickness in (hz - 0.002, hz + 0.022):
        ring = []
        for point in range(10):
            radius = 0.21 if point % 2 == 0 else 0.09
            angle = point * math.pi / 5 + math.pi / 2
            ring.append(Vector((radius * math.cos(angle), radius * math.sin(angle), thickness)))
        star.append(ring)
    body.loft(star, GOLD)
    body.to_object(materials)
    add_face(materials, half, eye_radius=0.135, eye_x=0.28, eye_z=0.0, mouth_z=-0.27, mouth_size=0.11)
    return half


def build_and_export(name, builder, root):
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
    scale = {"Cargo_Small": 2.6, "Cargo_Medium": 4.2, "Cargo_Large": 6.4}[name]
    for view, angle in (("front", (24.0, 18.0)), ("game", (0.0, 53.0)), ("back", (200.0, 30.0))):
        modeling.aim_camera(camera, (0, 0, 0), scale, angle)
        scene.render.filepath = str(Path(folder) / f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)


def main():
    root = Path(__file__).resolve().parents[2]
    (root / "Assets/Game/Models").mkdir(parents=True, exist_ok=True)
    folder = None
    if "--sheet" in sys.argv:
        folder = sys.argv[sys.argv.index("--sheet") + 1]
        Path(folder).mkdir(parents=True, exist_ok=True)
    for name, builder in (("Cargo_Small", build_small), ("Cargo_Medium", build_medium), ("Cargo_Large", build_large)):
        build_and_export(name, builder, root)
        if folder:
            render_checks(name, folder)


if __name__ == "__main__":
    main()

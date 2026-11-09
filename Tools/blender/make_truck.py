"""Builds the trucks and the traffic cone, and writes Truck.fbx, TitleTruck.fbx and TrafficCone.fbx
into Assets/Game/Models.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_truck.py

Add `-- --sheet folder` at the end to also render check images. Both trucks keep the look and size
of the truck from the original start screen: blue cab, orange cargo box, six big wheels. The front
points along +Y here, which is -Z in Unity. `CargoBed` is a separate object with its origin in the
middle of its floor, because the helicopter lifts it off.

Truck is the crashed one in the level. TitleTruck is the same truck before the crash, for the start
screen. Its wheels are separate objects at the origin, because RoverTerrainAnimator moves each one
over the bumps, and Unity adds the axles and shock absorbers that stretch between them.
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

BLUE = "Truck_Blue"
BLUE_DARK = "Truck_BlueDark"
ORANGE = "Truck_Orange"
ORANGE_DARK = "Truck_OrangeDark"
DARK = "Truck_Dark"
TIRE = "Truck_Tire"
STEEL = "Truck_Steel"
GLASS = "Truck_Glass"
WHITE = "Truck_White"
AMBER = "Truck_Amber"
RED = "Truck_Red"
LIGHT = "Truck_Light"
CONE = "Cone_Orange"
CONE_BAND = "Cone_Band"
CONE_BASE = "Cone_Base"

PALETTE = {
    BLUE: (0.03, 0.2, 0.62, 0.4),
    BLUE_DARK: (0.015, 0.08, 0.3, 0.4),
    ORANGE: (0.95, 0.2, 0.02, 0.45),
    ORANGE_DARK: (0.55, 0.09, 0.01, 0.45),
    DARK: (0.03, 0.035, 0.045, 0.6),
    TIRE: (0.015, 0.015, 0.018, 0.75),
    STEEL: (0.42, 0.44, 0.48, 0.25),
    GLASS: (0.3, 0.6, 0.8, 0.08),
    WHITE: (0.92, 0.92, 0.88, 0.4),
    AMBER: (1.0, 0.55, 0.03, 0.3),
    RED: (0.75, 0.03, 0.03, 0.3),
    LIGHT: (1.0, 0.92, 0.6, 0.2),
    CONE: (1.0, 0.28, 0.02, 0.4),
    CONE_BAND: (0.95, 0.95, 0.9, 0.4),
    CONE_BASE: (0.04, 0.04, 0.05, 0.6),
}

AXLES = (2.75, -0.15, -2.95)
WHEEL_X = 2.36
WHEEL_Z = 1.05
# The middle of the cargo box floor. Everything on the box is measured from here.
BED_PIVOT = Vector((0.0, -1.66, 1.88))


def add_wheel(part, x, y, z, outward):
    """A chunky tyre with blocks of tread and a bolted hub, turning around the X axis."""
    width = 0.38
    keys = [(-1.0, 0.04), (-0.98, 0.5), (-0.93, 0.86), (-0.74, 0.97), (-0.3, 1.0), (0.3, 1.0), (0.74, 0.97),
            (0.93, 0.86), (0.98, 0.5), (1.0, 0.04)]
    rings = []
    for t, factor in keys:
        center = Vector((x + t * width, y, z))
        rings.append(modeling.section(center, Vector((0, 1, 0)), Vector((0, 0, 1)), 0.98 * factor, 0.98 * factor, 2.0, 44))
    part.loft(rings, TIRE)

    blocks = 16
    for i in range(blocks):
        angle = i * math.tau / blocks
        offset = 0.17 if i % 2 == 0 else -0.17
        center = (x + offset, y + math.cos(angle) * 0.985, z + math.sin(angle) * 0.985)
        rotation = Euler((angle + math.pi / 2, 0.0, 0.0), "XYZ").to_quaternion()
        part.rounded_box(center, (0.16, 0.135, 0.055), TIRE, power=4.0, rings=6, points=12, rotation=rotation)

    face = x + outward * (width + 0.005)
    part.along((face - outward * 0.1, y, z), (face + outward * 0.03, y, z), 0.6, 0.6, STEEL, points=32, up=(0, 1, 0))
    part.along((face, y, z), (face + outward * 0.09, y, z), 0.4, 0.4, ORANGE, points=28, up=(0, 1, 0))
    part.along((face + outward * 0.06, y, z), (face + outward * 0.13, y, z), 0.17, 0.17, STEEL, points=20, up=(0, 1, 0))
    for i in range(6):
        angle = i * math.tau / 6
        bx = face + outward * 0.06
        by, bz = y + math.cos(angle) * 0.5, z + math.sin(angle) * 0.5
        part.ellipsoid((bx, by, bz), (0.04, 0.055, 0.055), DARK, rings=6, points=10)


def build_body(materials, intact=False):
    """`intact` leaves out the crash damage and the axles and wheels, which then move separately."""
    part = MeshPart("Body")

    # Frame, axles and the fuel tanks.
    part.rounded_box((0, -0.1, 1.5), (1.4, 4.1, 0.2), DARK, power=5.0, rings=10, points=28)
    if not intact:
        # The front wheel on the low side came off, which is why the truck sags that way.
        missing = {(-1, AXLES[0])}
        for y in AXLES:
            part.along((-WHEEL_X, y, WHEEL_Z), (WHEEL_X, y, WHEEL_Z), 0.1, 0.1, DARK, points=12, up=(0, 1, 0))
            part.ellipsoid((0, y, WHEEL_Z), (0.42, 0.34, 0.32), STEEL, rings=10, points=18)
            for sign in (-1, 1):
                part.along((sign * 1.3, y, 1.75), (sign * 1.55, y, WHEEL_Z), 0.09, 0.09, STEEL, points=10)
                if (sign, y) not in missing:
                    add_wheel(part, sign * WHEEL_X, y, WHEEL_Z, sign)
    for sign in (-1, 1):
        part.along((sign * 1.5, -1.0, 1.42), (sign * 1.5, 0.75, 1.42), 0.32, 0.32, STEEL, points=20, up=(1, 0, 0))
        part.rounded_box((sign * 1.5, -0.12, 1.42), (0.34, 0.06, 0.34), DARK, power=2.2, rings=8, points=18)

    # The cab: a big soft block with a slightly raised nose.
    part.rounded_box((0, 2.05, 2.56), (1.54, 1.74, 1.02), BLUE, power=4.6, rings=20, points=56)
    part.rounded_box((0, 2.08, 1.72), (1.6, 1.78, 0.2), BLUE_DARK, power=5.0, rings=10, points=40)

    # The windscreen, with a crack from whatever flew into it.
    tilt = Euler((math.radians(10.0), 0.0, 0.0), "XYZ").to_quaternion()
    part.rounded_box((0, 3.70, 3.0), (1.3, 0.13, 0.5), GLASS, power=5.0, rings=12, points=34, rotation=tilt)
    if not intact:
        centre = Vector((-0.5, 3.86, 3.1))
        for dx, dz in ((0.55, 0.26), (0.36, -0.33), (-0.42, 0.22), (-0.26, -0.34), (0.08, 0.34), (0.68, -0.05)):
            end = centre + Vector((dx, -0.06 * dz / 0.34, dz))
            mid = centre.lerp(end, 0.5) + Vector((0.04 * dz, 0, -0.04 * dx))
            part.tube([tuple(centre), tuple(mid), tuple(end)], 0.014, WHITE, points=6, samples=2)

    # Side windows, doors, a company badge, mirrors and steps.
    for sign in (-1, 1):
        part.rounded_box((sign * 1.5, 2.2, 3.03), (0.07, 0.8, 0.42), GLASS, power=5.0, rings=10, points=24)
        part.rounded_box((sign * 1.525, 2.08, 2.2), (0.035, 0.52, 0.42), WHITE, power=4.0, rings=8, points=20)
        part.rounded_box((sign * 1.55, 2.08, 2.2), (0.03, 0.26, 0.24), ORANGE, power=6.0, rings=6, points=16)
        part.rounded_box((sign * 1.555, 2.08, 2.26), (0.03, 0.26, 0.03), ORANGE_DARK, power=6.0, rings=6, points=12)
        part.along((sign * 1.5, 1.05, 2.72), (sign * 1.56, 1.2, 2.72), 0.035, 0.035, STEEL, points=8)
        part.rounded_box((sign * 1.65, 1.36, 1.56), (0.17, 0.62, 0.06), STEEL, power=5.0, rings=6, points=16)
        part.tube([(sign * 1.46, 3.4, 3.0), (sign * 1.72, 3.5, 3.08), (sign * 1.86, 3.5, 3.0)], 0.035, DARK, points=8, samples=3)
        part.rounded_box((sign * 1.9, 3.48, 2.78), (0.085, 0.05, 0.28), DARK, power=4.0, rings=8, points=14)

    # The nose: grille, bumper and lights. One headlight took the worst of the crash.
    part.rounded_box((0, 3.74, 2.0), (1.0, 0.11, 0.42), DARK, power=5.0, rings=10, points=26)
    for z in (1.74, 1.91, 2.08, 2.25):
        part.along((-0.9, 3.86, z), (0.9, 3.86, z), 0.035, 0.035, STEEL, points=8, up=(0, 1, 0))
    dent = None if intact else Euler((0.0, math.radians(-3.0), math.radians(5.0)), "XYZ").to_quaternion()
    part.rounded_box((0.02, 4.02, 1.6), (1.85, 0.24, 0.24), STEEL, power=5.0, rings=12, points=34, rotation=dent)
    for sign, lens in ((1, LIGHT), (-1, LIGHT if intact else DARK)):
        x = sign * 1.26
        part.along((x, 3.66, 2.08), (x, 3.84, 2.08), 0.27, 0.27, STEEL, points=24, up=(1, 0, 0))
        part.ellipsoid((x, 3.82, 2.08), (0.21, 0.07, 0.21), lens, rings=10, points=20)

    # Roof lights, sun visor and an exhaust stack behind the cab.
    part.rounded_box((0, 3.32, 3.53), (1.3, 0.3, 0.05), BLUE_DARK, power=4.0, rings=8, points=26)
    part.rounded_box((0, 2.05, 3.62), (1.0, 0.24, 0.07), DARK, power=4.0, rings=8, points=24)
    for x in (-0.66, 0.0, 0.66):
        part.ellipsoid((x, 2.05, 3.74), (0.21, 0.19, 0.15), AMBER, rings=10, points=18)
    part.tube([(1.2, 0.42, 1.8), (1.2, 0.42, 3.2), (1.2, 0.42, 4.05), (1.2, 0.3, 4.28)], 0.11, STEEL, points=14, samples=4)
    part.along((1.2, 0.42, 2.7), (1.2, 0.42, 3.6), 0.16, 0.16, STEEL, points=16)

    # Under the cargo box.
    for y in (-3.3, -1.66, 0.0):
        part.rounded_box((0, y, 1.72), (1.5, 0.16, 0.08), DARK, power=4.0, rings=6, points=16)
    part.to_object(materials)


def build_bed(materials):
    """Measured from the middle of the floor."""
    part = MeshPart("CargoBed")
    part.rounded_box((0, 0, 0), (1.9, 2.46, 0.12), DARK, power=6.0, rings=10, points=36)
    for sign in (-1, 1):
        x = sign * 1.78
        part.rounded_box((x, 0, 0.66), (0.12, 2.46, 0.56), ORANGE, power=7.0, rings=12, points=36)
        part.rounded_box((x, 0, 1.2), (0.17, 2.5, 0.055), DARK, power=4.0, rings=8, points=22)
        part.rounded_box((x, 0, 0.12), (0.17, 2.5, 0.055), DARK, power=4.0, rings=8, points=22)
        for y in (-1.9, -0.95, 0.0, 0.95, 1.9):
            part.rounded_box((sign * 1.91, y, 0.66), (0.045, 0.1, 0.5), ORANGE_DARK, power=4.0, rings=8, points=14)
        part.rounded_box((sign * 1.93, -0.47, 0.72), (0.015, 0.36, 0.12), WHITE, power=4.0, rings=6, points=12)
        part.rounded_box((sign * 1.93, 0.47, 0.72), (0.015, 0.36, 0.12), WHITE, power=4.0, rings=6, points=12)
    for y, thickness in ((2.34, 0.16), (-2.38, 0.11)):
        part.rounded_box((0, y, 0.66), (1.9, thickness, 0.56), ORANGE, power=7.0, rings=12, points=36)
        part.rounded_box((0, y, 1.2), (1.95, thickness + 0.05, 0.055), DARK, power=4.0, rings=8, points=22)
    for sign in (-1, 1):
        part.rounded_box((sign * 1.42, -2.5, 0.28), (0.24, 0.035, 0.12), RED, power=4.0, rings=6, points=14)
    # Yellow and dark warning stripes across the tailgate.
    for i in range(7):
        x = -1.2 + i * 0.4
        stripe = AMBER if i % 2 == 0 else DARK
        part.rounded_box((x, -2.5, 0.86), (0.2, 0.02, 0.14), stripe, power=8.0, rings=4, points=12)
    # Cross beams, for when it dangles under the helicopter.
    for y in (-1.5, 0.0, 1.5):
        part.rounded_box((0, y, -0.14), (1.82, 0.11, 0.065), STEEL, power=4.0, rings=6, points=14)
    part.to_object(materials, location=tuple(BED_PIVOT))


def build_loose_wheel(materials):
    """The missing front wheel, lying flat where it rolled to."""
    part = MeshPart("LooseWheel")
    add_wheel(part, 0.0, 0.0, 0.0, 1)
    part.along((-0.3, 0, 0), (-0.72, 0, 0), 0.1, 0.1, DARK, points=12, up=(0, 1, 0))
    wheel = part.to_object(materials, location=(-3.9, 6.5, 0.39))
    wheel.rotation_euler = (0.0, math.radians(-82.0), math.radians(24.0))


def build_title_wheel(materials, name, outward):
    part = MeshPart(name)
    add_wheel(part, 0.0, 0.0, 0.0, outward)
    part.to_object(materials)


def show_title_wheels(materials):
    """Only for the check images: copies of the wheels on the axles. These don't get exported."""
    for y in AXLES:
        for sign, name in ((1, "Wheel_L"), (-1, "Wheel_R")):
            source = bpy.data.objects[name]
            copy = bpy.data.objects.new(f"Preview_{name}_{y}", source.data)
            copy.location = (sign * WHEEL_X, y, WHEEL_Z)
            bpy.context.scene.collection.objects.link(copy)


def build_cone(materials):
    part = MeshPart("TrafficCone")
    part.rounded_box((0, 0, 0.035), (0.33, 0.33, 0.035), CONE_BASE, power=5.0, rings=6, points=24)
    keys = [(0.05, 0.215), (0.2, 0.185), (0.34, 0.155), (0.5, 0.115), (0.66, 0.085), (0.8, 0.055), (0.84, 0.03), (0.85, 0.005)]
    rings = [modeling.section(Vector((0, 0, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), r, r, 2.0, 22) for z, r in keys]
    part.loft(rings, lambda ring, point: CONE_BAND if ring == 2 else CONE)
    part.to_object(materials)


def render_truck_checks(folder, prefix="truck"):
    camera = modeling.setup_preview(target=(0, 0, 1.8), distance=19.0, angle=(38.0, 16.0), size=(640, 460), samples=20, lens=50)
    scene = bpy.context.scene
    for view, angle in (("front", (150.0, 16.0)), ("side", (215.0, 22.0)), ("game", (180.0, 53.0)), ("back", (30.0, 22.0))):
        modeling.aim_camera(camera, (0, 0, 1.8), 19.0, angle)
        scene.render.filepath = str(Path(folder) / f"{prefix}_{view}.png")
        bpy.ops.render.render(write_still=True)


def main():
    root = Path(__file__).resolve().parents[2]
    folder = None
    if "--sheet" in sys.argv:
        folder = sys.argv[sys.argv.index("--sheet") + 1]
        Path(folder).mkdir(parents=True, exist_ok=True)

    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    build_body(materials)
    build_bed(materials)
    build_loose_wheel(materials)
    output = root / "Assets/Game/Models/Truck.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)
    if folder:
        render_truck_checks(folder)

    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    build_body(materials, intact=True)
    build_bed(materials)
    build_title_wheel(materials, "Wheel_L", 1)
    build_title_wheel(materials, "Wheel_R", -1)
    output = root / "Assets/Game/Models/TitleTruck.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)
    if folder:
        show_title_wheels(materials)
        render_truck_checks(folder, "title")

    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    build_cone(materials)
    output = root / "Assets/Game/Models/TrafficCone.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)


if __name__ == "__main__":
    main()

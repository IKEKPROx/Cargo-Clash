"""Builds the extraction helicopter and writes Assets/Game/Models/Helicopter.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_helicopter.py

Add `-- --preview out.png` at the end to also render a check image. The rotors and the hook are
separate objects so the game can move them; `Winch` marks where the cable leaves the belly.
"""
import importlib
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Vector
from modeling import MeshPart

YELLOW = "Heli_Yellow"
WHITE = "Heli_White"
DARK = "Heli_Dark"
STEEL = "Heli_Steel"
GLASS = "Heli_Glass"
RED = "Heli_Red"

PALETTE = {
    YELLOW: (0.98, 0.56, 0.03, 0.35),
    WHITE: (0.88, 0.86, 0.80, 0.4),
    DARK: (0.035, 0.04, 0.05, 0.55),
    STEEL: (0.32, 0.34, 0.38, 0.3),
    GLASS: (0.30, 0.62, 0.84, 0.08),
    RED: (0.78, 0.05, 0.05, 0.35),
}


def body_paint(y_of_ring, ring_points):
    """Yellow on top, a white belly, a dark stripe between them, hazard bands on the tail."""

    def pick(ring, point):
        y = y_of_ring[ring]
        angle = (point + 0.5) / ring_points * 2.0 * math.pi
        height = math.sin(angle)
        if 4.0 < y < 6.2 and int((y - 4.0) / 0.44) % 2 == 1:
            return DARK
        if y < 3.5 and -0.2 < height < 0.02:
            return DARK
        if y < 3.5 and height <= -0.2:
            return WHITE
        return YELLOW

    return pick


def build_body():
    part = MeshPart("Body")

    # Fuselage and tail boom are one continuous loft: (y, x, z, radius x, radius z, power)
    keys = [
        (-3.30, 0, -0.26, 0.02, 0.02, 2.2),
        (-3.24, 0, -0.26, 0.36, 0.30, 2.2),
        (-3.06, 0, -0.24, 0.64, 0.56, 2.3),
        (-2.72, 0, -0.20, 0.94, 0.84, 2.4),
        (-2.20, 0, -0.13, 1.20, 1.08, 2.5),
        (-1.40, 0, -0.06, 1.37, 1.26, 2.5),
        (-0.40, 0, 0.00, 1.42, 1.30, 2.5),
        (0.60, 0, 0.07, 1.30, 1.17, 2.4),
        (1.50, 0, 0.18, 1.04, 0.91, 2.3),
        (2.20, 0, 0.30, 0.76, 0.67, 2.2),
        (2.90, 0, 0.44, 0.51, 0.47, 2.1),
        (3.60, 0, 0.56, 0.37, 0.35, 2.0),
        (4.60, 0, 0.72, 0.29, 0.29, 2.0),
        (5.60, 0, 0.88, 0.24, 0.24, 2.0),
        (6.60, 0, 1.02, 0.20, 0.20, 2.0),
        (7.14, 0, 1.08, 0.17, 0.17, 2.0),
        (7.26, 0, 1.10, 0.10, 0.10, 2.0),
        (7.30, 0, 1.10, 0.01, 0.01, 2.0),
    ]
    points = 48
    samples = modeling.sample_keys(keys, 6)
    y_of_ring = [s[0] for s in samples]
    rings = []
    for y, cx, cz, rx, rz, power in samples:
        rings.append(modeling.section(Vector((cx, y, cz)), Vector((1, 0, 0)), Vector((0, 0, 1)),
                                      max(rx, 0.005), max(rz, 0.005), power, points))
    part.loft(rings, body_paint(y_of_ring, points))

    # Big glass bubble across the nose, with a chin of yellow below it.
    part.ellipsoid((0, -1.88, 0.40), (1.27, 1.66, 1.14), GLASS, rings=22, points=36)

    # Window frames lie on the bubble.
    def bubble_point(y, angle, lift=1.012):
        factor = math.sqrt(max(0.0, 1.0 - ((y + 1.88) / 1.66) ** 2))
        return (math.cos(angle) * 1.27 * factor * lift, y, 0.40 + math.sin(angle) * 1.14 * factor * lift)

    for frame_y in (-2.42, -1.42):
        arc = [bubble_point(frame_y, math.radians(a)) for a in range(-14, 195, 16)]
        part.tube(arc, 0.055, DARK, samples=3)
    spine = [(0.0, y, 0.40 + 1.14 * math.sqrt(max(0.0, 1.0 - ((y + 1.88) / 1.66) ** 2)) * 1.012)
             for y in (-3.34, -3.2, -2.9, -2.5, -2.0, -1.5, -1.0, -0.6)]
    part.tube(spine, 0.06, DARK, samples=3)

    # A soft highlight strip on the glass reads as shine from far away.
    shine = [bubble_point(y, math.radians(128), 1.008) for y in (-2.32, -2.15, -1.95, -1.72, -1.52)]
    part.tube(shine, 0.05, WHITE, samples=3)
    shine = [bubble_point(y, math.radians(52), 1.008) for y in (-2.32, -2.15, -1.95, -1.72, -1.52)]
    part.tube(shine, 0.05, WHITE, samples=3)

    # Round cabin window on each side, behind the bubble.
    for side in (-1, 1):
        x = side * 1.36
        part.ellipsoid((x, 0.24, 0.30), (0.09, 0.42, 0.42), DARK, rings=10, points=20)
        part.ellipsoid((x * 1.022, 0.24, 0.30), (0.075, 0.33, 0.33), GLASS, rings=10, points=20)

    # Engine housing on the roof, intakes on its sides, exhaust at the back.
    part.lengthwise([
        (-0.92, 0, 1.14, 0.03, 0.03, 2.4),
        (-0.84, 0, 1.15, 0.36, 0.28, 2.4),
        (-0.50, 0, 1.19, 0.62, 0.45, 2.6),
        (0.30, 0, 1.24, 0.76, 0.56, 2.8),
        (1.20, 0, 1.18, 0.60, 0.42, 2.6),
        (1.86, 0, 1.08, 0.31, 0.21, 2.4),
        (1.98, 0, 1.06, 0.03, 0.03, 2.4),
    ], YELLOW)
    for side in (-1, 1):
        part.ellipsoid((side * 0.72, -0.1, 1.28), (0.08, 0.42, 0.20), DARK, rings=10, points=18)
    part.along((0, 1.86, 1.08), (0, 2.34, 1.02), 0.16, 0.16, DARK)

    # Mast.
    part.along((0, 0.15, 1.62), (0, 0.15, 2.20), 0.15, 0.15, DARK)
    part.along((0, 0.15, 1.72), (0, 0.15, 1.90), 0.27, 0.27, STEEL)

    # Tail surfaces.
    part.lengthwise([
        (5.28, 0, 0.90, 0.02, 0.02, 2),
        (5.34, 0, 0.90, 0.72, 0.05, 2),
        (5.56, 0, 0.90, 1.12, 0.07, 2),
        (5.86, 0, 0.91, 1.16, 0.07, 2),
        (6.10, 0, 0.91, 0.98, 0.05, 2),
        (6.18, 0, 0.91, 0.03, 0.02, 2),
    ], YELLOW)
    fin = []
    for t, y, z, chord, thick in ((0.00, 6.80, 1.06, 0.74, 0.03), (0.06, 6.80, 1.12, 0.74, 0.08),
                                  (0.30, 6.92, 1.42, 0.68, 0.085), (0.60, 7.10, 1.84, 0.56, 0.075),
                                  (0.88, 7.28, 2.20, 0.42, 0.055), (0.97, 7.33, 2.30, 0.26, 0.035),
                                  (1.00, 7.35, 2.33, 0.05, 0.015)):
        fin.append(modeling.section(Vector((0, y, z)), Vector((0, 1, 0)), Vector((1, 0, 0)), chord, thick, 2.2, 22))
    part.loft(fin, lambda ring, point: RED if ring >= 3 else YELLOW)
    ventral = []
    for y, z, chord, thick in ((6.88, 1.00, 0.48, 0.05), (6.98, 0.72, 0.40, 0.05), (7.08, 0.46, 0.27, 0.035),
                               (7.12, 0.38, 0.06, 0.012)):
        ventral.append(modeling.section(Vector((0, y, z)), Vector((0, 1, 0)), Vector((1, 0, 0)), chord, thick, 2.2, 18))
    part.loft(ventral, DARK)

    # Skids.
    for side in (-1, 1):
        x = side * 1.34
        rail = [(x, 2.56, -1.70), (x, 2.2, -1.82), (x, 0.0, -1.84), (x, -2.1, -1.84), (x, -2.86, -1.78),
                (x, -3.24, -1.56), (x, -3.38, -1.34)]
        part.tube(rail, 0.095, DARK)
        for y in (-1.45, 1.0):
            part.tube([(side * 0.82, y, -0.98), (side * 1.16, y, -1.28), (x, y, -1.84)], 0.075, DARK, samples=4)

    # Winch drum on the belly.
    part.along((-0.42, 0.15, -1.31), (0.42, 0.15, -1.31), 0.24, 0.24, STEEL)
    part.along((-0.50, 0.15, -1.31), (-0.42, 0.15, -1.31), 0.30, 0.30, DARK)
    part.along((0.42, 0.15, -1.31), (0.50, 0.15, -1.31), 0.30, 0.30, DARK)

    # Lights.
    part.ellipsoid((0, -3.0, -0.92), (0.14, 0.14, 0.14), WHITE, rings=8, points=14)
    part.ellipsoid((0, 7.33, 2.36), (0.09, 0.09, 0.09), RED, rings=8, points=12)
    return part


def blade_rings(length, chord_root, chord_tip, thickness, droop, points=14):
    """A blade pointing along +X from the origin, with a rounded tip."""
    rings = []
    for t, chord_factor in ((0.0, 1.0), (0.10, 1.0), (0.90, 1.0), (0.97, 0.82), (0.995, 0.46), (1.0, 0.06)):
        chord = (chord_root + (chord_tip - chord_root) * t) * chord_factor
        center = Vector((length * t, 0, -droop * t * t))
        rings.append(modeling.section(center, Vector((0, 1, 0)), Vector((0.05, 0, 1)), chord,
                                      thickness * max(chord_factor, 0.2), 2.3, points))
    return rings


def build_main_rotor():
    part = MeshPart("MainRotor")
    part.ellipsoid((0, 0, 0.02), (0.36, 0.36, 0.16), STEEL, rings=10, points=20)
    part.ellipsoid((0, 0, 0.16), (0.13, 0.13, 0.13), DARK, rings=8, points=14)
    length = 4.85
    for i in range(4):
        angle = math.radians(i * 90.0)
        c, s = math.cos(angle), math.sin(angle)
        rings = []
        for ring in blade_rings(length, 0.29, 0.25, 0.05, 0.3):
            rings.append([Vector((p.x * c - p.y * s, p.x * s + p.y * c, p.z)) for p in ring])
        paint = len(rings) - 3
        part.loft(rings, lambda r, p, limit=paint: RED if r >= limit else DARK)
    return part


def build_tail_rotor():
    part = MeshPart("TailRotor")
    part.along((-0.12, 0, 0), (0.12, 0, 0), 0.13, 0.13, STEEL)
    for sign in (-1, 1):
        rings = []
        for t, factor in ((0.0, 1.0), (0.8, 1.0), (0.96, 0.75), (1.0, 0.08)):
            center = Vector((0, 0, sign * 0.98 * t))
            rings.append(modeling.section(center, Vector((0, 1, 0)), Vector((1, 0, 0)), 0.13 * factor, 0.03, 2.2, 12))
        paint = 1
        part.loft(rings, lambda r, p, limit=paint: RED if r >= limit else DARK)
    return part


def build_hook():
    """Origin at the top where the cable ties on. The sling hangs from the eye at the bottom."""
    part = MeshPart("Hook")
    part.lengthwise([
        (-0.17, 0, -0.34, 0.03, 0.03, 3.4),
        (-0.15, 0, -0.34, 0.30, 0.30, 3.4),
        (-0.11, 0, -0.34, 0.34, 0.34, 3.6),
        (0.11, 0, -0.34, 0.34, 0.34, 3.6),
        (0.15, 0, -0.34, 0.30, 0.30, 3.4),
        (0.17, 0, -0.34, 0.03, 0.03, 3.4),
    ], RED, points=24, samples=2)
    part.along((-0.2, 0, -0.34), (0.2, 0, -0.34), 0.13, 0.13, STEEL, up=(1, 0, 0))
    part.along((0, 0, 0.0), (0, 0, -0.1), 0.1, 0.1, STEEL)
    hook = [(0, 0, -0.66), (0, 0, -0.82), (-0.2, 0, -0.98), (-0.23, 0, -1.2), (-0.03, 0, -1.34), (0.2, 0, -1.22),
            (0.24, 0, -1.04)]
    part.tube(hook, 0.085, STEEL)
    return part


def main():
    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)

    build_body().to_object(materials)
    build_main_rotor().to_object(materials, location=(0, 0.15, 2.30))
    build_tail_rotor().to_object(materials, location=(0.34, 7.12, 1.56))
    build_hook().to_object(materials, location=(0, 0.15, -1.50))
    modeling.add_empty("Winch", (0, 0.15, -1.50))

    root = Path(__file__).resolve().parents[2]
    output = root / "Assets/Game/Models/Helicopter.fbx"
    output.parent.mkdir(parents=True, exist_ok=True)
    modeling.export_fbx(output)
    print("Wrote", output)

    if "--" in sys.argv:
        arguments = sys.argv[sys.argv.index("--") + 1:]
        if "--preview" in arguments:
            path = arguments[arguments.index("--preview") + 1]
            modeling.render_preview(path, target=(0, 2.0, 0.2))
            print("Preview", path)


main()

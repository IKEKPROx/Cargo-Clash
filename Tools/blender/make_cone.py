"""Builds the traffic cone and writes Assets/Game/Models/TrafficCone.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_cone.py

The trucks and the helicopter used to come from make_truck.py and make_helicopter.py. They are Astra's
now (独立建模/运输载具_v07, outside the repository): Truck.fbx, TitleTruck.fbx and Helicopter.fbx in
Assets/Game/Models are copies of the files in her 交接/旧接口兼容 folder.
"""
import importlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)
from mathutils import Vector
from modeling import MeshPart

CONE = "Cone_Orange"
CONE_BAND = "Cone_Band"
CONE_BASE = "Cone_Base"

PALETTE = {
    CONE: (1.0, 0.28, 0.02, 0.4),
    CONE_BAND: (0.95, 0.95, 0.9, 0.4),
    CONE_BASE: (0.04, 0.04, 0.05, 0.6),
}


def build_cone(materials):
    part = MeshPart("TrafficCone")
    part.rounded_box((0, 0, 0.035), (0.33, 0.33, 0.035), CONE_BASE, power=5.0, rings=6, points=24)
    keys = [(0.05, 0.215), (0.2, 0.185), (0.34, 0.155), (0.5, 0.115), (0.66, 0.085), (0.8, 0.055), (0.84, 0.03), (0.85, 0.005)]
    rings = [modeling.section(Vector((0, 0, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), r, r, 2.0, 22) for z, r in keys]
    part.loft(rings, lambda ring, point: CONE_BAND if ring == 2 else CONE)
    part.to_object(materials)


def main():
    root = Path(__file__).resolve().parents[2]
    modeling.reset_scene()
    materials = modeling.make_materials(PALETTE)
    build_cone(materials)
    output = root / "Assets/Game/Models/TrafficCone.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)


if __name__ == "__main__":
    main()

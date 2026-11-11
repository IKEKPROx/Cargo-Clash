"""Builds what you see of yourself in first person, and writes Assets/Game/Models/FirstPersonHands.fbx.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_hands.py

Add `-- --sheet folder` at the end to also render check images, and `--source folder` if Astra's
独立建模 folder isn't next to the repository.

The gloves, the hammer and the vacuum are Astra's (make_crew.py says where her files are). Her first-person
gloves have real finger bones, and each hand shape FirstPersonRig switches between is one of her poses
frozen into a rigid mesh: relaxed, open (her carrying pose), a fist for punching, and a grip that closes
round a handle. The sleeves are made here in FP_Sleeve, which FirstPersonRig swaps for the clothes you
have on.

Each part is a separate rigid object and there are no animations in the file. FirstPersonRig poses them in
code, because where the hands go depends on the size of the cargo being held and the arms bend to follow.
A hand's origin is its wrist. A forearm's origin is also the wrist and it runs back to the elbow, and an
upper arm's origin is the elbow and it runs back to the shoulder. The lengths here have to match
FirstPersonRig's. Axes: -Y is forward along the fingers, +X is the model's left, +Z is up. The fist and
the grip are turned so a handle would run straight up through them with the thumb on top; in the grip it
runs GRIP_FORWARD in front of the wrist and GRIP_INWARD toward the palm (FirstPersonRig.GripToHandle).
The hammer and the vacuum have their origin where the right hand grips, the grip running up +Z; the
hammer's head strikes along -Y and the vacuum's mouth faces -Y.
"""
import importlib
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).parent))
import make_crew as crew
import modeling

importlib.reload(modeling)
importlib.reload(crew)
from modeling import MeshPart

SLEEVE = "FP_Sleeve"

FOREARM_LENGTH = 0.62
UPPER_ARM_LENGTH = 0.54

# Where her grip closes round a handle, measured from the wrist once the hand is turned thumb up.
GRIP_FORWARD = 0.130
GRIP_INWARD = 0.061

# Her pose for each of FirstPersonRig's hand shapes.
HAND_SHAPES = (("Relaxed", "Relaxed"), ("Open", "Carry"), ("Fist", "Fist"), ("Grip", "Grip"))

# Seen this close, the gloves and tools keep more of their detail than on people across the field.
GLOVE_DETAIL = 2.6
TOOL_DETAIL = 4.0


def build_forearm(name):
    """From the wrist back to the elbow. Its open end covers the back of the glove's cuff."""
    part = MeshPart(name)
    part.lengthwise([
        (0.03, 0, 0, 0.012, 0.012, 2.0),
        (0.035, 0, 0, 0.074, 0.074, 2.0),
        (0.25, 0, 0, 0.084, 0.084, 2.0),
        (FOREARM_LENGTH, 0, 0, 0.1, 0.1, 2.0),
        (FOREARM_LENGTH + 0.005, 0, 0, 0.012, 0.012, 2.0),
    ], SLEEVE, points=24, samples=3)
    return part


def build_upper_arm(name):
    """From the elbow back to the shoulder. The round end hides the joint however far the elbow bends."""
    part = MeshPart(name)
    part.ellipsoid((0, 0, 0), (0.104, 0.104, 0.104), SLEEVE, rings=12, points=20)
    part.lengthwise([
        (0.0, 0, 0, 0.012, 0.012, 2.0),
        (0.005, 0, 0, 0.098, 0.098, 2.0),
        (UPPER_ARM_LENGTH * 0.5, 0, 0, 0.11, 0.11, 2.0),
        (UPPER_ARM_LENGTH, 0, 0, 0.122, 0.122, 2.0),
        (UPPER_ARM_LENGTH + 0.005, 0, 0, 0.012, 0.012, 2.0),
    ], SLEEVE, points=24, samples=3)
    return part


def build_hands(source):
    core = {}
    path = source / crew.HAND_POSES_FILE
    exec(compile(path.read_text(), str(path), "exec"), core)
    for side, prefix in (("R", "Right"), ("L", "Left")):
        glove, rig = crew.append(source / f"搬运工_v05_可动手部/第一人称/FP_{side}.blend", [f"FP_{side}_Glove", f"FP_{side}_Rig"])
        crew.link(glove)
        crew.link(rig)
        for shape, her_pose in HAND_SHAPES:
            core["reset_pose"](rig)
            core["apply_pose"](rig, prefix, her_pose)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            mesh = bpy.data.meshes.new_from_object(glove.evaluated_get(depsgraph))
            hand = bpy.data.objects.new(f"Hand_{side}_{shape}", mesh)
            crew.link(hand)
            if shape in ("Fist", "Grip"):
                # Thumb up, back of the hand outward, so a handle stands straight up through the fist.
                mesh.transform(Matrix.Rotation(math.radians(-90.0 if side == "R" else 90.0), 4, "Y"))
            crew.recolour(hand, crew.OUTFIT_MATERIALS["Wrecker"])
            hand = crew.reduce(hand, GLOVE_DETAIL)
            crew.finish_mesh(hand)
        bpy.data.objects.remove(glove)
        bpy.data.objects.remove(rig)


def build_tools(source):
    hammer = crew.load_tool(source / crew.HAMMER_FILE, ["Hammer_Mesh"], keep_scale=TOOL_DETAIL)
    # Her head strikes along -X. FirstPersonRig swings it along -Y.
    hammer.data.transform(Matrix.Rotation(math.radians(90.0), 4, "Z"))
    crew.load_tool(source / crew.VACUUM_FILE, ["Vacuum_Housing", "Vacuum_Turbine"], keep_scale=TOOL_DETAIL)


def render_checks(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera = modeling.setup_preview(target=(0, 0.1, 0.1), distance=2.4, angle=(30.0, 22.0), size=(900, 520), samples=16)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    row = {"UpperArm_L": (1.0, 0.62, 0), "Forearm_L": (1.0, 0, 0), "UpperArm_R": (0.72, 0.62, 0), "Forearm_R": (0.72, 0, 0),
           "Hammer": (-1.15, 0, 0.1), "Vacuum": (-1.75, 0.2, 0.1)}
    for index, (shape, _) in enumerate(HAND_SHAPES):
        row[f"Hand_L_{shape}"] = (0.42 - index * 0.3, 0, 0.25)
        row[f"Hand_R_{shape}"] = (0.42 - index * 0.3, 0, -0.1)
    for name, place in row.items():
        bpy.data.objects[name].location = place
    scene.render.filepath = str(folder / "hands.png")
    bpy.ops.render.render(write_still=True)
    modeling.aim_camera(camera, (0, 0.1, 0.1), 2.4, (150.0, 25.0))
    scene.render.filepath = str(folder / "hands_back.png")
    bpy.ops.render.render(write_still=True)
    for name, angle in (("tools_side", (70.0, 12.0)), ("tools_front", (-20.0, 15.0))):
        modeling.aim_camera(camera, (-1.45, 0.0, 0.25), 1.6, angle)
        scene.render.filepath = str(folder / f"{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    options = crew.arguments()
    modeling.reset_scene()
    materials = crew.game_materials([SLEEVE])
    for part in (build_upper_arm("UpperArm_L"), build_upper_arm("UpperArm_R"), build_forearm("Forearm_L"), build_forearm("Forearm_R")):
        part.to_object(materials)
    build_hands(options["source"])
    build_tools(options["source"])

    output = crew.ROOT / "Assets/Game/Models/FirstPersonHands.fbx"
    modeling.export_fbx(output)
    print("Wrote", output)
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            print(f"[hands] {obj.name}: {crew.triangles(obj)} triangles")

    if options["sheet"]:
        render_checks(options["sheet"])


main()

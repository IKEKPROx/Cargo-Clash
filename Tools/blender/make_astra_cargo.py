"""Brings Astra's four kinds of cargo (独立建模/互动货物_v08) into the game. Writes Assets/Game/Models/Cargo_<Kind>.fbx
and Cargo_<Kind>_Pieces.fbx for Art, Gold, Cash and Tobacco.

Her folder has each kind whole (Cargo_<Kind>), dented (_D1), smashed (_D2) and in pieces (_Pieces), built by her
脚本/build_cargo.py: a root at the middle of the bottom, front toward -Y, with the case (Body), its lid on a hinge
(Lid_Pivot) and what's inside (Contents). This script keeps her geometry and her material names and only regroups
it for the game. In each Cargo_<Kind>.fbx:

  - Stage0, Stage1, Stage2: whole, dented and smashed. The game shows one at a time as the hammer lands.
  - In each stage: Body (the case and everything in it, as one mesh, so a case is a few draw calls rather than
    dozens), Lid_Pivot with the Lid on it (the game opens it for the inspect), and Slot: the one thing the hands
    take out during the inspect (the goose, a gold bar, a bundle of notes, a cigarette), so the game can hide it
    while it's out. Which one it is matches cargo_actions.SLOT.

The pieces are hers as they are: each one an object, starting where it sits in the whole case.

Run from the repository root, after her folder is next to it (or give `-- --source folder`):

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_astra_cargo.py

Unity imports these at the crew's scale (CargoClashBuilder.CrewScale), so cargo and people keep her proportions.
"""
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
KINDS = ("Art", "Gold", "Cash", "Tobacco")
SLOT = {"Art": "Content_Goose", "Gold": "Content_Gold_01", "Cash": "Cash_Bundle_01", "Tobacco": "Cigarette_06"}


def arguments():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    source = Path(args[args.index("--source") + 1]) if "--source" in args else ROOT.parent / "独立建模"
    return source / "互动货物_v08"


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def append(path):
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.objects = list(src.objects)
    objects = [o for o in dst.objects if o is not None]
    for obj in objects:
        bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    return objects


def one_material_each(mesh):
    """Appending several of her files makes "Cargo08_Gold.001"; the game matches her materials by name."""
    for index, material in enumerate(mesh.data.materials):
        if material is not None and "." in material.name:
            base = material.name.split(".")[0]
            if base in bpy.data.materials:
                mesh.data.materials[index] = bpy.data.materials[base]
            else:
                material.name = base


def join(objects, name, frame):
    """These meshes as one, its origin and axes `frame` (a world matrix)."""
    meshes = [o for o in objects if o.type == "MESH"]
    to_frame = frame.inverted()
    copies = []
    for obj in meshes:
        copy = obj.copy()
        copy.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(copy)
        copy.parent = None
        copy.data.transform(to_frame @ obj.matrix_world)
        copy.matrix_world = Matrix.Identity(4)
        copies.append(copy)
    target = copies[0]
    if len(copies) > 1:
        with bpy.context.temp_override(active_object=target, object=target, selected_objects=copies, selected_editable_objects=copies):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    one_material_each(target)
    for polygon in target.data.polygons:
        polygon.use_smooth = polygon.use_smooth
    return target


def empty(name, parent=None, world=None):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    if world is not None:
        bpy.context.view_layer.update()
        obj.matrix_world = world
    return obj


def stage(source, kind, suffix, holder, number):
    """One damage stage of a kind, regrouped under `holder` as StageN."""
    objects = append(source / "模型" / kind / f"Cargo_{kind}{suffix}.blend")
    root = next(o for o in objects if o.parent is None and o.type == "EMPTY")
    lid = next(o for o in root.children if o.name.split(".")[0] == "Lid_Pivot")
    slot = next(o for o in root.children_recursive if o.name.split(".")[0] == SLOT[kind])
    slot_meshes = [o for o in [slot, *slot.children_recursive] if o.type == "MESH"]
    lid_meshes = [o for o in lid.children_recursive if o.type == "MESH"]
    body_meshes = [o for o in root.children_recursive if o.type == "MESH" and o not in slot_meshes and o not in lid_meshes]

    group = empty(f"Stage{number}", holder, root.matrix_world)
    body = join(body_meshes, f"Body{number}", root.matrix_world)
    body.parent = group
    pivot = empty(f"Lid_Pivot{number}", group, lid.matrix_world)
    lid_mesh = join(lid_meshes, f"Lid{number}", lid.matrix_world)
    lid_mesh.parent = pivot
    slot_mesh = join(slot_meshes, f"Slot{number}", slot.matrix_world)
    slot_mesh.parent = group
    bpy.context.view_layer.update()
    # Parenting kept the parents' placements out of the children; put them back where they were.
    body.matrix_world = root.matrix_world
    pivot.matrix_world = lid.matrix_world
    lid_mesh.matrix_world = lid.matrix_world
    slot_mesh.matrix_world = slot.matrix_world
    for obj in objects:
        bpy.data.objects.remove(obj)
    return {"body": sum(len(p.vertices) - 2 for p in body.data.polygons), "lid": sum(len(p.vertices) - 2 for p in lid_mesh.data.polygons)}


def export(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=False,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False, path_mode="STRIP",
    )
    print("Wrote", path)


def main():
    source = arguments()
    models = ROOT / "Assets/Game/Models"
    for kind in KINDS:
        reset()
        holder = empty(f"Cargo_{kind}")
        report = {}
        for number, suffix in enumerate(("", "_D1", "_D2")):
            report[f"Stage{number}"] = stage(source, kind, suffix, holder, number)
        everything = [holder, *holder.children_recursive]
        export(models / f"Cargo_{kind}.fbx", everything)
        print(f"[cargo] {kind}: {report}")

        reset()
        objects = append(source / "模型" / kind / f"Cargo_{kind}_Pieces.blend")
        for obj in objects:
            if obj.type == "MESH":
                one_material_each(obj)
        root = next(o for o in objects if o.parent is None and o.type == "EMPTY")
        root.name = f"Cargo_{kind}_Pieces"
        export(models / f"Cargo_{kind}_Pieces.fbx", [root, *root.children_recursive])
        print(f"[cargo] {kind}: {sum(o.type == 'MESH' for o in objects)} pieces")

    # The play-money print on her notes, for the bundles and the notes in the hand.
    textures = ROOT / "Assets/Game/Textures"
    shutil.copyfile(source / "模型" / "Textures" / "Cargo08_Note.png", textures / "Cargo08_Note.png")
    print("Copied the note print")


if __name__ == "__main__":
    main()

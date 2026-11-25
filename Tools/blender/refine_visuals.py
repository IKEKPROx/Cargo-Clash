"""Refine crew and fleet appearances without rebaking game actions or changing their attachment points."""
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).parent))
import make_crew as crew
import cargo_actions


def main():
    cargo_actions.bind(crew)
    source = crew.ROOT.parent / "独立建模"
    if "--crew" not in sys.argv:
        # Since step 22 the Crew_*_Visual.fbx files are Astra's own (独立建模/三体型人物精修_v23/FBX), copied in as they
        # are and put on the game by Tools > Cargo Clash > Apply Crew Appearance. Exporting them from her old v05/v06
        # files here would overwrite her work, so this only does the fleet unless asked with -- --crew.
        export_fleet(source)
        return
    crew.KEEP.update({
        "Char_Team": 0.21, "Porter_Shirt": 0.21, "Wrecker_Jacket": 0.24, "Wrecker_Shirt": 0.28,
        "Crew_Skin": 0.28, "Crew_SkinShade": 0.34, "Crew_Hair": 0.30,
        "Crew_EyeWhite": 0.40, "Crew_Dark": 0.24, "Crew_Mouth": 0.42,
        "Crew_Glove": 0.11, "Crew_Boot": 0.30, "Crew_Brass": 0.18, "Crew_Light": 0.24,
        "Crew_HatPrimary": 0.26, "Crew_HatTrim": 0.30, "Crew_Lamp": 0.40,
        "Crew_Beacon": 0.40, "Crew_LensBlue": 0.40, "Crew_LensAmber": 0.40,
        "Hammer_Head": 0.08, "Hammer_Handle": 0.08, "Hammer_Grip": 0.10,
        "Vacuum_Housing": 0.035, "Vacuum_Red": 0.04, "Vacuum_Steel": 0.04,
        "Heli_Dark": 0.04, "Vacuum_Glass": 0.05, "Vacuum_Ivory": 0.05,
    })
    for body in ("Short", "Medium", "Tall"):
        rig, hands, meshes, report, _ = crew.load_body(source, body)
        if body == "Medium":
            report["FP_Arms"] = crew.triangles(cargo_actions.first_person_arms(rig, meshes["Porter"]))
        hammer, vacuum = crew.attach_tools(rig, source, hands)
        report.update(Hammer=crew.triangles(hammer), Vacuum=crew.triangles(vacuum))
        bpy.data.objects.remove(bpy.data.objects["DizzyStars"], do_unlink=True)
        crew.rest(rig)
        rig.animation_data_clear()
        bpy.ops.export_scene.fbx(
            filepath=str(crew.ROOT / f"Assets/Game/Models/Crew_{body}_Visual.fbx"),
            use_selection=False, object_types={"ARMATURE", "MESH"},
            use_mesh_modifiers=False, mesh_smooth_type="FACE",
            add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=False, path_mode="STRIP",
        )
        print(f"[Refine] {body} appearance:", report, flush=True)
    export_fleet(source)


def export_fleet(source):
    for master, name in (("Truck_07", "TitleTruck"), ("TruckCrashed_07", "Truck"), ("Helicopter_07", "Helicopter")):
        bpy.ops.wm.open_mainfile(filepath=str(source / f"运输载具_v07/模型/{master}/{master}.blend"))
        for obj in list(bpy.context.scene.objects):
            if obj.type != "MESH":
                continue
            modifier = obj.modifiers.new("Presentation detail", "DECIMATE")
            modifier.ratio = 0.90 if name == "Helicopter" else (0.65 if "Wheel" in obj.name else 0.78)
            modifier.use_collapse_triangulate = True
            crew.apply_modifier(obj, modifier)

        # Keep the game's wheel samples, pivots and detachable bed at their existing attachment points.
        if name == "TitleTruck":
            left, right = bpy.data.objects["Wheel_FL"], bpy.data.objects["Wheel_FR"]
            for obj in list(bpy.context.scene.objects):
                if obj.name.startswith("Wheel_") and obj not in (left, right):
                    bpy.data.objects.remove(obj, do_unlink=True)
            left.name, right.name = "Wheel_L", "Wheel_R"
            left.location = right.location = (0, 0, 0)
        if name == "Truck":
            crew.join([bpy.data.objects["Body"]] + [obj for obj in bpy.context.scene.objects
                      if obj.name.startswith("Wheel_")], "Body")
        if name != "Helicopter":
            bed = bpy.data.objects["CargoBed"]
            crew.join([bed] + [obj for obj in bed.children if obj.type == "MESH"], "CargoBed")
        bpy.ops.export_scene.fbx(
            filepath=str(crew.ROOT / f"Assets/Game/Models/{name}.fbx"),
            use_selection=False, object_types={"MESH", "EMPTY"},
            use_mesh_modifiers=True, mesh_smooth_type="FACE", bake_anim=False,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True, path_mode="STRIP",
        )
        print(f"[Refine] {name}:", {obj.name: crew.triangles(obj)
              for obj in bpy.context.scene.objects if obj.type == "MESH"}, flush=True)


if __name__ == "__main__":
    main()

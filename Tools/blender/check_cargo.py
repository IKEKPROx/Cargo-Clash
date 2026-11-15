"""Checks and pictures the cargo clips (cargo_actions.py) with Astra's real cargo on the markers.

Bake a body into a .blend first, then open that file with this script:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_crew.py -- --only Short --no-export --check /tmp/crew --clips InspectGold,CarryGold
    /Applications/Blender.app/Contents/MacOS/Blender --background /tmp/crew/crew_Short.blend --python Tools/blender/check_cargo.py -- --out /tmp/crew/shots

Options after `--`: `--out folder` for pictures (none without it), `--clips A,B`, `--every N` frames between
pictures (default 6), `--step N` frames between overlap checks (default 2), `--source folder` (her 独立建模).

For every clip it prints, per set of clothes, the worst overlap between the thing in the hands, the notes or
the case and the body, each hat and each pair of glasses; the case against the gloves (a hand inside the
case); and how far each wrist bends (forearm to hand; past about 60 looks broken). An overlap is counted in
triangle pairs, which is 0 when nothing touches. The gloves are allowed to touch what they hold.
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def option(flag, default=None):
    return ARGS[ARGS.index(flag) + 1] if flag in ARGS else default


OUT = Path(option("--out")) if option("--out") else None
ONLY = set(option("--clips").split(",")) if option("--clips") else None
EVERY = int(option("--every", "6"))
STEP = int(option("--step", "2"))
VERBOSE = "--where" in ARGS
SOURCE = Path(option("--source", str(Path(__file__).resolve().parents[2].parent / "独立建模")))
KINDS = ("Tobacco", "Cash", "Gold", "Art")
SLOT = {"Art": "Content_Goose", "Gold": "Content_Gold_01", "Cash": "Cash_Bundle_01", "Tobacco": "Cigarette_06"}
HATS = ("classic", "patched", "wide", "lamp", "earmuffs", "beacon")
GLASSES = ("square", "round", "visor")

scene = bpy.context.scene
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
marker_case = bpy.data.objects["Marker_Case"]
marker_lid = bpy.data.objects["Marker_Lid"]


def kind_of(name):
    for kind in KINDS:
        if name.endswith(kind):
            return kind
    if name.startswith("CoCarry"):
        return "Art"
    return None


def load_cargo(kind):
    path = SOURCE / "互动货物_v08" / "模型" / kind / f"Cargo_{kind}.blend"
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.objects = list(src.objects)
    objects = [o for o in dst.objects if o is not None]
    for obj in objects:
        scene.collection.objects.link(obj)
    root = next(o for o in objects if o.parent is None)
    root.name = "Check_" + kind
    lid = next(o for o in root.children if o.name.startswith("Lid_Pivot"))
    slot = next(o for o in root.children_recursive if o.name.split(".")[0] == SLOT[kind])
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    return {"root": root, "lid": lid, "slot": slot, "meshes": meshes, "objects": objects}


CARGO = {kind: load_cargo(kind) for kind in KINDS}


def show_cargo(kind):
    for k, cargo in CARGO.items():
        for obj in cargo["objects"]:
            obj.hide_render = k != kind
            obj.hide_set(k != kind)


def co_carry_crate():
    """Where the crate hangs for a co-carry clip: its side handle in both hands (make_crew.co_carry_values)."""
    sys.path.insert(0, str(Path(__file__).parent))
    import make_crew
    height = make_crew.CO_CARRY_HANDLE / make_crew.GAME_SCALE
    bar = -make_crew.CO_CARRY_REACH / make_crew.GAME_SCALE
    w, d, h = 0.76, 0.56, 0.75
    root = Vector((0.0, bar - (w / 2 + 0.095), height - h * 0.46))
    return Matrix.Translation(root) @ Matrix.Rotation(math.pi / 2, 4, "Z")


def place(kind, co_carry):
    cargo = CARGO[kind]
    if co_carry:
        cargo["root"].matrix_world = rig.matrix_world @ co_carry_crate()
        cargo["lid"].rotation_euler = (0, 0, 0)
        cargo["slot"].hide_render = False
        return
    cargo["root"].matrix_world = marker_case.matrix_world.normalized()
    bpy.context.view_layer.update()
    cargo["lid"].matrix_world = marker_lid.matrix_world.normalized()
    out = rig.pose.bones["Prop_Item"].scale.x > 0.01
    cargo["slot"].hide_render = out
    cargo["slot"].hide_set(out)


def show_props(kind, name):
    for k in KINDS:
        prop = bpy.data.objects["Prop_" + k]
        prop.hide_render = k != kind or name.startswith(("Carry", "CoCarry"))
    bpy.data.objects["Prop_Notes"].hide_render = kind != "Cash"


OWNERS = {}


def owners(obj):
    """The bone each of a skinned mesh's faces follows most, to say where something touches."""
    if obj.name not in OWNERS:
        names = {g.index: g.name for g in obj.vertex_groups}
        per_vertex = []
        for v in obj.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            per_vertex.append(names.get(best.group, "?") if best is not None else "?")
        OWNERS[obj.name] = [per_vertex[p.vertices[0]] for p in obj.data.polygons]
    return OWNERS[obj.name]


TREE_OF = {}


def tree(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    vertices = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    polygons = [tuple(p.vertices) for p in mesh.polygons]
    result = BVHTree.FromPolygons(vertices, polygons)
    evaluated.to_mesh_clear()
    TREE_OF[id(result)] = obj
    return result


def where(pairs, person_tree):
    """The bones of the person's faces in these touching pairs, most first."""
    obj = TREE_OF.get(id(person_tree))
    if obj is None or not obj.vertex_groups:
        return ""
    names = owners(obj)
    counts = {}
    for _, face in pairs:
        if face < len(names):
            counts[names[face]] = counts.get(names[face], 0) + 1
    return "(" + " ".join(f"{k}:{v}" for k, v in sorted(counts.items(), key=lambda kv: -kv[1])[:3]) + ")"


def wrist_bends():
    bends = []
    for side in ("Left", "Right"):
        forearm = rig.pose.bones[f"{side}LowerArm"].matrix.to_3x3().col[1]
        hand = rig.pose.bones[f"{side}Hand"].matrix.to_3x3().col[1]
        bends.append(math.degrees(forearm.angle(hand)))
    return bends


def overlaps(outfit, kind, co_carry, depsgraph):
    """Triangle pairs touching, for each pairing that mustn't touch."""
    result = {}
    held = [] if co_carry else [bpy.data.objects["Prop_" + kind]]
    if kind == "Cash" and not co_carry and rig.pose.bones["Prop_Notes"].scale.x > 0.01:
        held.append(bpy.data.objects["Prop_Notes"])
    if not co_carry and rig.pose.bones["Prop_Item"].scale.x <= 0.01:
        held = [h for h in held if not h.name.startswith("Prop_" + kind)]
    case = [m for m in CARGO[kind]["meshes"] if not m.hide_render and m.visible_get()]
    people = {"body": [bpy.data.objects[outfit]]}
    for hat in HATS:
        people["hat " + hat] = [bpy.data.objects[f"{outfit}_Hat_{hat}"]]
    for glasses in GLASSES:
        people["glasses " + glasses] = [bpy.data.objects[f"{outfit}_Glasses_{glasses}"]]
    trees = {name: [tree(o, depsgraph) for o in objs] for name, objs in people.items()}
    things = {"held": [tree(o, depsgraph) for o in held], "case": [tree(o, depsgraph) for o in case]}
    for thing, thing_trees in things.items():
        for name, person_trees in trees.items():
            pairs = [(a.overlap(b), b) for a in thing_trees for b in person_trees]
            count = sum(len(p) for p, _ in pairs)
            if count:
                detail = " ".join(where(p, b) for p, b in pairs if p) if VERBOSE else ""
                result[f"{thing}/{name}{detail}"] = count
    gloves = tree(bpy.data.objects["Gloves"], depsgraph)
    pairs = [t.overlap(gloves) for t in things["case"]]
    count = sum(len(p) for p in pairs)
    if count:
        # Hands are meant to touch what they hold: what matters is how deep a glove goes in, in millimetres.
        depth = glove_depth(bpy.data.objects["Gloves"], things["case"], depsgraph)
        detail = " ".join(where(p, gloves) for p in pairs if p) if VERBOSE else ""
        if depth > GLOVE_SINK:
            result[f"case/gloves {depth:.0f}mm{detail}"] = count
    return result


GLOVE_SINK = 6.0


def glove_depth(gloves, case_trees, depsgraph):
    """How far the deepest glove vertex is inside the case, in millimetres (0 if none is)."""
    evaluated = gloves.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    points = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    worst = 0.0
    for point in points:
        for case_tree in case_trees:
            hit = case_tree.find_nearest(point, 0.05)
            if hit[0] is None:
                continue
            location, normal, _, distance = hit
            if (point - location).dot(normal) < 0:
                worst = max(worst, distance * 1000.0)
    return worst


def camera(target, yaw, pitch, distance, lens=50):
    cam = bpy.data.objects.get("CheckCamera")
    if cam is None:
        cam = bpy.data.objects.new("CheckCamera", bpy.data.cameras.new("CheckCamera"))
        scene.collection.objects.link(cam)
    cam.data.lens = lens
    yaw_r = math.radians(yaw)
    pitch_r = math.radians(pitch)
    offset = Vector((math.sin(yaw_r) * math.cos(pitch_r), -math.cos(yaw_r) * math.cos(pitch_r), math.sin(pitch_r))) * distance
    cam.location = Vector(target) + offset
    cam.rotation_euler = (-offset).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam


def setup_render():
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 360
    scene.render.resolution_y = 420
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("CheckSky")
    world.color = (0.74, 0.79, 0.82)
    scene.world = world
    if "CheckGround" not in bpy.data.objects:
        mesh = bpy.data.meshes.new("CheckGround")
        mesh.from_pydata([(-5, -5, 0), (5, -5, 0), (5, 5, 0), (-5, 5, 0)], [], [(0, 1, 2, 3)])
        scene.collection.objects.link(bpy.data.objects.new("CheckGround", mesh))


def dress(outfit, hat, glasses):
    other = "Porter" if outfit == "Wrecker" else "Wrecker"
    bpy.data.objects[outfit].hide_render = False
    bpy.data.objects[other].hide_render = True
    for obj in bpy.data.objects:
        if "_Hat_" in obj.name or "_Glasses_" in obj.name:
            obj.hide_render = obj.name not in (f"{outfit}_Hat_{hat}", f"{outfit}_Glasses_{glasses}")
    for name in ("Hammer", "Vacuum", "DizzyStars", "FP_Arms"):
        if name in bpy.data.objects:
            bpy.data.objects[name].hide_render = True


def main():
    if OUT:
        OUT.mkdir(parents=True, exist_ok=True)
        setup_render()
    body = Path(bpy.data.filepath).stem.split("_")[-1]
    actions = [a for a in bpy.data.actions if kind_of(a.name) and (ONLY is None or a.name in ONLY)]
    for action in actions:
        rig.animation_data.action = action
        kind = kind_of(action.name)
        co_carry = action.name.startswith("CoCarry")
        show_cargo(kind)
        show_props(kind, action.name)
        start, end = (int(x) for x in action.frame_range)
        for outfit in ("Porter", "Wrecker"):
            worst = {}
            bent = [(0.0, start), (0.0, start)]
            for frame in range(start, end + 1):
                scene.frame_set(frame)
                place(kind, co_carry)
                bpy.context.view_layer.update()
                for i, bend in enumerate(wrist_bends()):
                    if bend > bent[i][0]:
                        bent[i] = (bend, frame)
                if (frame - start) % STEP:
                    continue
                depsgraph = bpy.context.evaluated_depsgraph_get()
                for name, count in overlaps(outfit, kind, co_carry, depsgraph).items():
                    short = name.split("(")[0]
                    if count > worst.get(short, (0, 0, ""))[0]:
                        worst[short] = (count, frame, name[len(short):])
            report = ", ".join(f"{name}={count}@{frame}{detail}" for name, (count, frame, detail) in sorted(worst.items())) or "none"
            print(f"CARGO {body} {outfit:7s} {action.name:15s} touching: {report}  wrists L={bent[0][0]:.0f}@{bent[0][1]} R={bent[1][0]:.0f}@{bent[1][1]}",
                  flush=True)
        if not OUT:
            continue
        dress("Porter", "classic", "round")
        for frame in range(start, end + 1, EVERY):
            scene.frame_set(frame)
            place(kind, co_carry)
            bpy.context.view_layer.update()
            for view, (yaw, pitch, distance) in (("front", (35, 12, 4.4)), ("side", (100, 6, 4.4))):
                camera((0, -0.35, 0.95), yaw, pitch, distance)
                scene.render.filepath = str(OUT / f"{body}_{action.name}_{view}_{frame:03d}.png")
                bpy.ops.render.render(write_still=True)


main()

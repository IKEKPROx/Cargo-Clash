"""Brings Astra's crew into the game. Writes Assets/Game/Models/Crew_Short.fbx, Crew_Medium.fbx and
Crew_Tall.fbx: one skeleton per body type with all of its animations, both sets of work clothes, the
gloves, the hammer, the vacuum and the dizzy stars.

Astra made the people in Blender, in her folder 独立建模 next to the repository (it isn't in Git):
three body types, each in two sets of clothes. The porters (S_B, M_C, T_A, folder v05) are the
transport company's staff, so Collectors wear them. The wreckers (D_S, D_M, D_T, folder v06) are the
rival company's crew, so Interrupters wear them. A porter and a wrecker of the same height have the
same skeleton, so each body type is animated once and swaps clothes at half-time. Her v06 corrected
how the gloves sit on the hands, so both outfits use the v06 gloves and finger bones.

Her files are sculpts of about 450,000 triangles with no game animations, so this script:
  - keeps all six hats and three pairs of glasses for each set of clothes, as separate meshes on the
    head that the game switches on to match each player's choice, and the ten face sliders, carried
    over onto the reduced face (her glasses widen and grow with the eyes, as in her set_face),
  - deletes the stitching and zip teeth, which are too fine to see in a match, and reduces the rest
    one colour at a time (every colour is a separate shell, so nothing tears),
  - merges her colours into the game's (COLOURS); ClothPrimary becomes the team colour,
  - animates the skeleton with the game's 19 clips. A clip only says where the hands and feet go (or
    where a tool goes, and both hands follow its grips), and the elbows and knees are worked out.
    Fingers use her hand poses.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_crew.py

Options after `--`: `--source folder` (her 独立建模 folder), `--only Medium`, `--sheet folder` to render
check images, `--no-export`, `--check folder` to save the posed .blend files, `--clips Smash,Slam` to bake
only those while checking (nothing is exported then). Axes as in her files: -Y is the front, +X is the character's left, +Z is up,
in metres. Unity imports these files at GAME_SCALE, so a size in the game is divided by it here.
"""
import bisect
import importlib
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

sys.path.insert(0, str(Path(__file__).parent))
import modeling

importlib.reload(modeling)

ROOT = Path(__file__).resolve().parents[2]
GAME_SCALE = 0.84
FPS = 30

# Body type: (wrecker file, porter file) inside Astra's folder.
BODIES = {
    "Short": ("摧毁方与收集器_v06/模型/D_S/D_S.blend", "搬运工_v05_可动手部/模型/S_B/S_B.blend"),
    "Medium": ("摧毁方与收集器_v06/模型/D_M/D_M.blend", "搬运工_v05_可动手部/模型/M_C/M_C.blend"),
    "Tall": ("摧毁方与收集器_v06/模型/D_T/D_T.blend", "搬运工_v05_可动手部/模型/T_A/T_A.blend"),
}
HAMMER_FILE = "摧毁方与收集器_v06/道具/Hammer/Hammer.blend"
VACUUM_FILE = "摧毁方与收集器_v06/道具/Vacuum/Vacuum.blend"
HAND_POSES_FILE = "摧毁方与收集器_v06/hand_pose_core.py"

# The game's colours for her materials, by the part of the name after the dot. Unity swaps each name for
# the project's material (CargoClashBuilder.ModelMaterials); these colours only matter for check images.
COLOURS = {
    "Char_Team": (0.86, 0.45, 0.18),
    "Porter_Shirt": (0.16, 0.23, 0.31),
    "Wrecker_Jacket": (0.27, 0.27, 0.28),
    "Wrecker_Shirt": (0.74, 0.71, 0.66),
    "Crew_Skin": (0.79, 0.61, 0.45),
    "Crew_SkinShade": (0.68, 0.48, 0.35),
    "Crew_Hair": (0.29, 0.21, 0.17),
    "Crew_EyeWhite": (0.94, 0.92, 0.86),
    "Crew_Dark": (0.15, 0.14, 0.13),
    "Crew_Mouth": (0.42, 0.25, 0.2),
    "Crew_Glove": (0.69, 0.55, 0.38),
    "Crew_Boot": (0.33, 0.3, 0.25),
    "Crew_Brass": (0.67, 0.58, 0.4),
    "Crew_Light": (0.8, 0.76, 0.65),
    "Crew_HatPrimary": (0.86, 0.66, 0.25),
    "Crew_HatTrim": (0.6, 0.45, 0.19),
    "Crew_Lamp": (1.0, 0.95, 0.75),
    "Crew_Beacon": (0.95, 0.58, 0.18),
    "Crew_LensBlue": (0.18, 0.51, 0.58),
    "Crew_LensAmber": (0.66, 0.41, 0.15),
    "Char_Star": (1.0, 0.85, 0.1),
    "FP_Sleeve": (0.16, 0.23, 0.31),
    "Hammer_Head": (0.39, 0.42, 0.42),
    "Hammer_Handle": (0.42, 0.27, 0.15),
    "Hammer_Grip": (0.68, 0.53, 0.34),
    "Vacuum_Housing": (0.9, 0.68, 0.22),
    "Vacuum_Red": (0.71, 0.31, 0.24),
    "Vacuum_Steel": (0.5, 0.56, 0.56),
    "Heli_Dark": (0.13, 0.14, 0.17),
    "Vacuum_Glass": (0.28, 0.61, 0.68),
    "Vacuum_Ivory": (0.94, 0.86, 0.75),
}

# Her material name (after the dot) -> the game's, for each outfit. None deletes those faces.
SHARED = {
    "ClothPrimary": "Char_Team", "EarCup": "Char_Team",
    "Skin": "Crew_Skin", "SkinWarm": "Crew_Skin", "SkinShade": "Crew_SkinShade", "Lip": "Crew_SkinShade",
    "Hair": "Crew_Hair", "EyeWhite": "Crew_EyeWhite", "EyeDark": "Crew_Dark", "Iris": "Crew_Dark",
    "Frame": "Crew_Dark", "Lens": "Crew_Dark", "Sole": "Crew_Dark", "Mouth": "Crew_Mouth",
    "Gloves": "Crew_Glove", "GlovePalm": "Crew_Glove", "GloveGrip": "Crew_Glove", "Cuff": "Crew_Glove",
    "GloveSeam": None, "Thread": None, "Zipper": None,
    "Boots": "Crew_Boot", "Laces": "Crew_Light", "Tape": "Crew_Light", "Metal": "Crew_Brass",
    "ClothAccent": "Crew_Brass", "HatPrimary": "Crew_HatPrimary", "HatTrim": "Crew_HatTrim",
    "Lamp": "Crew_Lamp", "Beacon": "Crew_Beacon", "LensBlue": "Crew_LensBlue", "LensAmber": "Crew_LensAmber",
}
OUTFIT_MATERIALS = {
    "Porter": dict(SHARED, ClothSecondary="Porter_Shirt", UnderShirt="Porter_Shirt", Patch="Crew_Light"),
    "Wrecker": dict(SHARED, ClothSecondary="Wrecker_Jacket", UnderShirt="Wrecker_Shirt", Patch="Char_Team"),
}
TOOL_MATERIALS = {
    "Hammer": {
        "Steel": "Hammer_Head", "Edge": "Hammer_Head", "Dark": "Hammer_Head", "Wood": "Hammer_Handle",
        "WoodLight": "Hammer_Handle", "Grip": "Hammer_Grip", "Team": "Char_Team",
    },
    "Vacuum": {
        "Housing": "Vacuum_Housing", "HousingLight": "Vacuum_Housing", "Red": "Vacuum_Red", "RedLight": "Vacuum_Red",
        "Steel": "Vacuum_Steel", "DarkSteel": "Vacuum_Steel", "Rubber": "Heli_Dark", "Ink": "Heli_Dark",
        "Glass": "Vacuum_Glass", "Light": "Vacuum_Glass", "Ivory": "Vacuum_Ivory", "Team": "Char_Team",
    },
}

# How many triangles each of her colours keeps, as a share of what it has. Small parts keep more.
KEEP = {
    "Char_Team": 0.075, "Porter_Shirt": 0.075, "Wrecker_Jacket": 0.09, "Wrecker_Shirt": 0.11,
    "Crew_Skin": 0.12, "Crew_SkinShade": 0.16, "Crew_Hair": 0.16, "Crew_EyeWhite": 0.2, "Crew_Dark": 0.1,
    "Crew_Mouth": 0.22, "Crew_Glove": 0.045, "Crew_Boot": 0.14, "Crew_Brass": 0.07, "Crew_Light": 0.1,
    "Crew_HatPrimary": 0.1, "Crew_HatTrim": 0.12, "Crew_Lamp": 0.2, "Crew_Beacon": 0.2,
    "Crew_LensBlue": 0.2, "Crew_LensAmber": 0.2,
    "Hammer_Head": 0.03, "Hammer_Handle": 0.03, "Hammer_Grip": 0.04,
    "Vacuum_Housing": 0.012, "Vacuum_Red": 0.015, "Vacuum_Steel": 0.015, "Heli_Dark": 0.015,
    "Vacuum_Glass": 0.02, "Vacuum_Ivory": 0.015,
}
MIN_FACES = 40
# The face is what people look at, so the clothes give up more.
DETAIL = {"_Face": 1.0, "Glove": 1.0, "_Body": 0.62, "_Hat_": 0.72, "_Eyewear_": 0.72}

# What players can wear. The game's CrewLook lists them in the same order.
HATS = ("classic", "patched", "wide", "lamp", "earmuffs", "beacon")
GLASSES = ("square", "round", "visor")
# Her set_face: glasses widen with EyeSpacing and grow taller with EyeSize.
GLASSES_WIDEN = 0.15
GLASSES_GROW = 0.12

SIDES = (("L", "Left", 1.0), ("R", "Right", -1.0))
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Little")
SEGMENTS = ("Proximal", "Intermediate", "Distal")
FINGER_POSES = ("Relaxed", "Open", "Grip", "Fist", "Carry")

# On the hammer and the vacuum, measured from the right hand's grip, which is their origin.
HAMMER_LEFT_GRIP = 0.27
VACUUM_FORE_GRIP = 0.1848
# Where a handle sits in a closed hand, in her canonical hand space (fingers +Y, back of the hand +Z).
GRIP_CENTER = Vector((0.0, 0.130, -0.061))

# Keeping the arms and tools out of the body. The torso is measured from the clothes skinned to these
# bones, as outlines every SLICE metres up: how far it reaches from its middle in each of SPOKES
# directions. A hand counts as a ball this big round its wrist and its fist.
TORSO_BONES = ("Hips", "Spine", "Chest")
SLICE = 0.03
SPOKES = 36
HAND_RADIUS = 0.055
# A wrist bends this far without looking broken. Past it, the elbow turns to bring the forearm round.
EASY_WRIST = 30.0
# The sleeves are measured to most of their thickness; this covers the rest, and the folds.
ARM_MARGIN = 0.015


def arguments():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    def value(flag, default=None):
        return args[args.index(flag) + 1] if flag in args else default

    source = Path(value("--source", ROOT.parent / "独立建模"))
    only = value("--only")
    return {
        "source": source,
        "only": only.split(",") if only else list(BODIES),
        "sheet": value("--sheet"),
        "export": "--no-export" not in args,
        "clips": value("--clips").split(",") if value("--clips") else None,
    }


# ---- Meshes ----

def link(obj):
    if obj.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(obj)


def apply_modifier(obj, modifier):
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        bpy.ops.object.modifier_apply(modifier=modifier.name)


def join(objects, name):
    target = objects[0]
    if len(objects) > 1:
        with bpy.context.temp_override(active_object=target, object=target, selected_objects=objects, selected_editable_objects=objects):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    return target


def placement_on_rig(obj):
    """Where an object sits, measured from the skeleton it hangs from."""
    owner = obj.parent
    while owner is not None and owner.type != "ARMATURE":
        owner = owner.parent
    placement = obj.matrix_world.copy()
    return owner.matrix_world.inverted() @ placement if owner is not None else placement


def bake_world(obj):
    """Moves an object's placement into its vertices, so it can hang off nothing. Placement is measured
    from the skeleton it hangs from: in her v05 file the short and tall porters stand off to the side."""
    owner = obj.parent
    while owner is not None and owner.type != "ARMATURE":
        owner = owner.parent
    placement = obj.matrix_world.copy()
    if owner is not None:
        placement = owner.matrix_world.inverted() @ placement
    obj.parent = None
    obj.data.transform(placement, shape_keys=True)
    obj.matrix_world = Matrix.Identity(4)


def bind_to_bone(obj, bone):
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")


def game_materials(names):
    materials = {}
    for name in names:
        material = bpy.data.materials.get(name)
        if material is None:
            colour = COLOURS[name]
            linear = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in colour)
            material = bpy.data.materials.new(name)
            material.diffuse_color = (*linear, 1.0)
            material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*linear, 1.0)
            material.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
        materials[name] = material
    return materials


def recolour(obj, mapping):
    """Swaps her material slots for the game's and deletes faces mapped to None."""
    mesh = obj.data
    # Her names are "D_M.ClothPrimary". Appending twice can add ".001" after them.
    old = [m.name.split(".")[1] if m else None for m in mesh.materials]
    used = {old[p.material_index] for p in mesh.polygons}
    for name in used:
        if name not in mapping:
            raise KeyError(f"{obj.name}: no game colour for her material {name}")
    wanted = sorted({mapping[n] for n in used if mapping[n] is not None})
    materials = game_materials(wanted)
    slot_of = {name: i for i, name in enumerate(wanted)}

    if all(mapping[n] is not None for n in used):
        # Nothing to delete: renumber the faces without bmesh, which would drop the face sliders.
        indices = [0] * len(mesh.polygons)
        mesh.polygons.foreach_get("material_index", indices)
        indices = [slot_of[mapping[old[i]]] for i in indices]
        mesh.materials.clear()
        for name in wanted:
            mesh.materials.append(materials[name])
        mesh.polygons.foreach_set("material_index", indices)
        mesh.update()
        return

    bm = bmesh.new()
    bm.from_mesh(mesh)
    doomed = [f for f in bm.faces if old[f.material_index] is None or mapping[old[f.material_index]] is None]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    for face in bm.faces:
        face.material_index = slot_of[mapping[old[face.material_index]]]
    # Clearing the slots resets every face to the first one, so the faces are written after.
    mesh.materials.clear()
    for name in wanted:
        mesh.materials.append(materials[name])
    bm.to_mesh(mesh)
    bm.free()


def reduce(obj, detail=1.0):
    """Decimates each colour on its own. Her colours are separate shells, so this can't open cracks."""
    pieces = []
    for index, material in enumerate(obj.data.materials):
        piece = obj.copy()
        piece.data = obj.data.copy()
        link(piece)
        bm = bmesh.new()
        bm.from_mesh(piece.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != index], context="FACES")
        bm.to_mesh(piece.data)
        bm.free()
        faces = len(piece.data.polygons)
        if faces == 0:
            bpy.data.objects.remove(piece)
            continue
        ratio = max(KEEP.get(material.name, 0.1) * detail, MIN_FACES / faces)
        if ratio < 1.0:
            modifier = piece.modifiers.new("Reduce", "DECIMATE")
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = True
            apply_modifier(piece, modifier)
        pieces.append(piece)
    name = obj.name
    bpy.data.objects.remove(obj)
    result = join(pieces, name)
    # Glue the material list back in the same order on the joined mesh.
    return result


def finish_mesh(obj):
    mesh = obj.data
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    mesh.validate(clean_customdata=False)
    mesh.update()


def strip(obj):
    while obj.modifiers:
        obj.modifiers.remove(obj.modifiers[0])


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def keep_shapes(original, reduced):
    """Rebuilds the face sliders on the reduced face: each of its points moves the way the original
    surface under it moves. Points only look at original faces of their own colour, so an eyelid
    can't borrow a slider from the eyeball beneath it."""
    source = original.data
    keys = source.shape_keys.key_blocks
    basis = [point.co.copy() for point in keys[0].data]
    source.calc_loop_triangles()
    triangles_of = {}
    for triangle in source.loop_triangles:
        triangles_of.setdefault(source.materials[triangle.material_index].name, []).append(tuple(triangle.vertices))
    trees = {name: (BVHTree.FromPolygons(basis, tris), tris) for name, tris in triangles_of.items()}

    target = reduced.data
    colour_of = {}
    for polygon in target.polygons:
        for index in polygon.vertices:
            colour_of.setdefault(index, target.materials[polygon.material_index].name)
    reduced.shape_key_add(name="Basis", from_mix=False)
    made = [(key, reduced.shape_key_add(name=key.name, from_mix=False)) for key in keys[1:]]
    for vertex in target.vertices:
        tree, tris = trees[colour_of.get(vertex.index, next(iter(trees)))]
        nearest, _, index, _ = tree.find_nearest(vertex.co)
        a, b, c = tris[index]
        for key, new in made:
            moved = barycentric_transform(nearest, basis[a], basis[b], basis[c], key.data[a].co, key.data[b].co, key.data[c].co)
            new.data[vertex.index].co = vertex.co + (moved - nearest)


def prepare_part(obj, outfit):
    """One of her parts, moved onto the skeleton, recoloured and reduced, keeping any face sliders."""
    on_head = obj.parent is not None and obj.parent.type == "EMPTY"
    strip(obj)
    bake_world(obj)
    if on_head:
        bind_to_bone(obj, "Head")
    recolour(obj, OUTFIT_MATERIALS[outfit])
    detail = next((v for k, v in DETAIL.items() if k in obj.name), 1.0)
    if obj.data.shape_keys is None:
        return reduce(obj, detail)
    original = obj.copy()
    original.data = obj.data.copy()
    link(original)
    obj.shape_key_clear()
    reduced = reduce(obj, detail)
    keep_shapes(original, reduced)
    bpy.data.objects.remove(original)
    return reduced


def prepare_outfit(rig, parts, outfit, name):
    """Her body and face (and for the gloves, both gloves) as one skinned mesh in the game's colours.
    Anything with face sliders goes first in the join, so the result keeps them."""
    for obj in parts:
        link(obj)
    bpy.context.view_layer.update()
    pieces = [prepare_part(obj, outfit) for obj in parts]
    pieces.sort(key=lambda piece: piece.data.shape_keys is None)
    mesh = join(pieces, name)
    finish_mesh(mesh)
    attach_to_rig(mesh, rig)
    return mesh


def add_glasses_shapes(obj, frame):
    """Two sliders that widen the glasses and make them taller, around where they sit on the face."""
    back = frame.inverted()
    obj.shape_key_add(name="Basis", from_mix=False)
    wider = obj.shape_key_add(name="EyeSpacing", from_mix=False)
    taller = obj.shape_key_add(name="EyeSize", from_mix=False)
    for vertex, wide_point, tall_point in zip(obj.data.vertices, wider.data, taller.data):
        local = back @ vertex.co
        wide_point.co = frame @ Vector((local.x * (1 + GLASSES_WIDEN), local.y, local.z))
        tall_point.co = frame @ Vector((local.x, local.y, local.z * (1 + GLASSES_GROW)))


def prepare_accessories(rig, objects, outfit):
    """Every hat and pair of glasses for one set of clothes, each its own mesh skinned to the head."""
    for obj in objects:
        link(obj)
    bpy.context.view_layer.update()
    made = []
    for obj in objects:
        kind, option = obj.name.split("_")[-2:]
        frame = placement_on_rig(obj)
        piece = prepare_part(obj, outfit)
        name = f"{outfit}_{'Hat' if kind == 'Hat' else 'Glasses'}_{option}"
        if kind == "Eyewear":
            add_glasses_shapes(piece, frame)
        finish_mesh(piece)
        piece.name = piece.data.name = name
        attach_to_rig(piece, rig)
        made.append(piece)
    return made


def attach_to_rig(obj, rig):
    obj.parent = rig
    obj.matrix_parent_inverse = Matrix.Identity(4)
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig


def append(path, names):
    with bpy.data.libraries.load(str(path), link=False) as (source, target):
        missing = [n for n in names if n not in source.objects]
        if missing:
            raise KeyError(f"{path.name} has no {missing}")
        target.objects = list(names)
    return [o for o in target.objects]


def edit_bones(rig, on):
    rig.hide_set(False)
    rig.hide_viewport = False
    bpy.context.view_layer.objects.active = rig
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == rig)
    bpy.ops.object.mode_set(mode="EDIT" if on else "OBJECT")


def rest(rig, on=True):
    rig.data.pose_position = "REST" if on else "POSE"
    bpy.context.view_layer.update()


# ---- Tools ----

def load_tool(path, names, keep_scale=1.0):
    """One of her tools as a single mesh, origin at the right hand's grip. Its name is the file's."""
    objects = append(path, names)
    for obj in objects:
        link(obj)
    bpy.context.view_layer.update()
    holders = {obj.parent for obj in objects if obj.parent is not None}
    for obj in objects:
        strip(obj)
        bake_world(obj)
        recolour(obj, TOOL_MATERIALS[path.stem])
    # Her empties (the tool's root and grip markers) came along as parents; the name is wanted.
    for holder in holders:
        bpy.data.objects.remove(holder)
    saved = dict(KEEP)
    for key in KEEP:
        if key.startswith(("Hammer_", "Vacuum_", "Heli_")):
            KEEP[key] = min(1.0, KEEP[key] * keep_scale)
    pieces = [reduce(o) for o in objects]
    KEEP.update(saved)
    tool = join(pieces, path.stem)
    finish_mesh(tool)
    return tool


# ---- The dizzy stars ----

def build_stars(rig, top):
    """Four little stars spinning round above the hat. CharacterVisual turns the Stars bone."""
    edit_bones(rig, True)
    bone = rig.data.edit_bones.new("Stars")
    head = rig.data.edit_bones["Head"]
    bone.head = Vector((0.0, head.head.y - 0.02, top + 0.04))
    bone.tail = bone.head + Vector((0.0, 0.0, 0.1))
    bone.roll = 0.0
    bone.parent = head
    # It moves only the stars, but a bone that doesn't deform moves nothing at all.
    bone.use_deform = True
    edit_bones(rig, False)

    part = modeling.MeshPart("DizzyStars")
    part.bind({"Stars": 1.0})
    center = rig.data.bones["Stars"].head_local
    for i in range(4):
        angle = i * math.pi / 2
        cx = center.x + math.cos(angle) * 0.24
        cy = center.y + math.sin(angle) * 0.24
        cz = center.z + (0.025 if i % 2 == 0 else -0.025)
        rings = []
        for thickness in (-0.016, 0.016):
            ring = []
            for point in range(10):
                radius = 0.085 if point % 2 == 0 else 0.037
                a = point * math.pi / 5 + math.pi / 2
                ring.append(Vector((cx + math.cos(a) * radius, cy + thickness, cz + math.sin(a) * radius)))
            rings.append(ring)
        part.loft(rings, "Char_Star")
    stars = part.to_object(game_materials(["Char_Star"]))
    attach_to_rig(stars, rig)
    return stars


# ---- One body type ----

def load_body(source, body):
    wrecker_file, porter_file = (source / f for f in BODIES[body])
    bpy.ops.wm.open_mainfile(filepath=str(wrecker_file))
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    wid = rig.name.replace("_Rig", "")
    hands = {k: Matrix([list(r) for r in v]) for k, v in rig["hand_bind_transforms"].items()}
    rest(rig)

    def accessories(prefix):
        return [f"{prefix}_Hat_{h}" for h in HATS] + [f"{prefix}_Eyewear_{g}" for g in GLASSES]

    wanted = [f"{wid}_Body", f"{wid}_Face", f"{wid}_LeftGlove", f"{wid}_RightGlove"]
    keep = {bpy.data.objects[n] for n in wanted + accessories(wid)}

    # The porter's clothes, face and hat, from the matching v05 file.
    # All in one go: appending twice would bring a second copy of the skeleton the hat hangs from.
    pid = porter_file.stem
    porter_names = [f"{pid}_Body", f"{pid}_Face"]
    with bpy.data.libraries.load(str(porter_file), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in porter_names + [f"{pid}_Rig"]
                       or n.startswith((f"{pid}_Hat_", f"{pid}_Eyewear_", f"{pid}_Socket_"))]
    appended = list(dst.objects)
    porter_rig = bpy.data.objects[f"{pid}_Rig"]
    extra = accessories(pid)
    for obj in appended:
        link(obj)
        if obj.parent is not None:
            link(obj.parent)
    rest(porter_rig)

    gloves = [bpy.data.objects[f"{wid}_LeftGlove"], bpy.data.objects[f"{wid}_RightGlove"]]
    wrecker_parts = [bpy.data.objects[f"{wid}_Body"], bpy.data.objects[f"{wid}_Face"]]
    wrecker_extras = [bpy.data.objects[n] for n in accessories(wid)]
    porter_parts = [bpy.data.objects[n] for n in porter_names]
    porter_extras = [bpy.data.objects[n] for n in extra]

    # Throw away everything else: the other hats and glasses, sockets, her review scenes.
    for obj in list(bpy.data.objects):
        if obj in keep or obj in porter_parts or obj in porter_extras or obj == rig or obj == porter_rig:
            continue
        if obj.type == "EMPTY" and any(p.parent == obj for p in list(keep) + porter_parts + porter_extras):
            continue
        bpy.data.objects.remove(obj)
    for scene in list(bpy.data.scenes):
        if scene != bpy.context.scene:
            bpy.data.scenes.remove(scene)

    report = {}
    wrecker = prepare_outfit(rig, wrecker_parts, "Wrecker", "Wrecker")
    porter = prepare_outfit(rig, porter_parts, "Porter", "Porter")
    glove_mesh = prepare_outfit(rig, gloves, "Wrecker", "Gloves")
    extras = prepare_accessories(rig, wrecker_extras, "Wrecker") + prepare_accessories(rig, porter_extras, "Porter")
    for obj in list(bpy.data.objects):
        if obj.type == "EMPTY" or obj == porter_rig:
            bpy.data.objects.remove(obj)
    rest(rig, False)
    for name, obj in (("Wrecker", wrecker), ("Porter", porter), ("Gloves", glove_mesh)):
        report[name] = triangles(obj)
    report["Hats and glasses"] = sum(triangles(obj) for obj in extras)
    report["Face sliders"] = len(wrecker.data.shape_keys.key_blocks) - 1 if wrecker.data.shape_keys else 0

    # Above the tallest hat, the beacon.
    top = max(v.co.z for obj in (wrecker, porter, *extras) for v in obj.data.vertices)
    stars = build_stars(rig, top)
    rig.name = "Armature"
    rig.data.name = "Armature"
    return rig, hands, {"Wrecker": wrecker, "Porter": porter, "Gloves": glove_mesh, "DizzyStars": stars}, report, top


# ---- Holding the tools ----

def tool_in_hand(kind, canonical):
    """Where a tool sits, given the right hand's canonical frame (a 4x4 with the grip point as origin).
    The hammer's handle runs along the hand's X and its head strikes along -Y (her review pose); the
    vacuum's handles stand up through the fist, thumb on top, with the mouth where the fingers point."""
    x_c, y_c, z_c = (canonical.col[i].xyz.normalized() for i in range(3))
    if kind == "Hammer":
        axes = (-y_c, -z_c, x_c)
    else:
        axes = (-z_c, -y_c, -x_c)
    matrix = Matrix.Identity(4)
    for i, axis in enumerate(axes):
        matrix.col[i].xyz = axis
    matrix.translation = canonical.translation
    return matrix


def attach_tools(rig, source, hands):
    """The hammer and the vacuum, held in the right hand, reduced for people seen across the field."""
    hammer = load_tool(source / HAMMER_FILE, ["Hammer_Mesh"])
    vacuum = load_tool(source / VACUUM_FILE, ["Vacuum_Housing", "Vacuum_Turbine"])
    assert hammer.name == "Hammer" and vacuum.name == "Vacuum", (hammer.name, vacuum.name)
    rest(rig)
    transform = hands["Right"]
    grip = transform @ GRIP_CENTER
    canonical = Matrix.Identity(4)
    for i in range(3):
        canonical.col[i].xyz = transform.col[i].xyz.normalized()
    canonical.translation = grip
    for tool in (hammer, vacuum):
        world = tool_in_hand(tool.name, canonical)
        tool.parent = rig
        tool.parent_type = "BONE"
        tool.parent_bone = "RightHand"
        bpy.context.view_layer.update()
        tool.matrix_world = world
    rest(rig, False)
    return hammer, vacuum


# ---- Fingers ----

def is_finger(name):
    return any(digit in name for digit in DIGITS)


def capture_fingers(rig, source):
    """Her hand poses as each finger bone's own rotation, which doesn't depend on where the hand is."""
    core = {}
    path = source / HAND_POSES_FILE
    exec(compile(path.read_text(), str(path), "exec"), core)
    poses = {}
    for name in FINGER_POSES:
        core["reset_pose"](rig)
        for _, prefix, _ in SIDES:
            core["apply_pose"](rig, prefix, name)
        poses[name] = {pb.name: pb.rotation_quaternion.copy() for pb in rig.pose.bones if is_finger(pb.name)}
    core["reset_pose"](rig)
    return poses


# ---- Measuring a body ----

class Body:
    """Where a body type's joints are, and how far its clothes stick out, in her file's units."""

    def __init__(self, name, rig, meshes):
        self.name = name
        bones = rig.data.bones

        def head(bone):
            return bones[bone].head_local.copy()

        self.hips = head("Hips")
        self.chest = head("Chest")
        self.neck = head("Neck")
        self.head = head("Head")
        self.shoulder = {s: head(f"{p}UpperArm") for s, p, _ in SIDES}
        self.wrist = {s: head(f"{p}Hand") for s, p, _ in SIDES}
        self.hip = {s: head(f"{p}UpperLeg") for s, p, _ in SIDES}
        self.ankle = {s: head(f"{p}Foot") for s, p, _ in SIDES}
        self.upper = bones["LeftUpperArm"].length
        self.lower = bones["LeftLowerArm"].length
        self.thigh = bones["LeftUpperLeg"].length
        self.shin = bones["LeftLowerLeg"].length
        self.arm = self.upper + self.lower
        self.leg = self.thigh + self.shin
        self.points = [v.co.copy() for mesh in meshes for v in mesh.data.vertices]
        self.top = max(p.z for p in self.points)
        self.spine_z = bones["Spine"].head_local.z
        self.slices = self.measure_torso(meshes)
        self.slice_heights = [s[0] for s in self.slices]
        self.arm_radius = self.measure_arms(bones, meshes)

    @staticmethod
    def skinned_to(mesh, names):
        """The vertices of a mesh that follow one of these bones more than any other."""
        groups = {g.index: g.name for g in mesh.vertex_groups}
        for v in mesh.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            if best is not None and groups.get(best.group) in names:
                yield v, groups[best.group]

    def measure_torso(self, meshes):
        """The torso at rest as outlines stacked every SLICE: (height, middle of its depth, the distance out
        from there in each of SPOKES directions). The short body's belly is wider than its shoulders, so
        the arms have to go round it."""
        points = [v.co.copy() for mesh in meshes for v, _ in self.skinned_to(mesh, TORSO_BONES)]
        low = min(p.z for p in points)
        high = max(p.z for p in points)
        slices = []
        z = low
        while z < high:
            band = [p for p in points if z - SLICE * 0.25 <= p.z < z + SLICE * 1.25]
            if len(band) >= 8:
                middle = (min(p.y for p in band) + max(p.y for p in band)) / 2
                reach = [0.0] * SPOKES
                for p in band:
                    x, y = p.x, p.y - middle
                    spoke = int(round(math.atan2(y, x) / (2 * math.pi) * SPOKES)) % SPOKES
                    reach[spoke] = max(reach[spoke], math.hypot(x, y))
                # A direction with no vertices takes the larger of its neighbours.
                for _ in range(SPOKES):
                    for i in range(SPOKES):
                        if reach[i] == 0.0:
                            reach[i] = max(reach[i - 1], reach[(i + 1) % SPOKES])
                slices.append((z + SLICE / 2, middle, reach))
            z += SLICE
        return slices

    @staticmethod
    def measure_arms(bones, meshes):
        """How thick the sleeves are round the upper arm and the forearm (most of the way out)."""
        result = []
        for part in ("LeftUpperArm", "LeftLowerArm"):
            bone = bones[part]
            head = bone.head_local
            direction = (bone.tail_local - head).normalized()
            distances = []
            for mesh in meshes:
                for v, _ in Body.skinned_to(mesh, (part,)):
                    along = (v.co - head).dot(direction)
                    if 0.15 * bone.length < along < 0.85 * bone.length:
                        distances.append((v.co - head - direction * along).length)
            distances.sort()
            result.append(distances[int(len(distances) * 0.9)])
        return tuple(result)

    def torso_bone(self, z):
        """The bone that carries the torso at this height, at rest."""
        if z < self.spine_z:
            return "Hips"
        return "Spine" if z < self.chest.z else "Chest"

    def outline_at(self, point):
        """Where the torso's middle is at this point's height, and how far the torso reaches toward the
        point from there; None above or below the torso."""
        heights = self.slice_heights
        if point.z < heights[0] - SLICE or point.z > heights[-1] + SLICE:
            return None
        i = bisect.bisect_right(heights, point.z)
        pair = [self.slices[max(i - 1, 0)], self.slices[min(i, len(heights) - 1)]]
        t = 0.0 if pair[0] is pair[1] else (point.z - pair[0][0]) / (pair[1][0] - pair[0][0])
        middle = pair[0][1] + (pair[1][1] - pair[0][1]) * t
        angle = math.atan2(point.y - middle, point.x) / (2 * math.pi) * SPOKES
        first = math.floor(angle)
        along = angle - first

        def reach(outline):
            return outline[2][int(first) % SPOKES] * (1 - along) + outline[2][int(first + 1) % SPOKES] * along

        return middle, reach(pair[0]) * (1 - t) + reach(pair[1]) * t

    def inside(self, point, radius):
        """How deep a ball at `point` (with the torso at rest) is inside the torso; 0 when it's clear."""
        outline = self.outline_at(point)
        if outline is None:
            return 0.0
        middle, reach = outline
        return max(0.0, reach + radius - math.hypot(point.x, point.y - middle))

    def pushed_out(self, point, radius):
        """`point` moved straight out from the middle of the torso until a ball there is clear of it."""
        outline = self.outline_at(point)
        if outline is None:
            return point
        middle, reach = outline
        distance = math.hypot(point.x, point.y - middle)
        if distance >= reach + radius:
            return point
        if distance < 1e-4:
            return Vector((point.x, middle - reach - radius, point.z))
        scale = (reach + radius) / distance
        return Vector((point.x * scale, middle + (point.y - middle) * scale, point.z))

    def front(self, low, high):
        """How far forward the torso reaches between two heights (a negative Y)."""
        return min((p.y for p in self.points if low <= p.z <= high and abs(p.x) < 0.2), default=-0.15)

    def back(self, low, high):
        return max((p.y for p in self.points if low <= p.z <= high and abs(p.x) < 0.2), default=0.15)

    def shoulder_at(self, side, x=0.0, y=0.0, z=0.0):
        """A point near a shoulder, in arm lengths: x outward, y forward, z up."""
        sign = 1.0 if side == "L" else -1.0
        return tuple(self.shoulder[side] + Vector((sign * x, -y, z)) * self.arm)

    def hang(self, side, out=0.0, forward=0.0, up=0.0):
        """A wrist relative to its shoulder, turning with the chest: how the arm hangs at rest, a little
        closer in and in front, then moved by arm lengths."""
        sign = 1.0 if side == "L" else -1.0
        rest = self.wrist[side] - self.shoulder[side]
        hang = Vector((rest.x * 0.8, rest.y - 0.05 * self.arm, rest.z * 0.97))
        return tuple(hang + Vector((sign * out, -forward, up)) * self.arm)

    def stand(self, side, out=0.0, forward=0.0, up=0.0):
        """A foot (its ankle) in the body's space, from where it stands at rest, in leg lengths."""
        sign = 1.0 if side == "L" else -1.0
        return tuple(self.ankle[side] + Vector((sign * out, -forward, up)) * self.leg)


# ---- Poses ----

def deg_quat(angles):
    return Euler(tuple(math.radians(a) for a in angles), "XYZ").to_quaternion()


class Pose:
    """One moment of a clip. Angles are degrees: X leans forward, Y tilts toward the left, Z turns left.
    Wrists are relative to the shoulder and turn with the chest, unless hands_in_body is set; then they
    are in the body's space, like the feet. A palm is (the way the fingers point, the way the back of the
    hand faces); without one the hand carries on from the forearm. A tool pose places the hammer or the
    vacuum instead, and both hands follow its grips."""

    DEFAULTS = dict(
        root_loc=(0.0, 0.0, 0.0), root_rot=(0.0, 0.0, 0.0), hips_loc=(0.0, 0.0, 0.0),
        hips=(0.0, 0.0, 0.0), spine=(0.0, 0.0, 0.0), chest=(0.0, 0.0, 0.0), head=(0.0, 0.0, 0.0),
        hands_in_body=0.0, hand_l=None, hand_r=None,
        elbow_l=(0.7, 0.5, -0.2), elbow_r=(-0.7, 0.5, -0.2), palm_l=None, palm_r=None,
        foot_l=None, foot_r=None, knee_l=(0.06, -1.0, 0.0), knee_r=(-0.06, -1.0, 0.0),
        foot_pitch_l=0.0, foot_pitch_r=0.0, fingers_l=None, fingers_r=None,
        tool=None, grip=None, axis=None, up=(0.0, 0.0, 1.0), back_l=(1.0, 0.0, 0.0), back_r=(-1.0, 0.0, 0.0),
        clear=0.3, left_grip=HAMMER_LEFT_GRIP,
    )

    def __init__(self, **values):
        self.__dict__.update(self.DEFAULTS)
        self.__dict__.update(values)

    def copy(self, **changes):
        pose = Pose(**self.__dict__)
        pose.__dict__.update(changes)
        return pose


def pose(b, **values):
    """A pose standing still with the arms hanging, changed by `values`."""
    base = dict(
        hand_l=b.hang("L"), hand_r=b.hang("R"), foot_l=b.stand("L"), foot_r=b.stand("R"),
        fingers_l={"Relaxed": 1.0}, fingers_r={"Relaxed": 1.0},
    )
    base.update(values)
    return Pose(**base)


def mix(a, b, t):
    if isinstance(a, dict) and isinstance(b, dict):
        return {k: a.get(k, 0.0) * (1 - t) + b.get(k, 0.0) * t for k in set(a) | set(b)}
    if a is None or b is None or isinstance(a, str) or isinstance(b, str):
        return b if t >= 0.5 else a
    if isinstance(a, tuple) and a and isinstance(a[0], tuple):
        return tuple(mix(x, y, t) for x, y in zip(a, b))
    if isinstance(a, (tuple, Vector)):
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    return a + (b - a) * t


def mix_poses(a, b, t):
    if a.tool != b.tool:
        return a if t < 0.5 else b
    return Pose(**{key: mix(a.__dict__[key], b.__dict__[key], t) for key in Pose.DEFAULTS})


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def keyed(keys, seconds):
    """(seconds, pose) pairs as a function of normalized time, easing between them."""
    def sample(u):
        time = u * seconds
        for i in range(len(keys) - 1):
            start, a = keys[i]
            end, b = keys[i + 1]
            if time <= end:
                return mix_poses(a, b, smooth((time - start) / max(end - start, 0.0001)))
        return keys[-1][1]
    return sample


# ---- Turning a pose into bone rotations ----

def frame(direction, hinge):
    y = direction.normalized()
    x = hinge - y * hinge.dot(y)
    x = x.normalized() if x.length > 1e-6 else Vector((1, 0, 0))
    return Matrix((x, y, x.cross(y))).transposed()


def solve(start, target, first, second, hint):
    """Two-bone solver: the joint in the middle and where the end really gets to."""
    to_target = target - start
    distance = max(min(to_target.length, first + second - 0.002), abs(first - second) + 0.01)
    axis = to_target.normalized() if to_target.length > 1e-5 else Vector((0, 0, -1))
    along = (first * first - second * second + distance * distance) / (2 * distance)
    height = math.sqrt(max(first * first - along * along, 0.0))
    side = hint - axis * hint.dot(axis)
    side = side.normalized() if side.length > 1e-5 else Vector((0, -1, 0))
    joint = start + axis * along + side * height
    return joint, start + axis * distance, side


def hinge_of(first, second, side):
    bend = second - first * second.dot(first)
    bend = bend.normalized() if bend.length > 0.03 else -side
    return first.cross(bend).normalized()


def roll_on_handle(frame, forearm, handle):
    """A hand closed round a handle, turned round it until the fingers come as close as they can to carrying
    on from the forearm. The handle is round, so this is the hold with the straightest wrist."""
    def across(v):
        v = v - handle * v.dot(handle)
        return v.normalized() if v.length > 1e-4 else None

    want = across(forearm)
    have = across(frame.col[1].normalized())
    if want is None or have is None:
        return frame
    angle = have.angle(want)
    if have.cross(want).dot(handle) < 0.0:
        angle = -angle
    return Matrix.Rotation(angle, 3, handle) @ frame


def wrist_bend_on(frame, handle):
    """How far a wrist bends, for a forearm coming in at a hand that can turn on this handle."""
    lean = math.degrees(math.asin(max(-1.0, min(1.0, frame.col[1].normalized().dot(handle)))))
    return lambda forearm: abs(90.0 - math.degrees(forearm.angle(handle)) - lean)


def twist_about(q, axis):
    """How far `q` turns around `axis`, in radians."""
    v = Vector((q.x, q.y, q.z))
    along = v.dot(axis)
    angle = 2.0 * math.atan2(along, q.w)
    return (angle + math.pi) % (2 * math.pi) - math.pi


def canonical(side, fingers, back):
    """Her hand frame from the way the fingers point and the back of the hand faces. Left hands are mirrored."""
    y = Vector(fingers).normalized()
    z = Vector(back) - y * Vector(back).dot(y)
    z = z.normalized() if z.length > 1e-5 else Vector((0, 0, 1))
    x = y.cross(z)
    if side == "L":
        x = -x
    return Matrix((x, y, z)).transposed()


class CrewRig:
    def __init__(self, arm, body, hands, fingers):
        self.arm = arm
        self.b = body
        self.fingers = fingers
        bones = arm.data.bones
        self.rest = {b.name: b.head_local.copy() for b in bones}
        self.rot = {b.name: b.matrix_local.to_quaternion() for b in bones}
        self.parents = {b.name: b.parent.name if b.parent else None for b in bones}
        self.hand = {}
        self.frames = {}
        for s, prefix, _ in SIDES:
            transform = hands[prefix]
            turn = transform.to_3x3()
            turn = Matrix([turn.col[i].normalized() for i in range(3)]).transposed()
            self.hand[s] = (transform, turn.inverted(), self.rest[f"{prefix}Hand"])

            def direction(bone):
                return (bones[bone].tail_local - bones[bone].head_local).normalized()

            upper, lower = direction(f"{prefix}UpperArm"), direction(f"{prefix}LowerArm")
            elbow = upper.cross(Vector((0, -1, 0))).normalized()
            self.frames[f"{prefix}UpperArm"] = frame(upper, elbow)
            self.frames[f"{prefix}LowerArm"] = frame(lower, elbow)
            thigh, shin = direction(f"{prefix}UpperLeg"), direction(f"{prefix}LowerLeg")
            knee = thigh.cross(Vector((0, 1, 0))).normalized()
            self.frames[f"{prefix}UpperLeg"] = frame(thigh, knee)
            self.frames[f"{prefix}LowerLeg"] = frame(shin, knee)
        for pose_bone in arm.pose.bones:
            pose_bone.rotation_mode = "QUATERNION"
        self.previous = {}
        self.misses = []
        bpy.context.view_layer.update()
        self.tool_balls = {name: self.sample_tool(name, hands) for name in ("Hammer", "Vacuum") if name in bpy.data.objects}

    @staticmethod
    def sample_tool(name, hands):
        """A tool as a cloud of balls in the right hand's grip space (as tool_grips gives it), so that a
        pose can keep it out of the body. Measured with the skeleton at rest, where attach_tools put it."""
        transform = hands["Right"]
        grip = Matrix.Identity(4)
        for i in range(3):
            grip.col[i].xyz = transform.col[i].xyz.normalized()
        grip.translation = transform @ GRIP_CENTER
        to_grip = grip.inverted()
        tool = bpy.data.objects[name]
        cells = {}
        size = 0.07
        for v in tool.data.vertices:
            local = to_grip @ (tool.matrix_world @ v.co)
            cells.setdefault(tuple(math.floor(c / size) for c in local), []).append(local)
        balls = []
        for points in cells.values():
            middle = sum(points, Vector()) / len(points)
            balls.append((middle, max((p - middle).length for p in points) + 0.01))
        return balls

    # ---- Keeping arms and tools out of the body ----

    def rest_space(self, delta, place):
        """A function that takes a point near the torso back to where it would be with the torso at
        rest, through whichever of the hips, spine and chest carries that part of it."""
        def rest_of(point):
            bone = "Spine"
            for _ in range(2):
                q = self.rest[bone] + delta[bone].inverted() @ (point - place[bone])
                bone = self.b.torso_bone(q.z)
            return self.rest[bone] + delta[bone].inverted() @ (point - place[bone])

        def back_to_pose(rested, bone):
            return place[bone] + delta[bone] @ (rested - self.rest[bone])

        rest_of.back = back_to_pose
        return rest_of

    def arm_inside(self, shoulder, elbow, wrist, rest_of):
        """How deep the deepest part of an arm is in the torso. The top of the upper arm always meets the
        shoulder, so it starts partway down."""
        b = self.b
        upper, lower = (r + ARM_MARGIN for r in b.arm_radius)
        worst = 0.0
        for t in (0.45, 0.6, 0.75, 0.9):
            worst = max(worst, b.inside(rest_of(shoulder.lerp(elbow, t)), upper))
        for t in (0.0, 0.25, 0.5, 0.75, 1.0):
            worst = max(worst, b.inside(rest_of(elbow.lerp(wrist, t)), lower))
        return worst

    def place_elbow(self, shoulder, elbow, wrist, rest_of, wrist_bend=None):
        """Turns the elbow round the line from the shoulder to the wrist, which leaves the hand where it is:
        out of the torso first, then, with a hand on a handle, round to where the wrist needn't bend past
        EASY_WRIST. Otherwise it stays as close to the pose's own elbow as it can. Returns the elbow and
        the way it points from that line."""
        axis = wrist - shoulder
        centre = shoulder + axis.normalized() * (elbow - shoulder).dot(axis.normalized()) if axis.length > 1e-5 else shoulder
        spoke = elbow - centre
        side = spoke.normalized() if spoke.length > 1e-4 else Vector((0.0, 1.0, 0.0))

        def cost(candidate, turned):
            value = self.arm_inside(shoulder, candidate, wrist, rest_of) * 400.0 + abs(turned) * 0.02
            if wrist_bend is not None:
                value += max(0.0, wrist_bend((wrist - candidate).normalized()) - EASY_WRIST) * 0.2
            return value

        if axis.length < 1e-5 or spoke.length < 1e-4 or cost(elbow, 0.0) <= 0.0:
            return elbow, side
        axis.normalize()
        best = None
        for step in range(-36, 37):
            angle = 2.5 * step
            candidate = centre + Quaternion(axis, math.radians(angle)) @ spoke
            value = cost(candidate, angle)
            if best is None or value < best[0] - 1e-9:
                best = (value, candidate)
        joint = best[1]
        return joint, (joint - centre).normalized()

    def clear_hand(self, shoulder, wrist, rest_of):
        """A hand that isn't holding anything, moved out of the torso, and then swung outward round the
        shoulder until an elbow can go round the belly between them."""
        rested = rest_of(wrist)
        bone = self.b.torso_bone(rested.z)
        wrist = rest_of.back(self.b.pushed_out(rested, HAND_RADIUS), bone)
        outward = Vector((1.0 if shoulder.x > 0 else -1.0, 0.0, 0.0))
        forward = Vector((0.0, -1.0, 0.0))
        best = None
        for step in range(0, 31):
            turned = shoulder + Quaternion(forward, math.radians(2.0 * step) * (1.0 if outward.x > 0 else -1.0)) @ (wrist - shoulder)
            joint, end, _ = solve(shoulder, turned, self.b.upper, self.b.lower, outward)
            joint, _ = self.place_elbow(shoulder, joint, end, rest_of)
            depth = self.arm_inside(shoulder, joint, end, rest_of)
            if depth <= 0.0:
                return turned
            if best is None or depth < best[0] - 0.002:
                best = (depth, turned)
        return best[1]

    def clear_tool(self, pose, grips, delta, rest_of, shoulders):
        """The tool moved, both grips together, by as little as gets the hands and the tool out of the body
        while both hands can still reach it: forward, or up over a belly, or some of each."""
        forward = delta["Chest"] @ Vector((0.0, -1.0, 0.0))
        forward.z = 0.0
        forward.normalize()
        up = Vector((0.0, 0.0, 1.0))
        point, frame, _ = grips["R"]
        balls = self.tool_balls.get(pose.tool, [])
        reach = self.b.upper + self.b.lower - 0.01

        def depth(shift):
            worst = 0.0
            for grip, _, _ in grips.values():
                worst = max(worst, self.b.inside(rest_of(grip + shift), HAND_RADIUS))
            for local, radius in balls:
                worst = max(worst, self.b.inside(rest_of(point + frame @ local + shift), radius))
            return worst

        def short_of(shift):
            """How much further than an arm's length the farther wrist would be."""
            worst = 0.0
            for s, (grip, canon, _) in grips.items():
                transform, unturn, wrist_rest = self.hand[s]
                wrist = grip + shift - (canon @ unturn) @ (transform @ GRIP_CENTER - wrist_rest)
                worst = max(worst, (wrist - shoulders[s]).length - reach)
            return worst

        best = None
        for total in range(0, int(round(pose.clear * 100)) + 1):
            for lift in range(0, total + 1):
                shift = forward * (0.01 * (total - lift)) + up * (0.01 * lift)
                if depth(shift) <= 0.0 and short_of(shift) <= max(0.0, short_of(Vector())):
                    best = shift
                    break
            if best is not None:
                break
        if best is None:
            best = Vector()
        return {s: (grip + best, f, handle) for s, (grip, f, handle) in grips.items()}

    def tool_grips(self, pose):
        """Each hand's grip point, canonical frame, and the way the handle it closes round runs."""
        grip = Vector(pose.grip)
        axis = Vector(pose.axis).normalized()
        if pose.tool == "Hammer":
            frames = {}
            for s, sign in (("R", 1.0), ("L", -1.0)):
                back = Vector(pose.back_r if s == "R" else pose.back_l)
                back = (back - axis * back.dot(axis)).normalized()
                x = axis * sign
                y = back.cross(x) * sign
                frames[s] = Matrix((x, y, back)).transposed()
            return {"R": (grip, frames["R"], axis), "L": (grip - axis * pose.left_grip, frames["L"], axis)}
        up = Vector(pose.up)
        up = (up - axis * up.dot(axis)).normalized()
        right = Matrix((-up, axis, axis.cross(up))).transposed()
        left = Matrix((-up, axis, up.cross(axis))).transposed()
        return {"R": (grip, right, -up), "L": (grip + axis * VACUUM_FORE_GRIP, left, -up)}

    def apply(self, pose, frame_number, key=True):
        b = self.b
        delta = {"Root": Quaternion()}
        place = {"Root": self.rest["Root"].copy()}
        location = {"Root": Vector(pose.root_loc), "Hips": Vector(pose.hips_loc)}

        def chain(name, parent, rotation=None, offset=None):
            delta[name] = delta[parent] @ (rotation or Quaternion())
            place[name] = place[parent] + delta[parent] @ (self.rest[name] - self.rest[parent])
            if offset is not None:
                place[name] = place[name] + offset

        chain("Hips", "Root", deg_quat(pose.hips), Vector(pose.hips_loc))
        chain("Spine", "Hips", deg_quat(pose.spine))
        chain("Chest", "Spine", deg_quat(pose.chest))
        chain("Neck", "Chest", deg_quat(tuple(a * 0.4 for a in pose.head)))
        chain("Head", "Neck", deg_quat(tuple(a * 0.6 for a in pose.head)))
        chain("Stars", "Head")

        grips = self.tool_grips(pose) if pose.tool else None
        rest_of = self.rest_space(delta, place)
        if grips is not None:
            shoulders = {}
            for s, prefix, _ in SIDES:
                chain(f"{prefix}Shoulder", "Chest")
                chain(f"{prefix}UpperArm", f"{prefix}Shoulder")
                shoulders[s] = place[f"{prefix}UpperArm"]
            grips = self.clear_tool(pose, grips, delta, rest_of, shoulders)
        for s, prefix, sign in SIDES:
            chain(f"{prefix}Shoulder", "Chest")
            chain(f"{prefix}UpperArm", f"{prefix}Shoulder")
            shoulder = place[f"{prefix}UpperArm"]
            in_body = pose.hands_in_body >= 0.5 or grips is not None
            hint = Vector(pose.elbow_l if s == "L" else pose.elbow_r)
            if not in_body:
                hint = delta["Chest"] @ hint
            transform, unturn, wrist_rest = self.hand[s]
            turn = None
            wrist_bend = None
            rolls = False
            if grips is not None:
                point, canon, handle = grips[s]
                # The tool turns with the right hand. The hammer's head can go round its handle with the
                # hand, but the vacuum has to keep aiming where the pose says, so that hand keeps its turn.
                rolls = pose.tool == "Hammer" or s == "L"
                if rolls:
                    wrist_bend = wrist_bend_on(canon, handle)
                else:
                    fingers = canon.col[1].normalized()
                    wrist_bend = lambda forearm, fingers=fingers: math.degrees(forearm.angle(fingers))
            else:
                wrist = Vector(pose.hand_l if s == "L" else pose.hand_r)
                if not in_body:
                    wrist = shoulder + delta["Chest"] @ wrist
                wrist = self.clear_hand(shoulder, wrist, rest_of)
                palm = pose.palm_l if s == "L" else pose.palm_r
                if palm is not None:
                    fingers, back = Vector(palm[0]), Vector(palm[1])
                    if not in_body:
                        fingers, back = delta["Chest"] @ fingers, delta["Chest"] @ back
                    turn = canonical(s, fingers, back) @ unturn
            # A hand on a handle turns on it to follow the forearm, which moves the wrist a little, so the
            # arm is solved again until they agree, with the elbow kept on the side it was first put.
            for attempt in range(4):
                if grips is not None:
                    turn = canon @ unturn
                    wrist = point - turn @ (transform @ GRIP_CENTER - wrist_rest)
                joint, end, side = solve(shoulder, wrist, b.upper, b.lower, hint)
                if attempt == 0:
                    joint, side = self.place_elbow(shoulder, joint, end, rest_of, wrist_bend)
                    hint = side
                if not rolls or attempt == 3:
                    break
                canon = roll_on_handle(canon, (end - joint).normalized(), handle)
            if (end - wrist).length > 0.02:
                self.misses.append((frame_number, s, round((end - wrist).length, 3)))
            upper = (joint - shoulder).normalized()
            lower = (end - joint).normalized()
            hinge = hinge_of(upper, lower, side)
            upper_turn = (frame(upper, hinge) @ self.frames[f"{prefix}UpperArm"].transposed()).to_quaternion()
            lower_turn = (frame(lower, hinge) @ self.frames[f"{prefix}LowerArm"].transposed()).to_quaternion()
            hand_turn = turn.to_quaternion() if turn is not None else lower_turn
            if turn is not None:
                # Share the hand's twist with the forearm, so the wrist doesn't wring like a towel.
                rest_axis = self.frames[f"{prefix}LowerArm"].col[1]
                angle = twist_about(lower_turn.inverted() @ hand_turn, rest_axis)
                lower_turn = Quaternion(lower, angle * 0.5) @ lower_turn
            delta[f"{prefix}UpperArm"] = upper_turn
            delta[f"{prefix}LowerArm"] = lower_turn
            delta[f"{prefix}Hand"] = hand_turn

            chain(f"{prefix}UpperLeg", "Hips")
            hip = place[f"{prefix}UpperLeg"]
            foot = Vector(pose.foot_l if s == "L" else pose.foot_r)
            knee = Vector(pose.knee_l if s == "L" else pose.knee_r)
            joint, end, side = solve(hip, foot, b.thigh, b.shin, knee)
            thigh = (joint - hip).normalized()
            shin = (end - joint).normalized()
            hinge = hinge_of(thigh, shin, side)
            delta[f"{prefix}UpperLeg"] = (frame(thigh, hinge) @ self.frames[f"{prefix}UpperLeg"].transposed()).to_quaternion()
            delta[f"{prefix}LowerLeg"] = (frame(shin, hinge) @ self.frames[f"{prefix}LowerLeg"].transposed()).to_quaternion()
            pitch = pose.foot_pitch_l if s == "L" else pose.foot_pitch_r
            delta[f"{prefix}Foot"] = deg_quat((pitch, 0.0, 0.0))
            delta[f"{prefix}Toes"] = delta[f"{prefix}Foot"]

        finger_turns = {}
        for s, prefix, _ in SIDES:
            weights = (pose.fingers_l if s == "L" else pose.fingers_r) or {"Relaxed": 1.0}
            total = sum(w for w in weights.values() if w > 0) or 1.0
            for digit in DIGITS:
                for segment in SEGMENTS:
                    name = prefix + digit + segment
                    blended = Quaternion((0, 0, 0, 0))
                    first = None
                    for pose_name, weight in weights.items():
                        if weight <= 0:
                            continue
                        q = self.fingers[pose_name][name].copy()
                        if first is None:
                            first = q
                        elif first.dot(q) < 0:
                            q.negate()
                        blended = Quaternion(tuple(a + b * (weight / total) for a, b in zip(blended, q)))
                    blended.normalize()
                    finger_turns[name] = blended

        root_turn = deg_quat(pose.root_rot)
        for pose_bone in self.arm.pose.bones:
            name = pose_bone.name
            rest_turn = self.rot[name]
            if name in finger_turns:
                local = finger_turns[name]
            elif self.parents[name] is None:
                local = rest_turn.inverted() @ root_turn @ rest_turn
            else:
                parent = self.parents[name]
                local = rest_turn.inverted() @ delta[parent].inverted() @ delta[name] @ rest_turn
            previous = self.previous.get(name)
            if previous is not None and previous.dot(local) < 0.0:
                local.negate()
            self.previous[name] = local.copy()
            pose_bone.rotation_quaternion = local
            if name in location:
                pose_bone.location = rest_turn.inverted() @ location[name]
            if key:
                pose_bone.keyframe_insert("rotation_quaternion", frame=frame_number)
                if name in location:
                    pose_bone.keyframe_insert("location", frame=frame_number)

    def bake(self, name, seconds, function):
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        if self.arm.animation_data is None:
            self.arm.animation_data_create()
        self.arm.animation_data.action = action
        self.previous = {}
        self.misses = []
        frames = max(int(round(seconds * FPS)), 1)
        for number in range(frames + 1):
            self.apply(function(self.b, number / frames), number)
        if self.misses:
            worst = max(self.misses, key=lambda m: m[2])
            print(f"[crew] {self.b.name} {name}: a hand falls short in {len(self.misses)} frames, worst {worst}")
        return action, frames

    def clear(self):
        if self.arm.animation_data is not None:
            self.arm.animation_data.action = None
        for pose_bone in self.arm.pose.bones:
            pose_bone.location = (0, 0, 0)
            pose_bone.rotation_quaternion = (1, 0, 0, 0)


# ---- The clips ----

TAU = 2 * math.pi
RELAXED = {"Relaxed": 1.0}
LOOSE_FIST = {"Relaxed": 0.5, "Fist": 0.5}
FIST = {"Fist": 1.0}
GRIP = {"Grip": 1.0}
CARRY = {"Carry": 1.0}
OPEN = {"Open": 1.0}

# Where her review pose holds the hammer across the hips, per body type.
READY_HAMMER_Y = {"Short": -0.325, "Medium": -0.26, "Tall": -0.365}


def idle(b, u):
    a = TAU * u
    breathe = math.sin(a)
    return pose(
        b,
        hips_loc=(0, 0, -0.008 * b.leg + 0.005 * breathe),
        hips=(0, 1.0 * math.sin(a + 0.5), 0),
        spine=(1.5 + 1.0 * breathe, 0, 0),
        chest=(-1.0 + 1.6 * math.sin(a + 0.5), 0, 1.5 * math.sin(a)),
        head=(1.5 - 1.5 * math.sin(a + 1.0), 0, 3.0 * math.sin(a)),
        hand_l=b.hang("L", up=0.012 * breathe),
        hand_r=b.hang("R", up=0.012 * breathe),
    )


def run_foot(b, side, phase, stride, lift):
    phase %= TAU
    forward = stride * math.cos(phase)
    up = 0.0 if phase < math.pi else lift * math.sin(phase - math.pi)
    pitch = 12.0 - 30.0 * math.cos(phase - 0.6)
    return b.stand(side, out=-0.03, forward=forward / b.leg, up=up / b.leg), pitch


def run(b, u):
    a = TAU * u
    foot_l, pitch_l = run_foot(b, "L", a, 0.34 * b.leg, 0.3 * b.leg)
    foot_r, pitch_r = run_foot(b, "R", a + math.pi, 0.34 * b.leg, 0.3 * b.leg)
    swing = -math.cos(a)

    def hand(side, s):
        sign = 1.0 if side == "L" else -1.0
        return tuple(Vector((0.26 * sign, -0.06 - 0.5 * s, -0.55 + 0.16 * max(s, 0.0))) * b.arm)

    return pose(
        b,
        hips_loc=(0, -0.04 * b.leg, -0.07 * b.leg - 0.04 * b.leg * math.cos(2 * a)),
        hips=(7.0, 10.0 * math.sin(a), -9.0 * math.cos(a)),
        spine=(6.0, -3.0 * math.sin(a), 6.0 * math.cos(a)),
        chest=(4.0 + 2.0 * math.cos(2 * a), -3.0 * math.sin(a), 8.0 * math.cos(a)),
        head=(-12.0 - 2.0 * math.cos(2 * a), -3.0 * math.sin(a), -5.0 * math.cos(a)),
        hand_l=hand("L", swing), hand_r=hand("R", -swing),
        elbow_l=(0.45, 0.85, -0.2), elbow_r=(-0.45, 0.85, -0.2),
        foot_l=foot_l, foot_r=foot_r, foot_pitch_l=pitch_l, foot_pitch_r=pitch_r,
        fingers_l=LOOSE_FIST, fingers_r=LOOSE_FIST,
    )


def held_cargo(b, half_width):
    """Where the wrists go to hold cargo against the belly, a palm on each side of it. CharacterVisual
    puts the cargo just in front of the point between the wrists."""
    height = b.hips.z + 0.42 * (b.shoulder["L"].z - b.hips.z)
    front = b.front(height - 0.12 * b.leg, height + 0.12 * b.leg)
    return half_width / GAME_SCALE + 0.05, front - 0.1, height


def hold(b, u, half_width):
    a = TAU * u
    x, y, z = held_cargo(b, half_width)
    return pose(
        b,
        hands_in_body=1.0,
        hand_l=(x, y, z + 0.01 * math.sin(a)), hand_r=(-x, y, z + 0.01 * math.sin(a + 1.4)),
        palm_l=((0, -1, -0.3), (1, 0, 0)), palm_r=((0, -1, -0.3), (-1, 0, 0)),
        elbow_l=(1.0, 0.35, -0.45), elbow_r=(-1.0, 0.35, -0.45),
        spine=(-5.0, 1.5 * math.sin(a), 0), chest=(-4.0 + math.sin(2 * a), 1.5 * math.sin(a + 0.6), 0),
        head=(5.0, -2.0 * math.sin(a + 1.0), 0),
        fingers_l=CARRY, fingers_r=CARRY,
    )


def carry(b, u):
    return hold(b, u, 0.3)


def carry_big(b, u):
    return hold(b, u, 0.52)


def toss(b, u):
    x, y, z = held_cargo(b, 0.3)
    up = b.shoulder["L"].z + 0.1 * b.arm
    keys = [
        (0.0, carry(b, 0.0)),
        (0.14, pose(b, hands_in_body=1.0, hand_l=(x * 0.9, y - 0.35 * b.arm, up), hand_r=(-x * 0.9, y - 0.35 * b.arm, up),
                    palm_l=((0, -0.6, 0.8), (1, 0.3, 0)), palm_r=((0, -0.6, 0.8), (-1, 0.3, 0)),
                    elbow_l=(1.0, 0.3, -0.2), elbow_r=(-1.0, 0.3, -0.2),
                    spine=(6.0, 0, 0), chest=(12.0, 0, 0), head=(-6.0, 0, 0), fingers_l=OPEN, fingers_r=OPEN)),
        (0.36, pose(b, hands_in_body=1.0,
                    hand_l=tuple(b.shoulder["L"] + Vector(b.hang("L"))), hand_r=tuple(b.shoulder["R"] + Vector(b.hang("R"))))),
    ]
    return keyed(keys, 0.36)(u)


def co_carry_values(b, a):
    """Leaning in to hold the end of the big crate, which is waist high in front of you."""
    reach = b.hips.z + 0.25 * (b.shoulder["L"].z - b.hips.z) - 0.05 * b.leg
    height = min(reach, 0.75 / GAME_SCALE)
    lean = 14.0 + 26.0 * max(0.0, min(1.0, (b.shoulder["L"].z - 1.0) / 0.5))
    x = 0.38 / GAME_SCALE
    y = -0.55 / GAME_SCALE + 0.1
    return dict(
        hands_in_body=1.0,
        hand_l=(x, y, height), hand_r=(-x, y, height),
        palm_l=((0, -1, -0.2), (1, 0, 0.3)), palm_r=((0, -1, -0.2), (-1, 0, 0.3)),
        elbow_l=(1.0, 0.4, -0.3), elbow_r=(-1.0, 0.4, -0.3),
        hips=(lean * 0.5, 0, 0), spine=(lean * 0.3, 0, 0),
        chest=(lean * 0.2 + 1.0 * math.sin(4 * a), 1.2 * math.sin(2 * a), 0), head=(-lean * 0.6, 0, 0),
        fingers_l=CARRY, fingers_r=CARRY,
    )


def co_carry_idle(b, u):
    a = TAU * u
    values = co_carry_values(b, a)
    return pose(
        b,
        hips_loc=(0, 0.06 * b.leg, -0.1 * b.leg + 0.006 * math.sin(2 * a)),
        foot_l=b.stand("L", out=0.06, forward=-0.08), foot_r=b.stand("R", out=0.06, forward=0.12),
        **values,
    )


def co_carry_walk(b, u):
    a = TAU * u
    foot_l, pitch_l = run_foot(b, "L", a, 0.16 * b.leg, 0.1 * b.leg)
    foot_r, pitch_r = run_foot(b, "R", a + math.pi, 0.16 * b.leg, 0.1 * b.leg)
    values = co_carry_values(b, a)
    values["hips"] = (values["hips"][0], 6.0 * math.sin(a), 0)
    return pose(
        b,
        hips_loc=(0, 0.06 * b.leg, -0.1 * b.leg - 0.015 * b.leg * math.cos(2 * a)),
        foot_l=foot_l, foot_r=foot_r, foot_pitch_l=pitch_l * 0.5, foot_pitch_r=pitch_r * 0.5,
        **values,
    )


def ram_pose(b):
    return pose(
        b,
        hips_loc=(0, -0.12 * b.leg, -0.2 * b.leg),
        hips=(22.0, 0, -8.0), spine=(14.0, 0, -8.0), chest=(10.0, 0, -18.0), head=(-28.0, 0, 22.0),
        hand_l=tuple(Vector((-0.2, -0.55, -0.55)) * b.arm), hand_r=tuple(Vector((-0.05, 0.6, -0.5)) * b.arm),
        elbow_l=(0.8, 0.4, -0.3), elbow_r=(-0.7, 0.5, 0.1),
        foot_l=b.stand("L", forward=0.42, up=0.05), foot_r=b.stand("R", forward=-0.5, up=0.18),
        foot_pitch_l=-12.0, foot_pitch_r=42.0,
        fingers_l=FIST, fingers_r=FIST,
    )


def ram(b, u):
    wobble = math.sin(u * TAU * 2) * 1.5
    result = mix_poses(pose(b), ram_pose(b), smooth(u / 0.3))
    result.chest = (result.chest[0] + wobble, result.chest[1], result.chest[2])
    return result


def lying(b, **values):
    """Flat on the back, arms and legs out."""
    lift = b.back(b.hips.z, b.shoulder["L"].z) + 0.02
    base = dict(
        root_rot=(-92.0, 0.0, 8.0), root_loc=(0.0, 0.18 * b.top, lift),
        hand_l=tuple(Vector((0.92, 0.12, 0.08)) * b.arm), hand_r=tuple(Vector((-0.92, 0.12, 0.08)) * b.arm),
        elbow_l=(0.2, 0.2, 1.0), elbow_r=(-0.2, 0.2, 1.0),
        foot_l=b.stand("L", out=0.16, forward=0.05, up=0.05), foot_r=b.stand("R", out=0.16, forward=0.05, up=0.05),
        foot_pitch_l=-30.0, foot_pitch_r=-30.0, knee_l=(0.8, -0.6, 0.0), knee_r=(-0.8, -0.6, 0.0),
        fingers_l=OPEN, fingers_r=OPEN,
    )
    base.update(values)
    return pose(b, **base)


def falling(b):
    h = b.top / 1.9
    lift = b.back(b.hips.z, b.shoulder["L"].z) + 0.02
    return [
        (0.00, pose(b)),
        (0.06, pose(b, root_rot=(-22.0, 0, 0), root_loc=(0, 0.06 * h, 0.09 * h), spine=(18.0, 0, 0), chest=(12.0, 0, 0), head=(20.0, 0, 0),
                    hand_l=tuple(Vector((0.4, -0.75, 0.2)) * b.arm), hand_r=tuple(Vector((-0.4, -0.75, 0.2)) * b.arm),
                    foot_l=b.stand("L", forward=0.3, up=0.2), foot_r=b.stand("R", forward=0.45, up=0.3),
                    foot_pitch_l=-25.0, foot_pitch_r=-25.0, fingers_l=OPEN, fingers_r=OPEN)),
        (0.20, pose(b, root_rot=(-62.0, 0, 0), root_loc=(0, 0.3 * h, 0.45 * h), spine=(-12.0, 0, 0), chest=(-8.0, 0, 0), head=(12.0, 0, 0),
                    hand_l=tuple(Vector((0.68, 0.0, 0.72)) * b.arm), hand_r=tuple(Vector((-0.64, -0.2, 0.76)) * b.arm),
                    foot_l=b.stand("L", out=0.08, forward=0.5, up=0.3), foot_r=b.stand("R", out=0.04, forward=0.7, up=0.2),
                    foot_pitch_l=-35.0, foot_pitch_r=-35.0, fingers_l=OPEN, fingers_r=OPEN)),
        (0.34, lying(b, root_rot=(-92.0, 0, 0), root_loc=(0, 0.17 * b.top, lift + 0.03),
                     hand_l=tuple(Vector((0.82, 0.0, 0.4)) * b.arm), hand_r=tuple(Vector((-0.82, 0.0, 0.4)) * b.arm),
                     foot_l=b.stand("L", out=0.1, forward=0.4, up=0.25), foot_r=b.stand("R", out=0.1, forward=0.4, up=0.25),
                     foot_pitch_l=0.0, foot_pitch_r=0.0)),
        (0.44, lying(b, root_rot=(-84.0, 0, 0), root_loc=(0, 0.19 * b.top, lift + 0.08), head=(10.0, 0, 0),
                     hand_l=tuple(Vector((0.68, -0.5, 0.28)) * b.arm), hand_r=tuple(Vector((-0.68, -0.5, 0.28)) * b.arm),
                     foot_l=b.stand("L", out=0.1, forward=0.55, up=0.2), foot_r=b.stand("R", out=0.1, forward=0.55, up=0.2),
                     foot_pitch_l=0.0, foot_pitch_r=0.0)),
        (0.56, lying(b, head=(-6.0, 14.0, 0))),
    ]


def knocked_out(b, u):
    return keyed(falling(b) + [
        (0.8, lying(b, head=(-6.0, 8.0, 0))),
        (1.2, lying(b, head=(-6.0, 0.0, 0))),
    ], 1.2)(u)


def knocked(b, u):
    h = b.top / 1.9
    return keyed(falling(b) + [
        (0.95, lying(b, head=(-6.0, -12.0, 0))),
        (1.10, pose(b, root_rot=(-48.0, 0, 0), root_loc=(0, 0.26 * h, 0.2 * h), spine=(18.0, 0, 0), chest=(10.0, 0, 0), head=(12.0, 0, 0),
                    hand_l=tuple(Vector((0.55, 0.32, -0.68)) * b.arm), hand_r=tuple(Vector((-0.55, 0.32, -0.68)) * b.arm),
                    foot_l=b.stand("L", out=0.05, forward=0.45, up=0.12), foot_r=b.stand("R", out=0.05, forward=0.45, up=0.12),
                    foot_pitch_l=-20.0, foot_pitch_r=-20.0, fingers_l=OPEN, fingers_r=OPEN)),
        (1.24, pose(b, root_rot=(10.0, 0, 0), root_loc=(0, 0.1 * h, 0.0), hips_loc=(0, 0, -0.22 * b.leg),
                    hand_l=tuple(Vector((0.4, -0.5, -0.4)) * b.arm), hand_r=tuple(Vector((-0.4, -0.5, -0.4)) * b.arm),
                    foot_l=b.stand("L", out=0.03, forward=0.05), foot_r=b.stand("R", out=0.03, forward=0.05))),
        (1.38, pose(b, root_loc=(0, 0, 0.02 * h), hips_loc=(0, 0, -0.01 * b.leg), spine=(-6.0, 0, 0), head=(0, 16.0, 0))),
        (1.50, pose(b, head=(0, -6.0, 0))),
    ], 1.5)(u)


# ---- Tools ----

def hammer(b, grip, axis, back_r=(-1.0, 0.0, 0.0), back_l=(1.0, 0.0, 0.0), **values):
    base = dict(tool="Hammer", grip=tuple(grip), axis=tuple(axis), back_r=back_r, back_l=back_l,
                elbow_r=(-1.0, 0.3, -0.3), elbow_l=(1.0, 0.3, -0.3), fingers_l=GRIP, fingers_r=GRIP)
    base.update(values)
    return pose(b, **base)


def ready_hammer_pose(b, sway=0.0):
    """Her review pose: across the hips, head out to the right, both hands over the handle."""
    grip = (-0.16, READY_HAMMER_Y[b.name], b.hips.z - 0.04 + 0.006 * sway)
    return hammer(b, grip, (-1.0, 0.0, 0.0), back_r=(0.0, -1.0, 0.25), back_l=(0.0, -1.0, 0.25),
                  elbow_r=(-0.38, 0.52, -0.55), elbow_l=(0.38, 0.52, -0.55),
                  chest=(2.0 + sway, 0, -3.0), head=(2.0, 0, 3.0))


def ready_hammer(b, u):
    return ready_hammer_pose(b, math.sin(TAU * u))


# Where the hands are when the hammer lands, per body type: how far in front of the belly and how far
# above the hips, for a smash and for a slam (which crouches). Found by trying poses on each body
# (forearms coming down across the handle, so the wrists stay nearly straight, and nothing inside the
# body, the end of the handle included). The handle points 50 degrees down, and the left hand slides up
# toward the right one.
STRIKE = {"Short": ((0.2, 0.16), (0.2, 0.08)), "Medium": ((0.3, 0.08), (0.3, 0.16)), "Tall": ((0.5, 0.0), (0.4, 0.16))}
STRIKE_PITCH = 50.0
STRIKE_LEFT_GRIP = 0.2


def strike_grip(b, slam=False, lift=0.0):
    forward, up = STRIKE[b.name][1 if slam else 0]
    front = b.front(b.hips.z - 0.05, b.hips.z + 0.25)
    drop = 0.1 * b.leg if slam else 0.0
    return (0.06, front - forward, b.hips.z + up - drop + lift)


def strike_axis(pitch=STRIKE_PITCH):
    return (0.04, -math.cos(math.radians(pitch)), -math.sin(math.radians(pitch)))


def upright_in_front(b, across, drop):
    """A grip for the hammer held upright in front: a little in from the right shoulder and `drop` arm
    lengths below it, far enough out that the hand lower on the handle clears the chest or belly."""
    z = b.shoulder["R"].z - drop * b.arm
    front = b.front(z - 0.3, z)
    return (b.shoulder["R"].x + across * b.arm, min(b.shoulder["R"].y - 0.3 * b.arm, front - 0.18), z)


def downswing(b, slam=False):
    """Half way down: the hands out in front at chest height and the head level with them, so the hand
    behind on the handle sweeps round in front of the chest instead of through it."""
    grip = Vector(strike_grip(b, slam)) + Vector((0.0, -0.08, 0.3 * b.arm))
    return hammer(b, grip, (0.04, -0.97, 0.22), clear=0.15, left_grip=STRIKE_LEFT_GRIP,
                  hips_loc=(0, 0, -0.05 * b.leg if slam else -0.02 * b.leg),
                  spine=(10.0, 0, 0), chest=(14.0, 0, 6.0), head=(-4.0, 0, -2.0))


def smash_keys(b):
    # The head stays on the target: it tips forward as the body leans back, and back as it leans in.
    top = hammer(b, b.shoulder_at("R", 0.05, -0.1, 0.38), (0.1, 0.75, 0.65),
                 spine=(-6.0, 0, 0), chest=(-12.0, 0, -14.0), head=(14.0, 0, 8.0))
    wind = hammer(b, b.shoulder_at("R", 0.0, -0.18, 0.36), (0.1, 0.92, 0.38),
                  spine=(-8.0, 0, 0), chest=(-18.0, 0, -20.0), head=(20.0, 0, 12.0))
    over = hammer(b, Vector(upright_in_front(b, 0.3, 0.0)) + Vector((0.0, 0.0, 0.3 * b.arm)), (0.05, -0.25, 0.97),
                  spine=(4.0, 0, 0), chest=(6.0, 0, 0), head=(4.0, 0, 2.0))
    # Quick, so the tool may only shift a little to clear the body; the next key moves on anyway.
    hit = hammer(b, strike_grip(b), strike_axis(), hips_loc=(0, 0, -0.04 * b.leg), clear=0.1, left_grip=STRIKE_LEFT_GRIP,
                 spine=(16.0, 0, 0), chest=(24.0, 0, 10.0), head=(-12.0, 0, -6.0))
    bounce = hammer(b, strike_grip(b, lift=0.07), strike_axis(STRIKE_PITCH - 14.0), hips_loc=(0, 0, -0.03 * b.leg),
                    clear=0.1, left_grip=STRIKE_LEFT_GRIP, spine=(12.0, 0, 0), chest=(18.0, 0, 8.0), head=(-8.0, 0, -4.0))
    # On the way back up it comes upright in front first, so it doesn't sweep round past the wrists.
    lift = hammer(b, upright_in_front(b, 0.32, 0.05), (0.05, -0.3, 0.95),
                  spine=(4.0, 0, 0), chest=(4.0, 0, -4.0), head=(2.0, 0, 2.0))
    return top, wind, over, hit, bounce, lift


def smash(b, u):
    top, wind, over, hit, bounce, lift = smash_keys(b)
    return keyed([(0.0, top), (0.19, wind), (0.235, over), (0.255, downswing(b)), (0.275, hit), (0.34, bounce),
                  (0.42, lift), (0.5, top)], 0.5)(u)


def charge_pose(b, shake=0.0):
    return hammer(b, b.shoulder_at("R", 0.05 + shake, -0.08, 0.5 + shake), (0.1, 0.6, 0.79),
                  spine=(-9.0, 0, 0), chest=(-20.0, 0, -16.0), head=(16.0, 0, 10.0))


def charge(b, u):
    return charge_pose(b, math.sin(TAU * u * 6) * 0.02)


def slam(b, u):
    top = charge_pose(b)
    over = hammer(b, Vector(upright_in_front(b, 0.3, 0.0)) + Vector((0.0, 0.0, 0.35 * b.arm)), (0.05, -0.25, 0.97),
                  spine=(6.0, 0, 0), chest=(8.0, 0, 0), head=(4.0, 0, 0))
    hit = hammer(b, strike_grip(b, slam=True), strike_axis(), hips_loc=(0, 0, -0.1 * b.leg), clear=0.1, left_grip=STRIKE_LEFT_GRIP,
                 spine=(22.0, 0, 0), chest=(30.0, 0, 10.0), head=(-16.0, 0, -6.0),
                 foot_l=b.stand("L", forward=0.12), foot_r=b.stand("R", forward=-0.08))
    lift = hammer(b, upright_in_front(b, 0.32, 0.1), (0.05, -0.3, 0.95), spine=(4.0, 0, 0), chest=(4.0, 0, -4.0))
    return keyed([(0.0, top), (0.07, over), (0.105, downswing(b, True)), (0.14, hit), (0.26, hit), (0.38, lift),
                  (0.5, ready_hammer_pose(b))], 0.5)(u)


def vacuum(b, grip, aim, up=(0.0, 0.0, 1.0), **values):
    base = dict(tool="Vacuum", grip=tuple(grip), axis=tuple(aim), up=up,
                elbow_r=(-1.0, 0.3, -0.4), elbow_l=(1.0, 0.1, -0.6), fingers_l=GRIP, fingers_r=GRIP,
                chest=(3.0, 0, -12.0), head=(2.0, 0, 6.0))
    base.update(values)
    return pose(b, **base)


# The short body's belly is where the others hold the vacuum, and its arms are short, so it holds it out in
# front of the belly, turned a little to the left and tipped down: (to the left of the right shoulder's
# half-way point, in front of the belly, turned left, tipped down), found by trying poses like STRIKE.
READY_VACUUM = {"Short": (0.1, 0.24, 30.0, 10.0)}


def ready_vacuum_pose(b, sway=0.0, lift=0.0, back=0.0, aim=(0.0, -1.0, 0.06), **values):
    if b.name in READY_VACUUM:
        across, ahead, turn, tip = READY_VACUUM[b.name]
        z = b.hips.z + 0.12 + (lift + 0.01 * sway) * b.arm
        grip = (b.shoulder["R"].x * 0.5 + across, b.front(z - 0.06, z + 0.06) - ahead + back * b.arm, z)
        aim = tuple(deg_quat((tip, 0.0, turn)) @ Vector(aim))
    else:
        grip = b.shoulder_at("R", -0.22, 0.42 - back, -0.6 + lift + 0.01 * sway)
    return vacuum(b, grip, aim, **values)


def ready_vacuum(b, u):
    return ready_vacuum_pose(b, math.sin(TAU * u))


def blast(b, u):
    ready = ready_vacuum_pose(b)
    return keyed([
        (0.00, ready),
        (0.05, ready_vacuum_pose(b, back=-0.05, lift=-0.02, spine=(4.0, 0, 0))),
        (0.10, ready_vacuum_pose(b, back=0.12, lift=0.08, aim=(0.0, -0.95, 0.3), spine=(-8.0, 0, 0), chest=(-8.0, 0, -12.0), head=(-4.0, 0, 6.0))),
        (0.22, ready_vacuum_pose(b, back=0.04, lift=0.03, aim=(0.0, -1.0, 0.16), spine=(-3.0, 0, 0))),
        (0.55, ready),
    ], 0.55)(u)


def flinch(b, u):
    rest = pose(b, hands_in_body=1.0,
                hand_l=tuple(b.shoulder["L"] + Vector(b.hang("L"))), hand_r=tuple(b.shoulder["R"] + Vector(b.hang("R", forward=0.15, up=0.2))),
                fingers_r=GRIP)
    guard = dict(hands_in_body=1.0, elbow_l=(1.0, 0.3, -0.3), elbow_r=(-1.0, 0.3, -0.3), fingers_l=OPEN, fingers_r=GRIP)
    return keyed([
        (0.00, rest),
        (0.05, pose(b, hand_l=b.shoulder_at("L", -0.25, 0.5, 0.25), hand_r=b.shoulder_at("R", -0.15, 0.45, 0.15),
                    spine=(-10.0, 0, 0), chest=(-14.0, 0, 0), head=(-16.0, 0, 8.0), **guard)),
        (0.18, pose(b, hand_l=b.shoulder_at("L", -0.2, 0.45, 0.1), hand_r=b.shoulder_at("R", -0.1, 0.4, 0.0),
                    spine=(-5.0, 0, 0), chest=(-6.0, 0, 0), head=(-4.0, 0, 4.0), **guard)),
        (0.40, rest),
    ], 0.4)(u)


def cheer(b, u):
    a = TAU * u
    jump = max(0.0, math.sin(a))
    crouch = max(0.0, -math.sin(a))
    wave = math.sin(a)
    return pose(
        b,
        root_loc=(0, 0, 0.16 * b.leg * jump),
        hips_loc=(0, 0, -0.01 * b.leg - 0.12 * b.leg * crouch),
        spine=(-3.0, 3.0 * math.sin(a * 2), 0), chest=(-4.0, 0, 0), head=(-14.0, 0, 7.0 * wave),
        hand_l=tuple(Vector((0.45 + 0.1 * wave, -0.05, 0.86)) * b.arm),
        hand_r=tuple(Vector((-0.45 + 0.1 * wave, -0.05, 0.86)) * b.arm),
        elbow_l=(1.0, 0.1, 0.0), elbow_r=(-1.0, 0.1, 0.0),
        foot_l=b.stand("L", up=0.1 * jump), foot_r=b.stand("R", up=0.1 * jump),
        foot_pitch_l=25.0 * jump, foot_pitch_r=25.0 * jump,
        fingers_l=FIST, fingers_r=FIST,
    )


def grumble(b, u):
    a = TAU * u
    stomp = smooth(1 - abs(u - 0.32) / 0.2) if abs(u - 0.32) < 0.2 else 0.0
    shake = math.sin(a * 5)
    return pose(
        b,
        hips_loc=(0, 0, -0.02 * b.leg + 0.03 * b.leg * stomp),
        hips=(0, -5.0 * stomp, 0), chest=(8.0, 3.0 * shake, 7.0 * math.sin(a)), head=(6.0, 5.0 * shake, -9.0 * math.sin(a)),
        hand_r=tuple(Vector((-0.22 + 0.05 * shake, -0.32, 0.84)) * b.arm), elbow_r=(-1.0, 0.3, 0.0),
        hand_l=tuple(Vector((0.48, 0.18, -0.55)) * b.arm), elbow_l=(1.0, 0.6, 0.0),
        foot_r=b.stand("R", up=0.18 * stomp), foot_pitch_r=-20.0 * stomp,
        fingers_r=FIST,
    )


# Name, seconds, function. Looping and speeds are set where Unity imports them.
CLIPS = [
    ("Idle", 2.0, idle),
    ("Run", 0.8, run),
    ("Carry", 1.0, carry),
    ("CarryBig", 1.0, carry_big),
    ("CoCarryIdle", 1.2, co_carry_idle),
    ("CoCarryWalk", 0.8, co_carry_walk),
    ("Ram", 0.34, ram),
    ("Knocked", 1.5, knocked),
    ("KnockedOut", 1.2, knocked_out),
    ("Smash", 0.5, smash),
    ("Toss", 0.36, toss),
    ("ReadyHammer", 1.2, ready_hammer),
    ("ReadyVacuum", 1.2, ready_vacuum),
    ("Blast", 0.55, blast),
    ("Charge", 1.0, charge),
    ("Slam", 0.5, slam),
    ("Flinch", 0.4, flinch),
    ("Cheer", 0.8, cheer),
    ("Grumble", 1.0, grumble),
]


# ---- Check images ----

def render_sheet(rig, crew_rig, actions, folder, body):
    """A few frames of every clip, in each set of clothes, with stand-ins for the cargo."""
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 420
    scene.render.resolution_y = 420
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("CheckSky")
    world.color = (0.74, 0.79, 0.82)
    scene.world = world
    camera = modeling.setup_preview(target=(0, -0.2, 1.0), distance=4.4, angle=(35.0, 14.0), size=(420, 420), samples=1)
    scene.render.engine = "BLENDER_WORKBENCH"
    ground = bpy.data.meshes.new("CheckGround")
    ground.from_pydata([(-5, -5, 0), (5, -5, 0), (5, 5, 0), (-5, 5, 0)], [], [(0, 1, 2, 3)])
    ground.materials.append(game_materials(["Crew_Light"])["Crew_Light"])
    ground_object = bpy.data.objects.new("CheckGround", ground)
    scene.collection.objects.link(ground_object)
    box = bpy.data.objects.new("CheckBox", bpy.data.meshes.new("CheckBox"))
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.to_mesh(box.data)
    bm.free()
    box.data.materials.append(game_materials(["Hammer_Grip"])["Hammer_Grip"])
    scene.collection.objects.link(box)

    sizes = {"Carry": (0.3, 0.3, 0.3), "CarryBig": (0.52, 0.46, 0.46), "Toss": (0.3, 0.3, 0.3)}
    for outfit, other in (("Wrecker", "Porter"), ("Porter", "Wrecker")):
        bpy.data.objects[other].hide_render = True
        bpy.data.objects[outfit].hide_render = False
        for obj in bpy.data.objects:
            if "_Hat_" in obj.name or "_Glasses_" in obj.name:
                obj.hide_render = obj.name != f"{outfit}_Hat_classic"
        for name, action, frames in actions:
            rig.animation_data.action = action
            bpy.data.objects["Hammer"].hide_render = name not in ("Smash", "ReadyHammer", "Charge", "Slam")
            bpy.data.objects["Vacuum"].hide_render = name not in ("ReadyVacuum", "Blast")
            bpy.data.objects["DizzyStars"].hide_render = name not in ("Knocked", "KnockedOut")
            for step in range(4):
                number = int(round(step / 4 * frames))
                scene.frame_set(number)
                box.hide_render = True
                if name in sizes or name.startswith("CoCarry"):
                    left = rig.matrix_world @ rig.pose.bones["LeftHand"].head
                    right = rig.matrix_world @ rig.pose.bones["RightHand"].head
                    if name.startswith("CoCarry"):
                        half = Vector((0.56, 1.16, 0.53)) / GAME_SCALE
                        box.location = (0.0, -(0.55 / GAME_SCALE + half.y), 0.55 / GAME_SCALE)
                    else:
                        half = Vector(sizes[name]) / GAME_SCALE
                        middle = (left + right) * 0.5
                        box.location = middle + Vector((0, -1, 0)) * (half.y - 0.05 / GAME_SCALE)
                    box.scale = half * 2
                    box.hide_render = name == "Toss" and step >= 2
                scene.render.filepath = str(folder / f"{body}_{outfit}_{name}_{step}.png")
                bpy.ops.render.render(write_still=True)


def export(body):
    """Like modeling.export_rigged_fbx, but without applying modifiers: that would drop the face and
    glasses sliders. The only modifiers here are the skeleton's, which go out as skin weights anyway."""
    output = ROOT / f"Assets/Game/Models/Crew_{body}.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        path_mode="STRIP",
    )
    print("Wrote", output)


def main():
    options = arguments()
    for body in options["only"]:
        rig, hands, meshes, report, top = load_body(options["source"], body)
        hammer_mesh, vacuum_mesh = attach_tools(rig, options["source"], hands)
        report["Hammer"] = triangles(hammer_mesh)
        report["Vacuum"] = triangles(vacuum_mesh)
        print(f"[crew] {body}: triangles {report}, top {top:.3f}")
        fingers = capture_fingers(rig, options["source"])
        measured = Body(body, rig, [meshes["Wrecker"], meshes["Porter"]])
        crew_rig = CrewRig(rig, measured, hands, fingers)
        scene = bpy.context.scene
        scene.render.fps = FPS
        actions = []
        for name, seconds, function in CLIPS:
            if options["clips"] and name not in options["clips"]:
                continue
            action, frames = crew_rig.bake(name, seconds, function)
            actions.append((name, action, frames))
        crew_rig.clear()
        scene.frame_set(0)
        # Her review poses would go out as extra clips.
        keep = {action for _, action, _ in actions}
        for action in list(bpy.data.actions):
            if action not in keep:
                bpy.data.actions.remove(action)
        if "--check" in sys.argv:
            folder = Path(sys.argv[sys.argv.index("--check") + 1])
            folder.mkdir(parents=True, exist_ok=True)
            bpy.ops.wm.save_as_mainfile(filepath=str(folder / f"crew_{body}.blend"))
        if options["export"] and not options["clips"]:
            export(body)
        if options["sheet"]:
            render_sheet(rig, crew_rig, actions, options["sheet"], body)


if __name__ == "__main__":
    main()

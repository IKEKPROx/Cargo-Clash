"""Checks the crew's clips for arms inside the body and for bent wrists.

Bake a body into a .blend first, then open that file with this script:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_crew.py -- --only Short --no-export --check /tmp/crew
    /Applications/Blender.app/Contents/MacOS/Blender --background /tmp/crew/crew_Short.blend --python Tools/blender/check_crew.py

Add `-- Smash,Slam` to check only those clips. For every clip and set of clothes it prints how deep the arms
and gloves go into the torso at worst (metres in her units, before the game's 0.84 scale), in which frame,
and how many vertices are in then; and how far each wrist bends at worst (the angle between the forearm
and hand bones; about 10 to 14 degrees is their rest pose, and past 60 starts to look broken).

The inside test uses the real posed meshes: a vertex counts as inside when the nearest torso face faces
away from it and most of 14 rays from it hit the torso from the inside, so the openings at the neck and
sleeves don't fool it.
"""
import math
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

TORSO = {"Hips", "Spine", "Chest"}
ARM = {"LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftUpperArm", "RightUpperArm"}
FRAME_STEP = 3

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
only = set(args[0].split(",")) if args else None
scene = bpy.context.scene
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
upper_length = rig.data.bones["LeftUpperArm"].length
gloves = bpy.data.objects["Gloves"]

DIRECTIONS = []
for i in range(14):
    z = 1 - 2 * (i + 0.5) / 14
    r = math.sqrt(1 - z * z)
    a = i * math.pi * (3 - math.sqrt(5))
    DIRECTIONS.append(Vector((r * math.cos(a), r * math.sin(a), z)))


def owners(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    result = []
    for v in obj.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        result.append(names[best.group] if best is not None else None)
    return result


def inside(tree, point):
    hit = tree.find_nearest(point, 0.15)
    if hit[0] is None:
        return 0.0
    location, normal, _, distance = hit
    if (point - location).dot(normal) >= -0.004:
        return 0.0
    votes = 0
    for direction in DIRECTIONS:
        ray = tree.ray_cast(point, direction, 1.5)
        if ray[0] is not None and ray[1].dot(direction) > 0:
            votes += 1
    return distance if votes >= 9 else 0.0


def arms_inside(outfit, owner, torso_faces, arm_vertices):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = outfit.evaluated_get(depsgraph).to_mesh()
    vertices = [outfit.matrix_world @ v.co for v in mesh.vertices]
    tree = BVHTree.FromPolygons(vertices, [tuple(mesh.polygons[i].vertices) for i in torso_faces])
    shoulders = {side: rig.matrix_world @ rig.pose.bones[f"{side}UpperArm"].head for side in ("Left", "Right")}
    points = []
    for i in arm_vertices:
        name = owner[i]
        side = "Left" if name.startswith("Left") else "Right"
        # The top of the upper arm always meets the shoulder.
        if name.endswith("UpperArm") and (vertices[i] - shoulders[side]).length < 0.55 * upper_length:
            continue
        points.append(vertices[i])
    glove_mesh = gloves.evaluated_get(depsgraph).to_mesh()
    points += [gloves.matrix_world @ v.co for v in glove_mesh.vertices]
    depth, count = 0.0, 0
    for point in points:
        d = inside(tree, point)
        if d > 0.0:
            count += 1
            depth = max(depth, d)
    outfit.evaluated_get(depsgraph).to_mesh_clear()
    gloves.evaluated_get(depsgraph).to_mesh_clear()
    return depth, count


def wrist_bends():
    bends = []
    for side in ("Left", "Right"):
        forearm = rig.pose.bones[f"{side}LowerArm"].matrix.to_3x3().col[1]
        hand = rig.pose.bones[f"{side}Hand"].matrix.to_3x3().col[1]
        bends.append(math.degrees(forearm.angle(hand)))
    return bends


def main():
    actions = [a for a in bpy.data.actions if only is None or a.name in only]
    for outfit_name in ("Porter", "Wrecker"):
        outfit = bpy.data.objects[outfit_name]
        owner = owners(outfit)
        torso_faces = [p.index for p in outfit.data.polygons if all(owner[i] in TORSO for i in p.vertices)]
        arm_vertices = [i for i, name in enumerate(owner) if name in ARM]
        for action in actions:
            rig.animation_data.action = action
            start, end = (int(x) for x in action.frame_range)
            worst = (0.0, start, 0)
            bent = [(0.0, start), (0.0, start)]
            for frame in range(start, end + 1):
                scene.frame_set(frame)
                for i, bend in enumerate(wrist_bends()):
                    if bend > bent[i][0]:
                        bent[i] = (bend, frame)
                if (frame - start) % FRAME_STEP:
                    continue
                depth, count = arms_inside(outfit, owner, torso_faces, arm_vertices)
                if depth > worst[0]:
                    worst = (depth, frame, count)
            print(f"CLIP {outfit_name:7s} {action.name:12s} inside={worst[0]:.3f} at frame {worst[1]} ({worst[2]} vertices)"
                  f"  wrists L={bent[0][0]:.0f} at {bent[0][1]}, R={bent[1][0]:.0f} at {bent[1][1]}", flush=True)


main()

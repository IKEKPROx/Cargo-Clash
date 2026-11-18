"""Menu-only acting on the existing crew rigs. Does not rebake or overwrite gameplay clips."""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).parent))
import make_crew as crew

PLACES = {"Medium": (0.55, 1.7800743, -2.15), "Short": (-0.30, 1.889099, -2.20),
          "Tall": (-0.80, 1.9084, 2.73)}
WHEEL = Vector((0.55, 2.64, -2.55))


def beat(t, start, rise, fall, end):
    def smooth(v):
        v = max(0.0, min(1.0, v))
        return v * v * (3.0 - 2.0 * v)
    return smooth((t - start) / (rise - start)) * (1.0 - smooth((t - fall) / (end - fall)))


def wheel_grip(solver, pose, side, angle):
    b = solver.b
    angle = math.radians(angle)
    tilt = math.radians(63)
    radial = Vector((math.cos(angle), -math.sin(angle) * math.cos(tilt), math.sin(angle) * math.sin(tilt)))
    tangent = Vector((-math.sin(angle), -math.cos(angle) * math.cos(tilt), math.cos(angle) * math.sin(tilt)))
    normal = Vector((0, math.sin(tilt), math.cos(tilt)))
    offset = WHEEL - Vector(PLACES[b.name])
    point = (Vector((offset.x, offset.z, offset.y)) + radial * 0.265) / crew.GAME_SCALE
    canon = crew.canonical(side, -normal, radial)
    shoulder = b.chest + Vector(pose.hips_loc) + crew.deg_quat(pose.chest) @ (b.shoulder[side] - b.chest)
    transform, unturn, wrist_rest = solver.hand[side]
    hint = Vector(pose.elbow_l if side == "L" else pose.elbow_r)
    for _ in range(4):
        wrist = point - canon @ unturn @ (transform @ crew.GRIP_CENTER - wrist_rest)
        joint, end, _ = crew.solve(shoulder, wrist, b.upper, b.lower, hint)
        canon = crew.roll_on_handle(canon, (end - joint).normalized(), tangent)
    wrist = point - canon @ unturn @ (transform @ crew.GRIP_CENTER - wrist_rest)
    return wrist, (canon.col[1].xyz, canon.col[2].xyz), point


def acting(solver, u):
    b = solver.b
    t = u * 20.0
    wave = math.sin(u * math.tau * 10)
    grab = beat(t, 8, 10.4, 12.8, 15.8)
    lift = beat(t, 8, 8.8, 14.8, 15.8)
    wrestle = grab * math.sin((t - 10.4) * math.pi * 2)
    lower = b.thigh * 0.56
    hip = b.hips.z - lower
    if b.name == "Tall":
        nod = beat(t, 3.5, 4.7, 5.1, 7.2)
        return crew.pose(b, hips_loc=(0, 0, -lower), hips=(-12, 0, 0), chest=(-6, 0, 0),
                         head=(9 + nod * 13, -8, -7), hands_in_body=1,
                         hand_l=(0.21, -0.32, hip - 0.03), hand_r=(-0.21, -0.30, hip - 0.03),
                         palm_l=((0, -1, -0.1), (0, -0.1, 1)), palm_r=((0, -1, -0.1), (0, -0.1, 1)),
                         foot_l=(b.hip["L"].x + 0.07, -0.70, 0.32),
                         foot_r=(b.hip["R"].x - 0.05, -0.64, 0.32))
    feet = {"foot_l": (b.hip["L"].x, -0.57, 0.25), "foot_r": (b.hip["R"].x, -0.59, 0.25)}
    if b.name == "Medium":
        pose = crew.pose(b, hips_loc=(0, 0, -lower), chest=(4, wave * 0.4, wrestle * 1.5),
                         head=(2, -grab * 3, -grab * 19), hands_in_body=1,
                         elbow_l=(0.8, 0.2, -0.5), elbow_r=(-0.5, 0.2, -0.8),
                         fingers_l={"Grip": 1}, fingers_r={"Grip": 1}, **feet)
        pose.hand_l, pose.palm_l, _ = wheel_grip(solver, pose, "L", 25 + wrestle * 12)
        pose.hand_r, pose.palm_r, _ = wheel_grip(solver, pose, "R", 205 + wrestle * 12)
        return pose
    pose = crew.pose(b, hips_loc=(grab * 0.15, -grab * 0.045, -lower), chest=(3 + grab * 4, grab * 7, -grab * 8),
                     head=(-3, grab * 3, 9 + grab * 19), hands_in_body=1,
                     hand_l=(0.25, -0.32, hip + 0.06), hand_r=(-0.28, -0.30, hip + 0.05),
                     palm_l=((0, -1, 0), (0, 0, 1)), palm_r=((0, -1, 0), (0, 0, 1)),
                     elbow_l=(0.4 - lift * 0.9, -0.3, -0.8 + lift * 1.8), elbow_r=(-0.8, 0.1, -0.3),
                     fingers_l={"Relaxed": 1 - grab, "Grip": grab}, **feet)
    wrist, palm, _ = wheel_grip(solver, pose, "L", 150 + wrestle * 12)
    rest = crew.canonical("L", *pose.palm_l)
    hold = crew.canonical("L", *palm)
    # Mirrored left-hand frames become proper rotations relative to the same bind frame.
    turn = (rest @ solver.hand["L"][1]).to_quaternion().slerp((hold @ solver.hand["L"][1]).to_quaternion(), grab)
    blended = turn.to_matrix() @ solver.hand["L"][1].inverted()
    pose.hand_l = Vector(pose.hand_l).lerp(wrist, grab) + Vector((0, -0.06, 0.22)) * math.sin(grab * math.pi)
    pose.palm_l = (blended.col[1].xyz, blended.col[2].xyz)
    return pose


def main():
    source = crew.ROOT.parent / "独立建模"
    data = []
    for name in ("Medium", "Short", "Tall"):
        rig, hands, meshes, _, top = crew.load_body(source, name)
        body = crew.Body(name, rig, [meshes["Porter"]])
        solver = crew.CrewRig(rig, body, hands, crew.capture_fingers(rig, source))
        solver.continuous_hand_twist = True
        solver.continuous_twists = {}
        action, frames = solver.bake("MenuLoop", 20, lambda b, u: acting(solver, u))
        contact_errors = []
        for frame in range(0, frames + 1, 3):
            bpy.context.scene.frame_set(frame)
            t = frame / crew.FPS
            grab = beat(t, 8, 10.4, 12.8, 15.8)
            contacts = [("L", "Left", 25), ("R", "Right", 205)] if name == "Medium" else [("L", "Left", 150)] if name == "Short" and grab > 0.999 else []
            for side, prefix, angle in contacts:
                _, _, target = wheel_grip(solver, acting(solver, frame / frames), side,
                                           angle + grab * math.sin((t - 10.4) * math.tau) * 12)
                actual = rig.pose.bones[prefix + "Hand"].matrix @ rig.data.bones[prefix + "Hand"].matrix_local.inverted() @ (solver.hand[side][0] @ crew.GRIP_CENTER)
                contact_errors.append((actual - target).length * crew.GAME_SCALE)
        bpy.context.scene.render.fps = crew.FPS
        bpy.context.scene.frame_start = 0
        bpy.context.scene.frame_end = frames
        for old in list(bpy.data.actions):
            if old != action:
                bpy.data.actions.remove(old)
        for obj in list(bpy.context.scene.objects):
            if obj != rig:
                bpy.data.objects.remove(obj, do_unlink=True)
        solver.clear()
        rig.animation_data.action = action
        bpy.context.scene.frame_set(0)
        bpy.ops.export_scene.fbx(
            filepath=str(crew.ROOT / f"Assets/Game/Models/Menu_{name}.fbx"),
            object_types={"ARMATURE"}, add_leaf_bones=False, use_armature_deform_only=False,
            bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True, bake_anim_simplify_factor=0,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        )
        data.append({"body": name, "seatedHip": (body.hips.z - body.thigh * 0.56) * crew.GAME_SCALE,
                     "top": top * crew.GAME_SCALE, "unreachable": len(solver.misses),
                     "maxWheelContactError": max(contact_errors, default=0)})
        print("[MenuLoop]", data[-1], flush=True)
    (crew.ROOT / "Assets/Game/Models/MenuPoses.json").write_text(json.dumps({"bodies": data}, indent=2))


if __name__ == "__main__":
    main()

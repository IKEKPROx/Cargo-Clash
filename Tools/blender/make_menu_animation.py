"""The start screen's loop, behind the main menu: twenty seconds that end where they began.

The truck cruises down the road. In the back the tall one naps on a long case and bounces when the truck hits a bump.
In the cab the driver (medium) nods off and the truck drifts; the passenger (short) lunges across and grabs the wheel,
the driver wakes and they wrestle for it while the truck swerves; the driver wins and glares, and the passenger
sulks back in his seat and goes back to his music. It hints at the crash every level starts with.

Run from the repository:
  Blender --background --factory-startup --python Tools/blender/make_menu_animation.py [-- --report] [-- --only Short]
--report prints the worst wrist bend of each body, --only bakes just those bodies, and --timeline only rewrites the
JSON (the camera, the wheel, the faces), which is quick.

Writes Assets/Game/Models/Menu_<Body>.fbx (the skeleton and one clip, MenuLoop, which MenuDeliveryLoop samples on the
crew page's bodies) and Assets/Game/Models/MenuLoop.json: everything else on the same clock, frame by frame (the
steering wheel's turn, the truck's swerve and bump, the faces, and the camera). The game's own clips aren't touched.

Places are in the truck body's space in Unity (x toward the truck's left, y up, z toward the back), the same as
make_menu_truck.py, which builds the seats and the wheel the crew sit in and hold. A body's own space (its rig) is
x to its left, -y in front of it, z up, in her units, which the game scales by crew.GAME_SCALE.
"""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).parent))
import make_crew as crew
import make_menu_truck as truck

SECONDS = 20.0
FPS = crew.FPS
FRAMES = int(round(SECONDS * FPS))

# Each body's rig origin in the truck, and which way it faces: 180 turns it to the front of the truck (the cab
# crew), 0 leaves it facing the back (the napper, leaning on the stack behind the cab).
ACTORS = {
    "Medium": (Vector((truck.DRIVER_X, 1.78, -2.15)), 180),
    "Short": (Vector((truck.PASSENGER_X, 1.889, -2.20)), 180),
    "Tall": (Vector((-0.80, 1.749, 1.02)), 0),
}

# The wheel faces up and back at the driver. Clock angles are as the driver sees it: 0 is 9 o'clock (his left,
# the truck's left, +x), 90 is 12 o'clock, 180 is 3 o'clock. Turning it clockwise (steering right) adds to them.
TILT = math.radians(63)
AXIS = Vector((0.0, math.cos(TILT), math.sin(TILT)))
UP = Vector((0.0, math.sin(TILT), -math.cos(TILT)))
LEFT = Vector((1.0, 0.0, 0.0))


# ---- Time ----

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def mix(a, b, u):
    if isinstance(a, (tuple, list, Vector)):
        return tuple(x + (y - x) * u for x, y in zip(a, b))
    return a + (b - a) * u


def keys(t, points):
    """A value eased from key to key: [(seconds, value), ...], held before the first and after the last."""
    if t <= points[0][0]:
        return points[0][1]
    for (t0, v0), (t1, v1) in zip(points, points[1:]):
        if t <= t1:
            return mix(v0, v1, smooth((t - t0) / max(t1 - t0, 1e-4)))
    return points[-1][1]


def wobble(t):
    """Small corrections on a straight road; every period divides the loop, so it comes round to the start."""
    return 1.6 * math.sin(math.tau * t / 5.0) + 0.7 * math.sin(math.tau * t / 2.5 + 1.0)


def wheel_turn(t):
    """Degrees, clockwise as the driver sees it (to the right)."""
    calm = wobble(t)
    drama = keys(t, [(8.6, 0.0), (10.2, 11.0), (10.42, -15.0), (10.95, 16.0), (11.45, -18.0), (11.95, 13.0),
                     (12.35, -9.0), (12.75, 6.0), (13.6, 0.0)])
    quiet = 1.0 - keys(t, [(8.2, 0.0), (8.8, 1.0), (13.2, 1.0), (14.2, 0.0)])
    return calm * quiet + drama


def bump(t, at):
    """The bed's jolt when a wheel drops into a pothole at `at`: a quick rise that dies away."""
    x = t - at
    return 0.0 if x < 0.0 else math.exp(-x * 5.5) * math.sin(x * math.pi * 4.2)


BUMP = 5.0


# ---- The steering wheel and the hands on it ----

def rim(clock):
    a = math.radians(clock)
    radial = LEFT * math.cos(a) + UP * math.sin(a)
    tangent = -LEFT * math.sin(a) + UP * math.cos(a)
    return Vector(truck.WHEEL) + radial * truck.WHEEL_RADIUS, radial, tangent


def rig_vector(name, v):
    flip = 1.0 if ACTORS[name][1] == 180 else -1.0
    return Vector((flip * v.x, flip * v.z, v.y))


def to_rig(name, point):
    return rig_vector(name, Vector(point) - ACTORS[name][0]) / crew.GAME_SCALE


def chain(solver, pose):
    """The torso bones' places and turns in a pose, as CrewRig.apply works them out."""
    rest = solver.rest
    hips_turn = crew.deg_quat(pose.hips)
    spine_turn = hips_turn @ crew.deg_quat(pose.spine)
    chest_turn = spine_turn @ crew.deg_quat(pose.chest)
    hips = rest["Hips"] + Vector(pose.hips_loc)
    spine = hips + hips_turn @ (rest["Spine"] - rest["Hips"])
    chest = spine + spine_turn @ (rest["Chest"] - rest["Spine"])
    return {"Hips": (hips, hips_turn), "Spine": (spine, spine_turn), "Chest": (chest, chest_turn)}


def shoulder_of(solver, pose, side):
    prefix = "Left" if side == "L" else "Right"
    rest = solver.rest
    chest, turn = chain(solver, pose)["Chest"]
    return chest + turn @ (rest[f"{prefix}UpperArm"] - rest["Chest"])


def on_body(solver, pose, bone, point):
    """A point that rides on a torso bone, given where it is with the body at rest, in this pose."""
    place, turn = chain(solver, pose)[bone]
    return place + turn @ (Vector(point) - solver.rest[bone])


# Choosing a grip: the elbow can swing round the line from the shoulder to the hand, and the rim can cross the palm at a
# slant (a fist holds a bar up to about 25 degrees off square). Both are searched for the straightest wrist, near the
# pose's own elbow and close to the last frame's choice so the arm doesn't flicker. Each try goes through the same
# steps as CrewRig.apply (the elbow kept out of the body), so the arm baked is the arm chosen.
SWIVELS = (-60, -45, -30, -15, 0, 15, 30, 45, 60)
SLANTS = (-24, -12, 0, 12, 24)
FINE = (-6, -3, 0, 3, 6)
choices = {}


def body_space(solver, pose):
    """CrewRig's way of taking a point near the torso back to the torso at rest, for this pose."""
    places = chain(solver, pose)
    return solver.rest_space({bone: turn for bone, (_, turn) in places.items()},
                             {bone: place for bone, (place, _) in places.items()})


def arm_with(solver, shoulder, point, tangent, frame, hint, unturn, transform, wrist_rest, rest_of):
    """The hand closed round the rim at `point`, rolled on it to follow the forearm: its wrist, frame and how far the
    wrist bends (forearm against the line of the fingers, degrees)."""
    b = solver.b
    for attempt in range(4):
        wrist = point - frame @ unturn @ (transform @ crew.GRIP_CENTER - wrist_rest)
        joint, end, _ = crew.solve(shoulder, wrist, b.upper, b.lower, hint)
        if attempt == 3:
            break
        frame = crew.roll_on_handle(frame, (end - joint).normalized(), tangent)
    joint, _ = solver.place_elbow(shoulder, joint, end, rest_of)
    bend = math.degrees((end - joint).normalized().angle(frame.col[1].xyz.normalized()))
    return wrist, frame, bend + 60.0 * (end - wrist).length


def rim_hand(solver, pose, side, clock):
    """The wrist, the hand's frame and the elbow's direction that close a hand round the rim at this clock angle: the
    rim runs through the fist (crew.GRIP_CENTER), as on the hammer's handle, with the straightest wrist there is."""
    name = solver.b.name
    point, radial, tangent = rim(clock)
    point, radial, tangent = to_rig(name, point), rig_vector(name, radial), rig_vector(name, tangent)
    square = crew.canonical(side, -rig_vector(name, AXIS), radial)
    shoulder = shoulder_of(solver, pose, side)
    rest_of = body_space(solver, pose)
    transform, unturn, wrist_rest = solver.hand[side]
    base = Vector(pose.elbow_l if side == "L" else pose.elbow_r)
    line = (point - shoulder).normalized()
    last = choices.get((name, side))

    def cost(swivel, slant):
        hint = Quaternion(line, math.radians(swivel)) @ base
        frame = Matrix.Rotation(math.radians(slant), 3, square.col[2].xyz) @ square
        wrist, frame, bend = arm_with(solver, shoulder, point, tangent, frame, hint, unturn, transform, wrist_rest, rest_of)
        value = bend + 0.08 * abs(swivel) + 0.15 * abs(slant)
        if last is not None:
            value += 0.3 * (abs(swivel - last[0]) + abs(slant - last[1]))
        return value, swivel, slant, wrist, frame, hint

    best = min((cost(swivel, slant) for swivel in SWIVELS for slant in SLANTS), key=lambda c: c[0])
    best = min((cost(best[1] + ds * 2.5, max(-26.0, min(26.0, best[2] + dt))) for ds in FINE for dt in FINE), key=lambda c: c[0])
    choices[(name, side)] = (best[1], best[2])
    _, _, _, wrist, frame, hint = best
    return wrist, frame.col[1].xyz.normalized(), frame.col[2].xyz.normalized(), hint


def free_hand(solver, pose, side, wrist, fingers, back, follow):
    """A hand at a place, turned partly to carry on from its forearm (`follow` of the way), so a hand laid on a thigh
    keeps its palm down without the wrist bending back like a hinge. The arm is worked out as CrewRig.apply will."""
    b = solver.b
    hint = Vector(pose.elbow_l if side == "L" else pose.elbow_r)
    if follow <= 0.0:
        return Vector(wrist), Vector(fingers).normalized(), Vector(back).normalized(), hint
    shoulder = shoulder_of(solver, pose, side)
    rest_of = body_space(solver, pose)
    # Resting on a thigh or the belly, an arm may lie as close to the body as her own stance does (Pose.relaxed).
    allow = solver.rest_overlap * pose.relaxed
    wrist = solver.clear_hand(shoulder, Vector(wrist), rest_of, allow)
    joint, end, _ = crew.solve(shoulder, wrist, b.upper, b.lower, hint)
    joint, _ = solver.place_elbow(shoulder, joint, end, rest_of, allow=allow)
    forearm = (end - joint).normalized()
    fingers = (Vector(fingers).normalized() * (1.0 - follow) + forearm * follow).normalized()
    back = Vector(back) - fingers * Vector(back).dot(fingers)
    return wrist, fingers, back.normalized(), hint


def frame_of(fingers, back):
    y = Vector(fingers).normalized()
    z = Vector(back) - y * Vector(back).dot(y)
    z.normalize()
    return Matrix((y.cross(z), y, z)).transposed().to_quaternion()


def hand_at(solver, pose, side, target, t):
    """A hand target: ("rim", clock) holds the wheel (turned with it at time t); ("free", wrist, fingers, back[, follow])
    is a place in the body's space and which way the fingers point and the back of the hand faces, turned `follow` of
    the way to carry on from the forearm. Gives the wrist, the fingers, the back of the hand and the elbow's direction."""
    if target[0] == "rim":
        return rim_hand(solver, pose, side, target[1] + wheel_turn(t))
    follow = target[4] if len(target) > 4 else 0.0
    return free_hand(solver, pose, side, target[1], target[2], target[3], follow)


def hand(solver, pose, side, track, t):
    """Where a hand is on its track [(seconds, target, fingers, lift), ...]: eased from target to target, through
    a lift (metres in the body's space, at the middle of the move) so it goes over things rather than through."""
    index = 0
    while index + 1 < len(track) and t > track[index + 1][0]:
        index += 1
    if index + 1 >= len(track) or t <= track[0][0]:
        start = track[index] if t > track[0][0] else track[0]
        wrist, fingers, back, elbow = hand_at(solver, pose, side, start[1], t)
        return wrist, fingers, back, start[2], elbow
    (t0, a, fingers_a, _), (t1, b, fingers_b, lift) = track[index], track[index + 1]
    u = smooth((t - t0) / (t1 - t0))
    wa, fa, ba, ea = hand_at(solver, pose, side, a, t)
    wb, fb, bb, eb = hand_at(solver, pose, side, b, t)
    wrist = wa.lerp(wb, u) + Vector(lift) * math.sin(math.pi * u)
    turn = frame_of(fa, ba).slerp(frame_of(fb, bb), u).to_matrix()
    fingers = {k: fingers_a.get(k, 0.0) * (1 - u) + fingers_b.get(k, 0.0) * u for k in set(fingers_a) | set(fingers_b)}
    return wrist, turn.col[1].xyz, turn.col[2].xyz, fingers, ea.lerp(eb, u)


def place_hands(solver, pose, t, left, right):
    for side, track in (("L", left), ("R", right)):
        wrist, fingers, back, grip, elbow = hand(solver, pose, side, track, t)
        if side == "L":
            pose.hand_l, pose.palm_l, pose.fingers_l, pose.elbow_l = tuple(wrist), (tuple(fingers), tuple(back)), grip, tuple(elbow)
        else:
            pose.hand_r, pose.palm_r, pose.fingers_r, pose.elbow_r = tuple(wrist), (tuple(fingers), tuple(back)), grip, tuple(elbow)
    return pose


GRIP = {"Grip": 1.0}
RELAXED = {"Relaxed": 1.0}
OPEN = {"Open": 1.0}
HALF_OPEN = {"Relaxed": 0.5, "Open": 0.5}


# ---- The driver ----

def seated(b, drop):
    return dict(hands_in_body=1, hips_loc=(0.0, 0.0, -drop))


def driver(solver, t):
    b = solver.b
    drop = b.thigh * 0.56
    floor = (1.8425 - ACTORS["Medium"][0].y) / crew.GAME_SCALE + 0.14
    road = math.sin(math.tau * t / 1.25) * 0.6
    dozing = keys(t, [(8.3, 0.0), (9.3, 1.0), (9.5, 0.55), (9.95, 1.0), (10.32, 1.0), (10.45, 0.0)])
    startled = keys(t, [(10.3, 0.0), (10.45, 1.0), (10.9, 0.3), (12.4, 0.3), (12.9, 0.0)])
    tug = keys(t, [(10.4, 0.0), (10.6, 1.0), (12.5, 1.0), (12.9, 0.0)])
    glare = keys(t, [(12.6, 0.0), (13.0, 1.0), (14.3, 1.0), (14.9, 0.0)])
    lean = wheel_turn(t) * 0.35 * tug
    pose = crew.pose(
        b, **seated(b, drop), relaxed=1.0,
        hips=(0.0, 0.0, 0.0),
        spine=(2.0 + dozing * 4.0, 0.0, 0.0),
        chest=(3.0 + dozing * 7.0 - startled * 6.0 + road * 0.3, -lean * 0.6, -glare * 6.0),
        head=(dozing * 30.0 - startled * 12.0 + road, dozing * 7.0 - lean * 0.4, -glare * 32.0 - tug * 10.0),
        elbow_l=(1.0, 0.25, -0.65), elbow_r=(-1.0, 0.25, -0.65),
        foot_l=(b.hip["L"].x + 0.02, -0.52, floor), foot_r=(b.hip["R"].x - 0.02, -0.50, floor),
        knee_l=(0.1, -1.0, 0.2), knee_r=(-0.1, -1.0, 0.2),
    )
    hip_z = b.hips.z - drop
    lap = ("free", (b.hip["R"].x - 0.03, -0.14, hip_z + 0.15), (0.05, -1.0, -0.35), (-0.15, -0.15, 1.0), 0.8)
    left = [(0.0, ("rim", 30.0), GRIP, (0, 0, 0))]
    right = [(0.0, ("rim", 150.0), GRIP, (0, 0, 0)),
             (8.75, ("rim", 150.0), GRIP, (0, 0, 0)),
             (9.6, lap, RELAXED, (0.0, -0.06, 0.06)),
             (10.38, lap, RELAXED, (0, 0, 0)),
             (10.62, ("rim", 118.0), GRIP, (0.0, -0.04, 0.10)),
             (15.0, ("rim", 118.0), GRIP, (0, 0, 0)),
             (15.9, ("rim", 150.0), GRIP, (0, 0, 0))]
    return place_hands(solver, pose, t, left, right)


# ---- The passenger ----

ELBOW_TUCKED_L = (0.3, 0.8, -0.5)


def passenger(solver, t):
    b = solver.b
    drop = b.thigh * 0.56
    hip_z = b.hips.z - drop
    music = keys(t, [(0.0, 1.0), (9.55, 1.0), (9.75, 0.0), (16.6, 0.0), (17.8, 1.0)])
    bob = math.sin(math.tau * t * 0.9) * 5.0 * music
    notice = keys(t, [(9.55, 0.0), (9.85, 1.0), (10.05, 1.0), (10.3, 0.0)])
    reach = keys(t, [(9.95, 0.0), (10.35, 1.0), (12.45, 1.0), (12.85, 0.0)])
    pulled = wheel_turn(t) * keys(t, [(10.35, 0.0), (10.5, 1.0), (12.4, 1.0), (12.6, 0.0)])
    flop = keys(t, [(12.55, 0.0), (12.85, 1.0), (13.2, 0.4), (13.6, 0.0)])
    sulk = keys(t, [(12.8, 0.0), (13.3, 1.0), (16.2, 1.0), (17.4, 0.0)])
    # How far the left hand is off its thigh (its track below): the elbow turns out for the wheel and back again.
    away = keys(t, [(9.95, 0.0), (10.18, 1.0), (12.7, 1.0), (13.15, 0.0)])
    window = 1.0 - max(notice, reach)
    # Holding on, he goes where the wheel takes his hand: in when the driver turns it away from him.
    follow = -wheel_turn(t) * keys(t, [(10.36, 0.0), (10.5, 1.0), (12.4, 1.0), (12.6, 0.0)])
    pose = crew.pose(
        b, hands_in_body=1, relaxed=1.0,
        hips_loc=(0.19 * reach + 0.007 * follow, -0.05 * reach, -drop + 0.06 * reach - 0.03 * flop),
        hips=(0.0, 3.0 * reach, 14.0 * reach),
        spine=(8.0 * reach - 4.0 * flop, 10.0 * reach + 0.25 * follow, 6.0 * reach),
        chest=(12.0 * reach + 2.0 * sulk, 18.0 * reach + 0.35 * follow - 3.0 * flop, 8.0 * reach),
        head=(bob + 4.0 * reach - 10.0 * sulk + 6.0 * flop, 6.0 * window * music - 6.0 * sulk - 8.0 * reach,
              -38.0 * window * (1.0 - sulk) - 52.0 * sulk + 30.0 * notice + 18.0 * reach),
        # With the hands on his thighs the elbows go back along his sides; pointing out, the right upper arm stood
        # out sideways like a wing. The left turns out to the wheel's side for the lunge, as it was.
        elbow_l=tuple(Vector(ELBOW_TUCKED_L).lerp(Vector((0.6, 0.2, -1.0)), away)),
        elbow_r=(-ELBOW_TUCKED_L[0], ELBOW_TUCKED_L[1], ELBOW_TUCKED_L[2]),
        foot_l=(b.hip["L"].x + 0.03, -0.33, 0.28), foot_r=(b.hip["R"].x - 0.03, -0.33, 0.28),
        knee_l=(0.15, -1.0, 0.1), knee_r=(-0.15, -1.0, 0.1),
    )
    thigh_l = ("free", (b.hip["L"].x + 0.05, -0.19, hip_z + 0.12), (0.05, -1.0, -0.35), (0.15, -0.1, 1.0), 0.6)
    thigh_r = ("free", (b.hip["R"].x - 0.05, -0.19, hip_z + 0.12), (-0.05, -1.0, -0.35), (-0.15, -0.1, 1.0), 0.6)
    # On the way: the hand opens as it comes up past the dashboard, fingers leading.
    up = ("free", (0.55, -0.45, hip_z + 0.52), (0.75, -0.65, 0.1), (0.1, 0.1, 1.0), 0.7)
    left = [(0.0, thigh_l, RELAXED, (0, 0, 0)),
            (9.95, thigh_l, RELAXED, (0, 0, 0)),
            (10.18, up, OPEN, (0.0, 0.0, 0.08)),
            (10.36, ("rim", 176.0), GRIP, (0.0, 0.0, 0.04)),
            (12.5, ("rim", 176.0), GRIP, (0, 0, 0)),
            (12.7, up, HALF_OPEN, (0.0, 0.0, 0.05)),
            (13.15, thigh_l, RELAXED, (0.0, 0.0, 0.04))]
    # The other hand stays on its thigh, which goes with him when he lunges.
    lunge = Vector(pose.hips_loc) + Vector((0.0, 0.0, drop))
    braced = ("free", tuple(Vector(thigh_r[1]) + lunge), thigh_r[2], thigh_r[3], 0.8)
    right = [(0.0, braced, RELAXED, (0, 0, 0))]
    return place_hands(solver, pose, t, left, right)


# ---- The napper ----

def napper(solver, t):
    b = solver.b
    drop = b.thigh * 0.6
    jolt = bump(t - 0.08, BUMP)
    sway = keys(t, [(10.3, 0.0), (10.6, 1.0), (12.8, 1.0), (13.4, 0.0)]) * wheel_turn(t - 0.25)
    breath = math.sin(math.tau * t / 4.0)
    scratch = keys(t, [(5.6, 0.0), (5.9, 1.0), (6.9, 1.0), (7.3, 0.0)])
    pose = crew.pose(
        b, hands_in_body=1, relaxed=1.0,
        hips_loc=(0.006 * sway, 0.0, -drop + 0.12 * max(jolt, 0.0)),
        hips=(-38.0, -0.25 * sway, 0.0),
        spine=(-4.0 + breath, 0.0, 0.0),
        chest=(-4.0 + breath * 1.5 - 4.0 * jolt, -0.3 * sway, 0.0),
        head=(24.0 + 6.0 * jolt, 10.0 + 0.4 * sway, 14.0),
        elbow_l=(1.0, 0.3, -0.4), elbow_r=(-1.0, 0.3, -0.4),
        knee_l=(0.0, -0.4, 1.0), knee_r=(0.0, -0.4, 1.0),
    )
    case_top = (2.26 - ACTORS["Tall"][0].y) / crew.GAME_SCALE
    hips_now = Vector(pose.hips_loc) + b.hips
    pose.foot_l = (0.05, hips_now.y - 0.80, case_top + 0.10 + 0.05 * max(jolt, 0.0))
    pose.foot_r = (-0.05, hips_now.y - 0.74, case_top + 0.19 + 0.08 * max(jolt, 0.0))
    # Hands one on the other on the belly, riding on the spine as it breathes; one scratches after the bump.
    belly = b.front(b.hips.z + 0.05, b.chest.z - 0.05)
    rest_l = on_body(solver, pose, "Spine", (0.03, belly - 0.05, b.hips.z + 0.17))
    rest_r = on_body(solver, pose, "Spine", (-0.07, belly - 0.07, b.hips.z + 0.26))
    turn = chain(solver, pose)["Spine"][1]
    left = [(0.0, ("free", tuple(rest_l), tuple(turn @ Vector((-1.0, -0.15, -0.2))), tuple(turn @ Vector((0.0, -1.0, 0.2)))),
             RELAXED, (0, 0, 0))]
    rub = Vector((0.04 * math.sin(math.tau * t * 2.2), 0.0, 0.03 * math.cos(math.tau * t * 2.2))) * scratch
    right = [(0.0, ("free", tuple(rest_r + turn @ rub), tuple(turn @ Vector((1.0, -0.15, -0.25))), tuple(turn @ Vector((0.0, -1.0, 0.25)))),
              RELAXED, (0, 0, 0))]
    return place_hands(solver, pose, t, left, right)


ACTING = {"Medium": driver, "Short": passenger, "Tall": napper}


# ---- Everything else on the same clock, for MenuLoop.json ----

def faces(name, t):
    """Her face sliders (-100 to 100) for each moment."""
    if name == "Tall":
        return {"EyeSquint": 100.0, "Smile": 35.0 - 25.0 * max(bump(t - 0.05, BUMP), 0.0), "BrowHeight": -20.0}
    if name == "Medium":
        dozing = keys(t, [(8.3, 0.0), (9.3, 1.0), (9.5, 0.6), (9.95, 1.0), (10.32, 1.0), (10.42, 0.0)])
        startled = keys(t, [(10.3, 0.0), (10.42, 1.0), (11.0, 0.0)])
        effort = keys(t, [(10.6, 0.0), (10.9, 1.0), (12.5, 1.0), (12.9, 0.0)])
        glare = keys(t, [(12.7, 0.0), (13.0, 1.0), (14.4, 1.0), (15.0, 0.0)])
        return {"EyeSquint": 100.0 * dozing - 100.0 * startled + 45.0 * effort + 55.0 * glare,
                "EyeSize": 100.0 * startled,
                "BrowHeight": 100.0 * startled - 80.0 * glare - 30.0 * effort,
                "Smile": 20.0 * (1.0 - max(effort, glare, startled)) - 70.0 * effort - 50.0 * glare}
    notice = keys(t, [(9.55, 0.0), (9.8, 1.0), (10.3, 1.0), (10.6, 0.0)])
    effort = keys(t, [(10.4, 0.0), (10.6, 1.0), (12.45, 1.0), (12.7, 0.0)])
    sulk = keys(t, [(12.8, 0.0), (13.3, 1.0), (16.3, 1.0), (17.4, 0.0)])
    happy = 1.0 - max(notice, effort, sulk)
    return {"EyeSquint": 35.0 * happy + 40.0 * effort + 25.0 * sulk - 60.0 * notice,
            "EyeSize": 70.0 * notice,
            "BrowHeight": 80.0 * notice - 50.0 * effort - 40.0 * sulk,
            "Smile": 60.0 * happy - 60.0 * effort - 90.0 * sulk}


def truck_motion():
    """The swerve follows the wheel a little later (yaw, and the drift sideways it causes), the body leans out of it,
    and the bump lifts and pitches the body. Integrated over two loops so the second starts where it ends."""
    heading, side = 0.0, 0.0
    frames = []
    dt = 1.0 / FPS
    for step in range(FRAMES * 2):
        t = (step % FRAMES) / FPS
        steer = wheel_turn(t - 0.2) - wobble(t - 0.2)
        rate = 0.55 * steer - 1.8 * heading
        heading += rate * dt
        side += (-math.radians(heading) * 5.5 - 0.9 * side) * dt
        if step >= FRAMES:
            frames.append((heading, side, -rate * 0.6, 0.11 * bump(t, BUMP) + 0.07 * bump(t, BUMP + 0.24),
                           2.6 * (bump(t, BUMP) - bump(t, BUMP + 0.24))))
    return frames


def camera_path():
    """The camera, frame by frame: a place and a point it looks at, both in the truck's space, and its field of view.
    `body` is how much it rides with the truck body (its bump and swerve) rather than just its travel down the road.
    Keys: (seconds, place, look at, field of view, body). Between them the camera eases through, holding still on the
    keys that come in pairs (a shot), so it drifts during a shot and glides from one to the next."""
    wide_a = ((-7.6, 4.6, -10.6), (-0.3, 2.35, -0.6), 33.0, 0.0)
    wide_b = ((-7.1, 4.8, -9.6), (-0.3, 2.45, -0.4), 33.0, 0.0)
    nap_a = ((-4.0, 5.3, 4.3), (-0.9, 2.75, 1.2), 32.0, 0.0)
    nap_b = ((-3.7, 5.1, 3.7), (-0.9, 2.8, 1.15), 32.0, 0.0)
    corner = ((-4.8, 3.9, -6.2), (0.0, 2.95, -2.6), 28.0, 0.4)
    # From the driver's side of the bonnet, so the passenger lunging in shows in the driver's window.
    cab_a = ((0.55, 3.45, -7.8), (0.05, 3.0, -2.6), 26.0, 1.0)
    cab_b = ((0.45, 3.40, -7.0), (0.05, 3.0, -2.6), 24.0, 1.0)
    leave = ((-5.6, 4.2, -9.4), (-0.3, 2.5, -1.2), 31.0, 0.3)
    # (seconds, key, holds still): a shot's two keys ease in and out; the keys between shots are glided through.
    # The first and last keys are the loop's own ends seen from the other side, so the wide shot spans the seam.
    rise = ((-7.4, 6.3, -2.2), (-0.6, 2.6, 0.2), 33.0, 0.0)
    path = [(-2.4, wide_a, True), (3.0, wide_b, True), (3.7, rise, False), (4.4, nap_a, True), (6.3, nap_b, True), (7.15, corner, False),
            (7.95, cab_a, True), (14.5, cab_b, True), (16.0, leave, False), (17.6, wide_a, True), (23.0, wide_b, True)]

    def value(key, i):
        return Vector(key[i]) if i < 2 else Vector((key[i], 0.0, 0.0))

    def sample(t, i):
        for k in range(len(path) - 1):
            t0, a, still_a = path[k]
            t1, b, still_b = path[k + 1]
            if t0 <= t <= t1:
                p0, p1 = value(a, i), value(b, i)
                before = value(path[k - 1][1], i) if k > 0 else p0
                after = value(path[k + 2][1], i) if k + 2 < len(path) else p1
                span = t1 - t0
                m0 = Vector() if still_a else (p1 - before) * (span / (t1 - path[k - 1][0]))
                m1 = Vector() if still_b else (after - p0) * (span / (path[k + 2][0] - t0))
                s = (t - t0) / span
                h00, h10, h01, h11 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s, -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
                return p0 * h00 + m0 * h10 + p1 * h01 + m1 * h11
        raise ValueError(t)

    frames = []
    for step in range(FRAMES):
        t = step / FPS
        frames.append((sample(t, 0), sample(t, 1), sample(t, 2).x, sample(t, 3).x))
    return frames


def write_timeline(path):
    motion = truck_motion()
    camera = camera_path()
    data = {
        "seconds": SECONDS, "fps": FPS,
        "wheel": [round(wheel_turn(i / FPS), 3) for i in range(FRAMES)],
        "yaw": [round(m[0], 3) for m in motion], "side": [round(m[1], 4) for m in motion],
        "roll": [round(m[2], 3) for m in motion], "heave": [round(m[3], 4) for m in motion],
        "pitch": [round(m[4], 3) for m in motion],
        "cameraPosition": [round(c, 3) for frame in camera for c in frame[0]],
        "cameraFocus": [round(c, 3) for frame in camera for c in frame[1]],
        "cameraField": [round(frame[2], 2) for frame in camera],
        "cameraOnBody": [round(min(max(frame[3], 0.0), 1.0), 3) for frame in camera],
        "faces": [],
    }
    for name in ACTORS:
        values = [faces(name, i / FPS) for i in range(FRAMES)]
        for shape in sorted(values[0]):
            data["faces"].append({"body": name, "shape": shape, "weights": [round(v[shape], 1) for v in values]})
    path.write_text(json.dumps(data, separators=(",", ":")))


# ---- Baking ----

def wrist_bend(rig, prefix):
    """How far the hand is bent away from the line of the forearm, in degrees, compared with the rest pose. The hand
    turning round its own length (the forearm's twist) doesn't count."""
    forearm, hand_bone = rig.pose.bones[f"{prefix}LowerArm"], rig.pose.bones[f"{prefix}Hand"]
    rest_forearm, rest_hand = rig.data.bones[f"{prefix}LowerArm"], rig.data.bones[f"{prefix}Hand"]
    posed = forearm.matrix.to_quaternion().inverted() @ hand_bone.matrix.to_quaternion()
    rest = rest_forearm.matrix_local.to_quaternion().inverted() @ rest_hand.matrix_local.to_quaternion()
    change = rest.inverted() @ posed
    return math.degrees((change @ Vector((0.0, 1.0, 0.0))).angle(Vector((0.0, 1.0, 0.0))))


def wrist_report(rig, name, frames):
    """The worst wrist bend in each half second of the clip, per hand: over 35 degrees starts to look broken."""
    lines = {}
    for frame in range(0, frames + 1, 3):
        bpy.context.scene.frame_set(frame)
        span = int(frame / FPS * 2) / 2
        for prefix in ("Left", "Right"):
            bend = wrist_bend(rig, prefix)
            key = (prefix, span)
            lines[key] = max(lines.get(key, 0.0), bend)
    for prefix in ("Left", "Right"):
        bad = [f"{span:.1f}s:{bend:.0f}" for (side, span), bend in sorted(lines.items()) if side == prefix and bend > 35.0]
        worst = max(bend for (side, _), bend in lines.items() if side == prefix)
        print(f"[MenuLoop] {name} {prefix} wrist worst {worst:.0f} deg; over 35 at: {' '.join(bad) or 'none'}", flush=True)


def main():
    if "--timeline" in sys.argv:
        write_timeline(crew.ROOT / "Assets/Game/Models/MenuLoop.json")
        return
    report = "--report" in sys.argv
    only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else list(ACTORS)
    source = crew.ROOT.parent / "独立建模"
    models = crew.ROOT / "Assets/Game/Models"
    for name in only:
        rig, hands, meshes, _, _ = crew.load_body(source, name)
        # The menu's crew wear her current porter clothes, so the arms keep clear of those: the v05 short porter's belly
        # was much bigger, and sitting with his hands on his thighs held his elbows out like wings.
        look = crew.load_look(source, name, rig)
        body = crew.Body(name, rig, [look["Porter"] if look else meshes["Porter"]])
        solver = crew.CrewRig(rig, body, hands, crew.capture_fingers(rig, source))
        solver.continuous_hand_twist = True
        solver.continuous_twists = {}
        acting = ACTING[name]
        action, frames = solver.bake("MenuLoop", SECONDS, lambda b, u: acting(solver, u * SECONDS))
        if report:
            wrist_report(rig, name, frames)
        if solver.misses:
            print(f"[MenuLoop] {name}: a hand falls short in {len(solver.misses)} frames, worst {max(solver.misses, key=lambda m: m[2])}", flush=True)
        bpy.context.scene.render.fps = FPS
        bpy.context.scene.frame_start = 0
        bpy.context.scene.frame_end = frames
        for old in list(bpy.data.actions):
            if old != action:
                bpy.data.actions.remove(old)
        if "--check" in sys.argv:
            # Her current meshes on the rig in place of the v05/v06 ones, so check_crew.py sees what the menu shows.
            folder = Path(sys.argv[sys.argv.index("--check") + 1])
            folder.mkdir(parents=True, exist_ok=True)
            if look:
                crew.show_look(look, meshes)
            rig.animation_data.action = action
            bpy.ops.wm.save_as_mainfile(filepath=str(folder / f"menu_{name}.blend"))
        for obj in list(bpy.context.scene.objects):
            if obj != rig:
                bpy.data.objects.remove(obj, do_unlink=True)
        solver.clear()
        rig.animation_data.action = action
        bpy.context.scene.frame_set(0)
        bpy.ops.export_scene.fbx(
            filepath=str(models / f"Menu_{name}.fbx"),
            object_types={"ARMATURE"}, add_leaf_bones=False, use_armature_deform_only=False,
            bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True, bake_anim_simplify_factor=0,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        )
        print(f"[MenuLoop] {name} baked, {frames} frames", flush=True)
    write_timeline(models / "MenuLoop.json")
    print("[MenuLoop] wrote MenuLoop.json", flush=True)


if __name__ == "__main__":
    main()

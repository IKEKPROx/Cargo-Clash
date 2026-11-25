"""Astra's interactive cargo (独立建模/互动货物_v08) in the crew's clips. make_crew.py calls this.

She made four kinds of cargo and, on the same 52-bone crew, how a person handles each: InspectArt (the
porcelain goose held up in both hands), InspectGold (a gold bar hefted in both palms), MakeItRain (a bundle in
the left hand, notes flung with the right) and Smoke. Her clips start with the thing already in the hands, and
her CargoOpenArt/CargoCloseArt open the goose crate standing on the ground. The functions marked "hers" below
are copied from her 脚本/build_actions.py and interaction_hands.py, so the poses are hers, solved by the same
solver she used (her copy of this repository's make_crew.py, plus the forearm-twist fix that is now in it).

What the game adds around her clips, so they can be played from carrying:

  - Tobacco, Cash and Gold are carried in front like a tray, palms up under the case (her ready pose, which is
    where her interactions start). The goose crate is too tall to carry in front without hiding the face, and
    the arms are too short to reach its side handles, so it is the two-person item: each carrier holds one side
    handle in both hands.
  - Inspect<Kind> is one clip per kind: the case drops from the hands to the ground in front and its lid springs
    open, the thing jumps up into the hands, her clip plays, it jumps back, the lid shuts and the case comes up
    into the hands again. For the crate it is her opening, her InspectArt and her closing, at the crate on the
    ground.
  - The props ride on four extra bones under the chest: Prop_Case (the case's origin, at the middle of its
    bottom, the front toward -Y), Prop_Lid (its hinge, under Prop_Case), Prop_Item (what's in the hands) and
    Prop_Notes (the notes pinched in MakeItRain). The things in the hands are meshes on Prop_Item and Prop_Notes;
    a bone scaled to nothing hides them. The game puts the real cargo on Marker_Case and turns its lid with
    Marker_Lid, which sit on those bones exactly as the case's and the lid's own origins sit in her models.

Writes Assets/Game/Models/CargoInspect.json: when each clip's things happen, for the game's effects.
"""
import json
import math
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

crew = None  # the running make_crew module, set by bind()

CARGO_FOLDER = "互动货物_v08"
FPS = 30
KINDS = ("Tobacco", "Cash", "Gold", "Art")
# Her build_cargo.py CONFIG: width, depth, height, metres, front -Y.
SIZE = {"Art": (0.76, 0.56, 0.75), "Gold": (0.68, 0.46, 0.28), "Cash": (0.68, 0.46, 0.24), "Tobacco": (0.52, 0.36, 0.19)}
# Her side handles: a bar this far out from each side wall, at this share of the height, 18 cm of it clear.
HANDLE_OUT = 0.095
HANDLE_AT = 0.46
# The one the hands take out, as she placed it in the case: name, position, turn round Z, scale. The case faces
# the person holding it, so the near row is her -Y row, and the person's left hand is on her -X side.
SLOT = {
    "Art": ("Content_Goose", (0.0, 0.0, 0.083), 0.0, (0.9, 0.9, 0.9)),
    "Gold": ("Content_Gold_01", (0.0, -0.095, 0.093), math.pi / 2, (0.85, 0.65, 0.85)),
    "Cash": ("Cash_Bundle_01", (-0.0755, -0.095, 0.091), 0.0, (0.8, 0.95, 1.0)),
    "Tobacco": ("Cigarette_06", (0.058, -0.072, 0.102), 0.0, (1.0, 1.0, 1.0)),
}
# The thing in the hands, from her Props folder, at the size her clip shows it (the lit cigarette is bigger).
PROP = {
    "Art": ("Art_Inspect", (0.9, 0.9, 0.9)),
    "Gold": ("Gold_Inspect", (0.85, 0.65, 0.85)),
    "Cash": ("Cash_Bundle", (0.8, 0.95, 1.0)),
    "Tobacco": ("Cigarette_Burnt", (1.3, 1.3, 1.3)),
}
# Her interaction for each kind and how long it is.
INTERACTION = {"Art": ("InspectArt", 3.0), "Gold": ("InspectGold", 2.6), "Cash": ("MakeItRain", 2.4), "Tobacco": ("Smoke", 4.2)}
PROP_BONES = (("Prop_Case", "Chest"), ("Prop_Lid", "Prop_Case"), ("Prop_Item", "Chest"), ("Prop_Notes", "Chest"))
HIDDEN = 0.001
LID_OPEN = math.radians(-110.0)

# Carrying from below, the way her interactions start: how far the case's near side stays off the clothes,
# and how far under it the middle of each palm goes.
TRAY_GAP = 0.09
TRAY_UNDER = 0.15
TRAY_UNDER_SHORT = 0.1
# Holding a case between the palms: the hands this far below the chest (the short body: this high), the case's
# near side this far off the clothes, the palm centres this far into the side walls from that near side, and
# pressing this far into them (the glove's padding).
HUG_HANDS = -0.16
HUG_HANDS_SHORT = 0.95
HUG_GAP = 0.095
HUG_ALONG = 0.012
HUG_PRESS = 0.014
HUG_FINGERS = {"Open": 0.85, "Relaxed": 0.15}
# Which way the elbows go from the line between shoulder and hand (the body's space: out, back, down). With the
# plain hint the tall body's long arms winged their elbows out at shoulder height (upper arms 74 degrees off
# hanging), and the tops of its shoulders, weighted partly to the chest, sank in where they met the raised arms.
# Out, back and down they come to about 50 degrees, which still spans the 68 cm case; the medium body's, which
# sank in less at 65 degrees, come to 43. The short body holds the case with its arms almost straight, so a hint
# makes no difference there.
HUG_ELBOW = (1.0, 0.2, -0.6)
HUG_ELBOW_BY_BODY = {"Tall": (0.7, 0.4, -1.0), "Medium": (0.7, 0.4, -1.0)}
# Which hold each body uses for the cases.
# The tobacco box's handles take up most of its short sides, so it's carried on the palms.
HOLD = {"Tobacco": "tray"}
# The short body holds the case low, in front of its belly and below its chin.
TRAY_GAP_SHORT = 0.05
TRAY_SHORT_LIFT = 0.06
# The palms this far out from the middle, so the forearms pass under the case clear of its corner guards.
TRAY_ACROSS = 0.13
# The case sits this far above the middle of the palms (the glove's thickness and the fingers' curl).
TRAY_RAISE = 0.026
TRAY_FINGERS = {"Carry": 0.45, "Open": 0.55}
# Where a dropped case lands: its near side this far past the toes.
TOE_GAP = 0.07

# The flourish around her clip (seconds). The case falls, the lid springs open, the thing jumps into the hands;
# at the end it jumps back, the lid shuts and the case comes back up.
# Before her clip: the hands slide back off the case, it falls (forward first, then down) and lands with its lid
# springing open, and the thing comes up out of it into the hands, which drop to meet it.
RELEASE = 4 / FPS
FALL = (2 / FPS, 8 / FPS)
LID_SPRING = 6 / FPS
RISE = (10 / FPS, 18 / FPS)
# After it: the thing goes back, the lid shuts, the case jumps up (up first, then in) and the hands take it.
BACK = 8 / FPS
SHUT = (5 / FPS, 9 / FPS)
LIFT = (8 / FPS, 13 / FPS)
SETTLE = 16 / FPS
CATCH = (10 / FPS, 15 / FPS)
CATCH_REST = 5 / FPS
# Sliding off the case: back toward you, a little out and down. A case held between the palms is let go by
# opening them outward instead.
RELEASE_MOVE = (0.05, 0.13, -0.06)
OPEN_MOVE = (0.04, 0.075, -0.02)
LET_GO_ELBOW = {"L": (0.45, 0.7, -0.6), "R": (-0.45, 0.7, -0.6)}
# How far toward hanging the hands get between letting go and taking hold (all the way looks like giving up).
REST_DROP = 0.7
# The case is pushed off the palms this far before it drops, so it can't fall through the fingers.
PUSH = 0.24
# Coming back up, it stops this far above the palms and settles onto them as they arrive. At 3 cm the medium
# body's belly touched the case for a frame; much lower and the fingers meet the handles' roots on the way in.
HOVER = 0.027

# Her contact points and finger poses (interaction_hands.capture). Set by capture_fingers.
PALM = (0, 0.125, -0.037)
PINCH = {"CashPinch": 1.0}
SMOKE = {"SmokeHold": 1.0}
CASH_HOLD = {"CashHold": 1.0}
UP = ((0, -1, 0), (0, 0, -1))
SMOKE_LOCAL = None
CASH_LOCAL = None


def bind(module):
    global crew
    crew = module


def lerp(a, b, t):
    return a + (b - a) * t


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def ease_in(t):
    t = max(0.0, min(1.0, t))
    return t * t


# ---- Her solver, on the game's ----

def rig_class():
    """Hers: a hand holding something keeps its contact, and the elbow is chosen for the straightest wrist and
    so it doesn't jump from one frame to the next."""

    class InteractionRig(crew.CrewRig):
        def apply(self, pose, frame_number, key=True):
            self.keep_contact = bool(getattr(pose, "lock_contacts", False))
            self.contact_sides = getattr(pose, "contact_sides", ())
            self.interaction_pose = pose if getattr(pose, "optimize_wrists", False) else None
            self.continuous_hand_twist = self.interaction_pose is not None
            if frame_number == 0:
                self.continuous_twists = {}
                self.previous_elbows = {}
            result = super().apply(pose, frame_number, key)
            if self.interaction_pose:
                bpy.context.view_layer.update()
                self.previous_elbows = {s: self.arm.pose.bones[prefix + "LowerArm"].head.copy() for s, prefix, _ in crew.SIDES}
            return result

        def clear_hand(self, shoulder, wrist, rest_of, allow=0.0):
            if getattr(self, "keep_contact", False) or ("L" if shoulder.x > 0 else "R") in getattr(self, "contact_sides", ()):
                return wrist
            return super().clear_hand(shoulder, wrist, rest_of, allow)

        def place_elbow(self, shoulder, elbow, wrist, rest_of, wrist_bend=None, allow=0.0):
            pose = getattr(self, "interaction_pose", None)
            if pose and wrist_bend is None:
                palm = pose.palm_l if shoulder.x > 0 else pose.palm_r
                if palm:
                    fingers = Vector(palm[0]).normalized()
                    wrist_bend = lambda forearm: math.degrees(forearm.angle(fingers))
            if not pose or wrist_bend is None:
                return super().place_elbow(shoulder, elbow, wrist, rest_of, wrist_bend, allow)
            axis = wrist - shoulder
            if axis.length < 1e-5:
                return super().place_elbow(shoulder, elbow, wrist, rest_of, wrist_bend, allow)
            axis.normalize()
            center = shoulder + axis * (elbow - shoulder).dot(axis)
            spoke = elbow - center
            if spoke.length < 1e-5:
                return super().place_elbow(shoulder, elbow, wrist, rest_of, wrist_bend, allow)
            old = self.previous_elbows.get("L" if shoulder.x > 0 else "R")
            previous = None if old is None else old - center - axis * (old - center).dot(axis)
            if previous is not None and previous.length < 1e-5:
                previous = None
            best = None
            for step in range(-72, 73):
                angle = step * 2.5
                offset = Quaternion(axis, math.radians(angle)) @ spoke
                candidate = center + offset
                cost = self.arm_inside(shoulder, candidate, wrist, rest_of, allow) * 400 + abs(angle) * 0.015
                cost += max(0, wrist_bend((wrist - candidate).normalized()) - 30) * 0.2
                # An elbow up near the shoulder, out to the side, reads as a flapping wing: the long arms took
                # it for the straighter wrist whenever the hand came in close (smoking, pinching notes).
                cost += max(0.0, candidate.z - (shoulder.z - ELBOW_LOW)) * ELBOW_UP_COST
                if previous is not None:
                    change = math.degrees(offset.angle(previous))
                    cost += change * 0.06 + max(0, change - 10) * 0.5
                if best is None or cost < best[0]:
                    best = (cost, candidate, offset.normalized())
            return best[1], best[2]

        def bake_cargo(self, name, clip):
            """Like bake, then the props on their bones, frame by frame, once the arms are in place."""
            action = bpy.data.actions.new(name)
            action.use_fake_user = True
            if self.arm.animation_data is None:
                self.arm.animation_data_create()
            self.arm.animation_data.action = action
            self.previous = {}
            self.misses = []
            # Looking into a case, the hands work close in front of the face with the palms set by the clip, and a
            # raised collarbone (crew.CLAVICLE_SHARE) bent the tall body's wrist to 70 degrees fanning the cash, so
            # the inspections keep their collarbones still. The carries keep the lift: that's where the shoulder
            # showed sinking in.
            self.still_collarbones = not isinstance(clip, CarryClip)
            frames = max(int(round(clip.seconds * FPS)), 1)
            for number in range(frames + 1):
                u = number / frames
                self.apply(clip.pose(self.b, u), number)
                bpy.context.view_layer.update()
                key_props(self.arm, clip.props(self.b, u, self.arm), number)
            self.still_collarbones = False
            if self.misses:
                worst = max(self.misses, key=lambda m: m[2])
                print(f"[crew] {self.b.name} {name}: a hand falls short in {len(self.misses)} frames, worst {worst}")
            return action, frames

    return InteractionRig


def capture_fingers(rig, source):
    """Hers: her hand poses plus three made for the cargo, and the points between the fingers where the
    cigarette and the pinched notes sit."""
    global SMOKE_LOCAL, CASH_LOCAL
    core = {}
    path = source / crew.HAND_POSES_FILE
    exec(compile(path.read_text(), str(path), "exec"), core)
    original = core["posed_chains"]
    poses = {
        "SmokeHold": original(curls={"Index": 0.08, "Middle": 0.12, "Ring": 0.48, "Little": 0.54, "Thumb": 0.16}),
        "CashHold": original(curls={"Index": 0.24, "Middle": 0.26, "Ring": 0.29, "Little": 0.32, "Thumb": 0.16}),
        "CashPinch": original("Pinch"),
    }
    # Fingertip centres must be separated by the padded glove thickness.
    target = poses["CashPinch"]["Index"][-1] + Vector((-0.003, 0, 0.028))
    poses["CashPinch"]["Thumb"] = core["solve_chain"](poses["CashPinch"]["Thumb"], target)
    core["posed_chains"] = lambda pose="Relaxed", amount=1, curls=None: poses[pose] if pose in poses else original(pose, amount, curls)
    fingers = {}
    for name in (*crew.FINGER_POSES, "Pinch", *poses):
        core["reset_pose"](rig)
        for _, prefix, _ in crew.SIDES:
            core["apply_pose"](rig, prefix, name)
        fingers[name] = {p.name: p.rotation_quaternion.copy() for p in rig.pose.bones if crew.is_finger(p.name)}
    core["reset_pose"](rig)
    SMOKE_LOCAL = (poses["SmokeHold"]["Index"][-2] + poses["SmokeHold"]["Middle"][-2]) * 0.5
    CASH_LOCAL = (poses["CashPinch"]["Index"][-1] + poses["CashPinch"]["Thumb"][-1]) * 0.5
    return fingers


def normalized_frame(matrix):
    return Matrix([matrix.to_3x3().col[i].normalized() for i in range(3)]).transposed()


# ---- Her interactions (build_actions.py) ----

def front(b, z):
    return min(b.front(z - 0.10, z + 0.10) - 0.07, -0.27)


def wrist_for(b, side, point, palm, local=PALM):
    """Hers: the wrist that puts this point of the glove on a prop's contact."""
    prefix = "Left" if side == "L" else "Right"
    transform = b.hand_bind[prefix]
    turn = transform.to_3x3()
    turn = Matrix([turn.col[i].normalized() for i in range(3)]).transposed()
    worldturn = crew.canonical(side, Vector(palm[0]), Vector(palm[1])) @ turn.inverted()
    wrist = b.wrist[side]
    return tuple(Vector(point) - worldturn @ (transform @ Vector(local) - wrist))


# How far the head bends forward looking at the goose (degrees). Hers bent it 8 degrees, and the medium body's
# nose came within 3 cm of the goose's beak, which looked like touching; lifted a little it's 8 cm.
ART_HEAD_PITCH = -4


def object_frame(b, clip, u):
    t = u * INTERACTION_SECONDS[clip]
    z = b.chest.z - (0.16 if clip == "InspectArt" else 0.06)
    if clip == "InspectArt" and b.name == "Medium":
        z = b.chest.z - 0.11
    if b.name == "Short":
        z = b.chest.z + (0.04 if clip == "InspectArt" else 0.08)
    y = min(b.front(z - 0.05, z + 0.1) - (0.21 if clip == "InspectArt" else 0.23), -0.40)
    if clip == "InspectArt" and b.name != "Short":
        y -= 0.14 if b.name == "Medium" else 0.45
    lift = 0.045 * math.sin(math.pi * u) ** 2
    angle = 0.23 * math.sin(u * math.tau) * math.sin(math.pi * u)
    if clip == "InspectGold":
        lift = 0.035 * math.sin(u * math.pi * 3) ** 2
        angle += math.pi / 2
    else:
        angle += math.pi
    return Vector((0, y, z + lift)), Quaternion((0, 0, 1), angle)


INTERACTION_SECONDS = {name: seconds for name, seconds in INTERACTION.values()}


def ready(b):
    z = b.chest.z - 0.12
    y = front(b, z)
    return crew.pose(b, hands_in_body=1, hand_l=(0.23, y, z), hand_r=(-0.23, y, z), palm_l=UP, palm_r=UP,
                     fingers_l=crew.CARRY, fingers_r=crew.CARRY, chest=(1, 0, 0), head=(8, 0, 0))


def hanging(b, side, drop=0.0):
    """A hand that has nothing to do, in the body's space (for poses with hands_in_body): hanging beside the
    thigh where the idle pose has it, fingers along the arm and a little forward, the back of the hand outward,
    so the palm faces the leg. `drop` is how far the shoulders have come down (the hips lowered). Without a palm
    given, a hand carries on from the forearm, which in these poses turned its palm forward or up."""
    sign = 1.0 if side == "L" else -1.0
    arm = Vector(b.hang(side))
    wrist = b.shoulder[side] + arm - Vector((0.0, 0.0, drop))
    # The fingers carry on along the arm: straight down for the thin bodies, out round the belly for the
    # short one, whose arms hang wide (fingers straight down bent its wrists 90 degrees).
    fingers = (arm.normalized() + Vector((0.0, -0.25, 0.0))).normalized()
    return tuple(wrist), (tuple(fingers), (sign, -0.3, 0.0))


# In her interactions the elbow may come up to this far below the shoulder before it costs (metres), and then
# this much per metre (her wrist cost is 0.2 per degree past 30).
ELBOW_LOW = 0.12
ELBOW_UP_COST = 80.0
# The elbows of hands hanging or on their way down: back and down, close to the sides, not out like wings.
HANG_ELBOW = {"L": (0.25, 0.9, -0.4), "R": (-0.25, 0.9, -0.4)}


def inspect_pose(b, clip, u):
    p = ready(b)
    pos, rot = object_frame(b, clip, u)
    support = rot @ Quaternion((0, 0, 1), -math.pi / 2 if clip == "InspectGold" else -math.pi)
    bottom = 0.0225 * 0.90 if clip == "InspectArt" else 0
    spacing = 0.085 if clip == "InspectArt" else 0.052
    if clip == "InspectArt" and b.name == "Medium":
        spacing = 0.13
    contacts = {"L": (spacing, 0.005, bottom), "R": (-spacing, 0.005, bottom)}
    palms = {s: (tuple(support @ Vector((-0.8 if s == "L" else 0.8, -0.6, 0))), (0, 0, -1)) for s in ("L", "R")}
    head_yaw = math.degrees(support.to_euler("XYZ").z) / 3
    result = p.copy(hand_l=wrist_for(b, "L", pos + support @ Vector(contacts["L"]), palms["L"]),
                    hand_r=wrist_for(b, "R", pos + support @ Vector(contacts["R"]), palms["R"]),
                    palm_l=palms["L"], palm_r=palms["R"], fingers_l=crew.CARRY, fingers_r=crew.CARRY,
                    head=(ART_HEAD_PITCH if clip == "InspectArt" else 8, 0, -head_yaw), chest=(0, 0, 0))
    result.lock_contacts = True
    return result


def keyed_hands(keys, seconds, u):
    """Hers: slerp the hands' turn, so opposite palms don't pass through zero."""
    time = u * seconds
    for (start, left), (end, right) in zip(keys, keys[1:]):
        if time <= end:
            amount = crew.smooth((time - start) / max(end - start, 0.0001))
            return blend(left, right, amount, smoothed=True)
    return keys[-1][1].copy()


def desired_hand_frame(b, side, wrist, palm):
    prefix = "Left" if side == "L" else "Right"
    bind = b.hand_bind[prefix]
    turn = crew.canonical(side, *palm) @ normalized_frame(bind).inverted()
    result = turn.to_4x4() @ bind
    result.translation = Vector(wrist) + turn @ (bind.translation - b.wrist[side])
    return result


def bundle_frame(hand):
    turn = normalized_frame(hand)
    result = Matrix((turn.col[0], turn.col[1], -turn.col[2])).transposed().to_4x4()
    result.translation = hand @ Vector((0, 0.135, -0.037))
    return result


def rain(b, u):
    p = ready(b)
    x, y, z = p.hand_l
    if b.name == "Short":
        z = b.chest.z + 0.07
    base = p.copy(hand_l=(0.10 if b.name == "Short" else 0.15, y - 0.05, z + 0.025), hand_r=(-0.22, y - 0.06, z + 0.15),
                  palm_l=((-0.25, -0.94, 0.23), (0, 0, -1)), palm_r=((0.35, -0.9, 0.25), (0, 0.25, 1)),
                  fingers_l=CASH_HOLD, fingers_r=crew.RELAXED, chest=(0, 0, 0), head=(10, 0, 0), elbow_r=(-1, -0.6, -0.4))
    bank = bundle_frame(desired_hand_frame(b, "L", base.hand_l, base.palm_l))
    pick_palm = ((0.82, -0.55, 0.15), (0, 0, 1))
    pick_point = bank @ Vector((0.052, -0.020, 0.047))
    take = base.copy(hand_r=wrist_for(b, "R", pick_point, pick_palm, CASH_LOCAL), palm_r=pick_palm, fingers_r=PINCH)
    hover = take.copy(hand_r=tuple(Vector(take.hand_r) + Vector((0, 0, 0.06 if b.name == "Short" else 0.08))))
    lifted = base.copy(hand_r=(-0.17, y - 0.10, z + 0.27), palm_r=((0.3, -0.78, 0.55), (0, 0.55, 0.8)), fingers_r=PINCH, head=(-5, 0, -6))
    released = lifted.copy(hand_r=(-0.22, y - 0.12, z + 0.32), fingers_r=crew.OPEN, head=(-7, 0, -10))
    settle = base.copy(hand_r=(-0.29, y - 0.06, z + 0.12), fingers_r=crew.RELAXED)
    result = keyed_hands([(0, base), (0.35, hover), (0.60, take), (0.96, lifted), (32 / 30, lifted), (36 / 30, released),
                          (1.90, settle), (2.4, base)], 2.4, u)
    result.contact_sides = ("L", "R")
    result.optimize_wrists = True
    return result


def smoke_pose(b, u):
    p = ready(b)
    y = p.hand_r[1]
    z = p.hand_r[2]
    rest_l, palm_l = hanging(b, "L")
    low = p.copy(hand_l=rest_l, palm_l=palm_l, elbow_l=HANG_ELBOW["L"], hand_r=(-0.27, y - 0.04, z + 0.08),
                 palm_r=((0.15, -0.94, 0.30), (0, 0.30, 0.94)), fingers_l=crew.RELAXED, fingers_r=SMOKE, head=(0, 0, 0),
                 chest=(0, 0, 0), elbow_r=(-1, -0.7, -0.5))
    pal = ((0.85, 0, 0.52), (0, -1, 0))
    point = b.mouth + Vector(pal[1]) * (0.035 * 1.3)
    mouth = low.copy(hand_r=wrist_for(b, "R", point, pal, SMOKE_LOCAL), palm_r=pal, head=(0, 0, 0), chest=(0, 0, 0))
    inhale = mouth.copy()
    away = low.copy(hand_r=(-0.27, y - 0.08, z + 0.22), palm_r=((0.55, -0.68, 0.48), (-0.25, -0.65, -0.72)), head=(-3, 0, 6))
    flick = away.copy(hand_r=(-0.30, y - 0.12, z + 0.16), fingers_r=crew.OPEN, head=(3, 0, -6))
    # After the flick the arm drops. Hers went back to the low hold, the elbow out like a wing (the long arms
    # most), on its way to taking the case again.
    rest_r, palm_r = hanging(b, "R")
    end = low.copy(hand_r=rest_r, palm_r=palm_r, fingers_r=crew.RELAXED, elbow_r=HANG_ELBOW["R"])
    result = keyed_hands([(0, low), (0.40, low), (1.25, mouth), (1.65, inhale), (2.05, mouth), (2.55, away), (3.50, away),
                          (3.74, flick), (4.2, end)], 4.2, u)
    # Once the butt is flicked away the hand holds nothing, and is kept out of the body like any empty hand.
    result.contact_sides = ("R",) if u * 4.2 < 3.74 else ()
    result.optimize_wrists = True
    return result


# Her lid touch put the fingertips 2 cm into the lid's front edge; this lifts the hand onto it.
CRATE_TOUCH_LIFT = 0.034
# How far the knees bend at the crate (her hips_loc).
CRATE_CROUCH = 0.13
# When (in the opening's 0..1) the hand lets go of the lid, is off it, and hangs; and how far it comes off it.
LID_LET_GO = (0.25, 0.33, 0.55)
LID_HAND_OFF = (-0.02, 0.12, 0.05)


def crate_origin():
    """Hers: the goose crate stands on the ground this far in front, its front toward the person."""
    return Vector((0, -0.84, 0))


def lid_touch(b, v):
    """Hers: where the right hand is on the lid's front edge at `v` of the opening, and the lid's angle."""
    angle = LID_OPEN * crew.smooth((v - 0.12) / 0.55)
    w, d, h = SIZE["Art"]
    root = crate_origin()
    boxrot = Quaternion((0, 0, 1), math.pi)
    hinge = root + boxrot @ Vector((0, d / 2 - 0.028, h - 0.035))
    rot = boxrot @ Quaternion((1, 0, 0), angle)
    touch = hinge + rot @ Vector((min(0.27, w * 0.40), -(d - 0.028), 0.026 + CRATE_TOUCH_LIFT))
    palm = (tuple(rot @ Vector((0, 1, 0))), tuple(rot @ Vector((0, 0, 1))))
    return wrist_for(b, "R", touch, palm, (0, 0.20, -0.04)), palm, angle


def open_crate(b, u, closing=False):
    """Hers (open_pose, kind Art): the lid's front edge lifted and let go early, so it swings away on its own.
    Closing is the same backwards: the hand comes onto the edge and pushes it down."""
    p = ready(b)
    v = 1 - u if closing else u
    wrist, palm, angle = lid_touch(b, v)
    # Hers had the other hand palm up beside the hip, and the lifting hand palm up out to the side after it let
    # go, waiting for the goose: the forearms turned further than they can, so the hands looked back to front.
    rest_l, palm_l = hanging(b, "L", drop=CRATE_CROUCH)
    rest_r, palm_r = hanging(b, "R", drop=CRATE_CROUCH)
    held = p.copy(hand_l=rest_l, hand_r=wrist, palm_l=palm_l, palm_r=palm, fingers_l=crew.RELAXED, fingers_r=crew.CARRY,
                  elbow_l=HANG_ELBOW["L"], head=(14, 0, 0), chest=(0, 0, 0), hips_loc=(0, 0, -CRATE_CROUCH))
    if v <= LID_LET_GO[0]:
        return held, angle
    # Let go, the hand comes back toward you and up off the edge (the lid swings the other way), then drops.
    # Straight down to hanging, the fingers went through the rising lid.
    let_go_at, let_go_palm, _ = lid_touch(b, LID_LET_GO[0])
    off = held.copy(hand_r=tuple(Vector(let_go_at) + Vector(LID_HAND_OFF)), palm_r=let_go_palm, fingers_r=crew.RELAXED)
    if v <= LID_LET_GO[1]:
        return blend(held, off, (v - LID_LET_GO[0]) / (LID_LET_GO[1] - LID_LET_GO[0])), angle
    end = held.copy(hand_r=rest_r, palm_r=palm_r, fingers_r=crew.RELAXED, elbow_r=HANG_ELBOW["R"])
    return blend(off, end, (v - LID_LET_GO[1]) / (LID_LET_GO[2] - LID_LET_GO[1])), angle


def interaction(b, kind, u):
    if kind == "Cash":
        return rain(b, u)
    if kind == "Tobacco":
        return smoke_pose(b, u)
    return inspect_pose(b, INTERACTION[kind][0], u)


# ---- Blending, with the palms turned rather than averaged ----

def blend(a, b, t, smoothed=False):
    amount = t if smoothed else ease(t)
    result = crew.mix_poses(a, b, amount)
    for name in ("palm_l", "palm_r"):
        p = getattr(a, name)
        q = getattr(b, name)
        if p is not None and q is not None:
            turn = crew.canonical("R", *p).to_quaternion().slerp(crew.canonical("R", *q).to_quaternion(), amount).to_matrix()
            setattr(result, name, (tuple(turn.col[1]), tuple(turn.col[2])))
    return result


# ---- Carrying ----

def tray(b, kind, u=0.0):
    """A case carried in front like a tray, palms up under its near half, close to the clothes. The short
    body holds it above the belly, as she did. Returns the pose and the case's origin in the body's space."""
    w, d, h = SIZE[kind]
    if b.name == "Short":
        palm_z = b.chest.z + TRAY_SHORT_LIFT
    else:
        palm_z = b.chest.z - 0.12
    bottom = palm_z + TRAY_RAISE
    near = b.front(bottom - 0.04, bottom + h + 0.06) - (TRAY_GAP_SHORT if b.name == "Short" else TRAY_GAP)
    a = math.tau * u
    bob = 0.008 * math.sin(a)
    centre = Vector((0.0, near - d / 2, bottom + bob))
    across = min(TRAY_ACROSS, w / 2 - 0.12)
    # The short body's belly keeps its elbows up: its wrists stay at the case's near side, so the forearms
    # don't run under the case.
    under = TRAY_UNDER_SHORT if b.name == "Short" else TRAY_UNDER
    palm = ((0.18, -1.0, 0.0), (0.0, 0.0, -1.0))
    palm_l = ((-0.18, -1.0, 0.0), (0.0, 0.0, -1.0))
    under_l = (across, near - under, palm_z + bob)
    under_r = (-across, near - under, palm_z + bob)
    pose = crew.pose(
        b, hands_in_body=1.0,
        hand_l=wrist_for(b, "L", under_l, palm_l), hand_r=wrist_for(b, "R", under_r, palm),
        palm_l=palm_l, palm_r=palm, elbow_l=(1.0, 0.3, -0.6), elbow_r=(-1.0, 0.3, -0.6),
        spine=(-3.0, 1.0 * math.sin(a), 0), chest=(-2.0, 1.0 * math.sin(a + 0.6), 0), head=(6.0, -1.5 * math.sin(a + 1.0), 0),
        fingers_l=TRAY_FINGERS, fingers_r=TRAY_FINGERS,
    )
    pose.lock_contacts = True
    return pose, centre


def front_across(b, low, high, half):
    """How far forward the clothes reach between two heights, across the whole width of a case (a negative Y)."""
    return min((p.y for p in b.points if low <= p.z <= high and abs(p.x) < half), default=-0.15)


def hug(b, kind, u=0.0):
    """A case held in front between the palms, flat on its side walls and through its side handles (her 8 cm
    of room behind the bar), fingers forward: the way the game's first boxes were carried. Returns the pose and
    the case's origin in the body's space."""
    w, d, h = SIZE[kind]
    grip = HANDLE_AT * h
    hands = HUG_HANDS_SHORT if b.name == "Short" else b.chest.z + HUG_HANDS
    bottom = hands - grip
    gap = TRAY_GAP_SHORT if b.name == "Short" else HUG_GAP
    near = front_across(b, bottom - 0.04, bottom + h + 0.1, w / 2) - gap
    a = math.tau * u
    bob = 0.008 * math.sin(a)
    centre = Vector((0.0, near - d / 2, bottom + bob))
    palm_l = ((0.0, -1.0, -0.12), (1.0, 0.0, 0.0))
    palm_r = ((0.0, -1.0, -0.12), (-1.0, 0.0, 0.0))
    side_l = (w / 2 + HUG_PRESS, near - HUG_ALONG, hands + bob)
    side_r = (-(w / 2 + HUG_PRESS), near - HUG_ALONG, hands + bob)
    out, back, down = HUG_ELBOW_BY_BODY.get(b.name, HUG_ELBOW)
    pose = crew.pose(
        b, hands_in_body=1.0,
        hand_l=wrist_for(b, "L", side_l, palm_l), hand_r=wrist_for(b, "R", side_r, palm_r),
        palm_l=palm_l, palm_r=palm_r, elbow_l=(out, back, down), elbow_r=(-out, back, down),
        spine=(0.0, 1.0 * math.sin(a), 0), chest=(1.0, 1.0 * math.sin(a + 0.6), 0), head=(5.0, -1.5 * math.sin(a + 1.0), 0),
        fingers_l=HUG_FINGERS, fingers_r=HUG_FINGERS,
    )
    pose.lock_contacts = True
    return pose, centre


def hold(b, kind, u=0.0):
    style = HOLD.get((b.name, kind), HOLD.get(kind, "hug"))
    return hug(b, kind, u) if style == "hug" else tray(b, kind, u)


def case_matrix(origin, lift=0.0, tip=0.0):
    """The case's origin in the body's space: turned so its front faces the person."""
    turn = Quaternion((0, 0, 1), math.pi) @ Quaternion((1, 0, 0), tip)
    return Matrix.Translation(origin + Vector((0, 0, lift))) @ turn.to_matrix().to_4x4()


class CarryClip:
    """Holding a case like a tray, a little bob on every beat."""

    def __init__(self, kind):
        self.kind = kind
        self.seconds = 1.0

    def pose(self, b, u):
        return hold(b, self.kind, u)[0]

    def props(self, b, u, rig):
        _, centre = hold(b, self.kind, u)
        return {"case": case_matrix(centre), "lid": 0.0, "item": None, "notes": None, "kind": self.kind}


# ---- The props on their bones ----

def hand_frame(rig, side):
    pb = rig.pose.bones[side + "Hand"]
    bind = Matrix([list(row) for row in rig["hand_bind_transforms"][side]])
    return pb.matrix @ pb.bone.matrix_local.inverted() @ bind


def bundle_now(rig):
    return bundle_frame(hand_frame(rig, "Left"))


def cigarette_now(rig, previous=[None]):
    a = rig.pose.bones["RightIndexDistal"].head
    b = rig.pose.bones["RightMiddleDistal"].head
    pinch = (a + b) * 0.5
    hf = normalized_frame(hand_frame(rig, "Right"))
    q = Matrix((hf.col[1], hf.col[2], hf.col[0])).transposed().to_quaternion()
    if previous[0] is not None and previous[0].dot(q) < 0:
        q.negate()
    previous[0] = q.copy()
    return Matrix.Translation(pinch) @ q.to_matrix().to_4x4()


def notes_now(rig):
    hf = normalized_frame(hand_frame(rig, "Right"))
    pinch = (rig.pose.bones["RightIndexDistal"].tail + rig.pose.bones["RightThumbDistal"].tail) * 0.5
    return Matrix.Translation(pinch) @ hf.to_4x4()


def slot_matrix(kind, case):
    name, position, turn, _ = SLOT[kind]
    return case @ Matrix.Translation(Vector(position)) @ Matrix.Rotation(turn, 4, "Z")


def arc(start, end, t, height):
    """A jump between two placements: the position along a curve that rises `height` above the straight line."""
    t = max(0.0, min(1.0, t))
    s = ease(t)
    position = start.translation.lerp(end.translation, s) + Vector((0, 0, height * math.sin(math.pi * t)))
    turn = start.to_quaternion().slerp(end.to_quaternion(), s)
    return Matrix.Translation(position) @ turn.to_matrix().to_4x4()


def key_props(rig, props, number):
    """Puts the four prop bones where `props` says (in the armature's space) and keys them."""
    bones = rig.pose.bones
    case = props.get("case")
    lid = props.get("lid", 0.0)
    kind = props.get("kind")
    item = props.get("item")
    notes = props.get("notes")
    item_scale = props.get("item_scale", 1.0)
    if case is not None:
        bones["Prop_Case"].matrix = case
        bpy.context.view_layer.update()
        hinge = Vector((0, SIZE[kind][1] / 2 - (0.028 if kind == "Art" else 0.021), SIZE[kind][2] - 0.035))
        bones["Prop_Lid"].matrix = case @ Matrix.Translation(hinge) @ Matrix.Rotation(lid, 4, "X")
    if item is not None:
        bones["Prop_Item"].matrix = item @ Matrix.Diagonal((item_scale, item_scale, item_scale, 1.0))
    else:
        bones["Prop_Item"].matrix = bones["Chest"].matrix @ Matrix.Diagonal((HIDDEN, HIDDEN, HIDDEN, 1.0))
    if notes is not None:
        bones["Prop_Notes"].matrix = notes
    else:
        bones["Prop_Notes"].matrix = bones["Chest"].matrix @ Matrix.Diagonal((HIDDEN, HIDDEN, HIDDEN, 1.0))
    bpy.context.view_layer.update()
    for name, _ in PROP_BONES:
        pb = bones[name]
        pb.keyframe_insert("location", frame=number)
        pb.keyframe_insert("rotation_quaternion", frame=number)
        pb.keyframe_insert("scale", frame=number)


def hide_props(rig, frames):
    """In the crew's own clips nothing is in the hands: the props' bones shrink to nothing at the chest."""
    for name in ("Prop_Item", "Prop_Notes"):
        pb = rig.pose.bones[name]
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (HIDDEN, HIDDEN, HIDDEN)
        for number in (0, frames):
            pb.keyframe_insert("location", frame=number)
            pb.keyframe_insert("rotation_quaternion", frame=number)
            pb.keyframe_insert("scale", frame=number)


# ---- The inspect clips ----

def placed(pose):
    """A pose whose hands go exactly where it says: they're on a case or on their way to it, not pushed about."""
    pose.lock_contacts = True
    return pose


def free(pose):
    """A pose whose hands hold nothing: like the idle's, they are moved out of the body (the short body's go out
    round its belly). Her interactions do this for their empty hands; locked, the hands of the blends into and
    out of them jumped at the first and last frame."""
    pose.lock_contacts = False
    pose.contact_sides = ()
    return pose


def her_elbows(pose, like):
    """On the way into or out of one of her interactions that places its elbows for the straightest wrist, so
    does the blend. A blend is a new pose without her flag, and the plain elbows flipped up like a wing."""
    if getattr(like, "optimize_wrists", False):
        pose.optimize_wrists = True
    return pose


class InspectClip:
    """Carrying a case (Tobacco, Cash, Gold): it drops, opens, and her interaction plays with what's in it."""

    def __init__(self, kind):
        self.kind = kind
        self.name, self.her_seconds = INTERACTION[kind]
        self.start = RISE[1]
        self.end_of_hers = self.start + self.her_seconds
        self.seconds = self.end_of_hers + SETTLE
        self.previous_cigarette = [None]

    # Times through the whole clip, in seconds.
    def her_u(self, t):
        return max(0.0, min(1.0, (t - self.start) / self.her_seconds))

    def release(self, carry, step=2):
        """The hands let go: slid back off the palms, or slid back along the side walls (clear of the handles'
        roots) and then opened outward. Step 1 is the sliding back alone."""
        tray_held = HOLD.get(self.kind, "hug") == "tray"
        x, y, z = RELEASE_MOVE if tray_held else OPEN_MOVE
        if step == 1 and not tray_held:
            x, z = 0.0, 0.0
        fingers = TRAY_FINGERS if tray_held else HUG_FINGERS
        result = carry.copy(hand_l=tuple(Vector(carry.hand_l) + Vector((x, y, z))),
                            hand_r=tuple(Vector(carry.hand_r) + Vector((-x, y, z))),
                            fingers_l=fingers, fingers_r=fingers)
        if step == 2 and not tray_held:
            # Once the hands are off the walls the elbows come in: kept out the way the hold has them, the
            # arms flapped out like wings while the hands came round to the front.
            result.elbow_l, result.elbow_r = LET_GO_ELBOW["L"], LET_GO_ELBOW["R"]
        return result

    def rested(self, b, pose):
        """The hands on their way down to hanging, where they wait between letting go of the case and taking
        what's in it, and again before the case comes back up. Hands that waited in the air, palms out, looked
        stiff. A case held between the palms comes back up between them, so they wait outside its side walls."""
        w = SIZE[self.kind][0]
        hands = {}
        for side in ("L", "R"):
            sign = 1.0 if side == "L" else -1.0
            wrist, palm = hanging(b, side)
            if HOLD.get(self.kind, "hug") == "hug":
                wrist = (sign * max(abs(wrist[0]), w / 2 + HUG_PRESS + 0.04), wrist[1], wrist[2])
            hands[side] = (wrist, palm)
        hung = pose.copy(hand_l=hands["L"][0], hand_r=hands["R"][0], palm_l=hands["L"][1], palm_r=hands["R"][1],
                         elbow_l=HANG_ELBOW["L"], elbow_r=HANG_ELBOW["R"], fingers_l=crew.RELAXED, fingers_r=crew.RELAXED)
        return blend(pose, hung, REST_DROP, smoothed=True)

    def pose(self, b, u):
        t = u * self.seconds
        carry, _ = hold(b, self.kind, 0.0)
        let_go = self.release(carry)
        first = interaction(b, self.kind, 0.0)
        last = interaction(b, self.kind, 1.0)
        if t < self.start:
            if t < RELEASE / 2:
                return placed(blend(carry, self.release(carry, 1), t / (RELEASE / 2)))
            if t < RELEASE:
                return placed(blend(self.release(carry, 1), let_go, (t - RELEASE / 2) / (RELEASE / 2)))
            rest = self.rested(b, let_go)
            if t < RISE[0]:
                return free(blend(let_go, rest, (t - RELEASE) / (RISE[0] - RELEASE)))
            return her_elbows(free(blend(rest, first, (t - RISE[0]) / (self.start - RISE[0]))), first)
        if t <= self.end_of_hers:
            return interaction(b, self.kind, self.her_u(t))
        back = t - self.end_of_hers
        rest = self.rested(b, let_go)
        # Down, then up beside where the case will be before it comes in over the hands (LIFT), then into the
        # grips as it settles. Rising while it came in, the sleeves were in its way.
        if back < CATCH_REST:
            return her_elbows(free(blend(last, rest, back / CATCH_REST)), last)
        if back < CATCH[0]:
            return free(blend(rest, self.release(carry, 1), (back - CATCH_REST) / (CATCH[0] - CATCH_REST)))
        middle = (CATCH[0] + CATCH[1]) / 2
        if back < middle:
            return placed(self.release(carry, 1))
        return placed(blend(self.release(carry, 1), carry, (back - middle) / (CATCH[1] - middle)))

    def ground(self, b):
        """Where the case lands: below where it was carried, or far enough forward to clear the shoes."""
        _, centre = hold(b, self.kind, 0.0)
        d = SIZE[self.kind][1]
        near = min(centre.y + d / 2 - PUSH, b.front(0.0, 0.3) - TOE_GAP)
        return Vector((centre.x, near - d / 2, 0.0)), centre

    def case_at(self, b, t):
        landed, carried = self.ground(b)
        back = t - self.end_of_hers
        if t < FALL[0]:
            return case_matrix(carried)
        if t < FALL[1]:
            # Pushed forward off the palms first, then down.
            s = (t - FALL[0]) / (FALL[1] - FALL[0])
            across = 1 - (1 - s) ** 3
            origin = Vector((lerp(carried.x, landed.x, across), lerp(carried.y, landed.y, across), lerp(carried.z, landed.z, s ** 2.5)))
            return case_matrix(origin, tip=math.radians(-8.0) * math.sin(math.pi * s))
        if back < LIFT[0]:
            return case_matrix(landed)
        if back < LIFT[1]:
            # Up in front of you first, then in over the hands.
            s = (back - LIFT[0]) / (LIFT[1] - LIFT[0])
            up = 1 - (1 - min(1.0, s / 0.65)) ** 2
            inward = ease((s - 0.45) / 0.55)
            origin = Vector((lerp(landed.x, carried.x, inward), lerp(landed.y, carried.y, inward), lerp(landed.z, carried.z + HOVER, up)))
            return case_matrix(origin)
        s = min(1.0, (back - LIFT[1]) / (SETTLE - LIFT[1]))
        return case_matrix(carried, lift=HOVER * (1 - ease(s)))

    def lid_at(self, t):
        back = t - self.end_of_hers
        if t < FALL[1]:
            return 0.0
        if back < SHUT[0]:
            s = min(1.0, (t - FALL[1]) / LID_SPRING)
            # Springs past open and settles back.
            return LID_OPEN * (1 + 0.1 * math.sin(math.pi * s)) * (1 - (1 - s) ** 3)
        return LID_OPEN * (1.0 - ease((back - SHUT[0]) / (SHUT[1] - SHUT[0])))

    def props(self, b, u, rig):
        t = u * self.seconds
        landed, _ = self.ground(b)
        back = t - self.end_of_hers
        slot = slot_matrix(self.kind, case_matrix(landed))
        props = {"case": self.case_at(b, t), "lid": self.lid_at(t), "kind": self.kind, "notes": None}
        scale = 1.0
        if t < RISE[0]:
            item = None
        elif t < self.start:
            s = (t - RISE[0]) / (self.start - RISE[0])
            item = arc(slot, self.held(rig, b, 0.0), s, 0.1)
            if self.kind == "Tobacco":
                scale = lerp(1.0 / PROP["Tobacco"][1][0], 1.0, ease(s))
        elif t <= self.end_of_hers:
            item = self.held(rig, b, self.her_u(t))
            if self.kind == "Tobacco" and self.her_u(t) * self.her_seconds * FPS >= 110:
                item = self.flicked(rig, b, self.her_u(t))
        elif self.kind == "Tobacco":
            item = None
        elif back < BACK:
            item = arc(self.held(rig, b, 1.0), slot, back / BACK, 0.08)
        else:
            item = None
        props["item"] = item
        props["item_scale"] = scale
        if self.kind == "Cash" and self.start <= t <= self.end_of_hers:
            frame = self.her_u(t) * self.her_seconds * FPS
            if 18 <= frame < 34:
                props["notes"] = notes_now(rig)
        return props

    def held(self, rig, b, u):
        """Where her clip has the thing at `u`: following her object frames, or the hand that holds it."""
        if self.kind in ("Gold", "Art"):
            position, turn = object_frame(b, self.name, u)
            return Matrix.Translation(position) @ turn.to_matrix().to_4x4()
        if self.kind == "Cash":
            if u == 0.0 or u == 1.0:
                # Before the arms are posed for that moment: where the bundle sits in the hand there.
                pose = interaction(b, "Cash", u)
                return bundle_frame(desired_hand_frame(b, "L", pose.hand_l, pose.palm_l))
            return bundle_now(rig)
        if u == 0.0:
            pose = interaction(b, "Tobacco", 0.0)
            hand = desired_hand_frame(b, "R", pose.hand_r, pose.palm_r)
            return self.cigarette_in(hand, rig)
        return cigarette_now(rig, self.previous_cigarette)

    def cigarette_in(self, hand, rig):
        """The cigarette in a right hand at `hand`, the way cigarette_now finds it once the fingers are posed."""
        bind = Matrix([list(row) for row in rig["hand_bind_transforms"]["Right"]])
        local = bind.inverted() @ Vector(SMOKE_LOCAL) if SMOKE_LOCAL is not None else Vector((0, 0.12, -0.02))
        hf = normalized_frame(hand)
        q = Matrix((hf.col[1], hf.col[2], hf.col[0])).transposed().to_quaternion()
        return Matrix.Translation(hand @ (bind @ local)) @ q.to_matrix().to_4x4()

    def flicked(self, rig, b, u):
        """Hers: the butt flicked away at frame 110, falling."""
        frame = u * self.her_seconds * FPS
        if not hasattr(self, "butt") or frame < 110.5:
            self.butt = cigarette_now(rig, self.previous_cigarette)
        age = (frame - 110) / FPS
        origin = self.butt.translation
        q = self.butt.to_quaternion()
        position = origin + Vector((-0.3 * age, -0.4 * age, 0.12 * age - 1.6 * age * age))
        turn = Quaternion((1, 0.3, 0), age * 5) @ q
        return Matrix.Translation(position) @ turn.to_matrix().to_4x4()

    def events(self):
        her = self.start
        result = {"drop": FALL[0], "land": FALL[1], "out": RISE[0], "start": her, "back": self.end_of_hers + BACK,
                  "shut": self.end_of_hers + SHUT[1], "lift": self.end_of_hers + LIFT[0], "caught": self.end_of_hers + SETTLE}
        if self.kind == "Cash":
            result["notes"] = her + 34 / FPS
        if self.kind == "Tobacco":
            result["puff"] = her + 74 / FPS
            result["flick"] = her + 110 / FPS
            # The cigarette doesn't come back; the one in the case shows again once the lid has shut.
            result["back"] = self.end_of_hers + SHUT[1]
        return result


class CrateClip:
    """The goose crate on the ground: her opening, the goose up into the hands, her InspectArt, back, her closing."""
    OPEN = 1.2
    UP = 0.36
    LOOK = 3.0
    DOWN = 0.36
    CLOSE = 0.9

    def __init__(self):
        self.kind = "Art"
        self.seconds = self.OPEN + self.UP + self.LOOK + self.DOWN + self.CLOSE

    def times(self, t):
        a = self.OPEN
        b = a + self.UP
        c = b + self.LOOK
        d = c + self.DOWN
        return a, b, c, d

    def pose(self, b, u):
        t = u * self.seconds
        a, bb, c, d = self.times(t)
        if t < a:
            return open_crate(b, t / self.OPEN)[0]
        if t < bb:
            s = (t - a) / self.UP
            if s < 0.5:
                return blend(open_crate(b, 1.0)[0], self.lifted(b, 0.0), s / 0.5)
            return blend(self.lifted(b, 0.0), inspect_pose(b, "InspectArt", 0.0), (s - 0.5) / 0.5)
        if t <= c:
            return inspect_pose(b, "InspectArt", (t - bb) / self.LOOK)
        if t < d:
            s = (t - c) / self.DOWN
            if s < 0.5:
                return blend(inspect_pose(b, "InspectArt", 1.0), self.lifted(b, 1.0), s / 0.5)
            return blend(self.lifted(b, 1.0), open_crate(b, 0.0, closing=True)[0], (s - 0.5) / 0.5)
        return open_crate(b, (t - d) / self.CLOSE, closing=True)[0]

    @staticmethod
    def lifted(b, u):
        """Her hold on the goose with the hands drawn back in front of the belly: they come up there first and
        then go out over the crate to it. Straight from hanging they went through the crate's front wall."""
        hold = inspect_pose(b, "InspectArt", u)
        near = {}
        for side in ("L", "R"):
            x, y, z = getattr(hold, "hand_l" if side == "L" else "hand_r")
            near[side] = (x, max(y, front(b, z) - 0.04), z)
        result = hold.copy(hand_l=near["L"], hand_r=near["R"])
        result.lock_contacts = False
        return result

    def props(self, b, u, rig):
        t = u * self.seconds
        a, bb, c, d = self.times(t)
        case = case_matrix(crate_origin())
        if t < a:
            lid = open_crate(b, t / self.OPEN)[1]
        elif t < d:
            lid = LID_OPEN
        else:
            lid = open_crate(b, (t - d) / self.CLOSE, closing=True)[1]
        slot = slot_matrix("Art", case)

        def look(v):
            position, turn = object_frame(b, "InspectArt", v)
            return Matrix.Translation(position) @ turn.to_matrix().to_4x4()

        if t < a or t >= d:
            item = None
        elif t < bb:
            item = arc(slot, look(0.0), (t - a) / self.UP, 0.12)
        elif t <= c:
            item = look((t - bb) / self.LOOK)
        else:
            item = arc(look(1.0), slot, (t - c) / self.DOWN, 0.1)
        return {"case": case, "lid": lid, "kind": "Art", "item": item, "notes": None}

    def events(self):
        a, b, c, d = self.times(0)
        return {"lidOpen": 27 / FPS, "out": a, "start": b, "back": d, "shut": d + 22 / FPS}


def clips():
    """(name, clip) for every cargo clip, in the order they are baked."""
    result = [("Carry" + kind, CarryClip(kind)) for kind in ("Tobacco", "Cash", "Gold")]
    result += [("Inspect" + kind, InspectClip(kind)) for kind in ("Tobacco", "Cash", "Gold")]
    result.append(("InspectArt", CrateClip()))
    return result


# ---- Setting up the rig: bones, props and markers ----

def add_prop_bones(rig):
    crew.edit_bones(rig, True)
    chest = rig.data.edit_bones["Chest"]
    for name, parent in PROP_BONES:
        bone = rig.data.edit_bones.new(name)
        bone.head = chest.head.copy()
        bone.tail = bone.head + Vector((0.0, 0.1, 0.0))
        bone.roll = 0.0
        bone.parent = rig.data.edit_bones[parent]
        bone.use_deform = False
        bone.use_connect = False
        bone.use_inherit_rotation = True
    crew.edit_bones(rig, False)
    for name, _ in PROP_BONES:
        rig.pose.bones[name].rotation_mode = "QUATERNION"


def load_prop(source, name, scale, mesh_name):
    """One of her props as a single mesh, its origin and axes those of her prop's root, at the size given."""
    path = source / CARGO_FOLDER / "模型" / "Props" / f"{name}.blend"
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.objects = list(src.objects)
    objects = [o for o in dst.objects if o is not None]
    for obj in objects:
        crew.link(obj)
    bpy.context.view_layer.update()
    root = next(o for o in objects if o.parent is None)
    to_root = root.matrix_world.inverted()
    meshes = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        local = to_root @ obj.matrix_world
        obj.parent = None
        obj.data = obj.data.copy()
        obj.data.transform(Matrix.Diagonal((*scale, 1.0)) @ local)
        obj.matrix_world = Matrix.Identity(4)
        meshes.append(obj)
    for obj in objects:
        if obj not in meshes:
            bpy.data.objects.remove(obj)
    mesh = crew.join(meshes, mesh_name)
    for polygon in mesh.data.polygons:
        polygon.use_smooth = True
    one_material_each(mesh)
    return mesh


def one_material_each(mesh):
    """Each of her files brings its own copy of her materials ("Cargo08_Gold.001"); the game matches them by
    name, so every slot goes back to the first copy."""
    for index, material in enumerate(mesh.data.materials):
        if material is None or "." not in material.name:
            continue
        base = material.name.split(".")[0]
        if base in bpy.data.materials:
            mesh.data.materials[index] = bpy.data.materials[base]
        else:
            material.name = base


def drop_unused_materials(obj):
    used = {p.material_index for p in obj.data.polygons}
    for index in reversed(range(len(obj.data.materials))):
        if index not in used:
            obj.active_material_index = index
            with bpy.context.temp_override(object=obj, active_object=obj):
                bpy.ops.object.material_slot_remove()


def notes_stack(source):
    """Hers: twelve notes fanned a little between the right thumb and fingers, as her preview has them."""
    pieces = []
    for i in range(12):
        note = load_prop(source, "Cash_Note", (1.0, 1.0, 1.0), f"Note_{i}")
        place = Matrix.Translation(Vector((0, 0.030, 0.00035 * i))) @ Matrix.Rotation((i - 5.5) * 0.014, 4, "Z")
        note.data.transform(place)
        pieces.append(note)
    return crew.join(pieces, "Prop_Notes")


def parent_to_bone(obj, rig, bone, world):
    obj.parent = rig
    obj.parent_type = "BONE"
    obj.parent_bone = bone
    bpy.context.view_layer.update()
    obj.matrix_world = world


def add_props(rig, source):
    """The props in the hands and the markers the game lines the real cargo up with, at rest."""
    crew.rest(rig)
    names = {}
    for kind in KINDS:
        file, scale = PROP[kind]
        mesh = load_prop(source, file, scale, "Prop_" + kind)
        parent_to_bone(mesh, rig, "Prop_Item", rig.matrix_world @ rig.data.bones["Prop_Item"].matrix_local)
        names[kind] = mesh.name
    notes = notes_stack(source)
    parent_to_bone(notes, rig, "Prop_Notes", rig.matrix_world @ rig.data.bones["Prop_Notes"].matrix_local)
    for marker, bone in (("Marker_Case", "Prop_Case"), ("Marker_Lid", "Prop_Lid")):
        empty = bpy.data.objects.new(marker, None)
        empty.empty_display_size = 0.05
        crew.link(empty)
        parent_to_bone(empty, rig, bone, rig.matrix_world @ rig.data.bones[bone].matrix_local)
    crew.rest(rig, False)
    return names


def first_person_arms(rig, porter):
    """Hers (finalize_package): the arms of the porter's shirt with the head and body cut away. The game draws
    these and the gloves for its first-person inspect, and swaps the sleeves for the clothes you have on."""
    arms = porter.copy()
    arms.data = porter.data.copy()
    crew.link(arms)
    arms.name = arms.data.name = "FP_Arms"
    arms.shape_key_clear()
    bm = bmesh.new()
    bm.from_mesh(arms.data)
    deform = bm.verts.layers.deform.active
    groups = {g.index for g in arms.vertex_groups if any(s in g.name for s in ("UpperArm", "LowerArm", "Hand", "Shoulder"))}
    remove = [v for v in bm.verts if deform is None or sum(w for k, w in v[deform].items() if k in groups) < 0.5]
    bmesh.ops.delete(bm, geom=remove, context="VERTS")
    bm.to_mesh(arms.data)
    bm.free()
    drop_unused_materials(arms)
    return arms


def mouth_point(porter):
    """Hers: the front of the mouth, where the cigarette's filter goes."""
    mesh = porter.data
    slots = [i for i, m in enumerate(mesh.materials) if m and "Mouth" in m.name]
    ids = {v for p in mesh.polygons if p.material_index in slots for v in p.vertices}
    points = [mesh.vertices[i].co for i in ids]
    mouth = sum(points, Vector()) / len(points)
    mouth.y = min(v.y for v in points) - 0.002
    return mouth


def eye_point(porter):
    """The middle between the eyes, at rest, for lining the first-person arms up with the camera."""
    mesh = porter.data
    slots = [i for i, m in enumerate(mesh.materials) if m and "EyeWhite" in m.name]
    ids = {v for p in mesh.polygons if p.material_index in slots for v in p.vertices}
    points = [mesh.vertices[i].co for i in ids]
    return sum(points, Vector()) / len(points)


def setup(rig, source, hands, body, meshes, measured):
    """Everything this module needs on a freshly loaded body. Returns the finger poses to add."""
    add_prop_bones(rig)
    add_props(rig, source)
    if body == "Medium":
        first_person_arms(rig, meshes["Porter"])
    measured.hand_bind = hands
    measured.mouth = mouth_point(meshes["Porter"])
    return capture_fingers(rig, source)


def write_times(path, body, measured):
    """When things happen in each clip, and where the eyes are, for the game (CargoClashBuilder reads it with
    JsonUtility, so it's lists rather than maps). Lengths are in the game's metres."""
    data = json.loads(path.read_text()) if path.exists() else {}
    data["fps"] = FPS
    data["clips"] = []
    for name, clip in clips():
        if not name.startswith("Inspect"):
            continue
        events = clip.events()
        entry = {"name": name, "kind": clip.kind, "seconds": round(clip.seconds, 4)}
        for key in ("drop", "land", "out", "start", "back", "shut", "lift", "caught", "notes", "puff", "flick", "lidOpen"):
            entry[key] = round(events.get(key, -1.0), 4)
        data["clips"].append(entry)
    eyes = [e for e in data.get("eyes", []) if isinstance(e, dict) and e.get("body") != body]
    eye = eye_point(bpy.data.objects["Porter"]) * crew.GAME_SCALE
    eyes.append({"body": body, "x": round(eye.x, 4), "y": round(eye.y, 4), "z": round(eye.z, 4)})
    data["eyes"] = eyes
    data["crateDistance"] = round(-crate_origin().y * crew.GAME_SCALE, 4)
    data["coCarryHandleHeight"] = crew.CO_CARRY_HANDLE
    data["coCarryReach"] = crew.CO_CARRY_REACH
    data["handleOut"] = HANDLE_OUT
    data["handleAt"] = HANDLE_AT
    data.pop("coCarry", None)
    data.pop("sizes", None)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False))

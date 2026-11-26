"""Paints and renders the start screen's interface, and writes it into Assets/Game/UI.

Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_ui.py

Add `-- --only logo`, `-- --only icons`, `-- --only appicon`, `-- --only sprites`, `-- --only cards`, `-- --only menu`
(the main menu's small buttons' icons) or `-- --only livery` (the main menu's plates, painted like the truck) to make one
group.

Sprites and level cards are painted with numpy. The logo is too, from the shapes of the letters in
the font Lilita One (see ASSETS.md), with each letter tilted a little. The cargo on the logo, the Practice
button's icon and the app icon are Astra's models (Cargo_<Kind>.fbx, and the hammer and vacuum from
FirstPersonHands.fbx), read back from Assets/Game/Models and coloured as the game colours them (CARGO_PALETTE,
TOOL_PALETTE). The truck on the icon is her TitleTruck.fbx, coloured with her palette (FLEET_PALETTE).
"""
import math
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import make_ground
import modeling
from make_ground import smoothstep, write_png
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "Assets/Game/UI"
FONT = FOLDER / "LilitaOne-Regular.ttf"

NAVY = (0.09, 0.12, 0.24)
WHITE = (1.0, 1.0, 1.0)
YELLOW = (1.0, 0.8, 0.15)


# ---- Painting helpers. Distances are in pixels, negative inside the shape. ----

def grid(width, height):
    ys, xs = np.mgrid[0:height, 0:width].astype(np.float32)
    return xs + 0.5, ys + 0.5


def box(xs, ys, cx, cy, half_x, half_y, radius):
    qx = np.abs(xs - cx) - (half_x - radius)
    qy = np.abs(ys - cy) - (half_y - radius)
    return np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0)) + np.minimum(np.maximum(qx, qy), 0.0) - radius


def circle(xs, ys, cx, cy, radius):
    return np.hypot(xs - cx, ys - cy) - radius


def segment(xs, ys, ax, ay, bx, by, radius):
    px, py = xs - ax, ys - ay
    dx, dy = bx - ax, by - ay
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(px - dx * t, py - dy * t) - radius


def cover(distance):
    return np.clip(0.5 - distance, 0.0, 1.0)


def blank(width, height, colour=(1.0, 1.0, 1.0)):
    """See-through, but already the right colour, so edges don't fade through black when scaled."""
    image = np.zeros((height, width, 4), np.float32)
    image[..., :3] = colour
    return image


def paint(image, colour, alpha):
    """Draws `colour` over a picture that has an alpha channel."""
    colour = np.asarray(colour, np.float32)
    amount = alpha[..., None]
    result = amount + image[..., 3:4] * (1.0 - amount)
    image[..., :3] = (colour * amount + image[..., :3] * image[..., 3:4] * (1.0 - amount)) / np.maximum(result, 1e-4)
    image[..., 3:4] = result


# ---- Sprites ----

def make_round():
    """White rounded corners, for nine-slicing into any panel or button."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    image[..., 3] = cover(box(xs, ys, 64, 64, 64, 64, 44))
    return image


def make_circle():
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    image[..., 3] = cover(circle(xs, ys, 64, 64, 64))
    return image


def make_frame():
    """A rounded rim with a rounded hole, which rounds the corners of a picture when laid over it."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    image[..., 3] = cover(box(xs, ys, 64, 64, 64, 64, 44)) * (1.0 - cover(box(xs, ys, 64, 64, 52, 52, 30)))
    return image


def make_soft():
    """A soft shadow, for nine-slicing. The solid part starts 40 pixels in from each edge."""
    size = 256
    xs, ys = grid(size, size)
    image = blank(size, size, (0.0, 0.0, 0.0))
    image[..., 3] = smoothstep(40.0, -40.0, box(xs, ys, 128, 128, 88, 88, 40)) ** 1.5
    return image


def make_lock():
    size = 192
    xs, ys = grid(size, size)
    image = blank(size, size, NAVY)
    # The shackle is a half ring on two legs, and the body is a rounded block with a keyhole.
    ring = np.abs(circle(xs, ys, 96, 72, 34)) - 11.0
    ring = np.where(ys > 72, np.minimum(segment(xs, ys, 62, 72, 62, 104, 11), segment(xs, ys, 130, 72, 130, 104, 11)), ring)
    body = box(xs, ys, 96, 124, 60, 46, 16)
    keyhole = np.minimum(circle(xs, ys, 96, 116, 11), segment(xs, ys, 96, 120, 96, 142, 6))
    image[..., 3] = cover(np.minimum(ring, body) - 9.0)
    paint(image, (0.86, 0.88, 0.94), cover(ring))
    paint(image, YELLOW, cover(body))
    paint(image, (1.0, 0.92, 0.5), cover(body) * (ys < 112))
    paint(image, NAVY, cover(keyhole))
    return image


def make_back_arrow():
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    shaft = segment(xs, ys, 38, 64, 100, 64, 11)
    upper = segment(xs, ys, 30, 64, 62, 32, 11)
    lower = segment(xs, ys, 30, 64, 62, 96, 11)
    image[..., 3] = cover(np.minimum(np.minimum(shaft, upper), lower))
    return image


def sky(top, bottom):
    t = np.linspace(0.0, 1.0, CARD_HEIGHT, dtype=np.float32)[:, None, None]
    image = np.array(top, np.float32) + (np.array(bottom, np.float32) - np.array(top, np.float32)) * t
    return np.repeat(image, CARD_WIDTH, axis=1)


def fill_below(image, profile, colour):
    """Fills everything under a line. `profile` holds the line's height, in pixels, for each column."""
    _, ys = grid(CARD_WIDTH, CARD_HEIGHT)
    alpha = np.clip(ys - profile[None, :] + 0.5, 0.0, 1.0)
    make_ground.mix(image, np.array(colour, np.float32), alpha)


def treetops(rng, level, height, width, pointed=False, gap=None):
    """A row of treetops, round or pointed, running across the picture, leaving out any in the `gap` columns."""
    xs = np.arange(CARD_WIDTH, dtype=np.float32)
    profile = np.full(CARD_WIDTH, 9999.0, np.float32)
    x = -width
    while x < CARD_WIDTH + width:
        size = rng.uniform(0.75, 1.25)
        half = width * size
        top = level - height * size * rng.uniform(0.8, 1.1)
        t = np.clip(np.abs(xs - x) / half, 0.0, 1.0)
        shape = t if pointed else 1.0 - np.sqrt(np.maximum(1.0 - t * t, 0.0))
        tree = top + (level - top) * shape
        if gap is None or not gap[0] < x < gap[1]:
            profile = np.minimum(profile, np.where(np.abs(xs - x) < half, tree, 9999.0))
        x += half * rng.uniform(0.9, 1.3)
    return np.minimum(profile, 9999.0 if gap else level)


def ridge(rng, level, height, peaks):
    """A jagged mountain line through random peaks."""
    xs = np.arange(CARD_WIDTH, dtype=np.float32)
    keys_x = np.linspace(-60.0, CARD_WIDTH + 60.0, peaks * 2 + 1)
    keys_y = np.array([level - (height * rng.uniform(0.6, 1.0) if i % 2 else height * rng.uniform(0.0, 0.2)) for i in range(len(keys_x))])
    return np.interp(xs, keys_x, keys_y).astype(np.float32)


def winding_road(image, colour, edge, centre_x, bend, top_row):
    """A road narrowing into the distance from the bottom of the picture."""
    xs, ys = grid(CARD_WIDTH, CARD_HEIGHT)
    t = np.clip((ys - top_row) / (CARD_HEIGHT - top_row), 0.0, 1.0)
    middle = centre_x + bend * np.sin(t * 3.4) * (1.0 - t * 0.4)
    half = 8.0 + t * t * 135.0
    road = (ys > top_row) & (np.abs(xs - middle) < half)
    verge = (ys > top_row) & (np.abs(xs - middle) < half * 1.14)
    make_ground.mix(image, np.array(edge, np.float32), verge.astype(np.float32))
    make_ground.mix(image, np.array(colour, np.float32), road.astype(np.float32))
    dashes = (np.abs(xs - middle) < 1.0 + t * 5.0) & (np.mod(np.sqrt(t) * 8.0, 1.0) < 0.5) & (ys > top_row + 8)
    make_ground.mix(image, np.array((0.97, 0.95, 0.85), np.float32), dashes.astype(np.float32))


def card_forest():
    rng = np.random.default_rng(2)
    image = sky((0.6, 0.86, 0.82), (0.93, 0.97, 0.72))
    fill_below(image, treetops(rng, 300.0, 70.0, 58.0), (0.56, 0.75, 0.55))
    fill_below(image, treetops(rng, 340.0, 90.0, 66.0, pointed=True), (0.36, 0.62, 0.42))
    fill_below(image, np.full(CARD_WIDTH, 335.0, np.float32), (0.42, 0.66, 0.3))
    winding_road(image, (0.82, 0.68, 0.46), (0.6, 0.48, 0.3), 470.0, 130.0, 338.0)
    fill_below(image, treetops(rng, 500.0, 170.0, 74.0, pointed=True, gap=(250.0, 690.0)), (0.16, 0.4, 0.28))
    return image


def card_mountain():
    rng = np.random.default_rng(3)
    image = sky((0.42, 0.56, 0.9), (0.97, 0.82, 0.76))
    far = ridge(rng, 300.0, 210.0, 4)
    fill_below(image, far, (0.62, 0.62, 0.82))
    fill_below(image, far + 34.0 + 10.0 * np.sin(np.arange(CARD_WIDTH) * 0.09), (0.45, 0.46, 0.68))
    # Snow on the peaks of the far range.
    _, ys = grid(CARD_WIDTH, CARD_HEIGHT)
    snow = np.clip(ys - far[None, :], 0.0, 1.0) * np.clip(far[None, :] + 34.0 + 10.0 * np.sin(np.arange(CARD_WIDTH) * 0.09)[None, :] - ys, 0.0, 1.0) * (ys < 178.0 + 14.0 * np.sin(np.arange(CARD_WIDTH) * 0.16)[None, :])
    make_ground.mix(image, np.array((0.98, 0.98, 1.0), np.float32), snow.astype(np.float32))
    fill_below(image, ridge(rng, 370.0, 130.0, 3), (0.34, 0.36, 0.56))
    fill_below(image, np.full(CARD_WIDTH, 360.0, np.float32) - 18.0 * np.sin(np.arange(CARD_WIDTH, dtype=np.float32) * 0.008), (0.5, 0.56, 0.4))
    winding_road(image, (0.42, 0.42, 0.48), (0.82, 0.8, 0.72), 520.0, -170.0, 372.0)
    return image


def card_river():
    rng = np.random.default_rng(4)
    image = sky((0.42, 0.5, 0.62), (0.78, 0.84, 0.86))
    fill_below(image, treetops(rng, 240.0, 56.0, 96.0), (0.46, 0.56, 0.56))
    fill_below(image, np.full(CARD_WIDTH, 232.0, np.float32), (0.56, 0.48, 0.34))
    xs, ys = grid(CARD_WIDTH, CARD_HEIGHT)
    # The river swings across the mud, wider toward the viewer.
    t = np.clip((ys - 232.0) / (CARD_HEIGHT - 232.0), 0.0, 1.0)
    middle = 520.0 + 240.0 * np.sin(t * 5.0 + 0.6)
    half = 18.0 + t * 150.0
    water = np.abs(xs - middle) - half
    make_ground.mix(image, np.array((0.44, 0.36, 0.25), np.float32), cover(water - 14.0) * (ys > 232))
    make_ground.mix(image, np.array((0.32, 0.6, 0.76), np.float32), cover(water) * (ys > 232))
    glint = np.sin(ys * 0.5 + xs * 0.03) > 0.86
    make_ground.mix(image, np.array((0.62, 0.82, 0.9), np.float32), (glint & (water < -6.0) & (ys > 240)).astype(np.float32))
    # Rain falling across everything.
    streaks = np.zeros((CARD_HEIGHT, CARD_WIDTH), np.float32)
    for _ in range(170):
        x = rng.uniform(0, CARD_WIDTH)
        y = rng.uniform(-40, CARD_HEIGHT)
        streaks = np.maximum(streaks, cover(segment(xs, ys, x, y, x - 10.0, y + 34.0, 1.3)))
    make_ground.mix(image, np.array((0.9, 0.95, 1.0), np.float32), streaks * 0.55)
    return image


def card_unknown(top, bottom, crate, seed):
    """Nothing is decided about this level yet, so the picture only hints at scattered cargo."""
    rng = np.random.default_rng(seed)
    image = sky(top, bottom)
    xs, ys = grid(CARD_WIDTH, CARD_HEIGHT)
    for _ in range(26):
        cx, cy = rng.uniform(0, CARD_WIDTH), rng.uniform(0, CARD_HEIGHT)
        half = rng.uniform(22.0, 62.0)
        angle = rng.uniform(-0.5, 0.5)
        u = (xs - cx) * math.cos(angle) + (ys - cy) * math.sin(angle)
        v = (ys - cy) * math.cos(angle) - (xs - cx) * math.sin(angle)
        shape = box(u, v, 0.0, 0.0, half, half, 7.0)
        make_ground.mix(image, np.array(crate, np.float32), cover(shape) * rng.uniform(0.25, 0.6))
    return image


# ---- Masks. The logo is worked out at twice its final size, which smooths the edges. ----

SUPER = 2
kernels = {}


def rasterize(triangles, width, height):
    """Fills triangles, an array of shape (n, 3, 2) in pixels, into a true/false picture."""
    mask = np.zeros((height, width), bool)
    for a, b, c in triangles:
        x0 = max(int(min(a[0], b[0], c[0])), 0)
        y0 = max(int(min(a[1], b[1], c[1])), 0)
        x1 = min(int(max(a[0], b[0], c[0])) + 2, width)
        y1 = min(int(max(a[1], b[1], c[1])) + 2, height)
        if x0 >= x1 or y0 >= y1:
            continue
        ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        xs += 0.5
        ys += 0.5
        d0 = (b[0] - a[0]) * (ys - a[1]) - (b[1] - a[1]) * (xs - a[0])
        d1 = (c[0] - b[0]) * (ys - b[1]) - (c[1] - b[1]) * (xs - b[0])
        d2 = (a[0] - c[0]) * (ys - c[1]) - (a[1] - c[1]) * (xs - c[0])
        inside = ((d0 >= 0) & (d1 >= 0) & (d2 >= 0)) | ((d0 <= 0) & (d1 <= 0) & (d2 <= 0))
        mask[y0:y1, x0:x1] |= inside
    return mask


def dilate(mask, radius, drop=0.0):
    """Grows a mask by `radius` pixels with round corners, and sweeps it `drop` pixels down the picture."""
    height, width = mask.shape
    key = (width, height, radius, drop)
    if key not in kernels:
        ys = np.fft.fftfreq(height, 1.0 / height).astype(np.float32)
        xs = np.fft.fftfreq(width, 1.0 / width).astype(np.float32)
        xs, ys = np.meshgrid(xs, ys)
        kernel = segment(xs, ys, 0.0, 0.0, 0.0, max(drop, 1e-3), radius) <= 0.0
        kernels[key] = np.fft.rfft2(kernel.astype(np.float32))
    grown = np.fft.irfft2(np.fft.rfft2(mask.astype(np.float32)) * kernels[key], s=mask.shape)
    return grown > 0.5


def fill_holes(mask):
    """Fills anything the outside can't reach, such as the middle of an O. Worked out at a quarter of the size."""
    rows = np.flatnonzero(mask.any(axis=1))
    columns = np.flatnonzero(mask.any(axis=0))
    top, bottom = rows[0], rows[-1] + 1
    left, right = columns[0], columns[-1] + 1
    small = mask[top:bottom:4, left:right:4]
    free = np.pad(~small, 1, constant_values=True)
    outside = np.zeros_like(free)
    outside[0, :] = True
    while True:
        grown = outside.copy()
        grown[1:] |= outside[:-1]
        grown[:-1] |= outside[1:]
        grown[:, 1:] |= outside[:, :-1]
        grown[:, :-1] |= outside[:, 1:]
        grown &= free
        if (grown == outside).all():
            break
        outside = grown
    holes = free & ~outside
    # One step wider, so no thin line is left between the hole and what surrounds it.
    grown = holes.copy()
    grown[1:] |= holes[:-1]
    grown[:-1] |= holes[1:]
    grown[:, 1:] |= holes[:, :-1]
    grown[:, :-1] |= holes[:, 1:]
    holes = grown[1:-1, 1:-1]
    holes = np.repeat(np.repeat(holes, 4, axis=0), 4, axis=1)[:bottom - top, :right - left]
    filled = mask.copy()
    filled[top:bottom, left:right] |= holes
    return filled


def blur(alpha, radius):
    height, width = alpha.shape
    ys = np.fft.fftfreq(height, 1.0 / height).astype(np.float32)
    xs = np.fft.rfftfreq(width, 1.0 / width).astype(np.float32)
    xs, ys = np.meshgrid(xs, ys)
    soft = np.exp(-2.0 * (math.pi * radius) ** 2 * ((xs / width) ** 2 + (ys / height) ** 2))
    return np.fft.irfft2(np.fft.rfft2(alpha) * soft, s=alpha.shape).astype(np.float32)


def shrink_half(values):
    """Averages a picture down to half its size. Works on masks and on images with channels."""
    height, width = values.shape[0] // SUPER, values.shape[1] // SUPER
    shape = (height, SUPER, width, SUPER) + values.shape[2:]
    return values.reshape(shape).astype(np.float32).mean(axis=(1, 3))


def from_palette(positions, colours, amount):
    """Colours along a ramp. `amount` is an array of positions."""
    colours = np.array(colours, np.float32)
    return np.stack([np.interp(amount, positions, colours[:, c]) for c in range(3)], axis=-1).astype(np.float32)


# ---- Models, rendered on a clear background. ----

def setup_render(size, view_width, samples=48):
    """An orthographic camera looking down at the XY plane, in soft light."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    world = bpy.data.worlds.new("Soft")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (1.0, 1.0, 1.0, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.62
    scene.world = world

    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 2.0
    sun.angle = math.radians(35.0)
    sun_object = bpy.data.objects.new("Sun", sun)
    sun_object.rotation_euler = (math.radians(32.0), math.radians(-24.0), 0.0)
    scene.collection.objects.link(sun_object)

    camera = bpy.data.cameras.new("Camera")
    camera.type = "ORTHO"
    camera.ortho_scale = view_width
    camera_object = bpy.data.objects.new("Camera", camera)
    camera_object.location = (0.0, 0.0, 30.0)
    scene.collection.objects.link(camera_object)
    scene.camera = camera_object


def render_to_array(folder):
    """Renders the scene and returns it as a float array with row 0 at the top of the picture."""
    scene = bpy.context.scene
    path = Path(folder) / "model.png"
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(str(path))
    width, height = image.size
    values = np.zeros(width * height * 4, np.float32)
    image.pixels.foreach_get(values)
    bpy.data.images.remove(image)
    return values.reshape(height, width, 4)[::-1].copy()


def turn_to_camera(pose, centre=(0.0, 0.0, 0.0)):
    """Points a model's front (-Y) at the camera. `pose` is degrees of (lean back to show the top, turn to show a side, roll on the screen)."""
    lean, turn, roll = (math.radians(angle) for angle in pose)
    holder = bpy.data.objects.new("Holder", None)
    bpy.context.scene.collection.objects.link(holder)
    for item in bpy.context.scene.objects:
        if item.type == "MESH":
            item.parent = holder
    move = Matrix.Translation(-Vector(centre))
    holder.matrix_world = Matrix.Rotation(roll, 4, "Z") @ Matrix.Rotation(lean - math.pi / 2.0, 4, "X") @ Matrix.Rotation(turn, 4, "Z") @ move


def hex_rgb(hex_colour):
    """An sRGB hex colour, as Blender's linear colour."""
    return tuple(linear(int(hex_colour[i:i + 2], 16) / 255.0) for i in (0, 2, 4))


def paint_materials(colour_of):
    """Gives each imported material the colour the game gives it. `colour_of(name)` is (linear RGB, roughness, metallic) or None."""
    for material in bpy.data.materials:
        found = colour_of(material.name.split(".")[0])
        if found is None or not material.use_nodes:
            continue
        tree = material.node_tree
        shader = tree.nodes.get("Principled BSDF")
        if shader is None:
            continue
        # A picture or a transparency the FBX brought with it would win over the colour.
        for link in list(tree.links):
            if link.to_node == shader and link.to_socket.name in ("Base Color", "Alpha"):
                tree.links.remove(link)
        rgb, roughness, metallic = found
        shader.inputs["Base Color"].default_value = (*rgb, 1.0)
        shader.inputs["Roughness"].default_value = roughness
        shader.inputs["Metallic"].default_value = metallic
        shader.inputs["Alpha"].default_value = 1.0


def import_fbx(path):
    """Imports a model and returns the objects it added."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    return [item for item in bpy.data.objects if item not in before]


def flatten(objects):
    """Keeps each mesh where it is, without the empties above it, so the meshes can be moved together."""
    bpy.context.view_layer.update()
    meshes = [item for item in objects if item.type == "MESH"]
    placed = {item: item.matrix_world.copy() for item in meshes}
    for item in meshes:
        item.parent = None
        item.matrix_world = placed[item]
    for item in objects:
        if item.type != "MESH":
            bpy.data.objects.remove(item)
    return meshes


def move(meshes, offset=(0.0, 0.0, 0.0), turn=(0.0, 0.0, 0.0), scale=1.0):
    """Turns meshes about the origin (degrees about X, then Y, then Z), scales them and then moves them."""
    rx, ry, rz = (math.radians(angle) for angle in turn)
    matrix = (Matrix.Translation(Vector(offset)) @ Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y") @
              Matrix.Rotation(rx, 4, "X") @ Matrix.Scale(scale, 4))
    for item in meshes:
        item.matrix_world = matrix @ item.matrix_world


# Astra's cargo colours (独立建模/互动货物_v08, her build_cargo.py PALETTE) as the game draws them
# (CargoClashBuilder.AddAstraCargoMaterials, which also holds metal to 0.45): sRGB hex, roughness, metallic.
CARGO_PALETTE = {
    "Ink": ("172B33", 0.48, 0.1), "Teal": ("367F83", 0.55, 0.05), "TealLight": ("62ABA5", 0.55, 0.05),
    "Wood": ("B78252", 0.68, 0.0), "WoodCut": ("E7BE83", 0.7, 0.0), "Gold": ("EFB64C", 0.28, 0.45),
    "GoldLight": ("FFE2A0", 0.25, 0.45), "Steel": ("7D9B9E", 0.4, 0.45), "Silver": ("CFDBD5", 0.35, 0.45),
    "Red": ("B54F40", 0.5, 0.05), "RedLight": ("E27B59", 0.55, 0.02), "Velvet": ("63476D", 0.9, 0.0),
    "Foam": ("233A3C", 0.95, 0.0), "Ivory": ("F4E6C3", 0.37, 0.0), "Porcelain": ("E7F0D7", 0.24, 0.05),
    "Green": ("50896D", 0.7, 0.0), "Paper": ("DBE1B2", 0.8, 0.0), "PaperEdge": ("AFB992", 0.9, 0.0),
    "Filter": ("C29154", 0.88, 0.0), "Ash": ("686A67", 0.9, 0.0), "Ember": ("F77832", 0.45, 0.0),
    "Smoke": ("C5D1CA", 1.0, 0.0), "Stage": ("E8DED0", 0.8, 0.0), "NotePrint": ("DBE1B2", 0.8, 0.0),
}

# The tools' colours in the game (CargoClashBuilder.ModelMaterials): sRGB, then roughness (1 - smoothness) and metallic.
# The vacuum's team colour is blue here, so the app icon has both teams' colours.
TOOL_PALETTE = {
    "Hammer_Handle": ((0.55, 0.35, 0.18), 0.8, 0.0), "Hammer_Head": ((0.5, 0.52, 0.57), 0.4, 0.0),
    "Hammer_Grip": ((0.7, 0.54, 0.34), 0.8, 0.0), "Vacuum_Housing": ((0.97, 0.72, 0.2), 0.55, 0.0),
    "Vacuum_Red": ((0.82, 0.32, 0.24), 0.6, 0.0), "Vacuum_Steel": ((0.55, 0.62, 0.62), 0.4, 0.0),
    "Vacuum_Glass": ((0.3, 0.66, 0.74), 0.15, 0.0), "Vacuum_Ivory": ((0.97, 0.89, 0.76), 0.7, 0.0),
    "Char_Team": ((0.22, 0.5, 0.95), 0.8, 0.0), "Heli_Dark": ((0.13, 0.14, 0.17), 0.7, 0.0),
}


def cargo_colour(name):
    if name.startswith("Cargo08_") and name[len("Cargo08_"):] in CARGO_PALETTE:
        hex_colour, roughness, metallic = CARGO_PALETTE[name[len("Cargo08_"):]]
        return hex_rgb(hex_colour), roughness, metallic
    if name in TOOL_PALETTE:
        rgb, roughness, metallic = TOOL_PALETTE[name]
        return tuple(linear(channel) for channel in rgb), roughness, metallic
    return None


def import_astra_cargo(kind, lid=0.0, lift=0.0, grow=1.0):
    """One of Astra's four kinds as the game has it (Assets/Game/Models/Cargo_<kind>.fbx): whole, with its lid opened
    `lid` degrees (None takes it off) and what's inside lifted `lift` metres and made `grow` times bigger. Her front is
    -Y and her origin the middle of the bottom."""
    objects = import_fbx(ROOT / f"Assets/Game/Models/Cargo_{kind}.fbx")

    def stage(item):
        while item is not None:
            if item.name.split(".")[0] in ("Stage0", "Stage1", "Stage2"):
                return item.name.split(".")[0]
            item = item.parent
        return None

    # Only the undamaged stage. The other two sit in the same place.
    for item in [item for item in objects if stage(item) in ("Stage1", "Stage2")]:
        objects.remove(item)
        bpy.data.objects.remove(item)
    bpy.context.view_layer.update()
    pivot = next(item for item in objects if item.name.startswith("Lid_Pivot0"))
    if lid is None:
        for item in [pivot, *pivot.children_recursive]:
            objects.remove(item)
            bpy.data.objects.remove(item)
    else:
        # Her hinge turns about its own X, and the front edge comes up for negative angles (cargo_actions.LID_OPEN).
        pivot.matrix_basis = pivot.matrix_basis @ Matrix.Rotation(math.radians(-lid), 4, "X")
    bpy.context.view_layer.update()
    inside = next(item for item in objects if item.name.startswith("Slot0"))
    corners = [inside.matrix_world @ Vector(corner) for corner in inside.bound_box]
    base = Vector((sum(c.x for c in corners) / 8.0, sum(c.y for c in corners) / 8.0, min(c.z for c in corners)))
    inside.matrix_world = (Matrix.Translation(base + Vector((0.0, 0.0, lift))) @ Matrix.Scale(grow, 4) @
                           Matrix.Translation(-base) @ inside.matrix_world)
    paint_materials(cargo_colour)
    return flatten(objects)


def import_tools():
    """The hammer and the vacuum from the first-person hands (Assets/Game/Models/FirstPersonHands.fbx)."""
    objects = import_fbx(ROOT / "Assets/Game/Models/FirstPersonHands.fbx")
    tools = {}
    for item in objects:
        name = item.name.split(".")[0]
        if name in ("Hammer", "Vacuum"):
            tools[name] = item
        else:
            bpy.data.objects.remove(item)
    paint_materials(cargo_colour)
    flatten(list(tools.values()))
    return tools


def render_astra_cargo(kind, pose, size, view_width, folder, lid=0.0, lift=0.0, grow=1.0, centre=None):
    modeling.reset_scene()
    setup_render(size, view_width)
    meshes = import_astra_cargo(kind, lid, lift, grow)
    if centre is None:
        # The middle of the case, so the picture is centred on it whatever the pose.
        lows = [min((item.matrix_world @ Vector(corner)).z for corner in item.bound_box) for item in meshes]
        highs = [max((item.matrix_world @ Vector(corner)).z for corner in item.bound_box) for item in meshes]
        centre = (0.0, 0.0, (min(lows) + max(highs)) / 2.0)
    turn_to_camera(pose, centre)
    return render_to_array(folder)


# Astra's vehicle colours (独立建模/运输载具_v07/交接/palette.json): sRGB hex, roughness, metallic.
FLEET_PALETTE = {
    "Fleet_Navy": ("303950", 0.46, 0.12), "Fleet_Orange": ("CB682B", 0.46, 0.08), "Fleet_Cream": ("DFD8B7", 0.46, 0.05),
    "Fleet_Gold": ("D9A928", 0.42, 0.12), "Fleet_Dark": ("353934", 0.58, 0.12), "Fleet_Rubber": ("24292C", 0.76, 0.0),
    "Fleet_Steel": ("849393", 0.33, 0.6), "Fleet_Brass": ("AC8E52", 0.36, 0.55), "Fleet_Glass": ("284B58", 0.18, 0.3),
    "Fleet_GlassGlint": ("8DB1B8", 0.25, 0.24), "Fleet_Lamp": ("FFF1BE", 0.26, 0.12), "Fleet_Red": ("B9503E", 0.4, 0.12),
    "Fleet_Grille": ("182932", 0.65, 0.05),
}
# Where the title truck's two sample wheels go: her axles, in Blender's axes (front along +Y).
TRUCK_AXLES = (2.75, -0.15, -2.95)
TRUCK_WHEEL = (2.36, 1.05)


def linear(channel):
    return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4


def render_truck(pose, size, view_width, folder):
    """Astra's truck, as the game shows it on the start screen, with a wheel on each end of every axle."""
    modeling.reset_scene()
    setup_render(size, view_width)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / "Assets/Game/Models/TitleTruck.fbx"))
    for material in bpy.data.materials:
        colour = FLEET_PALETTE.get(material.name.split(".")[0])
        if colour is None or not material.use_nodes:
            continue
        shader = material.node_tree.nodes.get("Principled BSDF")
        hex_colour, roughness, metallic = colour
        rgb = [linear(int(hex_colour[i:i + 2], 16) / 255.0) for i in (0, 2, 4)]
        shader.inputs["Base Color"].default_value = (*rgb, 1.0)
        shader.inputs["Roughness"].default_value = roughness
        shader.inputs["Metallic"].default_value = metallic
    for name, side in (("Wheel_L", 1.0), ("Wheel_R", -1.0)):
        sample = bpy.data.objects[name]
        for y in TRUCK_AXLES:
            copy = sample.copy()
            bpy.context.scene.collection.objects.link(copy)
            copy.location = (side * TRUCK_WHEEL[0], y, TRUCK_WHEEL[1])
        bpy.data.objects.remove(sample)
    for item in list(bpy.context.scene.objects):
        if item.type == "EMPTY":
            bpy.data.objects.remove(item)
    turn_to_camera(pose, centre=(0.0, 0.0, 2.1))
    return render_to_array(folder)


def with_outline(rgba, line_width):
    """A rendered model, with a dark line around it, at half the size it was rendered."""
    solid = rgba[..., 3] > 0.5
    line = dilate(solid, line_width * SUPER)
    colours = rgba[..., :3] * rgba[..., 3:4]
    alpha = shrink_half(rgba[..., 3])
    colours = shrink_half(colours) / np.maximum(alpha[..., None], 1e-4)
    result = blank(alpha.shape[1], alpha.shape[0], NAVY)
    result[..., 3] = shrink_half(line)
    paint(result, np.clip(colours, 0.0, 1.0), alpha)
    return result


# ---- Logo ----

LOGO_WIDTH = 1800
LOGO_HEIGHT = 1000
PER_UNIT = 100.0


def to_canvas(points):
    """From logo units, centred with Y up, to canvas pixels at the working size."""
    result = np.empty_like(points)
    result[..., 0] = (points[..., 0] + LOGO_WIDTH / PER_UNIT / 2.0) * PER_UNIT * SUPER
    result[..., 1] = (LOGO_HEIGHT / PER_UNIT / 2.0 - points[..., 1]) * PER_UNIT * SUPER
    return result


def letter_triangles(letter, font, size):
    """The filled shape of one letter, as triangles in logo units."""
    curve = bpy.data.curves.new("Letter", "FONT")
    curve.body = letter
    curve.font = font
    curve.size = size
    curve.resolution_u = 24
    holder = bpy.data.objects.new("Letter", curve)
    bpy.context.scene.collection.objects.link(holder)
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(holder.evaluated_get(graph))
    mesh.calc_loop_triangles()
    triangles = np.array([[mesh.vertices[i].co[:2] for i in triangle.vertices] for triangle in mesh.loop_triangles], np.float32)
    bpy.data.objects.remove(holder)
    bpy.data.curves.remove(curve)
    bpy.data.meshes.remove(mesh)
    return triangles


def add_word(canvas, silhouette, word, font, centre, sizes, tilts, lifts, gap, arch, colours):
    """Lays out one word. Each letter gets its own tilt and its own dark edge, so they stack like stickers."""
    shapes = [letter_triangles(letter, font, size) for letter, size in zip(word, sizes)]
    lefts = [shape[..., 0].min() for shape in shapes]
    rights = [shape[..., 0].max() for shape in shapes]
    total = sum(right - left for left, right in zip(lefts, rights)) + gap * (len(word) - 1)
    x = centre[0] - total / 2.0

    xs, ys = grid(LOGO_WIDTH, LOGO_HEIGHT)
    positions, ramp = zip(*colours["face"])
    for shape, left, right, tilt, lift in zip(shapes, lefts, rights, tilts, lifts):
        width = right - left
        tallest = shape[..., 1].max()
        t = (x + width / 2.0 - centre[0]) / (total / 2.0)
        angle = math.radians(tilt) + math.atan(-2.0 * arch * t / (total / 2.0))
        cosine, sine = math.cos(angle), math.sin(angle)
        pivot = np.array(((left + right) / 2.0, tallest / 2.0), np.float32)
        place = np.array((x + width / 2.0, centre[1] + lift - arch * t * t + tallest / 2.0), np.float32)

        local = shape - pivot
        turned = np.stack((local[..., 0] * cosine - local[..., 1] * sine, local[..., 0] * sine + local[..., 1] * cosine), axis=-1) + place
        face = rasterize(to_canvas(turned), LOGO_WIDTH * SUPER, LOGO_HEIGHT * SUPER)

        edge = fill_holes(dilate(face, 19 * SUPER, 26 * SUPER))
        side = dilate(face, 2 * SUPER, 20 * SUPER)
        paint(canvas, NAVY, shrink_half(edge))
        paint(canvas, colours["side"], shrink_half(side))
        silhouette |= edge

        # How high up the letter each pixel is, from 0 at the bottom to 1 at the top.
        offset_x = (xs - LOGO_WIDTH / 2.0) / PER_UNIT - place[0]
        offset_y = (LOGO_HEIGHT / 2.0 - ys) / PER_UNIT - place[1]
        height = (offset_y * cosine - offset_x * sine + pivot[1]) / tallest
        colour = from_palette(positions, ramp, height)
        paint(canvas, colour, shrink_half(face))

        # A bright line along the top edges and a dark one along the bottom edges make it look moulded.
        shine = face & ~np.roll(face, 7 * SUPER, axis=0)
        shade = face & ~np.roll(face, -6 * SUPER, axis=0)
        paint(canvas, colours["shine"], shrink_half(shine) * 0.9)
        paint(canvas, colours["shade"], shrink_half(shade) * 0.75)
        x += width + gap


def add_burst(canvas, silhouette, centre, radius_x, radius_y, points, seed):
    """A comic-book impact star behind the letters."""
    rng = np.random.default_rng(seed)
    corners = []
    for i in range(points * 2):
        angle = i * math.pi / points + rng.uniform(-0.06, 0.06)
        scale = rng.uniform(0.9, 1.12) if i % 2 == 0 else rng.uniform(0.6, 0.72)
        corners.append((math.cos(angle) * radius_x * scale, math.sin(angle) * radius_y * scale))
    corners = np.array(corners, np.float32)

    def star(scale):
        triangles = np.zeros((len(corners), 3, 2), np.float32)
        triangles[:, 0] = centre
        triangles[:, 1] = corners * scale + centre
        triangles[:, 2] = np.roll(corners, -1, axis=0) * scale + centre
        return rasterize(to_canvas(triangles), LOGO_WIDTH * SUPER, LOGO_HEIGHT * SUPER)

    outer = star(1.0)
    edge = dilate(outer, 15 * SUPER, 12 * SUPER)
    silhouette |= edge
    paint(canvas, NAVY, shrink_half(edge))
    paint(canvas, (0.93, 0.27, 0.13), shrink_half(outer))
    paint(canvas, (1.0, 0.52, 0.12), shrink_half(star(0.84)))
    paint(canvas, (1.0, 0.76, 0.2), shrink_half(star(0.62)))


def add_model(canvas, silhouette, rgba, centre, line_width):
    """Stamps a rendered model onto the logo. `centre` is in logo units."""
    height, width = rgba.shape[:2]
    solid = rgba[..., 3] > 0.5
    line = dilate(solid, line_width * SUPER)
    spot = to_canvas(np.array(centre, np.float32))
    left = int(spot[0] - width / 2) // SUPER * SUPER
    top = int(spot[1] - height / 2) // SUPER * SUPER

    silhouette[top:top + height, left:left + width] |= line
    stamp = with_outline(rgba, line_width)
    region = canvas[top // SUPER:top // SUPER + stamp.shape[0], left // SUPER:left // SUPER + stamp.shape[1]]
    paint(region, stamp[..., :3], stamp[..., 3])


def make_logo():
    orange = {
        "face": [(0.0, (1.0, 0.46, 0.05)), (0.49, (1.0, 0.62, 0.09)), (0.5, (1.0, 0.8, 0.18)), (1.0, (1.0, 0.94, 0.46))],
        "side": (0.7, 0.22, 0.05),
        "shine": (1.0, 0.99, 0.82),
        "shade": (0.82, 0.3, 0.04),
    }
    blue = {
        "face": [(0.0, (0.08, 0.38, 0.96)), (0.49, (0.18, 0.56, 1.0)), (0.5, (0.42, 0.78, 1.0)), (1.0, (0.76, 0.96, 1.0))],
        "side": (0.05, 0.17, 0.56),
        "shine": (0.94, 1.0, 1.0),
        "shade": (0.05, 0.26, 0.74),
    }

    with tempfile.TemporaryDirectory() as folder:
        # Astra's cargo: the goose crate with the goose looking out, the gold case and the cash case.
        crate = render_astra_cargo("Art", (14.0, -30.0, -10.0), 820, 1.42, folder, lid=None, lift=0.27, grow=1.3)
        gold = render_astra_cargo("Gold", (36.0, -26.0, 14.0), 680, 1.04, folder)
        cash = render_astra_cargo("Cash", (36.0, 26.0, -14.0), 520, 1.04, folder)

    modeling.reset_scene()
    font = bpy.data.fonts.load(str(FONT))
    canvas = blank(LOGO_WIDTH, LOGO_HEIGHT, NAVY)
    silhouette = np.zeros((LOGO_HEIGHT * SUPER, LOGO_WIDTH * SUPER), bool)

    add_burst(canvas, silhouette, (0.2, -1.05), 7.4, 3.15, 13, 41)
    add_word(canvas, silhouette, "CARGO", font, (-0.05, 0.98), (4.5, 3.95, 3.95, 3.95, 4.2), (5.0, -3.0, 2.5, -2.0, -6.0),
             (0.0, 0.08, -0.04, 0.08, 0.0), 0.3, 0.4, orange)
    add_word(canvas, silhouette, "CLASH", font, (0.3, -2.72), (4.75, 4.1, 4.1, 4.1, 4.45), (-5.0, 3.0, -2.0, 3.0, 6.0),
             (0.0, 0.1, -0.02, 0.1, 0.0), 0.3, -0.3, blue)
    add_model(canvas, silhouette, crate, (6.85, 2.85), 8)
    add_model(canvas, silhouette, gold, (-7.1, -1.55), 8)
    add_model(canvas, silhouette, cash, (7.55, -2.8), 7)

    # A white edge around the whole logo and a soft shadow keep it readable over a busy background.
    rim = dilate(silhouette, 13 * SUPER)
    shadow = blur(np.roll(shrink_half(rim), 12, axis=0), 12.0)
    result = blank(LOGO_WIDTH, LOGO_HEIGHT, NAVY)
    result[..., 3] = np.clip(shadow, 0.0, 1.0) * 0.4
    paint(result, WHITE, shrink_half(rim))
    paint(result, canvas[..., :3], canvas[..., 3])
    write_png(FOLDER / "UI_Logo.png", result)


# ---- Icons ----

def make_icons():
    with tempfile.TemporaryDirectory() as folder:
        # Astra's gold case, which reads well on the orange Practice button.
        icon = render_astra_cargo("Gold", (40.0, -26.0, 8.0), 512, 1.08, folder)
        write_png(FOLDER / "Icon_Cargo.png", with_outline(icon, 6))
        # The truck's front points along +Y, so turning it this far shows its side with the cab on the right.
        icon = render_truck((16.0, 62.0, 0.0), 512, 9.8, folder)
        write_png(FOLDER / "Icon_Truck.png", with_outline(icon, 6))


# ---- The app icon ----

APP_ICON = 1024
ORANGE_TEAM = ((1.0, 0.72, 0.28), (0.95, 0.42, 0.08))
BLUE_TEAM = ((0.36, 0.7, 1.0), (0.1, 0.34, 0.86))


def render_icon_scene(size, folder):
    """Astra's goose crate with its lid off and the goose looking out, in front of the two sides' tools crossed like
    swords: the Interrupters' hammer and the Collectors' vacuum."""
    modeling.reset_scene()
    setup_render(size, 2.05, samples=64)
    import_astra_cargo("Art", lid=None, lift=0.27, grow=1.3)
    tools = import_tools()
    # Both are upright in their file, the hammer's head and the vacuum's mouth facing the viewer. A quarter turn shows
    # their sides; then they lean opposite ways and cross behind the crate.
    hammer, vacuum = [tools["Hammer"]], [tools["Vacuum"]]
    move(hammer, turn=(0.0, 0.0, 90.0), scale=1.45)
    move(hammer, offset=(-0.02, 0.42, 0.56), turn=(0.0, -40.0, 0.0))
    move(vacuum, turn=(0.0, 0.0, 90.0), scale=1.55)
    move(vacuum, offset=(0.04, 0.5, 0.6), turn=(0.0, -40.0, 0.0))
    turn_to_camera((14.0, -16.0, 0.0), (0.0, 0.0, 0.55))
    return render_to_array(folder)


def resized(image, width, height):
    """Area-averaged when shrinking, so a rendered model stays crisp at any size."""
    old_height, old_width = image.shape[:2]
    premultiplied = image.copy()
    premultiplied[..., :3] *= premultiplied[..., 3:4]
    rows = np.minimum((np.arange(height + 1) * old_height / height).astype(np.int64), old_height)
    columns = np.minimum((np.arange(width + 1) * old_width / width).astype(np.int64), old_width)
    summed = np.zeros((old_height + 1, old_width + 1, 4), np.float64)
    summed[1:, 1:] = premultiplied.cumsum(axis=0).cumsum(axis=1)
    top, bottom = rows[:-1], np.maximum(rows[1:], rows[:-1] + 1)
    left, right = columns[:-1], np.maximum(columns[1:], columns[:-1] + 1)
    area = ((bottom - top)[:, None] * (right - left)[None, :])[..., None]
    total = (summed[bottom][:, right] - summed[top][:, right] - summed[bottom][:, left] + summed[top][:, left]) / area
    result = total.astype(np.float32)
    result[..., :3] /= np.maximum(result[..., 3:4], 1e-4)
    return result


def icon_picture(model, inset, radius, shadow):
    """A rounded square in the two teams' colours, split by a zigzag, with a comic burst behind the model.
    `inset` is the empty margin round the square, which is what macOS expects (its template is 824 in 1024)."""
    size = APP_ICON
    body = size - 2 * inset
    unit = body / 824.0
    centre = size / 2.0
    xs, ys = grid(size, size)
    shape = box(xs, ys, centre, centre, body / 2.0, body / 2.0, radius)

    image = blank(size, size, (0.0, 0.0, 0.0))
    if shadow:
        image[..., 3] = np.clip(blur(np.roll(cover(shape), int(round(10 * unit)), axis=0), 14.0 * unit), 0.0, 1.0) * 0.38

    # Orange top left, blue bottom right, each a little lighter towards the top.
    u = (xs - centre) / body
    v = (ys - centre) / body
    across = (u + v) / math.sqrt(2.0)
    along = (u - v) / math.sqrt(2.0)
    zigzag = 0.055 * (2.0 * np.abs(2.0 * (along * 2.6 - np.floor(along * 2.6 + 0.5))) - 1.0)
    split = (across - zigzag) * body
    height = np.clip(v + 0.5, 0.0, 1.0)[..., None]
    orange = from_palette((0.0, 1.0), ORANGE_TEAM, height[..., 0])
    blue = from_palette((0.0, 1.0), BLUE_TEAM, height[..., 0])
    background = blank(size, size)
    paint(background, NAVY, np.ones((size, size), np.float32))
    paint(background, blue, np.ones((size, size), np.float32))
    paint(background, orange, cover(split + 4.0 * unit))
    # The navy seam along the zigzag.
    paint(background, NAVY, cover(np.abs(split) - 5.0 * unit))

    # The burst: a jagged star behind the crate.
    rng = np.random.default_rng(12)
    points = 13
    angle = np.arctan2(ys - centre, xs - centre) + 0.2
    sector = math.tau / points
    index = np.floor(np.mod(angle, math.tau) / sector).astype(np.int32) % points
    outer = (318.0 + rng.uniform(-22.0, 22.0, points)[index]) * unit
    burst_centre_y = centre + 12.0 * unit
    burst = np.hypot(xs - centre, ys - burst_centre_y) - star_radius(angle, sector, outer, 205.0 * unit)
    paint(background, NAVY, cover(burst - 9.0 * unit))
    paint(background, (0.96, 0.3, 0.14), cover(burst))
    paint(background, (1.0, 0.56, 0.12), cover(burst + 34.0 * unit))
    paint(background, (1.0, 0.8, 0.22), cover(burst + 74.0 * unit))

    # The model, as big as the square allows, a little below the middle to leave room for the goose's crown.
    picture = resized(model, int(round(model.shape[1] * 0.9 * body / model.shape[1])), int(round(model.shape[0] * 0.9 * body / model.shape[1])))
    top = int(round(centre - picture.shape[0] / 2.0 + 18.0 * unit))
    left = int(round(centre - picture.shape[1] / 2.0))
    region = background[top:top + picture.shape[0], left:left + picture.shape[1]]
    paint(region, picture[..., :3], picture[..., 3])

    # A navy rim, then a soft light along the top edge, like the game's buttons.
    rim = cover(shape)
    inside = cover(shape + 11.0 * unit)
    gloss = np.clip(1.0 - (ys - (centre - body / 2.0)) / (body * 0.16), 0.0, 1.0) * cover(shape + 16.0 * unit)
    paint(background, WHITE, gloss * 0.16)
    paint(image, NAVY, rim)
    paint(image, background[..., :3], inside)
    return image


def make_app_icon():
    """AppIcon.png for Windows (and Unity's default), nearly filling its square, and AppIcon_Mac.png on Apple's grid with
    its shadow, which CargoClashPlayerBuild puts into the Mac app."""
    with tempfile.TemporaryDirectory() as folder:
        model = with_outline(render_icon_scene(2000, folder), 11)
    write_png(FOLDER / "AppIcon.png", icon_picture(model, 22, 196, False))
    write_png(FOLDER / "AppIcon_Mac.png", icon_picture(model, 100, 185.4, True))


def make_ring():
    """The crosshair when it's on something."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    image[..., 3] = cover(np.abs(circle(xs, ys, 64, 64, 54)) - 6.5)
    return image


def star_radius(angle, sector, outer, inner):
    """How far out a star's straight edge is at each angle, with a point in the middle of every sector."""
    phi = np.abs(np.mod(angle, sector) - sector * 0.5)
    half = sector * 0.5
    bx, by = inner * math.cos(half), inner * math.sin(half)
    return (outer * by) / np.maximum(by * np.cos(phi) - (bx - outer) * np.sin(phi), 1e-4)


def star_distance(xs, ys, cx, cy, outer, inner, points, turn=0.0):
    """Roughly how far outside a star each pixel is."""
    angle = np.arctan2(ys - cy, xs - cx) + turn
    return np.hypot(xs - cx, ys - cy) - star_radius(angle, math.tau / points, outer, inner)


def make_star():
    """The stars you see when you're knocked over."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size, YELLOW)
    shape = star_distance(xs, ys, 64, 64, 60, 27, 5, 0.31)
    paint(image, NAVY, cover(shape))
    paint(image, YELLOW, cover(shape + 7.0))
    return image


def make_pow():
    """A comic impact flash. White, so the game can tint it, with a darker rim."""
    size = 256
    xs, ys = grid(size, size)
    image = blank(size, size)
    rng = np.random.RandomState(7)
    points = 11
    angle = np.arctan2(ys - 128, xs - 128)
    sector = math.tau / points
    index = np.floor(np.mod(angle, math.tau) / sector).astype(np.int32) % points
    outer = 94.0 + rng.uniform(0.0, 30.0, points)[index]
    shape = np.hypot(xs - 128, ys - 128) - star_radius(angle, sector, outer, 52.0)
    image[..., :3] = np.where(shape[..., None] > -9.0, 0.72, 1.0)
    image[..., 3] = cover(shape)
    return image


def make_chevron():
    """A chunky arrow pointing up, for things off the edge of the screen."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    shape = np.minimum(segment(xs, ys, 64, 30, 24, 88, 15), segment(xs, ys, 64, 30, 104, 88, 15))
    paint(image, NAVY, cover(shape - 7.0))
    paint(image, WHITE, cover(shape))
    return image


def make_diamond():
    """A marker for something worth finding. White, so the game can tint it."""
    size = 128
    xs, ys = grid(size, size)
    image = blank(size, size)
    shape = (np.abs(xs - 64) + np.abs(ys - 64) - 50) / 1.414 - 6.0
    paint(image, NAVY, cover(shape - 7.0))
    paint(image, WHITE, cover(shape))
    return image


def make_dot():
    """A blip on the map. White with a dark edge, so the game can tint it."""
    size = 64
    xs, ys = grid(size, size)
    image = blank(size, size)
    shape = circle(xs, ys, 32, 32, 22)
    paint(image, NAVY, cover(shape - 8.0))
    paint(image, WHITE, cover(shape))
    return image


def make_vignette():
    """Dark at the corners and clear in the middle, tinted by the game. It stretches over the whole screen."""
    size = 256
    xs, ys = grid(size, size)
    distance = np.hypot((xs - 128) / 128, (ys - 128) / 128)
    image = blank(size, size)
    image[..., 3] = smoothstep(0.55, 1.35, distance) ** 1.4
    return image


def make_speed_lines():
    """Streaks rushing past the edges of the view during a ram."""
    size = 512
    xs, ys = grid(size, size)
    rng = np.random.RandomState(11)
    angle = np.mod(np.arctan2(ys - 256, xs - 256), math.tau)
    radius = np.hypot(xs - 256, ys - 256) / 256
    count = 72
    index = np.floor(angle / math.tau * count).astype(np.int32) % count
    centre = (index + 0.5) * math.tau / count
    width = rng.uniform(0.15, 0.42, count)[index]
    start = rng.uniform(0.5, 0.82, count)[index]
    keep = rng.uniform(0.0, 1.0, count)[index] > 0.34
    across = np.abs(angle - centre) * count / math.tau * 2.0
    streak = smoothstep(width, width * 0.4, across) * smoothstep(start, start + 0.3, radius) * keep
    image = blank(size, size)
    image[..., 3] = streak * 0.9
    return image


def make_sprites():
    write_png(FOLDER / "UI_Ring.png", make_ring())
    write_png(FOLDER / "UI_Star.png", make_star())
    write_png(FOLDER / "UI_Pow.png", make_pow())
    write_png(FOLDER / "UI_Chevron.png", make_chevron())
    write_png(FOLDER / "UI_Diamond.png", make_diamond())
    write_png(FOLDER / "UI_Dot.png", make_dot())
    write_png(FOLDER / "UI_Vignette.png", make_vignette())
    write_png(FOLDER / "UI_SpeedLines.png", make_speed_lines())
    write_png(FOLDER / "UI_Round.png", make_round())
    write_png(FOLDER / "UI_Circle.png", make_circle())
    write_png(FOLDER / "UI_Frame.png", make_frame())
    write_png(FOLDER / "UI_Soft.png", make_soft())
    write_png(FOLDER / "UI_Lock.png", make_lock())
    write_png(FOLDER / "UI_Back.png", make_back_arrow())


# ---- The main menu (Assets/Game/Editor/CargoClashBuilder.Title.cs, BuildMenuPanel) ----

# ---- The truck's livery: the main menu's buttons are painted like the truck behind them (step 25) ----

GOLD = (0.886, 0.663, 0.227)
BRASS = (0.706, 0.498, 0.133)
LENS = (1.0, 0.945, 0.847)


def make_plate():
    """UI_Round's corner again, imported with mip maps: a plate's rim, trim and panel shrink it to 24, 19 and 15 pixels
    round, and without mip maps such small corners come out jagged."""
    return make_round()


def make_plate_shade():
    """Laid over a plate's panel with the same corners: a light line along the top and a darker band along the bottom
    (12 and 20 of its pixels), so a flat panel reads as a slightly rounded one."""
    size = 128
    xs, ys = grid(size, size)
    inside = cover(box(xs, ys, 64, 64, 64, 64, 44))
    top = inside * (1.0 - cover(box(xs, ys - 12.0, 64, 64, 64, 64, 44)))
    bottom = inside * (1.0 - cover(box(xs, ys + 20.0, 64, 64, 64, 64, 44)))
    image = blank(size, size)
    image[..., :3] = np.where(bottom[..., None] > top[..., None], 0.0, 1.0)
    image[..., 3] = top * 0.22 + bottom * 0.18
    return image


def make_pinstripe():
    """The thin cream line inside a panel: 20 pixels round and 4 thick, nine-sliced with a border of 24, so at a corner
    multiplier of about 2 it is a 2-pixel line round a 9-pixel corner."""
    size = 64
    xs, ys = grid(size, size)
    shape = box(xs, ys, 32, 32, 32, 32, 20)
    image = blank(size, size)
    image[..., 3] = cover(shape) * (1.0 - cover(shape + 4.0))
    return image


def make_headlight():
    """The truck's headlight, which holds a button's picture: a gold ring with a darker inner edge round a cream lens,
    brightest up and to the left."""
    size = 256
    xs, ys = grid(size, size)
    radius = np.hypot(xs - 128.0, ys - 128.0)
    image = blank(size, size, GOLD)
    image[..., 3] = cover(radius - 126.0)
    brass = cover(radius - 109.0)[..., None]
    image[..., :3] = image[..., :3] * (1.0 - brass) + np.asarray(BRASS, np.float32) * brass
    lens = cover(radius - 102.0)[..., None]
    t = np.clip(np.hypot(xs - 108.0, ys - 97.0) / 112.0, 0.0, 1.0)[..., None]
    glass = np.asarray(WHITE, np.float32) * (1.0 - t) + np.asarray(LENS, np.float32) * t
    image[..., :3] = image[..., :3] * (1.0 - lens) + glass * lens
    return image


def make_livery():
    write_png(FOLDER / "UI_Plate.png", make_plate())
    write_png(FOLDER / "UI_PlateShade.png", make_plate_shade())
    write_png(FOLDER / "UI_Pinstripe.png", make_pinstripe())
    write_png(FOLDER / "UI_Headlight.png", make_headlight())


def render_crew_hat(size, folder):
    """The porters' hard hat from the medium body (Crew_Medium.fbx), in the game's hat colours, tipped toward the viewer."""
    modeling.reset_scene()
    setup_render(size, 0.62)
    objects = import_fbx(ROOT / "Assets/Game/Models/Crew_Medium.fbx")
    for item in list(objects):
        if item.type == "MESH" and item.name.split(".")[0] != "Porter_Hat_classic":
            objects.remove(item)
            bpy.data.objects.remove(item)
    for item in objects:
        for modifier in list(item.modifiers):
            item.modifiers.remove(modifier)
    colours = {"Crew_HatPrimary": ((0.95, 0.72, 0.25), 0.45, 0.0), "Crew_HatTrim": ((0.66, 0.49, 0.2), 0.5, 0.0),
               "Crew_Light": ((0.86, 0.82, 0.71), 0.6, 0.0), "Char_Team": ((0.95, 0.5, 0.15), 0.6, 0.0),
               "Crew_Lamp": ((1.0, 0.95, 0.75), 0.3, 0.0), "Crew_Brass": ((0.67, 0.58, 0.4), 0.4, 0.4)}
    paint_materials(lambda name: (tuple(linear(c) for c in colours[name][0]), colours[name][1], colours[name][2]) if name in colours else None)
    meshes = flatten(objects)
    lows = [min((item.matrix_world @ Vector(corner)).z for corner in item.bound_box) for item in meshes]
    highs = [max((item.matrix_world @ Vector(corner)).z for corner in item.bound_box) for item in meshes]
    turn_to_camera((22.0, 32.0, 0.0), (0.0, 0.0, (min(lows) + max(highs)) / 2.0))
    return render_to_array(folder)


def solid(name, mesh, colour, roughness=0.5, metallic=0.0, bevel=0.03):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*(linear(c) for c in colour), 1.0)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    item = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(item)
    item.data.materials.append(material)
    if bevel > 0.0:
        modifier = item.modifiers.new("Rounded", "BEVEL")
        modifier.width, modifier.segments, modifier.limit_method = bevel, 4, "ANGLE"
    for polygon in item.data.polygons:
        polygon.use_smooth = True
    return item


def extruded(points, depth):
    """A flat shape from its outline, made `depth` thick, standing on the XY plane."""
    import bmesh
    mesh = bpy.data.meshes.new("Shape")
    bm = bmesh.new()
    bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in points])
    extrude = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
    bmesh.ops.translate(bm, vec=(0.0, 0.0, depth), verts=[v for v in extrude["geom"] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def render_gear(size, folder):
    """A chunky eight-toothed cog with a hub, for SETTINGS."""
    modeling.reset_scene()
    setup_render(size, 2.6)
    teeth, points = 8, []
    for step in range(teeth * 24):
        u = (step % 24) / 24.0
        lift = max(0.0, min(1.0, min((u - 0.08) / 0.14, (0.62 - u) / 0.14)))
        radius = 0.82 + 0.26 * lift
        angle = step * math.tau / (teeth * 24)
        points.append((radius * math.cos(angle), radius * math.sin(angle)))
    solid("Cog", extruded(points, 0.32), (0.86, 0.88, 0.9), 0.35, 0.35, bevel=0.06)
    hub = bpy.data.meshes.new("Hub")
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=0.42, radius2=0.42, depth=0.16)
    bmesh.ops.translate(bm, vec=(0.0, 0.0, 0.38), verts=bm.verts)
    bm.to_mesh(hub)
    bm.free()
    solid("Hub", hub, (1.0, 0.72, 0.2), 0.4, 0.2, bevel=0.04)
    turn_to_camera((58.0, 0.0, 12.0), (0.0, 0.0, 0.2))
    return render_to_array(folder)


def render_power(size, folder):
    """The power sign standing up, for QUIT: a ring open at the top, with a bar through the gap."""
    modeling.reset_scene()
    setup_render(size, 2.7)
    ring = []
    for step in range(97):
        angle = math.radians(120.0 + step * 300.0 / 96.0)
        ring.append((0.0 + 0.78 * math.cos(angle), 0.0, 0.78 * math.sin(angle)))
    part = modeling.MeshPart("Power")
    part.tube(ring, 0.17, "Sign", points=16, smooth=False)
    part.along((0.0, 0.0, 0.08), (0.0, 0.0, 1.0), 0.17, 0.17, "Sign", points=16)
    material = bpy.data.materials.new("Sign")
    paint_one(material, (1.0, 0.95, 0.88), 0.45, 0.0)
    sign = part.to_object({"Sign": material})
    for polygon in sign.data.polygons:
        polygon.use_smooth = True
    turn_to_camera((12.0, 18.0, 0.0), (0.0, 0.0, 0.1))
    return render_to_array(folder)


def paint_one(material, colour, roughness, metallic):
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*(linear(c) for c in colour), 1.0)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic


def make_menu():
    with tempfile.TemporaryDirectory() as folder:
        write_png(FOLDER / "Icon_Hat.png", with_outline(render_crew_hat(512, folder), 6))
        write_png(FOLDER / "Icon_Gear.png", with_outline(render_gear(512, folder), 6))
        write_png(FOLDER / "Icon_Power.png", with_outline(render_power(512, folder), 6))


def make_cards():
    write_png(FOLDER / "Level02_Card.png", card_forest())
    write_png(FOLDER / "Level03_Card.png", card_mountain())
    # Level04_Card.png is the river level's real picture now, rendered by CargoClashBuilder.River.cs.
    write_png(FOLDER / "Level05_Card.png", card_unknown((0.42, 0.26, 0.62), (0.86, 0.46, 0.62), (0.3, 0.16, 0.46), 5))
    write_png(FOLDER / "Level06_Card.png", card_unknown((0.12, 0.42, 0.56), (0.42, 0.82, 0.68), (0.06, 0.28, 0.38), 6))


def main():
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    groups = {"sprites": make_sprites, "cards": make_cards, "logo": make_logo, "icons": make_icons, "appicon": make_app_icon,
              "menu": make_menu, "livery": make_livery}
    for name, make in groups.items():
        if only is None or only == name:
            make()


if __name__ == "__main__":
    main()

"""Paints the ground of level 1 and writes the textures into Assets/Game/Textures.

Blender is only here for its Python with numpy. Run from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/make_ground.py

Level01_Ground.png covers the playable area. Level01_Far.png covers everything around it, and
Level01_RoadTile.png repeats along the road outside the playable area. TruckZone.png is the yellow
area beside the truck. The skid marks and torn-up ground are painted to meet the truck, so run this
again if the truck moves (TRUCK and TRUCK_YAW below, which match CargoClashBuilder.BuildLevelScene).

Title_Grass.png, Title_Road.png, Title_Fields.png and Title_Sky.png are the start screen's endless countryside.
Add `-- --title` at the end to paint only those.
"""
import math
import struct
import sys
import zlib
from pathlib import Path

import numpy as np

HALF_X = 23.0
HALF_Z = 49.0
ROAD_HALF = 5.0
SHOULDER_HALF = 6.6
TRUCK = (11.0, -33.0)
TRUCK_YAW = 10.0
# The far texture covers this much of the world: west, east, south, north.
FAR_AREA = (-250.0, 150.0, -200.0, 200.0)

GRASS_DARK = np.array((0.34, 0.56, 0.25), np.float32)
GRASS_LIGHT = np.array((0.43, 0.65, 0.29), np.float32)
MEADOW = np.array((0.31, 0.5, 0.25), np.float32)
MEADOW_DARK = np.array((0.27, 0.45, 0.24), np.float32)
MEADOW_LIGHT = np.array((0.36, 0.55, 0.27), np.float32)
DIRT_DARK = np.array((0.6, 0.5, 0.34), np.float32)
DIRT_LIGHT = np.array((0.71, 0.61, 0.43), np.float32)
ASPHALT = np.array((0.29, 0.3, 0.33), np.float32)
PATCH = np.array((0.22, 0.23, 0.25), np.float32)
PAINT = np.array((0.95, 0.95, 0.87), np.float32)
RUBBER = np.array((0.09, 0.09, 0.1), np.float32)
EARTH = np.array((0.42, 0.31, 0.2), np.float32)
EARTH_DARK = np.array((0.27, 0.19, 0.12), np.float32)
OIL = np.array((0.08, 0.08, 0.1), np.float32)
ZONE = np.array((1.0, 0.85, 0.15), np.float32)
ZONE_STRIPE = np.array((0.12, 0.11, 0.12), np.float32)


def smoothstep(low, high, value):
    t = np.clip((value - low) / (high - low), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def noise(shape, cell, rng, wrap_rows=False, wrap_columns=False):
    """Smooth random values from 0 to 1. `cell` is the size of one blob in pixels."""
    height, width = shape
    grid_height = max(int(round(height / cell)), 1)
    grid_width = max(int(round(width / cell)), 1)
    grid = rng.random((grid_height + 2, grid_width + 2), dtype=np.float32)
    if wrap_rows:
        grid[grid_height:] = grid[:2]
    if wrap_columns:
        grid[:, grid_width:] = grid[:, :2]
    rows = np.arange(height, dtype=np.float32) * grid_height / height
    columns = np.arange(width, dtype=np.float32) * grid_width / width
    row0 = rows.astype(np.int32)
    column0 = columns.astype(np.int32)
    row_t = rows - row0
    column_t = columns - column0
    row_t = (row_t * row_t * (3.0 - 2.0 * row_t))[:, None]
    column_t = column_t * column_t * (3.0 - 2.0 * column_t)
    upper = grid[row0][:, column0] * (1.0 - column_t) + grid[row0][:, column0 + 1] * column_t
    lower = grid[row0 + 1][:, column0] * (1.0 - column_t) + grid[row0 + 1][:, column0 + 1] * column_t
    return upper * (1.0 - row_t) + lower * row_t


def layered(shape, cell, rng, octaves=3, wrap_rows=False, wrap_columns=False):
    total = np.zeros(shape, np.float32)
    weight = 1.0
    weights = 0.0
    for _ in range(octaves):
        total += noise(shape, max(cell, 2.0), rng, wrap_rows, wrap_columns) * weight
        weights += weight
        weight *= 0.5
        cell /= 2.0
    return total / weights


def mix(image, colour, alpha):
    """`colour` is one colour or an image of colours, `alpha` an image of amounts."""
    image += (colour - image) * alpha[..., None]


def blend_colours(low, high, amount):
    return low + (high - low) * amount[..., None]


class Painting:
    """An image with world coordinates for every pixel. Row 0 is the far (+Z) edge."""

    def __init__(self, width, height, west, east, south, north):
        self.width = width
        self.height = height
        self.west = west
        self.east = east
        self.south = south
        self.north = north
        self.per_x = width / (east - west)
        self.per_z = height / (north - south)
        xs = west + (np.arange(width, dtype=np.float32) + 0.5) / self.per_x
        zs = north - (np.arange(height, dtype=np.float32) + 0.5) / self.per_z
        self.x, self.z = np.meshgrid(xs, zs)
        self.shape = (height, width)
        self.image = np.zeros((height, width, 3), np.float32)

    def to_pixel(self, x, z):
        return (x - self.west) * self.per_x, (self.north - z) * self.per_z

    def stamp_line(self, mask, points, radius, strength):
        """Draws a soft line through (x, z) points into `mask`. `strength` is one value per point."""
        radius_px = radius * self.per_x
        reach = int(math.ceil(radius_px + 1.5))
        for i in range(len(points) - 1):
            start = self.to_pixel(*points[i])
            end = self.to_pixel(*points[i + 1])
            steps = max(int(max(abs(end[0] - start[0]), abs(end[1] - start[1]))), 1)
            for step in range(steps):
                t = step / steps
                column = start[0] + (end[0] - start[0]) * t
                row = start[1] + (end[1] - start[1]) * t
                amount = strength[i] + (strength[i + 1] - strength[i]) * t
                c0, r0 = int(column), int(row)
                left, right = max(c0 - reach, 0), min(c0 + reach + 1, self.width)
                top, bottom = max(r0 - reach, 0), min(r0 + reach + 1, self.height)
                if left >= right or top >= bottom:
                    continue
                ys, xs = np.mgrid[top:bottom, left:right]
                distance = np.hypot(xs + 0.5 - column, ys + 0.5 - row)
                soft = np.clip(radius_px + 0.5 - distance, 0.0, 1.0) * amount
                mask[top:bottom, left:right] = np.maximum(mask[top:bottom, left:right], soft)


def spline(keys, per_span=80):
    """A smooth path through (x, z) points."""
    keys = np.array(keys, np.float32)
    points = []
    for i in range(len(keys) - 1):
        a, b, c, d = keys[max(i - 1, 0)], keys[i], keys[i + 1], keys[min(i + 2, len(keys) - 1)]
        for step in range(per_span):
            t = step / per_span
            points.append(0.5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (3 * b - a - 3 * c + d) * t * t * t))
    points.append(keys[-1])
    return np.array(points, np.float32)


def side_path(path, offset):
    """The same path, moved sideways. Used for the second wheel and the streaks in a tyre mark."""
    direction = np.gradient(path, axis=0)
    direction /= np.linalg.norm(direction, axis=1, keepdims=True) + 1e-6
    normal = np.stack((direction[:, 1], -direction[:, 0]), axis=1)
    return path + normal * offset


def brush_strokes(paint, rng, count, length, spacing_mask, amplitude, wrap=False):
    """Thousands of short strokes, so the grass looks painted. Returns a brightness change."""
    change = np.zeros(paint.shape, np.float32)
    rows = rng.integers(0, paint.height, count)
    columns = rng.integers(0, paint.width, count)
    keep = spacing_mask[rows, columns]
    rows, columns = rows[keep], columns[keep]
    angles = rng.normal(math.pi / 2, 0.6, len(rows))
    amounts = rng.choice((-1.0, 0.6, 1.0), len(rows)).astype(np.float32) * amplitude * rng.uniform(0.5, 1.0, len(rows))
    for step in range(length):
        r = (rows - np.sin(angles) * step).astype(np.int32)
        c = (columns + np.cos(angles) * step).astype(np.int32)
        if wrap:
            r %= paint.height
            c %= paint.width
        inside = (r >= 0) & (r < paint.height) & (c >= 0) & (c < paint.width)
        change[r[inside], c[inside]] = amounts[inside]
    return change


def scatter_dots(paint, rng, count, radius_range, mask):
    """Round pebbles or specks. Returns an array of how much each pixel is covered, from -1 to 1."""
    dots = np.zeros(paint.shape, np.float32)
    rows = rng.integers(0, paint.height, count)
    columns = rng.integers(0, paint.width, count)
    keep = mask[rows, columns]
    for row, column in zip(rows[keep], columns[keep]):
        radius = rng.uniform(*radius_range)
        reach = int(radius + 2)
        top, bottom = max(row - reach, 0), min(row + reach + 1, paint.height)
        left, right = max(column - reach, 0), min(column + reach + 1, paint.width)
        ys, xs = np.mgrid[top:bottom, left:right]
        soft = np.clip(radius + 0.5 - np.hypot(xs - column, ys - row), 0.0, 1.0)
        dots[top:bottom, left:right] = np.where(soft > 0, soft * rng.choice((-1.0, 1.0)), dots[top:bottom, left:right])
    return dots


def paint_markings(image, x, z, wear, period_offset=-46.0):
    """White lines. Dashes sit every 6 metres, centred on `period_offset`."""
    edge = smoothstep(0.1, 0.075, np.abs(np.abs(x) - (ROAD_HALF - 0.45)))
    phase = np.mod(z - period_offset + 3.0, 6.0) - 3.0
    dash = smoothstep(0.17, 0.14, np.abs(x)) * smoothstep(1.43, 1.38, np.abs(phase))
    mix(image, PAINT, np.maximum(edge, dash) * (0.66 + 0.34 * smoothstep(0.32, 0.58, wear)))


def paint_play_area(rng):
    paint = Painting(2048, 4096, -HALF_X, HALF_X, -HALF_Z, HALF_Z)
    x, z, shape = paint.x, paint.z, paint.shape
    per_metre = paint.per_x
    abs_x = np.abs(x)

    # Grass: broad patches, mown stripes along the road, and brush strokes.
    patches = layered(shape, 9.0 * per_metre, rng, 3)
    colours = blend_colours(GRASS_DARK, GRASS_LIGHT, smoothstep(0.34, 0.66, patches))
    stripes = smoothstep(-0.35, 0.35, np.sin((abs_x - SHOULDER_HALF) * math.pi / 3.1))
    colours *= (0.96 + 0.08 * stripes)[..., None]
    colours *= (0.965 + 0.07 * layered(shape, 1.4 * per_metre, rng, 2))[..., None]
    strokes = brush_strokes(paint, rng, 170000, 13, abs_x > SHOULDER_HALF - 0.5, 0.085)
    colours *= (1.0 + strokes)[..., None]
    paint.image[:] = colours

    # Gravel shoulder with a rough edge and tufts of grass reaching into it.
    rough = layered(shape, 2.4 * per_metre, rng, 3) - 0.5
    shoulder = smoothstep(0.07, -0.07, abs_x - (SHOULDER_HALF + rough * 0.95))
    dirt = blend_colours(DIRT_DARK, DIRT_LIGHT, layered(shape, 0.8 * per_metre, rng, 3))
    pebbles = scatter_dots(paint, rng, 70000, (1.0, 3.2), shoulder > 0.5)
    dirt *= (1.0 + 0.14 * pebbles)[..., None]
    mix(paint.image, dirt, shoulder)

    # Asphalt: mottled, polished along the wheel tracks, with a few repairs and cracks.
    edge_wobble = layered(shape, 1.1 * per_metre, rng, 2) - 0.5
    road = smoothstep(0.03, -0.03, abs_x - (ROAD_HALF + edge_wobble * 0.14))
    tarmac = np.broadcast_to(ASPHALT, paint.image.shape).copy()
    tarmac *= (0.95 + 0.1 * layered(shape, 6.0 * per_metre, rng, 3))[..., None]
    tarmac *= (0.97 + 0.06 * rng.random(shape, dtype=np.float32))[..., None]
    tracks = np.exp(-((abs_x - 2.45) / 0.75) ** 2)
    tarmac *= (1.0 + 0.055 * tracks)[..., None]
    for centre_x, centre_z, half_x, half_z in ((-2.6, 31.0, 1.2, 2.2), (2.7, 9.0, 1.0, 1.7), (-2.9, -42.0, 1.3, 2.4)):
        wobble = (layered(shape, 0.9 * per_metre, rng, 2) - 0.5) * 0.3
        repair = smoothstep(0.03, -0.03, np.maximum(np.abs(x - centre_x) - half_x, np.abs(z - centre_z) - half_z) + wobble)
        mix(tarmac, PATCH * 1.1, repair * 0.6)
    cracks = np.zeros(shape, np.float32)
    for _ in range(16):
        position = np.array((rng.uniform(-4.4, 4.4), rng.uniform(-48.0, 48.0)), np.float32)
        heading = rng.uniform(0, math.tau)
        points = []
        strength = []
        steps = int(rng.uniform(16, 44))
        for step in range(steps):
            points.append(position.copy())
            strength.append(0.7 * min(1.0, (steps - step) / 6.0))
            heading += rng.normal(0.0, 0.32)
            position = position + np.array((math.cos(heading), math.sin(heading)), np.float32) * 0.12
        paint.stamp_line(cracks, points, 0.024, strength)
    mix(tarmac, PATCH * 0.6, cracks)
    mix(paint.image, tarmac, road)
    paint_markings(paint.image, x, z, layered(shape, 0.7 * per_metre, rng, 3))

    # The crash: earth torn up around the truck, tyre marks that lead to it, and spilt oil.
    yaw = math.radians(TRUCK_YAW)
    local_x = (x - TRUCK[0]) * math.cos(yaw) - (z - TRUCK[1]) * math.sin(yaw)
    local_z = (x - TRUCK[0]) * math.sin(yaw) + (z - TRUCK[1]) * math.cos(yaw)
    scar = 1.0 - np.hypot(local_x / 4.6, local_z / 7.0)
    scar += (layered(shape, 1.7 * per_metre, rng, 4) - 0.5) * 0.85
    torn = blend_colours(EARTH_DARK, EARTH, layered(shape, 0.6 * per_metre, rng, 3))
    mix(paint.image, torn, smoothstep(0.0, 0.1, scar) * 0.8)

    rear_axle = (TRUCK[0] + 2.9 * math.sin(yaw), TRUCK[1] + 2.9 * math.cos(yaw))
    path = spline(((-0.8, 7.0), (-0.2, -3.0), (1.6, -13.0), (5.1, -22.0), rear_axle), per_span=24)
    fade = np.clip(np.arange(len(path)) / (len(path) * 0.36), 0.0, 1.0) ** 1.5
    off_road = smoothstep(ROAD_HALF, SHOULDER_HALF, abs_x)
    grass_only = smoothstep(SHOULDER_HALF - 0.4, SHOULDER_HALF + 0.3, abs_x)
    for wheel in (-2.3, 2.3):
        rut = np.zeros(shape, np.float32)
        paint.stamp_line(rut, side_path(path, wheel), 0.33, fade)
        lumpy = 0.62 + 0.6 * layered(shape, 0.35 * per_metre, rng, 2)
        mix(paint.image, EARTH_DARK, np.clip(rut * lumpy, 0.0, 1.0) * off_road * 0.85)
        rubber = np.zeros(shape, np.float32)
        for streak in (-0.21, -0.1, 0.0, 0.1, 0.21):
            flicker = np.interp(np.arange(len(path)), np.linspace(0, len(path), 34), rng.uniform(0.5, 1.0, 34))
            paint.stamp_line(rubber, side_path(path, wheel + streak), 0.045, fade * flicker)
        mix(paint.image, RUBBER, np.clip(rubber * 1.1, 0.0, 1.0) * (1.0 - grass_only) * 0.88)

    # The groove the front corner dug, from where the wheel came off to where the wheel ended up.
    hub = (TRUCK[0] + 2.36 * math.cos(yaw) - 2.75 * math.sin(yaw), TRUCK[1] - 2.36 * math.sin(yaw) - 2.75 * math.cos(yaw))
    wheel_rest = (TRUCK[0] + 3.9 * math.cos(yaw) - 6.5 * math.sin(yaw), TRUCK[1] - 3.9 * math.sin(yaw) - 6.5 * math.cos(yaw))
    groove = np.zeros(shape, np.float32)
    trail = spline((hub, ((hub[0] + wheel_rest[0]) / 2 + 0.35, (hub[1] + wheel_rest[1]) / 2), wheel_rest), per_span=12)
    paint.stamp_line(groove, trail, 0.26, np.ones(len(trail)))
    mix(paint.image, EARTH_DARK, groove * 0.7)

    oil_spots = ((9.6, -38.2, 1.9), (7.7, -39.8, 1.1), (10.8, -36.2, 1.2), (6.6, -36.9, 0.5), (8.8, -41.4, 0.45))
    blobs = layered(shape, 1.1 * per_metre, rng, 3) - 0.5
    for spot_x, spot_z, size in oil_spots:
        distance = np.hypot(x - spot_x, (z - spot_z) * 1.25) / size
        stain = smoothstep(0.1, -0.08, distance - 1.0 + blobs * 0.8)
        mix(paint.image, OIL, stain * 0.78)

    # Fade every edge into the meadow outside, except where the road carries on.
    fade_x = smoothstep(0.0, 1.8, HALF_X - abs_x)
    fade_z = np.maximum(smoothstep(0.0, 1.8, HALF_Z - np.abs(z)), smoothstep(SHOULDER_HALF + 1.0, SHOULDER_HALF + 0.3, abs_x))
    mix(paint.image, MEADOW, 1.0 - fade_x * fade_z)
    return paint.image


def paint_far(rng):
    west, east, south, north = FAR_AREA
    paint = Painting(2048, 2048, west, east, south, north)
    x, z, shape = paint.x, paint.z, paint.shape
    per_metre = paint.per_x

    meadow = blend_colours(MEADOW_DARK, MEADOW_LIGHT, smoothstep(0.32, 0.68, layered(shape, 26.0 * per_metre, rng, 4)))
    meadow *= (0.97 + 0.06 * layered(shape, 2.5 * per_metre, rng, 2))[..., None]
    paint.image[:] = meadow

    # Fields with crops, a few degrees off the road's direction, with farm tracks between them.
    turn = math.radians(5.0)
    along = x * math.cos(turn) + z * math.sin(turn)
    across = -x * math.sin(turn) + z * math.cos(turn)
    crops = (
        (np.array((0.83, 0.74, 0.37), np.float32), np.array((0.76, 0.66, 0.32), np.float32)),
        (np.array((0.46, 0.64, 0.28), np.float32), np.array((0.37, 0.55, 0.25), np.float32)),
        (np.array((0.53, 0.41, 0.28), np.float32), np.array((0.44, 0.33, 0.22), np.float32)),
        (np.array((0.62, 0.72, 0.31), np.float32), np.array((0.53, 0.65, 0.28), np.float32)),
    )
    wobble = (layered(shape, 7.0 * per_metre, rng, 3) - 0.5) * 3.2
    field_width = 46.0
    field_length = 58.0
    column = np.floor((along + wobble) / field_width).astype(np.int32)
    row = np.floor((across + wobble) / field_length).astype(np.int32)
    table = rng.integers(0, len(crops) + 2, (64, 64))
    choice = table[row % 64, column % 64]
    for index, (light, dark) in enumerate(crops):
        rows = 0.5 + 0.5 * np.sin(along * math.tau / (2.4 + index * 0.5))
        colours = blend_colours(dark, light, smoothstep(0.25, 0.75, rows))
        colours *= (0.96 + 0.08 * layered(shape, 9.0 * per_metre, rng, 2))[..., None]
        mix(paint.image, colours, (choice == index).astype(np.float32))
    edge_along = np.abs(np.mod(along + wobble + field_width / 2, field_width) - field_width / 2)
    edge_across = np.abs(np.mod(across + wobble + field_length / 2, field_length) - field_length / 2)
    tracks = smoothstep(1.5, 0.9, np.minimum(edge_along, edge_across))
    mix(paint.image, DIRT_DARK, tracks * 0.85)

    # Plain meadow near the play area and the road, so the two textures meet without a seam.
    near_area = np.maximum(np.abs(x) - HALF_X - 11.0, np.abs(z) - HALF_Z - 11.0)
    near_area += (layered(shape, 12.0 * per_metre, rng, 3) - 0.5) * 12.0
    mix(paint.image, meadow, smoothstep(5.0, -1.0, near_area))
    near_road = smoothstep(10.2, 13.0, np.abs(x))
    near_plate = smoothstep(0.5, 3.5, np.maximum(np.abs(x) - HALF_X, np.abs(z) - HALF_Z))
    mix(paint.image, MEADOW, 1.0 - near_road * near_plate)
    return paint.image


def paint_road_tile(rng, verges_clear=False):
    """Ten metres either side of the road and one 6-metre repeat of its dashes.

    With `verges_clear`, the grass beside the shoulder is see-through, so the road can lie on any ground.
    """
    width, height = 1024, 256
    x = (np.arange(width, dtype=np.float32) + 0.5) / width * 20.0 - 10.0
    z = (np.arange(height, dtype=np.float32) + 0.5) / height * 6.0
    x, z = np.meshgrid(x, z)
    abs_x = np.abs(x)
    shape = (height, width)
    per_metre = width / 20.0

    image = np.broadcast_to(MEADOW, (height, width, 3)).copy()
    grass = blend_colours(MEADOW_DARK, MEADOW_LIGHT, layered(shape, 2.0 * per_metre, rng, 2, wrap_rows=True))
    mix(image, grass, smoothstep(9.8, 8.4, abs_x) * 0.7)

    rough = layered(shape, 1.7 * per_metre, rng, 2, wrap_rows=True) - 0.5
    shoulder = smoothstep(0.07, -0.07, abs_x - (SHOULDER_HALF + rough * 0.75))
    dirt = blend_colours(DIRT_DARK, DIRT_LIGHT, layered(shape, 0.5 * per_metre, rng, 3, wrap_rows=True))
    mix(image, dirt, shoulder)

    tarmac = np.broadcast_to(ASPHALT, (height, width, 3)).copy()
    tarmac *= (0.97 + 0.06 * layered(shape, 1.8 * per_metre, rng, 2, wrap_rows=True))[..., None]
    tarmac *= (0.97 + 0.06 * rng.random(shape, dtype=np.float32))[..., None]
    tarmac *= (1.0 + 0.055 * np.exp(-((abs_x - 2.45) / 0.75) ** 2))[..., None]
    road = smoothstep(0.03, -0.03, abs_x - ROAD_HALF)
    mix(image, tarmac, road)
    # Dash centre in the middle of the tile, which is where the strip's texture coordinates put it.
    paint_markings(image, x, z, layered(shape, 0.5 * per_metre, rng, 3, wrap_rows=True), period_offset=3.0)
    if verges_clear:
        return np.concatenate((image, shoulder[..., None]), axis=2)
    return image


def paint_zone():
    """Soft yellow with a hazard-striped edge, with alpha."""
    width, height = 512, 716
    x = (np.arange(width, dtype=np.float32) + 0.5) / width * 10.0
    z = (np.arange(height, dtype=np.float32) + 0.5) / height * 14.0
    x, z = np.meshgrid(x, z)
    edge = np.minimum(np.minimum(x, 10.0 - x), np.minimum(z, 14.0 - z))
    image = np.zeros((height, width, 4), np.float32)
    image[..., :3] = ZONE
    image[..., 3] = 0.2
    band = smoothstep(0.5, 0.47, edge)
    stripes = smoothstep(-0.06, 0.06, np.sin((x + z) * math.pi / 0.55))
    colours = blend_colours(ZONE_STRIPE, ZONE, stripes)
    image[..., :3] = image[..., :3] + (colours - image[..., :3]) * band[..., None]
    image[..., 3] = image[..., 3] + (0.86 - image[..., 3]) * band
    return image


def paint_title_grass(rng):
    """A 50-metre square for the start screen, which repeats in both directions without a seam."""
    size = 2048
    per_metre = size / 50.0
    paint = Painting(size, size, 0.0, 50.0, 0.0, 50.0)
    shape = paint.shape
    wrap = {"wrap_rows": True, "wrap_columns": True}
    image = blend_colours(GRASS_DARK, GRASS_LIGHT, smoothstep(0.34, 0.66, layered(shape, 9.0 * per_metre, rng, 3, **wrap)))
    image *= (0.965 + 0.07 * layered(shape, 1.4 * per_metre, rng, 2, **wrap))[..., None]
    image *= (1.0 + brush_strokes(paint, rng, 110000, 13, np.ones(shape, bool), 0.085, wrap=True))[..., None]
    return image


def paint_title_fields(rng):
    """Crops and hedges for the start screen's far distance. Repeats sideways every 184 metres."""
    width, height = 1472, 912
    west, east, south, north = 0.0, 184.0, 46.0, 160.0
    paint = Painting(width, height, west, east, south, north)
    x, z, shape = paint.x, paint.z, paint.shape
    per_metre = paint.per_x
    wrap = {"wrap_columns": True}

    crops = (
        (np.array((0.92, 0.8, 0.36), np.float32), np.array((0.84, 0.7, 0.3), np.float32)),
        (np.array((0.52, 0.72, 0.3), np.float32), np.array((0.42, 0.62, 0.27), np.float32)),
        (np.array((0.62, 0.46, 0.3), np.float32), np.array((0.52, 0.38, 0.24), np.float32)),
        (np.array((0.74, 0.8, 0.34), np.float32), np.array((0.64, 0.73, 0.3), np.float32)),
        (np.array((0.42, 0.62, 0.3), np.float32), np.array((0.36, 0.54, 0.27), np.float32)),
    )
    field_width = 46.0
    field_depth = 38.0
    wobble = (layered(shape, 7.0 * per_metre, rng, 3, **wrap) - 0.5) * 2.6
    column = np.floor((x + wobble) / field_width).astype(np.int32) % 4
    row = np.floor((z - south + wobble) / field_depth).astype(np.int32)
    table = rng.integers(0, len(crops), (8, 4))
    choice = table[row % 8, column]
    for index, (light, dark) in enumerate(crops):
        # Some fields are ploughed along the road and some across it, so they don't all look alike.
        lines = z if index % 2 else x
        rows = 0.5 + 0.5 * np.sin(lines * math.tau / (2.6 + index * 0.45))
        colours = blend_colours(dark, light, smoothstep(0.25, 0.75, rows))
        colours *= (0.96 + 0.08 * layered(shape, 9.0 * per_metre, rng, 2, **wrap))[..., None]
        mix(paint.image, colours, (choice == index).astype(np.float32))

    edge_x = np.abs(np.mod(x + wobble + field_width / 2, field_width) - field_width / 2)
    edge_z = np.abs(np.mod(z - south + wobble + field_depth / 2, field_depth) - field_depth / 2)
    hedges = smoothstep(1.3, 0.8, np.minimum(edge_x, edge_z))
    mix(paint.image, np.array((0.27, 0.45, 0.24), np.float32), hedges)

    # Fades into the grass on the near side.
    rough = (layered(shape, 5.0 * per_metre, rng, 3, **wrap) - 0.5) * 6.0
    alpha = smoothstep(south + 1.0, south + 5.0, z + rough)
    return np.concatenate((paint.image, alpha[..., None]), axis=2)


def paint_title_sky():
    """A sky from straight down to straight up. The horizon matches the start scene's fog colour."""
    height = 512
    elevation = 90.0 - (np.arange(height, dtype=np.float32) + 0.5) / height * 180.0
    positions = (-90.0, 0.0, 7.0, 32.0, 90.0)
    colours = np.array(((0.8, 0.91, 0.96), (0.8, 0.91, 0.96), (0.64, 0.84, 0.96), (0.38, 0.68, 0.95), (0.3, 0.58, 0.9)), np.float32)
    column = np.stack([np.interp(elevation, positions, colours[:, c]) for c in range(3)], axis=-1).astype(np.float32)
    return np.repeat(column[:, None], 32, axis=1)


def write_png(path, image):
    """Row 0 is the top of the picture. Each row stores differences from the pixel to its left, which compresses well."""
    data = (np.clip(image, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    height, width, channels = data.shape
    difference = data.copy()
    difference[:, 1:] = data[:, 1:] - data[:, :-1]
    rows = np.zeros((height, width * channels + 1), np.uint8)
    rows[:, 0] = 1
    rows[:, 1:] = difference.reshape(height, width * channels)

    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)

    colour_type = 6 if channels == 4 else 2
    header = struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0)
    blob = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows.tobytes(), 9)) + chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(blob)
    print(f"Wrote {path.name}: {width}x{height}, {len(blob) // 1024} KB")


def main():
    folder = Path(__file__).resolve().parents[2] / "Assets/Game/Textures"
    write_png(folder / "Title_Grass.png", paint_title_grass(np.random.default_rng(23)))
    write_png(folder / "Title_Road.png", paint_road_tile(np.random.default_rng(11), verges_clear=True))
    write_png(folder / "Title_Fields.png", paint_title_fields(np.random.default_rng(31)))
    write_png(folder / "Title_Sky.png", paint_title_sky())
    if "--title" in sys.argv:
        return
    write_png(folder / "Level01_Ground.png", paint_play_area(np.random.default_rng(4122)))
    write_png(folder / "Level01_Far.png", paint_far(np.random.default_rng(7)))
    write_png(folder / "Level01_RoadTile.png", paint_road_tile(np.random.default_rng(11)))
    write_png(folder / "TruckZone.png", paint_zone())


if __name__ == "__main__":
    main()

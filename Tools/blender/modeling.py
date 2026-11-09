"""Small modelling kit for the scripts in this folder.

Everything is built from lofted cross-sections, so shapes come out smooth and rounded instead of
being stacked from Unity primitives. Blender axes: X is the model's left, -Y is its front, Z is up.
After export that becomes X right flipped, Unity +Z forward, Unity +Y up.
"""
import math

import bmesh
import bpy
from mathutils import Vector


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def catmull(a, b, c, d, t):
    return 0.5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (3 * b - a - 3 * c + d) * t * t * t)


def sample_keys(keys, samples_per_span):
    """Smoothly interpolates tuples of numbers. Used for profiles and paths."""
    result = []
    count = len(keys)
    for i in range(count - 1):
        before = keys[max(i - 1, 0)]
        start = keys[i]
        end = keys[i + 1]
        after = keys[min(i + 2, count - 1)]
        for step in range(samples_per_span):
            t = step / samples_per_span
            result.append(tuple(catmull(before[n], start[n], end[n], after[n], t) for n in range(len(start))))
    result.append(tuple(keys[-1]))
    return result


def section(center, u, v, radius_u, radius_v, power, points):
    """One closed cross-section. Power 2 is an ellipse; higher values approach a rounded box."""
    ring = []
    exponent = 2.0 / power
    for i in range(points):
        angle = 2.0 * math.pi * i / points
        c = math.cos(angle)
        s = math.sin(angle)
        a = math.copysign(abs(c) ** exponent, c)
        b = math.copysign(abs(s) ** exponent, s)
        ring.append(center + u * (radius_u * a) + v * (radius_v * b))
    return ring


class MeshPart:
    """Collects geometry for one exported object. Materials are added by name as they are used."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        # Made up front, because adding it later invalidates vertices that already exist.
        self.deform = self.bm.verts.layers.deform.verify()
        self.slots = []
        self.groups = []
        self._binding = None

    def bind(self, weights):
        """Skins everything added from now on: {bone: weight}, or a function of the vertex position."""
        self._binding = weights

    def _apply_binding(self, verts):
        if self._binding is None:
            return
        for vert in verts:
            weights = self._binding(vert.co) if callable(self._binding) else self._binding
            for bone, weight in weights.items():
                if bone not in self.groups:
                    self.groups.append(bone)
                vert[self.deform][self.groups.index(bone)] = weight

    def _slot(self, material):
        if material not in self.slots:
            self.slots.append(material)
        return self.slots.index(material)

    def loft(self, rings, material, cap_start=True, cap_end=True):
        """Skins cross-sections with quads. `material` is a name or a function of (ring, point)."""
        pick = material if callable(material) else (lambda ring, point: material)
        bm = self.bm
        verts = [[bm.verts.new(p) for p in ring] for ring in rings]
        for ring in verts:
            self._apply_binding(ring)
        points = len(rings[0])
        for r in range(len(rings) - 1):
            for p in range(points):
                q = (p + 1) % points
                face = bm.faces.new((verts[r][p], verts[r][q], verts[r + 1][q], verts[r + 1][p]))
                face.smooth = True
                face.material_index = self._slot(pick(r, p))
        if cap_start:
            face = bm.faces.new(list(reversed(verts[0])))
            face.smooth = True
            face.material_index = self._slot(pick(0, 0))
        if cap_end:
            face = bm.faces.new(verts[-1])
            face.smooth = True
            face.material_index = self._slot(pick(len(rings) - 2, 0))

    def lengthwise(self, keys, material, points=32, samples=6):
        """Lofts along Y. Each key is (y, center x, center z, radius x, radius z, power)."""
        rings = []
        for y, cx, cz, rx, rz, power in sample_keys(keys, samples):
            rings.append(section(Vector((cx, y, cz)), Vector((1, 0, 0)), Vector((0, 0, 1)),
                                 max(rx, 0.005), max(rz, 0.005), max(power, 1.2), points))
        self.loft(rings, material)

    def upright(self, keys, material, points=32, samples=4):
        """Lofts along Z. Each key is (z, center y, radius x, radius y, power)."""
        rings = []
        for z, cy, rx, ry, power in sample_keys(keys, samples):
            rings.append(section(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)),
                                 max(rx, 0.005), max(ry, 0.005), max(power, 1.2), points))
        self.loft(rings, material)

    def ellipsoid(self, center, radii, material, rings=14, points=24):
        center = Vector(center)
        sections = []
        for i in range(rings + 1):
            angle = math.pi * i / rings
            factor = max(math.sin(angle), 0.02)
            ring_center = center + Vector((0, 0, radii[2] * math.cos(angle)))
            sections.append(section(ring_center, Vector((1, 0, 0)), Vector((0, 1, 0)),
                                    radii[0] * factor, radii[1] * factor, 2.0, points))
        self.loft(sections, material)

    def rounded_box(self, center, half, material, power=6.0, rings=14, points=36, rotation=None):
        """A box with soft corners and slightly puffy sides. Higher power makes it boxier."""
        center = Vector(center)
        exponent = 2.0 / power
        sections = []
        for i in range(rings + 1):
            angle = -math.pi / 2 + math.pi * i / rings
            scale = max(abs(math.cos(angle)) ** exponent, 0.015)
            height = math.copysign(abs(math.sin(angle)) ** exponent, math.sin(angle))
            ring = section(Vector((0, 0, half[2] * height)), Vector((1, 0, 0)), Vector((0, 1, 0)),
                           half[0] * scale, half[1] * scale, power, points)
            if rotation is not None:
                ring = [rotation @ p for p in ring]
            sections.append([center + p for p in ring])
        self.loft(sections, material)

    def along(self, start, end, radius_u, radius_v, material, power=2.0, points=16, keys=None, up=(0, 0, 1)):
        """A rounded bar between two points. `keys` optionally shapes it as (t, size factor)."""
        start = Vector(start)
        end = Vector(end)
        axis = (end - start).normalized()
        reference = Vector(up)
        if abs(axis.dot(reference)) > 0.95:
            reference = Vector((1, 0, 0))
        u = axis.cross(reference).normalized()
        v = u.cross(axis).normalized()
        keys = keys or [(0.0, 0.04), (0.012, 0.72), (0.05, 1.0), (0.95, 1.0), (0.988, 0.72), (1.0, 0.04)]
        rings = []
        for t, factor in keys:
            rings.append(section(start.lerp(end, t), u, v, radius_u * factor, radius_v * factor, power, points))
        self.loft(rings, material)

    def tube(self, path, radius, material, points=10, samples=5, smooth=True):
        """A round tube along a path, with the end caps closed."""
        if smooth:
            path = [Vector(p) for p in sample_keys([tuple(p) for p in path], samples)]
        else:
            path = [Vector(p) for p in path]
        rings = []
        reference = Vector((0, 0, 1))
        last_u = None
        for i, point in enumerate(path):
            before = path[max(i - 1, 0)]
            after = path[min(i + 1, len(path) - 1)]
            axis = (after - before).normalized()
            if last_u is None:
                if abs(axis.dot(reference)) > 0.95:
                    reference = Vector((1, 0, 0))
                u = axis.cross(reference).normalized()
            else:
                u = (last_u - axis * last_u.dot(axis)).normalized()
            v = u.cross(axis).normalized()
            last_u = u
            rings.append(section(point, u, v, radius, radius, 2.0, points))
        self.loft(rings, material)

    def displace(self, function):
        """Moves everything added so far, to dent or crush a finished shape. `function` maps a position to a new one."""
        for vert in self.bm.verts:
            vert.co = function(vert.co.copy())

    def to_object(self, materials, location=(0, 0, 0)):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for name in self.slots:
            mesh.materials.append(materials[name])
        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = location
        for bone in self.groups:
            obj.vertex_groups.new(name=bone)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def make_materials(palette):
    """Colours here only matter for previews. Unity assigns its own materials by these names."""
    materials = {}
    for name, (r, g, b, roughness) in palette.items():
        material = bpy.data.materials.new(name)
        material.diffuse_color = (r, g, b, 1.0)
        material.roughness = roughness
        material.use_nodes = True
        shader = material.node_tree.nodes.get("Principled BSDF")
        if shader is not None:
            shader.inputs["Base Color"].default_value = (r, g, b, 1.0)
            shader.inputs["Roughness"].default_value = roughness
        materials[name] = material
    return materials


def add_empty(name, location):
    empty = bpy.data.objects.new(name, None)
    empty.location = location
    bpy.context.scene.collection.objects.link(empty)
    return empty


def export_fbx(path):
    """Settings that import into Unity with no rotation or scale on the objects."""
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=False,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        path_mode="STRIP",
    )


def export_rigged_fbx(path):
    """For a skeleton with several animations. Each action becomes one clip in Unity."""
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
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


def setup_preview(target=(0, 0, 0), distance=19.0, angle=(38.0, 16.0), size=(1200, 760), samples=24, lens=50):
    """Light and camera for check images, rendered on the CPU."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.resolution_x = size[0]
    scene.render.resolution_y = size[1]
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("Sky")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.78, 0.92, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    scene.world = world

    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.0
    sun_object = bpy.data.objects.new("Sun", sun)
    sun_object.rotation_euler = (math.radians(45), math.radians(10), math.radians(30))
    scene.collection.objects.link(sun_object)

    camera = bpy.data.cameras.new("Camera")
    camera.lens = lens
    camera_object = bpy.data.objects.new("Camera", camera)
    yaw = math.radians(angle[0])
    pitch = math.radians(angle[1])
    target = Vector(target)
    offset = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch))) * distance
    camera_object.location = target + offset
    camera_object.rotation_euler = (-offset).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(camera_object)
    scene.camera = camera_object
    return camera_object


def aim_camera(camera_object, target, distance, angle):
    yaw = math.radians(angle[0])
    pitch = math.radians(angle[1])
    target = Vector(target)
    offset = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch))) * distance
    camera_object.location = target + offset
    camera_object.rotation_euler = (-offset).to_track_quat("-Z", "Y").to_euler()


def render_preview(path, target=(0, 0, 0), distance=19.0, angle=(38.0, 16.0)):
    """Quick check image: three-quarter view with soft light."""
    setup_preview(target, distance, angle)
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

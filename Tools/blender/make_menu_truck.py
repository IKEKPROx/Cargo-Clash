"""A hollow presentation cab inside the original V07 truck silhouette, with seats and a working wheel."""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent / "独立建模/运输载具_v07/脚本"))
import build_fleet as fleet


def main():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT.parent / "独立建模/运输载具_v07/模型/Truck_07/Truck_07.blend"))
    body = bpy.data.objects["Body"]
    for obj in list(bpy.context.scene.objects):
        if obj != body:
            bpy.data.objects.remove(obj, do_unlink=True)
    fleet.MATS.update({name: bpy.data.materials[name] for name in fleet.PALETTE})
    bm = bmesh.new()
    bm.from_mesh(body.data)
    remaining = set(bm.verts)
    cab_vertices, glass_vertices = set(), set()
    while remaining:
        first = remaining.pop()
        component, pending = {first}, [first]
        while pending:
            vertex = pending.pop()
            for edge in vertex.link_edges:
                neighbor = edge.other_vert(vertex)
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    component.add(neighbor)
                    pending.append(neighbor)
        center = sum((v.co for v in component), Vector()) / len(component)
        material = body.data.materials[next(iter(first.link_faces)).material_index].name
        if material in ("Fleet_Glass", "Fleet_GlassGlint"):
            glass_vertices.update(component)
        if material == "Fleet_Navy" and len(component) == 608:
            cab_vertices.update(component)
        if material == "Fleet_Dark" and ((center.y > 3.5 and center.z > 2.8) or
                                          (abs(center.x) > 1.5 and 3.0 < center.z < 3.3)):
            cab_vertices.update(component)
    vertices = list(cab_vertices)
    indices = {v: i for i, v in enumerate(vertices)}
    faces = list({f for v in vertices for f in v.link_faces})
    mesh = bpy.data.meshes.new("Cab Shell")
    mesh.from_pydata([v.co[:] for v in vertices], [], [[indices[v] for v in f.verts] for f in faces])
    for material in body.data.materials:
        mesh.materials.append(material)
    for polygon, face in zip(mesh.polygons, faces):
        polygon.material_index = face.material_index
        polygon.use_smooth = face.smooth
    cab = bpy.data.objects.new("Cab Shell", mesh)
    bpy.context.collection.objects.link(cab)
    shell = bmesh.new()
    shell.from_mesh(mesh)
    bmesh.ops.holes_fill(shell, edges=[edge for edge in shell.edges if edge.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(shell, faces=list(shell.faces))
    shell.to_mesh(mesh)
    shell.free()
    bmesh.ops.delete(bm, geom=list(cab_vertices | glass_vertices), context="VERTS")
    bm.to_mesh(body.data)
    bm.free()

    openings = [((0, 2.38, 2.71), (2.58, 2.48, 1.78), (0, 0, 0))]
    for side in (-1, 1):
        openings.extend([
            ((side * 0.66, 3.75, 3.04), (1.08, 1.05, 0.85), (12, 0, 0)),
            ((side * 1.50, 2.27, 3.17), (0.75, 1.16, 0.59), (0, 0, 0)),
        ])
    for center, size, rotation in openings:
        bpy.ops.mesh.primitive_cube_add(size=1, location=center)
        cutter = bpy.context.object
        cutter.scale = size
        cutter.rotation_euler = tuple(math.radians(a) for a in rotation)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        bevel = cutter.modifiers.new("Window corners", "BEVEL")
        bevel.width, bevel.segments = 0.045, 4
        bpy.ops.object.modifier_apply(modifier=bevel.name)
        bpy.context.view_layer.objects.active = cab
        difference = cab.modifiers.new("Interior and original window openings", "BOOLEAN")
        difference.operation, difference.solver, difference.object = "DIFFERENCE", "EXACT", cutter
        difference.use_self = True
        bpy.ops.object.modifier_apply(modifier=difference.name)
        print("[Cab cut]", center, len(cab.data.vertices), len(cab.data.polygons), flush=True)
        bpy.data.objects.remove(cutter, do_unlink=True)
    fleet.merge_into(body, [cab])

    interior = fleet.Geo("CabInterior")
    interior.box((0, 2.35, 1.82), (1.24, 1.12, 0.045), "Dark")
    interior.box((0, 1.37, 2.70), (1.26, 0.035, 0.82), "Cream")
    interior.box((0, 2.35, 3.52), (1.24, 1.05, 0.025), "Cream")
    for side in (-1, 1):
        x = side * 1.27
        interior.box((x, 2.22, 2.40), (0.025, 0.58, 0.24), "Dark")
        interior.bar((x - side * 0.05, 2.04, 2.61), (x - side * 0.05, 2.40, 2.61), 0.027, "Steel")
        interior.box((side * 0.64, 3.12, 3.45), (0.43, 0.12, 0.025), "Dark", r=(12, 0, 0))
    interior.box((0, 3.14, 2.43), (1.18, 0.26, 0.12), "Navy", p=4)
    interior.box((0, 3.20, 2.57), (1.12, 0.19, 0.035), "Cream", p=4)
    for x, radius in ((0.46, 0.10), (0.22, 0.065), (0.69, 0.065)):
        interior.bar((x, 2.93, 2.50), (x, 2.90, 2.50), radius, "Brass", n=24)
        interior.bar((x, 2.90, 2.50), (x, 2.885, 2.50), radius * 0.79, "Dark", n=24)
        interior.bar((x, 2.88, 2.50), (x + radius * 0.45, 2.88, 2.50 + radius * 0.35), 0.008, "Cream", n=8)
    for side in (-1, 1):
        for slot in range(5):
            interior.box((side * 0.94 + slot * 0.025, 2.878, 2.49), (0.004, 0.008, 0.043), "Dark")
    for x in (-0.60, -0.40, -0.20):
        interior.bar((x, 2.92, 2.49), (x, 2.87, 2.49), 0.025, "Dark")
        interior.bar((x, 2.865, 2.49), (x, 2.865, 2.507), 0.004, "Cream", n=6)
    interior.bar((0.55, 3.04, 2.43), (0.55, 2.55, 2.64), 0.043, "Steel")
    for x in (0.42, 0.64):
        interior.box((x, 2.88, 1.94), (0.06, 0.10, 0.025), "Dark", r=(25, 0, 0))
    interior.bar((-0.02, 2.36, 1.85), (-0.02, 2.42, 2.28), 0.024, "Steel")
    interior.ball((-0.02, 2.42, 2.30), (0.06, 0.06, 0.055), "Dark")
    lining = fleet.MATS["Fleet_Cream"].copy()
    lining.name = "Crew_Light"
    cabin = interior.finish()
    for index, material in enumerate(cabin.data.materials):
        if material.name == "Fleet_Cream":
            cabin.data.materials[index] = lining

    seats = fleet.Geo("CabSeats")
    for x in (-0.30, 0.55):
        seats.box((x, 2.01, 2.09), (0.41, 0.44, 0.08), "Dark")
        seats.box((x, 2.04, 2.17), (0.39, 0.42, 0.075), "Navy", p=4)
        seats.box((x, 1.73, 2.58), (0.39, 0.10, 0.39), "Dark", r=(-8, 0, 0), p=4)
        seats.box((x, 1.84, 2.58), (0.33, 0.035, 0.33), "Navy", r=(-8, 0, 0), p=4)
        seats.box((x, 1.80, 3.02), (0.25, 0.10, 0.13), "Navy", p=3)
        for seam in (-0.17, 0.17):
            seats.bar((x + seam, 1.84, 2.27), (x + seam, 1.92, 2.86), 0.006, "Cream", n=6)
            seats.bar((x + seam, 1.89, 2.245), (x + seam, 2.34, 2.245), 0.006, "Cream", n=6)
    fabric = fleet.MATS["Fleet_Navy"].copy()
    fabric.name = "Porter_Shirt"
    upholstery = seats.finish()
    for index, material in enumerate(upholstery.data.materials):
        if material.name == "Fleet_Navy":
            upholstery.data.materials[index] = fabric

    wheel = fleet.Geo("SteeringWheel")
    wheel.torus((0, 0, 0), 0.265, 0.265, 0.035, "Dark", n=48)
    wheel.ball((0, 0, 0), (0.07, 0.07, 0.035), "Brass")
    for angle in (30, 150, 270):
        angle = math.radians(angle)
        wheel.bar((0, 0, 0), (0.24 * math.cos(angle), 0.24 * math.sin(angle), 0), 0.022, "Steel")
    wheel.finish()
    bpy.ops.export_scene.fbx(filepath=str(ROOT / "Assets/Game/Models/MenuTruck.fbx"),
                            object_types={"MESH"}, bake_anim=False, use_mesh_modifiers=True,
                            mesh_smooth_type="FACE", apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                            axis_forward="-Z", axis_up="Y", bake_space_transform=True, path_mode="STRIP")
    print("[MenuTruck] Kept V07 exterior; opened existing windows and added interior.", flush=True)


if __name__ == "__main__":
    main()

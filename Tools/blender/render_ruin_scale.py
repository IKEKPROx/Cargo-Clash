"""Render shipped FBX geometry with the real crew at their Unity importer scale.

Run in Blender background. Only writes two review PNGs to Docs/verification.
This is a Blender scale illustration, not an in-game screenshot.
"""
import json
import math
import re
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT / 'Assets/Game/Resources/RuinCourtyard'
MODELS = ROOT / 'Assets/Game/Models'
OUT = ROOT / 'Docs/verification'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
for name in ('Environment', 'OperableDoors'):
    bpy.ops.import_scene.fbx(filepath=str(RES / f'RuinCourtyard_{name}.fbx'),use_anim=False)

palette = {r['name']:r['color'] for r in json.loads((RES / 'RuinPalette.json').read_text())['materials']}
for mat in bpy.data.materials:
    key = re.sub(r'\.\d+$', '', mat.name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if key in palette:
        c = palette[key]
        bsdf.inputs['Base Color'].default_value = (c['r'],c['g'],c['b'],1)
    bsdf.inputs['Roughness'].default_value = .72
    if key.startswith('RuinGround'):
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(RES / 'RuinGround.png'))
        mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])


def import_group(path, location, scale=1, angle=0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    items = set(bpy.data.objects)-before
    root = bpy.data.objects.new('Review '+path.stem, None)
    scene.collection.objects.link(root)
    for ob in items:
        if ob.parent not in items:
            ob.parent = root
        if ob.type == 'ARMATURE':
            ob.data.pose_position = 'REST'
    root.scale = (scale,)*3
    root.location = location
    root.rotation_euler.z = angle
    return items


def person(body, p, angle=0, outfit='Porter'):
    path = MODELS / f'Crew_{body}.fbx'
    text = Path(str(path)+'.meta').read_text()
    scale = float(re.search(r'^    globalScale: (.+)$', text, re.M)[1])
    items = import_group(path,p,scale,angle)
    for ob in items:
        if ob.type == 'MESH':
            name = re.sub(r'\.\d+$', '', ob.name)
            ob.hide_render = name not in (outfit,'Gloves',outfit+'_Hat_beacon',outfit+'_Glasses_square')
    return items


blue = bpy.data.materials.new('Review measurement navy')
blue.diffuse_color = (.035,.066,.10,1)
blue.use_nodes = True
blue.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.015,.028,.045,1)
blue.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .8


def text(label, p, size=.22):
    curve = bpy.data.curves.new(label,'FONT')
    curve.body, curve.size, curve.extrude = label,size,.001
    ob = bpy.data.objects.new(label,curve)
    scene.collection.objects.link(ob)
    ob.location = p
    ob.rotation_euler = (math.pi/2,0,0)
    curve.materials.append(blue)


def line(a,b, thickness=.017):
    curve = bpy.data.curves.new('Dimension line','CURVE')
    curve.dimensions='3D'
    curve.bevel_depth=thickness
    poly=curve.splines.new('POLY'); poly.points.add(1)
    for point, p in zip(poly.points,(a,b)):
        point.co=(*p,1)
    ob=bpy.data.objects.new('Dimension line',curve)
    scene.collection.objects.link(ob); curve.materials.append(blue)


for body,x,label in [('Short',42.9,'1.39 m'),('Medium',44.5,'1.66 m'),('Tall',46.1,'1.90 m')]:
    person(body,(x,31.4,.01))
    text(body+' / '+label,(x-.52,30.9,2.14),.18)
line((42.65,32.6,2.84),(46.35,32.6,2.84))
text('3.70 m clear',(43.65,32.57,3.0),.24)
line((46.95,32.5,0),(46.95,32.5,2.72))
text('2.72 m',(47.0,32.5,1.45),.23)
import_group(MODELS/'Cargo_Large.fbx',(44.5,36.5,.544),angle=math.pi/2)
person('Tall',(44.5,34.79,.014),angle=math.pi)
person('Tall',(44.5,38.21,.014))
person('Tall',(68.5,40.3,2.51))
person('Tall',(73.65,42,0),angle=-math.pi/2)
line((72.8,39.3,0),(72.8,39.3,2.5))
text('2.50 m terrace',(70,38.8,3.0),.3)
for y in [0,.5,1,1.5,2,2.5]:
    line((72.7,39.3,y),(72.9,39.3,y),.012)

world = bpy.data.worlds.new('Review sky')
world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.74,.82,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.65
scene.world=world
sun=bpy.data.lights.new('Review sun','SUN'); sun.energy=2.7; sun.angle=.25
ob=bpy.data.objects.new('Review sun',sun); scene.collection.objects.link(ob)
ob.rotation_euler=(math.radians(25),math.radians(-25),math.radians(-35))
area=bpy.data.lights.new('Review fill','AREA'); area.energy=1800; area.shape='DISK'; area.size=15
ob=bpy.data.objects.new('Review fill',area);scene.collection.objects.link(ob);ob.location=(47,28,12)
camera=bpy.data.cameras.new('Review camera'); cam=bpy.data.objects.new('Review camera',camera)
scene.collection.objects.link(cam);scene.camera=cam
scene.render.engine='CYCLES'
scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.resolution_x=1440;scene.render.resolution_y=1050;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
camera.type='ORTHO'
for name, pos, target, zoom in [
    ('ruin-scale-vault',(49.6,19.5,7.4),(44.8,34,1.3),10.8),
    ('ruin-scale-terrace',(81,22,12),(66.5,41,1.1),21),
]:
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    camera.ortho_scale=zoom
    scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
print('Saved two Blender scale reference images (not Unity screenshots).')

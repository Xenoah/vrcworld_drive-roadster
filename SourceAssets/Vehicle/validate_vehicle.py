import bpy,os,json,math
from mathutils import Vector
P=os.path.dirname(__file__);O=os.path.abspath(os.path.join(P,'../../Assets/DriveRoadster/Art/Vehicle'))
report={}
for lod in range(3):
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 bpy.ops.import_scene.gltf(filepath=os.path.join(O,f'DR_Asterion_LOD{lod}.glb'))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
 pts=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
 report[f'LOD{lod}']={'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),'renderers':len(meshes),'materials':len(set(m.name for o in meshes for m in o.data.materials)), 'bounds_blender':[[min(v[k] for v in pts) for k in range(3)],[max(v[k] for v in pts) for k in range(3)]], 'wheel_pivots_blender':{o.name:list(o.matrix_world.translation) for o in bpy.context.scene.objects if o.name.startswith('Wheel_') and o.type=='EMPTY'},'finite_vertices':all(math.isfinite(x) for o in meshes for v in o.data.vertices for x in v.co)}
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=os.path.join(O,'DR_Asterion_LOD0.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report['FBX_LOD0_reimport']={'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),'meshes':len(meshes),'wheel_pivots':{o.name:list(o.matrix_world.translation) for o in bpy.context.scene.objects if o.name.startswith('Wheel_') and o.type=='EMPTY'},'anchors':{o.name:list(o.matrix_world.translation) for o in bpy.context.scene.objects if o.name in ['Seat_Driver','Eye_Driver','Exit_Driver']}}
report['scope']='Blender GLB all LOD and FBX LOD0 re-import geometry inspection only. No Unity, VRChat, station, physics or multiplayer verification.'
with open(os.path.join(O,'vehicle_manifest.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report,indent=2))

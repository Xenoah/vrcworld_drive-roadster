import bpy,os,json
ROOT=os.path.dirname(os.path.abspath(__file__));OUT=os.path.abspath(os.path.join(ROOT,'../../Assets/DriveRoadster/Art/Course'))
bpy.ops.wm.open_mainfile(filepath=ROOT+'/VectorCoastal_Course.blend')
bpy.ops.object.select_all(action='DESELECT');stats=[]
for o in list(bpy.context.scene.objects):
 if not o.name.startswith('VIS_LOD0_'):continue
 o.select_set(True);bpy.context.view_layer.objects.active=o
 if 'Deck_Ceramic' not in o.name and 'Ocean' not in o.name:
  m=o.modifiers.new('Distance reduction','DECIMATE');m.ratio=.38;bpy.ops.object.modifier_apply(modifier=m.name)
 o.name=o.name.replace('LOD0','LOD1');stats.append({'name':o.name,'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)})
bpy.ops.export_scene.fbx(filepath=OUT+'/VectorCoastal_Course_LOD1_VisualOnly.fbx',use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
bpy.ops.export_scene.gltf(filepath=OUT+'/VectorCoastal_Course_LOD1_VisualOnly.glb',use_selection=True,export_format='GLB')
json.dump({'triangles':sum(o['triangles'] for o in stats),'objects':stats,'usage':'Optional far-distance visual replacement only. Never add duplicate collision. Road deck remains full-resolution. Unity screen-size threshold unvalidated.'},open(OUT+'/lod_statistics.json','w'),indent=2)

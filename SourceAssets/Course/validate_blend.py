import bpy,json,os,math
from mathutils import Vector
R=os.path.dirname(os.path.abspath(__file__));bpy.ops.wm.open_mainfile(filepath=R+'/VectorCoastal_Course.blend')
names=['COL_Ground_Acceleration','COL_WallRide_WallArc','COL_Ground_Launch','COL_Landing_Landing','COL_Ground_ReturnArc']
def verts(n):o=bpy.data.objects[n];return [o.matrix_world@v.co for v in o.data.vertices]
seams=[]
for a,b in [(names[0],names[1]),(names[1],names[2]),(names[3],names[4]),(names[4],names[0])]:
 va=verts(a);vb=verts(b);delta=max((va[-2]-vb[0]).length,(va[-1]-vb[1]).length);seams.append({'from':a,'to':b,'max_gap_metres':delta})
normal_ranges={}
for name in names+['COL_WallRide_CeilingLab','COL_WallRide_VerticalCurl']:
 o=bpy.data.objects[name];normal_ranges[name]=[min(p.normal.z for p in o.data.polygons),max(p.normal.z for p in o.data.polygons)]
r={'surface_seams':seams,'all_solid_seams_under_0_1mm':all(s['max_gap_metres']<.0001 for s in seams),'world_up_normal_dot_ranges':normal_ranges,'ceiling_inverted_face_present':normal_ranges['COL_WallRide_CeilingLab'][0]<-.99,'wall_faces_up':normal_ranges[names[1]][0]>.3,'ground_faces_up':normal_ranges[names[0]][0]>.99,'vertical_curl_top_overhang':normal_ranges['COL_WallRide_VerticalCurl'][0]<-.5,'flight_gap_intentional':True}
json.dump(r,open(R+'/surface_validation.json','w'),indent=2);print(r);assert r['all_solid_seams_under_0_1mm'] and r['ceiling_inverted_face_present'] and r['ground_faces_up'] and r['wall_faces_up'] and r['vertical_curl_top_overhang']

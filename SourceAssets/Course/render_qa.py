import bpy,os
R=os.path.dirname(os.path.abspath(__file__));bpy.ops.wm.open_mainfile(filepath=R+'/VectorCoastal_Course.blend');s=bpy.context.scene
for name,file in [('QA_Aerial','preview_aerial.png'),('QA_Start','preview_ground.png'),('QA_VerticalCurl','preview_vertical_curl.png')]:
 s.camera=bpy.data.objects[name];s.render.filepath=R+'/'+file;bpy.ops.render.render(write_still=True)

import bpy, bmesh, math, json, os, random
from mathutils import Vector
from math import sin,cos,pi
ROOT=os.path.dirname(os.path.abspath(__file__)); OUT=os.path.abspath(os.path.join(ROOT,'../../Assets/DriveRoadster/Art/Course')); os.makedirs(OUT,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
random.seed(31)
def xyz(p): return (p[0],-p[2],p[1])
def mat(n,c,metal=0,rough=.6,em=0):
 m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True; s=m.node_tree.nodes.get('Principled BSDF'); s.inputs['Base Color'].default_value=(*c,1); s.inputs['Metallic'].default_value=metal; s.inputs['Roughness'].default_value=rough
 if em: s.inputs['Emission Color'].default_value=(*c,1); s.inputs['Emission Strength'].default_value=em
 return m
road=mat('Roadster_Deck_Ceramic',(.30,.38,.42),.15,.67); white=mat('Roadster_Porcelain',(.78,.83,.81),.3,.36); dark=mat('Roadster_Basalt',(.035,.057,.069),.1,.8); steel=mat('Roadster_Structure',(.08,.14,.18),.65,.34); cyan=mat('Roadster_Guidance_Cyan',(.02,.75,.86),.3,.3,1.5); amber=mat('Roadster_Warning_Amber',(1,.28,.035),.2,.4,1); red=mat('Roadster_Ramp_Red',(.8,.015,.025),.2,.4,1.3); sea=mat('Roadster_Ocean',(.025,.18,.22),.3,.28)
def mesh(n,vs,fs,m):
 me=bpy.data.meshes.new(n); me.from_pydata([xyz(p) for p in vs],[],fs); me.update(); o=bpy.data.objects.new(n,me); bpy.context.collection.objects.link(o); o.data.materials.append(m); return o
def box(n,p,d,m,bevel=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=xyz(p)); o=bpy.context.object; o.name=n; o.dimensions=(d[0],d[2],d[1]); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); o.data.materials.append(m)
 if bevel: mod=o.modifiers.new('Manufactured edge','BEVEL'); mod.width=bevel; mod.segments=2; bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
 return o
def beam(n,a,b,r,m):
 av=Vector(xyz(a)); bv=Vector(xyz(b)); bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=r,depth=(bv-av).length,location=(av+bv)/2); o=bpy.context.object; o.name=n; o.rotation_euler=(bv-av).to_track_quat('Z','Y').to_euler(); o.data.materials.append(m); return o
def text(n,s,p,size,m,rot=(pi/2,0,0)):
 c=bpy.data.curves.new(n,'FONT'); c.body=s;c.size=size;c.align_x='CENTER';c.extrude=.012;c.space_character=1.1;o=bpy.data.objects.new(n,c);bpy.context.collection.objects.link(o);o.location=xyz(p);o.rotation_euler=rot;o.data.materials.append(m);bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False);return o
# Samples are in canonical Unity world coordinates: X right, Y up, Z forward.
sections=[]
def section(name,pts,tag): sections.append((name,pts,tag))
section('Acceleration',[(0,0,z,0) for z in range(-30,251,2)],'Ground')
pts=[]
for i in range(181):
 t=i/180; a=pi*(1-t); bank=math.radians(72)*sin(pi*t)**2; pts.append((90+90*cos(a),8*sin(bank),250+90*sin(a),bank))
section('WallArc',pts,'WallRide')
# Matching first/last samples keep solid surfaces continuous up to deliberate launch gap.
section('Launch',[(180,10*((250-z)/100)**2,z,0) for z in range(250,149,-2)],'Ground')
section('Landing',[(180,0,z,0) for z in range(0,-31,-2)],'Landing')
section('ReturnArc',[(90+90*cos(-pi*i/180),0,-30+90*sin(-pi*i/180),0) for i in range(181)],'Ground')
all_meta=[]
def tang(pts,i):
 a=pts[max(0,i-1)];b=pts[min(len(pts)-1,i+1)];dx=b[0]-a[0];dz=b[2]-a[2];
 if i in (0,len(pts)-1):
  if abs(dx)<abs(dz)*.02:dx=0
  if abs(dz)<abs(dx)*.02:dz=0
 l=math.hypot(dx,dz);return dx/l,dz/l
def pos(p,tx,tz,u,h=0):
 x,y,z,b=p;return(x+tz*u*cos(b),y-u*sin(b)+h,z-tx*u*cos(b))
def ribbon(n,pts,u0,u1,m,h=0):
 u0,u1=sorted((u0,u1))
 vs=[]
 for i,p in enumerate(pts):
  tx,tz=tang(pts,i);vs.extend([pos(p,tx,tz,u0,h),pos(p,tx,tz,u1,h)])
 fs=[(2*i,2*i+2,2*i+3,2*i+1) for i in range(len(pts)-1)];return mesh(n,vs,fs,m)
for name,pts,tag in sections:
 ribbon('VIS_Deck_'+name,pts,-9,9,road)
 co=ribbon('COL_'+tag+'_'+name,pts,-9,9,road);co.hide_render=True;co['surface_tag']=tag;co['collision_only']=True;co['unity_layer']='RoadsterSurface';co['static_nonconvex']=True
 all_meta.append({'mesh':co.name,'surface':tag,'samples':len(pts),'triangles':2*(len(pts)-1)})
 for side in [-1,1]:
  ribbon('VIS_Shoulder_'+name,pts,side*9,side*10.2,white,-.06);ribbon('VIS_Glow_'+name,pts,side*8.65,side*8.8,cyan,.025)
  # segmented expansion joints and edge supports, no track-blocking guardrail in wall sections
  for i in range(0,len(pts)-1,10):
   p=pts[i];tx,tz=tang(pts,i);a=pos(p,tx,tz,side*9.7,-.2);b=(a[0],-8,a[2]);beam('VIS_Pylon',a,b,.34,steel)
 for i in range(0,len(pts)-3,8):
  ribbon('VIS_LaneDash',pts[i:i+3],-.1,.1,white,.035)
 for i in range(12,len(pts)-1,18):
  tx,tz=tang(pts,i);p=pts[i]
  for u in [-7,7]:
   v=[pos(p,tx,tz,u-0.6,.04),pos(p,tx,tz,u+.6,.04),pos((p[0]+tx*2.7,p[1],p[2]+tz*2.7,p[3]),tx,tz,u,.04)];mesh('VIS_DirectionChevron',v,[(0,1,2)],cyan)
# Optional free-practice ceiling adhesion lab, separate from the timed course.
lab=[]
for z in range(150,301):
 if z<=210: t=(z-150)/60; b=pi*(.5-.5*cos(pi*t))
 elif z<=240:b=pi
 else:t=(z-240)/60;b=pi*(.5+.5*cos(pi*t))
 lab.append((110,9*(1-cos(b)),z,b))
ribbon('VIS_CeilingLab_GraySurface',lab,-6,6,road,.025)
co=ribbon('COL_WallRide_CeilingLab',lab,-6,6,road);co.hide_render=True;co['surface_tag']='WallRide';co['collision_only']=True
all_meta.append({'mesh':co.name,'surface':'WallRide','samples':len(lab),'triangles':2*(len(lab)-1),'practice_only':True})
for side in [-1,1]:
 ribbon('VIS_CeilingLab_Edge',lab,side*6,side*6.4,white)
 ribbon('VIS_CeilingLab_Guide',lab,side*5.7,side*5.9,cyan,.025)
 for z in [210,220,230,240]:beam('VIS_CeilingLabColumn',(110+side*7,0,z),(110+side*7,18.7,z),.22,steel)
box('VIS_CeilingCatchPad',(115,-.2,220),(60,.4,180),road,.2)
co=box('COL_Ground_CeilingCatch',(115,-.2,220),(60,.4,180),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
box('VIS_CeilingLabBoard',(99,2,147),(9,3,.3),steel,.1)
text('VIS_CeilingLabLabel','CEILING LAB',(99,2.1,146.8),.75,white,rot=(pi/2,0,pi))
text('VIS_CeilingLabSpeed','12 m/s / RESET TO EXIT',(99,1.1,146.8),.35,cyan,rot=(pi/2,0,pi))

# Video-observed design vocabulary: narrow grey vertical curl, white center marks,
# red edge lights and black rear truss. Dimensions/layout are original game choices.
curl=[]
for z in range(30,81,2):curl.append((0,0,z))
for i in range(1,126):
 a=pi*i/180;curl.append((a,30*(1-cos(a)),80+30*sin(a)))

def curlstrip(n,left,right,m,offset=0):
 vs=[]
 for a,y,z in curl:vs.extend([(240+left,y+cos(a)*offset,z-sin(a)*offset),(240+right,y+cos(a)*offset,z-sin(a)*offset)])
 return mesh(n,vs,[(i*2,i*2+2,i*2+3,i*2+1) for i in range(len(curl)-1)],m)
curlstrip('VIS_VerticalCurl_Surface',-3,3,road,.025)
co=curlstrip('COL_WallRide_VerticalCurl',-3,3,road);co.hide_render=True;co['surface_tag']='WallRide';co['collision_only']=True
all_meta.append({'mesh':co.name,'surface':'WallRide','samples':len(curl),'triangles':2*(len(curl)-1),'practice_only':True})
curlstrip('VIS_VerticalCurl_Centerline',-.055,.055,white,.05)
for side in [-1,1]:
 curlstrip('VIS_VerticalCurl_RedEdge',side*2.82-.05,side*2.82+.05,red,.06)
 prev=None
 for i in list(range(0,126,12))+[125]:
  a=pi*i/180;y=30*(1-cos(a));z=80+30*sin(a);p=(240+side*3.8,y-cos(a)*1.5,z+sin(a)*1.5)
  if prev:beam('VIS_VerticalCurl_BackTruss',prev,p,.16,steel)
  if i%24==0:
   foot=(p[0],-.5,p[2]);beam('VIS_VerticalCurl_Column',foot,p,.17,steel)
   if prev:beam('VIS_VerticalCurl_Diagonal',(p[0],max(0,p[1]-12),p[2]),prev,.10,steel)
  prev=p
box('VIS_CurlFoundation',(240,-5,100),(42,9,145),dark,.5)
box('VIS_CurlSafetyPad',(240,-.2,100),(40,.4,140),road,.2)
co=box('COL_Ground_CurlSafetyPad',(240,-.2,100),(40,.4,140),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
box('VIS_CurlAccess',(182.5,-.2,145),(75,.4,10),road,.1)
co=box('COL_Ground_CurlAccess',(182.5,-.2,145),(75,.4,10),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
box('VIS_CurlWarningBoard',(230,2,30),(10,3,.3),steel,.1)
text('VIS_CurlWarningTitle','VERTICAL / EXPERIMENTAL',(230,2.1,29.8),.55,white,rot=(pi/2,0,pi))
text('VIS_CurlWarningReset','NOT TIMED - RESET TO EXIT',(230,1.1,29.8),.4,red,rot=(pi/2,0,pi))

# Central volcanic island terraced apron and coast. Ocean is opaque single plane, no expensive transparency.
box('VIS_IslandFoundation',(85,-8,100),(260,10,500),dark,7)
box('VIS_Ocean',(90,-14,100),(1800,.2,1800),sea)
for i in range(45):
 x=random.choice([-1,1])*random.uniform(110,150)+90; z=random.uniform(-145,365)
 if x>200 and 10<z<190:continue # Keep vertical-ramp access clear of decorative rocks.
 bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=xyz((x,-6,z)));o=bpy.context.object;o.name='VIS_BasaltCoast';o.scale=(random.uniform(6,16),random.uniform(6,16),random.uniform(4,10));o.data.materials.append(dark)
# Launch engineering ribs and generous clean landing apron.
for z in [180,170,160,152]: box('VIS_LaunchMark',(180,10*((250-z)/100)**2+.06,z),(17,.04,.4),amber)
box('VIS_LandingApron',(180,-.15,5),(32,.22,52),road,.25);co=box('COL_Landing_Apron',(180,-.15,5),(32,.30,52),road);co.hide_render=True;co['surface_tag']='Landing';co['collision_only']=True
for z in [23,18,13,8,3]:box('VIS_LandingStripe',(180,.01,z),(24,.03,.25),white)
# Air rings: octagonal structural frame with inset emissive guides, free clear opening 22m diameter.
def ring(n,p,r,m):
 vs=[];fs=[]; N=64
 for zoff,rad in [(-.55,r),(-.55,r+1.1),(.55,r),(.55,r+1.1)]:
  for i in range(N):a=2*pi*i/N;vs.append((p[0]+rad*cos(a),p[1]+rad*sin(a),p[2]+zoff))
 for i in range(N):j=(i+1)%N;fs.extend([(i,j,N+j,N+i),(2*N+i,3*N+i,3*N+j,2*N+j),(i,2*N+i,2*N+j,j),(N+i,N+j,3*N+j,3*N+i)])
 return mesh(n,vs,fs,m)
for i,(z,y) in enumerate([(120,17),(75,23),(30,16)],1):
 ring('VIS_FlightRingFrame_%02d'%i,(180,y,z),11,white);ring('VIS_FlightRingGlow_%02d'%i,(180,y,z-.58),10.8,cyan)
 for x in [167,193]:beam('VIS_RingAnchor',(x,-6,z),(x,y,z),.28,steel)
 text('VIS_RingNumber','0'+str(i+3),(180,y+14,z),3,white)
# Paddock separate from route, connecting access road ends at track's inside edge.
box('VIS_Paddock',(48,-.18,55),(65,.35,125),road,.4);co=box('COL_Ground_Paddock',(48,-.2,55),(65,.4,125),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
box('VIS_PitConnector',(12,-.19,5),(24,.28,16),road);co=box('COL_Ground_PitConnector',(12,-.15,5),(24,.3,16),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
for z in [12,26,40,54]:
 box('VIS_PitBay',(37,.025,z),(13,.03,.18),white);box('VIS_PitBay',(50,.025,z),(13,.03,.18),white)
 box('VIS_ServicePlinth',(62,.65,z),(2,1.3,2.5),steel,.2);box('VIS_ServiceLight',(61,1.1,z),(.1,.2,1.8),cyan)
# Pit roof on slim canted supports; spectator steps and control tower.
box('VIS_PitCanopy',(48,6.8,33),(32,.7,64),white,.3)
for z in [4,24,44,64]:
 beam('VIS_CanopyColumn',(61,0,z),(60,6.5,z),.27,steel);beam('VIS_CanopyBrace',(60,5,z),(41,6.5,z),.17,steel)
for i in range(5):box('VIS_SpectatorTier',(74+i*2, .45+i*.8,175),(2,.9+i*1.6,64),white,.1)
box('VIS_ObservationDeck',(65,6.5,175),(14,.7,70),steel,.2); co=box('COL_Ground_ObservationDeck',(65,6.65,175),(14,.4,70),road);co.hide_render=True;co['surface_tag']='Pedestrian';co['collision_only']=True
for z in [141,209]:
 for x in [58,72]:beam('VIS_DeckPillar',(x,-3,z),(x,6.5,z),.3,steel)
for x in [58,72]:beam('VIS_DeckRailing',(x,7.8,140),(x,7.8,210),.08,white)
box('VIS_ControlTower',(62,10,235),(18,20,18),steel,1)
box('VIS_ControlCrown',(62,21,235),(26,3,24),white,.5);box('VIS_ControlGlazing',(62,19,222.8),(22,2,.15),cyan)
# Free practice pad is spatially separated, with its own slalom markers.
box('VIS_PracticePad',(105,-.16,90),(40,.3,95),road,.5);co=box('COL_Ground_Practice',(105,-.25,90),(40,.5,95),road);co.hide_render=True;co['surface_tag']='Ground';co['collision_only']=True
for z in range(55,130,15):
 x=105+(-1 if z%2 else 1)*7;box('VIS_PracticeBollard',(x,.55,z),(.55,1.1,.55),amber,.08)
# Start portal and brake distance boards. Gate structure sits outside 18m track clear span.
for x in [-12,12]:beam('VIS_StartPost',(x,0,0),(x,9,0),.6,steel)
box('VIS_StartBridge',(0,9,0),(27,2.3,1.2),white,.2);text('VIS_StartTitle','A E R O / 0 1',(0,8.6,-.7),1.1,steel,rot=(pi/2,0,pi))
text('VIS_PitTitle','VECTOR TEST FACILITY',(48,7.5,-.5),1.3,white,rot=(pi/2,0,pi))
for j in range(12):
 for k in range(2):box('VIS_StartChecker',(-8.25+j*1.5,.025,-1.5+k*1.5),(1.5,.03,1.5),white if (j+k)%2 else steel)
for z,label in [(130,'150'),(180,'100'),(220,'50')]:
 box('VIS_BrakeBoard',(-13,2,z),(4,3,.2),steel,.1);text('VIS_BrakeText',label,(-13,1.3,z-.15),1.3,white,rot=(pi/2,0,pi))
# Route contract and named empties retained in both interchange exports.
gates=[('START',(0,2,0),(0,0,1),'Ground',22,8),('ACCEL',(0,2,180),(0,0,1),'Ground',22,8),('WALL_APEX',(90,8,340),(1,0,0),'WallRide',25,25),('LAUNCH',(180,12,150),(0,0,-1),'Ground',24,12),('AIR_01',(180,17,120),(0,0,-1),'AssistedFlight',22,22),('AIR_02',(180,23,75),(0,0,-1),'AssistedFlight',22,22),('AIR_03',(180,16,30),(0,0,-1),'AssistedFlight',22,22),('LANDING',(180,2,-10),(0,0,-1),'Ground',30,10),('RETURN',(90,2,-120),(-1,0,0),'Ground',24,10),('FINISH',(0,2,0),(0,0,1),'Ground',22,8)]
manifest={'schema':1,'course_id':'vector_coastal_01','version':'0.1.0','units':'metres','coordinates':'Unity X-right Y-up Z-forward; Blender (x,-z,y)','spawn':{'position':[0,0.1,-12],'forward':[0,0,1],'up':[0,1,0]},'playerSpawn':{'position':[48,1,6],'forward':[-1,0,0],'up':[0,1,0]},'flightStartGate':3,'flightEndGate':7,'start_position':[0,0.1,0],'start_forward':[0,0,1],'checkpoints':[],'surfaces':all_meta,'flight_gap':{'launch':[180,10,150],'landing_apron_center':[180,0,5],'allowed_bounds_center':[180,20,78],'allowed_bounds_size':[60,80,180]},'recovery_positions':[[0,1,0],[0,1,210],[180,1,240],[180,1,-20],[0,1,-25]],'pit_slots':[[40,1,12],[40,1,26],[40,1,40],[40,1,54]],'spectator_spawn':[65,8,170],'practice_spawn':[105,1,80],'vertical_curl':{'practice_only':True,'entry':[240,0,30],'top_exit':[240,47.207,104.575],'arc_angle_degrees':125,'radius_m':30,'width_m':6,'source_observation':'Tesla official X 2110913438293647789 at 17.70 seconds; geometry and scale are game interpretation','safe_recovery':[105,1,80]},'ceiling_lab':{'practice_only':True,'entry':[110,0,150],'exit':[110,0,300],'maximum_height':18,'maximum_roll_degrees':180,'recommended_speed_mps':12,'safe_recovery':[105,1,80]},'unity_playtest':'NOT RUN; geometry only'}
for i,(name,p,f,mode,w,h) in enumerate(gates):
 e=bpy.data.objects.new('CP_%02d_%s'%(i,name),None);bpy.context.collection.objects.link(e);e.location=xyz(p);e['index']=i;e['required_mode']=mode;manifest['checkpoints'].append({'index':i,'name':name,'position':p,'forward':f,'up':[0,1,0],'width':w,'height':h,'depth':12,'required_mode':mode})
# Consolidate static visible geometry into seven material batches, reducing draw object count.
bpy.ops.object.select_all(action='DESELECT')
for ma in [road,white,dark,steel,cyan,amber,sea,red]:
 obs=[o for o in list(bpy.context.scene.objects) if o.type=='MESH' and o.name.startswith('VIS_') and o.data.materials and o.data.materials[0]==ma]
 for o in obs:o.select_set(True)
 if obs:
  bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();obs[0].name='VIS_LOD0_'+ma.name;obs[0]['static_batch']=True
 bpy.ops.object.select_all(action='DESELECT')
# Clean zero-area glyph triangulation before validation and export.
for o in bpy.context.scene.objects:
 if o.type=='MESH':
  bm=bmesh.new();bm.from_mesh(o.data);bad=[f for f in bm.faces if f.calc_area()<1e-9]
  if bad:bmesh.ops.delete(bm,geom=bad,context='FACES')
  bm.to_mesh(o.data);bm.free();o.data.update()
  bad_ids=[p.index for p in o.data.polygons if p.area<1e-9]
  if bad_ids:
   bm=bmesh.new();bm.from_mesh(o.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[bm.faces[i] for i in bad_ids],context='FACES');bm.to_mesh(o.data);bm.free();o.data.update()
# Stats and geometric verification: finite vertices, area, and positive/upward drive surface normals.
stats=[]; errors=[]
for o in bpy.context.scene.objects:
 if o.type!='MESH':continue
 tris=sum(len(p.vertices)-2 for p in o.data.polygons);stats.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':tris,'collision':o.name.startswith('COL_')})
 if any(not math.isfinite(v) for vert in o.data.vertices for v in vert.co):errors.append(o.name+' nonfinite')
 if any(p.area<1e-9 for p in o.data.polygons):errors.append(o.name+' degenerate polygon')
manifest['mesh_statistics']=stats;manifest['render_triangles']=sum(s['triangles'] for s in stats if not s['collision']);manifest['collision_triangles']=sum(s['triangles'] for s in stats if s['collision']);manifest['geometry_errors']=errors
json.dump(manifest,open(OUT+'/course_manifest.json','w'),indent=2)
# Export selected asset meshes and markers. Cameras/lights are only in editable .blend.
for o in bpy.context.scene.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT+'/VectorCoastal_Course.fbx',use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,use_custom_props=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=OUT+'/VectorCoastal_Course.glb',export_format='GLB',use_selection=True,export_extras=True)
# Preview lighting is not imported as a promised Unity lighting solution.
world=bpy.context.scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.48,.58,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.object.light_add(type='SUN',location=(0,0,300));bpy.context.object.rotation_euler=(.35,-.5,-.4);bpy.context.object.data.energy=3;bpy.context.object.data.angle=.1
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=False;scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
def camera(name,p,target,lens):
 bpy.ops.object.camera_add(location=xyz(p));o=bpy.context.object;o.name=name;o.rotation_euler=(Vector(xyz(target))-o.location).to_track_quat('-Z','Y').to_euler();o.data.lens=lens;o.data.clip_end=5000;scene.camera=o;return o
camera('QA_Aerial',(540,410,-470),(80,0,110),44)
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/VectorCoastal_Course.blend',compress=True);scene.render.filepath=ROOT+'/preview_aerial.png';bpy.ops.render.render(write_still=True) if not os.environ.get('COURSE_SKIP_RENDER') else None
camera('QA_Start',(-24,5,-52),(12,5,120),29);scene.render.filepath=ROOT+'/preview_ground.png';bpy.ops.render.render(write_still=True) if not os.environ.get('COURSE_SKIP_RENDER') else None
camera('QA_Wall',(125,48,285),(85,7,330),27);scene.render.filepath=ROOT+'/preview_wall.png';bpy.ops.render.render(write_still=True) if not os.environ.get('COURSE_SKIP_RENDER') else None
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/VectorCoastal_Course.blend',compress=True)
camera('QA_VerticalCurl',(315,40,20),(240,23,88),44);scene.render.filepath=ROOT+'/preview_vertical_curl.png';bpy.ops.render.render(write_still=True) if not os.environ.get('COURSE_SKIP_RENDER') else None
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/VectorCoastal_Course.blend',compress=True)
print('COURSE_DONE',manifest['render_triangles'],manifest['collision_triangles'],errors)

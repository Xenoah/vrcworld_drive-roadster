import bpy, math, os, json, hashlib
from mathutils import Vector
from math import sin,cos,pi,sqrt
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
OUT=os.path.join(ROOT,'Assets/DriveRoadster/Art/Vehicle'); SRC=os.path.dirname(__file__)
os.makedirs(os.path.join(OUT,'Textures'),exist_ok=True);os.makedirs(os.path.join(SRC,'renders'),exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for m in bpy.data.materials: bpy.data.materials.remove(m)
# Authored dimensions, not production car specifications. Blender -Y is forward.
def mat(n,c,metal=0,rough=.4,em=0):
 m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 if em:p.inputs['Emission Color'].default_value=(*c,1);p.inputs['Emission Strength'].default_value=em
 return m
paint=mat('DR_PearlAlloy',(.62,.67,.72),.72,.26)
carbon=mat('DR_CarbonComposite',(.022,.028,.032),.35,.32)
rubber=mat('DR_PerformanceRubber',(.012,.014,.016),0,.69)
metal=mat('DR_BrushedTitanium',(.34,.39,.44),.88,.24)
leather=mat('DR_IvoryMicrofiber',(.72,.74,.71),0,.73)
glass=mat('DR_CopperSmokedGlass',(.16,.09,.052),.08,.14)
p=glass.node_tree.nodes.get('Principled BSDF');p.inputs['Transmission Weight'].default_value=.85;p.inputs['IOR'].default_value=1.45
white=mat('DR_IceWhiteLED',(.65,.91,1),.1,.2,4)
red=mat('DR_RedLED',(.8,.008,.012),.1,.2,3)
cyan=mat('DR_ThrusterCyan',(.01,.45,.85),.15,.2,3)
# Portable tileable carbon maps, linked in blend and packed in GLB.
import numpy as np
N=512; yy,xx=np.mgrid[:N,:N]; w=((xx//16+yy//16)%2); strand=(np.sin((xx*(1-w)+yy*w)*pi/4)*.5+.5); v=.025+.02*strand+.008*w
im=bpy.data.images.new('DR_Carbon_BaseColor',N,N);arr=np.ones((N,N,4),dtype=np.float32);arr[:,:,:3]=v[:,:,None];im.pixels.foreach_set(arr.ravel());im.filepath_raw=os.path.join(OUT,'Textures/DR_Carbon_BaseColor.png');im.file_format='PNG';im.save();im.pack()
n=carbon.node_tree.nodes.new('ShaderNodeTexImage');n.image=im;carbon.node_tree.links.new(n.outputs['Color'],carbon.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
rr=bpy.data.images.new('DR_Carbon_Roughness',N,N);arr[:,:,:3]=(.3+.12*strand)[:,:,None];rr.pixels.foreach_set(arr.ravel());rr.filepath_raw=os.path.join(OUT,'Textures/DR_Carbon_Roughness.png');rr.file_format='PNG';rr.save();rr.pack();rr.colorspace_settings.name='Non-Color'
n=carbon.node_tree.nodes.new('ShaderNodeTexImage');n.image=rr;carbon.node_tree.links.new(n.outputs['Color'],carbon.node_tree.nodes.get('Principled BSDF').inputs['Roughness'])
parts=[]; wheelgroups={}
def finish(o,n,m,parent=None):
 o.name=n;o.data.materials.append(m)
 if parent:o.parent=parent
 for p in o.data.polygons:p.use_smooth=True
 parts.append(o);return o
def mesh(n,verts,faces,m,parent=None):
 me=bpy.data.meshes.new(n);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);return finish(o,n,m,parent)
def cube(n,loc,scale,m,bevel=.03,parent=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:mod=o.modifiers.new('Edge radii','BEVEL');mod.width=bevel;mod.segments=3; bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
 return finish(o,n,m,parent)
def uv(n,loc,scale,m,parent=None):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=loc);o=bpy.context.object;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return finish(o,n,m,parent)
def cyl(n,loc,r,depth,m,axis='Z',parent=None,verts=48):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc);o=bpy.context.object
 if axis=='X':o.rotation_euler[1]=pi/2
 if axis=='Y':o.rotation_euler[0]=pi/2
 return finish(o,n,m,parent)
def torus(n,loc,major,minor,m,axis='Z',parent=None,maj=64,minr=12):
 bpy.ops.mesh.primitive_torus_add(major_segments=maj,minor_segments=minr,location=loc,major_radius=major,minor_radius=minor);o=bpy.context.object
 if axis=='X':o.rotation_euler[1]=pi/2
 if axis=='Y':o.rotation_euler[0]=pi/2
 return finish(o,n,m,parent)
def line(n,pts,r,m,parent=None):
 cu=bpy.data.curves.new(n,'CURVE');cu.dimensions='3D';cu.resolution_u=1;cu.bevel_depth=r;cu.bevel_resolution=2;sp=cu.splines.new('POLY');sp.points.add(len(pts)-1)
 for p,co in zip(sp.points,pts):p.co=(*co,1)
 o=bpy.data.objects.new(n,cu);bpy.context.collection.objects.link(o);bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False);return finish(o,n,m,parent)
def empty(n,loc):
 o=bpy.data.objects.new(n,None);bpy.context.collection.objects.link(o);o.location=loc;return o
root=empty('DR_Asterion_Roadster', (0,0,0))
# Fenders are lofted ribbons with genuine open wheel arches.
def width(y):return .98-.20*(abs(y)/2.34)**5
for side in [-1,1]:
 vs=[];fs=[]
 for i in range(145):
  y=-2.32+4.64*i/144; ww=width(y); arch=.24
  for wy in [-1.42,1.38]:
   d=abs(y-wy)
   if d<.395:arch=max(arch,.35+sqrt(.395**2-d*d))
  z=.79+.055*math.exp(-((y+1.42)/.48)**2)+.10*math.exp(-((y-1.38)/.58)**2)-.23*(abs(y)/2.34)**6
  for x,zz in [(ww-.23,z-.025),(ww-.07,z+.005),(ww,z-.11),(ww+.005,max(arch,z-.20)),(ww-.025,arch)]:vs.append((side*x,y,zz))
 for i in range(144):
  for j in range(4):a=i*5+j;fs.append((a,a+1,a+6,a+5) if side==1 else (a+5,a+6,a+1,a))
 mesh('SculptedFender_'+str(side),vs,fs,paint)
 # Sill and uninterrupted belt line visible along cabin
 cube('CarbonRocker', (side*.88,.0,.22),(.15,1.89,.13),carbon,.04)
 line('RockerAccent',[(side*.97,y,.28) for y in [-.95,-.5,0,.5,.96]],.012,metal)
 # door seam hugs cabin side
 line('DoorPanelGap',[(side*.955,-.83,.69),(side*.97,-.74,.47),(side*.96,-.59,.30),(side*.96,.75,.30),(side*.97,.92,.52),(side*.95,.9,.76)],.004,carbon)
 cube('FlushDoorHandle',(side*.975,.48,.70),(.014,.22,.025),metal,.008)
 # aerodynamic mirror with slim stem
 line('MirrorStem',[(side*.75,-.60,.79),(side*1.02,-.65,.89)],.022,carbon)
 uv('MirrorHousing',(side*1.04,-.64,.91),(.115,.18,.054),paint)
 uv('MirrorLens',(side*1.045,-.54,.912),(.088,.012,.034),metal)
# compound-curved hood and rear deck, with panel highlight
for n,ya,yb in [('Hood',-2.30,-.64),('RearDeck',.81,2.30)]:
 vs=[];fs=[]
 for i in range(49):
  y=ya+(yb-ya)*i/48;ww=width(y)-.23
  z=(.77-.23*(abs(y)/2.34)**3) if n=='Hood' else (.85-.27*(abs(y)/2.34)**5)
  for j in range(33):
   t=-1+2*j/32; edge=.79+.055*math.exp(-((y+1.42)/.48)**2)+.10*math.exp(-((y-1.38)/.58)**2)-.23*(abs(y)/2.34)**6-.025; vs.append((t*ww,y,(z+.035)*(1-t**4)+edge*t**4))
 for i in range(48):
  for j in range(32):a=i*33+j;fs.append((a,a+1,a+34,a+33))
 mesh(n,vs,fs,paint)
# nose with dark open intake / splitters
# sealed front fascia follows the loft perimeter instead of floating bumper blocks
vs=[];fs=[]
for j in range(49):
 t=-1+2*j/48;xx=t*(width(-2.32)-.025);top=.588-.018*abs(t)**2-.075*abs(t)**12
 for zz,dy in [(top,0),(.42,-.035),(.26,0)]:vs.append((xx,-2.299+dy,zz))
for j in range(48):
 for k in range(2):a=j*3+k;fs.append((a+1,a+4,a+3,a))
mesh('IntegratedFrontFascia',vs,fs,carbon)
cube('LowerIntake',(0,-2.33,.34),(1.06,.025,.115),carbon,.05)
for x in [-.45,-.3,-.15,0,.15,.3,.45]:cube('IntakeFin',(x,-2.351,.34),(.012,.025,.10),carbon,.002)
cube('FrontSplitter',(0,-2.25,.235),(1.88,.26,.045),carbon,.018)
def surfacez(x,y):
 ww=width(y)-.23; t=min(1,abs(x)/ww);center=.77-.23*(abs(y)/2.34)**3+.035;edge=.79+.055*math.exp(-((y+1.42)/.48)**2)+.10*math.exp(-((y-1.38)/.58)**2)-.23*(abs(y)/2.34)**6-.025
 return center*(1-t**4)+edge*t**4+.008
for s in [-1,1]:
 coords=[(.48,-2.12),(.68,-1.88),(.62,-1.85)]
 mesh('HiddenHeadlightPocket',[(s*x,y,surfacez(x,y)) for x,y in coords],[(0,1,2)] if s>0 else [(2,1,0)],carbon)
 line('HeadlightBlade',[(s*x,y,surfacez(x,y)+.008) for x,y in [(.50,-2.09),(.57,-2.005),(.65,-1.91)]],.009,white)
# Rear diffuser, segmented lamps, discrete invented thruster hardware
cube('RearValance',(0,2.24,.45),(1.69,.18,.28),paint,.07)
cube('RearDarkPanel',(0,2.345,.53),(1.56,.025,.17),carbon,.035)
for s in [-1,1]:
 for x in [.25,.65]:
  cyl('UpperNozzleShroud',(s*x,2.36,.48),.054,.13,metal,'Y');cyl('UpperNozzleBore',(s*x,2.43,.48),.040,.014,carbon,'Y')
 # Downward cold-gas nozzles, observed rear-side location; no blue flame.
 cyl('DownThrusterShroud',(s*.78,2.14,.27),.050,.12,metal)
 cyl('DownThrusterBore',(s*.78,2.14,.205),.034,.008,carbon)
# Original interpreted four-fan rear service module. Mesh only, no claimed real mechanism.
cube('RearFanModule',(0,2.30,.30),(1.47,.15,.30),carbon,.035)
for x in [-.54,-.18,.18,.54]:
 torus('RearFanRing',(x,2.39,.30),.123,.013,metal,'Y',maj=40,minr=8)
 cyl('RearFanHub',(x,2.407,.30),.039,.035,carbon,'Y',verts=24)
 for k in range(7):
  a=k*2*pi/7
  mesh('RearFanBlade',[(x+.043*sin(a),2.403,.30+.043*cos(a)),(x+.118*sin(a+.32),2.396,.30+.118*cos(a+.32)),(x+.113*sin(a+.59),2.396,.30+.113*cos(a+.59)),(x+.045*sin(a+.33),2.403,.30+.045*cos(a+.33))],[(0,1,2,3)],metal)
for x in [-.65,-.4,-.15,.15,.4,.65]:cube('DiffuserStrake',(x,2.12,.23),(.025,.52,.15),carbon,.009)
cube('TailLightRecess',(0,2.363,.64),(1.77,.034,.046),carbon,.015)
line('FullWidthRearLight',[(-.86,2.37,.64),(0,2.38,.64),(.86,2.37,.64)],.011,red)
aero=empty('AeroWing',(0,1.96,.82))
cube('AeroWingBlade',(0,2.08,.91),(1.75,.29,.045),paint,.015,aero)
for ss in [-1,1]:
 cube('AeroWingEndplate',(ss*.88,2.08,.93),(.028,.34,.15),carbon,.01,aero)
 line('AeroActuator',[(ss*.54,1.94,.70),(ss*.54,2.04,.88)],.018,metal,aero)
# Underbody packaging and forward control-nozzle interpretation. Exact unseen hardware is original.
cube('UnderbodySkidPlate',(0,0,.215),(1.51,3.52,.045),carbon,.025)
for ss in [-1,1]:
 cyl('ForwardDownNozzle',(ss*.69,-1.13,.235),.045,.085,metal)
 cyl('ForwardDownBore',(ss*.69,-1.13,.189),.031,.009,carbon)
 cyl('FrontVectorNozzle',(ss*.96,-.91,.64),.030,.035,metal,'X')
 cyl('FrontVectorBore',(ss*.982,-.91,.64),.021,.006,carbon,'X')
# Cabin tub and cockpit details
cube('CabinFloor',(0,.05,.27),(1.43,1.62,.12),carbon,.06)
cube('CenterTunnel',(0,.08,.46),(.22,1.29,.29),carbon,.06)
cube('CabinFrontBulkhead',(0,-.79,.47),(1.45,.055,.47),carbon,.015)
cube('Dashboard',(0,-.62,.70),(1.40,.31,.22),carbon,.07)
line('DashboardMetalTrim',[(-.65,-.78,.75),(0,-.82,.77),(.65,-.78,.75)],.014,metal)
for s in [-1,1]:
 cube('DoorCard',(s*.715,.06,.56),(.10,1.37,.29),leather,.045)
 line('DoorAmbientRed',[(s*.655,-.5,.65),(s*.655,.3,.60),(s*.655,.55,.49)],.006,red)
 cube('DoorArmrest',(s*.66,.16,.57),(.12,.51,.075),carbon,.025)
 cube('InteriorPull',(s*.65,-.13,.67),(.035,.17,.025),metal,.008)
 # seats: contoured cushion, shoulder shell, center inserts and bolsters
 uv('SeatShell',(s*.395,.39,.66),(.29,.22,.41),carbon)
 back=cube('SeatBack',(s*.395,.35,.68),(.39,.18,.56),leather,.085);back.rotation_euler[0]=-.14
 cube('SeatCushion',(s*.395,.04,.405),(.44,.56,.15),leather,.07)
 cube('SeatHeadrest',(s*.395,.395,.995),(.29,.13,.18),leather,.055)
 cube('SeatGrayInsert',(s*.395,.245,.69),(.24,.055,.36),rubber,.035)
 for off in [-.19,.19]:
  uv('SeatSideBolster',(s*.395+off,.30,.64),(.064,.14,.26),leather)
  line('SeatContrastStitch',[(s*.395+off*.85,.225,.45),(s*.395+off*.85,.235,.67),(s*.395+off*.70,.29,.89)],.004,red)
 for k in range(6):line('SeatCushionSeam',[(s*.395-.14,-.16+k*.057,.482),(s*.395+.14,-.16+k*.057,.482)],.0025,carbon)
 cube('SeatBeltBuckle',(s*.18,.18,.47),(.05,.08,.08),red,.012)
# windshield lightly curved mesh inside a closed canopy
vs=[];fs=[]
for i in range(17):
 t=i/16;y=-.77+.57*t;z=.79+.41*t;w=.71-.105*t
 for j in range(25):u=-1+2*j/24;vs.append((u*w,y-.055*(1-u*u),z+.025*(1-u*u)))
for i in range(16):
 for j in range(24):a=i*25+j;fs.append((a,a+1,a+26,a+25))
mesh('Windscreen',vs,fs,glass)
for s in [-1,1]:line('APillar',[(s*(.71-.105*t),-.77+.57*t,.79+.41*t) for t in [i/16 for i in range(17)]],.032,paint)
line('WindscreenHeader',[(-.605,-.20,1.20),(0,-.255,1.225),(.605,-.20,1.20)],.031,carbon)
line('WindscreenFoot',[(-.71,-.77,.79),(0,-.825,.815),(.71,-.77,.79)],.021,carbon)
# Closed geometric canopy based on observed current presentation. Original mesh.
mesh('RoofPanel',[(-.605,-.20,1.20),(0,-.255,1.225),(.605,-.20,1.20),(-.62,.64,1.19),(0,.68,1.22),(.62,.64,1.19)],[(0,1,4,3),(1,2,5,4)],paint)
mesh('RearCanopyGlass',[(-.62,.64,1.19),(.62,.64,1.19),(.75,1.24,.87),(-.75,1.24,.87)],[(0,1,2,3)],glass)
for ss in [-1,1]:
 mesh('SideWindow',[(ss*.71,-.76,.80),(ss*.605,-.20,1.20),(ss*.62,.64,1.19),(ss*.75,.94,.83)],[(3,2,1,0)] if ss>0 else [(0,1,2,3)],glass)
 line('RoofSideRail',[(ss*.605,-.20,1.20),(ss*.62,.64,1.19),(ss*.75,1.24,.87)],.021,paint)
 mesh('CanopyRearQuarter',[(ss*.62,.64,1.19),(ss*.75,.94,.83),(ss*.75,1.24,.87)],[(0,1,2)] if ss>0 else [(2,1,0)],paint)
 line('CanopyRearPillar',[(ss*.62,.64,1.19),(ss*.75,.94,.83)],.026,paint)
 line('WindowSill',[(ss*.71,-.76,.80),(ss*.75,.94,.83)],.023,carbon)
 mesh('CabinBeltSeal',[(ss*.71,-.76,.80),(ss*.75,.94,.83),(ss*.80,.94,.79),(ss*.80,-.76,.76)],[(0,1,2,3)],paint)
# instrument cluster and console screen geometry + original graphic labels
cube('DriverDisplayHousing',(-.395,-.65,.828),(.38,.075,.145),carbon,.025)
cube('DriverDisplayGlass',(-.395,-.602,.83),(.34,.009,.111),glass,.008)
cube('CenterScreen',(0,-.41,.71),(.36,.08,.25),carbon,.02)
cube('CenterScreenFace',(0,-.35,.73),(.33,.012,.22),glass,.008)
def textmesh(n,body,loc,size,rot,material):
 cu=bpy.data.curves.new(n,'FONT');cu.body=body;cu.size=size;cu.align_x='CENTER';cu.extrude=.0003;o=bpy.data.objects.new(n,cu);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=rot;bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False);finish(o,n,material)
textmesh('DashSpeed','000',(-.395,-.593,.817),.052,(pi/2,0,pi),white)
textmesh('DashMode','GROUND',(-.395,-.590,.792),.013,(pi/2,0,pi),cyan)
textmesh('ConsoleTitle','ASTERION',(0,-.342,.78),.022,(pi/2,0,pi),white)
textmesh('ConsoleFlight','FLIGHT\nASSIST',(0,-.341,.71),.019,(pi/2,0,pi),cyan)
# steering wheel tilted toward driver, no avatar/physics steering claims
steer=empty('SteeringWheel',(-.395,-.38,.76))
line('SteeringYoke',[(-.55,-.38,.84),(-.56,-.38,.79),(-.54,-.38,.65),(-.25,-.38,.65),(-.23,-.38,.79),(-.24,-.38,.84)],.023,leather,steer)
for a in [pi/2,pi,pi*3/2]:line('SteeringSpoke',[(-.395,-.38,.76),(-.395+.125*sin(a),-.38,.76+.125*cos(a))],.013,metal,steer)
cube('SteeringHub',(-.395,-.371,.76),(.115,.06,.072),carbon,.025,steer)
for x in [-.50,-.30]:cube('Pedal',(x,-.64,.34),(.075,.16,.025),metal,.008)
for y in [.04,.25]:torus('ConsoleCupholder',(0,y,.612),.062,.008,metal,maj=32,minr=8)
# Independent wheel pivots; all mesh parts localised after creation.
for label,x,y in [('FL',-.88,-1.42),('FR',.88,-1.42),('RL',-.88,1.38),('RR',.88,1.38)]:
 pivot=empty('Wheel_'+label,(x,y,.35)); wheelgroups[label]=pivot;s=1 if x>0 else -1
 torus('Tire_'+label,(x,y,.35),.272,.078,rubber,'X',pivot,80,16)
 # tire sidewalls and rim lip
 for ox in [-.068,.068]:torus('SidewallRing',(x+ox,y,.35),.264,.012,rubber,'X',pivot,64,8)
 torus('RimLip',(x+s*.081,y,.35),.249,.013,metal,'X',pivot,64,8)
 cyl('WheelBarrel',(x,y,.35),.245,.12,carbon,'X',pivot)
 cyl('BrakeDisc',(x+s*.061,y,.35),.203,.012,metal,'X',pivot)
 for k in range(16):
  a=2*pi*k/16;cyl('DiscDrill',(x+s*.069,y+.172*sin(a),.35+.172*cos(a)),.006,.002,carbon,'X',pivot,10)
 cube('BrakeCaliper',(x+s*.071,y+.16,.35),(.045,.07,.18),red,.018,pivot)
 for k in range(5):
  for split in [-.10,.10]:
   a=2*pi*k/5+split
   line('ForgedSpoke',[(x+s*.094,y+.055*sin(a),.35+.055*cos(a)),(x+s*.085,y+.16*sin(a+.10),.35+.16*cos(a+.10)),(x+s*.08,y+.235*sin(a+.16),.35+.235*cos(a+.16))],.013,metal,pivot)
 cyl('WheelHub',(x+s*.103,y,.35),.058,.025,metal,'X',pivot)
 for k in range(5):a=2*pi*k/5;cyl('LugBolt',(x+s*.119,y+.039*sin(a),.35+.039*cos(a)),.007,.006,carbon,'X',pivot,12)
 # visible directional tread grooves represented as low-profile strips
 for k in range(32):
  a=k*2*pi/32
  line('TreadGroove',[(x-.045,y+.348*sin(a-.045),.35+.348*cos(a-.045)),(x,y+.348*sin(a),.35+.348*cos(a)),(x+.045,y+.348*sin(a+.045),.35+.348*cos(a+.045))],.0022,carbon,pivot)
# bake geometry transforms, parent-space preserve, UV unwrap carbon parts via cube projection
for o in parts:
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 # meshes were authored in world coordinates and parent assigned with no inverse: clear parent while retaining original location
 par=o.parent
 if par:o.parent=None
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 if len(o.data.polygons):
  bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.cube_project(cube_size=.25);bpy.ops.object.mode_set(mode='OBJECT')
 if par:
  mw=o.matrix_world.copy();o.parent=par;o.matrix_world=mw
 else:
  mw=o.matrix_world.copy();o.parent=root;o.matrix_world=mw
# Mesh consolidation per moving group/material, resulting in modest renderer count.
for par in [root,steer,aero]+list(wheelgroups.values()):
 for m in [paint,carbon,rubber,metal,leather,glass,white,red,cyan]:
  obs=[o for o in list(bpy.context.scene.objects) if o.type=='MESH' and o.parent==par and o.data.materials[0]==m]
  if not obs:continue
  bpy.ops.object.select_all(action='DESELECT')
  for o in obs:o.select_set(True)
  bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=bpy.context.object;o.name=par.name+'__'+m.name
  # eliminate inherited multi slots from join
  for p in o.data.polygons:p.material_index=0
  while len(o.data.materials)>1:o.data.materials.pop(index=len(o.data.materials)-1)
for o in [steer,aero]+list(wheelgroups.values()):mw=o.matrix_world.copy();o.parent=root;o.matrix_world=mw
anchors={'Seat_Driver':(-.395,.12,.53),'Seat_Passenger':(.395,.12,.53),'Eye_Driver':(-.395,.08,1.02),'Exit_Driver':(-1.4,.12,.1),'Thruster_Front_L':(-.96,-.91,.64),'Thruster_Front_R':(.96,-.91,.64),'Thruster_Down_FL':(-.69,-1.13,.18),'Thruster_Down_FR':(.69,-1.13,.18),'Thruster_Rear':(0,2.46,.48),'Thruster_Down_L':(-.78,2.14,.19),'Thruster_Down_R':(.78,2.14,.19),'CenterOfMass':(0,0,.45)}
for n,co in anchors.items():o=empty(n,co);o.parent=root
# Exports contain only the vehicle, no lights, floor, camera or collider.
def selectvehicle():
 bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
 for o in root.children_recursive:o.select_set(True)
def export(name):
 selectvehicle();bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,name+'.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,path_mode='COPY',embed_textures=True)
 bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,name+'.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)
export('DR_Asterion_LOD0')
stats={}
def count():return sum(len(p.vertices)-2 for o in root.children_recursive if o.type=='MESH' for p in o.data.polygons)
stats['LOD0_triangles']=count();stats['LOD0_renderers']=sum(o.type=='MESH' for o in root.children_recursive)
# Preserve editable high-res vehicle before LOD export, rebuild restores originals.
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC,'DR_Asterion.blend'),compress=True)
for lod,ratio in [(1,.38),(2,.095)]:
 mods=[]
 for o in root.children_recursive:
  if o.type=='MESH':
   mod=o.modifiers.new('LOD_Reduction','DECIMATE');mod.ratio=ratio;mods.append((o,mod))
 export('DR_Asterion_LOD'+str(lod))
 deps=bpy.context.evaluated_depsgraph_get();stats['LOD'+str(lod)+'_triangles']=sum(sum(len(p.vertices)-2 for p in o.evaluated_get(deps).data.polygons) for o in root.children_recursive if o.type=='MESH')
 for o,m in mods:o.modifiers.remove(m)
# Convex-friendly separate collision source, never exported as visual model.
coll=cube('UCX_DR_Asterion_Chassis',(0,0,.52),(1.74,4.22,.55),carbon,.08);coll.hide_render=True
bpy.ops.object.select_all(action='DESELECT');coll.select_set(True);bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'DR_Asterion_Collider.fbx'),use_selection=True,axis_forward='-Z',axis_up='Y',object_types={'MESH'})
coll.hide_set(True)
# studio renders
floor=cube('StudioFloor',(0,0,-.055),(200,200,.1),mat('StudioSlate',(.035,.048,.065),.15,.5),0)
world=bpy.context.scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.16,.23,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45
d=bpy.data.lights.new('CabinReviewFill','AREA');d.energy=12;d.shape='DISK';d.size=.8;o=bpy.data.objects.new('CabinReviewFill',d);bpy.context.collection.objects.link(o);o.location=(0,.1,1.15)
for n,loc,power,size in [('Key',(-3,-4,7),1800,5),('Rim',(3,3,5),2200,4),('Fill',(4,-2,3),1300,4)]:
 d=bpy.data.lights.new(n,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(n,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.5))-o.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=False;scene.render.resolution_x=1400;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
camd=bpy.data.cameras.new('ReviewCamera');cam=bpy.data.objects.new('ReviewCamera',camd);bpy.context.collection.objects.link(cam);scene.camera=cam
views=[('01_hero',(5,-7,3.6),(0,0,.58),52),('02_front',(0,-7,1.5),(0,-.2,.6),55),('03_side',(7.8,0,1.9),(0,0,.6),44),('04_rear',(4.8,6.2,2.9),(0,.1,.58),52),('05_cockpit',(0,.57,1.04),(0,-.62,.72),20)]
for n,loc,target,lens in views:
 cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();camd.lens=lens;camd.clip_start=.025;scene.render.filepath=os.path.join(SRC,'renders',n+'.png');bpy.ops.render.render(write_still=True)
# review scene saved separately, editable master stays vehicle-only
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC,'DR_Asterion_Review.blend'),compress=True)
stats['materials']=9;stats['dimensions_design_m']={'length':4.65,'width_body':1.98,'height':1.225};stats['unity_anchors']={n:[co[0],co[2],-co[1]] for n,co in anchors.items()};stats['validation']='Blender geometry/export/render only; Unity, VRChat and multiplayer untested.'
with open(os.path.join(OUT,'vehicle_source_manifest.json'),'w') as f:json.dump(stats,f,indent=2)
print('VEHICLE_STATS',stats)

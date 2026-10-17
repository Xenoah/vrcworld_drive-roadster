import bpy,os
p=os.path.dirname(__file__)
bpy.ops.wm.open_mainfile(filepath=os.path.join(p,'DR_Asterion.blend'))
s=open(os.path.join(p,'build_vehicle.py')).read()
# Execute helper definitions only plus studio stage against saved high-detail model.
exec(s[s.index('def finish('):s.index('root=empty(')])
root=bpy.data.objects['DR_Asterion_Roadster'];parts=[]
import math
from mathutils import Vector
from math import pi
SRC=p
exec(s[s.index('def mat('):s.index("paint=mat(")])
exec(s[s.index('# studio renders'):s.index("stats['materials']")])

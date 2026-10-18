"""Original procedural audio and readable signage. Reference video is never bundled."""
from pathlib import Path
import json, colorsys, wave
import numpy as np
from scipy.signal import butter, sosfilt
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/DriveRoadster/Art/Airport'
AUDIO=ROOT/'Assets/DriveRoadster/Audio'
OUT.mkdir(parents=True,exist_ok=True); AUDIO.mkdir(parents=True,exist_ok=True)
SR=22050
rng=np.random.default_rng(481)
def wav(name,x,peak=.8):
    x=np.nan_to_num(x); x=x/max(1e-8,np.max(np.abs(x)))*peak
    with wave.open(str(AUDIO/(name+'.wav')),'wb') as f:
        f.setnchannels(1 if x.ndim==1 else x.shape[1]); f.setsampwidth(2); f.setframerate(SR)
        f.writeframes((np.clip(x,-1,1)*32767).astype('<i2').tobytes())
def filtered(n,lo,hi):
    return sosfilt(butter(3,[lo,hi],btype='bandpass',fs=SR,output='sos'),rng.normal(size=n))
def loopnoise(seconds,lo,hi):
    n=int(seconds*SR); x=filtered(n+SR,lo,hi)[SR:]
    k=SR//5; fade=np.linspace(0,1,k); x[:k]=x[:k]*fade+x[-k:]*(1-fade)
    return x[:-k]
t=np.arange(SR*4)/SR
motor=sum(a*np.sin(2*np.pi*f*t+.12*np.sin(2*np.pi*6*t)) for f,a in [(120,.48),(240,.22),(480,.12),(960,.08),(1440,.025)])
wav('EV_Motor_Loop',motor,.65)
wav('Tyre_Wind_Loop',loopnoise(6,110,2400),.7)
wav('Cold_Gas_Loop',loopnoise(5,100,7500),.85)
t=np.arange(int(SR*.55))/SR
wav('Touchdown',filtered(len(t),45,1800)*np.exp(-t*9)+.3*np.sin(2*np.pi*(70*t-30*t*t))*np.exp(-t*12),.75)
beat=.6; duration=64*beat; N=int(duration*SR)
for tr,roots in enumerate([[45,41,48,43],[48,43,45,41],[41,48,43,45]],1):
    music=np.zeros(N); notes=[0,7,12,15,19,12,7,3]
    def add(start,signal,amp=1):
        idx=int(start*SR); ids=(idx+np.arange(len(signal)))%N
        np.add.at(music,ids,signal*amp)
    for b in range(64):
        tt=np.arange(int(.34*SR))/SR
        kick=np.sin(2*np.pi*(47*tt+95*.025*(1-np.exp(-tt/.025))))*np.exp(-tt*16)
        add(b*beat,kick,.36)
        if b%4 in [1,3]: add(b*beat,filtered(int(.17*SR),600,7000)*np.exp(-np.arange(int(.17*SR))/SR*24),.12)
        ht=np.arange(int(.06*SR))/SR
        add((b+.5)*beat,filtered(len(ht),5000,9500)*np.exp(-ht*65),.045)
        root=roots[(b//16)%4]
        tt=np.arange(int(.5*SR))/SR; freq=440*2**((root-69)/12)
        add(b*beat,np.sin(2*np.pi*freq*tt)*(1-np.exp(-tt*60))*np.exp(-tt*6),.21)
        if b%2==0:
            note=root+12+notes[(b//2+tr)%8]; freq=440*2**((note-69)/12)
            tt=np.arange(int(1.4*SR))/SR
            pluck=(np.sin(2*np.pi*freq*tt)+.2*np.sin(4*np.pi*freq*tt))*np.exp(-tt*4)*(1-np.exp(-tt*100))
            add(b*beat,pluck,.095); add((b+.75)*beat,pluck,.032)
    # Circular delay provides an exact musical loop; no gaps or clipping.
    left=music+.13*np.roll(music,int(.45*SR)); right=music+.13*np.roll(music,int(.75*SR))
    wav('Airport_BGM_%02d'%tr,np.stack([left,right],axis=1),.65)

FONT='C:/Windows/Fonts/YuGothM.ttc'; BOLD='C:/Windows/Fonts/YuGothB.ttc'
def font(n,b=False):return ImageFont.truetype(BOLD if b else FONT,n)
navy=(12,25,35); white=(232,240,240); mute=(158,186,195); orange=(255,146,55); cyan=(80,212,221)
for i in range(32):
    c=tuple(int(v*255) for v in colorsys.hsv_to_rgb((i*.61803398875)%1,.72,.95))
    im=Image.new('RGB',(256,256),navy);d=ImageDraw.Draw(im)
    d.rectangle((0,0,255,28),fill=c);d.rectangle((0,224,255,255),fill=c)
    d.text((128,108),f'{i+1:02d}',font=font(116,True),fill=white,anchor='mm')
    d.text((128,185),'ASTERION',font=font(24,True),fill=c,anchor='mm')
    im.save(OUT/f'Livery_{i+1:02d}.png')

from build_airport_signs import generate_signs
generate_signs()

points=[[-280,0,-260],[-280,0,100],[-280,0,470],[-240,3,570],[-130,8,590],[-60,3,530],[-90,0,440],[20,0,380],[120,0,460],[240,0,450],[340,9,350],[380,22,240],[360,18,100],[300,0,30],[350,0,-60],[420,0,-150],[400,0,-290],[280,0,-380],[140,0,-350],[40,0,-450],[-100,0,-470],[-220,0,-410],[-280,0,-330]]
data={'land_size':[1800,1600],'points':points,'flight_range':[10.12,12.85],
      'reference':'https://www.youtube.com/watch?v=r5vsHHPVhJg','audio':'Original synthesis; reference audio is not distributed.'}
(OUT/'airport_layout.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
im=Image.new('RGB',(1600,1600),navy);d=ImageDraw.Draw(im)
def xy(p):return (int(800+p[0]*1.05),int(850-p[2]*1.05))
d.text((70,55),'AIRFIELD / COURSE MAP',font=font(60,True),fill=white)
for x in [-280,-360]:
    a=xy([x-23,0,-580]);b=xy([x+23,0,650]);d.rectangle((a[0],b[1],b[0],a[1]),fill=(39,52,60))
pp=[xy(p) for p in points+[points[0]]]
smooth=[]
for i in range(len(points)):
    a,b,c,e=[np.array(points[k%len(points)],dtype=float) for k in [i-1,i,i+1,i+2]]
    for t in np.linspace(0,1,24,endpoint=False):smooth.append(xy(.5*(2*b+(-a+c)*t+(2*a-5*b+4*c-e)*t*t+(-a+3*b-3*c+e)*t*t*t)))
smooth.append(smooth[0]);d.line(smooth,fill=cyan,width=12,joint='curve');d.line(smooth[240:313],fill=orange,width=12)
for seq,k in enumerate([0,2,4,6,8,10,11,12,13,15,17,19,21],1):
    x,y=pp[k];d.ellipse((x-12,y-12,x+12,y+12),fill=white)
    d.text((x+20,y-22),f'{seq:02d}',font=font(26,True),fill=white)
d.text(xy([-135,0,40]),'STUNT\nPRACTICE',font=font(23,True),fill=orange)
x,y=xy([-500,0,-260]);d.rounded_rectangle((x-65,y-65,x+65,y+65),radius=8,fill=(69,92,105));d.text((x,y),'P\n32',font=font(36,True),fill=white,anchor='mm',align='center')
d.text((75,1430),'CYAN: CIRCUIT     ORANGE: FLIGHT     P: 32 CAR PADDOCK',font=font(30),fill=mute)
im.save(OUT/'Course_Map.png')
print('Generated 4 SFX, 3 looping music tracks, 32 liveries and 4 signs.')

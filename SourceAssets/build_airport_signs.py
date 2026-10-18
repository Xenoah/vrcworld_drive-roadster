"""Generate readable PC/VR instruction boards from the actual vehicle bindings."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[1] / 'Assets/DriveRoadster/Art/Airport'
FONT = 'C:/Windows/Fonts/YuGothM.ttc'
BOLD = 'C:/Windows/Fonts/YuGothB.ttc'
NAVY, WHITE, MUTED = (12,25,35), (232,240,240), (158,186,195)
ORANGE, CYAN = (255,146,55), (80,212,221)

def text(draw, xy, value, size, colour, bold=False, width=1868):
    while True:
        font = ImageFont.truetype(BOLD if bold else FONT, size)
        box = draw.textbbox((0,0), value, font=font)
        if box[2]-box[0] <= width:
            break
        size -= 1
        if size < 27:
            raise ValueError('Board text too long: '+value)
    draw.text(xy, value, font=font, fill=colour)

def board(name, title, subtitle, sections, footer):
    im=Image.new('RGB',(2048,1536),NAVY);d=ImageDraw.Draw(im)
    d.rectangle((0,0,2048,20),fill=CYAN)
    text(d,(90,60),title,76,WHITE,True)
    text(d,(92,177),subtitle,43,CYAN)
    d.line((90,260,1958,260),fill=(54,76,88),width=3)
    for y, heading, lines in sections:
        text(d,(90,y),heading,43,ORANGE,True)
        for j,line in enumerate(lines):
            text(d,(90,y+74+j*54),line,35,WHITE)
    text(d,(90,1435),footer,29,MUTED)
    im.save(OUT/name)

def generate_signs():
    OUT.mkdir(parents=True,exist_ok=True)
    board('Entrance_Controls_JA.png','ASTERION  /  AIRFIELD','DRIVING GUIDE   乗車・走行・車内操作',[
        (300,'01  乗車 / DRIVE',[
            '番号付きの車を選び、左ドアの DRIVER ACCESS を操作',
            'PC：W / S 加速・制動、A / D 操舵。離すと推力０で惰性走行',
            'VR：右グリップでヨーク保持、右トリガー加速、左トリガー制動',
            '左スティック左右で操舵。詳細は「VR CONTROLLERS」看板へ']),
        (640,'02  ジャンプ・視点 / COCKPIT',[
            'Space / VRのJump を長押し → 上昇スラスターが継続噴射',
            'AUTO：空中はスラスターで水平保持。MANUAL：自分で姿勢操作',
            '車内 VIEW − / ＋：目線を上下。DEFAULT：標準の高さへ',
            'PCは PageDown / PageUp / Home。PC / VR ボタンで説明切替']),
        (980,'03  復帰・計測 / RETURN & TIME ATTACK',[
            'Backspace / RESET：自分の駐車枠へ（走行中は２度操作）',
            'X / EXIT：入口へ降車。VRはヨークを離して車内ボタンを操作',
            'START手前で停止 → T / TIME ATTACK → 順番にゲートを通過',
            'BGM：入口の操作盤で ON/OFF・曲送り・音量調整'])
    ],'32 CARS / 譲り合って走行してください。斜面の吸着ファンは自動作動（最大２G）。')
    board('Thruster_Controls_JA.png','THRUSTERS  /  PC CONTROLS','長押しで上昇・AUTOで水平保持',[
        (300,'01  Space 長押し / AUTO・MANUAL 共通',[
            '平地で Space → ジャンプ。押し続けると下向き４基が継続噴射',
            '離すと上昇噴射が止まり、重力で降下。高度上限は100m',
            'AUTO：前後・左右の噴射を自動調整し、空中で水平を維持',
            'Space を離しても水平制御は継続。斜面では路面の姿勢を優先']),
        (640,'02  MANUAL / 自分で姿勢を操作',[
            'F / AUTOボタンで切替。一度入力を離してから Space で離陸',
            '離陸後は W/S・A/D・Q/E を一度中立に。Space は押したままでOK',
            'W / S：機首下げ / 上げ（後方２基 / 前方２基を強く噴射）',
            'Q / E：左 / 右ロール（右側２基 / 左側２基）。A / D：ヨー']),
        (980,'03  推力・着地 / LANDING',[
            'MANUAL空中：Ctrl 前進推力、Shift 制動。Space は上昇専用',
            'AUTO：W / S 前進・制動。キーを離すと推力０、速度は慣性で残る',
            'L / LANDING：着陸補助。MANUALでは着地前に車体を起こす',
            'F / AUTOへ戻すと水平復帰。VR操作は隣の専用看板を確認'])
    ],'白煙は噴射停止後も残ります。上昇・自動姿勢制御・着陸補助はそれぞれ独立します。')
    board('VR_Controls_JA.png','VR CONTROLLERS','左右の操作 / Touch系の標準Jumpは右手のA',[
        (300,'01  ヨークを握って走行 / 左右の役割',[
            '右グリップ（中指）：ハンドル付近で握り、ヨークを保持',
            '右トリガー（人差し指）：前進・加速。左トリガー：制動',
            '左スティック左右：操舵・ヨー。上下と左グリップは車両操作に未使用',
            '右グリップを離すと前進・上昇入力を解除。制動が加速より優先']),
        (640,'02  Jump 長押し / AUTO・MANUAL',[
            '右グリップ保持＋Jump 長押し：平地で離陸し、上昇噴射を継続',
            'Touch系は標準で右A。他の機種・変更済み設定は割当済みJumpを使用',
            'AUTO：スラスターで自動水平保持。Jumpを離しても姿勢制御は継続',
            'MANUAL：右手首を前後に傾けてピッチ、左右にひねってロール']),
        (980,'03  車内ボタン / Interact',[
            '右グリップを離す → 車内ボタンに照準を合わせて Interact',
            'AUTO / MANUAL：姿勢モード。LANDING：着陸補助の切替',
            'VIEW − / ＋：目線の高さ。DEFAULT：標準へ。PC / VR：説明切替',
            'TIME ATTACK：計測開始。RESET：駐車枠へ。EXIT：入口へ降車'])
    ],'Jump割当はVRChatの設定に従います。参考：docs.vrchat.com/docs/touch / steamvr-input-20')
    print('Generated entrance, PC thruster and VR controller boards.')

if __name__=='__main__':
    generate_signs()

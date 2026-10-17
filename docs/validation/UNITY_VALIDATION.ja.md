# Unity / ClientSim 検証記録

最終実行UTC: `2026-10-17T10:36:45.3369859Z`。**105/105項目成功**。生データは[clientsim-smoke.json](clientsim-smoke.json)。

## 環境と入力の範囲

Unity 2022.3.22f1 / VRChat Base・Worlds 3.10.5 / Windows / NVIDIA GeForce RTX 3070。ClientSimのローカルsimulated player 1人で、実Udon VMとPhysXを実行した。

SDK移動・Jump・Use・Grabイベントと実行中Interactを送信する。Desktop Q/E・Shiftの独立性検査はキー状態アダプター値を注入し、中立待機時計を一部の境界試験で進める。VRはEditor限定でプレイヤーAPIのVR識別・右手追跡データを合成し、終了時にフックを復元する。配布ワールドにはこのフィクスチャを含めない。**実キーボード、実トリガー、HMD、実リマップの検証ではない。**

バンク・飛行・着陸試験は途中位置・姿勢・速度を設定する。完全な一周ではない。所有権通知と休止は単一ClientSim内の分岐確認で、複数クライアントや実Alt-Tabの証拠ではない。SDK3.10.5はApplication.isFocusedをUdonへ公開していない。OSフォーカス／SteamVRダッシュボード／実トラッキング喪失時の解除は未検証。

## 結果

Unity C#・UdonSharpコンパイル、生成済みシーンのPlay Mode起動に成功。Python静的・数理33件とC#構文解析は別途実施する検査で、CIはUnity実行試験ではない。

| 検査 | 結果 | 詳細 |
|---|---|---|
| ClientSim initialized | PASS | one local player |
| Udon programs initialized | PASS | Vehicle and Run backing behaviours |
| Scene colliders | PASS | mesh colliders=15 |
| Vehicle LODs | PASS | 3 LOD levels |
| Wheel visual pivots connected | PASS | connected=12/12 |
| FBX markers match manifest | PASS | maximum position error=0.000000 m |
| Station entry | PASS | driver=1 |
| Free drive acceleration | PASS | position=(0.000, -0.035, 35.883); speed=40.413; mode=1 |
| Time attack start gate | PASS | nextGate=1; TIME ATTACK |
| Reset cancels run | PASS | position=(0.000, 0.100, -12.000); speed=0.000; mode=1 |
| Banked section contact | PASS | position=(101.743, 7.401, 339.243); speed=13.941; mode=2; modeMask=22; scripted placement at 72 degree bank |
| Free drive launch gate and assisted flight | PASS | position=(180.000, 17.532, 82.973); speed=50.755; mode=3; modeMask=26 |
| Discontinuity invalidates run | PASS | INVALID / discontinuity |
| Nozzle demand follows correction moment | PASS | torque=(-1.205, 0.000, 2.284); down jets=0.02219623,0.3418884,0,0.1731685,0,5.960465E-09,0 |
| Nozzle exhaust follows body orientation | PASS | world downward angle=36.52 |
| All seven exhaust directions oppose modeled force | PASS | minimum opposite-direction dot=1.000000 |
| Air attitude error converges | PASS | initial=41.458; after 0.9 s=6.481 |
| Angular velocity damping | PASS | initial=1.972; after .45 s=0.179 |
| Unassisted control drop contacts road | PASS | touchdown approach=15.859 m/s; rebound=2.150 |
| Precontact landing braking | PASS | highest braking clearance=7.000 m |
| Ground mist begins before contact | PASS | mist observed at clearance=1.659 m |
| Landing assistance reduces impact approach | PASS | baseline=15.859; assisted=1.862 m/s |
| Ground settling without hovering | PASS | height=-0.035; vertical=0.000 |
| Horizontal momentum retained | PASS | initial=12; after landing=11.144 m/s |
| Postcontact rebound bounded | PASS | peak upward velocity=0.141 m/s |
| Smoke persists after valve closes | PASS | at close=20; after .12 s=20 |
| Smoke expires within lifetime | PASS | after 3.12 s=0 |
| Assisted flight settles on temporary flat test pad | PASS | initial clearance=3 m; closing=2 m/s; forward=28 m/s; after 2.6 s: position=(180.000, 19.965, 101.586); speed=20.821; mode=1 |
| Cockpit moving parts wired across LODs | PASS | 3 yokes + 3 throttle pedals + 3 brake pedals |
| Movement exit disabled with explicit escape | PASS | X and EXIT use ExitStation; Jump is flight control |
| Occupied boarding feedback | PASS | simulated driverId=99; OCCUPIED / DRIVER 99 |
| Door control enters station | PASS | RoadsterAction Interact -> vehicle -> station |
| Move axis does not unseat driver | PASS | ClientSim station movement path, axis=1 |
| Input deadzone rejects drift | PASS | SDK InputMove callbacks at .08; deadzone=.12 |
| Live speed and road mode instruments | PASS | display=43 km/h; physics=48.54 |
| Yoke and throttle pedal follow input | PASS | yoke=21.83; pedal=18.00 |
| Moving reset requires confirmation | PASS | RESET? PRESS AGAIN WITHIN 2s |
| Confirmed reset clears inputs | PASS | second Interact restores start box and neutral controls |
| Time attack panel starts real run | PASS | LAP 1 / 1   2.07 s / CP 1 / 10    BEST -- |
| Steering profile changes full-scale response | PASS | COMFORT full axis -> 0.650 |
| Brake pedal follows negative axis | PASS | brake pedal=18.00 |
| Flight and thruster cockpit telemetry | PASS | AUTO / LEVEL ASSIST / LANDING  ARMED |
| Help control changes live panel | PASS | help hidden via physical panel Interact |
| Jump no longer exits the driver seat | PASS | Jump is now a pilot input |
| Explicit EXIT safely returns to paddock | PASS | player=(48.00, 0.00, 6.00); exit=(48.00, 1.00, 6.00) |
| Exit restores available boarding state | PASS | DRIVER ACCESS / AVAILABLE / SAFE EXIT / PADDOCK / INTERACT / REQUEST SEAT |
| Boarding defaults to Auto and landing assist | PASS | new driver defaults |
| Manual neutral preserves tilt with landing assist on | PASS | orientation delta=0 |
| Manual neutral damps angular velocity | PASS | initial=1.972; final=0.1917238 |
| Manual pitch isolated | PASS | SDK movement/Jump + synthetic Q/E/Shift adapter sample; P/Y/R=(0.100, 0.000, 0.000) |
| Manual yaw isolated | PASS | SDK movement/Jump + synthetic Q/E/Shift adapter sample; P/Y/R=(0.000, 0.100, 0.000) |
| Manual roll isolated | PASS | SDK movement/Jump + synthetic Q/E/Shift adapter sample; P/Y/R=(0.000, 0.000, -0.100) |
| Manual three axes simultaneous | PASS | SDK movement/Jump + synthetic Q/E/Shift adapter sample; P/Y/R=(-0.100, -0.100, 0.100) |
| Manual Space supplies forward thrust | PASS | Space adapter -> thrust=0.1 |
| Desktop Shift overrides held thrust | PASS | throttle=-0.1666667 |
| Mode change clears and blocks held controls | PASS | held inputs require neutral |
| VR input method selected by SDK hook | PASS | synthetic ClientSim VR identity; no HMD |
| Unheld yoke blocks right trigger and stick Y | PASS | no throttle from left stick Y or panel trigger |
| Left grip is neither yoke nor index brake | PASS | InputGrab LEFT is separate from InputUse LEFT |
| Yoke grip outside reach is rejected | PASS | synthetic hand 1 m from anchor |
| Right grip acquires nearby yoke | PASS | InputGrab RIGHT, separate from InputUse |
| Right grip does not become accelerator | PASS | grip acquires pose control only |
| Right index button ramps forward thrust | PASS | binary Use -> smoothed throttle=1 |
| Both index triggers prioritize brake immediately | PASS | right+left held; throttle=-0.1666667 |
| Left index button ramps braking | PASS | throttle=-1 |
| Right trigger release preserves left brake | PASS | independent right release with left held |
| Left trigger release preserves right request | PASS | independent left release; smooth output crosses neutral over time |
| Trigger release returns to zero | PASS | zero after smooth release |
| VR yoke pitch isolated | PASS | synthetic right-hand pitch 22 degrees |
| VR yoke roll isolated | PASS | synthetic right-hand roll 25 degrees |
| VR left stick yaw isolated | PASS | Look inputs unused; yaw from MoveHorizontal |
| VR left stick vertical is unused | PASS | MoveVertical=1 with no index trigger |
| VR three axes plus thrust simultaneous | PASS | P/Y/R=(0.38, 0.36, 0.55) |
| Panel Interact disarms held trigger | PASS | panel disarms before action |
| Releasing yoke cuts held acceleration | PASS | right Use remains held while grip releases |
| Held accelerator cannot reacquire yoke | PASS | release index trigger before regripping |
| SDK menu-style releases clear VR propulsion | PASS | synthetic release events; not real menu or focus actuation |
| SDK suspension clears held controls | PASS | ClientSim isSuspended=true, real Udon branch |
| Resume waits for neutral | PASS | held index/grip do not rearm |
| Long frame gap disarms controls | PASS | synthetic 0.7 s clock gap; no OS focus API |
| Ownership callback clears controls | PASS | single-client callback branch; not network transfer |
| Input family change clears VR state | PASS | VR fixture restored to ClientSim desktop |
| Landing switch independent of Manual | PASS | Manual stays selected; landing OFF |
| Auto Jump supplies permitted lift without exiting | PASS | height delta=-0.4366226; vy=2.024003 |
| Jump cannot bypass flight permission | PASS | permission removed; no applied lift or lift exhaust |
| Crossing flight envelope revokes permission | PASS | x=211 beyond permitted max x=210 |
| EXIT survives driver authority mismatch | PASS | simulated driverId disagreement; local ExitStation still available |
| Particle budget respected | PASS | whole-run peak=447; configured cap=1280 |
| Station exit releases driver | PASS | driver=-1 |
| Capture clientsim-free-drive.png | PASS | 223169 bytes |
| Capture clientsim-time-attack.png | PASS | 218523 bytes |
| Capture clientsim-bank-section.png | PASS | 322880 bytes |
| Capture clientsim-assisted-flight.png | PASS | 427466 bytes |
| Capture clientsim-thruster-attitude.png | PASS | 302546 bytes |
| Capture clientsim-thruster-approach.png | PASS | 266709 bytes |
| Capture clientsim-thruster-touchdown.png | PASS | 267502 bytes |
| Capture clientsim-thruster-residual.png | PASS | 264016 bytes |
| Capture clientsim-cockpit-driving.png | PASS | 518406 bytes |
| Capture clientsim-cockpit-flight.png | PASS | 629101 bytes |
| Capture clientsim-cockpit-controls.png | PASS | 488486 bytes |
| Capture clientsim-cockpit-boarding.png | PASS | 337527 bytes |
| Capture clientsim-manual-desktop.png | PASS | 641786 bytes |
| Capture clientsim-manual-vr-inputs.png | PASS | 610057 bytes |
| Capture clientsim-pilot-mode-panel.png | PASS | 489626 bytes |
| No runtime errors | PASS |  |

## 画像と再現

Game Viewキャプチャは15枚。全画像はPlay Mode中にScreenCaptureで保存し、検証用追従／設計目位置／パネル確認カメラを使用する。ローカル仮アバターは撮影カメラのみから除外。画像加工、Blenderレンダーの代用、HMD視点の偽装は行っていない。

- [clientsim-free-drive.png](../images/clientsim-free-drive.png)
- [clientsim-time-attack.png](../images/clientsim-time-attack.png)
- [clientsim-bank-section.png](../images/clientsim-bank-section.png)
- [clientsim-assisted-flight.png](../images/clientsim-assisted-flight.png)
- [clientsim-thruster-attitude.png](../images/clientsim-thruster-attitude.png)
- [clientsim-thruster-approach.png](../images/clientsim-thruster-approach.png)
- [clientsim-thruster-touchdown.png](../images/clientsim-thruster-touchdown.png)
- [clientsim-thruster-residual.png](../images/clientsim-thruster-residual.png)
- [clientsim-cockpit-driving.png](../images/clientsim-cockpit-driving.png)
- [clientsim-cockpit-flight.png](../images/clientsim-cockpit-flight.png)
- [clientsim-cockpit-controls.png](../images/clientsim-cockpit-controls.png)
- [clientsim-cockpit-boarding.png](../images/clientsim-cockpit-boarding.png)
- [clientsim-manual-desktop.png](../images/clientsim-manual-desktop.png)
- [clientsim-manual-vr-inputs.png](../images/clientsim-manual-vr-inputs.png)
- [clientsim-pilot-mode-panel.png](../images/clientsim-pilot-mode-panel.png)

`Drive Roadster → Run ClientSim smoke validation`または`-executeMethod RoadsterValidation.Run -roadsterExit`で再実行する。同名JSONと画像を上書きする。`RoadsterValidation.BuildScene`は生成結果をcanonicalの`DriveRoadster.unity`へ保存し直す検証用入口であり、手編集内容を先に保全すること。

SDK3.10.5の既知のSceneTemplate pipeline / ArgumentNullExceptionは新規シーン保存時に発生した。シーン保存・Udonコンパイル・Play Modeは成功しており、SDK本体は変更していない。Play Mode開始後のエラーは上表のNo runtime errorsで別に集計する。

未実施: VRChat SDK Validation / Build & Test、ワールドアップロード、HMD・実入力・パネルの狙いやすさ、複数クライアント・同時乗車・途中参加・切断、連続フルラップ、全着地条件、VR快適性、GPUフレーム時間・実帯域。詳細は[受入試験](../ACCEPTANCE_TESTS.ja.md)。生ログは個人環境情報を含むためignoredのartifacts/へ保存する。

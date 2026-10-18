# Meta Touch / OpenXR / Udon 入力対応と検証

調査日: 2026-10-17。Meta Horizon OSの公式入力APIとOpenXR仕様を照合した。Roadsterの実装はVRChatが公開するUdon入力イベントを使用する。Meta SDKやOpenXRプラグインをワールドへ追加して直接呼び出す構成ではない。

## 2026-10-18 継続上昇と操作ガイドの更新

- 右グリップでヨークを保持し、Jumpを長押しするとAuto/Manualとも上昇噴射を継続。右トリガーは前進、左トリガーは制動で、上昇とは独立しています。左制動・右グリップ解放・Jump解放で上昇入力を解除します。
- Touch系の標準Jumpは右手A。他機種やリマップ済みの場合は現在のJump割当を使います。[VRChat公式Touch操作](https://docs.vrchat.com/docs/touch)、[SteamVR入力設定](https://docs.vrchat.com/docs/steamvr-input-20)。
- Autoは空中でスラスターによるピッチ・ロール水平保持。Manualは握った右手の前後・左右傾きで姿勢操作し、左スティック左右でヨー。ヨークを離して車内のAUTO/LANDING/VIEW/EXIT等をInteractで操作します。
- 入口に日本語VR専用看板を追加。車内は実際の入力方式に応じてガイドを選び、PC / VRボタンで別方式の説明も読めます。これは説明の切替であり、実入力の方式を変更しません。
- 新しい検証結果は [airport-held-lift.json](validation/airport-held-lift.json)。VR識別・手位置はClientSim内の合成データです。以下の105項目は以前の実行履歴です。

## 物理入力とAPIの対応

| 物理入力 | Meta OVRInputの明示的な左右指定例 | OpenXR入力パス | Roadsterで受け取るもの／用途 |
|---|---|---|---|
| 右人差し指トリガー | `Get(Axis1D.PrimaryIndexTrigger, Controller.RTouch)` | `/user/hand/right/input/trigger/value` | `InputUse(bool, RIGHT)` → 前進要求 |
| 左人差し指トリガー | `Get(Axis1D.PrimaryIndexTrigger, Controller.LTouch)` | `/user/hand/left/input/trigger/value` | `InputUse(bool, LEFT)` → 制動要求 |
| 右グリップ／hand trigger | `Get(Axis1D.PrimaryHandTrigger, Controller.RTouch)` | `/user/hand/right/input/squeeze/value` | `InputGrab(bool, RIGHT)` → ヨーク取得／解放 |
| 左グリップ／hand trigger | `Get(Axis1D.PrimaryHandTrigger, Controller.LTouch)` | `/user/hand/left/input/squeeze/value` | `InputGrab(bool, LEFT)` → 操縦では未使用 |
| 左スティック | `Get(Axis2D.PrimaryThumbstick, Controller.LTouch)` | `/user/hand/left/input/thumbstick/x`, `/y` | `InputMoveHorizontal` → 操舵／yaw。縦は未使用 |
| 右手位置・回転 | `GetLocalControllerPosition/Rotation(Controller.RTouch)` | `/user/hand/right/input/grip/pose` | `GetTrackingData(RightHand)` → 車体ローカルの相対pitch／roll |

Metaの`Axis1D`は0～1の連続値で、`Button`は押下状態。`Touch`／`NearTouch`は接触／近接を表す。`Controller.Touch`の両手指定ではPrimary/Secondaryが左右に対応するが、`LTouch`／`RTouch`の片手指定ではPrimaryが指定した手に対応する。Primaryを常に左と解釈しない。[Meta Controller Input](https://developers.meta.com/vr/documentation/unity/unity-ovrinput/)、[Meta Cross-Platform Input](https://developers.meta.com/vr/documentation/unity/unity-cross-platform-dev/)

OpenXRの`trigger/value`と`squeeze/value`は別の0～1入力。`trigger/touch`は接触状態、`grip/pose`は姿勢であり、握り込みボタンではない。Unity OpenXR側もtrigger/gripのFloatと、それをBoolean化したpressedを区別する。[Khronos semantic paths](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/semantic_paths.adoc)、[Unity Oculus Touch profile](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.18/manual/features/oculustouchcontrollerprofile.html)

| Touch系統 | 公開プロファイル |
|---|---|
| Oculus Touch | `/interaction_profiles/oculus/touch_controller` |
| Touch Pro・拡張仕様 | `/interaction_profiles/facebook/touch_controller_pro` (`XR_FB_touch_controller_pro`) |
| Touch Plus・拡張仕様 | `/interaction_profiles/meta/touch_controller_plus` (`XR_META_touch_controller_plus`) |
| OpenXR 1.1 coreのPro／Plus | `/interaction_profiles/meta/touch_pro_controller`、`/interaction_profiles/meta/touch_plus_controller` |

いずれにも両手のtrigger/value・squeeze/valueが定義される。拡張名と1.1 coreのパス名を混同しない。ローカル機器がどのprofileを実際に選択したかは未取得。[Khronos Pro拡張](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/extensions/fb/fb_touch_controller_pro.adoc)、[Khronos Plus拡張](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/extensions/meta/meta_touch_controller_plus.adoc)、[coreプロファイル](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/semantic_paths.adoc)

## VRChatとの境界

VRChat公式ではUseは通常trigger、Grabは通常gripであり、どちらもboolと`handType`を渡す。ユーザーのリマップに従うため、上表は標準的な意味の対応であって実機の固定割当の保証ではない。ボタンイベントの`floatValue`は握り量ではない。Roadsterはboolから毎秒6（制動10）の速度で要求値を平滑化する。[VRChat Input Events](https://creators.vrchat.com/worlds/udon/input-events/)

このプロジェクトの旧InputManagerにはPrimary/SecondaryIndexTrigger（axis 8/9）とPrimary/SecondaryHandTrigger（axis 10/11）が存在する。ただし、名前の存在は実VRChatクライアントでのアナログ入力到達を証明しない。実装ではそれらの`Input.GetAxis`を使用しない。Meta側でアナログ取得可能でもUdon側で同じAPIが使えるとは判断しない。

## ローカル確認と試験方法

- Windows OpenXRの`ActiveRuntime`はSteamVRの`steamxr_win64.json`を指していた。
- 調査時に`vrserver`、`vrcompositor`、`OVRServer_x64`、`VirtualDesktop.Streamer`の実行プロセスは見つからなかった。
- 接続中PnPデバイス名のOculus／Meta Quest／HTC Vive／Valve Index／Mixed Reality／VR Headset検索は該当なし。別名や無線接続を網羅する検出ではないため、機器不存在の証明とはしない。
- ランタイム、バインド、認証、システム設定は変更していない。実HMD／コントローラーを使った検査は未実施。

ClientSimではEditor限定フィクスチャでVR識別と右手姿勢を合成し、`InputUse`／`InputGrab`の左右イベントを実Udon VMへ送った。フィクスチャの`SteamVR2`表示は合成したSDK入力方式であり、SteamVRやTouchの実入力を検出した結果ではない。テスト終了時にAPIフックを復元する。

## 左右・押下・解放の実行結果

Unity 2022.3.22f1 / SDK 3.10.5、UTC `2026-10-17T10:36:45.3369859Z`。全105項目PASSのうち関連19項目を抜粋。以下はすべてClientSim合成入力の結果。

| 試験 | 結果・観測 |
|---|---|
| Unheld yoke blocks right trigger and stick Y | PASS — no throttle from left stick Y or panel trigger |
| Left grip is neither yoke nor index brake | PASS — InputGrab LEFT is separate from InputUse LEFT |
| Yoke grip outside reach is rejected | PASS — synthetic hand 1 m from anchor |
| Right grip acquires nearby yoke | PASS — InputGrab RIGHT, separate from InputUse |
| Right grip does not become accelerator | PASS — grip acquires pose control only |
| Right index button ramps forward thrust | PASS — binary Use -> smoothed throttle=1 |
| Both index triggers prioritize brake immediately | PASS — right+left held; throttle=-0.1666667 |
| Left index button ramps braking | PASS — throttle=-1 |
| Right trigger release preserves left brake | PASS — independent right release with left held |
| Left trigger release preserves right request | PASS — independent left release; smooth output crosses neutral over time |
| Trigger release returns to zero | PASS — zero after smooth release |
| VR yoke pitch isolated | PASS — synthetic right-hand pitch 22 degrees |
| VR yoke roll isolated | PASS — synthetic right-hand roll 25 degrees |
| VR left stick yaw isolated | PASS — Look inputs unused; yaw from MoveHorizontal |
| VR left stick vertical is unused | PASS — MoveVertical=1 with no index trigger |
| VR three axes plus thrust simultaneous | PASS — P/Y/R=(0.38, 0.36, 0.55) |
| Panel Interact disarms held trigger | PASS — panel disarms before action |
| Releasing yoke cuts held acceleration | PASS — right Use remains held while grip releases |
| Held accelerator cannot reacquire yoke | PASS — release index trigger before regripping |

## 実機で残る確認

Quest Link/Air Link/Virtual Desktop等の実際の接続方式、VRChat内の入力方式・リマップ、両手の押下／解放、ヨーク到達位置、SteamVRダッシュボード／OSフォーカス切替、追跡喪失・復帰を実機で確認する必要がある。SDK3.10.5のUdonでは`Application.isFocused`が公開されず、SDK休止・入力解除・処理中断の分岐試験だけで実フォーカス喪失対応済みとはしない。

試験の全結果と再現手順: [Unity / ClientSim検証記録](validation/UNITY_VALIDATION.ja.md)。

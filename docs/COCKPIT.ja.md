# 車内・計器・操作系統

2026-10-17。対象は PC / PCVR、共有車両1台、運転席1席。最新の実測結果は [ClientSim 検証記録](validation/UNITY_VALIDATION.ja.md)、原データは [JSON](validation/clientsim-smoke.json)。

## 外観の根拠と設計範囲

既存の [公式映像／ページの観察記録](VISUAL_REFERENCES.ja.md) で確認した銅色／黒の閉じたキャノピー、灰白色の内装、角張ったヨークを引き継いだ。未確認の実車のUI、表示内容、機構、寸法、制御性能を再現したものではない。ダッシュボードの二画面、操作コンソール、ペダル形状、通風口、内張り、金属トリムは独自のゲーム設計。

`SourceAssets/Vehicle/DR_Asterion.blend` が編集可能な原本。`build_vehicle.py` は全3 LODのFBX／GLB、Collider、カーボンテクスチャと統計を出力する。`-- --export-only` でスタジオレンダーを省略できる。今回の生成／再インポートは Blender 5.1.0 で実施。以前の `DR_Asterion_Review.blend` と `renders/` は更新前の外観レビュー資料として残す。

各LODは38 Renderer、9材質。可動親 `SteeringWheel`、`Pedal_Throttle`、`Pedal_Brake` を独立させ、UnityビルダーがUnity軸の親ピボットを各LODへ追加する。ホイール12個と車内可動部9個は同じ入力から更新する。材質はStandard、シートは灰白色、ダッシュと内張りはカーボン系。シーンの環境光をFlatへ明示し、車内が未生成の空の環境照明に依存しないようにした。フレーム時間、Quest品質、LOD遷移の視覚品質は未測定。

## 動作する部分と装飾

| 場所 | 実装 |
|---|---|
| 運転席ドア DRIVER ACCESS | Interactで乗車要求。空席／占有／同期待ち／要求中／失敗理由を表示 |
| TIME ATTACK | `RoadsterRun.BeginRun()`。開始位置と速度の条件を検査 |
| RESET | 走行中は確認を挟み、開始位置へ復帰して計測と入力を解除 |
| FEEL | ローカルの感度をNORMAL→DIRECT→COMFORT→NORMALで切替 |
| CONTROLS | ローカルの操作説明を開閉 |
| EXIT TO PADDOCK | Stationを退出し、パドックへ移動 |
| ヨーク／ペダル | ヨークはVR右グリップで取得する仮想操縦桿。ペダルは入力表示で足トラッキングなし |
| AUTO / MANUAL | 空中の水平復帰／3軸角速度制御を切替 |
| LANDING ON / OFF | モードと独立した着陸補助 |
| ヨーク上の小キー、ベント、カップホルダー、ねじ、シートベルト部品 | 装飾。入力機能なし |

7個の車内ボタンには物理BoxColliderと `RoadsterAction` がある。押下時はキャップとラベルが2.72 mm沈み、0.16秒後に戻る。Unity UIのクリックイベントには依存せず、VRChat Interactで処理する。乗車パネルには別の乗車用Interactを設ける。操作パネルは運転席方向を向き、物理的な台座の上へ配置する。

## 入力・モード・安全な退出

操作表は[README](../README.md#操作)。Autoを標準とし、Manualと着陸補助を別々に切り替える。乗車時はAuto・着陸補助ON。Fがモード、Lが着陸、Backspaceが復帰、Xが降車。Rは標準Action Menuと競合させない。

Desktopは移動軸のW/S・A/Dを使用する。キーを離すとスロットルを即0にして惰性走行し、実キー解放で残ったSDK入力も解除する。平地ではSpaceの押下でジャンプ（両モード共通、再発動は離して再押下）。Manualの空中ではW/Sを機首下げ／上げ、A/Dをyaw、Q/Eを左／右roll、Spaceを前進推力、Shiftを速度方向の制動へ切替。離陸・着地で中立復帰を要求し、キーを約0.15秒離してから操作する。地上で保持していたアクセルがpitch、空中で保持していたpitchがアクセルへ突然切り替わらないようにする。

VRは`InputUse(bool, UdonInputEventArgs)`の`handType`で右／左を分ける。右＝アクセル／前進推力、左＝制動。連続アナログ圧力ではない。左スティックは横だけを操舵／yawに使用し、縦は未使用。Look系イベントを取得しないので、右スティック旋回をpitch/rollへ転用しない。実リマップ・全機種対応は未検証。

右グリップ（`InputGrab`）をヨーク中心から0.32 m以内で押すと取得する。取得時は両トリガーを解放している必要がある。取得時の車体ローカル右手回転を記憶し、`GetTrackingData(RightHand)`の車体ローカル相対回転からpitch／rollを取り出す。35度でフル入力、yaw成分は使用しない。手のworld回転をそのまま使わないため、車体の回転を手の操作と取り違えない。右グリップ解放、Drop、中心から0.55 m超、パネル操作でヨークを解除する。

2026-10-22更新：VRの加速・操舵・制動はヨーク保持不要。右Useで前進、左Useで制動、左スティック横で操舵／yaw。グリップ保持は通常走行の中立待ちを妨げない。Jump上昇とManualの手首pitch／rollにはヨーク保持が必要。取得済みヨークを解放／距離超過すると前進・上昇を止め、右Useを解放して押し直すまで前進を再開しない。全パネルInteractは自身の処理より先に操縦を解除し、左右Use・Jump・左スティック横の中立待ちに戻す。ヨーク再取得には両トリガーの解放が必要。[回帰試験](validation/VR_TRIGGER_FIX.ja.md)。

デッドゾーンd=.12で`sign(x)*clamp01((abs(x)-d)/(1-d))`。yaw／操舵はカーブ指数1.4、感度はCOMFORT=.65、NORMAL=.85、DIRECT=1。pitch／rollも同じ感度係数を使用。操舵・姿勢入力と推力立上りは毎秒6、制動側は毎秒10。スロットルと上昇要求の解除は即0。制動開始時には正の推力を即ゼロにし、同時トリガーは必ず制動優先。安全境界での解除も平滑化を待たずゼロにする。

Autoは姿勢誤差による水平復帰と角速度減衰。Manualは`(commandedLocalRate - actualLocalAngularVelocity)*4.5`を用い、最大角速度要求1.5 rad/s、角加速度上限10 rad/s²。無入力では回転を止めるが、その傾きを水平には戻さない。着陸補助は独立した力であり、Manualへ水平復帰トルクを追加しない。

乗車・リセット・パネル・モード切替・Manual地空切替・所有権通知・入力方式系統変更・休止・0.5秒を超える処理中断で出力をゼロにし、中立を0.15秒観測して再開する。押下状態は境界をまたいで覚え、押しっぱなしを中立と誤認しない。KeyboardとMouse間の通常切替だけでは解除しない。SDKメニューのfalse／zero入力も受け取る。

`Application.isFocused`はSDK3.10.5のUdon非公開APIであり採用していない。実OSフォーカス喪失、SteamVRダッシュボード、トラッキング喪失による解除通知は未検証。ClientSim試験ではSDK休止、解除イベント列、処理中断判定の分岐を区別して扱う。

Stationは`ImmobilizeForVehicle`と`disableStationExit=true`。Jumpは操縦専用で、長押しによる退出も実装しない。X／EXIT→`ExitStation`→パドックへ移動し、入力とrunを解除する。走行中のRESETのみ2秒以内の二度押しを要求する。

公式Meta/OpenXRとUdonの割当境界、ローカルランタイム確認、左右ボタンの追加試験は[Meta入力バインド資料](INPUT_BINDINGS_META.ja.md)を参照。

## 生計器

`RoadsterRun` が約10 Hzでテキストを更新する。速度や計測状態を焼き込んだメッシュ文字は削除した。

| 表示 | データ元 |
|---|---|
| km/h | ownerのRigidbody速度、非ownerは受信netVelocity。大きさ×3.6 |
| PARKED / ROAD / WALL・BANK / FLIGHT / AIRBORNE | 実際のmotionMode 0–4 |
| THRUST / BRAKE、百分率 | 整形済みペダル入力。Desktop地上のSは停止後に後退し、VR制動は後退しない |
| LAP 1/1、秒、CP、BEST | 実行中run、サーバー時刻、順序ゲート、ローカルPB |
| PITCH / ROLL、水平線 | 車体Transformの姿勢。HMD姿勢ではない |
| H、DOWN、LANDING | Raycastの路面距離、接近速度、着陸補助加速度。未検出の高さは -- |
| FL/FR/RL/RR/L/R/PUSH | 四隅の下向き、左右、後方推進の7噴射要求。燃料量／推力測定ではない |
| AUTO / MANUAL、LANDING ON / OFF | 独立した操縦・着陸補助の同期状態 |
| DESKTOP / KEYS、VR / TRIGGER BUTTONS + YOKE | 実際に選択した入力分岐。SDK入力方式も併記 |
| 状態メッセージ | 所有権要求、占有、操作結果、TA失格理由 |

表示用の操舵とペダルを各signed 8 bit相当へ量子化して1 intへ詰め、路面距離／接近速度／補助加速度をVector3にして既存Manualスナップショットに追加。これらに加えて操縦モード・着陸補助の2 bool、操縦3軸のVector3、入力状態のintを同期する。実通信量は未測定で、Udonの実送信bytesや帯域を実測した値ではない。遠隔側は受信値から可動部と計器を描画し、物理力を加えない。初回受信、途中参加、所有権変更、遅延下の見え方は複数クライアントで要検証。

## 座席と画像の扱い

実Station座標は `(-.4,.53,-.12)` m、設計用の目位置は `(-.395,1.08,-.08)` m。後者は撮影カメラの参照で、ユーザーの頭をその位置へ固定しない。現ClientSimの仮アバターは実際の着座IKを再現しないため、車内の撮影時だけアバターRendererを検証カメラから除外する。保存シーンや実プレイヤーの座席は変更しない。

- [走行中の設計視点](images/clientsim-cockpit-driving.png)
- [飛行中の設計視点](images/clientsim-cockpit-flight.png)
- [操作パネルの確認視点](images/clientsim-cockpit-controls.png)
- [降車後の乗車パネル](images/clientsim-cockpit-boarding.png)

いずれも実Unity Editor Play Mode / ClientSimのGame View。スクリプト入力・区間配置・検証用カメラを使用した。実VRChatクライアント、HMD、自然な手動走行の記録ではない。画面外の幾何や照明を画像加工で補完していない。

残る受入試験は、各アバター／HMDの頭位置とヨークの干渉、パネルへの実Interact到達性、物理キーとコントローラーのリマップ、VR酔い、複数クライアントでの状態表示と同時乗車。ClientSimの占有拒否テストはdriverIdへ別IDを設定した単一クライアントの分岐確認であり、実際の競合試験ではない。

## 追加の実行画像

[Manual Desktop](images/clientsim-manual-desktop.png) / [合成VR入力](images/clientsim-manual-vr-inputs.png) / [モード・着陸パネル](images/clientsim-pilot-mode-panel.png)。すべてUnity Editor Play Mode / ClientSimのGame View。VR識別・右手姿勢はEditor限定フィクスチャで、実HMD操作の証拠ではない。

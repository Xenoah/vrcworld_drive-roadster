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
| STEER | ローカルの感度をNORMAL→DIRECT→COMFORT→NORMALで切替 |
| CONTROLS | ローカルの操作説明を開閉 |
| EXIT TO PADDOCK | Stationを退出し、パドックへ移動 |
| ヨーク／ペダル | 操舵／加速／制動入力に追従する見た目。掴む操作・手足トラッキングなし |
| ヨーク上の小キー、ベント、カップホルダー、ねじ、シートベルト部品 | 装飾。入力機能なし |

5個の車内ボタンには物理BoxColliderと `RoadsterAction` がある。押下時はキャップとラベルが2.72 mm沈み、0.16秒後に戻る。Unity UIのクリックイベントには依存せず、VRChat Interactで処理する。乗車パネルには別の乗車用Interactを設ける。操作パネルは運転席方向を向き、物理的な台座の上へ配置する。

## 入力と安全な退出

`InputMoveVertical`／`InputMoveHorizontal` はローカル運転者だけが受け取る。デッドゾーンd=.12で `sign(x) × clamp01((abs(x)-d)/(1-d))` へ変換。操舵はさらに絶対値の1.4乗と感度係数を掛ける。係数は COMFORT=.65、NORMAL=.85、DIRECT=1。操舵と正スロットルは毎秒6、負方向は毎秒10で目標値へ追従する。感度は入力だけを変え、姿勢補助／着陸の安全値は変更しない。

標準Desktopキーは W/S・A/D。Rはリセット、TはTA、Cは操舵感度、Hはヘルプ、Xは退出。VRは移動スティックとInteractを使用する。入力イベントはVRChatのリマップに従うため、[SDK Input Events](https://creators.vrchat.com/worlds/udon/input-events/) を参照。

`VRCStation.disableStationExit=true` とし、前後左右の入力による自動退出を防ぐ。`InputJump(true)` は `ExitVehicle()` → `ExitStation()` を呼ぶ。EXITボタンとDesktop Xも同じ経路。これは [VRCStationの仕様](https://creators.vrchat.com/worlds/components/vrc_station/) に基づく。通常移動による退出を無効にしたまま、明示的な退出経路を必ず残す。

速度2 m/s超では RESET 初回で「2秒以内にもう一度」を表示。再操作で復帰し、停止中は即時復帰。乗車／降車／復帰で生入力と平滑化後の入力をゼロにする。ボタンの運転者ガードと既存の owner ガードを維持する。Jump／EXIT はリセット確認を要求しない。

## 生計器

`RoadsterRun` が約10 Hzでテキストを更新する。速度や計測状態を焼き込んだメッシュ文字は削除した。

| 表示 | データ元 |
|---|---|
| km/h | ownerのRigidbody速度、非ownerは受信netVelocity。大きさ×3.6 |
| PARKED / ROAD / WALL・BANK / FLIGHT / AIRBORNE | 実際のmotionMode 0–4 |
| DRIVE / BRAKE / REVERSE、百分率 | 整形済みペダル入力。負方向は BRAKE / REV と併記 |
| LAP 1/1、秒、CP、BEST | 実行中run、サーバー時刻、順序ゲート、ローカルPB |
| PITCH / ROLL、水平線 | 車体Transformの姿勢。HMD姿勢ではない |
| H、DOWN、LANDING | Raycastの路面距離、接近速度、着陸補助加速度。未検出の高さは -- |
| FL/FR/RL/RR/L/R/PUSH | 四隅の下向き、左右、後方推進の7噴射要求。燃料量／推力測定ではない |
| 状態メッセージ | 所有権要求、占有、操作結果、TA失格理由 |

表示用の操舵とペダルを各signed 8 bit相当へ量子化して1 intへ詰め、路面距離／接近速度／補助加速度をVector3にして既存Manualスナップショットに追加。生フィールド容量は計16 byte増加するが、Udonの実送信bytesや帯域を実測した値ではない。遠隔側は受信値から可動部と計器を描画し、物理力を加えない。初回受信、途中参加、所有権変更、遅延下の見え方は複数クライアントで要検証。

## 座席と画像の扱い

実Station座標は `(-.4,.53,-.12)` m、設計用の目位置は `(-.395,1.08,-.08)` m。後者は撮影カメラの参照で、ユーザーの頭をその位置へ固定しない。現ClientSimの仮アバターは実際の着座IKを再現しないため、車内の撮影時だけアバターRendererを検証カメラから除外する。保存シーンや実プレイヤーの座席は変更しない。

- [走行中の設計視点](images/clientsim-cockpit-driving.png)
- [飛行中の設計視点](images/clientsim-cockpit-flight.png)
- [操作パネルの確認視点](images/clientsim-cockpit-controls.png)
- [降車後の乗車パネル](images/clientsim-cockpit-boarding.png)

いずれも実Unity Editor Play Mode / ClientSimのGame View。スクリプト入力・区間配置・検証用カメラを使用した。実VRChatクライアント、HMD、自然な手動走行の記録ではない。画面外の幾何や照明を画像加工で補完していない。

残る受入試験は、各アバター／HMDの頭位置とヨークの干渉、パネルへの実Interact到達性、物理キーとコントローラーのリマップ、VR酔い、複数クライアントでの状態表示と同時乗車。ClientSimの占有拒否テストはdriverIdへ別IDを設定した単一クライアントの分岐確認であり、実際の競合試験ではない。

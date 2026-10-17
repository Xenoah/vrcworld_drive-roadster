# Drive Roadster — VRChat world

オリジナルの電動スポーツカー **DR Asterion** で、海上コースの地上・バンク・飛行区間を走る PC / PCVR 向け試作ワールドです。共有車両は **1 台・運転席 1 席**。フリー走行と個人タイムアタックを実装しています。

## 検証状態

2026-10-17、Windows 上の **Unity 2022.3.22f1 / VRChat SDK 3.10.5** で C#・UdonSharp コンパイルとシーン構築を実施しました。**ClientSim の区間スモーク試験 60/60 項目成功**。乗降、直線加速、タイムアタックの START 通過、リセット、バンク接地、LAUNCH 通過からのアシスト飛行、不連続移動による失格、姿勢補助・3層白煙・着陸補助を確認しています。車内計器、ヨーク／ペダルの入力連動、操作パネル、感度切替、明示的な降車も SDK 入力イベントで検証しました。

Python 静的・数理検査 33 件と C# 構文解析も成功。実行条件と生の検査値は [Unity 検証記録](docs/validation/UNITY_VALIDATION.ja.md) / [JSON](docs/validation/clientsim-smoke.json) を参照してください。

**VRChat ワールドへのアップロード、VRChat Build & Test、HMD、複数クライアント、完全な 1 周の受入試験は未検証です。** ClientSim は Unity Editor 内のシミュレーターです。ネットワーク同期、VR の視認性・酔い・操作性、公開ワールドの性能を保証する結果ではありません。

## Unity Play Mode の画像

以下は **Unity Editor Play Mode / ClientSim、ローカル simulated player 1 人** で撮影した Game View です。自動入力と検証用追従カメラを使用しています。左上は検証用 HUD。VRChat 実クライアントの画像ではありません。

![ClientSim フリー走行](docs/images/clientsim-free-drive.png)

直線で静止から加速したフリー走行。mode 1。カメラと HUD は検証時だけ追加しています。

![ClientSim タイムアタック開始](docs/images/clientsim-time-attack.png)

タイムアタックを開始して START を通過した状態（CP 1/10）。一周完走は未検証です。

![ClientSim バンク区間の接地](docs/images/clientsim-bank-section.png)

72 度バンクの途中へ位置・姿勢・初速を設定した区間試験。mode 2 の接地を確認しました。直線からの連続進入試験ではありません。

![ClientSim LAUNCH 通過後のアシスト飛行](docs/images/clientsim-assisted-flight.png)

LAUNCH 手前へ配置してからゲート面を横切り、mode 3 に移行した飛行区間。着地・完走は未検証です。バンクと飛行の INVALID 表示は、先行する計測をリセットして自由走行の試験へ移った結果です。

## スラスター姿勢制御・白煙・着地

公式 [Kickflip](https://x.com/Tesla/status/2110926164306931860) / [Ramp](https://x.com/Tesla/status/2110913438293647789) の回転中の噴流、接地前から広がる雲、停止後の残留霧を再観察して追加しました。映像の見える振る舞いを参考にしたゲーム補助であり、実車の内部機構や性能を断定する実装ではありません。時刻付き観察、式、全パラメーター、ノズル対応は [詳細技術資料](docs/THRUSTERS.ja.md) を参照してください。

- **姿勢**: 空中の Quaternion 姿勢誤差と実角速度から PD トルクを算出。四隅の下向きノズルと左右ノズルの強弱を、その補正の方向に対応させます。ジェット方向は車体の回転に追従します。
- **着陸**: 路面までの高さと接近速度から接地前の制動量を計算。最大42 m/s²の車体上向き補助で降下を緩和し、接地後0.35秒の過大反発を抑えます。地上では着陸噴射を止めます。低高度で降下中は既存の滑空揚力を解除し、着陸制動との重複を防ぎます。
- **白煙**: 細い Local ジェット、World の残留煙、排気方向の Raycast が路面へ当たった地点の拡散霧の3層。接地前から霧を生成し、噴射停止後も寿命まで残ります。
- **負荷と同期**: 18個の ParticleSystem を再利用、最大1,280粒子。7噴射強度を2個の同期 int へ量子化し、既存スナップショットで送ります。物理は owner のみ。実ネットワークと GPU 負荷は未測定です。

![姿勢補正に応じた噴流](docs/images/clientsim-thruster-attitude.png)

Unity Editor Play Mode / ClientSim。傾けて配置した車体の姿勢復元中。スクリプトによる区間検証で、公式映像ではありません。

![接地前のスラスター噴射](docs/images/clientsim-thruster-approach.png)

同環境。高さ7 m・初期降下速度11 m/sの自動垂直降下で、接地前に制動と白煙が発生。

![接地付近の拡散霧](docs/images/clientsim-thruster-touchdown.png)

同環境。接地付近の路面上の霧。[噴射停止後の残留画像](docs/images/clientsim-thruster-residual.png)も掲載しています。

補助オン／オフ比較は別の共通条件（高さ7 m、降下11 m/s、前進12 m/s）で行いました。実測値は [検証報告](docs/validation/UNITY_VALIDATION.ja.md) と [JSON](docs/validation/clientsim-smoke.json) に記載しています。地上での静止、横速度の維持、煙の寿命も確認しました。連続した全周走行・斜面を含む全着地条件・HMD・複数クライアントは未検証です。

## 車内と操作系統

灰白色シート、黒いダッシュボード、角丸長方形ヨーク、独立したアクセル／ブレーキペダル、計器の筐体とセンターコンソールを更新しました。銅色の閉じたキャノピーなど既存の外観を引き継ぎ、詳細な内装・計器配置はゲーム独自の設計です。編集可能な `.blend` と全3段階の FBX / GLB を同梱しています。

![走行中の運転席計器](docs/images/clientsim-cockpit-driving.png)

Unity Editor Play Mode / ClientSim。設計上の `CockpitEye_AuthoredReference` から撮影。実速度、ROAD モード、操作入力、CP／ラップ表示を描画しています。撮影用カメラでは ClientSim の仮アバターを非表示にしています。実アバターの着座視点や HMD トラッキングの検証画像ではありません。

![飛行中の車内表示](docs/images/clientsim-cockpit-flight.png)

同じ設計視点。空中に姿勢・初速を設定して飛行補助を有効にした区間試験。ピッチ／ロール、路面までの距離、接近速度、7系統の噴射要求を実状態から表示します。`INVALID` は直前の計測解除によるもので、完走画像ではありません。

![車内操作パネル](docs/images/clientsim-cockpit-controls.png)

同環境のパネル確認用カメラ。5個の Interact ボタンで TA 開始、リセット、操舵感度、ヘルプ、安全な退出を操作します。映像用の飾りスイッチと動作するボタンは [車内技術資料](docs/COCKPIT.ja.md) で区別しています。

![運転席の乗車状態表示](docs/images/clientsim-cockpit-boarding.png)

同環境。降車後の AVAILABLE 表示。ドア側の乗車操作から所有権要求・Station 着座へ進みます。

## 操作

| 操作 | Desktop | VR |
|---|---|---|
| 乗車 | 運転席ドアの DRIVER ACCESS または車体を Interact | 同じ場所を Interact |
| 加速 | W / 移動入力の前 | 移動スティック縦の正方向 |
| 制動・後退 | S / 移動入力の後 | 移動スティック縦の負方向 |
| 操舵 | A / D | 移動スティック横 |
| 飛行中の下降補助 | S | 移動スティック縦の負方向 |
| スタートへ復帰 | R または RESET | 車内 RESET を Interact |
| タイムアタック開始 | T または TIME ATTACK | 車内 TIME ATTACK を Interact |
| 操舵感度 | C または STEER | 車内 STEER を Interact |
| 操作説明の表示 | H または CONTROLS | 車内 CONTROLS を Interact |
| 降車 | Jump（標準 Space）/ X / EXIT | 割り当て済み Jump / 車内 EXIT |

乗車は所有権取得後に確定し、占有中は理由を表示します。移動軸による Station 自動退出を無効にし、Jump／EXIT から明示的に降車します。降車先は常にパドックです。速度2 m/s超のリセットは2秒以内の二度押しが必要です。降車とリセットで入力をゼロにします。

入力の遊びは .12。操舵はカーブ指数1.4、感度は COMFORT .65／NORMAL .85／DIRECT 1.0です。ヨークとペダルはこの入力に追従するアニメーションで、手でつかむ操作や足のトラッキングは実装していません。車内ボタンの実際の狙いやすさ、物理キー、VRコントローラー、各アバターの着座は受入試験が必要です。自動検証は SDK 入力イベントと Udon Interact を実行します。

フリー走行は着座してそのまま加速します。飛行は **LAUNCH ゲートを正方向に通過したときだけ** 許可され、LANDING ゲートで解除されます。空中の任意位置から飛び始める機能ではありません。

タイムアタックはスタート位置から **8 m 以内・速度 1 m/s 以下** で開始します。開始時に車両をスタートへ戻し、START → ACCEL → WALL_APEX → LAUNCH → AIR_01 → AIR_02 → AIR_03 → LANDING → RETURN → FINISH の順で通過します。リセット・運転者変更・不連続な位置移動・不正なゲート通過で失格になります。

## 導入と再現

検証に使用した Unity は **2022.3.22f1 (887be4894c44)**、VRChat Base / Worlds SDK は **3.10.5** です。[VRChat が指定する Unity](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)を導入時にも確認してください。

1. このリポジトリを clone します。`Assets`、`Packages`、`ProjectSettings` を含む Unity プロジェクトです。
2. [VRChat Creator Companion](https://vcc.docs.vrchat.com/) に既存プロジェクトとして追加し、`Packages/vpm-manifest.json` の Base / Worlds / VPM Resolver を復元します。SDK のソースとバイナリはリポジトリへ再配布していません。新規 Worlds プロジェクトへ `Assets/DriveRoadster` をコピーする方法も利用できます。
3. Unity 2022.3.22f1 で開き、Package Manager の復元とスクリプトコンパイル完了を待ちます。
4. 保存済みの `Assets/DriveRoadster/Scenes/DriveRoadster.unity` を開きます。再生成する場合は **Drive Roadster → Build vertical slice scene** を実行します。ビルダーは既存シーンの保存を確認し、生成先を連番化します。
5. UdonSharp をコンパイルします。Play Mode で ClientSim を有効にして乗降・走行を試します。
6. 自動スモーク試験は **Drive Roadster → Run ClientSim smoke validation**。結果を `docs/validation/clientsim-smoke.json`、画像を `docs/images/` へ保存します。同名の検証成果物は更新されます。実行中のプロジェクトを保存してから利用してください。
7. 公開前に SDK Validation と VRChat Build & Test、および [実機受入試験](docs/ACCEPTANCE_TESTS.ja.md) を実施してください。今回の作業ではワールドをアップロードしていません。

バッチでのシーン再生成は Unity の `-batchmode -projectPath <project> -executeMethod RoadsterValidation.BuildScene -quit -logFile <log>` で実行できます。画像を伴う検証は描画可能な Editor から `-executeMethod RoadsterValidation.Run -roadsterExit` を指定します。`-quit` を併用しないでください。

## ファイル構成

| 場所 | 用途 |
|---|---|
| `Assets/DriveRoadster/Scripts/RoadsterVehicle.cs` | 乗降、所有権、車両物理、入力、スナップショット同期 |
| `Assets/DriveRoadster/Scripts/RoadsterRun.cs` | ゲート判定、タイマー、失格、参考記録、HUD |
| `Assets/DriveRoadster/Scripts/RoadsterAction.cs` | 乗車と5種の車内 Interact、押下演出 |
| `Assets/DriveRoadster/Editor/BuildRoadsterWorld.cs` | FBX と manifest からシーン・座席・HUD・LOD を生成 |
| `Assets/DriveRoadster/Editor/RoadsterCockpitSetup.cs` | 生計器、ボタン、乗車表示、可動部ピボットを生成 |
| `Assets/DriveRoadster/Editor/RoadsterThrusterSetup.cs` | ノズル参照と3層のParticleSystem・白煙材質を生成 |
| `Assets/DriveRoadster/Editor/RoadsterValidation.cs` | Editor 限定 ClientSim 検証。ワールド実行コードには含まれない |
| `Assets/DriveRoadster/Art/Vehicle/` | 車両 LOD0–2 の FBX / GLB、材質テクスチャ、幾何統計 |
| `Assets/DriveRoadster/Art/Course/` | コース FBX / GLB、`course_manifest.json` |
| `Assets/DriveRoadster/Scenes/` | Unity で生成したシーン |
| `SourceAssets/Vehicle/`, `SourceAssets/Course/` | 編集可能な Blender 原本、生成・検証スクリプト、Blender レンダー |
| `Tests/`, `Tests/Review/`, `tools/` | Python 静的・数理検査、C# 構文解析 |
| `docs/validation/`, `docs/images/` | 実行結果と Play Mode キャプチャ |

`Library` / `Temp` / ローカル実行ログ / SDK パッケージ本体は追跡しません。`.meta` とシーンは GUID の参照を保つため一緒に扱ってください。

## シーン構成と物理

`DriveRoadsterWorld` がコースと `VRCSceneDescriptor` を持ち、`Roadster_Authority` が Rigidbody、BoxCollider、VRCStation、Vehicle / Run の UdonBehaviour を持ちます。車体は layer 17 (Walkthrough)、路面は Default、サスペンションの Raycast は Default のみを見るため自分の車体へ接触しません。`COL_` 名のメッシュだけに MeshCollider を付け、衝突用 Renderer は非表示にします。

| パラメーター | 初期値・意味 |
|---|---|
| 車重 / 重心 | 1,200 kg / 車体ローカル `(0, 0.30, 0)` |
| 車体衝突形状 | 中心 `(0, .62, 0)`、寸法 `(1.8, .65, 4.3)` m の BoxCollider |
| 最高速度 | 65 m/s ≈ 234 km/h。速度ベクトル全体を制限 |
| 加速 / 制動 | 18 / 32 m/s² 相当の `ForceMode.Acceleration` |
| サスペンション | 4 本の下向き Raycast、長さ .60 m、spring 42、damping 7 |
| 接地判定 | 2 本以上のヒット。法線を平滑化して車体姿勢を追従 |
| 地上 / 壁 | 上向き法線と世界上方向の内積が .65 未満なら壁モード |
| 押し付け | 地上 9.81、壁 23 m/s²。横滑り速度へ減衰係数 6 |
| 飛行 | 許可区間で非接地が .12 秒超。前進加速 8～18 m/s²、速度 28 m/s まで揚力増加 |
| 飛行上限 | 世界 Y=100 m 超で揚力 0。自動重力は無効、各モードで重力相当を明示適用 |
| 復帰条件 | Y < −20 m または原点から 1,800 m 超。R / RESET でも復帰 |

飛行許可範囲は中心 `(180,20,78)`、サイズ `(60,80,180)` m の軸平行ボックスです。範囲外へ出ると飛行許可を解除し、計測中なら失格になります。飛行の上下入力は下降補助のみで、自由な 6 軸飛行ではありません。

モード値は `0` 待機、`1` 地上、`2` バンク/壁、`3` アシスト飛行、`4` 許可なしの空中です。地上の法線追従と飛行の水平復元は簡易アーケード物理であり、実車の性能モデルではありません。

## 同期と所有権

- **owner だけが物理を計算**。非 owner の Rigidbody は kinematic で、受信位置・姿勢を補間します。owner の CCD は ContinuousDynamic、非 owner は ContinuousSpeculative です。
- Vehicle と Run は同一 GameObject 上の Manual 同期です。`VRCObjectSync` は併用しません。運転中は .12 秒、無人時は 1 秒ごとに送信を要求します。これは要求周期であり、実送信間隔や帯域の保証ではありません。
- 車両は driver ID、世代、モード、位置・回転・速度、reset sequence、7ノズルの噴射強度（2 int）、操舵／ペダル（1 int）、着陸テレメトリー（Vector3）を同期。受信側は速度から最大 .18 秒先まで外挿し、指数補間を適用します。最初の受信と reset sequence 変更時は即座に位置を合わせます。
- 乗車前に `IsNetworkSettled` を確認し、非 owner は初回スナップショットも待ちます。所有権要求後 .4 秒待ち、自分が owner かつ空席なら Station へ入ります。2 秒後も着座していなければ解放します。
- owner を失った運転者は退出します。通常降車・driver の切断・無人時の自動所有権移譲では旧 run を取り消し、スタートへ復帰します。安全な退出先はパドック側の player spawn です。
- .4 秒の待機は合意プロトコルではありません。同時乗車、遅延、途中参加、切断、所有権移譲のイベント順は **複数クライアント未検証** です。

車輪の回転は速度に基づくローカル演出です。操舵／ペダル入力を量子化して送り、遠隔側の前輪、ヨーク、ペダルを同じ値で動かします。各 Transform の個別同期は行いません。この遠隔表示は複数クライアント未検証です。遠隔 HUD の速度は受信 `netVelocity` を使用します。`showDiagnostics` で送信 byte 数・成功/失敗数・世代を表示できますが、ClientSim の結果を実ネットワーク帯域として扱わないでください。

## チェックポイントと記録

`course_manifest.json` はメートル単位・Unity の X 右 / Y 上 / Z 前です。各ゲートは位置、forward / up、開口幅・高さ、必要走行モードを定義します。

| index | 名前 | 必要モード | 特別な処理 |
|---|---|---|---|
| 0 | START | Ground | 計測開始位置の先に配置 |
| 1 | ACCEL | Ground | 加速直線 |
| 2 | WALL_APEX | WallRide | バンク頂点を通過 |
| 3 | LAUNCH | Ground | 飛行許可を有効化 |
| 4–6 | AIR_01 / AIR_02 / AIR_03 | AssistedFlight | 空中リングの開口を通過 |
| 7 | LANDING | Ground | 飛行許可を解除 |
| 8 | RETURN | Ground | 戻りカーブ |
| 9 | FINISH | Ground | 完走して記録へ挿入 |

車両位置の前フレームから現フレームまでの線分をゲート座標系へ変換します。`a.z < 0 && b.z >= 0` の正方向交差だけを受け付け、交点が幅・高さの半分以内か確認します。薄い Trigger を高速で抜ける問題を避けるため、Rigidbody.position の swept plane 判定を使用します。逆走・横抜け・同一平面での二重接触は受け付けません。

次のゲート以外は計測対象になりません。通過間隔は「前ゲートからの直線距離 ÷ 65 m/s × .75」以上、START は .10 秒以上を要求します。運転者世代の不一致または 1 サンプルで 15 m 超の位置移動を検知すると失格です。これはカジュアルな検証であり、改造クライアントに対する認証や完全な不正対策ではありません。

記録は当該車両が保持する **インスタンス内 5 件** のタイム順配列です。同一プレイヤーの重複記録は残ります。表示名は 24 文字で切ります。PB はそのクライアントのセッション内のローカル値で、再参加をまたぐ保存はありません。世界ランキング、サーバー検証、永続保存は未実装です。

## LOD と描画

| 車両 | 三角形数（Blender GLB再インポート） | LODGroup の画面占有率しきい値 |
|---|---:|---:|
| LOD0 | 73,096 | .30 |
| LOD1 | 27,738 | .10 |
| LOD2 | 6,957 | .015（それ未満はカリング） |

各段 38 Renderer・9 材質です。車輪参照は 4 輪 × 3 段を接続します。しきい値は距離そのものではなくスクリーン上の相対サイズです。材質は Unity Standard へ割り当て、金属・カーボン・透明ガラス・発光部をビルダーで設定します。

コースの manifest 統計は描画 38,144 三角形、衝突 1,826 三角形です。コースには見た目用 LOD 資産もありますが、現ビルダーは 1 つのコース FBX を採用し、コース用 LODGroup は組みません。FBX の X 軸反転はビルダーで補正し、車輪は Unity 軸の親ピボットで回します。描画負荷の計測と車両 LOD の遷移品質は別途確認が必要です。

## 既知の制約

- フルラップ、垂直カール/天井の練習区間、斜面・高速・強い傾きを含む着地の安定性、逆走からの復帰は継続調整が必要です。
- 車体 FBX の軸・車輪回転、コックピット表示とボタンの大きさ、Station の視点は PC/VR の実機確認が必要です。
- カメラの強制ロールや揺れ演出は追加していませんが、Station と車体回転による VR 快適性を検証した意味ではありません。
- 4 台化、助手席、車同士の衝突競技、モバイル/Quest 対応、永続記録は未実装です。
- 本リポジトリの CI は静的・数理検査と構文解析です。Unity/Udon のコンパイルや VRChat 実行を CI が保証するものではありません。

## 検証の再実行

```sh
python -m pip install -r tools/requirements-validation.txt
python tools/check_csharp_syntax.py
python -m unittest discover -s tests -v
python -m unittest discover -s Tests/Review -v
```

Windows の大文字小文字を区別しないファイルシステムでは `tests` と `Tests` は同じフォルダーを参照します。全テキストを UTF-8 で読みます。Unity の結果と検証条件は [ClientSim 実行記録](docs/validation/UNITY_VALIDATION.ja.md)、残る受入項目は [受入試験](docs/ACCEPTANCE_TESTS.ja.md) にまとめています。

## 制作資料

- [ワールド設計](docs/WORLD_DESIGN.ja.md) / [実装状態](docs/IMPLEMENTATION_STATUS.ja.md)
- [映像観察と創作の区別](docs/VISUAL_REFERENCES.ja.md) / [公式資料](docs/REFERENCES.ja.md)
- [Unity 引き継ぎ](docs/UNITY_HANDOFF.ja.md) / [保全と checkpoint](docs/CHECKPOINTS.ja.md)

Tesla とは無関係のファン制作・研究用オリジナルモデルです。公式 3D モデルや画像テクスチャの抽出・再配布は行っていません。飛行・壁走り、コース寸法はゲームの創作仕様です。

以下は **車内更新前に Blender で制作した外観確認用レンダー** です。現在の車内計器や可動部を示す画像ではありません。Unity Play Mode / VRChat の実行画像とは区別しています。

![Blender 車両外観レンダー](SourceAssets/Vehicle/renders/01_hero.png)

![Blender 垂直カールコースのレンダー](SourceAssets/Course/preview_vertical_curl.png)

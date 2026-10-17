# Unity / ClientSim 検証記録

2026-10-17 に Windows のローカル Unity Editor で実施しました。自動試験と画像の元データは [clientsim-smoke.json](clientsim-smoke.json) です。

## 環境と範囲

| 項目 | 実際に使用した環境 |
|---|---|
| Unity | 2022.3.22f1 (887be4894c44)、Direct3D 11 |
| SDK | VRChat Base / Worlds 3.10.5、VPM Resolver 0.1.29 |
| テンプレート | この PC に導入済みの公式 VCC Worlds テンプレート |
| CPU / GPU | AMD Ryzen 9 5950X / NVIDIA GeForce RTX 3070 |
| 実行方式 | Unity Editor Play Mode、ClientSim、ローカル simulated player 1 人 |
| 入力 | Editor 検証コードが実行中の UdonBehaviour へ throttle / steering を設定 |
| 写真のカメラ | Play Mode 中だけ追加した検証用追従カメラ。`ScreenCapture.CaptureScreenshot` で Game View を保存 |
| 制限 | バンクと飛行は途中位置・姿勢・速度を設定して開始。フルラップ試験ではない |

設定メニューは検証カメラの InternalUI layer から除外します。左上の表示は同じ Play Mode 中に描画した検証用 HUD です。画像への生成 AI 加工、Blender レンダーの代用、合成は行っていません。検証用カメラと HUD はシーンへ保存されず、ワールドの利用者向け機能ではありません。

## 結果

Unity C# と UdonSharp のコンパイル、シーン構築、ClientSim スモーク試験 **19/19 項目成功**。検査項目は次のとおりです。

| 検査 | 結果・意味 |
|---|---|
| ClientSim 初期化 | ローカルプレイヤー API 有効 |
| Udon VM | Vehicle / Run の実行中 backing behaviour を取得 |
| 路面 | 15 個の MeshCollider を生成 |
| 車輪ピボット | 4 輪 × 3 LOD の 12 ピボットすべてに表示メッシュを接続 |
| 車両 LOD | 3 段の LODGroup が存在。遷移品質・性能は別途未検証 |
| 座標整合 | FBX の全 10 CP marker と manifest の位置差が .01 m 未満 |
| 乗車 | Interact → 所有権確認 → Station 入場 → driver ID 一致 |
| フリー走行 | 静止から 2.4 秒加速し、10 m 超移動・5 m/s 超・mode 1 を確認 |
| タイムアタック | 開始後 START を通過し `running=true`, `nextGate=1` |
| リセット | 計測停止、gate 0、スタート位置復帰 |
| バンク | 72 度バンク頂点付近へ配置、12 m/s の初速、.9 秒間に mode 2 の接地を確認 |
| 飛行 | LAUNCH 手前へ配置し、自由走行のゲート面通過から `flightAllowed=true` と mode 3 を確認 |
| 失格 | 計測中に 25 m の位置不連続を与え、discontinuity として無効化 |
| 降車 | Station 退出後、着座 false / driver −1 |
| キャプチャ 4 件 | それぞれ PNG が生成され、ファイルサイズと目視で内容を確認 |
| 実行エラー | EnteredPlayMode 以降の Error / Exception / Assert は 0 件 |

この検査では車両物理の式を Python 等へ置き換えず、Unity PhysX と Udon VM を実行しています。一方、実キーボードや VR 入力を使う人間の通しプレイではありません。内部入力値の設定と区間途中への配置による限定試験です。

Python 静的・数理検査は既存 17 件と Review 16 件の計 **33 件成功**。tree-sitter の C# 構文解析も成功しました。CI に Review 16 件を追加し、Linux でも両方を実行します。

## 実行で見つけて修正したこと

1. **初回 Program Asset 不足**: `AddUdonSharpComponent` が例外になるため、3 本の UdonSharp Program Asset を生成・コンパイルしてからシーンへ追加する処理を追加しました。既存の同一スクリプト用 Asset は再利用します。
2. **誤ったコース選択**: `Array.Sort` による FBX 選択で Windows では `LOD1_VisualOnly` が先頭になり、路面 Collider がありませんでした。メイン `VectorCoastal_Course.fbx` を明示し、Collider 0 件なら生成を失敗させます。
3. **FBX の X 軸反転**: 初回インポートでは WALL_APEX が `(−90,8,340)`、LAUNCH が `(−180,12,150)` にあり、JSON と反対側でした。インポートした見た目側だけ X の符号を補正し、Physics / Station / ゲートの canonical 座標を維持します。全 CP marker と実接地で再検証しました。
4. **車輪の回転軸**: Blender 由来のピボットを直接回すとローカル軸が Unity と一致しません。生成した車体の Prefab インスタンスを展開し、Unity 軸の親ピボットを作り、見た目の位置を維持して車輪を接続しました。未展開の Prefab では SetParent が拒否されたため、12 ピボットすべてに Renderer が接続されることも検査します。VR での見え方は未検証です。
5. **Windows の文字コード**: Python 検査で日本語 README が cp1252 として読まれて失敗したため、UTF-8 を明示しました。

SDK 3.10.5 の `VRCDefaultWorldScene.scenetemplate` は、参照先ファイル名とクラス名が異なる pipeline を持ち、この環境では新規シーン保存時に `SceneTemplateAsset.CreatePipeline` の `ArgumentNullException` を出しました。シーン保存・Udon コンパイル・その後の Play Mode は完了しています。SDK 本体には変更を加えていません。保存済みシーンを開く最終スモーク実行とは分けて扱っています。

## 画像

- [直線のフリー走行](../images/clientsim-free-drive.png) — mode 1、通常加速の途中。
- [タイムアタック開始](../images/clientsim-time-attack.png) — START 通過後、CP 1/10。完走の証拠ではありません。
- [バンク区間](../images/clientsim-bank-section.png) — 途中配置後の mode 2。直線からバンクまでの連続操作は未検証。
- [飛行区間](../images/clientsim-assisted-flight.png) — LAUNCH 手前への配置後、ゲート通過で mode 3。着地・一周完走は未検証。

バンク・飛行の HUD にある `INVALID / reset or driver change` は、前のタイムアタックをリセットして自由走行の区間検証へ移ったためです。計測中の成功画像としては扱いません。

## 再現と残る検証

Unity の **Drive Roadster → Run ClientSim smoke validation** を実行すると、同じ JSON と 4 枚の PNG を更新します。Editor 起動引数でも `-executeMethod RoadsterValidation.Run -roadsterExit` を指定できます。試験が失敗した場合は終了コード 1、全項目成功なら 0 です。

未実施: VRChat SDK Validation / Build & Test、VRChat へのアップロード、実キーボード・コントローラー入力、HMD、複数クライアント、同時乗車、途中参加、切断、所有権移譲、記録のネットワーク引き継ぎ、連続一周、着地、練習用垂直カール/天井、VR の視認性・快適性、CPU/GPU フレーム時間、実ネットワーク帯域。詳細は [受入試験](../ACCEPTANCE_TESTS.ja.md) を参照してください。

生の Unity ログはプロジェクトの `artifacts/` に残しています。ローカル環境情報を含むため Git 管理せず、公開する結果は本資料と JSON に限定します。

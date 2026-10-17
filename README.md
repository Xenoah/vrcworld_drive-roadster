# Drive Roadster — VRChat world source kit

オリジナル電動スポーツカーで地上・バンク/壁・空中を走る、PC/PCVR向けVRChatワールドの開発ソースです。1車種・1台の共有車両から始め、フリー走行と個人タイムアタックを試す構成です。

## 現在の状態

**Unityコンパイル、UdonSharp変換、ClientSim、VRChat Build & Test、実機VR、複数クライアント試験は未実施です。完成・公開済みワールドではありません。**

クラウドのBlender 4.3.2で車両/コース原本とFBX/GLBを生成し、Unity Editor用シーンビルダーとUdonSharpソースを作成しています。この環境にUnity Editorがないため、Unity用ソースキットとして渡します。外観は公式映像との比較・調整中です。

- 所有者だけの車両物理とManualスナップショット同期
- Station乗降、所有権喪失時退出、切断時復帰
- 接地サスペンション、バンク吸着、区間限定アシスト飛行
- 順序/方向/開口/時間/走行モードを検査するタイムアタック
- 5件のインスタンス参考ランキング。改造クライアント対策や世界共通記録は未提供
- 編集可能なBlender原本、FBX、GLB、生成スクリプト、検証記録

## Unityで開く

1. [VRChat Creator Companion](https://vcc.docs.vrchat.com/)で新規Worldsプロジェクトを作成する。
2. [公式対応Unity](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)を使う。2026-10-17確認時は2022.3.22f1。導入時に再確認する。
3. このrepoの `Assets/DriveRoadster` をそのプロジェクトの `Assets/` 配下へコピーする。SDKをこのrepoへ手動で同梱しない。
4. UdonSharp/Worlds SDKが読み込まれた状態でコンパイルエラーを確認する。
5. Unityメニュー `Drive Roadster > Build vertical slice scene` を実行。既存シーンを保存するか確認し、新規シーンを連番で保存する。
6. UdonSharpをコンパイルし、SDKのチェック結果を確認してBuild & Testする。
7. [実機受入テスト](docs/ACCEPTANCE_TESTS.ja.md)を実行。単体Play Modeの成功を複数人同期の成功と扱わない。

これはVCCテンプレートに取り込むソースキットです。架空のPackagesロックや未生成のUnityシーンは含めません。Unityが生成した `.meta`、シーン、実バージョンの依存ロックは検証後に追加してください。

## 操作候補

- 車両へInteractで乗車。運転権取得後に着座
- W/Sまたは移動スティック縦: 加速/制動・後退。飛行中Sで下降補助
- A/Dまたは移動スティック横: 操舵
- R: 車両復帰。T: ピットで停止してタイムアタック開始
- VRは車内のRESTART / TIME ATTACK / EXIT操作部を利用
- 通常Station退出も維持。視点の強制ロール/揺れ演出は追加しない

入力割当とStationの振る舞いは実機未確認です。操作部の視認性と届きやすさも実機調整が必要です。

## 資産と設計

- `SourceAssets/Vehicle/` / `SourceAssets/Course/`: 編集用原本・生成手順
- `Assets/DriveRoadster/Art/`: Unity用FBXと交換用GLB
- `Assets/DriveRoadster/Scripts/`: UdonSharpランタイム
- `Assets/DriveRoadster/Editor/`: シーン組立
- [ワールド設計](docs/WORLD_DESIGN.ja.md)
- [公式資料](docs/REFERENCES.ja.md)
- [Unity引き継ぎ](docs/UNITY_HANDOFF.ja.md)
- [保全とcheckpoint](docs/CHECKPOINTS.ja.md)

Teslaとは無関係のファン制作・研究用オリジナルモデルです。公式の3Dモデルや画像テクスチャを抽出・再配布しません。飛行・壁走りは本ゲームの創作仕様で、実車性能を保証するものではありません。

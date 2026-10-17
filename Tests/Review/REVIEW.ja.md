# Roadster 独立静的レビュー

レビュー日: 2026-10-17。対象: Runtime 3本、Editor builder、course manifest。
これは Unity/UdonSharp コンパイル、VRChat実機、ネットワーク実行、走行試験の合格証拠ではない。

## 確定指摘と対応

1. **修正確認済み**: manifest の AIR_01〜03 は `AssistedFlight` 指定だったが、Builder が `Flight` / `Air` のみを扱い、制約なしの 0 に変換していた。`AssistedFlight` → 3 と未知モードの例外が追加された。
2. **修正確認済み**: 非接地・飛行未許可時に `motionMode` が直前の Ground/Wall を保持していた。現在は 4（非接地）に更新される。Vehicle と Run の物理イベント順序は別途確認が必要。

## 統合担当へ渡した残件

- **高優先**: `flightAllowed` は launch から landing の正方向通過または Recover まで継続し、manifest の `flight_gap.allowed_bounds_*` は初回レビュー時の Runtime/Builder に取り込まれていなかった。コース外飛行・領域逸脱無効化は、そのままでは未実装。領域チェックを追加するか未実装として明示する。
- **高優先・実機必須**: 空席判定と `SetOwner` 後の 0.4 秒待機は、同時乗車が直列確定した証明ではない。両クライアントの `driverId` が空席である時間帯を含む試験が必要。所有者チェックと所有権喪失時の降車はあるが、ネットワーク遅延下の座席占有整合性は静的には保証できない。
- **中優先**: 初回同期前の `Interact` を止める `Networking.IsNetworkSettled` 等のガードが初回レビュー時になかった。所有者側の拒否と併せ、途中参加直後の古い空席状態からの要求を検証する。
- **中優先**: `generation` / `runId` は初回レビュー時には加算されるだけで、走行と現在の運転者世代の照合に使われていなかった。自動移譲時の CancelRun は存在するが、古いスナップショット除外を実装済みとは呼べない。
- **仕様差異**: 初回コードのランキングは5件であり、設計上の10件ではない。PB、運転席HUD、通信量・同期誤差の診断、4台プール、助手席、快適性設定、領域ショートカット抑止を実装済みと扱わない。

## 静的に確認できた良い点

- 車両とRunは同一GameObjectで、所有権の境界が一致している。
- 物理力は所有者のみ、非所有者はkinematicと補間で描画する。VRCObjectSyncと独自Transform同期の二重駆動はない。
- 所有権要求は占有時拒否。ローカル着座と所有権とdriverIdを合わせて運転権を検査する。
- 自動所有者移譲は前運転者の走行継続に使わず、Release/Recoverへ進む分岐がある。
- Runは現在の次ゲートのみを判定し、順方向の線分交差を使う。逆方向、面外、平行移動は採用しない。
- 完走時にはrunning=falseとなり、通常の次tickで同じ完走を重複登録する分岐はない。
- RecoverはCancelRunを呼び、飛行許可・速度・角速度を消去し、復帰番号を更新する。
- 遠隔の復帰番号変更時は補間せず位置を合わせる。途中参加は同期変数で現在値を得る構成。
- 同期配列そのものは初期化されている。文字列要素を空文字に初期化することとOnPostSerialization成功ログの追加は堅牢化として推奨。

## 独立テスト

`python3 -m unittest discover -s Tests/Review -v`

9テスト成功。全ゲートの正規直交座標系、正方向/逆方向/面外/平行/零長線分、面上終点での二重交差回避、飛行モード対応、既定速度での1物理tick変位、ゲート間時間下限を確認。

PythonはC#の線分交差式を独立にモデル化したもので、実際のUdonを起動していない。タイム計測精度、実コライダー通過、正常周回の成立、落下・壁走り・飛行着地、遅延/切断競合は未検証。

## 必須のUnity/VRChat試験

1. 対応Unity/Worlds SDKでBuilder実行、UdonSharp Compile、SDK validation、Build & Test。
2. 2クライアント同時乗車、100〜500ms程度の実測遅延下の競合、乗車要求中の切断。
3. 運転者切断、空席所有者切断、走行中/復帰直後/結果表示中の途中参加。
4. 正常1周、逆走、ゲート飛ばし、飛行リングを地上状態で通過、壁状態を失った直後のゲート、二重finish、reset後再開始。
5. VR/PC両入力、通常Jump退出、コックピットEXIT、高速退出時の安全な退避。
6. OnPostSerialization成功/byteCount、所有者ID、世代、runId、座席状態の実ログを記録。通信成功はRequestSerialization呼出しだけでは証明されない。

## 公式一次資料

- [Object Ownership](https://creators.vrchat.com/worlds/udon/networking/ownership/): 所有者のみの同期変更と切断時自動再割当。
- [Network Components](https://creators.vrchat.com/worlds/udon/networking/network-components/): OnOwnershipRequestが要求者と現所有者双方で動くこと、IsNetworkSettled、OnPostSerialization。
- [Networking Specs](https://creators.vrchat.com/worlds/udon/networking/network-details/): Manual同期の送信時刻はデータ量に応じ制限され、呼出し時刻即時送信ではない。配列初期化の必要性。
- [Late Joiners](https://creators.vrchat.com/worlds/udon/networking/late-joiners/): 現在の同期状態を利用する設計。
- [UdonSharp](https://creators.vrchat.com/worlds/udon/udonsharp/): 対応C#機能とClass Exposure TreeでのAPI確認。一般C#構文検査はUdonコンパイルの代わりにならない。
- [Editor Scripting](https://creators.vrchat.com/worlds/udon/udonsharp/editorscripting/): UdonSharp用追加/編集API。
- [VRC Station](https://creators.vrchat.com/worlds/components/vrc_station/): Station配置と乗降イベント。

## 再レビュー追記（2026-10-17 05:34 UTC）

上記は初回時点の記録。最新状態は以下を優先する。今回の再レビューはRuntimeの同期・所有権・ゲート・セッション状態に集中し、Builder/SDK型/シーン組立の独立レビューとは区別した。

| 対象 | 最新ソース確認 | 対応済み・未実装 | 実機検証 |
|---|---|---|---|
| AssistedFlight写像・非接地mode | 写像とmode=4あり | 対応済み | 正規周回・状態境界は未検証 |
| VehicleとRunの処理順 | Vehicle.FixedUpdate末尾からSamplePositionを呼ぶ | 対応済み | physics/ゲート遷移未検証 |
| 飛行許可領域 | 3軸bounds検査、許可取消、run失格あり | 対応済み | 領域境界・着地未検証 |
| 走行と運転者世代 | BeginRunで保存、SamplePositionで照合 | 対応済み | 所有権競合・移譲未検証 |
| 初回同期前乗車 | IsNetworkSettled＋非ownerのhasSnapshot条件あり | 対応済み | 初回待機解除未検証 |
| 同時乗車 | owner/driverId/着座ガードと喪失時退出あり | 0.4秒待機による競合リスクは未解消の検証課題 | 2クライアント以上必須 |
| リセット・切断 | CancelRun/Recover/復帰番号あり | ソース実装済み | イベント順・一度だけ復帰は未検証 |
| 途中参加・記録移譲 | 同期変数と現在値復元 | ソース実装済み | 配列・占有・車両位置未検証 |
| 送信時の位置 | OnPreSerializationで最新body位置/姿勢/速度を設定 | 対応済み | 実際の送信頻度・遅延未検証 |
| 送信診断 | OnPostSerializationのbytes/成功/失敗数あり | 対応済み | 実通信値未測定。フレーム時間・同期誤差診断は未実装 |
| 同期文字列配列 | null要素をStartで空文字化 | 対応済み | Udon同期未検証 |
| PB | ローカルpersonalBest更新あり | セッション内PB対応済み、永続PB未実装 | 実機未検証 |
| 記録件数 | 同期5件 | 5件実装、設計上の10件は未実装 | 実機未検証 |
| 車内HUD | speed/mode/time/gate/status更新あり | ソース実装済み | 可読性・配置・VR視認未検証 |
| 非owner physics | kinematic＋ContinuousSpeculative、ownerはContinuousDynamic | ソース実装済み | Unity警告/衝突挙動未検証 |
| 4台・助手席・快適性設定 | 対応コードなし | 未実装 | 未検証 |

初回レビューの「PB/HUD/診断なし」は現在は上表の範囲で対応済み。飛行領域検査は飛行許可中の専用AABBであり、コース全域の許可経路検証や改造クライアント対策を実装した意味ではない。

追加の致命的な確定バグは今回見つからなかった。ただし構文・静的検査からUnityコンパイル可能性やネットワーク安全性の合格を出していない。

### 小改善（統合担当が適用予定、05:34時点では未確認）

- Runのcurrent/previousPositionと開始位置検査をRigidbody.positionへ統一し、補間Transformと物理modeの参照時刻差を減らす。
- 遠隔HUDの速度はkinematic Rigidbody.velocityではなく同期netVelocityを用いるアクセサへ変更する。

### 再実行テスト

独立幾何9件＋Runtimeソース回帰チェック7件＝16/16成功。追加の `test_runtime_contracts.py` は条件やコールバックの存在を検査するだけであり、状態機械の実行・Udon VM・ネットワークのシミュレーションではない。

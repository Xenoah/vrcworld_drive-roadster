# 公式資料と確認状態

確認日: 2026-10-17。公式Web文書の確認と、Unity/VRChat実機での動作検証は別。下記の設計選択は公式保証を意味しない。

## Teslaの参考範囲

- [公式Roadsterページ](https://www.tesla.com/ja_jp/roadster): ロケット、スラスト拡張への注記を確認。2017年版検索キャッシュの座席数や航続距離を現行仕様として混ぜない。
- [Roadster Event](https://www.youtube.com/watch?v=qt5p-_tE8d0): ページ到達とタイトル確認のみ。映像フレーム未取得のため、実演内容を直接視聴・検証したとは扱わない。
- 飛行時間、寸法、推力、壁面吸着条件は今回の一次資料確認では確定できていない。
- ゲームの自由な飛行、壁走り、姿勢制御は本ワールド独自の創作仕様。公式モデルの抽出や画像のテクスチャ流用は行わず、参考を基に独自制作する。

## VRChatの同期・座席

1. [Object Ownership](https://creators.vrchat.com/worlds/udon/networking/ownership/)
   所有者が同期状態を更新する設計の根拠。運転者退出・切断・所有権移譲を扱い、Instance Masterに固定依存しない。
2. [VRC Object Sync](https://creators.vrchat.com/worlds/components/vrc_objectsync/)
   位置・回転・kinematic・重力の同期。車両ではCollision Ownership Transfer OFF、Force Kinematic On Remote ONを初期候補にする。復帰時のTeleportTo/FlagDiscontinuityを検証し、補間の引きずりを防ぐ。これらの設定選択は本ゲームの提案。
3. [VRC Station](https://creators.vrchat.com/worlds/components/vrc_station/)
   Immobilize For Vehicleを使用する候補。OnStationEntered/Exitedで座席状態を管理。通常退出を禁止する場合でも独立した安全退出を用意し、実機検証する。
4. [Network Variables](https://creators.vrchat.com/worlds/udon/networking/variables/)
   レース・座席・走行モード等の永続的な状態をUdonSyncedに保持。Manual同期ではRequestSerializationが必要。
5. [Late Joiners](https://creators.vrchat.com/worlds/udon/networking/late-joiners/)
   イベントのみでは途中参加時に現在状態を復元できない。同期変数から状態を再構築する。
6. [Network Details](https://creators.vrchat.com/worlds/udon/networking/network-details/)
   確認時の公式目安はUdon送信約11 KB/s、Continuousは1回約200 B。これを車両1台ごとの固定割当と誤解しない。ManualとContinuousを同じGameObjectに混在させるとManualに揃うため、同期境界を分離して設計する。最新制限と実測値は実装時にも確認する。

## 実装開始時の追加確認

- 現行Worlds SDKが指定するUnityの正確なバージョンとVCC導入経路
- 使用するRigidbody/Physics APIのUdon対応
- Station内のVR/デスクトップ入力、退出、アバター姿勢
- 時間基準API、永続記録API、使用条件と同期タイミング
- 個人の記録保存と、全員が共有する信頼できる世界ランキングの違い

これらの未確認項目に対して、推測のバージョン番号や実装済みAPIを記載しない。

## 追加の映像確認

その後、Tesla公式XのThe Ramp / The Kickflipを時刻指定して実再生・観察した。確認済み時刻、形状の観察、創作との差分は [VISUAL_REFERENCES.ja.md](VISUAL_REFERENCES.ja.md) を参照。先のYouTube到達確認だけで映像を見たと扱ってはいない。

- [Unity Layers in VRChat](https://creators.vrchat.com/worlds/layers/): 車体はWalkthrough(17)でプレイヤーとの物理衝突を避ける設計。サスペンションはDefault(0)のコースのみ照会。組込みレイヤーの衝突設定を書き換えない。

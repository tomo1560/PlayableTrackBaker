# GAIA Lyrics Movie Sample

`GAIA_LyricsMovie.unity` は、LRC 同期歌詞と宇宙的なモーショングラフィックスを
Build & Publish 時に PlayableTrackBaker で非破壊ベイクする VRChat Worlds 向けサンプルです。

## 開き方

1. Unity 2022.3.22f1 で `VerificationProject` を開きます。
2. `Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity` を開きます。
3. Play を押し、スポーン脇の `▶ GAIA START` ボタンを Interact すると
   GAIA（3:41.504）が再生されます。VRChat SDK 付属の ClientSim が
   Play モードでネットワークAPIをローカル実装するため、エディタ上でも
   ボタンのクリック（Interact）でそのまま動作します。

シーンは Build Settings に登録済みです。`GAIA Show Director` には
`PlayableDirector` と `TimelineBakeMarker` があり、保存TimelineのCustom PlayableTrackは
有効な未ベイク状態です。Unity Play時はCustom Playableを直接評価します。

Build & Publish / Build & Test 時は、ビルド用一時シーン上でTimelineを複製し、
`[Baked] Lyrics` / `[Baked] Baked Visuals` を自動生成します。保存シーンと元Timelineは
変更されず、一時アセットもビルド後に削除されます。

## SignalEmitter 演出

Timeline のグローバルMarker Track `GAIA Signal Cues` には、全10個のSignalEmitter(8種類の
SignalAsset)があります。Unity Playでは `SignalReceiver` とプレビュー演出(`GAIASignalPreviewEffect`)
が同じ組み合わせを再生し、Build & Publish / Build & Test時は同じEmitterを
`[Baked] Baked Visuals` のAnimationClip上にある `SendCustomEvent` へ変換して
`GAIASignalEventReceiver`（UdonSharp）が再生します。演出は加算式(発火済みの演出は
以降も点灯したまま)で、Bridge Dimのみ例外的にIntro Spark Shardsを退避させます。
Editor用のプレビューコンポーネントはビルド用シーンから自動除去されるため、本番で二重発火しません。

| 時刻(秒) | SignalAsset | Udonイベント | 演出 |
|---|---|---|---|
| 12.5 | GAIA Intro Spark | OnIntroSpark | Intro Spark Shards(シアンの小片×5)が点灯 |
| 30.0 / 90.0 / 120.0 | GAIA Verse Beacon | OnVerseBeacon | Verse Beacon Towerのセグメントが1個ずつ点灯(計3段) |
| 56.52 | GAIA Chorus Pulse | OnChorusPulse | Chorus Signal Halo(シアンの輪)が点灯 |
| 100.0 | GAIA Rapid Pulse A | OnRapidPulseA | Rapid Twin A(シアンの球)が点灯 |
| 100.4 | GAIA Rapid Pulse B | OnRapidPulseB | Rapid Twin B(マゼンタの球)が点灯 |
| 130.0 | GAIA Bridge Dim | OnBridgeDim | Bridge Veil(紺色の大スラブ)が点灯し、Intro Spark Shardsを退避 |
| 153.24 | GAIA Finale Bloom | OnFinaleBloom | Finale Signal Bloom(マゼンタの球)が点灯 |
| 210.0 | GAIA Outro Fade | OnOutroFade | Outro Ring(マゼンタのトーラス)が点灯 |

確認するには、Unity PlayまたはVRChatのBuild & Testで▶ボタンから再生し、上表の時刻ごとに
各演出が加わっていくことを見ます。Build & Test側ではVRChat client output logの
`[GAIA Signal / Udon]` でもUdonイベント名と発火回数を確認できます。100.0秒と100.4秒の
Rapid Pulse A/Bは間隔0.4秒の連続発火で、近接イベントの順序が保たれるかを確認できます。

## ベイク忠実度プローブ

`Baked Visuals` の5番目のレイヤー `Fidelity Probes` は、PlayableTrackBakerがPlayableTrackの
Transformモーションを30fps・High Precision Reduction 0.002でどれだけ忠実にベイクできているかを
目視確認するための7ステーションです。スポーン(0, 0.05, -9)から見て、ステージ手前の低いアーチ上に
横一列(x -7〜+7)で並びます。各ステーションの静止ペデスタルは常に動かず、非アニメーションの
Transformにベイク誤差が出ていないかの基準になります。Unity Play時のCustom Playable評価と、
Build & Publish後のベイク済みAnimationClip再生とを見比べて、動きが一致するかを確認します。

| ステーション | 動き | 何を検証するか | 不忠実だとどう見えるか |
|---|---|---|---|
| Comet Circuit | 球がリサージュ8の字(3Hz成分)を描く | 30fpsサンプリングの高周波モーション追従 | エイリアシング、軌道のカクつき／過度な平滑化 |
| Drift Monolith | 微小振幅(0.002〜0.003)のドリフト＋隣に100倍振幅の分身 | High Precision Reduction 0.002の閾値付近の精度 | 微小動作の消失(本体だけ止まって見え、分身は動く) |
| Gyro Spinner | 540°/秒(30fpsで1フレーム18°)の高速回転 | 四元数カーブのベイク精度 | 回転のエイリアシング、速度のブレ |
| Teleport Beacons | 4スロットを3.457秒ごとに瞬間移動(補間なし) | 離散ジャンプがベイクで補間されないか | ジャンプがスミア(滑らかに繋がって)見える |
| Orbit Pair (nested) | 親armが回転、子counterが逆回転しつつ上下振動 | 深い階層のTransformパス記録 | 子だけ動きが欠落/親子の位相がズレる |
| Phase Choir | 8本のピラーが位相をずらして波打つ | 複数オブジェクト間のタイミング同期 | 波が揃わず乱れる、一部だけ遅延する |
| Scale Beat | 指数関数の鋭いアタック/ディケイでスケール | 非正弦(鋭い)エンベロープのキー削減耐性 | アタックが鈍る、鋭さが失われて丸くなる |

## ▶ボタンとインスタンス同期再生

スポーン脇の `GAIA Play Button` を Interact すると、`Udon/GAIAShowController.cs`
（UdonSharp / Manual sync）が `Networking.GetServerTimeInSeconds()` を基準時刻として
同期変数へ書き込み、インスタンス全員の `PlayableDirector` を同じ経過秒から再生します。
AudioTrackはPlayableDirectorにバインドされているため、シークだけで音も追従します。

- 途中参加者は `OnDeserialization` で自動追従し、現在の経過秒へシークして再生します。
- 再生中にもう一度押すと、全員が最初から再生し直します。
- 再生中は5秒ごとにサーバー時刻と `director.time` を比較し、0.1秒を超える
  ずれをハードシークで補正します。
- シークで飛ばした SignalEmitter 由来の演出は `GAIASignalEventReceiver.ResyncToTime`
  が経過秒に合わせて復元します（実イベント計測の eventCount は増えません）。
- シークでグラフを再構築すると、最初の評価で 0 秒から経過秒までのベイク済み
  AnimationEvent が一括再生されます（Unity の実挙動）。演出は加算式のため最終状態は
  正しくなりますが、beaconFireCount 等の内部カウンタが二重加算されるため、
  シークの数フレーム後に `ResyncToTime` を再実行して正規化しています。
- 曲（221.504秒）の終了後に入室した場合は停止状態のまま、フィナーレ演出の
  最終状態のみ表示します。

## 再生成

`Tools > PlayableTrackBaker > Create GAIA Lyrics Movie Sample` を実行すると、
自動ベイク用の未ベイクシーンとTimelineを再生成します。

再生成メニューはこのサンプルが所有する Generated、Scene、旧GAIA用 bake clipのみを
作り直します。音源、LRC、フォントは変更しません。また、`Udon/GAIAShowController.asset`
（UdonSharp program asset）が無ければ生成し、未コンパイルなら同期コンパイルします。

## 仕様と注意

- 音源: GAIA / 魔王魂、48 kHz stereo OGG（Streaming）
- クレジット: 作曲 森田交一、作詞 火ノ岡レイ、ボーカル KEI
- 歌詞: `Data/GAIA.lrc` の時刻を使用（44個の表示オブジェクト）
- 演出: Transform のみを 30 fps、High Precision Reduction 0.002 でベイク
- ベイク忠実度確認用に `Baked Visuals/Fidelity Probes` の7ステーションを同梱
- 再生: ▶ボタンの Interact で開始（`playOnAwake` は無効）
- 同期: `Networking.GetServerTimeInSeconds()` 基準でインスタンス全体が同期し、
  途中参加者も自動追従します（5秒ごと・0.1秒閾値のドリフト補正付き）
- ParticleSystem は Unity/VRChat ネイティブ再生で、AnimationClip にはベイクしません

インスタンス同期・途中参加追従・再生UIはサンプル側の `Udon/GAIAShowController.cs` が
提供します。PlayableTrackBaker 本体の機能は Transform の非破壊ベイクのみです。

## Font

Japanese glyphs use Noto Sans JP. The font is licensed under the SIL Open Font License 1.1.
See `Fonts/OFL.txt`.

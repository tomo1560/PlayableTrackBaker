# GAIA Lyrics Movie Sample

`GAIA_LyricsMovie.unity` は、LRC 同期歌詞と宇宙的なモーショングラフィックスを
Build & Publish 時に PlayableTrackBaker で非破壊ベイクする VRChat Worlds 向けサンプルです。

## 開き方

1. Unity 2022.3.22f1 で `VerificationProject` を開きます。
2. `Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity` を開きます。
3. Play を押すと、GAIA（3:41.504）がローカルで自動再生されます。

シーンは Build Settings に登録済みです。`GAIA Show Director` には
`PlayableDirector` と `TimelineBakeMarker` があり、保存TimelineのCustom PlayableTrackは
有効な未ベイク状態です。Unity Play時はCustom Playableを直接評価します。

Build & Publish / Build & Test 時は、ビルド用一時シーン上でTimelineを複製し、
`[Baked] Lyrics` / `[Baked] Baked Visuals` を自動生成します。保存シーンと元Timelineは
変更されず、一時アセットもビルド後に削除されます。

## SignalEmitter 演出

Timeline のグローバルMarker Track `GAIA Signal Cues` には、56.52秒の `GAIA Chorus Pulse` と
153.24秒の `GAIA Finale Bloom` があります。Unity Playでは `SignalReceiver` と
プレビュー演出がシアンのハロー／マゼンタのブルームを切り替えます。

Build & Publish / Build & Test時は、同じ2個のSignalEmitterを
`[Baked] Baked Visuals` のAnimationClip上にある `SendCustomEvent` へ変換し、
`GAIASignalEventReceiver`（UdonSharp）が同じ演出を再生します。
Editor用のプレビューコンポーネントはビルド用シーンから自動除去されるため、本番で二重発火しません。

確認するには、Unity PlayまたはVRChatのBuild & Testで再生し、56.52秒にシアンの
ハロー、153.24秒にマゼンタのブルームへ切り替わることを見ます。Build & Test側では
VRChat client output logの `[GAIA Signal / Udon]` でもUdonイベント名と発火回数を確認できます。

## 再生成

`Tools > PlayableTrackBaker > Create GAIA Lyrics Movie Sample` を実行すると、
自動ベイク用の未ベイクシーンとTimelineを再生成します。

再生成メニューはこのサンプルが所有する Generated、Scene、旧GAIA用 bake clipのみを
作り直します。音源、LRC、フォントは変更しません。

## 仕様と注意

- 音源: GAIA / 魔王魂、48 kHz stereo OGG（Streaming）
- クレジット: 作曲 森田交一、作詞 火ノ岡レイ、ボーカル KEI
- 歌詞: `Data/GAIA.lrc` の時刻を使用（44個の表示オブジェクト）
- 演出: Transform のみを 30 fps、High Precision Reduction 0.002 でベイク
- 再生: `PlayableDirector.playOnAwake` によるローカル自動再生
- 同期: 各参加者の入室時刻から始まるため、インスタンス全体では同期しません
- ParticleSystem は Unity/VRChat ネイティブ再生で、AnimationClip にはベイクしません

インスタンス同期・途中参加追従・再生UIが必要な場合は、Udon のサーバー時刻同期を
別途追加してください。これは PlayableTrackBaker の Transform ベイク範囲外です。

## Font

Japanese glyphs use Noto Sans JP. The font is licensed under the SIL Open Font License 1.1.
See `Fonts/OFL.txt`.

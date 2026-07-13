# GAIA Lyrics Movie Sample

`GAIA_LyricsMovie.unity` は、LRC 同期歌詞と宇宙的なモーショングラフィックスを
PlayableTrackBaker で AnimationClip 化した VRChat Worlds 向けサンプルです。

## 開き方

1. Unity 2022.3.22f1 で `VerificationProject` を開きます。
2. `Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity` を開きます。
3. Play を押すと、GAIA（3:41.504）がローカルで自動再生されます。

シーンは Build Settings に登録済みです。`GAIA Show Director` の Timeline には、
元のカスタム PlayableTrack と、生成済みの `[Baked] Lyrics` / `[Baked] Baked Visuals`
が含まれます。元トラックはミュート済みなので、Play 時には VRChat と同じ
AnimationTrack 側だけで歌詞と宇宙演出が動きます。

完成シーンは手動ベイク済みで、二重ベイクを避けるため `TimelineBakeMarker` を
保存前に除去します。再生成時はマーカーを一時作成し、ベイク後に再び除去します。

## 再生成

`Tools > PlayableTrackBaker > Create GAIA Lyrics Movie Sample` を実行すると、
シーン再生成からプレビューベイクまで一操作で完了します。

再生成メニューはこのサンプルが所有する Generated、Scene、GAIA 用 bake clip のみを
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

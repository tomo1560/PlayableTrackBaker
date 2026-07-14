# Audio Volume Experiment

VRChatの音量スライダー（Master / World）が **Timeline AudioTrack経由の音に効かない** のか
どうかを実機で切り分けるための最小ワールド。

## 背景

GAIAワールドで「World音量もMaster音量も0にしても音楽が鳴り続ける」症状が出た。
Unity側の一次情報では、TimelineのAudioTrackはバインドしたAudioSourceの
`volume` を無視し、AudioMixerGroupにもルーティングされないことが確認済み
（空間化設定は2018.2以降反映される）。VRChatのスライダーがAudioSource/ミキサー層で
作用しているなら、Timeline音声だけ素通りするはず——という仮説を検証する。

## シーンの生成

- メニュー: `Tools > PlayableTrackBaker > Create Audio Volume Experiment Scene`
- コマンドライン: `-executeMethod AudioVolumeExperiment.Editor.AudioVolumeExperimentSceneBuilder.BuildFromCommandLine`

生成物: `Scenes/AudioVolumeExperiment.unity` と `Generated/AudioVolumeExperiment_Timeline.playable`

## 実験手順

1. シーンを開き、VRChat SDKの **Build & Test** で起動する。
2. 左のシアンのボタン（`Toggle: Timeline AudioTrack`）で **AudioSourceにバインドしたTimeline経由** の再生を開始。
3. 中央のアンバーのボタン（`Toggle: Unbound AudioTrack`）で **未バインドAudioTrack** の再生を開始。
4. 右のマゼンタのボタン（`Toggle: AudioSource.Play()`）で **素のAudioSource** の再生を開始
   （同じ曲が重なって鳴るので、片方ずつでもよい）。
5. VRChatの設定で **World音量 → Master音量** の順にスライダーを動かす。

各経路はクリップ・実効音量(0.6)・2D再生まで同一条件に揃えてある。違いは再生経路だけ。

### 中央ボタン＝バグの意図的な再現

中央の経路はAudioTrackに**AudioSourceを意図的にバインドしていない**。この状態のTimeline音声は
AudioPlayableOutputからAudioListenerへ直接2D出力されるため、音は鳴るのに
AudioSource層で作用するVRChatの音量制御（World/Master）を全て素通りする——
かつてビルド時ベイクのバインディング欠落で起きていた症状の最小再現。
アドオンなしの標準操作でも、Timelineのバインディング欄をNoneにする／TimelineAssetを
複製してDirectorに差し替える、のどちらでも同じ状態になる。

## 判定

| 観測結果 | 結論 |
| --- | --- |
| AudioSource側だけ音量が変わる | Timeline AudioTrackがVRChatの音量制御を素通りしている（仮説確定）。音楽はUdon制御のAudioSourceに移すべき |
| 両方とも変わらない | 経路以外の原因（クライアント設定・デバイス等）を疑う |
| 両方とも変わる | Timeline犯人説は誤り。GAIAシーン固有の別要因を調査する |

## 実験結果（2026-07-14）

**両経路ともローカルの音量スライダーで音量変更できた** → Timeline AudioTrack自体は
VRChatの音量制御に従う。犯人説は棄却。

真因はGAIAワールドのビルド時非破壊ベイク: `PlayableTrackBakeSceneProcessor` が
Timelineを `CopyAsset` で複製して差し替える際、PlayableDirectorのトラックバインディング
（トラックオブジェクト参照がキー）を引き継いでおらず、**ビルド版だけAudioTrackが
未バインド（AudioSourceを介さない直接再生）になっていた**。未バインドのTimeline音声は
AudioSource層で作用するVRChatの音量制御を素通りする。
修正: `CopyTrackBindings` でクローンへバインディングを引き継ぐ（PlayableTrackBaker.cs）。

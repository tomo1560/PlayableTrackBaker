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
2. 左のシアンのボタン（`Toggle: Timeline AudioTrack`）で **Timeline経由** の再生を開始。
3. 右のマゼンタのボタン（`Toggle: AudioSource.Play()`）で **素のAudioSource** の再生を開始
   （同じ曲が二重に鳴るので、片方ずつでもよい）。
4. VRChatの設定で **World音量 → Master音量** の順にスライダーを動かす。

両経路はクリップ・`volume`(0.6)・`spatialBlend`(0)・VRC Spatial Audio Source
（空間化無効・Gain 0）まで同一条件に揃えてある。違いは再生経路だけ。

## 判定

| 観測結果 | 結論 |
| --- | --- |
| AudioSource側だけ音量が変わる | Timeline AudioTrackがVRChatの音量制御を素通りしている（仮説確定）。音楽はUdon制御のAudioSourceに移すべき |
| 両方とも変わらない | 経路以外の原因（クライアント設定・デバイス等）を疑う |
| 両方とも変わる | Timeline犯人説は誤り。GAIAシーン固有の別要因を調査する |

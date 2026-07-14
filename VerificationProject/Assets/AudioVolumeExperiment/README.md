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
2. 各ボタンで経路を再生する（重ねてもよいが、片方ずつが聞き分けやすい）:

| ボタン | 経路 | スライダーの予想 |
| --- | --- | --- |
| 1. TIMELINE（シアン） | AudioSourceにバインドしたAudioTrack | 効く（実測済み） |
| 2. UNBOUND（アンバー） | 未バインドAudioTrack＝バグ再現 | **効かないはず** |
| 3. VIDEO（バイオレット） | VideoPlayerのDirect音声出力（ビープ音+映像） | 効くはず（VRChatが自動修正） |
| 4. AUDIOSOURCE（マゼンタ） | 素のAudioSource.Play() | 効く（実測済み） |

3. VRChatの設定で **World音量 → Master音量** の順にスライダーを動かす。

音声経路はクリップ・実効音量(0.6)・2D再生まで同一条件に揃えてある。VIDEOだけは
音楽と聞き分けるため専用のビープ動画（`Video/DirectAudioProbe.mp4`、MediaEncoderで
プロシージャル生成）を使う。

### UNBOUND＝バグの意図的な再現

AudioTrackに**AudioSourceを意図的にバインドしていない**。この状態のTimeline音声は
AudioPlayableOutputからAudioListenerへ直接2D出力されるため、音は鳴るのに
AudioSource層で作用するVRChatの音量制御（World/Master）を全て素通りする——
かつてビルド時ベイクのバインディング欠落で起きていた症状の最小再現。
アドオンなしの標準操作でも、Timelineのバインディング欄をNoneにする／TimelineAssetを
複製してDirectorに差し替える、のどちらでも同じ状態になる。

### VIDEO＝VRChat側に対策が実在する類似ケース

VideoPlayerの `audioOutputMode = Direct` も同様にAudioSourceを介さない音になるが、
VRChatの `WorldValidation.SecurityScan`（com.vrchat.base）は**ロード時にDirect出力を検出して
AudioSource出力へ強制変換する**（ログ: `VideoPlayer using DIRECT audio output fixed.`）。
そのため実機での予想は「スライダーが効く」。

同じSecurityScanには**未バインドAudioPlayableOutputへの対策も存在する**が、こちらは
`playableGraph.IsValid()` のときしか走らない＝**ロード時に再生中でないDirector
（playOnAwake=false、Udonが後からPlay()する構成）は素通りする**。GAIAワールドが
クライアント側の防御をすり抜けて症状に至ったのはこの穴のせい。UNBOUNDボタンも
Interact起動なので修正されない（=効かないまま）はず。

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

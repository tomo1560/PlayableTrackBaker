# PlayableTrackBaker 検証用プロジェクト

`PlayableTrackBaker` を実際に動かして確認するための Unity プロジェクト骨組みです。
「自作 PlayableTrack でキューブを上下に動かす → ベイク → `[Baked]` トラックだけで同じ動きになる」までをワンクリックのセットアップで再現できます。

> **重要:** VRChat SDK は同梱できません（再配布不可）。SDK を入れるまでは `TimelineBakeMarker.cs` / `PlayableTrackBaker.cs` が
> `VRC.SDKBase` を参照できず**コンパイルエラーになります**。まず下記手順1〜2で SDK を入れてください。

## 含まれるもの

| パス | 役割 |
| --- | --- |
| `Packages/manifest.json` | `net.tomo1560.playabletrackbaker` をリポジトリ内の UPM パッケージから参照 |
| `Assets/PlayableTrackBakerTests/Samples/MoveSamplePlayableAsset.cs` | 検証用の自作 PlayableTrack。キューブを正弦波で上下に動かす（VRChat では動かない＝ベイク対象） |
| `Assets/PlayableTrackBakerTests/Editor/BakeTestSetup.cs` | 検証シーンを自動生成するメニュー |

## 手順

### 1. プロジェクトを開く

Unity Hub で **Add project from disk** → この `VerificationProject` フォルダを選択して開きます。
想定バージョンは `2022.3.22f1`（`ProjectSettings/ProjectVersion.txt`）。手元の VRChat 対応 2022.3 系があればそれで構いません。

初回オープン時は VRChat SDK 未導入のためコンパイルエラーが出ます。ここでは無視して次へ。

### 2. VRChat Worlds SDK を入れる

VCC（VRChat Creator Companion）または ALCOM でこのプロジェクトを **Add** し、**VRChat SDK - Worlds** を追加します。
Unity に戻ってコンパイルが通れば準備完了です（エラーが消えます）。

### 3. 検証シーンを生成する

メニュー **Tools > Timeline > Create Bake Test Setup** を実行します。以下が自動生成されます。

- `BakeTest_Target`（キューブ）
- `BakeTest_Director`（`PlayableDirector` + `TimelineBakeMarker`）
- `Assets/PlayableTrackBakerTests/Generated/BakeTest_Timeline.playable`

### 4. ベイク前の動きを確認する

1. `BakeTest_Director` を選択し、**Window > Sequencing > Timeline** を開きます。
2. 再生ヘッドをドラッグすると、キューブが上下に動きます（これが自作 PlayableTrack の動き）。

### 5. ベイクする（手動＝破壊的プレビュー）

> このツールには 2 モードあります。**手動メニュー（このステップ）は結果を目視確認するための破壊的ベイク**で、シーンと `.playable` を実際に書き換えます。**アップロード時は非破壊ベイク**が別途走ります（後述の「実機での確認」参照）。

メニュー **Tools > Timeline > Bake All PlayableTracks** を実行します（または `TimelineBakeMarker` のインスペクタの **Bake This PlayableTrack** ボタン／コンポーネント右クリック **> Bake This** でも可）。想定される結果:

- `Assets/BakedTimelineClips/BakeTest_Director_BakeTest_Target_<安定ハッシュ>_baked.anim` が生成される
- Timeline に `[Baked] BakeTest_Target` という AnimationTrack が追加される
- 元の `Move (Custom PlayableTrack)` が**自動でミュート**される（`Mute Playable Tracks After Bake` が既定オンのため）
- `BakeTest_Target` に `Animator` が自動追加される

### 6. ベイク結果を確認する

再度 Timeline の再生ヘッドを動かします。**元トラックはミュート済みなので、`[Baked]` トラックだけでキューブが同じように上下すれば成功**です。

比較したい場合は `[Baked]` トラックを一時ミュート／元トラックをアンミュートして動きを見比べ、確認後は元に戻してください（`[Baked]` を有効・元を無効の状態がアップロード想定）。

## チェックポイント（修正点の検証）

- **パス衝突修正**: `recordRoots` に同名オブジェクトを2つ入れても、オブジェクト識別子を含む異なる安定ハッシュで別ファイルになる。
- **自動ミュート**: 手動ベイク後にエディタで元トラックが鳴らず、二重再生にならない。再ベイクしても（内部で一旦アンミュートするため）記録が空にならない。
- **null ガード**: `recordRoots` に空要素があっても例外で止まらず、警告ログを出してスキップする。
- **float 精度**: `Frame Rate` を上げても、長いクリップでタイミングがズレにくい。

## 非破壊ベイク（アップロード時）の検証

手動ベイクとは別に、**Build & Publish 時に非破壊ベイクが自動で走ります**。確認手順:

1. まず手動ベイクの痕跡を消しておく（`BakeTest_Root` を削除 → **Create Bake Test Setup** で作り直す）。これでシーンには `[Baked]` トラックが無い状態になります。
2. **Build & Publish**（テスト用の Build & Test でも可）を実行。
3. **保存シーンと `BakeTest_Timeline.playable` に `[Baked]` トラックが増えていないこと**を確認する（＝非破壊。Git 管理していれば差分が出ない）。
4. `Assets/BakedTimelineClips/__ndbake_temp__/` がビルド後に残っていないこと（自動削除される）。
5. Console に `[PlayableTrackBaker] N 個の Timeline を非破壊ベイクしました（ビルド用コピー）。` が出ていること。
6. アップロードしたワールド（または Build & Test のローカル起動）でキューブが上下に動けば、VRChat 上でも `[Baked]` トラックが機能していることの確認になります。

### 実機検証状況

VRChat SDK 3.10.4 / Unity 2022.3.22f1 で、`IProcessSceneWithReport` の発火、`IEditorOnly` 除去前のマーカー取得、Timeline クローンとサブアセットクリップを使った非破壊ベイクを確認済みです。

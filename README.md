# PlayableTrackBaker for VRChat

VRChat ワールドのアップロード（Build & Publish）時に、Timeline の **PlayableTrack（カスタムトラック）の評価結果を AnimationClip に自動ベイク**し、AnimationTrack として同じ Timeline に追加するエディタ拡張です。

VRChat ワールドでは Timeline 自体は動作しますが、カスタム C# に依存する PlayableTrack は動作しません。このツールを使うと、エディタ上でカスタムトラックが生み出す動きをそのまま AnimationClip に焼き込み、VRChat 上でも再生できるようにします。

アバタープロジェクトでも、Timeline をオーサリングツールとして使う形で手動ベイクとゴースト比較プレビューを利用できます（自動ベイクはワールド専用。詳細は「[アバタープロジェクトでの利用](#アバタープロジェクトでの利用)」参照）。

## 動作環境

- Unity 2022.3 系（VRChat 推奨バージョン）
- VRChat SDK3 - Worlds または Avatars（VCC / ALCOM でのインストールを想定。依存は両者共通の base パッケージ `com.vrchat.base`）
- Timeline パッケージ（com.unity.timeline）

## 導入

このリポジトリは UPM パッケージ `net.tomo1560.playabletrackbaker`（`Packages/net.tomo1560.playabletrackbaker/`）として構成されています。導入方法は 2 通りです。

### A. UPM パッケージとして参照（推奨）

Unity プロジェクトの `Packages/manifest.json` の `dependencies` に、パッケージへのパスを追加します。

```json
"net.tomo1560.playabletrackbaker": "file:../../Packages/net.tomo1560.playabletrackbaker"
```

- ローカル参照なら `file:` に相対パスを、Git 参照なら
  `"net.tomo1560.playabletrackbaker": "https://github.com/tomo1560/PlayableTrackBaker.git?path=Packages/net.tomo1560.playabletrackbaker"`
  のように指定します（GitHub リポジトリ名を `PlayableTrackBaker` とした場合。以降の URL 例もこの前提です）。
- 本リポジトリの `VerificationProject/` は前者（`file:` ローカル参照）でこのパッケージを取り込む検証用プロジェクトです。
- パッケージは VRChat SDK の base パッケージ（`com.vrchat.base` / `VRC.SDKBase`。Worlds・Avatars どちらの SDK にも含まれます）と Timeline（`com.unity.timeline`）に依存します。

パッケージ内の構成:

```
Packages/net.tomo1560.playabletrackbaker/
├── package.json
├── Runtime/
│   ├── net.tomo1560.playabletrackbaker.Runtime.asmdef   (VRC.SDKBase 参照)
│   └── TimelineBakeMarker.cs
└── Editor/
    ├── net.tomo1560.playabletrackbaker.Editor.asmdef     (Runtime + Unity.Timeline 参照)
    ├── PlayableTrackBaker.cs
    └── PlayableTrackPreview.cs
```

### B. ソースを直接 Assets に置く（ドロップイン）

パッケージ管理を使わない場合は、上記 3 つの `.cs` を Assets 内へコピーしても動きます。その際は `PlayableTrackBaker.cs` と `PlayableTrackPreview.cs`（非破壊のゴースト比較プレビュー）を必ず **`Editor` フォルダ内**へ、`TimelineBakeMarker.cs` はランタイム側へ置いてください。asmdef を使わない場合は `Editor` フォルダ規約だけで分離されます。

## 配布（VPM リポジトリ / VCC・ALCOM）

このリポジトリは GitHub Actions ＋ GitHub Pages で **VPM リポジトリ（VCC/ALCOM の "Add Repository" で登録できる listing）** を自前ホストできる構成になっています（VRChat 公式テンプレート `vrchat-community/template-package` 準拠）。

構成要素:

- `.github/workflows/release.yml` — 手動実行（Build Release）で、`package.json` の `version` を元にタグを打ち、パッケージ `.zip` ＋ `.unitypackage` ＋ `package.json` を GitHub Release に添付。
- `.github/workflows/build-listing.yml` — Release 後に `vrchat-community/package-list-action` で Release 群から `index.json`（VPM listing）を生成し、`Website/` の表示ページごと GitHub Pages へ公開。
- `Website/` — listing 表示ページ（"Add to VCC" ボタン付き）のテンプレート。

### 公開手順（初回セットアップ）

1. GitHub にこのリポジトリを push。
2. **Settings → Secrets and variables → Actions → Variables** で
   リポジトリ変数 **`PACKAGE_NAME` = `net.tomo1560.playabletrackbaker`** を作成。
3. **Settings → Secrets and variables → Actions → Secrets** で、GameCI 用の **`UNITY_LICENSE`**（`.ulf` ファイル全体）、**`UNITY_EMAIL`**（Unity ID のメールアドレス）、**`UNITY_PASSWORD`**（Unity ID のパスワード）を作成。
4. **Settings → Pages** の Source を **"GitHub Actions"** に設定。
5. **Actions → Build Release** を手動実行（`workflow_dispatch`）。Unity EditMode テストが成功した場合だけ Release が作られ、続けて listing が Pages へ公開されます。
6. 公開 URL（例: `https://tomo1560.github.io/PlayableTrackBaker/index.json`）を VCC/ALCOM の **Add Repository** に登録。

### バージョンを上げて再リリース

`Packages/net.tomo1560.playabletrackbaker/package.json` の `version` を上げて push → **Build Release** を実行するだけ。タグ・Release・listing 更新まで自動です。

## 使い方

### 1. マーカーを設定する

ベイクしたい Timeline を再生している PlayableDirector と同じ GameObject に `TimelineBakeMarker` コンポーネントを追加し、以下を設定します。

| プロパティ | 説明 |
| --- | --- |
| Director | ベイク対象の PlayableDirector。未指定なら同じオブジェクトから自動取得 |
| Record Roots | PlayableTrack が動かしているオブジェクトのルート。ここ以下の階層の動きが記録される |
| Record All Properties | オンにすると Transform 以外（BlendShape 等の全アニメーション可能プロパティ）も記録する |
| Frame Rate | ベイクのサンプリングレート（既定値 60） |
| High Precision | オンにすると精度優先モードでベイクする。キーフレーム削減を行わず、サンプルした全フレームをそのままキー化する（サンプル点で厳密一致・線形補間）。既定はオフ（軽量・削減あり）。詳細は「ベイクの精度と限界」参照 |
| High Precision Reduction | 精度優先モード時のキー削減量（0〜0.05）。各カーブの値域に対する最大許容誤差の割合。0（既定）で無削減。上げるほど誤差上限を保ったままキーを間引いてクリップを軽くする。軽量モードや Record All Properties の追加分には影響しない |
| Mute Playable Tracks After Bake | オン（既定）にすると、ベイク後に元の PlayableTrack を自動でミュートする。エディタプレビューでの二重再生を防ぎ、VRChat 上と同じく `[Baked]` トラックだけが再生される状態に揃う。再ベイク時は自動でアンミュートしてから評価するので、記録が空になることはない |

### 2. ゴースト比較プレビューで確認する（非破壊・推奨）

メニューの **Tools > Timeline > Bake Preview (Ghost Compare)** を実行すると専用ウィンドウが開きます。ここで **Marker** を指定して「プレビュー開始」を押すと、シーンやアセットを一切書き換えずに、**元の PlayableTrack が動かす実オブジェクト**と、**ベイク結果のクリップで駆動する使い捨てのゴースト**を並べて比較できます。

- 時刻スライダーで任意のフレームを、「再生」トグルで連続再生を確認できます。
- 「ゴーストのオフセット(X)」でゴーストを横にずらして並べるか、`0` にして重ねて厳密確認できます。
- 「最大ローカル位置誤差」が表示されます。オフセット `0`（重ね置き）で誤差がほぼ 0 なら一致です。ずれる場合は `Frame Rate` を上げるか `High Precision` を有効化してください。
- **原則として非破壊**です。ゴーストは `HideFlags.HideAndDontSave` の一時オブジェクトで、停止時に破棄され、実オブジェクトの Transform も元へ戻ります。Git 差分は出ません。
- 制約: 復元されるのは **Record Roots 配下の Transform のみ**です。Activation など他トラックや、`Record All Properties` 使用時の Transform 以外のプロパティを動かす Timeline では、停止後に一部状態が残る場合があります。**プレビュー中はシーンを保存しないでください。**

### 3. 手動でベイクして確認する（破壊的・任意）

ゴーストプレビューで十分ですが、生成される `.anim` の中身（キー数・カーブ）まで見たい場合はこちらを使います。以下のいずれかでベイクが走ります。**これは開いているシーンと TimelineAsset を実際に書き換える「破壊的」ベイクです**（Git 差分が出ます）。アップロード時の自動ベイクは非破壊なので、この手動ベイクは確認だけに使い、確認後は元に戻して構いません。

- **Tools > Timeline > Bake All PlayableTracks** … シーン内すべての `TimelineBakeMarker` をベイク
- **Tools > Timeline > Bake Selected PlayableTracks** … Hierarchy で選択したオブジェクト（子孫含む）のマーカーだけをベイク
- `TimelineBakeMarker` コンポーネントを右クリック **> Bake This** … その 1 個だけをベイク
- `TimelineBakeMarker` のインスペクタ下部の **Bake This PlayableTrack** ボタン … 表示中のマーカーをその場でベイク

- AnimationClip は `Assets/BakedTimelineClips/` に `ディレクター名_ルート名_インデックス_baked.anim` として保存されます（Record Roots 内でのインデックスを含むため、同名のオブジェクトを複数指定してもパスが衝突しません）
- Timeline に `[Baked] ルート名` という AnimationTrack が追加されます
- Timeline ウィンドウで `[Baked]` トラックだけを有効にして再生し、元の動きと一致するか確認してください

### 4. アップロードする

VRChat SDK の **Build & Publish** を実行するだけです。ビルド直前に**非破壊**の自動ベイクが走ります。開いているシーンや `.playable` アセットには一切変更が残らないため、手動ベイクした `[Baked]` トラックが残っていなくても、アップロードには自動で焼き込まれます。

### SignalEmitter → Udon Event（Worlds PoC）

`TimelineBakeMarker` の **Bake Signal Events** を有効にすると、指定した SignalAsset を event host の AnimationClip 上の `SendCustomEvent` AnimationEvent に変換できます。event host は Record Roots の一つで、常時有効かつ同じ GameObject に対象 UdonBehaviour がある必要があります。

- `Signal Event Routes` に SignalAsset と Udon custom event 名を明示対応付けします。未登録 Signal は無視されます。
- AnimationEvent は host clip 1本だけに置かれるため、複数 Record Root でも二重発火しません。
- **Worlds 専用・ローカル実行のみ**です。ネットワーク同期、途中参加、シーク／逆再生、`retroactive`、`emitOnce` の SignalEmitter 意味論は再現しません。
- VRChat へアップロードする前に、Build & Test で UdonBehaviour の受信と発火順を確認してください。

## アバタープロジェクトでの利用

アバタープロジェクトでは、**Timeline を「動きのオーサリングツール」として使い、その結果を `.anim` に書き出す**用途で利用できます。ワールドとはできることが異なります。

| 機能 | アバターでの可否 |
| --- | --- |
| 手動ベイク（Tools > Timeline > Bake All 等） | ○ そのまま使える |
| ゴースト比較プレビュー | ○ そのまま使える |
| アップロード時の自動ベイク | ✕ ワールド専用（下記参照） |

**ワークフロー:**

1. エディタ上で Timeline + カスタム PlayableTrack で動きを作り、`TimelineBakeMarker` を設定する（ワールドと同じ手順）。
2. 手動ベイクを実行し、`Assets/BakedTimelineClips/` に生成された `.anim` を得る。
3. その clip を**自分でアバターの Animator Controller（FX レイヤー等）に組み込む**（Modular Avatar / VRCFury などの利用も可）。clip の組み込みはこのツールの守備範囲外です。

**ワールドとの違い・注意点:**

- **アバターでは Timeline 自体が再生されません。** PlayableDirector はアバターの許可コンポーネントに含まれないため、`[Baked]` トラックを追加した Timeline をアバターに含めても動きません。動きの再生はあくまで Animator Controller 経由です。ベイクに使った Timeline / PlayableDirector 一式はアバターに含めないでください。
- **自動ベイクは発火しません。** アップロード時の自動ベイクはシーンビルド時のフック（`IProcessSceneWithReport`）で動くため、プレハブ単位でビルドされるアバターでは実行されません。必ず手動ベイクを使ってください。
- **Humanoid ボーンには使えません。** ベイク結果は generic な Transform カーブなので、Humanoid リグのボーンに適用するとヒューマノイドアニメーションと競合します。対象は非 Humanoid の子オブジェクト（小物・ギミック・アクセサリ等）に限定してください。
- **clip のパスは Record Root からの相対**です。Animator Controller に載せる際は、アニメーションさせる階層が Record Root と同じ相対パスになるようレイヤー／Animator の配置に注意してください（Record Root 自体に Animator を置くのが確実です）。

## 仕組み（2 つのモード）

このツールには **破壊的な手動ベイク**（確認用）と **非破壊な自動ベイク**（アップロード用）の 2 モードがあり、記録処理（Record Roots を 1 フレームずつ評価してスナップショット）は共通です。記録の内部方式は `High Precision` で軽量／精度優先を切り替えられます（詳細は「ベイクの精度と限界」参照）。

### 手動ベイク（Tools > Timeline > Bake All / Bake Selected、右クリック Bake This、インスペクタのボタン）

1. 開いているシーンの各 Director の TimelineAsset を直接書き換えます。
2. AnimationClip を `Assets/BakedTimelineClips/` に保存し、`[Baked]` AnimationTrack を追加します。
3. `[Baked]` トラックはベイクのたびに削除してから再生成されるため、何度実行しても増殖しません。

### 自動ベイク（アップロード時・非破壊）

1. Unity の `IProcessSceneWithReport` により、ビルド用に複製された**一時シーン**の上で処理が走ります。保存済みシーンには影響しません。
2. TimelineAsset は共有アセットなので、`AssetDatabase.CopyAsset` で一時フォルダ（`Assets/BakedTimelineClips/__ndbake_temp__/`）にクローンし、そのクローンに対してベイクして Director に差し替えます。**元の `.playable` は一切変更されません。**
3. 生成した AnimationClip はクローン Timeline のサブアセットとしてビルドに含まれます。
4. 一時アセットはビルド完了後に自動削除されます（ビルドが途中失敗した場合もエディタ起動時に掃除されます）。
5. `TimelineBakeMarker` は `IEditorOnly` を実装しているため、アップロード時に自動で除去されます。

> `Object.Instantiate` では Timeline のトラックが元アセットを参照したままになり非破壊にならないため、クローンには `AssetDatabase.CopyAsset` を使っています。

## ベイクの精度と限界（課題）

ベイクの「精度」は座標の細かさではなく、**①時間サンプリング精度**と**②そもそも記録できるプロパティかどうか**の2軸で決まります。特に Particle が焼けないのは①ではなく②の問題です。

### 座標そのものの精度は劣化しない

`GameObjectRecorder` が記録するのは各フレームでの Transform／プロパティの値そのもので、Unity ランタイムと同じ 32bit float です。「位置が丸められて荒くなる」といった空間的な精度劣化はなく、ここは実質ロスレスです。

### 実際の精度を決めるのは Frame Rate（時間分解能）

記録は `director.time` を `1/fps` 刻みで進めてスナップショットを取り、**キーフレーム間は AnimationClip の補間**という構造です。したがって時間サンプリングが精度の本質的な上限になります。

- **元の動きの最高周波数の 2 倍以上の fps（ナイキスト）**が必要です。fps が低いと速い振動・急なイージングがエイリアスして「カクつき／ズレ」になります。
- `Frame Rate` を上げるほど原理的には元の動きに漸近しますが、クリップサイズと引き換えです。実用上は 60〜120 で足りることが多く、高速な回転体などは 120〜240 まで上げる価値があります。

### 精度優先モード（High Precision）と軽量モード

`Frame Rate` を上げてもまだ曲線がナマる場合、原因は**キーフレーム削減**です。ベイク方式を `High Precision` トグルで切り替えられます。

| モード | 挙動 | 向き |
| --- | --- | --- |
| **軽量**（既定・`High Precision` オフ） | `GameObjectRecorder` の既定のキーフレーム削減（誤差許容つき圧縮）で保存。曲線的な動きはごく僅かにナマる可能性がある | クリップサイズ優先。多くのケースはこれで十分 |
| **精度優先**（`High Precision` オン） | 削減を一切行わず、**サンプルした全フレームをそのままキー化**。サンプル点で厳密一致し、キー間は線形補間 | 高速回転・急なイージングなど、削減由来のナマりを消したいとき。クリップは大きくなる |

精度優先モードの実装ポイント:

- **Transform（位置／回転／スケール）を削減なしで手動キー化**します。サンプル点では厳密一致します。
- **回転はクォータニオンで記録**するため、Euler の 180° 跨ぎによる補間の乱れが起きません（高速回転に強い）。
- 補間は**線形**固定です。サンプルが密なので過補間による揺れ（オーバーシュート）が出ません。
- `Record All Properties` をオンにした場合、Transform 以外（BlendShape・マテリアル等）は `GameObjectRecorder` で採取して同じクリップにマージします。**この追加プロパティ分は従来どおり削減あり**です（Transform だけが厳密一致）。
- サンプル数ぶんのキーを書くため、`Frame Rate` × 対象 Transform 数に比例してクリップが肥大します。まず `Frame Rate` を必要十分に設定してからオンにするのが効率的です。

#### キー削減スライダー（High Precision Reduction）

クリップを軽くしたい場合は、精度優先の綺麗なカーブに対して**誤差上限つきの削減**をかけられます。`High Precision Reduction`（0〜0.05）で調整します。

- **相対トレランス方式**: 削減量は「各カーブの値域（振幅）に対する割合」です。例えば 0.01 なら、そのカーブの振れ幅の 1% を超えない範囲でキーを間引きます。位置・回転・スケールで単位が違っても、1つのスライダーで一貫して効きます。
- **誤差が保証されます**: 内部は RDP（Ramer–Douglas–Peucker）で、間引いた後も元サンプルからの最大逸脱が指定割合以内に収まります。軽量モードの不透明な削減と違い、**どこまでナマるかを自分で決められる**のが利点です。
- **回転は連動削減**: クォータニオン xyzw をまとめて間引くため、成分ごとに時刻がズレて軸外の揺れが出ることがありません。
- `0`（既定）で無削減＝全フレーム保持（従来の精度優先そのまま）。まず `0` で焼いて動きを確認し、必要に応じて少しずつ上げてサイズと精度のバランスを取るのが安全です。
- この削減が効くのは**手動キャプチャした Transform カーブのみ**です。`Record All Properties` の追加分（BlendShape・マテリアル等）は対象外です。

### Particle は「精度」ではなく原理的に焼けない

`GameObjectRecorder` は**シリアライズされたアニメーション可能プロパティ**しか記録できません。ParticleSystem の一粒一粒の位置は内部シミュレーション状態であり、キーフレーム化できないため、fps をいくら上げても**パーティクルの軌跡は AnimationClip に焼き込めません**。

ただし実運用では問題にならないことが多いです。

- **ParticleSystem 自体は VRChat 上でネイティブに動きます。** カスタム C# に依存しないので、そもそもベイク不要でそのまま再生されます。
- 焼く必要があるのは「カスタム PlayableTrack が ParticleSystem のパラメータ（emission rate、色など）を時間駆動している」ケースだけです。その場合、パラメータ自体はアニメーション可能プロパティなので `Record All Properties` をオンにすれば**値の時間変化は焼けます**（＝粒の位置ではなくモジュールの設定値が焼ける）。
- 粒の初速・寿命・シード等が焼けた設定で VRChat 側が再シミュレートするため、**乱数シードが違えば見た目は変わり得ます**（下記「決定論的な動きのみ」に該当）。

### 焼ける／焼けないの一覧

| 対象 | 焼ける? | 理由・備考 |
| --- | --- | --- |
| Transform（位置／回転／スケール） | ◎ | 常時記録。実質ロスレス（時間サンプリング依存のみ） |
| BlendShape／マテリアルの animatable プロパティ | ○ | `Record All Properties` オン時のみ。クリップは肥大する |
| ParticleSystem の粒の運動 | ✕ | シミュレーション状態でキー化不可（ネイティブ再生に任せる） |
| Rigidbody／物理挙動 | △ | ベイク時点の再生結果が固定焼き。再現ではなく「録画」になる |
| 乱数・プレイヤー入力依存 | △ | ベイク時点の 1 回の再生が固定化される（決定論なら実質 OK） |
| Audio | ✕ | AnimationClip で表現不可 |
| Udon 変数操作・イベント | ✕ | 同上 |
| SignalEmitter | △ | Worlds PoC: 明示 route のみ `SendCustomEvent` AnimationEvent へ変換可能。同期・途中再生等は非対応 |
| その他のカスタムロジック | ✕ | コードそのものは焼けない（結果の Transform 変化だけ焼ける） |

**まとめ:** 決定論的で Transform か animatable プロパティに落ちる動きであれば、`Frame Rate` を十分に取ることで目視で区別できないレベルまで精度を出せます。Particle・物理・Audio・Udon は精度の問題ではなく原理的に焼けないため、Particle のようにネイティブで動くものはベイクせず VRChat 側の再生に任せる構成が基本です。

## 運用上の注意

（「何が焼けるか」は前章「ベイクの精度と限界」を参照。ここでは使い方の注意をまとめます。）

- **手動ベイクの二重評価対策（Mute Playable Tracks After Bake）。** 手動ベイクでこのオプションがオンだと、ベイク後に元の PlayableTrack が自動でミュートされ、エディタプレビューでも `[Baked]` トラックだけが再生されます。オフにすると元の PlayableTrack と `[Baked]` の両方が動く（二重再生）ため、確認時はどちらかを手動でミュートしてください。手動ベイクではミュート状態がアセットに保存される点に注意してください（自動ベイクはクローン上での操作なので元アセットには残りません）。
- **元の PlayableTrack は削除されません。** VRChat 上では動作しないため無害ですが、Timeline 上には残り続けます。
- **Record Roots に Animator が自動追加されます。** AnimationTrack の駆動に必要なため、Animator を持たない Record Root には自動で追加されます（自動ベイク時はコピーシーン上の追加なので保存シーンには残りません）。既に別の Animator の配下にあるオブジェクトを Record Root に指定すると、ネストした Animator になって親のアニメーションが壊れる場合があります。その場合は Animator 階層が競合しない位置を Record Root に指定してください。
- **Git 差分について。** アップロード時の自動ベイクは非破壊なので差分は出ません。手動ベイク（プレビュー）は TimelineAsset 本体を書き換えるため差分が出ます。手動ベイクの結果はコミットせず、確認後に Undo/破棄することを推奨します。
- **自作 PlayableTrack が持つ TimelineAsset は、保存済みアセット（`.playable`）である必要があります。** 自動ベイクはアセットのクローンを作るため、どこにも保存されていないインラインの Timeline は非破壊ベイクできません（警告を出してスキップします）。

## トラブルシューティング

**ベイクしたのに VRChat 上で動かない**
`[Baked]` トラックがミュートされたままになっていないか、Record Roots に Animator が付いているか（自動追加されているはず）を確認してください。

**動きが元とズレる・カクつく**
Frame Rate を上げてみてください。また、Record Roots の指定が実際に動いているオブジェクトの親階層になっているか確認してください。

**アップロードでベイクされない／ビルドが中断される**
Console の `[PlayableTrackBaker]` で始まるログを確認してください。Record Roots が未設定の場合は警告を出してスキップされます（ビルドは中断しません）。非破壊ベイクは Unity の `IProcessSceneWithReport`（ビルド用コピーシーン）で走るため、実行されるのは Build & Publish 時だけで、Play モードでは走りません。もしアップロード時に一切ベイクされない場合は、この環境で `IProcessSceneWithReport` が発火しているか、`TimelineBakeMarker`（`IEditorOnly`）がベイク前に除去されていないかを確認してください（このツールはコールバック順を十分小さく設定して除去より先に走らせています）。

## ファイル構成

| ファイル | 役割 |
| --- | --- |
| `TimelineBakeMarker.cs` | ベイク対象を指定するマーカーコンポーネント（ランタイムアセンブリ、IEditorOnly） |
| `Editor/PlayableTrackBaker.cs` | エディタ専用。記録処理の共有コア（`PlayableTrackBakeCore`）、手動プレビュー用の破壊的ベイク（`PlayableTrackBaker`）、アップロード時の非破壊ベイク（`PlayableTrackBakeSceneProcessor : IProcessSceneWithReport`）、一時アセットの後始末（`PlayableTrackBakeTempCleanup`）を含む |
| `Editor/PlayableTrackPreview.cs` | エディタ専用。非破壊のゴースト比較プレビューウィンドウ（`PlayableTrackPreviewWindow : EditorWindow`）。シーン/アセットを書き換えずに元の動きとベイク結果を比較する |

## ライセンス

MIT License です。詳細はリポジトリルートの [LICENSE](LICENSE) を参照してください。

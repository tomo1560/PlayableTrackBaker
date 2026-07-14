# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-07-14

### Added

- Timeline の SignalEmitter を、VRChat Worlds で許可された `UdonBehaviour.SendCustomEvent` 呼び出しの AnimationEvent としてベイクする機能を追加。`SignalReceiver` の UnityEvent は移植せず、`TimelineBakeMarker` で明示した SignalAsset→Udon event 名の route だけを出力する（Worlds 専用。`bakeSignalEvents` / `signalEventHost` / `signalEventRoutes`）
- ベイク進捗と、AnimationClip の複雑さ・概算サイズを可視化するパフォーマンスレポートを追加（VRChat 固有の未検証な上限値には依存しない比較用の概算）
- 手動ベイク後に、ベイク済み clip と元 Timeline の位置誤差を全区間で検証するオプションを追加（`validatePrecisionAfterManualBake` / `precisionPositionWarningMeters`）

### Changed

- Signal route 型のため、Runtime アセンブリが `Unity.Timeline` へ依存するようになった

### Fixed

- ビルド時の非破壊ベイクで複製した Timeline に PlayableDirector のトラック binding が引き継がれず、AudioTrack が未バインドで再生される問題を修正。未バインドの Timeline 音声は AudioSource を介さず再生されるため VRChat の音量スライダー（World / Master）で音量調整できなくなっていた

## [1.1.0] - 2026-07-13

### Fixed

- 手動ベイク後に Timeline ウィンドウが即時更新されず、Undo で PlayableTrack の mute 状態が戻らない問題を修正
- 手動ベイク失敗時に、既存 AnimationClip、Timeline、binding、追加 Animator、新規アセットを元の状態へ戻すよう修正
- ツール生成クリップの専用署名で所有権を判定し、ユーザー作成の `[Baked]` トラックを削除しないよう修正
- ビルド一時フォルダ内のユーザー資産を削除せず、所有ラベル付き生成物だけを掃除するよう修正
- 非有限 Frame Rate と過大な総ベイク処理量を事前に拒否するよう修正
- VPM listing のメタデータ埋め込み、外部 URL、依存スクリプトの扱いを強化

### Changed

- GitHub Actions の外部参照をコミット SHA に固定し、カバレッジ 80% ゲートと再試行可能なドラフト Release 手順を追加

## [1.0.0] - 2026-07-12

### Fixed

- 同じ PlayableDirector に複数の `TimelineBakeMarker` がある場合、最後のマーカー以外の `[Baked]` トラックが消える問題を修正
- 別シーンや別 Timeline に同名の Director / Record Root がある場合、手動ベイクの `.anim` が互いに上書きされる問題を修正
- Timeline の長さがサンプリングフレーム境界と一致しない場合、軽量モードおよび追加プロパティの記録終端が Timeline 終端と一致しない問題を修正

### Changed

- リリース前にパッケージ構造、manifest、バージョン重複を検証するよう GitHub Actions を強化
- Unity EditMode テストを CI とリリースの必須ゲートに追加
- 非破壊ベイクの AssetDatabase 統合テストと、例外時の Timeline 状態復元を追加

### Added

- VRChat ワールドアップロード（Build & Publish）時の非破壊自動ベイク。`IProcessSceneWithReport` でビルド用コピーシーン上の TimelineAsset をクローンしてベイクするため、元のシーン・`.playable` アセットに変更が残らない
- 手動ベイク（破壊的・確認用）: Tools > Timeline > Bake All / Bake Selected、`TimelineBakeMarker` の右クリック Bake This、インスペクタの Bake This PlayableTrack ボタン
- ゴースト比較プレビュー（Tools > Timeline > Bake Preview (Ghost Compare)）: シーンやアセットを書き換えずに、元の PlayableTrack の動きとベイク結果を並べて比較。最大ローカル位置誤差の表示付き
- `TimelineBakeMarker` コンポーネント（IEditorOnly）でベイク対象の Director / Record Roots / Frame Rate 等を指定
- High Precision モード: キーフレーム削減なしで全サンプルをキー化（回転はクォータニオン記録・線形補間）
- High Precision Reduction スライダー: RDP（Ramer–Douglas–Peucker）による誤差上限つきキー削減（相対トレランス方式、回転はクォータニオン連動削減）
- Record All Properties オプション: Transform 以外の animatable プロパティ（BlendShape・マテリアル等）も記録
- Mute Playable Tracks After Bake オプション: ベイク後に元の PlayableTrack を自動ミュートして二重再生を防止
- ビルド失敗時の一時アセット自動クリーンアップ
- アバタープロジェクト対応: VPM 依存を `com.vrchat.base` にし、Worlds / Avatars 両プロジェクトで導入可能に。アバターでは手動ベイク＋ゴースト比較プレビューを利用できる（アップロード時の自動ベイクはワールド専用。生成した clip の Animator Controller への組み込みは手動）

[1.0.0]: https://github.com/tomo1560/PlayableTrackBaker/releases/tag/1.0.0
[1.1.0]: https://github.com/tomo1560/PlayableTrackBaker/releases/tag/1.1.0
[Unreleased]: https://github.com/tomo1560/PlayableTrackBaker/compare/1.1.0...HEAD

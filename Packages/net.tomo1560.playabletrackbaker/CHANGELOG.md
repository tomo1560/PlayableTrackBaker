# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- 同じ PlayableDirector に複数の `TimelineBakeMarker` がある場合、最後のマーカー以外の `[Baked]` トラックが消える問題を修正
- 別シーンや別 Timeline に同名の Director / Record Root がある場合、手動ベイクの `.anim` が互いに上書きされる問題を修正
- Timeline の長さがサンプリングフレーム境界と一致しない場合、軽量モードおよび追加プロパティの記録クリップが次フレームまで延びる問題を修正

### Changed

- リリース前にパッケージ構造、manifest、バージョン重複を検証するよう GitHub Actions を強化

## [1.0.0] - 2026-07-12

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
[Unreleased]: https://github.com/tomo1560/PlayableTrackBaker/compare/1.0.0...HEAD

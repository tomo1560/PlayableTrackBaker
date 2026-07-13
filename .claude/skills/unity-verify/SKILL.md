---
name: unity-verify
description: GAIAシーンのヘッドレス再生成とEditModeテスト実行の定型手順。VerificationProject配下のコード（UdonSharp/シーンビルダー/テスト）を変更したら必ずこれで検証する。
---

# Unity ヘッドレス検証（シーン再生成 + テスト）

VerificationProject のコードを変更したら、以下の2段階で検証する。UdonSharpのコンパイルは
シーン再生成が兼ねるため、**テストだけ回して済ませないこと**（UdonSharp変更時は必ず再生成から）。

## 前提チェック

- Unity Editor がプロジェクトを開いていないこと:
  `Get-Process Unity` が空、かつ `VerificationProject/Temp/UnityLockfile` が存在しない。
- Unity のパス: `C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe`

## 1. シーン再生成（UdonSharpコンパイル込み・約2〜3分）

```bash
"/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe" -batchmode -nographics \
  -projectPath "<repo>/VerificationProject" \
  -executeMethod GAIALyricsMovie.Editor.GAIALyricsMovieSceneBuilder.BuildFromCommandLine \
  -logFile <scratchpad>/gaia_regen.log
```

成功判定: ログに `Command-line generation completed` があること（`grep -c`）。

失敗時にログで見るもの:
- `Method is not exposed to Udon: '...'` — UdonSharpが使えないAPIを使用。udonsharp-authoring スキル参照。
- `InvalidOperationException: ...未コンパイル` — UdonSharpコンパイル失敗の後続症状。上の行に真因がある。
- **注意**: ビルダーは途中失敗すると旧シーン/Timelineを削除したまま終わる（git status で D 表示）。
  修正して再実行すれば復元されるので、慌てて checkout しないこと。

## 2. EditModeテスト（プレイモードテスト含む・約2〜4分）

```bash
"/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe" -batchmode \
  -projectPath "<repo>/VerificationProject" \
  -runTests -testPlatform EditMode -assemblyNames "GAIALyricsMovie.EditorTests" \
  -testResults <scratchpad>/gaia_tests.xml -logFile <scratchpad>/gaia_tests.log
```

結果判定: XMLの先頭付近 `passed="N" failed="0"` を確認。失敗時は
`<test-case ... result="Failed"` の `name` と `<message><![CDATA[...]]>` を見る。
プレイモードテストのスタックトレース行番号は±1〜3行ずれることがある。

どちらのコマンドも長時間かかるため `run_in_background: true` で実行し、完了通知を待つ。
2つを `&&` で連結して1回のバックグラウンド実行にしてよい。

## 検証後のコミット規約

このリポジトリは2コミットに分ける:
1. `feat:`/`fix:` — GAIALyricsMovie 配下の実変更（再生成されたシーン/Timeline含む）
2. `chore: absorb UdonSharp serialization churn from recompilation` —
   `VerificationProject/Assets/SerializedUdonPrograms` と ProjectSettings の変動吸収

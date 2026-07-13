---
name: udonsharp-authoring
description: このリポジトリでUdonSharpスクリプトを新規作成・変更するときの作法。Udon API公開確認、program asset生成、シーンビルダー配線、シーク/AnimationEventの実測セマンティクス、テストの罠。
---

# UdonSharp スクリプト作成の作法

## 言語・API制約

- UdonSharp-legal C# のみ: LINQ・try/catch・リフレクション・staticステート不可。
  プレーンなループ/配列/`GameObject[]` SerializeFieldは可。文字列補間は可。
- **APIがUdonに公開されているか必ず確認する**。実測で判明済み:
  - ❌ `TextMesh.text` setter（コンパイル時 `Method is not exposed to Udon` で失敗）
    → ラベルは静的表示にするか GameObject の SetActive 切り替えで代替
  - ✅ `PlayableDirector` の `time`/`Play()`/`Stop()`、`Networking.GetServerTimeInSeconds()`、
    `[UdonSynced] double`、`SendCustomEventDelayedSeconds/Frames`、`SetActive`
- 未確認APIの事前チェック: `VerificationProject/Packages/com.vrchat.worlds/Runtime/Udon/External/VRC.Udon.Wrapper.dll`
  のバイナリ文字列を検索（PowerShellで `[regex]::Matches` を使う。信頼度は中程度）。
  最終確認はシーン再生成でのUdonSharpコンパイル（unity-verify スキル参照）。

## 新規UdonSharpビヘイビアの追加手順

1. スクリプトは asmdef の外（Assembly-CSirp相当）に置く: `Assets/GAIALyricsMovie/Udon/` か `Signals/`。
   `.cs.meta` はリポジトリ規約でコミット対象（新規GUIDで作るかUnityに生成させる）。
2. UdonSharpProgramAsset はシーンビルダーで冪等生成する。参考実装:
   `GAIALyricsMovieSceneBuilder.EnsureShowControllerProgramAsset()` —
   `ScriptableObject.CreateInstance<UdonSharpProgramAsset>` + `sourceCsScript` 設定 +
   `AssetDatabase.CreateAsset` → 未コンパイルなら `UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions())`
   （`CompileAllCsPrograms` は非同期でバッチモードで不安定）→ コンパイル検証で失敗は例外。
3. シーン配線パターン（エディタコード）:
   ```csharp
   var proxy = UdonSharpUndo.AddComponent<MyBehaviour>(go);
   var so = new SerializedObject(proxy);
   so.FindProperty("field").objectReferenceValue = ...;
   so.ApplyModifiedPropertiesWithoutUndo();
   UdonSharpEditorUtility.CopyProxyToUdon(proxy);
   // Interact文言は backing UdonBehaviour 側:
   var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
   new SerializedObject(backing).FindProperty("interactText")...
   ```

## ネットワーク同期・シーク再生の実測セマンティクス（重要）

- 同期再生は `[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]` + `[UdonSynced] double 開始サーバー時刻` +
  `OnDeserialization` で途中参加者が自動追従（参考: `Udon/GAIAShowController.cs`）。
- **シークは必ず `Stop()` → `time = t` → `Play()`**。再生中に time を直接巻き戻すと
  Timelineがループ扱いで通過済みマーカー/AnimationEventを再発火させる。
- **再構築したグラフは最初の評価で [0, t] の全ベイク済みAnimationEventを時刻順に一括再生する**
  （t=0 なら何も発火しない）。演出を加算式に設計すれば見た目は自己修復するが、
  カウンタ系は二重加算されるため、シーク数フレーム後に `SendCustomEventDelayedFrames`
  で状態正規化（`ResyncToTime` 再実行）を入れること。
- 未ベイクのSignalEmitter（retroactive=false）はグラフ再構築後の再生開始で過去分を発火**しない**。
  ベイク済みAnimationEventと挙動が異なる点に注意。
- シークで飛ばしたイベントは発火しないため、経過秒から演出状態を復元する
  `ResyncToTime(float)` 相当のメソッドを受信側に用意する。

## テストの罠

- Play中のUdonSharpプロキシは `SerializedObject.FindProperty` で**配列プロパティを解決できない**
  （NullReference）。配列はリフレクション `GetType().GetField(..., NonPublic|Instance).GetValue()` で読む。
  スカラー（GameObject参照・int等）は SerializedObject で読める。
- Play中、ベイク済み `SendCustomEvent` AnimationEvent はUdonだけでなく**プロキシのC#メソッドも直接実行**
  するため、プロキシの `eventCount` 等がそのまま検証に使える。
- テストコードは Assembly-CSharp を直接参照できないので、型名文字列 + SerializedObject/リフレクションで扱う。

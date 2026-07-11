#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using PlayableTrackBaking;
using PlayableTrackBaking.Samples;

namespace PlayableTrackBaking.Tests
{
    /// <summary>
    /// PlayableTrackBaker の検証用シーンをワンクリックで生成するエディタツール。
    ///
    /// 生成物（軽量／精度優先の 2 種類。X 方向にずらして配置するので同時に置ける）:
    ///   - BakeTest_Target[_HP] : 自作 PlayableTrack が上下に動かすキューブ（= ベイク対象）
    ///   - BakeTest_Director[_HP]: PlayableDirector + TimelineBakeMarker
    ///   - BakeTest_Timeline[_HP].playable: PlayableTrack に MoveSample クリップを載せた Timeline
    ///
    /// 軽量版（High Precision オフ）と精度優先版（High Precision オン）を並べて生成し、
    /// それぞれベイクしてキー数・サンプル点一致・ズレの有無を比較できる。
    /// </summary>
    static class BakeTestSetup
    {
        const string RootName = "BakeTest_Root";
        const string GeneratedFolder = "Assets/PlayableTrackBakerTests/Generated";

        [MenuItem("Tools/Timeline/Create Bake Test Setup")]
        static void CreateSetup() => Build(false);

        [MenuItem("Tools/Timeline/Create Bake Test Setup (High Precision)")]
        static void CreateSetupHighPrecision() => Build(true);

        static void Build(bool highPrecision)
        {
            string suffix = highPrecision ? "_HP" : "";
            string rootName = RootName + suffix;
            string timelinePath = GeneratedFolder + "/BakeTest_Timeline" + suffix + ".playable";
            // 軽量版と並べて比較できるよう、精度優先版は X 方向にずらして配置する
            float xOffset = highPrecision ? 3f : 0f;

            // 同じ種類の既存生成物だけを掃除して作り直す（何度実行しても同じ状態になる）
            var existingRoot = GameObject.Find(rootName);
            if (existingRoot != null)
                Object.DestroyImmediate(existingRoot);

            EnsureFolder();
            AssetDatabase.DeleteAsset(timelinePath);

            // シーンオブジェクトを構築
            var root = new GameObject(rootName);
            root.transform.localPosition = new Vector3(xOffset, 0f, 0f);

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "BakeTest_Target" + suffix;
            target.transform.SetParent(root.transform, false);
            target.transform.localPosition = Vector3.zero;

            var directorGo = new GameObject("BakeTest_Director" + suffix);
            directorGo.transform.SetParent(root.transform, false);
            var director = directorGo.AddComponent<PlayableDirector>();

            // Timeline アセットを作成し、PlayableTrack にサンプルクリップを載せる
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, timelinePath);

            var track = timeline.CreateTrack<PlayableTrack>(null, "Move (Custom PlayableTrack)");
            var clip = track.CreateClip<MoveSamplePlayableAsset>();
            clip.start = 0;
            clip.duration = 4;
            clip.displayName = "Move";

            var asset = (MoveSamplePlayableAsset)clip.asset;
            asset.amplitude = 1.5f;
            asset.frequency = 0.5f; // 4 秒でちょうど 2 周期

            director.playableAsset = timeline;
            director.extrapolationMode = DirectorWrapMode.Hold;

            // ExposedReference で対象キューブを Timeline に紐付ける
            var exposedName = new PropertyName(GUID.Generate().ToString());
            var reference = asset.target;
            reference.exposedName = exposedName;
            asset.target = reference;
            director.SetReferenceValue(exposedName, target.transform);

            // ベイクマーカーを設定
            var marker = directorGo.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { target };
            marker.recordAllProperties = false;
            marker.frameRate = 60f;
            marker.highPrecision = highPrecision;
            marker.mutePlayableTracksAfterBake = true;

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();

            Selection.activeGameObject = directorGo;

            if (highPrecision)
            {
                Debug.Log(
                    "[BakeTestSetup] 精度優先（High Precision）検証セットアップを作成しました。\n" +
                    "1) Tools > Timeline > Bake All PlayableTracks を実行。\n" +
                    "2) Assets/BakedTimelineClips/ の生成 .anim を選び、Animation または Curves 表示でキーを確認。\n" +
                    "   - 精度優先: サンプルした全フレーム（4 秒 × 60fps ＝ 約 241 キー）が残り、各サンプル点を厳密に通る。\n" +
                    "   - 軽量版（Create Bake Test Setup で生成）と見比べると、軽量版はキーフレーム削減でキー数が少ない。\n" +
                    "3) [Baked] トラックだけで再生し、元の動きとズレないことを確認。",
                    directorGo);
            }
            else
            {
                Debug.Log(
                    "[BakeTestSetup] 軽量（High Precision オフ）検証セットアップを作成しました。\n" +
                    "1) Timeline ウィンドウで BakeTest_Director を選び、再生ヘッドを動かすとキューブが上下します（自作 PlayableTrack）。\n" +
                    "2) Tools > Timeline > Bake All PlayableTracks を実行。\n" +
                    "3) 実行後は元の PlayableTrack が自動ミュートされ、[Baked] トラックだけで同じ動きになれば成功です。\n" +
                    "精度優先版と比較したい場合は Tools > Timeline > Create Bake Test Setup (High Precision) も実行してください（X+3 に並びます）。",
                    directorGo);
            }
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PlayableTrackBakerTests"))
                AssetDatabase.CreateFolder("Assets", "PlayableTrackBakerTests");
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/PlayableTrackBakerTests", "Generated");
        }
    }
}
#endif

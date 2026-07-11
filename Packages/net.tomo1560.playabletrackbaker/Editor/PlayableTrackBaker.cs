#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace PlayableTrackBaking
{
    /// <summary>
    /// TimelineBakeMarker の PlayableTrack を評価し、AnimationClip にベイクして
    /// "[Baked]" プレフィックス付きの AnimationTrack として同じ Timeline に追加する共有ロジック。
    ///
    /// このクラス自体は VRChat SDK に依存しない（記録とトラック生成だけを担当し、
    /// クリップの保存先や破壊／非破壊の判断は呼び出し側に委ねる）。
    /// </summary>
    static class PlayableTrackBakeCore
    {
        public const string OutputFolder = "Assets/BakedTimelineClips";
        public const string BakedTrackPrefix = "[Baked]";

        public static bool HasPlayableTrack(TimelineAsset timeline)
            => timeline.GetOutputTracks().Any(t => t is PlayableTrack);

        /// <summary>
        /// director / timeline を 1 フレームずつ評価しながら recordRoots をスナップショットし、
        /// メモリ上の AnimationClip として返す（アセット保存は呼び出し側の責務）。
        ///
        /// marker.highPrecision に応じて記録方式を切り替える。
        ///  - false（軽量）: GameObjectRecorder の既定のキーフレーム削減で圧縮する。
        ///  - true（精度優先）: 削減を行わず、サンプルした全フレームをそのままキー化する。
        /// </summary>
        public static List<(AnimationClip clip, GameObject root)> Record(
            PlayableDirector director, TimelineAsset timeline, TimelineBakeMarker marker)
        {
            // ミュートされたトラックは評価されず記録できないので、記録前に必ずアンミュート
            foreach (var pt in timeline.GetOutputTracks().OfType<PlayableTrack>())
                pt.muted = false;

            float fps = Mathf.Max(1f, marker.frameRate);
            double duration = timeline.duration;
            int frames = Mathf.CeilToInt((float)duration * fps);

            return marker.highPrecision
                ? RecordHighPrecision(director, marker, fps, duration, frames)
                : RecordWithRecorder(director, marker, fps, duration, frames);
        }

        /// <summary>
        /// 軽量モード：GameObjectRecorder でスナップショットし、既定のキーフレーム削減つきで保存する。
        /// </summary>
        static List<(AnimationClip, GameObject)> RecordWithRecorder(
            PlayableDirector director, TimelineBakeMarker marker, float fps, double duration, int frames)
        {
            float dt = 1f / fps;

            // レコーダー準備（記録ルートごとに 1 つ）。未設定（null）のスロットは記録しない
            var recorders = new GameObjectRecorder[marker.recordRoots.Length];
            for (int i = 0; i < marker.recordRoots.Length; i++)
            {
                var root = marker.recordRoots[i];
                if (root == null)
                {
                    Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots[{i}] が未設定のためスキップします。", marker);
                    continue;
                }
                recorders[i] = new GameObjectRecorder(root);
                if (marker.recordAllProperties)
                    recorders[i].BindAll(root, true);
                else
                    recorders[i].BindComponentsOfType<Transform>(root, true);
            }

            // director.time は float ではなく double で算出し、長尺での累積誤差を防ぐ
            director.RebuildGraph();
            for (int f = 0; f <= frames; f++)
            {
                director.time = System.Math.Min(f / (double)fps, duration);
                director.Evaluate();
                foreach (var r in recorders)
                {
                    if (r != null)
                        r.TakeSnapshot(f == 0 ? 0f : dt);
                }
            }
            director.time = 0;
            director.Evaluate();

            var results = new List<(AnimationClip, GameObject)>();
            for (int i = 0; i < recorders.Length; i++)
            {
                if (recorders[i] == null)
                    continue;
                var clip = new AnimationClip { frameRate = fps };
                recorders[i].SaveToClip(clip, fps);
                recorders[i].ResetRecording();
                results.Add((clip, marker.recordRoots[i]));
            }
            return results;
        }

        /// <summary>
        /// 精度優先モード：Transform（位置/回転/スケール）を削減なしで手動キー化し、サンプル点で厳密一致させる。
        /// 回転はクォータニオンで記録し（Euler の 180° 跨ぎを回避）、補間は線形（サンプル過剰による揺れを防ぐ）。
        /// recordAllProperties が true の場合、Transform 以外のプロパティ（BlendShape・マテリアル等）は
        /// GameObjectRecorder で採取して同じクリップにマージする（これらは従来どおり削減あり）。
        /// </summary>
        static List<(AnimationClip, GameObject)> RecordHighPrecision(
            PlayableDirector director, TimelineBakeMarker marker, float fps, double duration, int frames)
        {
            float dt = 1f / fps;

            var captures = new List<RootCapture>();
            for (int i = 0; i < marker.recordRoots.Length; i++)
            {
                var root = marker.recordRoots[i];
                if (root == null)
                {
                    Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots[{i}] が未設定のためスキップします。", marker);
                    continue;
                }
                var rc = new RootCapture(root);
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    rc.AddTransform(tr, AnimationUtility.CalculateTransformPath(tr, root.transform));
                if (marker.recordAllProperties)
                {
                    rc.recorder = new GameObjectRecorder(root);
                    rc.recorder.BindAll(root, true);
                }
                captures.Add(rc);
            }

            director.RebuildGraph();
            double lastT = double.NegativeInfinity;
            for (int f = 0; f <= frames; f++)
            {
                double t = System.Math.Min(f / (double)fps, duration);
                director.time = t;
                director.Evaluate();

                // duration へクランプ後に同一時刻が連続しても、重複キーを打たないようガード
                bool newTime = t > lastT + 1e-9;
                foreach (var rc in captures)
                {
                    if (newTime)
                        rc.Capture((float)t);
                    if (rc.recorder != null)
                        rc.recorder.TakeSnapshot(f == 0 ? 0f : dt);
                }
                if (newTime)
                    lastT = t;
            }
            director.time = 0;
            director.Evaluate();

            var results = new List<(AnimationClip, GameObject)>();
            foreach (var rc in captures)
                results.Add((rc.BuildClip(fps, marker.highPrecisionReduction), rc.root));
            return results;
        }

        /// <summary>精度優先モードで 1 つの Record Root ぶんの Transform 曲線と追加プロパティを溜め込むバッファ。</summary>
        sealed class RootCapture
        {
            public readonly GameObject root;
            public GameObjectRecorder recorder; // recordAllProperties 時のみ（Transform 以外のマージ用）
            readonly List<TransformCurves> transforms = new List<TransformCurves>();

            public RootCapture(GameObject root) => this.root = root;

            public void AddTransform(Transform tr, string path) => transforms.Add(new TransformCurves(tr, path));

            public void Capture(float time)
            {
                foreach (var tc in transforms)
                    tc.Capture(time);
            }

            public AnimationClip BuildClip(float fps, float reduction)
            {
                var clip = new AnimationClip { frameRate = fps };
                foreach (var tc in transforms)
                    tc.Apply(clip, reduction);
                if (recorder != null)
                    MergeExtraProperties(clip, fps);
                clip.EnsureQuaternionContinuity(); // クォータニオン曲線の符号反転を連続化
                return clip;
            }

            // Transform 以外（BlendShape・マテリアル・有効/無効など）を GameObjectRecorder から取り込む。
            // Transform 系バインディングは手動キー化と重複するので除外する。
            void MergeExtraProperties(AnimationClip clip, float fps)
            {
                var tmp = new AnimationClip { frameRate = fps };
                recorder.SaveToClip(tmp, fps);
                recorder.ResetRecording();

                foreach (var b in AnimationUtility.GetCurveBindings(tmp))
                {
                    if (b.type == typeof(Transform))
                        continue;
                    AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(tmp, b));
                }
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(tmp))
                    AnimationUtility.SetObjectReferenceCurve(clip, b, AnimationUtility.GetObjectReferenceCurve(tmp, b));
            }
        }

        /// <summary>1 つの Transform について、位置 3 / 回転 4 / スケール 3 の削減なし曲線を組み立てる。</summary>
        sealed class TransformCurves
        {
            readonly Transform tr;
            readonly string path;
            readonly AnimationCurve px = new AnimationCurve(), py = new AnimationCurve(), pz = new AnimationCurve();
            readonly AnimationCurve rx = new AnimationCurve(), ry = new AnimationCurve(), rz = new AnimationCurve(), rw = new AnimationCurve();
            readonly AnimationCurve sx = new AnimationCurve(), sy = new AnimationCurve(), sz = new AnimationCurve();

            public TransformCurves(Transform tr, string path)
            {
                this.tr = tr;
                this.path = path;
            }

            public void Capture(float t)
            {
                var p = tr.localPosition;
                var q = tr.localRotation;
                var s = tr.localScale;
                px.AddKey(t, p.x); py.AddKey(t, p.y); pz.AddKey(t, p.z);
                rx.AddKey(t, q.x); ry.AddKey(t, q.y); rz.AddKey(t, q.z); rw.AddKey(t, q.w);
                sx.AddKey(t, s.x); sy.AddKey(t, s.y); sz.AddKey(t, s.z);
            }

            public void Apply(AnimationClip clip, float reduction)
            {
                if (reduction > 0f)
                {
                    // 位置・スケールはチャンネル独立で間引く
                    ReduceIndependent(px, reduction); ReduceIndependent(py, reduction); ReduceIndependent(pz, reduction);
                    ReduceIndependent(sx, reduction); ReduceIndependent(sy, reduction); ReduceIndependent(sz, reduction);
                    // 回転は xyzw を連動して間引く（成分ごとに時刻がズレて軸外の揺れが出るのを防ぐ）
                    ReduceCoupled(reduction, rx, ry, rz, rw);
                }

                SetCurve(clip, "m_LocalPosition.x", px); SetCurve(clip, "m_LocalPosition.y", py); SetCurve(clip, "m_LocalPosition.z", pz);
                SetCurve(clip, "m_LocalRotation.x", rx); SetCurve(clip, "m_LocalRotation.y", ry); SetCurve(clip, "m_LocalRotation.z", rz); SetCurve(clip, "m_LocalRotation.w", rw);
                SetCurve(clip, "m_LocalScale.x", sx); SetCurve(clip, "m_LocalScale.y", sy); SetCurve(clip, "m_LocalScale.z", sz);
            }

            // 1 チャンネルを RDP で単純化する。許容誤差 = reduction × そのカーブの値域。
            static void ReduceIndependent(AnimationCurve curve, float reduction)
            {
                var keys = curve.keys;
                if (keys.Length <= 2)
                    return;
                var keep = new bool[keys.Length];
                keep[0] = keep[keys.Length - 1] = true;
                Rdp(keys, 0, keys.Length - 1, reduction * ValueRange(keys), keep);
                curve.keys = Collect(keys, keep);
            }

            // 複数チャンネルを同一の残存キー集合で間引く（各成分の keep を OR で統合）。
            // OR はキーを増やす方向なので、どの成分も自分の許容誤差以内は保たれる。
            static void ReduceCoupled(float reduction, params AnimationCurve[] curves)
            {
                int n = curves[0].length;
                if (n <= 2)
                    return;
                var keep = new bool[n];
                keep[0] = keep[n - 1] = true;
                foreach (var c in curves)
                {
                    var keys = c.keys;
                    Rdp(keys, 0, n - 1, reduction * ValueRange(keys), keep);
                }
                foreach (var c in curves)
                    c.keys = Collect(c.keys, keep);
            }

            // Ramer–Douglas–Peucker: [lo,hi] を結ぶ直線から最も外れた点が eps を超える間、その点を残して分割する。
            static void Rdp(Keyframe[] pts, int lo, int hi, float eps, bool[] keep)
            {
                if (hi <= lo + 1)
                    return;
                float t0 = pts[lo].time, v0 = pts[lo].value;
                float span = pts[hi].time - t0;
                float dv = pts[hi].value - v0;
                float maxErr = 0f;
                int idx = -1;
                for (int i = lo + 1; i < hi; i++)
                {
                    float lineV = span > 0f ? v0 + dv * ((pts[i].time - t0) / span) : v0;
                    float e = Mathf.Abs(pts[i].value - lineV);
                    if (e > maxErr) { maxErr = e; idx = i; }
                }
                if (maxErr > eps && idx > lo)
                {
                    keep[idx] = true;
                    Rdp(pts, lo, idx, eps, keep);
                    Rdp(pts, idx, hi, eps, keep);
                }
            }

            static float ValueRange(Keyframe[] keys)
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var k in keys)
                {
                    if (k.value < min) min = k.value;
                    if (k.value > max) max = k.value;
                }
                return max - min;
            }

            static Keyframe[] Collect(Keyframe[] keys, bool[] keep)
            {
                var kept = new List<Keyframe>(keys.Length);
                for (int i = 0; i < keys.Length; i++)
                    if (keep[i])
                        kept.Add(keys[i]);
                return kept.ToArray();
            }

            void SetCurve(AnimationClip clip, string property, AnimationCurve curve)
            {
                MakeLinear(curve); // サンプル点を厳密に通す＝過補間による揺れを避けるため線形補間で固定
                clip.SetCurve(path, typeof(Transform), property, curve);
            }

            // AddKey で作ったキーはタンジェントが 0（フラット）になり、評価時に各キーで
            // イージング／オーバーシュートが出る。隣接キーを結ぶ直線の傾きをタンジェント値として
            // 明示的に書き込み、確実に線形補間にする（AnimationUtility のモード再計算に依存しない）。
            static void MakeLinear(AnimationCurve curve)
            {
                int n = curve.length;
                for (int i = 0; i < n; i++)
                {
                    var key = curve[i];
                    float inTangent = 0f, outTangent = 0f;
                    if (i > 0)
                    {
                        var prev = curve[i - 1];
                        float d = key.time - prev.time;
                        if (d > 0f)
                            inTangent = (key.value - prev.value) / d;
                    }
                    if (i < n - 1)
                    {
                        var next = curve[i + 1];
                        float d = next.time - key.time;
                        if (d > 0f)
                            outTangent = (next.value - key.value) / d;
                    }
                    if (i == 0) inTangent = outTangent;         // 端点は片側の傾きに合わせる
                    if (i == n - 1) outTangent = inTangent;
                    key.inTangent = inTangent;
                    key.outTangent = outTangent;
                    curve.MoveKey(i, key); // 時刻は変えないのでインデックスは安定
                }
            }
        }

        /// <summary>
        /// 既存の [Baked] トラックを削除してから、記録済みクリップごとに AnimationTrack を追加する。
        /// クリップは呼び出し側で永続化（ディスク保存 or サブアセット化）済みであること。
        /// </summary>
        public static void AddBakedTracks(
            PlayableDirector director, TimelineAsset timeline,
            List<(AnimationClip clip, GameObject root)> recorded, bool mutePlayableTracks)
        {
            // 既存の [Baked] トラックを削除（再ベイク時／クローン元由来の増殖防止）
            foreach (var t in timeline.GetOutputTracks().Where(t => t.name.StartsWith(BakedTrackPrefix)).ToArray())
                timeline.DeleteTrack(t);

            foreach (var (clip, root) in recorded)
            {
                // ルートに Animator が無ければ追加（AnimationTrack の駆動に必要）
                var animator = root.GetComponent<Animator>();
                if (animator == null)
                    animator = root.AddComponent<Animator>();

                var track = timeline.CreateTrack<AnimationTrack>(null, $"{BakedTrackPrefix} {root.name}");
                track.trackOffset = TrackOffset.ApplySceneOffsets;
                var tlClip = track.CreateClip(clip);
                tlClip.start = 0;
                tlClip.duration = clip.length;
                director.SetGenericBinding(track, animator);
            }

            if (mutePlayableTracks)
            {
                foreach (var pt in timeline.GetOutputTracks().OfType<PlayableTrack>())
                    pt.muted = true;
            }
        }

        public static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
                AssetDatabase.Refresh();
            }
        }

        public static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }

    /// <summary>
    /// 手動プレビュー用の「破壊的」ベイク。
    /// Tools > Timeline > Bake All PlayableTracks から実行し、開いているシーンの
    /// TimelineAsset を直接書き換えて [Baked] トラックを追加する（結果をエディタで目視確認できる）。
    ///
    /// アップロード時の自動ベイクは PlayableTrackBakeSceneProcessor（非破壊）が担当するため、
    /// このメニューはあくまで事前確認用。
    /// </summary>
    public static class PlayableTrackBaker
    {
        // ---- 実行の入口（対象の絞り込み方が違うだけで、実処理は BakeDestructive 共通）----

        [MenuItem("Tools/Timeline/Bake All PlayableTracks")]
        static void BakeAllMenu()
            => RunBakeWithDialog(Object.FindObjectsOfType<TimelineBakeMarker>(true), "シーン内すべて");

        [MenuItem("Tools/Timeline/Bake Selected PlayableTracks")]
        static void BakeSelectedMenu()
            => RunBakeWithDialog(SelectedMarkers(), "選択オブジェクト");

        // 選択に対象マーカーが無ければメニューをグレーアウト
        [MenuItem("Tools/Timeline/Bake Selected PlayableTracks", true)]
        static bool BakeSelectedValidate() => SelectedMarkers().Any();

        // コンポーネント右クリック → この 1 個だけベイク
        [MenuItem("CONTEXT/TimelineBakeMarker/Bake This")]
        static void BakeThisContext(MenuCommand cmd)
            => RunBakeWithDialog(new[] { (TimelineBakeMarker)cmd.context }, "このマーカー");

        // 選択オブジェクト（子孫含む）上の TimelineBakeMarker を重複なく集める
        static IEnumerable<TimelineBakeMarker> SelectedMarkers()
            => Selection.gameObjects
                .SelectMany(g => g.GetComponentsInChildren<TimelineBakeMarker>(true))
                .Distinct();

        static void RunBakeWithDialog(IEnumerable<TimelineBakeMarker> markers, string scopeLabel)
        {
            int count = RunBake(markers);
            EditorUtility.DisplayDialog("PlayableTrackBaker",
                count > 0
                    ? $"{count} 個の Timeline をベイクしました（{scopeLabel}）。"
                    : $"ベイク対象が見つかりませんでした（{scopeLabel}）。", "OK");
        }

        /// <summary>
        /// 渡されたマーカー群をベイクしてベイク成功数を返す（ダイアログは出さない）。
        /// Inspector のボタンなど、確認ダイアログを挟みたくない入口から使う。
        /// </summary>
        internal static int RunBake(IEnumerable<TimelineBakeMarker> markers)
        {
            int count = 0;
            foreach (var marker in markers)
            {
                if (marker != null && BakeDestructive(marker))
                    count++;
            }
            if (count > 0)
                AssetDatabase.SaveAssets();
            return count;
        }

        internal static bool BakeDestructive(TimelineBakeMarker marker)
        {
            var director = marker.director != null ? marker.director : marker.GetComponent<PlayableDirector>();
            if (director == null || !(director.playableAsset is TimelineAsset timeline))
                return false;
            if (!PlayableTrackBakeCore.HasPlayableTrack(timeline))
                return false;
            if (marker.recordRoots == null || marker.recordRoots.Length == 0)
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots が未設定のためスキップします。", marker);
                return false;
            }

            PlayableTrackBakeCore.EnsureFolder();

            // Record は PlayableTrack を unmute する副作用があるので muted 状態を退避（破壊的ベイクではアセットにそのまま保存されてしまうため）
            var muteSnapshot = timeline.GetOutputTracks().OfType<PlayableTrack>()
                .Select(pt => (track: pt, muted: pt.muted)).ToList();

            var recorded = PlayableTrackBakeCore.Record(director, timeline, marker);

            // クリップを固定パスに保存し、保存済みアセットへ差し替える（再ベイク時は上書き）。
            // ルート名衝突を避けるためインデックスをパスに含める。
            for (int i = 0; i < recorded.Count; i++)
            {
                var (clip, root) = recorded[i];
                string path = $"{PlayableTrackBakeCore.OutputFolder}/{PlayableTrackBakeCore.Sanitize(director.name)}_{PlayableTrackBakeCore.Sanitize(root.name)}_{i}_baked.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    recorded[i] = (existing, root);
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, path);
                }
            }

            PlayableTrackBakeCore.AddBakedTracks(director, timeline, recorded, marker.mutePlayableTracksAfterBake);

            // 全ミュートしない設定のときは、ユーザーが意図的にミュートしていたトラックを元へ戻す
            if (!marker.mutePlayableTracksAfterBake)
            {
                foreach (var (track, muted) in muteSnapshot)
                    if (track != null)
                        track.muted = muted;
            }

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            Debug.Log($"[PlayableTrackBaker] {director.name}: PlayableTrack をベイクしました（手動・破壊的）。", director);
            return true;
        }
    }

    /// <summary>
    /// TimelineBakeMarker のインスペクタにベイクボタンを追加する。
    /// 通常のフィールド表示に加えて、その場で対象マーカーだけをベイクできる。
    /// </summary>
    [CustomEditor(typeof(TimelineBakeMarker))]
    [CanEditMultipleObjects]
    public class TimelineBakeMarkerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            // 再生中は director.time を動かすため実行させない
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                int n = targets.Length;
                string label = n > 1 ? $"Bake These PlayableTracks ({n})" : "Bake This PlayableTrack";
                if (GUILayout.Button(label, GUILayout.Height(28)))
                {
                    int baked = PlayableTrackBaker.RunBake(targets.OfType<TimelineBakeMarker>());
                    Debug.Log($"[PlayableTrackBaker] Inspector から {baked} 個の Timeline をベイクしました。");
                }
            }

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("再生中はベイクできません。再生を停止してください。", MessageType.Info);
        }
    }

    /// <summary>
    /// アップロード時の「非破壊」ベイク。
    ///
    /// IProcessSceneWithReport はビルド用に複製された一時シーン上で呼ばれるため、
    /// シーンオブジェクトへの変更（Animator 追加・バインド）は保存済みシーンに影響しない。
    /// TimelineAsset は共有アセットなので、AssetDatabase.CopyAsset で一時フォルダにクローンし、
    /// クローンに対してベイクして Director に差し替える。元の .playable は一切変更されない。
    /// 生成した一時アセットはビルド後に PlayableTrackBakeTempCleanup が削除する。
    /// </summary>
    public class PlayableTrackBakeSceneProcessor : IProcessSceneWithReport
    {
        // VRChat の IEditorOnly コンポーネント除去より先に走らせたいので十分に小さい値にする
        public int callbackOrder => -10000;

        internal const string TempFolder = PlayableTrackBakeCore.OutputFolder + "/__ndbake_temp__";

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var markers = new List<TimelineBakeMarker>();
            foreach (var go in scene.GetRootGameObjects())
                markers.AddRange(go.GetComponentsInChildren<TimelineBakeMarker>(true));
            if (markers.Count == 0)
                return;

            EnsureTempFolder();

            int baked = 0;
            foreach (var marker in markers)
            {
                if (BakeNonDestructive(marker))
                    baked++;
            }
            if (baked > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[PlayableTrackBaker] {baked} 個の Timeline を非破壊ベイクしました（ビルド用コピー）。");
            }
        }

        static bool BakeNonDestructive(TimelineBakeMarker marker)
        {
            var director = marker.director != null ? marker.director : marker.GetComponent<PlayableDirector>();
            if (director == null || !(director.playableAsset is TimelineAsset src))
                return false;
            if (!PlayableTrackBakeCore.HasPlayableTrack(src))
                return false;
            if (marker.recordRoots == null || marker.recordRoots.Length == 0)
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots が未設定のためスキップします。", marker);
                return false;
            }

            string srcPath = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(srcPath))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: TimelineAsset が保存済みアセットではないため非破壊ベイクできません。", marker);
                return false;
            }

            // Instantiate ではトラックが元を参照したままになるため、CopyAsset で確実に深く複製する
            string clonePath = AssetDatabase.GenerateUniqueAssetPath($"{TempFolder}/{PlayableTrackBakeCore.Sanitize(director.name)}.playable");
            if (!AssetDatabase.CopyAsset(srcPath, clonePath))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: TimelineAsset のクローンに失敗しました（{srcPath}）。", marker);
                return false;
            }
            var clone = AssetDatabase.LoadAssetAtPath<TimelineAsset>(clonePath);
            director.playableAsset = clone;

            var recorded = PlayableTrackBakeCore.Record(director, clone, marker);

            // クリップはクローン Timeline のサブアセットとして持たせ、ビルドに含める
            foreach (var (clip, _) in recorded)
                AssetDatabase.AddObjectToAsset(clip, clone);

            PlayableTrackBakeCore.AddBakedTracks(director, clone, recorded, true);

            EditorUtility.SetDirty(clone);
            EditorUtility.SetDirty(director);
            return true;
        }

        static void EnsureTempFolder()
        {
            PlayableTrackBakeCore.EnsureFolder();
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder(PlayableTrackBakeCore.OutputFolder, "__ndbake_temp__");
        }
    }

    /// <summary>
    /// 非破壊ベイクが作った一時アセットの後始末。
    /// ビルド完了後に削除し、ビルドが途中で失敗した場合に備えてエディタ起動時にも掃除する。
    /// </summary>
    public class PlayableTrackBakeTempCleanup : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPostprocessBuild(BuildReport report) => Cleanup();

        [InitializeOnLoadMethod]
        static void SweepLeftoverOnLoad() => Cleanup();

        static void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(PlayableTrackBakeSceneProcessor.TempFolder))
            {
                AssetDatabase.DeleteAsset(PlayableTrackBakeSceneProcessor.TempFolder);
                AssetDatabase.Refresh();
            }
        }
    }
}
#endif

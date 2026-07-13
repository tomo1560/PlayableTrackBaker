#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace PlayableTrackBaking
{
    /// <summary>手動ベイクで完了した Timeline 数を通知する、UI 非依存の進捗値。</summary>
    internal readonly struct BakeProgress
    {
        public int CompletedCount { get; }
        public int TotalCount { get; }

        public BakeProgress(int completedCount, int totalCount)
        {
            CompletedCount = completedCount;
            TotalCount = totalCount;
        }
    }

    /// <summary>ユーザーが手動ベイクを中止したことを、既存のロールバック経路へ伝える内部例外。</summary>
    sealed class BakeCancelledException : System.OperationCanceledException { }

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
        internal const string OwnershipSignature = "PlayableTrackBaker.GeneratedClip.v1:";

        /// <summary>
        /// 1 回の Record で許容する総サンプル数の上限。
        /// 異常な frameRate × 長尺 Timeline の組み合わせでエディタがフリーズ／メモリ枯渇するのを防ぐ。
        /// </summary>
        internal const int MaxTotalFrames = 1_000_000;
        internal const long MaxWorkUnits = 10_000_000;

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
            => Record(director, timeline, marker, null);

        /// <summary>
        /// onSampleProgress は 0〜1 の進捗を受け取り、false を返すと記録を中止する。
        /// 中止時も既存の catch により Timeline の状態は復元される。
        /// </summary>
        internal static List<(AnimationClip clip, GameObject root)> Record(
            PlayableDirector director, TimelineAsset timeline, TimelineBakeMarker marker,
            System.Func<float, bool> onSampleProgress)
        {
            ValidateWorkload(timeline, new[] { marker });
            // Inspector には Range 属性があるが、古いシーンに保存済みの有限な異常値対策として同じ範囲へクランプする
            float fps = Mathf.Clamp(marker.frameRate, TimelineBakeMarker.MinFrameRate, TimelineBakeMarker.MaxFrameRate);
            double duration = timeline.duration;
            int frames = Mathf.CeilToInt((float)duration * fps);

            var muteSnapshot = timeline.GetOutputTracks().OfType<PlayableTrack>()
                .Select(track => (track, track.muted)).ToList();
            double originalTime = director != null ? director.time : 0.0;

            // ミュートされたトラックは評価されず記録できないので、記録前に必ずアンミュート
            foreach (var pt in timeline.GetOutputTracks().OfType<PlayableTrack>())
                pt.muted = false;

            try
            {
                return marker.highPrecision
                    ? RecordHighPrecision(director, marker, fps, duration, frames, onSampleProgress)
                    : RecordWithRecorder(director, marker, fps, duration, frames, onSampleProgress);
            }
            catch
            {
                // 呼び出し先が例外を処理して編集を続けても、Timeline の状態を汚さない。
                foreach (var (track, muted) in muteSnapshot)
                    if (track != null)
                        track.muted = muted;
                if (director != null)
                {
                    director.time = originalTime;
                    try { director.Evaluate(); }
                    catch { /* 元の例外を優先する */ }
                }
                throw;
            }
        }

        internal static void ValidateWorkload(TimelineAsset timeline, IEnumerable<TimelineBakeMarker> markers)
        {
            if (timeline == null || double.IsNaN(timeline.duration) || double.IsInfinity(timeline.duration) || timeline.duration < 0)
                throw new System.InvalidOperationException("[PlayableTrackBaker] Timeline の長さが有限の非負値ではありません。");

            long total = 0;
            foreach (var marker in markers.Where(x => x != null))
            {
                if (float.IsNaN(marker.frameRate) || float.IsInfinity(marker.frameRate))
                    throw new System.InvalidOperationException($"[PlayableTrackBaker] {marker.name}: Frame Rate は有限値である必要があります。");
                float fps = Mathf.Clamp(marker.frameRate, TimelineBakeMarker.MinFrameRate, TimelineBakeMarker.MaxFrameRate);
                double rawFrames = System.Math.Ceiling(timeline.duration * fps);
                if (rawFrames > MaxTotalFrames)
                    throw new System.InvalidOperationException($"[PlayableTrackBaker] {marker.name}: 総フレーム数が上限 {MaxTotalFrames} を超えています。");

                long rootWeight = 0;
                foreach (var root in marker.recordRoots ?? System.Array.Empty<GameObject>())
                {
                    if (root == null) continue;
                    int transforms = root.GetComponentsInChildren<Transform>(true).Length;
                    long weight = System.Math.Max(1, transforms);
                    if (marker.recordAllProperties)
                    {
                        // BindAll の正確なカーブ数は記録後まで分からないため、Component と BlendShape 数を
                        // 境界前の保守的な代理値として加える。
                        weight += root.GetComponentsInChildren<Component>(true).Length * 4L;
                        weight += root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                            .Sum(renderer => renderer.sharedMesh != null ? (long)renderer.sharedMesh.blendShapeCount : 0L);
                    }
                    rootWeight += weight;
                }
                try { total = checked(total + checked(((long)rawFrames + 1L) * rootWeight)); }
                catch (System.OverflowException) { total = long.MaxValue; }
                if (total > MaxWorkUnits)
                    throw new System.InvalidOperationException($"[PlayableTrackBaker] 推定処理量 {total} が上限 {MaxWorkUnits} を超えています。マーカー、記録ルート、階層、FPS または Timeline 長を減らしてください。");
            }
        }

        /// <summary>
        /// 軽量モード：GameObjectRecorder でスナップショットし、既定のキーフレーム削減つきで保存する。
        /// </summary>
        static List<(AnimationClip, GameObject)> RecordWithRecorder(
            PlayableDirector director, TimelineBakeMarker marker, float fps, double duration, int frames,
            System.Func<float, bool> onSampleProgress)
        {
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
            double previousTime = 0.0;
            for (int f = 0; f <= frames; f++)
            {
                ThrowIfCancelled(onSampleProgress, f, frames);
                double sampleTime = System.Math.Min(f / (double)fps, duration);
                director.time = sampleTime;
                director.Evaluate();
                foreach (var r in recorders)
                {
                    if (r != null)
                        r.TakeSnapshot(f == 0 ? 0f : (float)(sampleTime - previousTime));
                }
                previousTime = sampleTime;
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
                NormalizeRecorderEndTime(clip, (float)duration);
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
            PlayableDirector director, TimelineBakeMarker marker, float fps, double duration, int frames,
            System.Func<float, bool> onSampleProgress)
        {
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
            double previousTime = 0.0;
            for (int f = 0; f <= frames; f++)
            {
                ThrowIfCancelled(onSampleProgress, f, frames);
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
                        rc.recorder.TakeSnapshot(f == 0 ? 0f : (float)(t - previousTime));
                }
                if (newTime)
                    lastT = t;
                previousTime = t;
            }
            director.time = 0;
            director.Evaluate();

            var results = new List<(AnimationClip, GameObject)>();
            foreach (var rc in captures)
                results.Add((rc.BuildClip(fps, marker.highPrecisionReduction, (float)duration), rc.root));
            return results;
        }

        static void ThrowIfCancelled(System.Func<float, bool> onSampleProgress, int frame, int totalFrames)
        {
            // UI の再描画コストを抑えつつ、開始直後・終端・約16フレームごとには確実にキャンセルを確認する。
            if (onSampleProgress == null || (frame != 0 && frame != totalFrames && frame % 16 != 0))
                return;
            float progress = totalFrames == 0 ? 1f : (float)frame / totalFrames;
            if (!onSampleProgress(progress))
                throw new BakeCancelledException();
        }

        // GameObjectRecorder は最後のスナップショット値を、直前の内部記録時刻にキー化する。
        // 最後の値は Timeline 終端で評価した値なので、各カーブの終端キーを正しい duration へ合わせる。
        static void NormalizeRecorderEndTime(AnimationClip clip, float duration)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    continue;
                var last = curve[curve.length - 1];
                last.time = duration;
                curve.MoveKey(curve.length - 1, last);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0)
                    continue;
                keys[keys.Length - 1].time = duration;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }
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

            public AnimationClip BuildClip(float fps, float reduction, float duration)
            {
                var clip = new AnimationClip { frameRate = fps };
                foreach (var tc in transforms)
                    tc.Apply(clip, reduction);
                if (recorder != null)
                    MergeExtraProperties(clip, fps, duration);
                clip.EnsureQuaternionContinuity(); // クォータニオン曲線の符号反転を連続化
                return clip;
            }

            // Transform 以外（BlendShape・マテリアル・有効/無効など）を GameObjectRecorder から取り込む。
            // Transform 系バインディングは手動キー化と重複するので除外する。
            void MergeExtraProperties(AnimationClip clip, float fps, float duration)
            {
                var tmp = new AnimationClip { frameRate = fps };
                recorder.SaveToClip(tmp, fps);
                recorder.ResetRecording();
                NormalizeRecorderEndTime(tmp, duration);

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
        /// VRChat 上でも再生できる組み込み AnimationTrack を保ったまま、参照クリップへ
        /// 付与した専用署名で本ツールの生成物を判定する。表示名や保存先だけでは判定しない。
        /// 旧版トラックは誤削除を避けるため自動移行しない。
        /// </summary>
        internal static bool IsBakedTrackOwnedByTool(TrackAsset track)
        {
            if (!(track is AnimationTrack))
                return false;
            return track.GetClips().Any(clip =>
                clip.asset is AnimationPlayableAsset animationAsset &&
                (animationAsset.name.StartsWith(OwnershipSignature, System.StringComparison.Ordinal) ||
                 (animationAsset.clip != null && animationAsset.clip.name.StartsWith(
                     OwnershipSignature, System.StringComparison.Ordinal))));
        }

        /// <summary>
        /// 既存の [Baked] トラックを削除してから、記録済みクリップごとに AnimationTrack を追加する。
        /// クリップは呼び出し側で永続化（ディスク保存 or サブアセット化）済みであること。
        /// </summary>
        public static void AddBakedTracks(
            PlayableDirector director, TimelineAsset timeline,
            List<(AnimationClip clip, GameObject root)> recorded, bool mutePlayableTracks,
            bool registerUndo = false)
        {
            // 既存の [Baked] トラックを削除（再ベイク時／クローン元由来の増殖防止）。
            // ユーザーが自分で "[Baked]..." と命名したトラックは所有証拠が無いため削除せずスキップする
            var existingTracks = timeline.GetOutputTracks().ToArray();
            // DeleteTrack 後の TrackAsset は MissingReference になるため、警告判定は削除前に済ませる。
            foreach (var t in existingTracks.Where(t =>
                t.name.StartsWith(BakedTrackPrefix, System.StringComparison.Ordinal) &&
                !IsBakedTrackOwnedByTool(t)))
            {
                Debug.LogWarning(
                    $"[PlayableTrackBaker] トラック \"{t.name}\" は所有署名がないため削除しません。" +
                    "旧バージョンの生成物であれば手動で削除してください。", timeline);
            }
            foreach (var t in existingTracks.Where(IsBakedTrackOwnedByTool))
            {
                if (registerUndo)
                {
                    Undo.RegisterCompleteObjectUndo(timeline, "Replace Baked Track");
                    Undo.RegisterCompleteObjectUndo(t, "Replace Baked Track");
                }
                timeline.DeleteTrack(t);
            }

            foreach (var (clip, root) in recorded)
            {
                // ルートに Animator が無ければ追加（AnimationTrack の駆動に必要）
                var animator = root.GetComponent<Animator>();
                if (animator == null)
                    animator = registerUndo
                        ? Undo.AddComponent<Animator>(root)
                        : root.AddComponent<Animator>();

                var track = timeline.CreateTrack<AnimationTrack>(null, $"{BakedTrackPrefix} {root.name}");
                if (registerUndo)
                    Undo.RegisterCreatedObjectUndo(track, "Create Baked Track");
                track.trackOffset = TrackOffset.ApplySceneOffsets;
                var tlClip = track.CreateClip(clip);
                // clip 自体の名前を変えると、永続 AnimationClip の main object 名とファイル名が
                // 不一致になり Unity が警告する。Timeline 内の AnimationPlayableAsset に署名を置く。
                if (tlClip.asset is AnimationPlayableAsset animationAsset)
                    animationAsset.name = OwnershipSignature + root.name;
                // 表示名はユーザーが Timeline 上で識別しやすい値を保つ。
                tlClip.displayName = $"{BakedTrackPrefix} {root.name}";
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

        /// <summary>
        /// Signal PoC を有効化した 1 つの marker について、指定 event host の生成 clip にだけ
        /// UdonBehaviour.SendCustomEvent 用 AnimationEvent を加える。
        /// 複数 marker の設定を暗黙に混在させると宛先が曖昧になるため、同一 Director では 1 件に限定する。
        /// </summary>
        internal static int AddConfiguredSignalEvents(
            TimelineAsset timeline,
            List<(AnimationClip clip, GameObject root)> recorded,
            IEnumerable<TimelineBakeMarker> markers,
            TimelineAsset routeSourceTimeline = null)
        {
            var enabledMarkers = markers
                .Where(marker => marker != null && marker.bakeSignalEvents)
                .ToList();
            if (enabledMarkers.Count == 0)
                return 0;
            if (enabledMarkers.Count > 1)
                throw new System.InvalidOperationException(
                    "[PlayableTrackBaker] 同じ PlayableDirector では Signal Event Bake を有効にできる TimelineBakeMarker は 1 つだけです。");

            var marker = enabledMarkers[0];
            if (marker.signalEventHost == null)
                throw new System.InvalidOperationException(
                    $"[PlayableTrackBaker] {marker.name}: Signal Event Host を Record Roots のいずれかに指定してください。");

            var eventHostClips = recorded
                .Where(result => result.root == marker.signalEventHost)
                .Select(result => result.clip)
                .Where(clip => clip != null)
                .Distinct()
                .ToList();
            if (eventHostClips.Count != 1)
                throw new System.InvalidOperationException(
                    $"[PlayableTrackBaker] {marker.name}: Signal Event Host は Record Roots に一度だけ含める必要があります。");

            int count = TimelineSignalAnimationEventBaker.AddRoutedSignalEvents(
                timeline, eventHostClips[0], marker.signalEventRoutes, routeSourceTimeline);
            if (count > 0)
                EnsureEventHostTrackCoversTimeline(timeline, eventHostClips[0]);
            return count;
        }

        static void EnsureEventHostTrackCoversTimeline(TimelineAsset timeline, AnimationClip eventHost)
        {
            foreach (var track in timeline.GetOutputTracks().OfType<AnimationTrack>())
            {
                foreach (var timelineClip in track.GetClips())
                {
                    if (timelineClip.asset is AnimationPlayableAsset asset && asset.clip == eventHost)
                        timelineClip.duration = System.Math.Max(timelineClip.duration, timeline.duration);
                }
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

        /// <summary>
        /// 同名の Director / Record Root が別シーンや別 Timeline に存在しても衝突しない、
        /// 手動ベイク用 AnimationClip の安定した保存パスを返す。
        /// </summary>
        internal static string BuildClipAssetPath(
            PlayableDirector director, TimelineAsset timeline, GameObject root, int resultIndex)
        {
            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            string timelineId = string.IsNullOrEmpty(timelinePath)
                ? timeline.GetInstanceID().ToString()
                : AssetDatabase.AssetPathToGUID(timelinePath);
            string directorId = GlobalObjectId.GetGlobalObjectIdSlow(director).ToString();
            string rootId = GlobalObjectId.GetGlobalObjectIdSlow(root).ToString();
            string stableId = Hash128.Compute($"{timelineId}|{directorId}|{rootId}|{resultIndex}").ToString();
            return $"{OutputFolder}/{Sanitize(director.name)}_{Sanitize(root.name)}_{stableId}_baked.anim";
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
        // EditMode テストが、永続化と Timeline 更新の間の例外を再現するためのフック。
        internal static System.Action BakeDestructiveFailureInjection;
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

        internal static void RunBakeWithDialog(IEnumerable<TimelineBakeMarker> markers, string scopeLabel)
        {
            bool cancelled = false;
            int count;
            try
            {
                count = RunBake(
                    markers,
                    progress =>
                    {
                        bool keepGoing = ShowBakeProgress(progress.CompletedCount, progress.TotalCount, 0f);
                        cancelled |= !keepGoing;
                        return keepGoing;
                    },
                    (completed, total, sample) =>
                    {
                        bool keepGoing = ShowBakeProgress(completed, total, sample);
                        cancelled |= !keepGoing;
                        return keepGoing;
                    });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EditorUtility.DisplayDialog("PlayableTrackBaker",
                cancelled
                    ? $"ベイクをキャンセルしました。完了済み {count} 個の Timeline は保持されています。"
                    : count > 0
                    ? $"{count} 個の Timeline をベイクしました（{scopeLabel}）。"
                    : $"ベイク対象が見つかりませんでした（{scopeLabel}）。", "OK");
        }

        static bool ShowBakeProgress(int completed, int total, float currentTimelineProgress)
        {
            float ratio = total == 0 ? 1f : (completed + Mathf.Clamp01(currentTimelineProgress)) / total;
            return !EditorUtility.DisplayCancelableProgressBar(
                "PlayableTrackBaker",
                $"{completed} / {total} Timeline をベイク中",
                Mathf.Clamp01(ratio));
        }

        /// <summary>
        /// 渡されたマーカー群をベイクしてベイク成功数を返す（ダイアログは出さない）。
        /// Inspector のボタンなど、確認ダイアログを挟みたくない入口から使う。
        /// </summary>
        internal static int RunBake(IEnumerable<TimelineBakeMarker> markers)
            => RunBake(markers, null);

        /// <summary>
        /// 手動ベイクを実行する。進捗コールバックが false を返した場合、完了済みの結果を保ったまま
        /// 次の Timeline を開始せずに終了する。UI を持たないため EditMode テストからも検証可能。
        /// </summary>
        internal static int RunBake(IEnumerable<TimelineBakeMarker> markers, System.Func<BakeProgress, bool> onProgress)
            => RunBake(markers, onProgress, null);

        static int RunBake(
            IEnumerable<TimelineBakeMarker> markers,
            System.Func<BakeProgress, bool> onProgress,
            System.Func<int, int, float, bool> onSampleProgress)
        {
            int count = 0;
            var groups = markers
                .Where(marker => marker != null)
                .Select(marker => (marker, director: ResolveDirector(marker)))
                .Where(x => x.director != null)
                .GroupBy(x => x.director)
                .ToList();

            int completedTimelines = 0;
            foreach (var group in groups)
            {
                var groupMarkers = group.Select(x => x.marker).ToList();
                bool baked;
                try
                {
                    baked = BakeDestructive(group.Key, groupMarkers,
                        onSampleProgress == null ? null : sample =>
                            onSampleProgress(completedTimelines, groups.Count, sample));
                }
                catch (BakeCancelledException)
                {
                    break;
                }
                completedTimelines++;
                if (baked)
                {
                    // 1 group は 1 PlayableDirector（= 1 Timeline）なので、marker 数ではなく
                    // 実際に完了した Timeline 数を返す。
                    count++;
                }
                // ベイク対象外だった group も処理済みとして進捗を進め、正常終了時は必ず 100% にする。
                if (onProgress != null && !onProgress(new BakeProgress(completedTimelines, groups.Count)))
                    break;
            }
            if (count > 0)
                AssetDatabase.SaveAssets();
            return count;
        }

        internal static bool BakeDestructive(TimelineBakeMarker marker)
        {
            var director = ResolveDirector(marker);
            return director != null && BakeDestructive(director, new[] { marker });
        }

        static PlayableDirector ResolveDirector(TimelineBakeMarker marker)
            => marker.director != null ? marker.director : marker.GetComponent<PlayableDirector>();

        static bool BakeDestructive(
            PlayableDirector director, IReadOnlyList<TimelineBakeMarker> markers,
            System.Func<float, bool> onSampleProgress = null)
        {
            if (director == null || !(director.playableAsset is TimelineAsset timeline))
                return false;
            if (!PlayableTrackBakeCore.HasPlayableTrack(timeline))
                return false;

            var validMarkers = markers.Where(marker => marker != null && marker.recordRoots != null && marker.recordRoots.Length > 0).ToList();
            foreach (var marker in markers.Except(validMarkers))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots が未設定のためスキップします。", marker);
            }
            if (validMarkers.Count == 0)
                return false;

            PlayableTrackBakeCore.ValidateWorkload(timeline, validMarkers);

            PlayableTrackBakeCore.EnsureFolder();

            // Record は PlayableTrack を unmute する副作用があるので muted 状態を退避（破壊的ベイクではアセットにそのまま保存されてしまうため）
            var muteSnapshot = timeline.GetOutputTracks().OfType<PlayableTrack>()
                .Select(pt => (track: pt, muted: pt.muted)).ToList();

            bool mutePlayableTracks = validMarkers.All(marker => marker.mutePlayableTracksAfterBake);
            var createdAssetPaths = new List<string>(); // 例外時ロールバック用（このベイクで新規作成した .anim のみ追跡）
            var overwrittenAssets = new List<(AnimationClip asset, AnimationClip snapshot)>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            // Timeline 1.7.x は "Timeline" で始まる Undo 名を検知してウィンドウを再構築する。
            Undo.SetCurrentGroupName("Timeline Bake Playable Tracks");
            Undo.RegisterCompleteObjectUndo(timeline, "Bake Playable Tracks");
            Undo.RegisterCompleteObjectUndo(director, "Bake Playable Tracks");
            // muted は TimelineAsset ではなく各 TrackAsset に直列化されるため、
            // Record が一時的に unmute する前の状態を TrackAsset 自身へ登録する。
            foreach (var (track, _) in muteSnapshot)
                if (track != null)
                    Undo.RegisterCompleteObjectUndo(track, "Bake Playable Tracks");
            bool succeeded = false;
            try
            {
                var recorded = new List<(AnimationClip clip, GameObject root)>();
                for (int markerIndex = 0; markerIndex < validMarkers.Count; markerIndex++)
                {
                    int currentMarkerIndex = markerIndex;
                    System.Func<float, bool> markerProgress = onSampleProgress == null
                        ? null
                        : sample => onSampleProgress((currentMarkerIndex + sample) / validMarkers.Count);
                    var marker = validMarkers[markerIndex];
                    var markerRecorded = PlayableTrackBakeCore.Record(
                        director, timeline, marker, markerProgress);
                    recorded.AddRange(markerRecorded);
                    ValidateManualBakePrecision(director, timeline, marker, markerRecorded);
                }

                // クリップを固定パスに保存し、保存済みアセットへ差し替える（再ベイク時は上書き）。
                // ルート名衝突を避けるためインデックスをパスに含める。
                for (int i = 0; i < recorded.Count; i++)
                {
                    var (clip, root) = recorded[i];
                    string path = PlayableTrackBakeCore.BuildClipAssetPath(director, timeline, root, i);
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existing != null)
                    {
                        overwrittenAssets.Add((existing, Object.Instantiate(existing)));
                        Undo.RegisterCompleteObjectUndo(existing, "Bake Animation Clip");
                        EditorUtility.CopySerialized(clip, existing);
                        recorded[i] = (existing, root);
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(clip, path);
                        Undo.RegisterCreatedObjectUndo(clip, "Create Baked Animation Clip");
                        createdAssetPaths.Add(path);
                    }
                }

                PlayableTrackBakeCore.AddBakedTracks(director, timeline, recorded, mutePlayableTracks, true);
                int signalEvents = PlayableTrackBakeCore.AddConfiguredSignalEvents(
                    timeline, recorded, validMarkers);
                if (recorded.Count > 0)
                {
                    var reports = recorded.Select(x => AnimationClipPerformanceAnalyzer.Analyze(x.clip));
                    long keys = reports.Sum(report => (long)report.TotalKeyCount);
                    long bytes = reports.Sum(report => report.EstimatedSizeBytes);
                    Debug.Log($"[PlayableTrackBaker] {director.name}: ベイク結果 {recorded.Count} clip、" +
                        $"推定キー数 {keys:N0}、推定サイズ {bytes:N0} B、Signal Event {signalEvents} 件", director);
                }
                BakeDestructiveFailureInjection?.Invoke();
                succeeded = true;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                foreach (var (asset, snapshot) in overwrittenAssets)
                {
                    if (asset != null && snapshot != null)
                        EditorUtility.CopySerialized(snapshot, asset);
                }
                foreach (var path in createdAssetPaths)
                    AssetDatabase.DeleteAsset(path);
                throw;
            }
            finally
            {
                foreach (var (_, snapshot) in overwrittenAssets)
                    if (snapshot != null)
                        Object.DestroyImmediate(snapshot);
                // 失敗時は必ず元の muted 状態へ戻す（Record / AddBakedTracks の途中で例外が出ても失われないように）。
                // 成功時は全ミュートしない設定のときだけ、ユーザーが意図的にミュートしていたトラックを元へ戻す
                if (!succeeded || !mutePlayableTracks)
                {
                    foreach (var (track, muted) in muteSnapshot)
                        if (track != null)
                            track.muted = muted;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            // 非破壊 build は AddBakedTracks を直接呼ぶため、Editor UI 更新は手動 Bake に限定する。
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            Debug.Log($"[PlayableTrackBaker] {director.name}: PlayableTrack をベイクしました（手動・破壊的）。", director);
            return true;
        }

        static void ValidateManualBakePrecision(
            PlayableDirector director,
            TimelineAsset timeline,
            TimelineBakeMarker marker,
            IReadOnlyList<(AnimationClip clip, GameObject root)> recorded)
        {
            if (!marker.validatePrecisionAfterManualBake)
                return;

            // 再ベイク時は、まだ削除されていない旧 [Baked] Track を元 Timeline の評価から除外する。
            // これをしないと旧 clip と PlayableTrack が合成され、今回の clip との比較にならない。
            var bakedTrackMuteSnapshot = timeline.GetOutputTracks()
                .Where(PlayableTrackBakeCore.IsBakedTrackOwnedByTool)
                .Select(track => (track, muted: track.muted))
                .ToList();
            try
            {
                foreach (var (track, _) in bakedTrackMuteSnapshot)
                    track.muted = true;
                if (bakedTrackMuteSnapshot.Count > 0)
                    director.RebuildGraph();

                foreach (var (clip, root) in recorded)
                {
                    try
                    {
                        var report = TimelineBakePrecisionValidationRunner.ValidatePosition(
                            director, timeline, clip, root, marker.frameRate);
                        string message = $"[PlayableTrackBaker] {marker.name}: {root.name} の位置精度 " +
                            $"最大 {report.MaxPositionError:F6} m (t={report.MaxPositionErrorTime:F3}s, " +
                            $"{report.SampleCount} samples)";
                        if (report.MaxPositionError > marker.precisionPositionWarningMeters)
                            Debug.LogWarning(message + $"。閾値 {marker.precisionPositionWarningMeters:F6} m を超えています。frameRate を上げるか High Precision を有効にしてください。", marker);
                        else
                            Debug.Log(message, marker);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: {root.name} の精度検証をスキップしました。{exception.Message}", marker);
                    }
                }
            }
            finally
            {
                foreach (var (track, muted) in bakedTrackMuteSnapshot)
                    if (track != null)
                        track.muted = muted;
                if (bakedTrackMuteSnapshot.Count > 0)
                    director.RebuildGraph();
            }
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
                    PlayableTrackBaker.RunBakeWithDialog(
                        targets.OfType<TimelineBakeMarker>(), n > 1 ? "Inspector 選択" : "Inspector");
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
            var groups = markers
                .Select(marker => (marker, director: ResolveDirector(marker)))
                .Where(x => x.director != null)
                .GroupBy(x => x.director);
            foreach (var group in groups)
            {
                var groupMarkers = group.Select(x => x.marker).ToList();
                if (BakeNonDestructive(group.Key, groupMarkers))
                    baked += groupMarkers.Count;
            }
            if (baked > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[PlayableTrackBaker] {baked} 個の Timeline を非破壊ベイクしました（ビルド用コピー）。");
            }
        }

        static PlayableDirector ResolveDirector(TimelineBakeMarker marker)
            => marker.director != null ? marker.director : marker.GetComponent<PlayableDirector>();

        internal static bool BakeNonDestructive(
            PlayableDirector director, IReadOnlyList<TimelineBakeMarker> markers)
        {
            if (director == null || !(director.playableAsset is TimelineAsset src))
                return false;
            if (!PlayableTrackBakeCore.HasPlayableTrack(src))
                return false;

            var validMarkers = markers.Where(marker => marker.recordRoots != null && marker.recordRoots.Length > 0).ToList();
            foreach (var marker in markers.Except(validMarkers))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {marker.name}: recordRoots が未設定のためスキップします。", marker);
            }
            if (validMarkers.Count == 0)
                return false;

            PlayableTrackBakeCore.ValidateWorkload(src, validMarkers);

            string srcPath = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(srcPath))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {director.name}: TimelineAsset が保存済みアセットではないため非破壊ベイクできません。", director);
                return false;
            }

            // Instantiate ではトラックが元を参照したままになるため、CopyAsset で確実に深く複製する
            string clonePath = AssetDatabase.GenerateUniqueAssetPath($"{TempFolder}/{PlayableTrackBakeCore.Sanitize(director.name)}.playable");
            if (!AssetDatabase.CopyAsset(srcPath, clonePath))
            {
                Debug.LogWarning($"[PlayableTrackBaker] {director.name}: TimelineAsset のクローンに失敗しました（{srcPath}）。", director);
                return false;
            }
            var clone = AssetDatabase.LoadAssetAtPath<TimelineAsset>(clonePath);
            if (clone == null)
            {
                AssetDatabase.DeleteAsset(clonePath);
                return false;
            }
            AssetDatabase.SetLabels(clone, AssetDatabase.GetLabels(clone)
                .Concat(new[] { PlayableTrackBakeTempCleanup.OwnershipLabel }).Distinct().ToArray());

            director.playableAsset = clone;
            try
            {
                var recorded = new List<(AnimationClip clip, GameObject root)>();
                foreach (var marker in validMarkers)
                    recorded.AddRange(PlayableTrackBakeCore.Record(director, clone, marker));

                // クリップはクローン Timeline のサブアセットとして持たせ、ビルドに含める
                foreach (var (clip, _) in recorded)
                    AssetDatabase.AddObjectToAsset(clip, clone);

                PlayableTrackBakeCore.AddBakedTracks(director, clone, recorded, true);
                PlayableTrackBakeCore.AddConfiguredSignalEvents(clone, recorded, validMarkers, src);

                EditorUtility.SetDirty(clone);
                EditorUtility.SetDirty(director);
                return true;
            }
            catch
            {
                // 失敗したクローンを Director に残さず、次のビルドへ持ち越さない。
                director.playableAsset = src;
                AssetDatabase.DeleteAsset(clonePath);
                EditorUtility.SetDirty(director);
                throw;
            }
        }

        internal static void EnsureTempFolder()
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
        internal const string OwnershipLabel = "PlayableTrackBaker.GeneratedTemp";
        public int callbackOrder => 10000;

        public void OnPostprocessBuild(BuildReport report) => Cleanup();

        [InitializeOnLoadMethod]
        static void SweepLeftoverOnLoad() => Cleanup();

        internal static void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(PlayableTrackBakeSceneProcessor.TempFolder))
            {
                // 固定フォルダ自体はユーザーも利用できるため、所有ラベル付き生成物だけを削除する。
                foreach (string guid in AssetDatabase.FindAssets($"l:{OwnershipLabel}", new[] { PlayableTrackBakeSceneProcessor.TempFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith(PlayableTrackBakeSceneProcessor.TempFolder + "/"))
                        AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.Refresh();
            }
        }
    }
}
#endif

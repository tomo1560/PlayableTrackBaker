using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>
    /// PlayableTrackBakeCore.Record / Sanitize を公開 API として検証する EditMode テスト。
    ///
    /// 各テストはメモリ上に GameObject / PlayableDirector / TimelineAsset / TimelineBakeMarker を
    /// 組み立て、SineMoveTestBehaviour（y = amplitude * sin(2π * frequency * t)）で決定論的な
    /// 動きを作って Record の結果カーブを期待値と突き合わせる。生成物は TearDown で全て破棄する。
    /// </summary>
    [TestFixture]
    public class PlayableTrackBakeCoreTests
    {
        const float FrameRate = 30f;
        const double Duration = 2.0; // 秒。frames = ceil(2.0 * 30) = 60、キー数は 61
        const float Amplitude = 1.5f;

        readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            _cleanup.Clear();
        }

        // ---- テスト用リグの組み立て ----------------------------------------------------

        sealed class Rig
        {
            public GameObject targetGo;
            public PlayableDirector director;
            public TimelineAsset timeline;
            public PlayableTrack track;
            public TimelineBakeMarker marker;
        }

        /// <summary>
        /// PlayableTrack に SineMoveTestPlayableAsset を 1 クリップ載せた Timeline と、
        /// それを評価する Director / Marker を組み立てる。
        /// クリップは Timeline の長さ（FixedLength = Duration）より長めにしておき、
        /// t == Duration ちょうどのサンプルでもクリップが評価されるようにする。
        /// </summary>
        Rig BuildRig(bool highPrecision, float reduction, float frequency, double duration = Duration)
        {
            var targetGo = new GameObject("BakeTest_Target");
            _cleanup.Add(targetGo);

            var directorGo = new GameObject("BakeTest_Director");
            _cleanup.Add(directorGo);
            var director = directorGo.AddComponent<PlayableDirector>();
            director.extrapolationMode = DirectorWrapMode.Hold;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "BakeTest_Timeline";
            _cleanup.Add(timeline);

            var track = timeline.CreateTrack<PlayableTrack>(null, "Move (Custom PlayableTrack)");
            _cleanup.Add(track);

            var clip = track.CreateClip<SineMoveTestPlayableAsset>();
            clip.start = 0;
            clip.duration = duration + 0.5; // 終端サンプル（t == duration）でも評価されるよう余裕を持たせる
            var asset = (SineMoveTestPlayableAsset)clip.asset;
            _cleanup.Add(asset);
            asset.amplitude = Amplitude;
            asset.frequency = frequency;

            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = duration;

            director.playableAsset = timeline;

            // ExposedReference で対象 Transform を Director 経由で解決させる
            var exposedName = new PropertyName(GUID.Generate().ToString());
            var reference = asset.target;
            reference.exposedName = exposedName;
            asset.target = reference;
            director.SetReferenceValue(exposedName, targetGo.transform);

            // TimelineBakeMarker は RequireComponent(PlayableDirector) なので同じ GameObject に付ける
            var marker = directorGo.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { targetGo };
            marker.recordAllProperties = false;
            marker.frameRate = FrameRate;
            marker.highPrecision = highPrecision;
            marker.highPrecisionReduction = reduction;

            return new Rig
            {
                targetGo = targetGo,
                director = director,
                timeline = timeline,
                track = track,
                marker = marker,
            };
        }

        /// <summary>Record を実行し、返却クリップを破棄リストに登録したうえで唯一のクリップを返す。</summary>
        AnimationClip RecordSingleClip(Rig rig)
        {
            var recorded = PlayableTrackBakeCore.Record(rig.director, rig.timeline, rig.marker);
            foreach (var (clip, _) in recorded)
                _cleanup.Add(clip);

            Assert.AreEqual(1, recorded.Count, "Record は recordRoots 1 件につき 1 クリップを返すはず");
            Assert.IsNotNull(recorded[0].clip, "Record が返すクリップが null");
            Assert.AreSame(rig.targetGo, recorded[0].root, "クリップに対応する root が recordRoots と一致するはず");
            return recorded[0].clip;
        }

        /// <summary>記録ルート直下（パス ""）の m_LocalPosition.y カーブを取得する。</summary>
        static AnimationCurve GetLocalPositionYCurve(AnimationClip clip)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y");
            return AnimationUtility.GetEditorCurve(clip, binding);
        }

        /// <summary>SineMoveTestBehaviour と同じ式で期待値を計算する。</summary>
        static float ExpectedY(float frequency, double t)
            => Amplitude * Mathf.Sin(2f * Mathf.PI * frequency * (float)t);

        static int SampleFrames => Mathf.CeilToInt((float)Duration * FrameRate); // 60

        // ---- Record（精度優先モード）---------------------------------------------------

        [Test]
        public void HighPrecision_MatchesSamplePointsExactly()
        {
            var rig = BuildRig(highPrecision: true, reduction: 0f, frequency: 0.5f);
            var clip = RecordSingleClip(rig);

            var curve = GetLocalPositionYCurve(clip);
            Assert.IsNotNull(curve, "m_LocalPosition.y のカーブが存在するはず");

            int frames = SampleFrames;
            Assert.AreEqual(frames + 1, curve.length, "無削減の精度優先モードでは全サンプルがキー化されるはず");

            for (int f = 0; f <= frames; f++)
            {
                double t = System.Math.Min(f / (double)FrameRate, Duration);
                var key = curve[f];
                Assert.AreEqual((float)t, key.time, 1e-5f, $"キー {f} の時刻がサンプル時刻と一致するはず");
                Assert.AreEqual(ExpectedY(0.5f, t), key.value, 1e-4f, $"キー {f}（t={t:F4}）の値が期待値と一致するはず");
            }
        }

        [Test]
        public void HighPrecision_LinearInterpolationBetweenSamples()
        {
            var rig = BuildRig(highPrecision: true, reduction: 0f, frequency: 0.5f);
            var clip = RecordSingleClip(rig);

            var curve = GetLocalPositionYCurve(clip);
            Assert.IsNotNull(curve);
            Assert.GreaterOrEqual(curve.length, 2);

            for (int i = 0; i < curve.length - 1; i++)
            {
                var k0 = curve[i];
                var k1 = curve[i + 1];
                float midTime = (k0.time + k1.time) * 0.5f;
                float expected = (k0.value + k1.value) * 0.5f; // 線形補間なら中点の値は隣接キーの平均
                Assert.AreEqual(expected, curve.Evaluate(midTime), 1e-4f,
                    $"キー {i}-{i + 1} の中間時刻 t={midTime:F4} で線形補間になっているはず");
            }
        }

        [Test]
        public void Reduction_StaysWithinErrorBound()
        {
            const float reduction = 0.02f;
            const float frequency = 0.5f;

            var rig = BuildRig(highPrecision: true, reduction: 0f, frequency: frequency);

            // まず無削減で記録してキー数の基準を得る
            var fullClip = RecordSingleClip(rig);
            var fullCurve = GetLocalPositionYCurve(fullClip);
            Assert.IsNotNull(fullCurve);

            // 同じリグのまま削減率だけ変えて記録し直す（動きは決定論的なので比較可能）
            rig.marker.highPrecisionReduction = reduction;
            var reducedClip = RecordSingleClip(rig);
            var reducedCurve = GetLocalPositionYCurve(reducedClip);
            Assert.IsNotNull(reducedCurve);

            Assert.Less(reducedCurve.length, fullCurve.length,
                "reduction > 0 ではキー数が無削減時より減るはず");

            // 期待値の値域から許容誤差（reduction × 値域）を算出し、全サンプル時刻で検証
            int frames = SampleFrames;
            float min = float.MaxValue, max = float.MinValue;
            var expected = new float[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                double t = System.Math.Min(f / (double)FrameRate, Duration);
                expected[f] = ExpectedY(frequency, t);
                if (expected[f] < min) min = expected[f];
                if (expected[f] > max) max = expected[f];
            }
            float bound = reduction * (max - min) + 1e-3f; // 微小マージン込み

            float maxError = 0f;
            for (int f = 0; f <= frames; f++)
            {
                double t = System.Math.Min(f / (double)FrameRate, Duration);
                float error = Mathf.Abs(reducedCurve.Evaluate((float)t) - expected[f]);
                if (error > maxError) maxError = error;
            }
            Assert.LessOrEqual(maxError, bound,
                $"削減後の最大誤差 {maxError:F5} が許容値 {bound:F5}（reduction × 値域 + マージン）以内であるはず");
        }

        // ---- Record（軽量モード）-------------------------------------------------------

        [Test]
        public void LightweightMode_ProducesClip()
        {
            // 端点の期待値が 0 で退化しないよう、Duration = 2 秒で 3/4 周期になる周波数を使う
            // （t=0 → 0、t=2 → sin(1.5π) = -1 × Amplitude）
            const float frequency = 0.375f;

            var rig = BuildRig(highPrecision: false, reduction: 0f, frequency: frequency);
            var clip = RecordSingleClip(rig);

            var curve = GetLocalPositionYCurve(clip);
            Assert.IsNotNull(curve, "軽量モードでも m_LocalPosition.y のカーブが存在するはず");
            Assert.GreaterOrEqual(curve.length, 2);

            Assert.AreEqual(ExpectedY(frequency, 0.0), curve.Evaluate(0f), 1e-3f,
                "t=0 の値が期待値と一致するはず");
            Assert.AreEqual(ExpectedY(frequency, Duration), curve.Evaluate((float)Duration), 1e-3f,
                "t=duration の値が期待値と一致するはず");
        }

        [Test]
        public void LightweightMode_NonIntegralFrameDurationEndsAtTimelineDuration()
        {
            const double duration = 1.01;
            var rig = BuildRig(highPrecision: false, reduction: 0f, frequency: 0.375f, duration: duration);
            var clip = RecordSingleClip(rig);

            Assert.AreEqual((float)duration, clip.length, 1e-4f,
                "Timeline の終端がフレーム境界でなくても、記録クリップが次フレームまで延びないはず");
        }

        // ---- AddBakedTracks（複数マーカー相当の集約結果）------------------------------

        [Test]
        public void AddBakedTracks_PreservesAllAggregatedRecordings()
        {
            var rig = BuildRig(highPrecision: true, reduction: 0f, frequency: 0.5f);
            var secondRoot = new GameObject("BakeTest_Target_Second");
            _cleanup.Add(secondRoot);

            var firstClip = new AnimationClip();
            var secondClip = new AnimationClip();
            _cleanup.Add(firstClip);
            _cleanup.Add(secondClip);
            var recorded = new List<(AnimationClip clip, GameObject root)>
            {
                (firstClip, rig.targetGo),
                (secondClip, secondRoot),
            };

            PlayableTrackBakeCore.AddBakedTracks(rig.director, rig.timeline, recorded, true);

            var bakedTracks = new List<TrackAsset>();
            foreach (var track in rig.timeline.GetOutputTracks())
                if (track.name.StartsWith(PlayableTrackBakeCore.BakedTrackPrefix))
                    bakedTracks.Add(track);

            Assert.AreEqual(2, bakedTracks.Count,
                "複数マーカーから集約した記録は、同じ Timeline 上にすべて追加されるはず");
            Assert.IsTrue(rig.track.muted, "集約追加後に元の PlayableTrack がミュートされるはず");
        }

        // ---- Record（ミュート解除）-----------------------------------------------------

        [Test]
        public void Record_UnmutesPlayableTracksForRecording()
        {
            var rig = BuildRig(highPrecision: true, reduction: 0f, frequency: 0.5f);
            rig.track.muted = true; // ミュートされたままでは評価されず、動きが記録できないはず

            var clip = RecordSingleClip(rig);

            Assert.IsFalse(rig.track.muted, "Record は記録前に PlayableTrack をアンミュートするはず");

            var curve = GetLocalPositionYCurve(clip);
            Assert.IsNotNull(curve);
            // t=0.5（freq 0.5 Hz の 1/4 周期）でピーク値 Amplitude が記録されているはず
            Assert.AreEqual(Amplitude, curve.Evaluate(0.5f), 1e-3f,
                "ミュートされていたトラックの動きも記録されているはず");
        }

        // ---- Sanitize -------------------------------------------------------------------

        [Test]
        public void Sanitize_ReplacesInvalidChars()
        {
            // '/' は全プラットフォームでファイル名に使えない
            Assert.AreEqual("A_B", PlayableTrackBakeCore.Sanitize("A/B"));

            // 実行環境の不正文字すべてが '_' に置換され、通常文字はそのまま残ること
            var invalid = Path.GetInvalidFileNameChars();
            string input = "Clip" + new string(invalid) + "Name";
            string result = PlayableTrackBakeCore.Sanitize(input);

            Assert.AreEqual(input.Length, result.Length, "置換で文字数は変わらないはず");
            StringAssert.StartsWith("Clip", result);
            StringAssert.EndsWith("Name", result);
            for (int i = 0; i < invalid.Length; i++)
            {
                Assert.AreEqual('_', result["Clip".Length + i],
                    $"不正文字 (0x{(int)invalid[i]:X2}) が '_' に置換されるはず");
            }
        }
    }
}

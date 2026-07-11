#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking
{
    /// <summary>
    /// ゴースト比較プレビュー。シーンや .playable / .anim アセットを一切書き換えずに、
    /// 「元の PlayableTrack が駆動する実オブジェクト」と「ベイク結果のクリップで駆動する使い捨てゴースト」を
    /// 同一時刻で並べてライブ比較するためのエディタウィンドウ。
    ///
    /// 非破壊の担保:
    ///  - ベイククリップは PlayableTrackBakeCore.Record でメモリ上に作るだけ（保存しない）。
    ///  - ゴーストは HideFlags.HideAndDontSave の複製で、確認後に DestroyImmediate で破棄する。
    ///  - オリジナルは director.Evaluate で実際に動かすが、開始前に record root 以下の TRS を退避し停止時に復元する。
    ///  - Record が PlayableTrack を unmute する副作用も、muted 状態を退避/復元して打ち消す。
    /// プレビュー中はシーンが一時的に dirty 化するが、停止時に値を元へ戻すため保存しなければ差分は残らない。
    /// （既存の未保存変更を壊す恐れがあるため EditorSceneManager.ClearSceneDirtiness は使わない。）
    /// </summary>
    public class PlayableTrackPreviewWindow : EditorWindow
    {
        [MenuItem("Tools/Timeline/Bake Preview (Ghost Compare)")]
        static void Open() => GetWindow<PlayableTrackPreviewWindow>("Bake Preview");

        TimelineBakeMarker _marker;

        // --- プレビュー中のみ有効な状態（StartPreview で確保、StopPreview で全解放）---
        bool _active;
        PlayableDirector _director;
        TimelineAsset _timeline;
        double _duration;
        GameObject[] _originalRoots;
        AnimationClip[] _clips;      // Record が返したメモリ上クリップ（root と同じ並び。未対応スロットは null）
        GameObject[] _ghosts;        // Instantiate した root クローン（クリップで駆動）
        Transform[] _containers;     // ゴーストのオフセット親（クリップに上書きされない位置にオフセットを持たせる）
        Transform[][] _origXf;       // 誤差比較用にキャッシュした元 root 配下の Transform 列
        Transform[][] _ghostXf;      // 同・ゴースト配下（Instantiate 複製なので列挙順は元と一致）
        List<TrsSnapshot> _trsSnapshot;
        List<MuteSnapshot> _muteSnapshot;

        bool _playing;
        float _time;
        double _lastUpdate;
        float _offset = 1.5f;

        // --- マーカー変更の自動反映（再ベイク）---
        const double RebakeDebounce = 0.25; // 最後の変更からこの秒数だけ待ってまとめて再ベイク
        bool _autoReapply = true;
        double _pendingRebakeAt = -1;       // <0 = 予約なし
        string _appliedSig;                 // 直近で反映済みのフィールド署名
        string _appliedRootsSig;            // 同・recordRoots だけの署名（構造変化の判定用）
        string _lastSeenSig;                // 前フレームで見た署名（変化した瞬間だけデバウンスを張り直す）

        struct TrsSnapshot { public Transform tr; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
        struct MuteSnapshot { public PlayableTrack track; public bool muted; }

        // ---------------------------------------------------------------- ライフサイクル

        void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopPreview(); // ウィンドウを閉じた／リロードされたときも確実に後始末
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                StopPreview();
        }

        // ---------------------------------------------------------------- GUI

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "元の PlayableTrack の動きと、ベイク結果で駆動するゴーストを並べて比較します。\n" +
                "シーンやアセットは書き換えません。プレビュー中はシーンを保存しないでください（停止時に元へ戻します）。\n" +
                "復元されるのは Record Roots 配下の Transform のみです。Activation など他トラックや、Transform 以外の" +
                "プロパティ（recordAllProperties 使用時の BlendShape 等）を動かす Timeline では停止後に一部状態が残る場合があります。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_active))
            {
                _marker = (TimelineBakeMarker)EditorGUILayout.ObjectField(
                    "Marker", _marker, typeof(TimelineBakeMarker), true);

                if (_marker == null && Selection.activeGameObject != null)
                {
                    var selected = Selection.activeGameObject.GetComponent<TimelineBakeMarker>();
                    if (selected != null && GUILayout.Button("選択中の TimelineBakeMarker を使う"))
                        _marker = selected;
                }
            }

            using (new EditorGUI.DisabledScope(_marker == null))
            {
                if (!_active)
                {
                    if (GUILayout.Button("プレビュー開始"))
                        StartPreview();
                }
                else if (GUILayout.Button("プレビュー停止"))
                {
                    StopPreview(); // 以降は _active==false になり下の return に落ちる
                }
            }

            if (!_active)
                return;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                _autoReapply = EditorGUILayout.ToggleLeft(
                    new GUIContent("変更を自動反映",
                        "Marker の Frame Rate / High Precision / Record Roots 等の変更を、プレビュー中に自動で再ベイクして反映します。\n重い（長尺 × High Precision）場合は OFF にして「再ベイク」ボタンで手動反映してください。"),
                    _autoReapply, GUILayout.Width(140));
                using (new EditorGUI.DisabledScope(_autoReapply))
                    if (GUILayout.Button("再ベイク"))
                        Rebake();
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _offset = EditorGUILayout.Slider("ゴーストのオフセット(X)", _offset, 0f, 5f);
            if (EditorGUI.EndChangeCheck())
                UpdateOffset();

            EditorGUI.BeginChangeCheck();
            bool play = EditorGUILayout.Toggle("再生", _playing);
            if (EditorGUI.EndChangeCheck())
            {
                _playing = play;
                if (_playing)
                    _lastUpdate = EditorApplication.timeSinceStartup; // 再開時の時間ジャンプを防ぐ
            }

            EditorGUI.BeginChangeCheck();
            float t = EditorGUILayout.Slider("時刻", _time, 0f, (float)_duration);
            if (EditorGUI.EndChangeCheck())
            {
                _time = t;
                ApplyAt(t);
            }

            EditorGUILayout.LabelField("最大ローカル位置誤差", ComputeMaxError().ToString("F5") + " m");
            EditorGUILayout.HelpBox(
                "オフセットを 0 にして重ね、誤差が 0 付近なら一致です。ずれる場合は Frame Rate を上げるか High Precision を有効化してください。",
                MessageType.None);
        }

        // ---------------------------------------------------------------- 開始／停止

        void StartPreview()
        {
            if (_marker == null)
                return;

            var director = _marker.director != null ? _marker.director : _marker.GetComponent<PlayableDirector>();
            if (director == null || !(director.playableAsset is TimelineAsset timeline))
            {
                EditorUtility.DisplayDialog("Bake Preview", "PlayableDirector か TimelineAsset が見つかりません。", "OK");
                return;
            }
            if (!PlayableTrackBakeCore.HasPlayableTrack(timeline))
            {
                EditorUtility.DisplayDialog("Bake Preview", "この Timeline に PlayableTrack がありません。", "OK");
                return;
            }
            if (_marker.recordRoots == null || _marker.recordRoots.Length == 0)
            {
                EditorUtility.DisplayDialog("Bake Preview", "Record Roots が未設定です。", "OK");
                return;
            }
            if (AnimationMode.InAnimationMode())
            {
                // 最多の原因は Timeline ウィンドウの Preview（既定 ON）。まず自動で OFF にして続行を試みる。
                if (TryDisableTimelinePreview())
                    Debug.Log("[BakePreview] Timeline ウィンドウの Preview が AnimationMode を握っていたため、自動で OFF にしました。");

                if (AnimationMode.InAnimationMode())
                {
                    EditorUtility.DisplayDialog("Bake Preview",
                        "AnimationMode を他のツールが使用中のため開始できません。\n\n" +
                        "多くの場合、Timeline ウィンドウの Preview（プレビュー）が原因です。自動解除を試みましたが解けませんでした。\n" +
                        "Timeline ウィンドウ左上の Preview ボタンを OFF にするか、Animation ウィンドウを閉じてから再試行してください。", "OK");
                    return;
                }
            }

            _director = director;
            _timeline = timeline;
            _duration = timeline.duration;
            _originalRoots = _marker.recordRoots;

            // Record は PlayableTrack を unmute する副作用があるので muted 状態を退避
            _muteSnapshot = _timeline.GetOutputTracks().OfType<PlayableTrack>()
                .Select(pt => new MuteSnapshot { track = pt, muted = pt.muted }).ToList();

            // Record（＝最初の Evaluate）より前にオリジナルの TRS を退避する。
            // Record の後に退避すると「タイムライン time=0 のポーズ」しか戻せず、開始前のポーズに復元できない。
            SnapshotTrs();

            // ここから確保物（unmute 済みトラック・ゴースト・AnimationMode）が発生する。
            // 途中で失敗しても StopPreview で確実に回収できるよう、先に active にしておく。
            _active = true;
            try
            {
                // 既存ロジックでメモリ上のベイククリップを得る（director をフレーム送りする副作用あり）
                var recorded = PlayableTrackBakeCore.Record(_director, _timeline, _marker);

                _clips = new AnimationClip[_originalRoots.Length];
                for (int i = 0; i < _originalRoots.Length; i++)
                {
                    var root = _originalRoots[i];
                    if (root == null)
                        continue;
                    foreach (var rec in recorded)
                        if (rec.root == root) { _clips[i] = rec.clip; break; }
                }

                if (_clips.All(c => c == null))
                {
                    EditorUtility.DisplayDialog("Bake Preview", "ベイク対象のクリップを生成できませんでした（Record Roots を確認してください）。", "OK");
                    StopPreview();
                    return;
                }

                // 比較の基準時刻として一旦 0 を評価（復元用スナップショットは Record 前に取得済み）
                _director.time = 0;
                _director.Evaluate();

                // ゴースト生成＋比較用 Transform 配列のキャッシュ
                _ghosts = new GameObject[_originalRoots.Length];
                _containers = new Transform[_originalRoots.Length];
                _origXf = new Transform[_originalRoots.Length][];
                _ghostXf = new Transform[_originalRoots.Length][];
                for (int i = 0; i < _originalRoots.Length; i++)
                {
                    if (_originalRoots[i] == null || _clips[i] == null)
                        continue;
                    CreateGhost(i);
                    _origXf[i] = _originalRoots[i].GetComponentsInChildren<Transform>(true);
                    _ghostXf[i] = _ghosts[i].GetComponentsInChildren<Transform>(true);
                }

                AnimationMode.StartAnimationMode();

                _playing = false;
                _time = 0f;
                _lastUpdate = EditorApplication.timeSinceStartup;

                // 自動反映の基準署名を初期化（この時点の値が「反映済み」）
                _appliedRootsSig = RootsSig();
                _appliedSig = FullSig();
                _lastSeenSig = _appliedSig;
                _pendingRebakeAt = -1;

                EditorApplication.update += OnEditorUpdate;
                ApplyAt(0f);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                StopPreview(); // 確保済みリソース（mute / ゴースト / AnimationMode）を回収
            }
        }

        // 冪等：どのフック（OnDisable / リロード / プレイ突入 / ボタン）から呼ばれても安全。
        void StopPreview()
        {
            if (!_active)
                return;
            _active = false;
            _playing = false;

            EditorApplication.update -= OnEditorUpdate;

            // ゴースト破棄より先に AnimationMode を止める（追跡中オブジェクトの破棄警告を避ける）
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            if (_containers != null)
                foreach (var c in _containers)
                    if (c != null)
                        DestroyImmediate(c.gameObject);
            _containers = null;
            _ghosts = null;
            _clips = null;
            _origXf = null;
            _ghostXf = null;

            RestoreMute();
            RestoreTrs(); // 最後に元の TRS へ戻す（以降 Evaluate しない）

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 開いている Timeline ウィンドウの Preview を OFF にして AnimationMode を解放する（1 つでも外せたら true）。
        ///
        /// Timeline ウィンドウは Preview（既定 ON）の間エディタを AnimationMode に入れるため、
        /// Bake Preview の AnimationMode 占有と競合する。内部 API
        /// （UnityEditor.Timeline.TimelineWindow / WindowState.previewMode）へのリフレクションなので、
        /// 構造が異なる Unity/Timeline 版では黙って false を返し、呼び出し側は改善メッセージへフォールバックする。
        /// 動作確認: Unity 2022.3 / com.unity.timeline 1.7。
        /// </summary>
        static bool TryDisableTimelinePreview()
        {
            try
            {
                var winType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("UnityEditor.Timeline.TimelineWindow"))
                    .FirstOrDefault(t => t != null);
                if (winType == null)
                    return false;

                // instance が未設定でも拾えるよう、ロード済みの全ウィンドウを走査する
                var windows = Resources.FindObjectsOfTypeAll(winType);
                if (windows == null || windows.Length == 0)
                    return false;

                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var stateProp = winType.GetProperty("state", F);
                if (stateProp == null)
                    return false;

                bool disabledAny = false;
                foreach (var win in windows)
                {
                    var state = stateProp.GetValue(win);
                    var previewProp = state?.GetType().GetProperty("previewMode", F);
                    if (previewProp == null || !previewProp.CanWrite || !previewProp.CanRead)
                        continue;
                    if ((bool)previewProp.GetValue(state))
                    {
                        previewProp.SetValue(state, false); // setter が同期的に AnimationMode を停止する
                        disabledAny = true;
                    }
                }
                return disabledAny;
            }
            catch
            {
                return false; // 内部 API 差異は握りつぶし、改善メッセージにフォールバック
            }
        }

        // ---------------------------------------------------------------- 毎フレーム／適用

        void OnEditorUpdate()
        {
            if (!_active)
                return;
            double now = EditorApplication.timeSinceStartup;

            if (_autoReapply)
                DetectAndScheduleRebake(now);

            if (!_active) // 上の再ベイクがフル再スタートを予約して停止した場合に備える
                return;

            if (_playing)
            {
                _time += (float)(now - _lastUpdate);
                if (_duration > 0 && _time > _duration)
                    _time %= (float)_duration; // ループ（大きなフレームギャップでも確実に範囲内へ）
                ApplyAt(_time);
            }
            _lastUpdate = now;
        }

        // ---------------------------------------------------------------- マーカー変更の自動反映

        // 署名が変化した瞬間だけデバウンスを張り直し、静止して閾値を過ぎたら再ベイクする。
        void DetectAndScheduleRebake(double now)
        {
            string cur = FullSig();
            if (cur != _lastSeenSig)
            {
                _lastSeenSig = cur;
                // 反映済みと違えばデバウンス開始、元に戻ったら予約取り消し
                _pendingRebakeAt = (cur != _appliedSig) ? now + RebakeDebounce : -1;
            }
            if (_pendingRebakeAt >= 0 && now >= _pendingRebakeAt)
            {
                _pendingRebakeAt = -1;
                Rebake();
            }
        }

        /// <summary>
        /// マーカーの現在値でベイクし直してプレビューへ反映する。
        ///  - recordRoots（対象そのもの）が変わった場合はゴースト/スナップショットの作り直しが要るため、
        ///    StopPreview→StartPreview のフル再スタート（delayCall で update コールバック外へ逃がす）。
        ///  - クリップ系（Frame Rate / High Precision / Reduction / Record All Properties）だけの変更なら、
        ///    再 Record して _clips を差し替えるだけの軽い経路。
        /// 自動反映 OFF 時は「再ベイク」ボタンからも呼ばれる。
        /// </summary>
        void Rebake()
        {
            if (!_active || _director == null || _marker == null)
                return;

            // 構造変化：安全のためフル再スタート（再入を避けて次ティックで実行）
            if (RootsSig() != _appliedRootsSig)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this == null) // ウィンドウが閉じられていたら何もしない
                        return;
                    StopPreview();
                    StartPreview();
                };
                return;
            }

            // クリップ系のみ：再 Record → _clips 差し替え → 現在時刻で再サンプル
            try
            {
                var recorded = PlayableTrackBakeCore.Record(_director, _timeline, _marker);
                var clips = new AnimationClip[_originalRoots.Length];
                for (int i = 0; i < _originalRoots.Length; i++)
                {
                    var root = _originalRoots[i];
                    if (root == null)
                        continue;
                    foreach (var rec in recorded)
                        if (rec.root == root) { clips[i] = rec.clip; break; }
                }
                _clips = clips;
                _appliedSig = FullSig();
                _lastSeenSig = _appliedSig;
                ApplyAt(_time);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // 再ベイク要否を判定するためのフィールド署名。
        // 構造署名 = director ＋ recordRoots の並び（インスタンス ID）。
        // director / recordRoots が変わるとバインドや対象自体が変わるため、
        // これが変化したら（クリップ差し替えではなく）フル再スタートに乗せる。
        string RootsSig()
        {
            if (_marker == null)
                return "";
            var dir = _marker.director != null ? _marker.director : _marker.GetComponent<PlayableDirector>();
            string dirId = dir != null ? dir.GetInstanceID().ToString() : "0";
            string roots = _marker.recordRoots == null
                ? ""
                : string.Join(",", _marker.recordRoots.Select(r => r == null ? "0" : r.GetInstanceID().ToString()));
            return dirId + ";" + roots;
        }

        string ClipSig()
        {
            if (_marker == null)
                return "";
            return $"{_marker.frameRate}|{_marker.highPrecision}|{_marker.highPrecisionReduction}|{_marker.recordAllProperties}";
        }

        string FullSig() => RootsSig() + "#" + ClipSig();

        void ApplyAt(float t)
        {
            if (!_active || _director == null)
                return;

            float clamped = Mathf.Min(t, (float)_duration);

            // オリジナル：director 評価で実 Transform を動かす（停止時 RestoreTrs で復元）
            _director.time = clamped;
            _director.Evaluate();

            // ゴースト：AnimationMode でベイククリップをサンプル（Animator 不要で generic クリップを駆動）
            if (AnimationMode.InAnimationMode() && _ghosts != null)
            {
                AnimationMode.BeginSampling();
                for (int i = 0; i < _ghosts.Length; i++)
                    if (_ghosts[i] != null && _clips[i] != null)
                        AnimationMode.SampleAnimationClip(_ghosts[i], _clips[i], clamped);
                AnimationMode.EndSampling();
            }

            SceneView.RepaintAll();
            Repaint();
        }

        // ---------------------------------------------------------------- ゴースト

        void CreateGhost(int index)
        {
            var root = _originalRoots[index];

            // オフセット親：元 root と同じ親の下に置き、ローカル基準を元 root と揃える。
            // クリップはゴースト root の localPosition を上書きするため、オフセットはこの親に持たせて誤差計算から分離する。
            var container = new GameObject("[BakePreview] " + root.name).transform;
            container.SetParent(root.transform.parent, false);
            container.localPosition = new Vector3(_offset, 0f, 0f);
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;

            // 非アクティブな親の下に複製し、複製の [ExecuteAlways]/IEditorOnly 等の OnEnable 副作用を抑止する。
            // Strip で無効化・除去してから有効化する。
            container.gameObject.SetActive(false);

            var ghost = Instantiate(root, container, false); // ローカル値を保持したまま非アクティブ親配下に生成
            ghost.name = root.name + " (Baked Preview)";

            StripForPreview(ghost);
            foreach (var tr in container.GetComponentsInChildren<Transform>(true))
                tr.gameObject.hideFlags = HideFlags.HideAndDontSave;

            container.gameObject.SetActive(true); // Strip 後に有効化

            _containers[index] = container;
            _ghosts[index] = ghost;
        }

        // 複製に紛れ込む副作用源を無効化（Play On Awake の Director、ポーズを上書きする Animator、edit mode で走る Behaviour 等）。
        static void StripForPreview(GameObject ghost)
        {
            foreach (var d in ghost.GetComponentsInChildren<PlayableDirector>(true))
                DestroyImmediate(d);
            foreach (var a in ghost.GetComponentsInChildren<Animator>(true))
                DestroyImmediate(a);
            foreach (var b in ghost.GetComponentsInChildren<MonoBehaviour>(true))
                if (b != null)
                    b.enabled = false;
        }

        void UpdateOffset()
        {
            if (_containers == null)
                return;
            foreach (var c in _containers)
                if (c != null)
                {
                    var lp = c.localPosition;
                    lp.x = _offset;
                    c.localPosition = lp;
                }
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- スナップショット

        void SnapshotTrs()
        {
            _trsSnapshot = new List<TrsSnapshot>();
            foreach (var root in _originalRoots)
            {
                if (root == null)
                    continue;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    _trsSnapshot.Add(new TrsSnapshot
                    {
                        tr = tr,
                        pos = tr.localPosition,
                        rot = tr.localRotation,
                        scale = tr.localScale
                    });
            }
        }

        void RestoreTrs()
        {
            if (_trsSnapshot == null)
                return;
            foreach (var s in _trsSnapshot)
                if (s.tr != null)
                {
                    s.tr.localPosition = s.pos;
                    s.tr.localRotation = s.rot;
                    s.tr.localScale = s.scale;
                }
            _trsSnapshot = null;
        }

        void RestoreMute()
        {
            if (_muteSnapshot == null)
                return;
            foreach (var m in _muteSnapshot)
                if (m.track != null)
                    m.track.muted = m.muted;
            _muteSnapshot = null;
        }

        // ---------------------------------------------------------------- 誤差

        // ゴーストは Instantiate の完全複製なので Transform の列挙順が元と一致する＝同一インデックスで対応する。
        // オフセットは container 側に載っているため localPosition 比較にはオフセットが混入しない。
        // 配列は開始時にキャッシュ済み（構造は不変）なので毎フレームの GetComponentsInChildren を避ける。
        float ComputeMaxError()
        {
            if (_origXf == null || _ghostXf == null)
                return 0f;
            float max = 0f;
            for (int i = 0; i < _origXf.Length; i++)
            {
                var a = _origXf[i];
                var b = _ghostXf[i];
                if (a == null || b == null)
                    continue;
                int n = Mathf.Min(a.Length, b.Length);
                for (int k = 0; k < n; k++)
                    if (a[k] != null && b[k] != null)
                        max = Mathf.Max(max, Vector3.Distance(a[k].localPosition, b[k].localPosition));
            }
            return max;
        }
    }
}
#endif

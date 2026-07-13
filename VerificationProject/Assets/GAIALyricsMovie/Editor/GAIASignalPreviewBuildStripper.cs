#if UNITY_EDITOR
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace GAIALyricsMovie.Editor
{
    /// <summary>通常Play専用のSignalReceiver一式をビルド用複製シーンから除去する。</summary>
    public sealed class GAIASignalPreviewBuildStripper : IProcessSceneWithReport
    {
        // PlayableTrackBakerがSignalをAnimationEventへ変換した後にプレビュー経路を除去する。
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report != null)
                StripPreview(scene);
        }

        public static void StripPreview(Scene scene)
        {
            GAIASignalPreviewEffect[] previewEffects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GAIASignalPreviewEffect>(true))
                .ToArray();
            foreach (GAIASignalPreviewEffect previewEffect in previewEffects)
            {
                SignalReceiver signalReceiver = previewEffect.GetComponent<SignalReceiver>();
                if (signalReceiver != null)
                    Object.DestroyImmediate(signalReceiver);
                Object.DestroyImmediate(previewEffect);
            }
        }
    }
}
#endif

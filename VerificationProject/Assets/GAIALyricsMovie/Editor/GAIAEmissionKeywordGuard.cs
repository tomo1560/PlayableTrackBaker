#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GAIALyricsMovie.Editor
{
    /// <summary>
    /// GAIAのマテリアルはほぼ黒いアルベド + エミッションだけで発光を表現しているため、
    /// バッチモードでの再生成やUnityプロセスをまたいだ再インポートで _EMISSION
    /// シェーダーキーワードが失われると、VRChat上でワールドが真っ黒に見える。
    /// アセットファイル側の修復だけでは再発しうるため、実際にVRChat SDKが
    /// パッケージ化する直前に強制的にキーワードを復元して保証する。
    /// </summary>
    public sealed class GAIAEmissionKeywordGuard : IProcessSceneWithReport
    {
        public int callbackOrder => -100;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report != null)
                RestoreEmission(scene);
        }

        public static void RestoreEmission(Scene scene)
        {
            var visited = new HashSet<Material>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !visited.Add(material))
                    continue;
                if (!material.HasProperty("_EmissionColor"))
                    continue;
                if (material.GetColor("_EmissionColor") == Color.black)
                    continue;
                if (material.IsKeywordEnabled("_EMISSION"))
                    continue;

                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
    }
}
#endif

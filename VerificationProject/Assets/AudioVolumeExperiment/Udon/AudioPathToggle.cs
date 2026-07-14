using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;

namespace AudioVolumeExperiment
{
    /// <summary>
    /// VRChatの音量スライダー検証用トグル。Interactするたびに、割り当てられた1つの再生経路
    /// （Timeline AudioTrack経由のPlayableDirector / 素のAudioSource.Play() /
    /// playOnAwake再生するオブジェクトのSetActive切り替え）を再生/停止する。
    /// どの経路がMaster/World音量スライダーに反応するかを実機で聞き比べるための装置で、同期はしない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class AudioPathToggle : UdonSharpBehaviour
    {
        [SerializeField] PlayableDirector director;
        [SerializeField] AudioSource audioSource;
        // VideoPlayer等、UdonにAPIが公開されていない再生コンポーネント用。
        // playOnAwake+loop設定済みオブジェクトのSetActiveで再生/停止を切り替える。
        [SerializeField] GameObject toggleTarget;

        // director.state / audioSource.isPlaying に頼らずローカルで再生状態を持つ。
        // 曲が自然終了した場合は2回押せば再び再生される。
        bool playing;

        public override void Interact()
        {
            playing = !playing;

            if (director != null)
            {
                if (playing)
                {
                    director.time = 0d;
                    director.Play();
                }
                else
                {
                    director.Stop();
                }
            }

            if (audioSource != null)
            {
                if (playing)
                    audioSource.Play();
                else
                    audioSource.Stop();
            }

            if (toggleTarget != null)
                toggleTarget.SetActive(playing);
        }
    }
}

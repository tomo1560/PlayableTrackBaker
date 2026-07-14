using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;

namespace AudioVolumeExperiment
{
    /// <summary>
    /// VRChatの音量スライダー検証用トグル。Interactするたびに、割り当てられた片方の再生経路
    /// （Timeline AudioTrack経由のPlayableDirector、または素のAudioSource.Play()）を再生/停止する。
    /// どちらの経路がMaster/World音量スライダーに反応するかを実機で聞き比べるための装置で、同期はしない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class AudioPathToggle : UdonSharpBehaviour
    {
        [SerializeField] PlayableDirector director;
        [SerializeField] AudioSource audioSource;

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
        }
    }
}

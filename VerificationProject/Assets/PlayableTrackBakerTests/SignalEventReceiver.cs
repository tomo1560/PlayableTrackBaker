using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;

namespace PlayableTrackBaking.Verification
{
    /// <summary>
    /// SignalEmitter → AnimationEvent → UdonBehaviour.SendCustomEvent の実機確認用 receiver。
    /// event host と同じ GameObject に置き、Inspector の Event Count と色で発火を確認する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class SignalEventReceiver : UdonSharpBehaviour
    {
        [SerializeField] private Renderer indicator;
        [SerializeField] private Color openDoorColor = Color.green;
        [SerializeField] private Color unlockPuzzleColor = Color.cyan;

        [HideInInspector] public int eventCount;
        [HideInInspector] public string lastEventName;

        public void OnOpenDoor()
        {
            RecordEvent(nameof(OnOpenDoor), openDoorColor);
        }

        public void OnUnlockPuzzle()
        {
            RecordEvent(nameof(OnUnlockPuzzle), unlockPuzzleColor);
        }

        private void RecordEvent(string eventName, Color color)
        {
            eventCount++;
            lastEventName = eventName;
            if (indicator != null)
                indicator.material.color = color;
            Debug.Log($"[PlayableTrackBaker Signal Test] {eventName} ({eventCount})", this);
        }
    }
}

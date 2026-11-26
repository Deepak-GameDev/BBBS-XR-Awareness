using System.Collections;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// Quest controller input:
    ///   A                    -> pause / resume detection
    ///   B                    -> toggle people-only filter
    ///   Right thumbstick up  -> raise confidence threshold (+5%, repeats while held)
    ///   Right thumbstick down-> lower confidence threshold (-5%)
    /// Each action gives a short haptic pulse as feedback.
    ///
    /// In the Unity Editor the keyboard works too: P = pause, O = people-only, Up/Down arrows = threshold.
    /// </summary>
    public class AwarenessControls : MonoBehaviour
    {
        public float StickDeadzone = 0.6f;
        public float RepeatDelay = 0.35f;

        private DetectionUiMenuManager m_menu;
        private bool m_started;
        private int m_startedFrame;
        private float m_nextRepeatTime;
        private int m_lastStickDirection;

        public void Init(DetectionUiMenuManager menu) => m_menu = menu;

        private void Update()
        {
            // The sample shows a start panel ("press A to start"). Our controls only become active
            // after that, and not in the same frame, so the same A press doesn't also pause.
            if (!m_started)
            {
                bool sampleStarted = m_menu == null || !m_menu.IsPaused || Application.isEditor;
                if (sampleStarted)
                {
                    m_started = true;
                    m_startedFrame = Time.frameCount;
                }
                return;
            }
            if (Time.frameCount == m_startedFrame)
            {
                return;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.A) || EditorKeyDown(KeyCode.P))
            {
                AwarenessState.TogglePause();
                Pulse();
            }

            if (OVRInput.GetDown(OVRInput.RawButton.B) || EditorKeyDown(KeyCode.O))
            {
                AwarenessState.TogglePeopleOnly();
                Pulse();
            }

            HandleThreshold();
        }

        private void HandleThreshold()
        {
            float y = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
            int direction = y > StickDeadzone ? 1 : y < -StickDeadzone ? -1 : 0;

            if (EditorKeyDown(KeyCode.UpArrow))
            {
                direction = 1;
                m_lastStickDirection = 0; // force a step
            }
            else if (EditorKeyDown(KeyCode.DownArrow))
            {
                direction = -1;
                m_lastStickDirection = 0;
            }

            if (direction == 0)
            {
                m_lastStickDirection = 0;
                return;
            }

            // Step once when the stick is pushed, then repeat every RepeatDelay while held.
            bool newPush = direction != m_lastStickDirection;
            if (newPush || Time.unscaledTime >= m_nextRepeatTime)
            {
                AwarenessState.ChangeThreshold(direction * AwarenessState.ThresholdStep);
                m_nextRepeatTime = Time.unscaledTime + RepeatDelay;
                Pulse();
            }
            m_lastStickDirection = direction;
        }

        private static bool EditorKeyDown(KeyCode key) => EditorKeys.Down(key);

        private void Pulse() => StartCoroutine(PulseRoutine());

        private IEnumerator PulseRoutine()
        {
            OVRInput.SetControllerVibration(0.5f, 0.3f, OVRInput.Controller.RTouch);
            yield return new WaitForSecondsRealtime(0.06f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}

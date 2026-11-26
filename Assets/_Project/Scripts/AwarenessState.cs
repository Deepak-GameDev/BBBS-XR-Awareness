using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// The user-controlled settings of the app, in one place:
    /// paused, people-only filter and confidence threshold.
    /// Controls write here; the HUD, markers, radar and inference read from here.
    /// </summary>
    public static class AwarenessState
    {
        public const float MinThreshold = 0.10f;
        public const float MaxThreshold = 0.90f;
        public const float ThresholdStep = 0.05f;
        public const float DefaultThreshold = 0.40f;

        public static bool Paused { get; private set; }
        public static bool PeopleOnly { get; private set; }
        public static float ConfidenceThreshold { get; private set; } = DefaultThreshold;

        /// <summary>True when detections come from the editor simulator instead of the headset camera.</summary>
        public static bool SimulationActive { get; set; }

        // Reset static values when entering Play mode (needed if "Domain Reload" is disabled).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Paused = false;
            PeopleOnly = false;
            SimulationActive = false;
            SetThreshold(DefaultThreshold);
            SentisInferenceRunManager.ExternalPause = false;
        }

        public static void TogglePause()
        {
            Paused = !Paused;
            SentisInferenceRunManager.ExternalPause = Paused; // stops the inference loop
        }

        public static void TogglePeopleOnly() => PeopleOnly = !PeopleOnly;

        public static void ChangeThreshold(float delta) => SetThreshold(ConfidenceThreshold + delta);

        private static void SetThreshold(float value)
        {
            // Round to the step so we never show values like 39.999%
            float stepped = Mathf.Round(value / ThresholdStep) * ThresholdStep;
            ConfidenceThreshold = Mathf.Clamp(stepped, MinThreshold, MaxThreshold);
            SentisInferenceRunManager.ScoreThresholdOverride = ConfidenceThreshold; // filters in NMS
        }

        /// <summary>Should this tracked item be shown given the current filter?</summary>
        public static bool PassesFilter(TrackedDetection track) => !PeopleOnly || track.IsPerson;
    }
}

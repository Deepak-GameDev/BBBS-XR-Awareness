using System.Collections.Generic;
using Meta.XR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// The glue: receives raw detections from the inference runner (Meta sample, modified),
    /// projects them into 3D, and feeds the tracker. Also keeps the stats the HUD shows.
    ///
    ///   SentisInferenceRunManager --(2D boxes + capture pose)--> AwarenessPipeline
    ///        --> DetectionProjector (3D) --> DetectionTracker (stable IDs) --> markers / HUD / radar
    /// </summary>
    public class AwarenessPipeline : MonoBehaviour
    {
        public DetectionTracker Tracker { get; } = new DetectionTracker();
        public Camera ViewCamera { get; private set; }

        // Stats for the HUD
        public float LastInferenceMs { get; private set; }
        public float InferencesPerSecond { get; private set; }
        public int RawDetectionsLastFrame { get; private set; }

        private DetectionProjector m_projector;
        private readonly List<Detection3D> m_frameDetections = new List<Detection3D>(32);
        private int m_inferenceCounter;
        private float m_rateWindowStart;
        private const int MaxDetectionsPerFrame = 25;

        public void Init(Camera viewCamera, PassthroughCameraAccess cameraAccess, EnvironmentRaycastManager raycastManager)
        {
            ViewCamera = viewCamera;
            m_projector = new DetectionProjector(cameraAccess, raycastManager);
        }

        private void OnEnable() => SentisInferenceRunManager.OnInferenceCompleted += HandleInference;
        private void OnDisable() => SentisInferenceRunManager.OnInferenceCompleted -= HandleInference;

        private void HandleInference(IReadOnlyList<(int classId, Vector4 boundingBox, float score)> detections, Vector2Int inputSize, Pose cameraPose, float inferenceMs)
        {
            if (AwarenessState.Paused || m_projector == null)
            {
                return;
            }

            LastInferenceMs = inferenceMs;
            m_inferenceCounter++;

            m_frameDetections.Clear();
            var labels = SentisInferenceRunManager.Labels;
            int count = Mathf.Min(detections.Count, MaxDetectionsPerFrame);
            for (int i = 0; i < count; i++)
            {
                var d = detections[i];
                if (d.score < AwarenessState.ConfidenceThreshold)
                {
                    continue;
                }
                string label = d.classId >= 0 && d.classId < labels.Length ? labels[d.classId] : $"class {d.classId}";
                bool isPerson = label == "person";

                if (!m_projector.TryProject(d.boundingBox, inputSize, cameraPose, isPerson, out var worldPos))
                {
                    continue; // no depth hit: skip rather than guess a fixed distance
                }

                m_frameDetections.Add(new Detection3D
                {
                    ClassId = d.classId,
                    Label = label,
                    IsPerson = isPerson,
                    Confidence = d.score,
                    Position = worldPos,
                });
            }

            RawDetectionsLastFrame = m_frameDetections.Count;
            Tracker.Integrate(m_frameDetections, cameraPose.position, Time.time);
        }

        /// <summary>Used by the editor simulator to inject detections that are already in 3D.</summary>
        public void InjectSimulatedDetections(List<Detection3D> detections, float simulatedInferenceMs)
        {
            if (AwarenessState.Paused || ViewCamera == null)
            {
                return;
            }
            LastInferenceMs = simulatedInferenceMs;
            m_inferenceCounter++;
            RawDetectionsLastFrame = detections.Count;
            Tracker.Integrate(detections, ViewCamera.transform.position, Time.time);
        }

        private void Update()
        {
            Tracker.Tick(ViewCamera, Time.time, Time.deltaTime, AwarenessState.Paused);

            // Inference rate, measured over 1-second windows
            float elapsed = Time.unscaledTime - m_rateWindowStart;
            if (elapsed >= 1f)
            {
                InferencesPerSecond = m_inferenceCounter / elapsed;
                m_inferenceCounter = 0;
                m_rateWindowStart = Time.unscaledTime;
            }
        }
    }
}

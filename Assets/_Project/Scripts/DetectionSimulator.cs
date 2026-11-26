using System.Collections.Generic;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// EDITOR TESTING ONLY. Pretends to be the detector so the tracker, markers, HUD, radar and
    /// controls can be tested in Play mode without a headset.
    ///
    /// Creates fake people/objects around the user. At ~10 Hz it "detects" the ones in view,
    /// with position noise, random misses and realistic confidences, and feeds them into the
    /// same pipeline the real detector uses.
    ///
    /// Keys: Q / E = turn left / right, so you can test the radar and items leaving the view.
    /// Never runs in a device build.
    /// </summary>
    public class DetectionSimulator : MonoBehaviour
    {
        private class FakeItem
        {
            public string Label;
            public int ClassId;
            public float BaseConfidence;
            public Vector3 Center;
            public float OrbitRadius;   // > 0: walks in a circle
            public float OrbitSpeed;    // degrees / second
            public float Angle;
            public Vector3 Position;
        }

        public float DetectionRateHz = 10f;
        public float PositionNoise = 0.05f;
        public float MissChance = 0.12f;
        public float TurnSpeed = 90f;

        private AwarenessPipeline m_pipeline;
        private Camera m_camera;
        private Transform m_rigRoot;
        private readonly List<FakeItem> m_items = new List<FakeItem>();
        private readonly List<Detection3D> m_buffer = new List<Detection3D>();
        private float m_nextDetection;

        public void Init(AwarenessPipeline pipeline, Camera viewCamera, Transform rigRoot)
        {
            m_pipeline = pipeline;
            m_camera = viewCamera;
            m_rigRoot = rigRoot;
            AwarenessState.SimulationActive = true;

            // Layout relative to where the user starts (COCO class ids: person 0, bottle 39, chair 56, laptop 63)
            Vector3 head = viewCamera.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // Heights are relative to the user's eyes so everything is visible in the editor camera.
            Add("person", 0, 0.88f, head + fwd * 2.5f, 1.2f, 25f, head.y - 0.4f);              // walking in a circle in front
            Add("person", 0, 0.80f, head - right * 1.6f + fwd * 1.4f, 0f, 0f, head.y - 0.4f);  // standing to the left
            Add("chair", 56, 0.62f, head + right * 1.4f + fwd * 2.2f, 0f, 0f, head.y - 0.9f);
            Add("bottle", 39, 0.46f, head + fwd * 1.6f + right * 0.3f, 0f, 0f, head.y - 0.5f); // lower confidence: disappears when threshold > 50%
            Add("laptop", 63, 0.72f, head - fwd * 2.5f, 0f, 0f, head.y - 0.7f);                // behind you: only on the radar
        }

        private void Add(string label, int classId, float confidence, Vector3 center, float orbitRadius, float orbitSpeed, float height)
        {
            center.y = height;
            m_items.Add(new FakeItem
            {
                Label = label, ClassId = classId, BaseConfidence = confidence,
                Center = center, OrbitRadius = orbitRadius, OrbitSpeed = orbitSpeed,
                Angle = Random.Range(0f, 360f), Position = center,
            });
        }

        private void OnDestroy() => AwarenessState.SimulationActive = false;

        private void Update()
        {
            if (m_pipeline == null || m_camera == null)
            {
                return;
            }
            HandleTurning();
            MoveItems();

            if (Time.time < m_nextDetection)
            {
                return;
            }
            m_nextDetection = Time.time + 1f / DetectionRateHz;

            m_buffer.Clear();
            foreach (var item in m_items)
            {
                Vector3 vp = m_camera.WorldToViewportPoint(item.Position);
                bool visible = vp.z > 0.3f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
                if (!visible || Random.value < MissChance)
                {
                    continue;
                }
                float confidence = Mathf.Clamp01(item.BaseConfidence + Random.Range(-0.06f, 0.06f));
                if (confidence < AwarenessState.ConfidenceThreshold)
                {
                    continue; // same rule as the real NMS threshold
                }
                m_buffer.Add(new Detection3D
                {
                    ClassId = item.ClassId,
                    Label = item.Label,
                    IsPerson = item.ClassId == 0,
                    Confidence = confidence,
                    Position = item.Position + Random.insideUnitSphere * PositionNoise,
                });
            }
            m_pipeline.InjectSimulatedDetections(m_buffer, Random.Range(35f, 55f));
        }

        private void MoveItems()
        {
            foreach (var item in m_items)
            {
                if (item.OrbitRadius <= 0f)
                {
                    continue;
                }
                item.Angle += item.OrbitSpeed * Time.deltaTime;
                float rad = item.Angle * Mathf.Deg2Rad;
                item.Position = item.Center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * item.OrbitRadius;
            }
        }

        private void HandleTurning()
        {
#if UNITY_EDITOR
            if (m_rigRoot == null)
            {
                return;
            }
            float turn = 0f;
            if (EditorKeys.Held(KeyCode.Q)) turn -= 1f;
            if (EditorKeys.Held(KeyCode.E)) turn += 1f;
            if (turn != 0f)
            {
                m_rigRoot.RotateAround(m_camera.transform.position, Vector3.up, turn * TurnSpeed * Time.deltaTime);
            }
#endif
        }
    }
}

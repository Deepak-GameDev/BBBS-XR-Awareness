using Meta.XR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;
using UnityEngine.UI;

namespace BBB.Awareness
{
    /// <summary>
    /// Head-locked HUD panel (follows the head smoothly, sits slightly below eye level):
    ///   left : people count, total detections, inference time, inference rate, frame rate
    ///   right: radar
    ///   bottom: current state (live / paused, people-only filter, threshold), controls help,
    ///           and clear status messages (camera permission missing, camera starting...)
    /// </summary>
    public class HudPresenter : MonoBehaviour
    {
        public Vector3 HeadOffset = new Vector3(0f, -0.26f, 0.85f);  // metres, in head space
        public float FollowSharpness = 8f;                             // higher = follows faster
        public float RefreshInterval = 0.2f;                           // text updates 5x per second

        private AwarenessPipeline m_pipeline;
        private Transform m_head;
        private PassthroughCameraAccess m_cameraAccess;
        private DetectionUiMenuManager m_menu;

        private Transform m_canvasTransform;
        private Text m_stats;
        private Text m_state;
        private Text m_status;

        private float m_nextRefresh;
        private int m_frameCount;
        private float m_fpsWindowStart;
        private float m_fps;
        private bool m_placed;

        public void Init(AwarenessPipeline pipeline, Transform head, PassthroughCameraAccess cameraAccess, DetectionUiMenuManager menu)
        {
            m_pipeline = pipeline;
            m_head = head;
            m_cameraAccess = cameraAccess;
            m_menu = menu;
            Build();
        }

        private void Build()
        {
            var canvas = UiFactory.CreateWorldCanvas("HUD", null, new Vector2(620, 330), 0.00062f);
            m_canvasTransform = canvas.transform;
            var root = (RectTransform)canvas.transform;

            UiFactory.CreateImage("Panel", root, Vector2.zero, new Vector2(620, 330), UiFactory.PanelColor);

            var title = UiFactory.CreateText("Title", root, new Vector2(-150, 135), new Vector2(300, 40), 26, TextAnchor.MiddleLeft, UiFactory.DimTextColor, FontStyle.Bold);
            title.text = "SITUATIONAL AWARENESS";

            m_stats = UiFactory.CreateText("Stats", root, new Vector2(-150, 30), new Vector2(300, 170), 30, TextAnchor.UpperLeft, UiFactory.TextColor);
            m_stats.lineSpacing = 1.15f;

            m_state = UiFactory.CreateText("State", root, new Vector2(0, -105), new Vector2(580, 34), 26, TextAnchor.MiddleLeft, UiFactory.TextColor, FontStyle.Bold);
            var help = UiFactory.CreateText("Help", root, new Vector2(0, -138), new Vector2(580, 30), 22, TextAnchor.MiddleLeft, UiFactory.DimTextColor);
            help.text = "A: pause/resume    B: people only    Right stick up/down: threshold";

            m_status = UiFactory.CreateText("Status", root, new Vector2(0, 185), new Vector2(620, 40), 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

            RadarView.Create(root, new Vector2(175, 35), 190f, m_pipeline, m_head);
        }

        private void LateUpdate()
        {
            if (m_head == null)
            {
                return;
            }
            FollowHead();
            MeasureFps();

            if (Time.unscaledTime >= m_nextRefresh)
            {
                m_nextRefresh = Time.unscaledTime + RefreshInterval;
                RefreshText();
            }
        }

        private void FollowHead()
        {
            Vector3 targetPos = m_head.position + m_head.rotation * HeadOffset;
            Quaternion targetRot = Quaternion.LookRotation(targetPos - m_head.position, m_head.up);

            if (!m_placed)
            {
                m_canvasTransform.SetPositionAndRotation(targetPos, targetRot);
                m_placed = true;
                return;
            }
            // Frame-rate independent smoothing: feels attached to the head but without jitter.
            float t = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
            m_canvasTransform.SetPositionAndRotation(
                Vector3.Lerp(m_canvasTransform.position, targetPos, t),
                Quaternion.Slerp(m_canvasTransform.rotation, targetRot, t));
        }

        private void MeasureFps()
        {
            m_frameCount++;
            float elapsed = Time.unscaledTime - m_fpsWindowStart;
            if (elapsed >= 0.5f)
            {
                m_fps = m_frameCount / elapsed;
                m_frameCount = 0;
                m_fpsWindowStart = Time.unscaledTime;
            }
        }

        private void RefreshText()
        {
            int people = 0;
            int total = 0;
            foreach (var track in m_pipeline.Tracker.Tracks)
            {
                if (!track.Confirmed || !AwarenessState.PassesFilter(track))
                {
                    continue;
                }
                total++;
                if (track.IsPerson)
                {
                    people++;
                }
            }

            string personHex = ColorUtility.ToHtmlStringRGB(UiFactory.PersonColor);
            string objectHex = ColorUtility.ToHtmlStringRGB(UiFactory.ObjectColor);
            m_stats.text =
                $"<color=#{personHex}>People</color>          <b>{people}</b>\n" +
                $"<color=#{objectHex}>Detections</color>    <b>{total}</b>\n" +
                $"Inference       <b>{m_pipeline.LastInferenceMs:0} ms</b>\n" +
                $"Model rate     <b>{m_pipeline.InferencesPerSecond:0.0} /s</b>\n" +
                $"Frame rate     <b>{m_fps:0} fps</b>";

            string live = AwarenessState.Paused ? "<color=#FFC040>|| PAUSED</color>" : "<color=#50E080>LIVE</color>";
            if (AwarenessState.SimulationActive)
            {
                live += " <color=#B0B0FF>(SIMULATION)</color>";
            }
            m_state.text = $"{live}    People only: {(AwarenessState.PeopleOnly ? "ON" : "OFF")}    Threshold: {AwarenessState.ConfidenceThreshold * 100f:0}%";

            m_status.text = GetStatusMessage();
        }

        /// <summary>Clear message when something prevents detection (requirement: handle denied permission gracefully).</summary>
        private string GetStatusMessage()
        {
            if (AwarenessState.SimulationActive)
            {
                return "";
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            {
                return "Camera permission needed: Settings > Privacy > App permissions, then restart";
            }
            if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
            {
                return "Spatial data permission needed for distance: enable it in Settings, then restart";
            }
#endif
            if (m_menu != null && m_menu.IsPaused && !AwarenessState.Paused)
            {
                return "Press A to start detection";
            }
            if (m_cameraAccess != null && !m_cameraAccess.IsPlaying)
            {
                return "Starting camera...";
            }
            return "";
        }
    }
}

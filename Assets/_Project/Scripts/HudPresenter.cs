using Meta.XR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;
using UnityEngine.UI;

namespace BBB.Awareness
{
    /// <summary>
    /// Compact head-locked HUD. It sits low in the view (about 22 degrees below eye level) so it
    /// never covers what the user is looking at, but is readable with a quick glance down.
    ///
    ///  +----------+-----------------------+----------------+
    ///  |  RADAR   |   2 PEOPLE  3 OBJECTS  |  LIVE          |
    ///  |          |   48 ms     72 fps     |  People: OFF   |
    ///  |          |                       |  Thr: 45%      |
    ///  |          |  A pause  B people  R-stick threshold  |
    ///  +----------+-----------------------+----------------+
    /// A status banner appears above the panel only when something needs attention
    /// (e.g. camera permission missing).
    /// </summary>
    public class HudPresenter : MonoBehaviour
    {
        public Vector3 HeadOffset = new Vector3(0f, -0.30f, 0.75f);  // metres, in head space (low in the view)
        public float FollowSharpness = 8f;                             // higher = follows faster
        public float RefreshInterval = 0.2f;                           // text updates 5x per second

        private const float MetersPerUnit = 0.00048f;                  // 720 units wide -> about 35 cm at 75 cm away

        private AwarenessPipeline m_pipeline;
        private Transform m_head;
        private PassthroughCameraAccess m_cameraAccess;
        private DetectionUiMenuManager m_menu;

        private Transform m_canvasTransform;
        private Text m_peopleValue;
        private Text m_objectsValue;
        private Text m_inferenceValue;
        private Text m_modelRate;
        private Text m_fpsValue;
        private Text m_liveState;
        private Text m_filterState;
        private Text m_thresholdState;
        private Text m_status;
        private Image m_statusBackground;

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

        // ---------------------------------------------------------------------------------
        // LAYOUT. All positions are in UI units, measured from the panel centre (x right, y up).
        // The panel is PanelSize wide/tall; 1 unit = MetersPerUnit metres in the world.
        // To restyle the HUD, change the numbers in this method.
        // ---------------------------------------------------------------------------------
        private static readonly Vector2 PanelSize = new Vector2(720, 220);

        private void Build()
        {
            var canvas = UiFactory.CreateWorldCanvas("HUD", null, PanelSize, MetersPerUnit);
            m_canvasTransform = canvas.transform;
            var root = (RectTransform)canvas.transform;
            var dividerColor = new Color(1f, 1f, 1f, 0.12f);

            // Background: transparent enough that the real world shows through.
            UiFactory.CreateImage("Panel", root, Vector2.zero, PanelSize, new Color(0.04f, 0.05f, 0.07f, 0.55f));

            // LEFT column (x -360 .. -150): radar
            RadarView.Create(root, new Vector2(-255, 0), 190f, m_pipeline, m_head);
            UiFactory.CreateImage("Divider1", root, new Vector2(-145, 0), new Vector2(2, 190), dividerColor);

            // MIDDLE column (x -140 .. 120): 2x2 stat grid, each cell 125 wide
            m_peopleValue = CreateStat(root, new Vector2(-75, 52), "people", UiFactory.PersonColor);
            m_objectsValue = CreateStat(root, new Vector2(55, 52), "objects", UiFactory.ObjectColor);
            m_inferenceValue = CreateStat(root, new Vector2(-75, -22), "inference", UiFactory.TextColor);
            m_fpsValue = CreateStat(root, new Vector2(55, -22), "fps", UiFactory.TextColor);
            UiFactory.CreateImage("Divider2", root, new Vector2(125, 10), new Vector2(2, 150), dividerColor);

            // RIGHT column (x 140 .. 350): current state, left aligned
            const float rightX = 245f;      // centre of a 210-wide column -> starts at x 140
            const float rightWidth = 210f;
            m_liveState = UiFactory.CreateText("Live", root, new Vector2(rightX, 66), new Vector2(rightWidth, 30), 22, TextAnchor.MiddleLeft, UiFactory.TextColor, FontStyle.Bold);
            m_filterState = UiFactory.CreateText("Filter", root, new Vector2(rightX, 34), new Vector2(rightWidth, 26), 19, TextAnchor.MiddleLeft, UiFactory.TextColor);
            m_thresholdState = UiFactory.CreateText("Threshold", root, new Vector2(rightX, 6), new Vector2(rightWidth, 26), 19, TextAnchor.MiddleLeft, UiFactory.TextColor);
            m_modelRate = UiFactory.CreateText("ModelRate", root, new Vector2(rightX, -22), new Vector2(rightWidth, 26), 19, TextAnchor.MiddleLeft, UiFactory.DimTextColor);

            // BOTTOM row (under middle + right columns): controls help
            var help = UiFactory.CreateText("Help", root, new Vector2(105, -88), new Vector2(480, 26), 17, TextAnchor.MiddleCenter, UiFactory.DimTextColor);
            help.text = "A  pause        B  people only        R-stick  threshold";

            // Status banner above the panel, only visible when there is a message
            m_statusBackground = UiFactory.CreateImage("StatusBg", root, new Vector2(0, 140), new Vector2(PanelSize.x, 40), new Color(0.35f, 0.22f, 0f, 0.8f));
            m_status = UiFactory.CreateText("Status", m_statusBackground.transform, Vector2.zero, new Vector2(PanelSize.x - 20f, 36), 21, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.4f), FontStyle.Bold);
            m_statusBackground.gameObject.SetActive(false);
        }

        /// <summary>A big number with a small caption under it.</summary>
        private static Text CreateStat(RectTransform root, Vector2 pos, string caption, Color valueColor)
        {
            var value = UiFactory.CreateText("Value", root, pos, new Vector2(125, 46), 42, TextAnchor.MiddleCenter, valueColor, FontStyle.Bold);
            var captionText = UiFactory.CreateText("Caption", root, pos + new Vector2(0, -32), new Vector2(125, 22), 16, TextAnchor.MiddleCenter, UiFactory.DimTextColor);
            captionText.text = caption.ToUpperInvariant();
            return value;
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
            // Face the eyes: the panel tilts up towards the user, which keeps it readable low in the view.
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
            int objects = 0;
            foreach (var track in m_pipeline.Tracker.Tracks)
            {
                if (!track.Confirmed || !AwarenessState.PassesFilter(track))
                {
                    continue;
                }
                if (track.IsPerson)
                {
                    people++;
                }
                else
                {
                    objects++;
                }
            }

            m_peopleValue.text = people.ToString();
            m_objectsValue.text = AwarenessState.PeopleOnly ? "-" : objects.ToString();
            m_inferenceValue.text = $"{m_pipeline.LastInferenceMs:0}<size=22> ms</size>";
            m_modelRate.text = $"Model runs: {m_pipeline.InferencesPerSecond:0.0}/s";
            m_fpsValue.text = $"{m_fps:0}";

            if (AwarenessState.Paused)
            {
                m_liveState.text = "<color=#FFC040>|| PAUSED</color>";
            }
            else
            {
                m_liveState.text = AwarenessState.SimulationActive ? "<color=#B0B0FF>SIMULATED</color>" : "<color=#50E080>LIVE</color>";
            }
            m_filterState.text = AwarenessState.PeopleOnly ? "People only: <b>ON</b>" : "People only: OFF";
            m_thresholdState.text = $"Threshold: <b>{AwarenessState.ConfidenceThreshold * 100f:0}%</b>";

            string status = GetStatusMessage();
            m_statusBackground.gameObject.SetActive(!string.IsNullOrEmpty(status));
            m_status.text = status;
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
                return "Spatial data permission needed for distance: enable in Settings, then restart";
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

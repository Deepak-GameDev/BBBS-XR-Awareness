using UnityEngine;
using UnityEngine.UI;

namespace BBB.Awareness
{
    /// <summary>
    /// The world-anchored marker for one tracked item:
    ///   - a dot exactly at the real object's 3D position
    ///   - a short stem up to a label: "PERSON #3" / "87%  ·  2.4 m"
    /// People: orange, bigger, with an extra ring. Objects: cyan, smaller.
    /// The label always faces the user and keeps a readable size at any distance.
    /// Fades in when it appears and fades out when the item is lost.
    /// </summary>
    public class MarkerView : MonoBehaviour
    {
        private const float FadeSpeed = 1f / 0.35f;     // full fade in 0.35 s
        private const float MetersPerUnitPerMeter = 0.00028f; // label size grows with distance -> constant apparent size

        public int TrackId { get; private set; }
        public bool IsFullyHidden => m_alpha <= 0.001f && m_targetAlpha <= 0f;

        private CanvasGroup m_group;
        private RectTransform m_canvasRect;
        private Text m_title;
        private Text m_detail;
        private Image m_accent;
        private Image m_dot;
        private Image m_ring;
        private Image m_stem;

        private float m_alpha;
        private float m_targetAlpha;
        private bool m_isPerson;
        private string m_lastDetail;

        public static MarkerView Create(Transform parent)
        {
            // Canvas pivot at the bottom-centre = the point that sits on the real object.
            var canvas = UiFactory.CreateWorldCanvas("Marker", parent, new Vector2(420, 300), 0.001f);
            var rt = (RectTransform)canvas.transform;
            rt.pivot = new Vector2(0.5f, 0f);

            var view = canvas.gameObject.AddComponent<MarkerView>();
            view.m_canvasRect = rt;
            view.m_group = canvas.GetComponent<CanvasGroup>();
            view.m_group.alpha = 0f;

            // Positions are relative to the canvas centre (canvas is 420 x 300, pivot at bottom).
            // Bottom of canvas is y = -150.
            view.m_ring = UiFactory.CreateImage("Ring", rt, new Vector2(0, -150), new Vector2(64, 64), Color.white, UiFactory.Circle(0.18f));
            view.m_dot = UiFactory.CreateImage("Dot", rt, new Vector2(0, -150), new Vector2(26, 26), Color.white, UiFactory.Circle());
            view.m_stem = UiFactory.CreateImage("Stem", rt, new Vector2(0, -85), new Vector2(4, 130), Color.white);

            var panel = UiFactory.CreateImage("Panel", rt, new Vector2(0, 45), new Vector2(380, 130), UiFactory.PanelColor);
            view.m_accent = UiFactory.CreateImage("Accent", panel.transform, new Vector2(-184, 0), new Vector2(12, 130), Color.white);
            view.m_title = UiFactory.CreateText("Title", panel.transform, new Vector2(10, 26), new Vector2(340, 56), 46, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            view.m_detail = UiFactory.CreateText("Detail", panel.transform, new Vector2(10, -30), new Vector2(340, 50), 38, TextAnchor.MiddleLeft, UiFactory.TextColor);

            canvas.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Attach this (pooled) marker to a track and style it.</summary>
        public void Bind(TrackedDetection track)
        {
            TrackId = track.Id;
            m_isPerson = track.IsPerson;
            m_alpha = 0f;
            m_group.alpha = 0f;
            m_targetAlpha = 1f;
            m_lastDetail = null;

            Color c = m_isPerson ? UiFactory.PersonColor : UiFactory.ObjectColor;
            m_dot.color = c;
            m_accent.color = c;
            m_stem.color = new Color(c.r, c.g, c.b, 0.8f);
            m_title.color = c;
            m_ring.color = c;
            m_ring.gameObject.SetActive(m_isPerson);       // people get an extra ring
            m_dot.rectTransform.sizeDelta = m_isPerson ? new Vector2(30, 30) : new Vector2(22, 22);
            m_title.text = $"{track.Label.ToUpperInvariant()}  #{track.Id}";

            transform.position = track.Position;
            gameObject.SetActive(true);
        }

        /// <summary>Show (fade in) or hide (fade out) the marker.</summary>
        public void SetVisible(bool visible) => m_targetAlpha = visible ? 1f : 0f;

        /// <summary>Called every frame while the track exists.</summary>
        public void UpdateFromTrack(TrackedDetection track, Transform head)
        {
            transform.position = track.Position; // tracker already smooths the position

            // Only rebuild the string when the shown values change (saves garbage/CPU).
            string detail = $"{Mathf.RoundToInt(track.Confidence * 100f)}%  ·  {track.Distance:0.0} m";
            if (detail != m_lastDetail)
            {
                m_detail.text = detail;
                m_lastDetail = detail;
            }
            Billboard(head);
        }

        /// <summary>Fade + billboard; also call for markers whose track is gone (fading out).</summary>
        public void Animate(Transform head, float deltaTime)
        {
            m_alpha = Mathf.MoveTowards(m_alpha, m_targetAlpha, FadeSpeed * deltaTime);
            m_group.alpha = m_alpha;
            Billboard(head);
        }

        public void Release()
        {
            m_targetAlpha = 0f;
            m_alpha = 0f;
            gameObject.SetActive(false);
        }

        private void Billboard(Transform head)
        {
            if (head == null)
            {
                return;
            }
            Vector3 toMarker = transform.position - head.position;
            float distance = toMarker.magnitude;
            if (distance < 0.01f)
            {
                return;
            }
            // UI is readable when its forward axis points away from the viewer.
            transform.rotation = Quaternion.LookRotation(toMarker, Vector3.up);

            // Scale with distance so the label keeps roughly the same size in the user's view.
            float scale = MetersPerUnitPerMeter * Mathf.Clamp(distance, 0.5f, 6f) * (m_isPerson ? 1.2f : 1f);
            m_canvasRect.localScale = Vector3.one * scale;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BBB.Awareness
{
    /// <summary>
    /// Top-down radar. The user is at the centre and "forward" (where the user looks) is always UP.
    /// Each tracked item is a dot placed by its bearing (angle) and range (distance) from the user.
    ///   - filled dot  = item is currently in the field of view
    ///   - hollow ring = item is outside the view (e.g. behind you) - its last known position
    ///   - items further than the radar range are pinned to the outer edge
    /// The two lines show the approximate camera field of view.
    /// </summary>
    public class RadarView : MonoBehaviour
    {
        public float RangeMeters = 6f;
        public float FovHalfAngle = 40f;     // approx. half horizontal FOV of the passthrough camera

        private RectTransform m_dotsRoot;
        private float m_radiusUnits;
        private AwarenessPipeline m_pipeline;
        private Transform m_head;
        private readonly List<Image> m_dots = new List<Image>();
        private Sprite m_filled;
        private Sprite m_hollow;

        /// <summary>Builds the radar UI inside the given parent (part of the HUD canvas).</summary>
        public static RadarView Create(RectTransform parent, Vector2 anchoredPos, float diameterUnits, AwarenessPipeline pipeline, Transform head)
        {
            var root = UiFactory.CreateRect("Radar", parent, anchoredPos, new Vector2(diameterUnits, diameterUnits), new Vector2(0.5f, 0.5f));
            var radar = root.gameObject.AddComponent<RadarView>();
            radar.m_pipeline = pipeline;
            radar.m_head = head;
            radar.m_radiusUnits = diameterUnits * 0.5f;
            radar.m_filled = UiFactory.Circle();
            radar.m_hollow = UiFactory.Circle(0.35f);
            radar.Build(root, diameterUnits);
            return radar;
        }

        private void Build(RectTransform root, float d)
        {
            // Background disc + range rings (2 m, 4 m, 6 m with the default range)
            UiFactory.CreateImage("Background", root, Vector2.zero, new Vector2(d, d), new Color(0.02f, 0.05f, 0.08f, 0.85f), UiFactory.Circle());
            var ringColor = new Color(0.4f, 0.8f, 1f, 0.35f);
            UiFactory.CreateImage("Ring3", root, Vector2.zero, new Vector2(d, d), ringColor, UiFactory.Circle(0.02f, 256));
            UiFactory.CreateImage("Ring2", root, Vector2.zero, new Vector2(d * 2f / 3f, d * 2f / 3f), ringColor, UiFactory.Circle(0.03f, 256));
            UiFactory.CreateImage("Ring1", root, Vector2.zero, new Vector2(d / 3f, d / 3f), ringColor, UiFactory.Circle(0.06f, 256));

            // Field-of-view lines: thin bars pivoting at the centre, rotated +/- FovHalfAngle
            CreateFovLine(root, FovHalfAngle);
            CreateFovLine(root, -FovHalfAngle);

            // The user (centre)
            UiFactory.CreateImage("User", root, Vector2.zero, new Vector2(14, 14), Color.white, UiFactory.Circle());

            var rangeLabel = UiFactory.CreateText("RangeLabel", root, new Vector2(0, -d * 0.5f - 12f), new Vector2(160, 30), 22, TextAnchor.MiddleCenter, UiFactory.DimTextColor);
            rangeLabel.text = $"{RangeMeters:0} m";

            m_dotsRoot = UiFactory.CreateRect("Dots", root, Vector2.zero, new Vector2(d, d), new Vector2(0.5f, 0.5f));
        }

        private void CreateFovLine(RectTransform root, float angle)
        {
            var line = UiFactory.CreateRect("FovLine", root, Vector2.zero, new Vector2(2f, m_radiusUnits), new Vector2(0.5f, 0f));
            var img = line.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.35f);
            img.raycastTarget = false;
            line.localRotation = Quaternion.Euler(0f, 0f, -angle); // UI rotates counter-clockwise for positive z
        }

        private void LateUpdate()
        {
            if (m_pipeline == null || m_head == null)
            {
                return;
            }

            // Horizontal forward direction of the user (ignore looking up/down).
            Vector3 forward = Vector3.ProjectOnPlane(m_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(m_head.up, Vector3.up); // looking straight down/up
            }
            forward.Normalize();

            int used = 0;
            foreach (var track in m_pipeline.Tracker.Tracks)
            {
                if (!track.Confirmed || !AwarenessState.PassesFilter(track))
                {
                    continue;
                }

                Vector3 offset = track.Position - m_head.position;
                offset.y = 0f;
                float range = offset.magnitude;
                float bearing = Vector3.SignedAngle(forward, offset, Vector3.up); // + = to the right

                float r = Mathf.Min(range / RangeMeters, 1f) * (m_radiusUnits - 8f);
                float rad = bearing * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Sin(rad) * r, Mathf.Cos(rad) * r);

                var dot = GetDot(used++);
                dot.rectTransform.anchoredPosition = pos;
                dot.sprite = track.InView ? m_filled : m_hollow;
                dot.color = track.IsPerson ? UiFactory.PersonColor : UiFactory.ObjectColor;
                float size = track.IsPerson ? 22f : 15f;
                dot.rectTransform.sizeDelta = new Vector2(size, size);
            }

            // Hide unused dots
            for (int i = used; i < m_dots.Count; i++)
            {
                if (m_dots[i].enabled)
                {
                    m_dots[i].enabled = false;
                }
            }
        }

        private Image GetDot(int index)
        {
            while (m_dots.Count <= index)
            {
                m_dots.Add(UiFactory.CreateImage("Dot", m_dotsRoot, Vector2.zero, new Vector2(15, 15), Color.white, m_filled));
            }
            var dot = m_dots[index];
            dot.enabled = true;
            return dot;
        }
    }
}

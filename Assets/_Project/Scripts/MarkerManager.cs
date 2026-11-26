using System.Collections.Generic;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// Keeps one MarkerView per confirmed track. Markers are pooled (re-used) instead of
    /// created/destroyed every time, which avoids garbage-collection hitches in VR.
    /// When a track disappears its marker fades out first, then goes back to the pool.
    /// </summary>
    public class MarkerManager : MonoBehaviour
    {
        private AwarenessPipeline m_pipeline;
        private Transform m_head;
        private Transform m_root;

        private readonly Dictionary<int, MarkerView> m_active = new Dictionary<int, MarkerView>();
        private readonly List<MarkerView> m_fadingOut = new List<MarkerView>();
        private readonly Stack<MarkerView> m_pool = new Stack<MarkerView>();
        private readonly HashSet<int> m_seenThisFrame = new HashSet<int>();
        private readonly List<int> m_toRemove = new List<int>();

        public void Init(AwarenessPipeline pipeline, Transform head)
        {
            m_pipeline = pipeline;
            m_head = head;
            m_root = new GameObject("Markers").transform; // world space, not parented to the head
        }

        private void LateUpdate()
        {
            if (m_pipeline == null)
            {
                return;
            }
            float dt = Time.deltaTime;
            m_seenThisFrame.Clear();

            // Create / update markers for confirmed tracks
            foreach (var track in m_pipeline.Tracker.Tracks)
            {
                if (!track.Confirmed)
                {
                    continue;
                }
                m_seenThisFrame.Add(track.Id);

                if (!m_active.TryGetValue(track.Id, out var marker))
                {
                    marker = GetFromPool();
                    marker.Bind(track);
                    m_active.Add(track.Id, marker);
                }
                marker.SetVisible(AwarenessState.PassesFilter(track));
                marker.UpdateFromTrack(track, m_head);
                marker.Animate(m_head, dt);
            }

            // Tracks that no longer exist -> start fading their markers out
            m_toRemove.Clear();
            foreach (var pair in m_active)
            {
                if (!m_seenThisFrame.Contains(pair.Key))
                {
                    m_toRemove.Add(pair.Key);
                }
            }
            foreach (int id in m_toRemove)
            {
                var marker = m_active[id];
                m_active.Remove(id);
                marker.SetVisible(false);
                m_fadingOut.Add(marker);
            }

            // Animate fading markers and return them to the pool when invisible
            for (int i = m_fadingOut.Count - 1; i >= 0; i--)
            {
                var marker = m_fadingOut[i];
                marker.Animate(m_head, dt);
                if (marker.IsFullyHidden)
                {
                    marker.Release();
                    m_pool.Push(marker);
                    m_fadingOut.RemoveAt(i);
                }
            }
        }

        private MarkerView GetFromPool() => m_pool.Count > 0 ? m_pool.Pop() : MarkerView.Create(m_root);
    }
}

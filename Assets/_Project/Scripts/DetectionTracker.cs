using System.Collections.Generic;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>One detection already placed in 3D, for a single inference frame.</summary>
    public struct Detection3D
    {
        public int ClassId;
        public string Label;
        public bool IsPerson;
        public float Confidence;     // 0..1
        public Vector3 Position;     // world space
    }

    /// <summary>A real-world item we are following over time. Has a stable Id.</summary>
    public class TrackedDetection
    {
        public int Id;
        public int ClassId;
        public string Label;
        public bool IsPerson;

        public Vector3 Position;     // smoothed world position
        public float Confidence;     // smoothed 0..1
        public float Distance;       // metres from the user's head (updated every frame)
        public bool InView;          // inside the user's field of view right now?

        public int Hits;             // how many frames it was detected in
        public bool Confirmed;       // only confirmed tracks are shown (filters one-frame ghosts)
        public float FirstSeen;
        public float LastSeen;
        public float InViewSince;
    }

    /// <summary>
    /// Keeps a stable identity for each detected item across frames.
    ///
    /// Every inference result is matched to existing tracks:
    ///  1. Candidate pairs = same class AND within a distance gate (gate grows with distance,
    ///     because depth is noisier far away).
    ///  2. Greedy matching: closest pairs first, each track/detection used once.
    ///  3. Matched tracks are smoothed (exponential moving average) -> no jitter.
    ///  4. Unmatched detections start new "tentative" tracks; they become visible only after
    ///     being seen twice -> no flicker from one-frame false positives.
    ///  5. Removal: a track disappears only if it SHOULD be visible (it is in view) but has not
    ///     been detected for a while. Items outside the view are remembered longer, so the radar
    ///     can show things behind the user.
    /// </summary>
    public class DetectionTracker
    {
        // Tuning values (explained in the README)
        public float MatchGateMeters = 0.45f;        // base max distance to match a detection to a track
        public float MatchGatePerMeter = 0.12f;      // extra gate per metre of distance from the user
        // Adaptive smoothing: small changes are mostly depth/box noise -> damp strongly;
        // big changes mean the object really moved -> follow quickly.
        public float NoiseSmoothing = 0.12f;         // used when the change is below NoiseBandMeters
        public float MotionSmoothing = 0.45f;        // used when the change is clearly real movement
        public float NoiseBandMeters = 0.06f;
        public float MotionBandMeters = 0.25f;
        public float ConfidenceSmoothing = 0.30f;
        public int HitsToConfirm = 2;
        public float TentativeTimeout = 0.8f;        // unconfirmed tracks die after this many seconds
        public float LostInViewTimeout = 1.2f;       // in view but not detected for this long -> remove
        public float OutOfViewMemory = 20f;          // out of view: remember this long
        public float ViewportMargin = 0.12f;         // ignore the outer edges of the view

        private readonly List<TrackedDetection> m_tracks = new List<TrackedDetection>();
        private readonly List<(float cost, int track, int detection)> m_pairs = new List<(float, int, int)>();
        private readonly List<bool> m_trackUsed = new List<bool>();
        private readonly List<bool> m_detectionUsed = new List<bool>();
        private int m_nextId = 1;

        public IReadOnlyList<TrackedDetection> Tracks => m_tracks;

        /// <summary>Feed the detections from one inference frame.</summary>
        public void Integrate(List<Detection3D> detections, Vector3 headPosition, float now)
        {
            // 1. Build all candidate pairs
            m_pairs.Clear();
            for (int t = 0; t < m_tracks.Count; t++)
            {
                var track = m_tracks[t];
                for (int d = 0; d < detections.Count; d++)
                {
                    var det = detections[d];
                    if (det.ClassId != track.ClassId)
                    {
                        continue;
                    }
                    float range = Vector3.Distance(headPosition, det.Position);
                    float gate = MatchGateMeters + MatchGatePerMeter * range;
                    float dist = Vector3.Distance(track.Position, det.Position);
                    if (dist <= gate)
                    {
                        m_pairs.Add((dist, t, d));
                    }
                }
            }

            // 2. Greedy assignment, closest first
            m_pairs.Sort((a, b) => a.cost.CompareTo(b.cost));
            ResetFlags(m_trackUsed, m_tracks.Count);
            ResetFlags(m_detectionUsed, detections.Count);

            foreach (var pair in m_pairs)
            {
                if (m_trackUsed[pair.track] || m_detectionUsed[pair.detection])
                {
                    continue;
                }
                m_trackUsed[pair.track] = true;
                m_detectionUsed[pair.detection] = true;

                // 3. Update the matched track with smoothing
                var track = m_tracks[pair.track];
                var det = detections[pair.detection];
                float change = Vector3.Distance(track.Position, det.Position);
                float k = Mathf.InverseLerp(NoiseBandMeters, MotionBandMeters, change);
                float smoothing = Mathf.Lerp(NoiseSmoothing, MotionSmoothing, k);
                track.Position = Vector3.Lerp(track.Position, det.Position, smoothing);
                track.Confidence = Mathf.Lerp(track.Confidence, det.Confidence, ConfidenceSmoothing);
                track.Hits++;
                track.LastSeen = now;
                if (track.Hits >= HitsToConfirm)
                {
                    track.Confirmed = true;
                }
            }

            // 4. Unmatched detections -> new tentative tracks
            for (int d = 0; d < detections.Count; d++)
            {
                if (m_detectionUsed[d])
                {
                    continue;
                }
                var det = detections[d];
                m_tracks.Add(new TrackedDetection
                {
                    Id = m_nextId++,
                    ClassId = det.ClassId,
                    Label = det.Label,
                    IsPerson = det.IsPerson,
                    Position = det.Position,
                    Confidence = det.Confidence,
                    Distance = Vector3.Distance(headPosition, det.Position),
                    Hits = 1,
                    Confirmed = HitsToConfirm <= 1,
                    FirstSeen = now,
                    LastSeen = now,
                    InViewSince = now,
                    InView = true,
                });
            }
        }

        /// <summary>Call every frame: updates distance / in-view state and removes stale tracks.</summary>
        /// <param name="frozen">When paused we don't age tracks, so nothing disappears while paused.</param>
        public void Tick(Camera viewCamera, float now, float deltaTime, bool frozen)
        {
            if (viewCamera == null)
            {
                return;
            }
            Vector3 head = viewCamera.transform.position;

            for (int i = m_tracks.Count - 1; i >= 0; i--)
            {
                var track = m_tracks[i];
                track.Distance = Vector3.Distance(head, track.Position);

                bool inView = IsInView(viewCamera, track.Position);
                if (inView && !track.InView)
                {
                    track.InViewSince = now; // just came back into view: give it time to be re-detected
                }
                track.InView = inView;

                if (frozen)
                {
                    // Shift timestamps forward so pausing doesn't count as "not seen".
                    track.LastSeen += deltaTime;
                    track.FirstSeen += deltaTime;
                    track.InViewSince += deltaTime;
                    continue;
                }

                if (ShouldRemove(track, now))
                {
                    m_tracks.RemoveAt(i);
                }
            }
        }

        public void Clear() => m_tracks.Clear();

        private bool ShouldRemove(TrackedDetection track, float now)
        {
            if (!track.Confirmed)
            {
                return now - track.FirstSeen > TentativeTimeout;
            }
            if (track.InView)
            {
                // Time it has been visible-but-undetected
                float unseenFor = now - Mathf.Max(track.LastSeen, track.InViewSince);
                return unseenFor > LostInViewTimeout;
            }
            return now - track.LastSeen > OutOfViewMemory;
        }

        private bool IsInView(Camera cam, Vector3 worldPosition)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPosition);
            return vp.z > 0.2f
                && vp.x > ViewportMargin && vp.x < 1f - ViewportMargin
                && vp.y > ViewportMargin && vp.y < 1f - ViewportMargin;
        }

        private static void ResetFlags(List<bool> flags, int count)
        {
            flags.Clear();
            for (int i = 0; i < count; i++)
            {
                flags.Add(false);
            }
        }
    }
}

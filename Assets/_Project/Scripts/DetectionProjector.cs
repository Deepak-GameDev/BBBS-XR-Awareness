using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// Turns a 2D bounding box from the model into a 3D point on the real object.
    ///
    /// How: we cast several rays from the passthrough camera through points inside the box
    /// and hit them against the real environment (MRUK depth raycast). We take the MEDIAN
    /// distance of the hits. A single ray through the box centre often misses the object
    /// (e.g. through the gap between a chair's legs) and hits the wall behind it; the median
    /// of 5 rays ignores those outliers.
    ///
    /// The camera pose used is the pose at the moment the frame was captured (provided by the
    /// sample), so markers stay correct even if the head moved while the model was running.
    /// </summary>
    public class DetectionProjector
    {
        private readonly PassthroughCameraAccess m_cameraAccess;
        private readonly EnvironmentRaycastManager m_raycastManager;
        private readonly List<float> m_hitDistances = new List<float>(8);

        // Sample points inside the box, in box-relative coordinates (0..1, y down like the image).
        private static readonly Vector2[] s_objectSamplePoints =
        {
            new Vector2(0.50f, 0.50f), new Vector2(0.35f, 0.50f), new Vector2(0.65f, 0.50f),
            new Vector2(0.50f, 0.35f), new Vector2(0.50f, 0.65f),
        };

        // For people we aim at the torso (upper-middle of the box), which is more reliable
        // than the exact centre (often between the legs or at the belt).
        private static readonly Vector2[] s_personSamplePoints =
        {
            new Vector2(0.50f, 0.35f), new Vector2(0.40f, 0.35f), new Vector2(0.60f, 0.35f),
            new Vector2(0.50f, 0.25f), new Vector2(0.50f, 0.45f),
        };

        public DetectionProjector(PassthroughCameraAccess cameraAccess, EnvironmentRaycastManager raycastManager)
        {
            m_cameraAccess = cameraAccess;
            m_raycastManager = raycastManager;
        }

        /// <param name="box">Pixel box in model-input space: (x1, y1, x2, y2), y pointing down.</param>
        /// <param name="inputSize">Model input size in pixels (e.g. 640x640).</param>
        /// <param name="cameraPose">Camera pose at capture time.</param>
        public bool TryProject(Vector4 box, Vector2 inputSize, Pose cameraPose, bool isPerson, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (m_cameraAccess == null || m_raycastManager == null || !EnvironmentRaycastManager.IsSupported)
            {
                return false;
            }

            var points = isPerson ? s_personSamplePoints : s_objectSamplePoints;
            m_hitDistances.Clear();
            Ray mainRay = default;

            for (int i = 0; i < points.Length; i++)
            {
                // Box-relative point -> pixel -> normalised image coords (0..1, y down)
                float px = Mathf.Lerp(box.x, box.z, points[i].x) / inputSize.x;
                float py = Mathf.Lerp(box.y, box.w, points[i].y) / inputSize.y;

                // Viewport coords have y pointing UP, image coords have y pointing DOWN -> flip.
                var ray = m_cameraAccess.ViewportPointToRay(new Vector2(px, 1f - py), cameraPose);
                if (i == 0)
                {
                    mainRay = ray;
                }

                if (m_raycastManager.Raycast(ray, out var hit))
                {
                    m_hitDistances.Add(Vector3.Distance(ray.origin, hit.point));
                }
            }

            // Need at least 2 agreeing rays to trust the result.
            if (m_hitDistances.Count < 2)
            {
                return false;
            }

            m_hitDistances.Sort();
            float median = m_hitDistances[m_hitDistances.Count / 2];
            worldPosition = mainRay.GetPoint(median);
            return true;
        }
    }
}

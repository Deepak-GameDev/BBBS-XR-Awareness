using Meta.XR;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// The ONLY component you add to the scene. On start it finds the sample's objects
    /// (camera access, depth raycaster, start menu, head camera) and creates all awareness
    /// systems: pipeline, markers, HUD + radar, controls, and (in the Editor) the simulator.
    /// Fields left empty in the Inspector are found automatically.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class AwarenessBootstrap : MonoBehaviour
    {
        [Header("Optional - found automatically if empty")]
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private EnvironmentRaycastManager m_raycastManager;
        [SerializeField] private DetectionUiMenuManager m_menu;
        [SerializeField] private Camera m_headCamera;

        [Header("Testing")]
        [Tooltip("In the Unity Editor there is no passthrough camera, so use fake detections instead.")]
        [SerializeField] private bool m_useSimulatorInEditor = true;

        private void Awake()
        {
            FindReferences();

            var pipeline = gameObject.AddComponent<AwarenessPipeline>();
            pipeline.Init(m_headCamera, m_cameraAccess, m_raycastManager);

            gameObject.AddComponent<MarkerManager>().Init(pipeline, m_headCamera.transform);
            gameObject.AddComponent<HudPresenter>().Init(pipeline, m_headCamera.transform, m_cameraAccess, m_menu);
            gameObject.AddComponent<AwarenessControls>().Init(m_menu);

            if (Application.isEditor && m_useSimulatorInEditor)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                gameObject.AddComponent<DetectionSimulator>().Init(pipeline, m_headCamera, rig != null ? rig.transform : null);
            }
        }

        private void FindReferences()
        {
            if (m_cameraAccess == null) m_cameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
            if (m_raycastManager == null) m_raycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (m_menu == null) m_menu = FindFirstObjectByType<DetectionUiMenuManager>();

            if (m_headCamera == null)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null && rig.centerEyeAnchor != null)
                {
                    m_headCamera = rig.centerEyeAnchor.GetComponent<Camera>();
                }
                if (m_headCamera == null)
                {
                    m_headCamera = Camera.main;
                }
            }

            if (m_cameraAccess == null) Debug.LogWarning("[Awareness] PassthroughCameraAccess not found in scene.");
            if (m_raycastManager == null) Debug.LogWarning("[Awareness] EnvironmentRaycastManager not found: distances will not work.");
            if (m_headCamera == null) Debug.LogError("[Awareness] No head camera found.");
        }
    }
}

// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionUiMenuManager : MonoBehaviour
    {
        [Header("Ui elements ref.")]
        [SerializeField] private GameObject m_loadingPanel;
        [SerializeField] private GameObject m_initialPanel;
        [SerializeField] private GameObject m_noPermissionPanel;
        [SerializeField] internal Text m_labelInformation;

        public bool IsInputActive { get; set; } = false;

        public UnityEvent<bool> OnPause;

        // [BBB] Start detection automatically once permissions are granted (no "press A" step),
        // and hide the sample's menu/info panel afterwards: our HUD replaces it.
        [Header("[BBB] Awareness integration")]
        [SerializeField] private bool m_autoStart = true;
        [SerializeField] private bool m_hidePanelsAfterStart = true;

        private bool m_initialMenu;

        // start menu
        private int m_objectsDetected = 0;
        private int m_objectsIdentified = 0;

        // pause menu
        public bool IsPaused { get; private set; } = true;

        #region Unity Functions
        private IEnumerator Start()
        {
            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(false);
            m_loadingPanel.SetActive(false);

            // Wait for permissions
            OnNoPermissionMenu();
            while (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene) || !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            {
                yield return null;
            }
            if (m_autoStart) // [BBB]
            {
                OnPauseMenu(false);
                yield break;
            }
            OnInitialMenu();
        }

        private void Update()
        {
            if (!IsInputActive)
                return;

            if (m_initialMenu)
            {
                InitialMenuUpdate();
            }
        }
        #endregion

        #region Ui state: No permissions Menu
        private void OnNoPermissionMenu()
        {
            m_initialMenu = false;
            IsPaused = true;
            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(true);
        }
        #endregion

        #region Ui state: Initial Menu

        private void OnInitialMenu()
        {
            m_initialMenu = true;
            IsPaused = true;
            m_initialPanel.SetActive(true);
            m_noPermissionPanel.SetActive(false);
        }

        private void InitialMenuUpdate()
        {
            if (InputManager.IsButtonADownOrPinchStarted())
            {
                OnPauseMenu(false);
            }
        }

        private void OnPauseMenu(bool visible)
        {
            m_initialMenu = false;
            IsPaused = visible;

            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(false);

            OnPause?.Invoke(visible);

            // [BBB] Hide the sample's floating info/menu panel once detection runs.
            // Only the info text's own panel is hidden (not the whole canvas, which may hold other sample objects).
            if (!visible && m_hidePanelsAfterStart && m_labelInformation != null)
            {
                var infoPanel = m_labelInformation.transform.parent;
                bool parentIsCanvas = infoPanel == null || infoPanel.GetComponent<Canvas>() != null;
                (parentIsCanvas ? m_labelInformation.gameObject : infoPanel.gameObject).SetActive(false);
            }
        }
        #endregion

        #region Ui state: detection information
        private void UpdateLabelInformation()
        {
            m_labelInformation.text = $"Unity Sentis version: 2.1.3\nAI model: Yolo\nDetecting objects: {m_objectsDetected}\nObjects identified: {m_objectsIdentified}";
        }

        public void OnObjectsDetected(int objects)
        {
            m_objectsDetected = objects;
            UpdateLabelInformation();
        }

        public void OnObjectsIndentified(int objects)
        {
            if (objects < 0)
            {
                // reset the counter
                m_objectsIdentified = 0;
            }
            else
            {
                m_objectsIdentified += objects;
            }
            UpdateLabelInformation();
        }
        #endregion
    }
}

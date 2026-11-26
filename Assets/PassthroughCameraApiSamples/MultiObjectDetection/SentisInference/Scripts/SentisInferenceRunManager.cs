// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Meta.XR;
using Meta.XR.Samples;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceRunManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;
        [SerializeField] private DetectionManager m_detectionManager;

        [Header("Sentis Model config")]
        [SerializeField] private BackendType m_backend = BackendType.CPU;
        [SerializeField] private ModelAsset m_sentisModel;
        [SerializeField] private TextAsset m_labelsAsset;
        [SerializeField, Range(0, 1)] private float m_iouThreshold = 0.6f;
        [SerializeField, Range(0, 1)] private float m_scoreThreshold = 0.23f;

        [Header("UI display references")]
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("[Editor Only] Convert to Sentis")]
        public ModelAsset OnnxModel;
        [Space(40)]

        // ---------------------------------------------------------------------------------
        // [BBB] Hooks added for the Situational Awareness layer (Assets/_Project).
        // The sample code cannot reference our scripts directly, so it exposes static
        // hooks that our code subscribes to / writes to.
        // ---------------------------------------------------------------------------------
        [Header("[BBB] Awareness integration")]
        [SerializeField] private bool m_drawSampleBoxes = false; // the sample's own 2D boxes (off: we draw our own markers)

        /// <summary>Raised after every inference: detections (pixel boxes in model-input space + score), model input size, camera pose AT CAPTURE TIME, inference latency in ms.</summary>
        public static event System.Action<IReadOnlyList<(int classId, Vector4 boundingBox, float score)>, Vector2Int, Pose, float> OnInferenceCompleted;
        /// <summary>Set by our controls to pause/resume detection.</summary>
        public static bool ExternalPause;
        /// <summary>Confidence threshold set by our controls. Negative = use the inspector value.</summary>
        public static float ScoreThresholdOverride = -1f;
        /// <summary>Class names loaded from the labels file (index = class id).</summary>
        public static string[] Labels { get; private set; } = new string[0];

        private Worker m_engine;
        private Vector2Int m_inputSize;
        private readonly List<(int classId, Vector4 boundingBox, float score)> m_detections = new List<(int classId, Vector4 boundingBox, float score)>(); // [BBB] added score
        private readonly List<(int classId, Vector4 boundingBox)> m_detectionsForSampleUi = new List<(int classId, Vector4 boundingBox)>(); // [BBB]

        private void Awake()
        {
            var model = ModelLoader.Load(m_sentisModel);
            var inputShape = model.inputs[0].shape;
            m_inputSize = new Vector2Int(inputShape.Get(2), inputShape.Get(3));
            m_engine = new Worker(model, m_backend);
        }

        private IEnumerator Start()
        {
            m_uiInference.SetLabels(m_labelsAsset);

            // [BBB] Parse labels once for our code (trim '\r' left by Windows line endings).
            var rawLabels = m_labelsAsset.text.Split('\n');
            Labels = new string[rawLabels.Length];
            for (int i = 0; i < rawLabels.Length; i++)
            {
                Labels[i] = rawLabels[i].Trim();
            }

            while (true)
            {
                while (m_uiMenuManager.IsPaused || ExternalPause) // [BBB] also honour our pause button
                {
                    yield return null;
                }
                yield return RunInference();
            }
        }

        private void OnDestroy()
        {
            if (m_engine == null) // [BBB] Awake failed (e.g. model missing) - nothing to clean up
            {
                return;
            }
            m_engine.PeekOutput(0)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(1)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(2)?.CompleteAllPendingOperations();
            m_engine.Dispose();
        }

        internal static void PreloadModel(ModelAsset modelAsset)
        {
            // Load model
            var model = ModelLoader.Load(modelAsset);
            var inputShape = model.inputs[0].shape;

            // Create engine to run model
            using var worker = new Worker(model, BackendType.CPU);

            // Run inference with an empty image to load the model in the memory. The first inference blocks the main thread for a long time, so we're doing it on the app launch
            Texture tempTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var textureTransform = new TextureTransform().SetDimensions(tempTexture.width, tempTexture.height, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, inputShape.Get(2), inputShape.Get(3)));
            TextureConverter.ToTensor(tempTexture, input, textureTransform);
            worker.Schedule(input);

            // Complete the inference immediately and destroy the temporary texture
            worker.PeekOutput(0).CompleteAllPendingOperations();
            worker.PeekOutput(1).CompleteAllPendingOperations();
            worker.PeekOutput(2).CompleteAllPendingOperations();
            Destroy(tempTexture);
        }

        private IEnumerator RunInference()
        {
            if (!m_cameraAccess.IsPlaying)
            {
                yield break;
            }

            [DllImport("OVRPlugin", CallingConvention = CallingConvention.Cdecl)]
            static extern OVRPlugin.Result ovrp_GetNodePoseStateAtTime(double time, OVRPlugin.Node nodeId, out OVRPlugin.PoseStatef nodePoseState);
            if (!ovrp_GetNodePoseStateAtTime(OVRPlugin.GetTimeInSeconds(), OVRPlugin.Node.Head, out _).IsSuccess())
            {
                Debug.Log("ovrp_GetNodePoseStateAtTime failed, which means 'm_cameraAccess.GetCameraPose()' is not reliable, skipping.");
                yield break;
            }

            var cachedCameraPose = m_cameraAccess.GetCameraPose();

            // Update Capture data
            Texture targetTexture = m_cameraAccess.GetTexture();

            // Convert the texture to a Tensor and schedule the inference
            var textureTransform = new TextureTransform().SetDimensions(targetTexture.width, targetTexture.height, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.x, m_inputSize.y));
            TextureConverter.ToTensor(targetTexture, input, textureTransform);

            // Schedule all model layers
            float inferenceStartTime = Time.realtimeSinceStartup; // [BBB] measure inference latency
            m_engine.Schedule(input);

            // Get the results. ReadbackAndCloneAsync waits for all layers to complete before returning the result
            var boxesAwaiter = (m_engine.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            while (!boxesAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var boxes = boxesAwaiter.GetResult();
            if (boxes.shape[0] == 0)
            {
                // [BBB] Still report "nothing found" so the HUD's inference time keeps updating.
                m_detections.Clear();
                if (m_cameraAccess.IsPlaying && !ExternalPause)
                {
                    OnInferenceCompleted?.Invoke(m_detections, m_inputSize, cachedCameraPose, (Time.realtimeSinceStartup - inferenceStartTime) * 1000f);
                }
                yield break;
            }

            var classIDsAwaiter = (m_engine.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync().GetAwaiter();
            while (!classIDsAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var classIDs = classIDsAwaiter.GetResult();
            if (classIDs.shape[0] == 0)
            {
                Debug.LogError("classIDs.shape[0] == 0");
                yield break;
            }

            var scoresAwaiter = (m_engine.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            while (!scoresAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var scores = scoresAwaiter.GetResult();
            if (scores.shape[0] == 0)
            {
                Debug.LogError("scores.shape[0] == 0");
                yield break;
            }

            float inferenceMs = (Time.realtimeSinceStartup - inferenceStartTime) * 1000f; // [BBB]
            float scoreThreshold = ScoreThresholdOverride >= 0f ? ScoreThresholdOverride : m_scoreThreshold; // [BBB]
            NonMaxSuppression(m_detections, boxes, classIDs, scores, m_iouThreshold, scoreThreshold);

            // [BBB] Hand the results to the awareness layer. Done BEFORE the spatial-anchor check
            // because our markers live in world space and don't depend on the sample's anchor.
            if (m_cameraAccess.IsPlaying && !ExternalPause)
            {
                OnInferenceCompleted?.Invoke(m_detections, m_inputSize, cachedCameraPose, inferenceMs);
            }

            if (!m_drawSampleBoxes) // [BBB]
            {
                yield break;
            }

            // Checking if spatial anchor is tracked ensures bounding boxes are placed at correct world space positIons.
            if (!m_cameraAccess.IsPlaying || m_detectionManager.m_spatialAnchor == null || !m_detectionManager.m_spatialAnchor.IsTracked)
            {
                yield break;
            }

            // Update UI.
            m_detectionsForSampleUi.Clear(); // [BBB] sample UI expects the list without scores
            foreach (var d in m_detections)
            {
                m_detectionsForSampleUi.Add((d.classId, d.boundingBox));
            }
            m_uiInference.DrawUIBoxes(m_detectionsForSampleUi, m_inputSize, cachedCameraPose);
        }

        // [BBB] Changed: keeps the score, and suppression is class-aware. The original suppressed
        // overlapping boxes regardless of class, so a person sitting on a chair could disappear.
        // Now: same class -> normal NMS; different classes -> only suppressed when almost identical
        // (IoU > 0.85), and a person box is never suppressed by a non-person box (people are priority).
        private static void NonMaxSuppression(List<(int classId, Vector4 boundingBox, float score)> outDetections, Tensor<float> boxes, Tensor<int> classIDs, Tensor<float> scores, float iouThreshold, float scoreThreshold)
        {
            outDetections.Clear();

            // Filter by score threshold first
            List<int> filteredIndices = new List<int>();
            NativeArray<float>.ReadOnly scoresArray = scores.AsReadOnlyNativeArray();
            for (int i = 0; i < scoresArray.Length; i++)
            {
                if (scoresArray[i] >= scoreThreshold)
                {
                    filteredIndices.Add(i);
                }
            }

            if (filteredIndices.Count == 0)
            {
                return;
            }

            // Sort filtered indices by scores in descending order
            filteredIndices.Sort((a, b) => scoresArray[b].CompareTo(scoresArray[a]));

            // Apply NMS algorithm
            bool[] suppressed = new bool[filteredIndices.Count];
            for (int i = 0; i < filteredIndices.Count; i++)
            {
                if (suppressed[i])
                    continue;

                int idx = filteredIndices[i];

                // Add this detection to results
                outDetections.Add((classIDs[idx], GetBox(idx), scoresArray[idx])); // [BBB] keep score

                // [BBB] Class-aware suppression (see comment above the method)
                bool keptIsPerson = IsPersonClass(classIDs[idx]);
                for (int j = i + 1; j < filteredIndices.Count; j++)
                {
                    if (suppressed[j])
                        continue;

                    int jdx = filteredIndices[j];
                    bool sameClass = classIDs[jdx] == classIDs[idx];
                    if (!sameClass && IsPersonClass(classIDs[jdx]) && !keptIsPerson)
                        continue; // never let an object hide a person

                    float iou = CalculateIoU(GetBox(idx), GetBox(jdx));
                    float threshold = sameClass ? iouThreshold : 0.85f;
                    if (iou > threshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            Vector4 GetBox(int i) => new Vector4(boxes[i, 0], boxes[i, 1], boxes[i, 2], boxes[i, 3]);
        }

        // [BBB]
        private static bool IsPersonClass(int classId) => classId >= 0 && classId < Labels.Length && Labels[classId] == "person";

        internal static float CalculateIoU(Vector4 boxA, Vector4 boxB)
        {
            // Boxes are in format (topLeftX, topLeftY, bottomRightX, bottomRightY)
            // Calculate intersection coordinates
            float x1 = Mathf.Max(boxA.x, boxB.x);
            float y1 = Mathf.Max(boxA.y, boxB.y);
            float x2 = Mathf.Min(boxA.z, boxB.z);
            float y2 = Mathf.Min(boxA.w, boxB.w);

            // Calculate intersection area
            float intersectionWidth = Mathf.Max(0, x2 - x1);
            float intersectionHeight = Mathf.Max(0, y2 - y1);
            float intersectionArea = intersectionWidth * intersectionHeight;

            // Calculate individual box areas
            float boxAArea = (boxA.z - boxA.x) * (boxA.w - boxA.y);
            float boxBArea = (boxB.z - boxB.x) * (boxB.w - boxB.y);

            // Calculate union area
            float unionArea = boxAArea + boxBArea - intersectionArea;

            // Return IoU (Intersection over Union)
            if (unionArea == 0)
                return 0;

            return intersectionArea / unionArea;
        }
    }
}

# Situational Awareness for Meta Quest 3

A mixed-reality app for **Meta Quest 3** that detects **people and everyday objects** in real time, **entirely on the headset**, and shows them as world-anchored markers in passthrough, with a head-locked HUD and a top-down radar.

Each marker shows **what** the item is, **how confident** the model is, and **how far away** it is. People are highlighted differently from objects. The radar also shows tracked items that are **outside the user's field of view**.

> Demo video: `[https://youtu.be/FYmLhZDalBQ]` · APK: see **Releases** on this repository

---

## Features

| Requirement | How it is done |
|---|---|
| Camera access + permissions | MRUK `PassthroughCameraAccess`. If camera or spatial-data permission is missing, the HUD shows a clear message (no crash). |
| On-device detection | YOLO (from Meta's sample) running in **Unity Inference Engine** on the headset. No network. |
| People as priority | Class-aware NMS: an object box can **never** suppress a person box; people are drawn larger, in orange, with an extra ring; people-only filter. |
| World-anchored markers | 2D box → 5 depth raycasts inside the box (MRUK `EnvironmentRaycastManager`) → **median** hit distance → 3D point. Uses the camera pose **at capture time**. |
| Labels | `PERSON #3` / `87% · 2.4 m`. Billboarded, constant apparent size. |
| Tracking | Own tracker: stable IDs, greedy matching (class + distance gate), adaptive smoothing, confirmation after 2 hits, fade-out when lost. |
| HUD | Head-locked (smoothed), low in the view: people, objects, inference time, model rate, FPS, state. |
| Radar | Bearing + range relative to the user (forward = up), 6 m range, FOV lines; filled dot = in view, hollow ring = outside view. |
| Controls | **A** pause/resume · **B** people-only · **Right stick ↑/↓** threshold ±5% (10–90%). Haptic feedback. |
| Performance | 72 FPS measured on Quest 3. Inference is asynchronous and never blocks rendering. |

---

## Requirements & versions

| Item | Version |
|---|---|
| Unity | **6000.3.14f1** (Unity 6.3 LTS) with Android Build Support (OpenJDK, SDK, NDK) |
| Meta MR Utility Kit (MRUK) | `com.meta.xr.mrutilitykit` **85.0.0** (pulls in Meta XR Core SDK) |
| Unity Inference Engine | `com.unity.ai.inference` **2.6.1** |
| OpenXR / XR Management | `com.unity.xr.openxr` 1.16.1 · `com.unity.xr.management` 4.5.4 |
| Device | Meta Quest 3 / 3S, Horizon OS **v81+** (`PassthroughCameraAccess` requires v81) |
| Model | YOLOv9 (tiny) exported to `.sentis`, from Meta's sample (stored with **Git LFS**) |

## Setup & build

1. Install **Git LFS** (`git lfs install`), then clone this repository. Check that the model files are real (several MB), not LFS pointers: if they are ~1 KB, run `git lfs pull`.
2. Open the project in **Unity 6000.3.14f1** (Unity Hub → Add project from disk).
3. **File → Build Profiles → Android → Switch Platform.**
4. Open the scene `Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity`.
5. Headset in **Developer Mode**, connected by USB, **Allow USB debugging**.
6. **Build Profiles → Run Device: Quest 3 → Build And Run**.
7. On first launch, **allow** camera and spatial data permissions. Detection starts automatically.

**Install the prebuilt APK instead:** download `app.apk` from Releases, then use sidequest to upload the .apk`. Launch from *Library → Unknown Sources*.

**Test in the Editor (no headset):** press Play. A built-in **detection simulator** creates fake people/objects so the tracker, markers, HUD, radar and controls can be tested. Keys: **Q/E** turn, **P** pause, **O** people-only, **↑/↓** threshold.

## Controls

| Quest controller | Editor key | Action |
|---|---|---|
| **A** | P | Pause / resume detection |
| **B** | O | Toggle people-only filter |
| **Right thumbstick up / down** | ↑ / ↓ | Raise / lower confidence threshold (5% steps, 10–90%, default 45%) |

---

## Architecture

```
 PassthroughCameraAccess (MRUK)
          │ camera frame + camera pose at capture time
          ▼
 SentisInferenceRunManager   (Meta sample, modified)
   YOLO on Inference Engine, async readback, class-aware NMS
          │ OnInferenceCompleted(2D boxes + scores, capture pose, ms)
          ▼
 AwarenessPipeline ──► DetectionProjector ──► DetectionTracker
   (glue + stats)       5 depth rays, median     stable IDs, smoothing,
                        → 3D world point          confirm / fade-out
                                                    │
          ┌──────────────────────┬──────────────────┤
          ▼                      ▼                  ▼
   MarkerManager/MarkerView   HudPresenter      RadarView
   (pooled world labels)      (head-locked)     (bearing + range)

 AwarenessState  ◄── AwarenessControls (A / B / right stick)
   paused · people-only · threshold   (read by inference, markers, HUD, radar)

 AwarenessBootstrap: the single scene component that wires everything up.
 DetectionSimulator: Editor-only fake detections for testing without a headset.
```

The Meta sample code cannot reference project code, so the sample exposes **static hooks** (an event plus pause/threshold fields) and the awareness layer subscribes to them. This keeps the changes to the sample small and easy to review.

---

## What was reused vs. written

**Reused: Meta's [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples), MultiObjectDetection sample**
- Camera capture (`PassthroughCameraAccess`), permission request flow and scene setup
- YOLO model, labels, and inference loop (`SentisInferenceRunManager`)
- Depth raycasting component (`EnvironmentRaycastManager`, from MRUK)

**Modified sample files** (every change is marked `// [BBB]`)
| File | Change |
|---|---|
| `SentisInferenceRunManager.cs` | Keeps confidence scores; **class-aware NMS** (objects never hide people); event with detections + capture pose + inference time; external pause and threshold; sample's own 2D boxes off |
| `DetectionManager.cs` | Removed sample's A/B marker controls (buttons reused); disabled the spatial anchor (not needed, and saving fails for sideloaded apps) |
| `DetectionUiMenuManager.cs` | Auto-start once permissions are granted; hides the sample's info panel |

**Written for this project: `Assets/_Project/Scripts/`**
| File | Purpose |
|---|---|
| `AwarenessBootstrap.cs` | Single scene component; finds sample objects and creates all systems |
| `AwarenessPipeline.cs` | Receives detections, projects to 3D, feeds the tracker, keeps stats |
| `DetectionProjector.cs` | Multi-ray median depth projection (person: torso-biased sample points) |
| `DetectionTracker.cs` | Stable IDs, greedy gated matching, adaptive smoothing, confirmation, in-view-aware removal |
| `MarkerView.cs`, `MarkerManager.cs` | World labels with fade in/out, billboarding, distance scaling, pooling |
| `HudPresenter.cs` | Head-locked HUD + permission/status banner |
| `RadarView.cs` | Top-down radar |
| `AwarenessControls.cs` | Controller input + haptics |
| `AwarenessState.cs` | Shared user settings (pause, filter, threshold) |
| `UiFactory.cs` | Builds world-space UI from code (no prefabs to wire) |
| `DetectionSimulator.cs`, `EditorKeys.cs` | Editor-only testing without a headset |

---

## Assumptions

- **"Common objects"** = the 80 COCO classes the pretrained YOLO model knows (chair, bottle, laptop, cup, …).
- **Distance** = straight-line distance from the user's head to the detected point, in metres.
- **People priority** = never suppressed by objects in NMS, stronger visual style, people-only filter, counted separately in the HUD.
- **Head-locked HUD** = follows the head with light smoothing, placed about 22° below eye level so it does not cover the view.
- **Radar** shows items up to 6 m (further items are pinned to the edge). Items outside the view are remembered for 20 s at their last known position.
- No persistence between sessions is required, so spatial anchors are not used.

## Known limitations

- **Depth needs a surface:** detections whose rays don't hit the environment mesh (very thin objects, glass, far away) are skipped rather than placed at a guessed distance.
- **Class changes:** if the model flips between similar classes (cup ↔ bowl), a new ID can appear for a moment.
- **Moving objects:** smoothing adds a short lag; fast-moving people trail slightly.
- **Occlusion:** a person hidden behind furniture is removed after ~1.2 s and gets a new ID when seen again (no re-identification, by design for privacy).
- **Model:** the small YOLO variant trades accuracy for speed; small/far objects are detected less reliably.
- **Editor:** Meta camera/depth APIs log "not supported" errors in the Editor (they only work on device); the simulator is used instead.
- **Model license:** the YOLO weights come from Meta's sample; check the model's license before any commercial use.

## Privacy

Camera frames are processed **in memory only** for inference and are **never stored, recorded or transmitted**. There is no face recognition or identification of individuals; "person" is only a class label, and IDs are temporary tracking numbers that reset when the app restarts.

# Design Note: Situational Awareness for Quest 3

## Design decisions

**Build on Meta's sample, own the awareness layer.** Camera capture, the YOLO model and inference loop come from Meta's official MultiObjectDetection sample; that is solved infrastructure. I spent my time on what the sample lacks: 3D placement quality, tracking, HUD, radar and controls. Changes to the sample are small, marked `[BBB]`, and exposed through static hooks, so the two layers stay clearly separated.

**Capture-time pose + multi-ray median depth.** Inference takes ~tens of ms, during which the head moves. Projecting with the camera pose saved *at capture time* keeps markers on the object. A single ray through the box centre often misses (e.g. through a chair's legs) and hits the wall behind; casting 5 rays and taking the median distance rejects those outliers. For people, rays aim at the torso.

**Tracker designed for VR comfort.** Greedy matching by class and a distance gate that widens with range (depth is noisier far away). **Adaptive smoothing**: small changes are treated as noise and damped; large changes follow quickly. Markers also interpolate every frame, because detections arrive ~10×/s while rendering runs at 72 fps. Tracks must be seen twice before appearing (no one-frame ghosts) and are only removed when they *should* be visible but aren't. Items out of view are remembered, which is what lets the radar show things behind the user.

**People are priority.** The original NMS suppressed overlapping boxes regardless of class, so a person sitting on a chair could vanish. NMS is now class-aware and an object can never suppress a person.

**UI built in code, low and transparent.** One scene component creates everything; nothing can come unlinked in the Inspector. The HUD sits ~22° below eye level so it is a glance away, not in the way.

**Editor simulator.** I had limited headset access, so I wrote a simulator that feeds fake detections through the same pipeline. This let me build and test the tracker, markers, HUD, radar and controls without a device.

## Problems I hit

- **Hardware access:** I only had a Quest 2 (no camera access or depth), so headset time was short. The simulator was the workaround.
- **Git LFS:** model files ended up as LFS pointer files locally, so Unity could not load the model; fixed with `git lfs pull`.
- **Input System:** my editor test keys used Unity's old input system, but the project uses the new Input System, so they caused errors; fixed with a small helper that supports both.
- **Jitter:** label stepping from the low detection rate; fixed with per-frame interpolation and adaptive smoothing.

## With another week

- **Smoother labels on moving people:** the tracker currently averages positions, so a walking person's label trails slightly behind. I would also track each item's speed and direction to predict where it will be next frame.
- **Alert for people behind you:** a short controller vibration or sound when a new person appears on the radar outside the field of view, so the user doesn't have to keep checking the radar.
- **User settings:** let the user move or resize the HUD and change the radar range with the controller.

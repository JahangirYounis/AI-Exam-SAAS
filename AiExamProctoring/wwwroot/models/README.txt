AI Based Online Examination System - model directory

RESET BASELINE (2026-08-29)

This directory intentionally contains no AI model binaries.

Removed legacy assets:
- MediaPipe Face Landmarker task bundle
- BlazeFace compatibility asset
- EfficientDet-Lite0 object detector
- MediaPipe Tasks Vision browser/WASM runtime

The previous face-continuity heuristic, face/behavior monitor, general COCO
device detector and browser speech/VAD implementation were removed because they
overlap the new six-module architecture without meeting its training and
validation requirements.

New model assets must be added only through the module-by-module integration
process in AI_ARCHITECTURE_ROADMAP.md. Each model needs documented provenance,
version, checksum, validation metrics, calibrated thresholds, fallback policy
and a human-review path before Proctoring.Enabled may be turned on.


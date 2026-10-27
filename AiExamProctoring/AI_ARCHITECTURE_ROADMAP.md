# AI Based Online Examination System — AI Architecture Roadmap

## Current state

The legacy inference and blocking stack remains removed. Model 1 and Model 2 are now integrated
as separate local Python services. Model 1 creates a normalized 512-dimensional
template from 3-10 enrollment photos, verifies five fresh pre-exam frames, and
records periodic three-frame identity observations during an active exam.

Model 2 uses MediaPipe Face Landmarker with a per-session neutral baseline and
temporal confirmation for face count, head, gaze, eye and mouth observations.
Its one-person functional test passed, but its thresholds remain provisional.

Model 3 now has a ten-class dataset/training/export workflow, an optional local
Python API on port 8003, ASP.NET integration and review-only evidence logging.
Its trained ONNX export remains pending, so the website visibly reports
`training_pending` and continues safely with Models 1, 2 and 4.

Model 4 uses the local multilingual Whisper large-v3-turbo model through
faster-whisper CPU INT8 inference. Its FastAPI service runs on port 8004 and
transcribes bounded short microphone clips for Teacher review.

`Proctoring.Enabled` remains `false` because the final risk model is not built.
Model 1 may allow initial exam entry after a verified match, but its continuous
results cannot warn, score risk or block an active attempt.

## Final six-module architecture

| # | Model / module | Responsibility | Training approach | Status |
|---|---|---|---|---|
| 1 | Face Identity Verification | Verify that the registered student is starting and continuing the exam | Pretrained InsightFace buffalo_l plus project-specific template/voting/calibration pipeline | Integrated; threshold calibration pending |
| 2 | Face & Behavior Analysis | Detect no face, multiple faces, head direction, eye gaze and mouth movement | MediaPipe Face Landmarker plus personal baseline and temporal algorithm | Integrated as provisional, observation-only module; representative calibration pending |
| 3 | Electronic Device Detection | Detect phones, tablets, earphones, smart watches, books, notes and calculators | YOLO detector fine-tuned on a project-owned examination dataset | Integration/training tools complete; dataset training and ONNX export pending |
| 4 | Audio / Speech Recognition | Convert audible speech into multilingual text where microphone quality permits | Pretrained Whisper large-v3-turbo through faster-whisper CPU INT8 | Integrated; representative Urdu/English/noisy-room evaluation pending |
| 5 | Question-Relevance Detection | Decide whether speech relates to the visible MCQ, options, letters or numbers | Custom multilingual classifier/algorithm using current-question context | Pending |
| 6 | Proctoring Risk Model | Fuse verified signals into alerts, evidence requests and final decisions | Custom explainable decision model calibrated on reviewed events | Pending |

## Required integration order

1. Build and validate Face Identity Verification in isolation.
2. Add temporal Face & Behavior Analysis without changing identity decisions.
3. Train and evaluate the custom Electronic Device Detection model.
4. Measure Whisper speech transcription on representative Urdu/English and quiet/noisy-room clips.
5. Add Question-Relevance Detection on top of approved transcripts.
6. Add the Proctoring Risk Model only after all upstream modules produce stable,
   versioned signals.

## Module contract

Every module must return a versioned result containing: model/module name,
version, timestamp, confidence, quality/availability state, reason code and
evidence reference. A module reports observations only. It must not directly
block an exam.

Only Module 6 may recommend a warning or hold. Its decision must include the
contributing signals and rule/model version so a Teacher can review and appeal
the result. Three-warning behavior will be added there after false-positive,
cooldown, recovery and identity-substitution policies are validated.

## Activation gates

Before any module becomes active:

- dataset provenance and consent are documented;
- train/validation/test splits prevent student leakage;
- accuracy, false-positive rate and subgroup/device/lighting tests are recorded;
- thresholds are calibrated on representative validation data;
- unavailable/low-quality behavior is fail-safe and visible;
- model files have version and SHA-256 records;
- automated and browser tests cover the integration;
- Teachers can review evidence and candidates have an appeal route.

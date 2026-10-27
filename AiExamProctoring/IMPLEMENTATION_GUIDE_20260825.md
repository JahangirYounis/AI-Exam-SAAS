# AI Based Online Examination System — Reset Baseline Guide

## What currently works

- Admin, Teacher and Student role workflows
- Public/private examination creation, assignment and release
- Session timers, breaks, autosave and results
- One MCQ at a time with Previous/Next navigation and a question-status palette
- Stable candidate-specific question ordering for the same paper
- Camera and microphone readiness checks
- Automatic fullscreen request on the first eligible exam interaction
- Browser focus, visibility and restricted-copy deterrence
- Camera-only short evidence buffer and server evidence storage interfaces

## AI status

No AI inference model is installed or active in this baseline. The following
legacy components were intentionally removed:

- MediaPipe Face Landmarker and its browser/WASM runtime
- BlazeFace legacy model asset
- EfficientDet-Lite0 and its object-detection worker
- custom face identity/behavior fusion scripts
- browser SpeechRecognition and adaptive VAD proctoring scripts
- active legacy threshold-based warning/risk/block decisions

The UI reports **AI models not installed**. Pre-exam entry does not claim an
identity match, and the server does not accept legacy monitoring signals as
violations while `Proctoring.Enabled` is `false`.

## Preserved integration boundaries

Database audit entities, evidence storage, verification ticket services and
monitoring review views remain as dormant infrastructure. They do not prove
that an AI model is running. These interfaces will be connected to the new
modules only after each module passes its validation gates.

The browser does not request Entire Screen sharing. Standard websites cannot
silently enumerate other applications or guarantee prevention of operating-
system switching. Fullscreen and camera/microphone permissions still require
browser support and, where required, a real user gesture or permission choice.

## Next development step

Start with Module 1, Face Identity Verification. Select a real face-embedding
model, define enrollment/reference quality rules, calibrate match thresholds on
representative data, add presentation-attack/liveness handling as a separately
measured control, and connect it to the dormant verification ticket only after
the evaluation report is approved.

See `AI_ARCHITECTURE_ROADMAP.md` for the complete six-module sequence and the
activation checklist.


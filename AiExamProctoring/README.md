# AI Based Online Examination System

ASP.NET Core 8 MVC + Razor Pages examination platform with separate Admin,
Teacher and Student portals, public/private papers, timed multi-session exams,
automatic marking and a staged, review-first AI proctoring roadmap.

## Current AI status

The separate `AI/services` module has four explicit module contracts:

1. **Model 1 — Face Identity:** InsightFace `buffalo_l` detection/recognition,
   multi-photo enrollment, multi-frame voting and periodic identity observation.
2. **Model 2 — Candidate Behavior:** MediaPipe Face Landmarker single/multiple-face,
   head, gaze, eyes and mouth observations with a personal temporal baseline.
3. **Model 3 — Object Detection:** a custom ten-class YOLO ONNX inference path
   for phones, tablets, earphones, smart watches, books, notes and calculators.
4. **Model 4 — Speech Recognition:** local multilingual Whisper large-v3-turbo
   transcription of bounded short microphone clips through faster-whisper CPU INT8.

Models 1, 2 and 4 are functional with their installed local model files. Model 3's
API, .NET client, exam-page monitor, evidence logging and launcher are integrated;
its trained `model3_objects.onnx` remains pending until the separate dataset is
captured, annotated, trained and externally tested.

Every AI/browser event is observation-only and stored for authorized human review.
No current model automatically fails, disqualifies or blocks a candidate.

See `AI_ARCHITECTURE_ROADMAP.md` for the required order:

1. Face Identity Verification
2. Face & Behavior Analysis
3. Electronic Device Detection
4. Audio / Speech Recognition
5. Question-Relevance Detection
6. Proctoring Risk Model

## Working examination features

- Admin, Teacher and Student role portals
- Public/private exam creation, assignment and release
- Reusable question bank and session builder
- One MCQ at a time with Previous/Next navigation
- Question palette showing unanswered, answered and review states
- Stable candidate-specific question ordering for the same paper
- Server timers, breaks, autosave, resume and automatic marking
- Camera/microphone readiness without Entire Screen sharing
- Automatic fullscreen request and browser-focus deterrence
- Camera-only short evidence interface and authorized review folders
- Results, answer review and PDF/certificate generation

## Database

The project includes an EF Core migration baseline. `Program.cs` applies pending
migrations before seeding roles and application data. Update the SQL Server
connection string in `appsettings.json` for the target environment.

The supplied development administrator credential is for local setup only.
Change it and move secrets to User Secrets or environment variables before any
real deployment.

## Build and run

On a new laptop, restore the reusable AI asset folders and recreate all virtual
environments rather than copying them:

```powershell
cd "D:\Final Year Project"
powershell -ExecutionPolicy Bypass -File ".\AI\services\restore_ai_assets.ps1"
powershell -ExecutionPolicy Bypass -File ".\AI\services\setup_ai_services.ps1"
powershell -ExecutionPolicy Bypass -File ".\START_AI_SERVICES.ps1"
```

Models 1, 2, 3 and 4 use ports 8001–8004. Model 3 reports `training_pending`
until `AI/models/model3_objects/model3_objects.onnx` exists; Model 4 uses its
installed `AI/models/model4_speech/faster-whisper-large-v3-turbo` weights.

Use Visual Studio 2022 or the .NET 8 SDK:

```text
dotnet restore Website/AiExamProctoring.sln
dotnet build Website/AiExamProctoring.sln
dotnet run --project Website/AiExamProctoring/AiExamProctoring.csproj
```

The development HTTPS profile uses `https://localhost:7197`.

## Next step

Capture and label the ten Model 3 classes, train a leakage-safe detector,
export to ONNX, run untouched internal and external tests, then install the
accepted export without changing the website contract.

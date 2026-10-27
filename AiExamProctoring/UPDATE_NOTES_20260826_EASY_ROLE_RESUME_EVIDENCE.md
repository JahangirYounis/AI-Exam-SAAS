# AI Based Online Examination System — Easy Role, Resume and Evidence Upgrade

## What changed

- **Clear ownership:** Admin now handles platform governance, accounts, exam oversight, results and integrity audit. Teacher owns departments, posts, subjects, question bank, examination creation, candidate assignment and publishing.
- **Teacher Academic Setup:** one simple Teacher page manages departments, recruitment posts/positions and subjects, with safe archive/activate actions.
- **Teacher profile:** dedicated view/edit pages, professional information and protected JPG/PNG profile photo upload.
- **Candidate photo gate:** a candidate cannot start an examination until a valid current JPG/PNG photo exists in private profile storage. The screen explains that the photo is used for AI verification and the PDF certificate.
- **One examination attempt:** completed, auto-submitted or disqualified examinations automatically disappear for that candidate. An always-open practice test still allows only one attempt per candidate.
- **Exact secure resume:** the active question is saved server-side. If a misconduct hold is reviewed and unblocked by the Teacher or Admin, paused timer seconds are restored and the candidate resumes at the saved question with saved answers.
- **Evidence preservation:** still screenshots and short camera WebM clips are stored separately. Entire Screen sharing is not requested. When microphone permission is active, the microphone track is included and labelled in review pages.
- **Evidence folders:** files remain under `App_Data/ProctoringEvidence/candidate-{candidate}/exam-{exam}/` and are streamed only through authorized controllers. Default retention is now 3650 days.
- **Candidate navigation fix:** sidebar brand, profile and security/footer sections stay fixed while only the navigation area scrolls; the active page is brought into view without moving the whole sidebar.

## Required database update

The new migration is `20260826090000_ResumeExactQuestion` and adds:

- `ExamSessionAttempts.LastQuestionId`
- `ProctoringLogs.RecordingPath`
- `ProctoringLogs.RecordingIncludesAudio`

Run in Visual Studio Package Manager Console:

```powershell
Update-Database
```

Then use **Clean Solution** and **Rebuild Solution**. The project targets .NET 8.

## Browser reality

Modern browsers do not let a normal website silently share a screen or make tab switching physically impossible. The system therefore requests Entire Screen at exam entry, enters fullscreen where supported, detects exits/switches, records server-confirmed events and applies the configured warning/block policy. This is the strongest standards-compliant web approach; kiosk/managed-browser mode can be added for an institution-controlled lab.

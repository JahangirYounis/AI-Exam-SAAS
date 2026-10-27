# Role Review and Candidate Evidence Folder Update

## Final role boundaries

| Role | Owns | Does not own |
| --- | --- | --- |
| Admin | User approval/blocking, departments, posts, subjects, Teacher-question sharing moderation, global exam/results oversight, read-only integrity audit | Exam/question authoring, candidate assignment, publishing, proctoring unblock decisions |
| Teacher | Own questions, examinations, sessions, candidates, publishing, results, candidate evidence review and owned-attempt unblock | Other Teachers' examinations, questions, results or evidence |
| Student | Profile, available assigned/public examinations, secure attempts, notifications, results and certificates | Authoring, moderation or review actions |

## Proctoring folder structure

New evidence is stored privately as:

`App_Data/ProctoringEvidence/candidate-{CandidateNumber}/exam-{ExamId}/attempt-{AttemptId}-...`

Admin and Teacher screens use the same understandable navigation:

1. Candidate folder
2. Examination folder
3. Chronological event/evidence list

Admin access is read-only. The owning Teacher can mark one event or the full exam folder as reviewed and can unblock an owned attempt with paused time restored.

## UI and reliability improvements

- Rebuilt Student dashboard with a single primary action, profile readiness, available exams, upcoming schedule, active holds, performance, checklist, notifications and results.
- Expanded Teacher dashboard into a four-step authoring workflow with direct access to question bank, paper building, candidates, publishing, results and evidence folders.
- Removed Admin question Create/Edit pages and replaced them with a sharing-moderation queue.
- Removed Admin operational proctoring controls and corrected all Student wording to identify the Teacher as the examination reviewer.
- Restricted Teacher profile-photo access to candidates connected to that Teacher's examinations.
- Evidence retention cleanup now works recursively and removes empty candidate/exam folders safely.

No database schema change was required for this release. Apply the migrations already included in the project before running it.

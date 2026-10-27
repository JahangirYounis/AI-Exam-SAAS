# Real-World Exam Builder & Question Bank Update — 25 August 2026

## Exam creation defect fixed

- Root cause: the default exam was `today at 10:00`; after 10:00 server validation rejected it, while the page used `ModelOnly` and hid every property-level reason.
- New examinations default to tomorrow at 10:00.
- All validation summaries now show field and model errors.
- Teacher Create/Edit actions return a clear database-save error and log the technical exception instead of failing silently.
- Recruitment Department/Post relationship is validated on the server.

## Purpose-specific teacher flow

- Step 1: Private or Public.
- Step 2: Academic, Recruitment / Job, Practice / Mock, Certification or Other.
- Academic supports and persists School, College, University, Academy and Madrasa levels.
- Recruitment shows only Department, Post and recruitment batch fields; Posts filter by Department.
- Public Practice supports an always-open reusable mode with a per-student attempt limit.
- Irrelevant target fields are cleared on the server so hidden browser values cannot pollute examination data.

## Pakistani test bank

- 21 active subject folders.
- At least 52 verified starter MCQs per subject (1,092 expansion questions plus the original 180 curated questions).
- Folder-based paper builder shows remaining available counts.
- Selecting a checkbox saves once, removes the question from the available list and decrements folder/page counts.

## Teacher catalog

- Read-only Teacher page for active Departments and Posts.
- Admin continues to own master-data maintenance.
- Seeded catalog includes education, police, health, revenue, excise, public-service commissions, local government, IT, banking, universities and defence-oriented testing groups.

## Student access and testing

- New Student registrations are approved and signed in immediately after successful identity-photo registration.
- Teacher registrations still require Admin approval.
- Legacy Students are upgraded only after a correct-password sign-in; failed sign-ins never mutate the account.
- `Online Examination System Demo Test` is an always-open public 25-question mock exam with up to 20 attempts per student.
- The first approved Teacher who opens the workspace becomes this seeded test's accountable owner, so evidence review and unblock stay inside Teacher permissions.
- Reusable attempts are supported by database schema, availability logic, pre-check, start transaction and student dashboard.

## Database

- Migration `20260825143000_ReusablePracticeExams` adds `AcademicLevel`, `IsReusable` and `MaximumAttempts` and safely changes the attempt index to support reusable tests.

# Teacher Question Bank and Academic Setup Upgrade

## Fixed root cause

The old Teacher Question Bank filtered every row by the current Teacher ID. Built-in MCQs are created by `System Seed`, so the database contained the questions but the Teacher page displayed an empty table.

## New Teacher workflow

- Question Bank now combines verified starter/shared MCQs with the signed-in Teacher's own MCQs.
- Active subject folders show separate shared and personal counts.
- Search covers question text, topic and tags.
- Source filters: All, Verified Starter/Shared, and My Editable Questions.
- 30-row pagination keeps large Pakistani-test banks fast.
- Teachers can edit/archive only their own MCQs.
- A protected starter MCQ can be copied into `My Questions` and customized safely.
- Exam paper builder continues to show verified shared questions plus the Teacher's own active questions.
- Academic Setup now has clear numbered navigation for Departments, Posts/Positions, Subjects and Question Bank.
- Admin contains no Question Bank controller, view or navigation item. Admin remains responsible for platform governance, accounts, results, oversight and security audit.

No new database migration is required for this particular update. Existing seeders automatically repair missing/inactive built-in subjects and insert missing starter MCQs during application startup.

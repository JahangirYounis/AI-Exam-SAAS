/* Student Task 02: server-authoritative answer saving, navigation and timer.
   AI proctoring is delegated to the existing ExamProctoring implementation. */
(() => {
    'use strict';
    const workspace = document.getElementById('examWorkspace');
    const form = document.getElementById('examForm');
    if (!workspace || !form) return;
    const logic = window.ExamAttemptState;
    const questions = [...form.querySelectorAll('.exam-question')];
    if (!questions.length) return;
    const sessionId = Number(workspace.dataset.sessionId);
    const allowBack = workspace.dataset.backAllowed === 'true';
    const saveStatus = document.getElementById('examSaveStatus');
    const timer = document.getElementById('examTimeRemaining');
    const timerPanel = document.querySelector('.exam-taking-timer');
    const timerMessage = document.getElementById('examTimerStatus');
    const progress = document.getElementById('examAnswerProgress');
    const progressFill = document.getElementById('examProgressFill');
    const navButtons = [...document.querySelectorAll('[data-jump]')];
    const next = document.getElementById('examNext');
    const back = document.getElementById('examBack');
    const clear = document.getElementById('examClear');
    const review = document.getElementById('examReviewToggle');
    const submit = document.getElementById('submitExamSession');
    const pause = document.getElementById('examPauseForm');
    const token = form.querySelector('[name="__RequestVerificationToken"]')?.value;
    if (!token || !sessionId || !logic || !window.ExamProctoring || !window.ProctoringRealtime) {
        saveStatus.textContent = 'Examination services did not load correctly. Reload this page or contact your teacher.';
        saveStatus.classList.add('error');
        form.querySelectorAll('button, input[type="radio"]').forEach(el => el.disabled = true);
        return;
    }

    const state = new Map(questions.map(q => {
        const id = Number(q.dataset.questionId);
        return [id, { value: q.querySelector('[data-answer]')?.value || '', persisted: q.querySelector('[data-answer]')?.value || '', reviewed: q.dataset.reviewed === 'true', saving: false }];
    }));
    const resumeId = Number(workspace.dataset.resumeQuestion);
    let currentIndex = Math.max(0, questions.findIndex(q => Number(q.dataset.questionId) === resumeId));
    let saveQueue = Promise.resolve();
    let busy = false, submitted = false, timeEnded = false, stopped = false;
    const serverNow = Date.parse(workspace.dataset.serverNow);
    const expiresAt = Date.parse(workspace.dataset.expires);
    const clockStart = performance.now();
    const remainingSeconds = () => logic.secondsRemaining(expiresAt, serverNow, performance.now() - clockStart);
    const currentId = () => Number(questions[currentIndex]?.dataset.questionId || 0);
    const dirty = () => logic.hasUnsaved(state.values());
    function feedback(text, error = false) {
        saveStatus.textContent = text;
        saveStatus.classList.toggle('error', error);
    }
    const idFor = i => Number(questions[i]?.dataset.questionId || 0);
    const correctOption = (id, value) => !!questions.find(q => Number(q.dataset.questionId) === id)?.querySelector(`input[type="radio"][value="${value}"]`);
    async function post(url, values) {
        const body = new FormData();
        body.append('__RequestVerificationToken', token);
        body.append('sessionAttemptId', String(sessionId));
        Object.entries(values).forEach(([key, value]) => body.append(key, String(value)));
        let response;
        try {
            response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers: { Accept: 'application/json' }, body, signal: AbortSignal.timeout(16000) });
        } catch (error) {
            throw new Error(error?.name === 'TimeoutError' ? 'The server did not respond. Your answer is still on this screen; retry saving.' : 'Connection interrupted. Reconnect and retry before continuing.');
        }
        let data;
        try { data = await response.json(); } catch { throw new Error('The examination server returned an unreadable response. Retry after reconnecting.'); }
        if (data?.blocked || response.status === 423) {
            stopped = true;
            window.top.location.replace(data.redirectUrl || workspace.dataset.blockedUrl);
            throw new Error('Attempt locked for Teacher review.');
        }
        if (data?.identityRequired || response.status === 428) {
            stopped = true;
            window.location.replace(data.redirectUrl || `${location.pathname}?sessionAttemptId=${sessionId}`);
            throw new Error('Fresh identity verification required.');
        }
        if (data?.expired || response.status === 409 && remainingSeconds() <= 0) {
            finish(true);
            throw new Error('Session time has ended.');
        }
        if (!response.ok || data?.ok !== true) throw new Error(data?.message || 'The server could not save your change. Please retry.');
        return data;
    }
    async function save(id) {
        const item = state.get(id);
        if (!item || item.value === item.persisted || stopped || submitted || timeEnded) return true;
        if (item.value && !correctOption(id, item.value)) return false;
        const intended = item.value;
        item.saving = true;
        feedback('Saving your answer…');
        try {
            const response = await post(workspace.dataset.saveUrl, { questionId: id, selectedOption: intended });
            item.persisted = intended;
            feedback(`Saved on the server at ${response.savedAt || 'this moment'}.`);
            update();
            return true;
        } catch (error) {
            if (!stopped) feedback(error.message || 'Could not save. Retry.', true);
            return false;
        } finally { item.saving = false; }
    }
    const scheduleSave = id => {
        const task = saveQueue.catch(() => {}).then(() => save(id));
        saveQueue = task;
        return task;
    };
    async function saveDirty() {
        await saveQueue.catch(() => {});
        for (const [id, item] of state) if (item.value !== item.persisted) await scheduleSave(id);
        return !dirty() && !stopped;
    }
    function update() {
        questions.forEach((section, index) => { section.hidden = index !== currentIndex; });
        const answered = logic.answeredCount([...state.values()].map(s => s.value));
        document.getElementById('examAnsweredCount').textContent = `${answered} of ${questions.length} answered`;
        document.getElementById('examQuestionNumber').textContent = String(currentIndex + 1);
        progress.setAttribute('aria-valuenow', String(answered));
        progressFill.style.width = `${100 * answered / questions.length}%`;
        const item = state.get(currentId());
        review.textContent = item?.reviewed ? 'Remove review mark' : 'Mark for review';
        back.disabled = busy || currentIndex === 0 || !allowBack;
        next.disabled = busy || currentIndex === questions.length - 1;
        next.innerHTML = currentIndex === questions.length - 1 ? 'Last question' : (item?.value ? 'Next question' : 'Skip &amp; next') + ' <i class="bi bi-chevron-right" aria-hidden="true"></i>';
        navButtons.forEach((button, i) => {
            const s = state.get(idFor(i));
            button.classList.toggle('answered', !!s?.value);
            button.classList.toggle('reviewed', !!s?.reviewed);
            button.classList.toggle('current', i === currentIndex);
            button.setAttribute('aria-current', i === currentIndex ? 'step' : 'false');
            button.disabled = busy || !allowBack;
        });
    }
    async function moveTo(index) {
        if (busy || stopped || submitted || timeEnded || index === currentIndex ||
            !logic.canNavigate(currentIndex, index, questions.length, allowBack)) return;
        busy = true; update();
        try {
            if (!await saveDirty()) { feedback('Current answer has not been saved. Retry before leaving this question.', true); return; }
            await post(workspace.dataset.positionUrl, { questionId: idFor(index) });
            currentIndex = index;
            update();
            questions[index].scrollIntoView({ block: 'nearest', behavior: 'instant' });
        } catch (error) {
            if (!stopped) feedback(error.message || 'Question navigation failed; retry.', true);
        } finally { busy = false; update(); }
    }
    questions.forEach(section => {
        const id = Number(section.dataset.questionId);
        section.querySelectorAll('input[type="radio"]').forEach(input => input.addEventListener('change', () => {
            if (stopped || submitted || timeEnded) return;
            const value = input.checked ? input.value : '';
            const s = state.get(id); s.value = value;
            section.querySelector('[data-answer]').value = value;
            update();
            scheduleSave(id);
        }));
    });
    navButtons.forEach((button, index) => button.addEventListener('click', () => { if (allowBack) moveTo(index); }));
    back?.addEventListener('click', () => moveTo(currentIndex - 1));
    next.addEventListener('click', () => moveTo(currentIndex + 1));
    clear.addEventListener('click', () => {
        if (stopped || submitted || timeEnded) return;
        const id = currentId();
        const section = questions[currentIndex];
        section.querySelectorAll('input[type="radio"]').forEach(input => { input.checked = false; });
        section.querySelector('[data-answer]').value = '';
        state.get(id).value = '';
        update(); scheduleSave(id);
    });
    review.addEventListener('click', async () => {
        if (busy || stopped || submitted || timeEnded) return;
        const id = currentId(), item = state.get(id);
        busy = true; update();
        try { await post(workspace.dataset.reviewUrl, { questionId: id, marked: !item.reviewed }); item.reviewed = !item.reviewed; feedback('Review mark saved.'); }
        catch (error) { if (!stopped) feedback(error.message, true); }
        finally { busy = false; update(); }
    });
    pause?.addEventListener('submit', e => {
        if (!confirm('Pausing locks this attempt until your Teacher authorizes a resume. Your saved answers will be retained. Continue?')) e.preventDefault();
    });
    let monitoring;
    async function finish(expired = false) {
        if (submitted) return;
        submitted = true;
        feedback(expired ? 'Time ended. Submitting answers that reached the server before the deadline…' : 'Submitting your saved answers…');
        submit.disabled = true; next.disabled = true; back.disabled = true;
        // Answers are already persisted. Give final local recorder writes a short
        // grace period; the durable queue retries independently after navigation.
        const finalized = monitoring?.finishEvidence?.();
        if (finalized) await Promise.race([finalized.catch(() => {}), new Promise(resolve => setTimeout(resolve, 1200))]);
        // Native POST uses antiforgery and delegates final scoring/time enforcement to the server.
        HTMLFormElement.prototype.submit.call(form);
    }
    form.addEventListener('submit', async e => {
        e.preventDefault();
        if (submitted || stopped) return;
        if (remainingSeconds() <= 0) { finish(true); return; }
        if (!confirm('Submit this session now? You will not be able to edit these answers afterwards.')) return;
        busy = true; submit.disabled = true; update();
        if (!await saveDirty()) { busy = false; submit.disabled = false; update(); feedback('Submission was not sent because one or more answers are not saved. Retry when connected.', true); return; }
        finish(false);
    });
    function tick() {
        if (submitted || stopped) return;
        const seconds = remainingSeconds();
        const m = Math.floor(seconds / 60), s = seconds % 60;
        timer.textContent = `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
        timerPanel.classList.toggle('urgent', seconds < 60);
        timerMessage.textContent = seconds < 60 ? 'Final minute' : 'Server-controlled countdown';
        if (seconds === 0) finish(true);
    }
    const scheduleWarning = e => { if (!submitted && !stopped && dirty()) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', scheduleWarning);
    window.addEventListener('proctoring-state', event => {
        if (Number(event.detail?.attemptId) !== Number(workspace.dataset.attemptId)) return;
        const status = event.detail?.status;
        if (status === 'ProctoringBlocked') { stopped = true; feedback('Exam locked. Your saved answers are retained.', true); }
        if (['Completed', 'AutoSubmitted', 'Disqualified'].includes(status)) { stopped = true; feedback('This attempt has been finalized.'); }
    });
    window.addEventListener('online', () => { if (!submitted && !stopped) saveDirty(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) tick(); });
    update(); tick();
    const interval = setInterval(tick, 750);
    window.addEventListener('pagehide', event => {
        if (event.persisted) return;
        clearInterval(interval);
        window.removeEventListener('beforeunload', scheduleWarning);
    });
    window.addEventListener('pageshow', event => { if (event.persisted) tick(); });
    // Preserve existing Model 1-4 proctoring, evidence, SignalR and health heartbeats.
    const config = window.examSessionMonitoringConfig;
    if (config) {
        config.question = currentId;
        config.submitted = () => submitted || stopped;
        try { monitoring = window.ExamProctoring.start(config); }
        catch (error) { feedback('Exam monitoring could not start. Please reload or contact your Teacher.', true); submit.disabled = true; }
    }
})();

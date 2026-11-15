(() => {
    'use strict';
    const form = document.getElementById('teacherExamForm');
    if (!form) return;
    const field = name => form.querySelector(`[name="${name}"]`);
    const audience = field('Audience'), purpose = field('Purpose'), reusable = field('IsReusable');
    const mode = field('BlockingMode'), warnings = field('AllowedWarnings');
    const steps = Array.from(form.querySelectorAll('[data-wizard-step]'));
    const indicators = Array.from(form.querySelectorAll('[data-wizard-indicator]'));
    const back = form.querySelector('[data-wizard-back]'), next = form.querySelector('[data-wizard-next]');
    const submit = form.querySelector('[data-wizard-submit]');
    const counter = form.querySelector('[data-wizard-counter]');
    const serverBaseline = new Date(form.dataset.serverNow);
    const elapsedBaseline = Date.now();
    let current = 0;
    const nowServer = () => new Date(serverBaseline.getTime() + Date.now() - elapsedBaseline);
    const pad = n => String(n).padStart(2, '0');
    const localDate = date => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    const localTime = date => `${pad(date.getHours())}:${pad(date.getMinutes())}`;

    // Hide irrelevant inputs and remove them from client validation. Their server
    // equivalents are still normalized and authorization-checked before saving.
    function panel(id, visible) {
        const element = form.querySelector(`#${id}`);
        if (!element) return;
        element.hidden = !visible;
        element.querySelectorAll('input, select, textarea').forEach(input => {
            input.disabled = !visible;
        });
    }
    function updateChoices() {
        const privateTest = audience.value === 'Private' || audience.value === '0';
        const recruitment = purpose.value === 'Recruitment' || purpose.value === '1';
        const practice = purpose.value === 'Practice' || purpose.value === '2';
        panel('privateAccess', privateTest);
        panel('recruitmentFields', recruitment);
        panel('practiceFields', practice && !privateTest);
        if (!practice || privateTest) reusable.checked = false;
        const scheduled = !reusable.checked;
        panel('scheduleFields', scheduled);
        ['RecruitmentDepartment', 'RecruitmentPost', 'RecruitmentGrade'].forEach(name => {
            if (field(name)) field(name).required = recruitment;
        });
    }
    function updatePolicy() {
        const manual = mode.value === 'MANUAL_ONLY';
        const section = form.querySelector('#automaticWarningSettings');
        section.hidden = manual;
        warnings.disabled = manual;
        const number = Number(warnings.value);
        form.querySelector('#examPolicyExample').textContent = manual
            ? 'Teacher review mode: confirmed events are recorded, but warning counts do not trigger an automatic block.'
            : Number.isInteger(number) && number >= 1 && number <= 10
                ? `Automatic mode: the first ${number} distinct qualifying incidents produce warnings; the next qualifying incident triggers a review block.`
                : 'Choose 1–10 allowed warnings.';
    }
    function validateSchedule() {
        const date = field('ExamDate'), time = field('StartTime');
        if (!date || !time) return true;
        const now = nowServer();
        date.min = localDate(now);
        if (date.value && date.value < date.min) date.setCustomValidity('Choose today or a future date.');
        else date.setCustomValidity('');
        if (!reusable.checked && date.value === localDate(now) && time.value && time.value <= localTime(now))
            time.setCustomValidity('Choose a start time later than the current server time.');
        else time.setCustomValidity('');
        if (!reusable.checked && date.value === localDate(now)) time.min = localTime(now);
        else time.removeAttribute('min');
        return date.checkValidity() && time.checkValidity();
    }
    function display(index, focus = false) {
        current = Math.max(0, Math.min(steps.length - 1, index));
        steps.forEach((step, idx) => { step.hidden = idx !== current; });
        indicators.forEach((item, idx) => {
            item.classList.toggle('is-current', idx === current);
            item.classList.toggle('is-complete', idx < current);
            if (idx === current) item.setAttribute('aria-current', 'step'); else item.removeAttribute('aria-current');
        });
        back.hidden = current === 0;
        next.hidden = current === steps.length - 1;
        submit.hidden = current !== steps.length - 1;
        counter.textContent = `Step ${current + 1} of ${steps.length}`;
        if (focus) steps[current].querySelector('h2')?.focus({preventScroll: true});
        if (focus) form.scrollIntoView({behavior: 'smooth', block: 'start'});
    }
    function validateStep(index) {
        if (index === 3) validateSchedule();
        const controls = steps[index].querySelectorAll('input:not(:disabled), select:not(:disabled), textarea:not(:disabled)');
        for (const control of controls) {
            if (!control.checkValidity()) {
                if (current !== index) display(index);
                if (control.closest('details')) control.closest('details').open = true;
                control.reportValidity();
                return false;
            }
        }
        if (index === 4 && mode.value !== 'MANUAL_ONLY' && !form.querySelector('input[name="qualifyingEvents"]:checked')) {
            alert('Select at least one meaningful incident category for automatic blocking.');
            return false;
        }
        return true;
    }
    [audience, purpose, reusable].forEach(control => control?.addEventListener('change', () => {updateChoices(); validateSchedule();}));
    mode?.addEventListener('change', updatePolicy);
    warnings?.addEventListener('input', updatePolicy);
    ['ExamDate', 'StartTime'].forEach(name => field(name)?.addEventListener('change', validateSchedule));
    updateChoices(); updatePolicy(); validateSchedule();
    form.classList.add('wizard-enabled');
    const invalid = form.querySelector('[data-wizard-step] .field-validation-error');
    const startIndex = invalid?.closest('[data-wizard-step]')?.dataset.wizardStep;
    display(startIndex ? Number(startIndex) : 0);
    back?.addEventListener('click', () => display(current - 1, true));
    next?.addEventListener('click', () => { if (validateStep(current)) display(current + 1, true); });
    form.addEventListener('invalid', event => {
        const step = event.target.closest('[data-wizard-step]');
        if (step && Number(step.dataset.wizardStep) !== current) display(Number(step.dataset.wizardStep));
        const details = event.target.closest('details');
        if (details) details.open = true;
    }, true);
    form.addEventListener('submit', event => {
        // Check the entire form without relying on hidden-step jQuery validation.
        for (let i = 0; i < steps.length; i++) {
            if (!validateStep(i)) { event.preventDefault(); display(i, true); return; }
        }
        // Re-check on server even when the browser clock is inaccurate.
        if (window.jQuery?.validator) {
            // jQuery unobtrusive ignores hidden wizard controls, so native validation
            // above is authoritative for client UX. Server ModelState is authoritative.
        }
    });
    setInterval(() => { if (current === 3) validateSchedule(); }, 30000);
})();

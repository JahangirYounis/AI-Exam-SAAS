(() => {
    'use strict';
    const form = document.getElementById('teacherExamForm');
    if (!form) return;
    const field = name => form.querySelector(`[name="${name}"]`);
    const audience = field('Audience'), purpose = field('Purpose'), reusable = field('IsReusable');
    function panel(id, visible) {
        const node = document.getElementById(id);
        node.hidden = !visible;
        node.querySelectorAll('input,select').forEach(input => input.disabled = !visible);
    }
    function update() {
        const privateTest = audience.value === 'Private' || audience.value === '0';
        const job = purpose.value === 'Recruitment' || purpose.value === '1';
        const practice = purpose.value === 'Practice' || purpose.value === '2';
        panel('privateAccess', privateTest);
        panel('recruitmentFields', job);
        panel('practiceFields', practice && !privateTest);
        if (!practice || privateTest) reusable.checked = false;
        // Keep dates posted; server normalizes dates for an always-open test.
        document.getElementById('scheduleFields').hidden = reusable.checked;
        ["RecruitmentDepartment", "RecruitmentPost", "RecruitmentGrade"].forEach(name => field(name).required = job);
    }
    [audience, purpose, reusable].forEach(input => input.addEventListener('change', update));
    // Open advanced fields before the browser tries to focus an invalid input.
    form.addEventListener('invalid', event => { const details = event.target.closest('details'); if (details) details.open = true; }, true);
    form.addEventListener('submit', event => {
        if (window.jQuery && !window.jQuery(form).valid()) {
            form.querySelectorAll('details').forEach(details => details.open = true);
            event.preventDefault();
        }
    });
    update();
})();

(() => {
    const page = document.querySelector('.lock-audit');
    if (!page) return;
    const status = document.getElementById('reviewStatus');
    const check = document.getElementById('checkStatus');
    check.addEventListener('click', async () => {
        check.disabled = true;
        status.textContent = 'Checking instructor review status…';
        try {
            const response = await fetch(`/proctoring-state/${page.dataset.proctoringAttempt}`, { cache: 'no-store', signal: AbortSignal.timeout(8000) });
            if (!response.ok) throw new Error();
            window.ProctoringRealtime.apply(await response.json(), false);
            status.textContent = 'Your attempt is still locked. Waiting for instructor review.';
        } catch { status.textContent = 'Could not reach the server. Please try again.'; }
        finally { check.disabled = false; }
    });
    window.addEventListener('proctoring-state', event => {
        if (event.detail.attemptId !== Number(page.dataset.proctoringAttempt)) return;
        const seconds = Math.max(0, Number(event.detail.remainingTimeSeconds) || 0);
        document.getElementById('frozenTime').textContent = [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60].map(n => String(n).padStart(2, '0')).join(':');
        status.textContent = event.detail.status === 'ProctoringBlocked' ? 'Connected · waiting for instructor review.' : 'Status updated by your instructor.';
    });
    const form = document.getElementById('appealForm');
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const button = form.querySelector('button');
        const feedback = document.getElementById('appealStatus');
        if (button.disabled) return;
        button.disabled = true; feedback.textContent = 'Sending your review request…';
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', signal: AbortSignal.timeout(15000) });
            const result = await response.json();
            if (!response.ok || !result.ok) throw new Error(result.message || 'The request could not be saved. Please retry.');
            button.textContent = 'Instructor review requested'; feedback.textContent = result.message;
        } catch (error) { button.disabled = false; feedback.textContent = error.message || 'Connection interrupted. Please retry.'; }
    });
    const filter = document.getElementById('auditFilter');
    const rows = [...page.querySelectorAll('.lock-event')];
    filter.addEventListener('change', () => {
        let count = 0;
        rows.forEach(row => { row.hidden = filter.value !== 'all' && row.dataset[filter.value] !== 'true'; if (!row.hidden) count++; });
        document.getElementById('auditVisible').textContent = `${count} of ${rows.length} records`;
        const empty = document.getElementById('auditNoMatches'); if (empty) empty.hidden = count !== 0;
    });
    const dialog = document.getElementById('evidenceLightbox');
    const content = document.getElementById('evidenceContent');
    const error = document.getElementById('evidenceError');
    let trigger;
    page.querySelectorAll('[data-evidence]').forEach(link => link.addEventListener('click', event => {
        if (!dialog.showModal) return; // The authenticated evidence URL remains a usable fallback.
        event.preventDefault(); trigger = link;
        document.getElementById('evidenceCaption').textContent = link.dataset.caption;
        error.textContent = ''; content.replaceChildren();
        const media = document.createElement(link.dataset.evidence === 'image' ? 'img' : 'video');
        media.src = link.href;
        if (link.dataset.evidence === 'image') media.alt = link.dataset.caption;
        else { media.controls = true; media.preload = 'metadata'; }
        media.addEventListener('error', () => { error.textContent = 'This evidence file is unavailable or could not be played. Please ask your instructor to review the original record.'; });
        content.append(media); dialog.showModal();
    }));
    document.getElementById('closeEvidence').onclick = () => dialog.close();
    dialog.addEventListener('close', () => {
        content.querySelector('video')?.pause(); content.replaceChildren(); trigger?.focus();
    });
    page.querySelectorAll('.lock-snapshot img').forEach(img => img.addEventListener('error', () => {
        img.alt = 'Snapshot unavailable'; img.closest('a').querySelector('span').textContent = 'Snapshot unavailable';
    }));
})();

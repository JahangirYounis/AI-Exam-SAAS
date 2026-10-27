(() => {
    const root = document.getElementById('live-room');
    if (!root) return;
    const examId = Number(root.dataset.exam), viewer = crypto.randomUUID().replaceAll('-', '');
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const grid = document.getElementById('camera-grid'), status = document.getElementById('room-status');
    const tiles = new Map();
    let page = 1, pages = 1, config, stopped = false, pauseAttempt;
    const send = (attemptId, kind, data = '') => LiveProctoring.post('/live-proctoring/signal', { attemptId, viewer, kind, data }, token);
    function remove(id) {
        const tile = tiles.get(id);
        tile?.peer?.pc.close(); tile?.element.remove(); tiles.delete(id);
        send(id, 'bye').catch(() => {});
    }
    function make(row) {
        const element = document.createElement('article'); element.className = 'camera-tile';
        element.innerHTML = '<video autoplay muted playsinline></video><div class="tile-info"><strong class="name"></strong> <strong class="strikes"></strong><p class="connection small">Connecting camera…</p><p class="tile-alert" role="status"></p><button class="btn btn-outline-success btn-sm acknowledge">Seen</button><a class="btn btn-outline-primary btn-sm review">Recordings &amp; review</a><button class="btn btn-outline-danger btn-sm pause">Block exam</button><button class="btn btn-success btn-sm resume">Unblock &amp; Resume</button></div>';
        element.querySelector('.name').textContent = row.name;
        element.querySelector('.review').href = `/Teacher/Monitoring/ExamFolder?examId=${examId}&studentId=${encodeURIComponent(row.studentId)}`;
        const tile = { element, video: element.querySelector('video'), row, seen: 0, watchAt: 0 };
        tile.video.onclick = () => tile.video.requestFullscreen?.().catch(() => {});
        element.querySelector('.acknowledge').onclick = () => { tile.seen = tile.row.alert?.logId || 0; update(tile, tile.row); };
        element.querySelector('.pause').onclick = () => {
            pauseAttempt = row.attemptId;
            document.getElementById('pause-name').textContent = row.name;
            document.getElementById('pause-reason').value = '';
            document.getElementById('pause-error').textContent = '';
            document.getElementById('pause-dialog').showModal();
        };
        element.querySelector('.resume').onclick = async () => {
            const button = element.querySelector('.resume'); button.disabled = true;
            try {
                const reason = window.prompt('Why should this candidate be unblocked? Enter 10–300 characters.');
                if (reason === null) return;
                if (reason.trim().length < 10 || reason.trim().length > 300) { status.textContent = 'Enter a review reason in 10–300 characters.'; return; }
                const response = await LiveProctoring.post('/proctoring-state/control', { attemptId: row.attemptId, resume: true, reason: reason.trim() }, token);
                const state = await response.json(); update(tile, { ...tile.row, ...state, alert: null });
            } catch { status.textContent = 'Resume failed. Refresh status and retry.'; }
            finally { button.disabled = false; }
        };
        grid.append(element); tiles.set(row.attemptId, tile); return tile;
    }
    function update(tile, row) {
        // An HTTP roster started before a push must not undo a newer lock/resume.
        if ((row.version || 0) < (tile.row.version || 0)) return;
        tile.row = row;
        const flagged = row.status === 'ProctoringBlocked' || !!(row.alert && row.alert.logId > tile.seen);
        tile.element.querySelector('.strikes').textContent = row.status === 'ProctoringBlocked' ? 'BLOCKED' : row.blockingMode === "MANUAL_ONLY" ? `${row.strikeCount || 0} recorded · Teacher decision` : `${Math.min(3, row.strikeCount || 0)}/3`;
        tile.element.querySelector('.resume').hidden = row.status !== 'ProctoringBlocked';
        tile.element.classList.toggle('needs-review', !!flagged);
        const reasons = { HeadAway: 'Head turned away from the screen', GazeAway: 'Eyes looking away from the screen', LookAway: 'Looking away from the screen', FaceNotDetected: 'Candidate not visible for 10 seconds', MultipleFaces: 'Multiple people detected in camera view', TalkingDetected: 'Candidate speech detected', AnswerPromptSpeech: 'Exam-related discussion detected', ElectronicDeviceDetected: 'Prohibited object near candidate — inspect evidence', TabSwitch: 'Exam page left', FullscreenExit: 'Fullscreen exited' };
        const identityReason = row.alert?.kind === 'IdentityMismatch'
            ? row.status === 'ProctoringBlocked'
                ? 'Identity mismatch confirmed after fresh checks — examination locked. Review the recorded evidence.'
                : row.alert?.description?.startsWith('Identity recheck pending:') ? 'Identity needs confirmation — fresh camera checks are running.' : 'Identity mismatch confirmed — urgent teacher review required.'
            : null;
        tile.element.querySelector('.tile-alert').textContent = flagged
            ? `${row.name || 'Candidate'}: ${identityReason || reasons[row.alert?.kind] || 'Review needed: ' + (row.alert?.kind || 'ExamBlocked').replace(/([a-z])([A-Z])/g, '$1 $2')}` : '';
        tile.element.querySelector('.tile-alert').title = flagged ? row.alert?.description || '' : '';
        tile.element.querySelector('.acknowledge').hidden = !flagged;
        tile.element.querySelector('.pause').disabled = row.status !== 'InProgress' || row.blockingMode === 'AUTO_ONLY';
        if (!['InProgress', 'ProctoringBlocked'].includes(row.status)) {
            tile.peer?.pc.close(); tile.peer = null; tile.video.srcObject = null;
            tile.element.querySelector('.connection').textContent = 'Paused — open review to authorize resume';
        }
    }
    async function roster() {
        try {
            const response = await fetch(`/Teacher/Monitoring/LiveRoster?examId=${examId}&page=${page}`, { signal: AbortSignal.timeout(10000) });
            if ([401,403,404].includes(response.status)) { for(const id of [...tiles.keys()]) remove(id); stopped=true; status.textContent='This monitoring invitation is no longer available.'; return; }
            if (!response.ok) throw new Error('Room unavailable');
            const result = await response.json(); page = result.page; pages = result.pages;
            for (const id of tiles.keys()) if (!result.rows.some(r => r.attemptId === id)) remove(id);
            for (const row of result.rows) {
                const tile = tiles.get(row.attemptId) || make(row); update(tile, row);
                if (['InProgress', 'ProctoringBlocked'].includes(row.status) && Date.now() - tile.watchAt > 10000) {
                    tile.watchAt = Date.now(); await send(row.attemptId, 'watch');
                }
            }
            status.textContent = result.count ? `${result.count} candidates · ${result.rows.length} shown` : 'No active candidates yet. This room updates automatically.';
            document.getElementById('live-page').textContent = `Page ${page} of ${pages}`;
            document.getElementById('live-prev').disabled = page <= 1;
            document.getElementById('live-next').disabled = page >= pages;
        } catch { status.textContent = 'Connection interrupted. Reconnecting…'; }
        finally { if (!stopped) setTimeout(roster, 4000); }
    }
    async function poll() {
        try {
            config ??= await LiveProctoring.configuration();
            const response = await LiveProctoring.post('/live-proctoring/poll', { viewer }, token);
            for (const message of await response.json()) {
                const tile = tiles.get(message.attemptId);
                if (!tile || !['InProgress', 'ProctoringBlocked'].includes(tile.row.status)) continue;
                if (message.kind === 'offer') {
                    tile.peer?.pc.close();
                    tile.peer = LiveProctoring.peer(config, (kind, data) => send(message.attemptId, kind, data),
                        stream => { tile.video.srcObject = stream; },
                        state => { tile.element.querySelector('.connection').textContent = state === 'connected' ? 'Live camera' : 'Camera reconnecting…'; });
                    await tile.peer.receive('offer', message.data);
                    await tile.peer.pc.setLocalDescription(await tile.peer.pc.createAnswer());
                    await send(message.attemptId, 'answer', JSON.stringify(tile.peer.pc.localDescription));
                    for (const ice of tile.pending || []) await tile.peer.receive('ice', ice);
                    tile.pending = [];
                } else if (message.kind === 'ice') {
                    if (tile.peer) await tile.peer.receive('ice', message.data);
                    else { tile.pending ??= []; if (tile.pending.length < 64) tile.pending.push(message.data); }
                }
            }
        } catch { /* Room status and connection labels remain visible during retries. */ }
        finally { if (!stopped) setTimeout(poll, 1500); }
    }
    document.getElementById('live-prev').onclick = () => { if (page > 1) { page--; for (const id of tiles.keys()) remove(id); } };
    document.getElementById('live-next').onclick = () => { if (page < pages) { page++; for (const id of tiles.keys()) remove(id); } };
    document.getElementById('pause-cancel').onclick = () => document.getElementById('pause-dialog').close();
    document.getElementById('pause-form').onsubmit = async event => {
        event.preventDefault(); const button = event.submitter; button.disabled = true;
        try {
            await LiveProctoring.post('/Teacher/Monitoring/PauseLive', { attemptId: pauseAttempt, reason: document.getElementById('pause-reason').value }, token);
            document.getElementById('pause-dialog').close();
        } catch { document.getElementById('pause-error').textContent = 'Could not pause. Check your reason and connection, then retry.'; }
        finally { button.disabled = false; }
    };
    window.addEventListener('pagehide', () => { stopped = true; for (const id of tiles.keys()) remove(id); });
    window.ProctoringRealtime?.connection.on('CandidateStateChanged', state => {
        const tile = tiles.get(state.attemptId);
        if (tile && state.version >= (tile.row.version || 0)) update(tile, { ...tile.row, ...state,
            alert: state.violationType ? { logId: state.eventId, kind: state.violationType } : null });
    });
    const appealIds = new Set();
    window.ProctoringRealtime?.connection.on('LockReviewRequested', appeal => {
        if (appealIds.has(appeal.appealId)) return;
        appealIds.add(appeal.appealId);
        let notices = document.getElementById('lock-review-notices');
        if (!notices) { notices = document.createElement('div'); notices.id = 'lock-review-notices'; notices.setAttribute('aria-live', 'polite'); document.getElementById('live-room').prepend(notices); }
        const notice = document.createElement('div'); notice.className = 'alert alert-warning d-flex flex-wrap gap-2 align-items-center';
        const text = document.createElement('strong'); text.textContent = `${appeal.candidateName} requests lock review · Attempt #${appeal.attemptId}`;
        const link = document.createElement('a'); link.className = 'btn btn-sm btn-dark'; link.textContent = 'Review violation audit'; link.href = appeal.reviewUrl;
        notice.append(text, link); notices.prepend(notice);
        while (notices.children.length > 10) notices.lastElementChild.remove();
    });
    if (examId) { roster(); poll(); } else status.textContent = 'Create an exam first, then open its live room.';
})();

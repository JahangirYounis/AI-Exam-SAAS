(() => {
    'use strict';
    const root = document.getElementById('teacher-alerts');
    if (!root) return;
    const list = root.querySelector('[data-alert-list]'), count = root.querySelector('[data-alert-count]');
    const status = root.querySelector('[data-alert-status]'), toast = document.getElementById('teacher-alert-toast');
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const connection = window.ProctoringRealtime.connection;
    const seen = new Set();
    let initialized = false, busy = false, stopped = false, nextBefore = null, audio = null, sound = false;
    function safeUrl(value) { const url = new URL(value, location.origin); return url.origin === location.origin ? url.href : '#'; }
    async function mark(id) {
        const data = new FormData(); data.append('id', id); data.append('__RequestVerificationToken', token);
        const response = await fetch('/Teacher/Alerts/Read', { method: 'POST', body: data });
        if (!response.ok) throw Error('Read state could not be saved');
    }
    async function announce(item) {
        if (!document.hidden) {
            toast.replaceChildren();
            const link = document.createElement('a'); link.href = safeUrl(item.url);
            link.textContent = `${item.student} · ${item.title} · ${item.exam}`;
            toast.append(link); toast.hidden = false;
            setTimeout(() => { toast.hidden = true; }, 8000);
        }
        // Exclusive origin lock prevents duplicate OS/sound alerts across teacher tabs.
        const deliver = () => {
            const key = `teacher-alert:${root.dataset.account}:${item.id}`;
            try { if (localStorage.getItem(key)) return; localStorage.setItem(key, String(Date.now())); } catch { return; }
            if (sound && audio?.state === 'running') {
                const oscillator = audio.createOscillator(), gain = audio.createGain();
                gain.gain.value = .05; oscillator.connect(gain); gain.connect(audio.destination);
                oscillator.start(); oscillator.stop(audio.currentTime + .12);
            }
            if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
                const notification = new Notification('Examination alert', { body: 'A monitoring update is available. Open the application to review.', tag: `incident-${item.id}` });
                notification.onclick = () => { window.focus(); location.assign(safeUrl(item.url)); notification.close(); };
            }
        };
        if (navigator.locks) await navigator.locks.request('teacher-alert-delivery', deliver);
        // Without cross-tab locking, in-app alerts remain available; do not race OS popups.
    }
    function render(item) {
        const entry = document.createElement('li'); entry.className = 'border-bottom py-2';
        const link = document.createElement('a'); link.href = safeUrl(item.url);
        link.textContent = `${item.student} · ${item.title}`;
        const detail = document.createElement('div'); detail.className = 'small';
        const duration = item.durationSeconds == null ? '' : ` · ${Number(item.durationSeconds).toFixed(1)} seconds`;
        const absence = duration + (item.title === 'Student Absence' ? ` · ${item.incidentEndedAt ? 'Student returned' : item.recoveryStatus === 'Absent' ? 'Student absent' : 'Return verification pending'}` : '');
        detail.textContent = `${item.exam} · ${item.category} · ${item.priority} · ${new Date(item.occurredAt).toLocaleString()} · ${item.hasEvidence ? 'Evidence available' : 'Evidence pending or unavailable'}${absence}`;
        const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-outline-secondary';
        button.textContent = item.read ? 'Read' : 'Mark read'; button.disabled = item.read;
        button.onclick = async () => { button.disabled = true; try { await mark(item.id); await sync(); } catch { button.disabled = false; status.textContent = 'Could not save read state. Retry.'; } };
        link.onclick = () => { mark(item.id).catch(() => {}); };
        entry.append(link, detail, button);
        if (item.liveUrl) {
            const live = document.createElement('a');
            live.href = safeUrl(item.liveUrl); live.textContent = 'Live camera';
            entry.append(live);
        }
        return entry;
    }
    async function sync(older = false) {
        if (busy || stopped) return;
        busy = true;
        try {
            const response = await fetch('/Teacher/Alerts/Feed' + (older && nextBefore ? `?before=${nextBefore}` : ''), { cache: 'no-store', signal: AbortSignal.timeout(10000) });
            if (response.status === 401 || response.status === 403) { stopped = true; list.replaceChildren(); count.textContent = '0'; return; }
            if (!response.ok) throw Error();
            const result = await response.json();
            count.textContent = result.unread; nextBefore = result.nextBefore;
            if (!older) list.replaceChildren();
            for (const item of result.items) {
                if (!older && initialized && !seen.has(item.id) && !item.read) await announce(item);
                seen.add(item.id); list.append(render(item));
            }
            while (seen.size > 1000) seen.delete(seen.values().next().value);
            while (list.children.length > 200) list.firstElementChild.remove();
            initialized = true;
            root.querySelector('[data-alert-older]').disabled = !nextBefore;
            status.textContent = result.items.length ? 'Saved monitoring history' : 'No monitoring alerts';
        } catch { status.textContent = 'Connection interrupted. Saved alerts will reload automatically.'; }
        finally { busy = false; }
    }
    root.querySelector('[data-alert-older]').onclick = () => sync(true);
    root.querySelector('[data-alert-permission]').onclick = async () => {
        if (!('Notification' in window)) { status.textContent = 'Browser notifications are unavailable.'; return; }
        const permission = await Notification.requestPermission();
        status.textContent = permission === 'granted' ? 'Private browser alerts enabled while an application tab remains open.' : 'In-app alerts remain enabled.';
    };
    root.querySelector('[data-alert-sound]').onclick = async event => {
        try { audio ??= new (window.AudioContext || window.webkitAudioContext)(); await audio.resume(); sound = !sound; event.target.textContent = sound ? 'Sound on' : 'Sound off'; }
        catch { status.textContent = 'Sound is unavailable; visual alerts remain enabled.'; }
    };
    connection.on('CandidateStateChanged', () => sync());
    connection.onreconnected(() => sync());
    document.addEventListener('visibilitychange', () => { if (!document.hidden) sync(); });
    window.addEventListener('pagehide', () => { stopped = true; clearInterval(timer); audio?.close(); });
    const timer = setInterval(() => sync(), 15000); sync();
})();

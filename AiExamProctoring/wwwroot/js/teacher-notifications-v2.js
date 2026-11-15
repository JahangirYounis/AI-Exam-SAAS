/* Task 03: one authoritative teacher inbox. No automatic "read" when opened. */
(() => {
    'use strict';
    const root = document.getElementById('teacher-notifications');
    if (!root) return;
    const $ = selector => root.querySelector(selector);
    const list = $('[data-unified-list]');
    const badge = $('[data-unified-count]');
    const status = $('[data-unified-status]');
    const more = $('[data-tn-more]');
    const allButton = $('[data-tn-read-all]');
    const categorySelect = $('[data-tn-category]');
    const token = $('input[name="__RequestVerificationToken"]')?.value;
    if (!list || !badge || !status || !token) return;

    const urls = {
        feed: '/Teacher/NotificationCenter/Feed',
        open: '/Teacher/NotificationCenter/Open',
        read: '/Teacher/NotificationCenter/Read',
        readAll: '/Teacher/NotificationCenter/ReadAll'
    };
    let tab = 'All', category = 'All', page = 0, loading = false, pending = false;
    let generation = 0, polling = null, initialized = false, audio = null, sound = false;
    const seen = new Set(), knownAlerts = new Set();

    const create = (tag, className = '', value) => {
        const el = document.createElement(tag);
        el.className = className;
        if (value !== undefined && value !== null) el.textContent = String(value);
        return el;
    };
    function validUrl(candidate) {
        try {
            const url = new URL(candidate, location.origin);
            return url.origin === location.origin && ['https:', 'http:'].includes(url.protocol) ? url.href : null;
        } catch { return null; }
    }
    function dateText(value) {
        if (!value) return 'Earlier';
        const date = new Date(value);
        return isNaN(date.getTime()) ? 'Earlier' : date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
    }
    async function post(url, fields = {}) {
        const body = new URLSearchParams({ __RequestVerificationToken: token, ...fields });
        const response = await fetch(url, {
            method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
            body
        });
        if (!response.ok) throw new Error(response.status === 404
            ? 'This notification is no longer available to your account.'
            : `Could not save notification (${response.status}).`);
        return response;
    }
    function crossTabUpdate() {
        try { localStorage.setItem('teacher-notifications:' + root.dataset.account, String(Date.now())); }
        catch { /* localStorage may be unavailable */ }
    }
    async function announce(item) {
        if (item.read || Number(item.priority) > 1) return;
        const alertKey = `teacher-alert:${root.dataset.account}:${item.kind}:${item.entryId}`;
        const notify = () => {
            try {
                if (localStorage.getItem(alertKey)) return;
                localStorage.setItem(alertKey, String(Date.now()));
            } catch { return; }
            if (sound && audio?.state === 'running') {
                try {
                    const tone = audio.createOscillator(), volume = audio.createGain();
                    volume.gain.value = .04;
                    tone.connect(volume); volume.connect(audio.destination);
                    tone.start(); tone.stop(audio.currentTime + .12);
                } catch { /* sound is optional */ }
            }
            if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
                const n = new Notification('Teacher examination update', {
                    body: `${item.candidate || 'Candidate'} · ${item.exam || 'Examination'} · ${item.title || 'Review required'}`,
                    tag: alertKey
                });
                n.onclick = () => { window.focus(); root.open = true; n.close(); };
            }
        };
        // No duplicate OS notifications from several tabs for the same account.
        if (navigator.locks) await navigator.locks.request('teacher-alert-delivery', notify);
    }
    function render(item) {
        const li = create('li', 'tn-item' + (item.read ? ' is-read' : ' is-unread'));
        li.dataset.key = `${item.kind}:${item.entryId}`;
        const symbol = {
            'Review Requests': 'bi-clipboard2-check', Monitoring: 'bi-shield-exclamation',
            Messages: 'bi-chat-dots', Invitations: 'bi-envelope-open',
            Connections: 'bi-people', Examinations: 'bi-journal-check'
        }[item.category] || 'bi-bell';
        const icon = create('span', 'tn-item-icon' + (item.priority <= 1 ? ' is-urgent' : ''));
        const iconGlyph = create('i', `bi ${symbol}`);
        iconGlyph.setAttribute('aria-hidden', 'true');
        icon.append(iconGlyph);
        const content = create('div', 'tn-item-content');
        const title = create('span', 'tn-item-title', item.title || 'Teacher update');
        const description = create('span', 'tn-item-description', item.detail || '');
        const meta = create('span', 'tn-item-meta');
        meta.append(create('span', '', item.category || 'Updates'), create('span', '', dateText(item.occurredAtUtc)));
        if (item.incidentCount > 1) meta.append(create('span', 'tn-incident-count', `${item.incidentCount} events`));
        content.append(title, description, meta);
        const actions = create('div', 'tn-item-actions');
        const liveUrl = item.liveUrl && validUrl(item.liveUrl);
        if (liveUrl) {
            const liveLink = create('a', 'tn-open-button', 'Live camera');
            liveLink.href = liveUrl;
            actions.append(liveLink);
        }
        if (!item.read) {
            const dot = create('span', 'tn-new-dot');
            dot.title = 'Unread';
            actions.append(dot);
        }
        const readButton = create('button', 'tn-read-button', item.read ? 'Read' : 'Mark read');
        readButton.type = 'button';
        readButton.disabled = Boolean(item.read);
        readButton.setAttribute('aria-label', `Mark ${item.title || 'notification'} as read`);
        readButton.addEventListener('click', async () => {
            readButton.disabled = true;
            try {
                await post(urls.read, { kind: item.kind, entryId: String(item.entryId) });
                crossTabUpdate(); await refresh();
            } catch (error) { status.textContent = error.message; readButton.disabled = false; }
        });
        const openButton = create('button', 'tn-open-button', 'View');
        openButton.type = 'button';
        openButton.setAttribute('aria-label', `View ${item.title || 'notification'}`);
        openButton.addEventListener('click', async () => {
            openButton.disabled = true;
            try {
                const response = await post(urls.open, { kind: item.kind, entryId: String(item.entryId) });
                const data = await response.json();
                const destination = validUrl(data.url);
                if (!destination) throw new Error('A secure destination is unavailable.');
                crossTabUpdate(); location.assign(destination);
            } catch (error) { status.textContent = error.message; openButton.disabled = false; }
        });
        actions.append(readButton, openButton);
        li.append(icon, content, actions);
        return li;
    }
    async function load(append = false) {
        if (loading) { pending = true; return; }
        loading = true;
        const requestGeneration = generation;
        const requestPage = append ? page + 1 : 0;
        try {
            const url = new URL(urls.feed, location.origin);
            url.searchParams.set('page', String(requestPage));
            url.searchParams.set('category', category);
            url.searchParams.set('unreadOnly', String(tab === 'Unread').toLowerCase());
            const response = await fetch(url.href, { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error(`Notifications unavailable (${response.status}).`);
            const data = await response.json();
            if (requestGeneration !== generation) return;
            if (!append) { list.replaceChildren(); seen.clear(); page = 0; }
            else page = requestPage;
            const items = Array.isArray(data.items) ? data.items : [];
            for (const item of items) {
                const key = `${item.kind}:${item.entryId}`;
                if (seen.has(key)) continue;
                seen.add(key);
                if (initialized && !knownAlerts.has(key)) void announce(item);
                knownAlerts.add(key);
                list.append(render(item));
            }
            initialized = true;
            // Bound client-side dedup memory; the server remains authoritative.
            while (knownAlerts.size > 1000) knownAlerts.delete(knownAlerts.values().next().value);
            const unread = Math.max(0, Number(data.unread || 0));
            badge.textContent = String(unread > 99 ? '99+' : unread);
            badge.hidden = unread === 0;
            more.hidden = !data.hasMore;
            more.disabled = false;
            if (list.children.length === 0)
                list.append(create('li', 'tn-empty', tab === 'Unread'
                    ? 'All caught up. There are no unread notifications.'
                    : 'No notifications in this category yet.'));
            status.textContent = unread ? `${unread} unread notification${unread === 1 ? '' : 's'}`
                : 'All caught up · Past updates remain available.';
        } catch (error) { status.textContent = error.message || 'Notifications could not be loaded.'; }
        finally {
            loading = false;
            if (pending) { pending = false; void load(false); }
        }
    }
    function refresh() { generation++; return load(false); }
    root.addEventListener('toggle', () => { if (root.open) void refresh(); });
    $('[data-tn-close]')?.addEventListener('click', () => { root.open = false; });
    for (const button of root.querySelectorAll('[data-tn-tab]')) {
        button.addEventListener('click', () => {
            tab = button.dataset.tnTab || 'All';
            for (const candidate of root.querySelectorAll('[data-tn-tab]')) {
                const active = candidate === button;
                candidate.classList.toggle('is-active', active);
                candidate.setAttribute('aria-pressed', String(active));
            }
            void refresh();
        });
    }
    categorySelect.addEventListener('change', () => { category = categorySelect.value; void refresh(); });
    allButton?.addEventListener('click', async () => {
        allButton.disabled = true;
        try { await post(urls.readAll); crossTabUpdate(); await refresh(); }
        catch (error) { status.textContent = error.message || 'Could not mark notifications read.'; }
        finally { allButton.disabled = false; }
    });
    $('[data-tn-browser]')?.addEventListener('click', async event => {
        if (!('Notification' in window)) { status.textContent = 'Desktop alerts are unavailable in this browser.'; return; }
        const permission = await Notification.requestPermission();
        event.currentTarget.textContent = permission === 'granted' ? 'Desktop alerts enabled' : 'Desktop alerts unavailable';
        status.textContent = permission === 'granted' ? 'Urgent updates can appear as desktop alerts.' : 'In-app notifications remain available.';
    });
    $('[data-tn-sound]')?.addEventListener('click', async event => {
        try {
            audio ??= new (window.AudioContext || window.webkitAudioContext)();
            await audio.resume(); sound = !sound;
            event.currentTarget.textContent = sound ? 'Sound on' : 'Sound off';
        } catch { status.textContent = 'Notification sound is unavailable. Visual alerts remain active.'; }
    });
    more.addEventListener('click', () => { more.disabled = true; void load(true); });
    document.addEventListener('pointerdown', event => {
        if (root.open && !root.contains(event.target)) root.open = false;
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') root.open = false; });
    window.addEventListener('storage', event => {
        if (event.key === 'teacher-notifications:' + root.dataset.account) void refresh();
    });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) void refresh(); });
    window.addEventListener('teacher-collaboration-changed', () => { void refresh(); });
    const conn = window.ProctoringRealtime?.connection;
    conn?.on?.('CandidateStateChanged', () => { void refresh(); });
    conn?.on?.('LockReviewRequested', () => { void refresh(); });
    conn?.onreconnected?.(() => { void refresh(); });
    polling = setInterval(() => { void refresh(); }, 30000);
    window.addEventListener('pagehide', () => { if (polling) clearInterval(polling); audio?.close(); }, { once: true });
    void refresh();
})();

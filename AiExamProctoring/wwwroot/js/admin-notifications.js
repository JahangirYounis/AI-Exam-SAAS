
(() => {
    'use strict';
    const root = document.getElementById('admin-notifications');
    if (!root) return;
    const list = root.querySelector('[data-list]');
    const count = root.querySelector('[data-count]');
    const status = root.querySelector('[data-status]');
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const filter = root.querySelector('[data-history]');
    const older = root.querySelector('[data-older]');
    if (!list || !count || !status || !token || !filter || !older) return;
    let history = true, page = 0, busy = false, pending = false, marking = false;
    filter.textContent = 'Unread only';
    function syncTabs() { try { localStorage.setItem('notification-reads', String(Date.now())); } catch { } }
    function safeUrl(value) {
        try { const u = new URL(value || '/', location.origin); return u.origin === location.origin ? u.href : null; }
        catch { return null; }
    }
    function el(tag, css, value) { const item = document.createElement(tag); if (css) item.className = css; if (value != null) item.textContent = String(value); return item; }
    async function post(path) {
        const response = await fetch(path, { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: token } });
        if (!response.ok) throw Error('Could not save read status.');
    }
    async function load() {
        if (busy) { pending = true; return; }
        busy = true;
        try {
            const response = await fetch(`/Admin/Notification/Recent?history=${history}&page=${page}`, { cache: 'no-store', credentials: 'same-origin' });
            if (!response.ok) throw Error('Notifications unavailable.');
            const data = await response.json();
            const unread = Number(data.unread || 0);
            count.textContent = unread ? String(unread) : '';
            count.hidden = unread === 0;
            list.replaceChildren();
            const entries = data.items || [];
            for (const item of entries) {
                const link = el('a', `d-block border-bottom rounded p-2 text-decoration-none ${item.read ? 'text-body' : 'bg-light text-body'}`);
                link.href = safeUrl(item.url) || '#';
                const head = el('span', 'd-flex align-items-center justify-content-between gap-2');
                head.append(el('strong', 'small', item.title));
                if (!item.read) head.append(el('span', 'badge text-bg-primary', 'New'));
                const detail = el('span', 'd-block small mt-1', item.message || '');
                const meta = el('span', 'd-block small text-secondary mt-1', `${item.category || 'Account'} · ${item.createdAt ? new Date(item.createdAt).toLocaleString() : ''}`);
                link.append(head, detail, meta);
                link.addEventListener('click', async event => {
                    event.preventDefault();
                    if (!safeUrl(item.url)) { status.textContent = 'Destination unavailable.'; return; }
                    try { await post('/Admin/Notification/Read?id=' + encodeURIComponent(item.id)); syncTabs(); location.assign(link.href); }
                    catch (error) { status.textContent = error.message; }
                });
                list.append(link);
            }
            if (!entries.length) list.append(el('div', 'small text-secondary p-3', history ? 'No notifications yet.' : 'No unread notifications. Select All history.'));
            older.disabled = entries.length < 20;
            status.textContent = unread ? `${unread} unread · Read items stay in history.` : 'All caught up · Previous notifications are retained.';
            if (root.open && unread > 0 && !marking && history && page === 0) Promise.resolve().then(markAll);
        } catch (error) { status.textContent = error.message || 'Notifications unavailable.'; }
        finally { busy = false; if (pending) { pending = false; void load(); } }
    }
    async function markAll() {
        if (marking) return;
        marking = true;
        try { await post('/Admin/Notification/MarkAllRead'); syncTabs(); await load(); }
        catch (error) { status.textContent = error.message || 'Unable to mark read.'; }
        finally { marking = false; }
    }
    root.addEventListener('toggle', () => { if (root.open) { history = true; page = 0; filter.textContent = 'Unread only'; void load(); void markAll(); } });
    root.querySelector('[data-read]')?.addEventListener('click', () => { page = 0; void markAll(); });
    filter.addEventListener('click', () => { history = !history; page = 0; filter.textContent = history ? 'Unread only' : 'All history'; void load(); });
    older.addEventListener('click', () => { page += 1; void load(); });
    window.addEventListener('storage', event => { if (event.key === 'notification-reads') { page = 0; void load(); } });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { page = 0; void load(); } });
    const timer = setInterval(() => { page = 0; void load(); }, 15000);
    window.addEventListener('pagehide', () => clearInterval(timer), { once: true });
    void load();
})();
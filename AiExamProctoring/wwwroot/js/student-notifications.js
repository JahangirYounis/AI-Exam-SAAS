
(() => {
    'use strict';
    const menu = document.getElementById('notificationMenu');
    const badge = document.getElementById('notificationBadge');
    const list = document.getElementById('notificationList');
    if (!menu || !badge || !list) return;
    const markAll = document.getElementById('markNotificationsRead');
    const token = document.querySelector('#notificationTokenForm input[name="__RequestVerificationToken"]')?.value || '';
    if (!token) return;
    let history = true, page = 0, lastUnread = null, loading = false, marking = false;
    let rerun = false;
    const controls = document.createElement('div');
    controls.className = 'd-flex gap-2 align-items-center px-3 pt-2';
    const filter = document.createElement('button');
    filter.type = 'button'; filter.className = 'btn btn-sm btn-outline-secondary'; filter.textContent = 'Unread only';
    const older = document.createElement('button');
    older.type = 'button'; older.className = 'btn btn-sm btn-link'; older.textContent = 'Older history';
    controls.append(filter, older); list.before(controls);

    function category(value) {
        const type = String(value || '').toLowerCase();
        if (type.includes('result') || type.includes('released')) return 'Results';
        if (type.includes('resume') || type.includes('block') || type.includes('proctor')) return 'Exam status';
        if (type.includes('exam') || type.includes('assignment') || type.includes('public')) return 'Examinations';
        if (type.includes('message') || type.includes('chat')) return 'Messages';
        if (type.includes('approval') || type.includes('account')) return 'Account';
        return 'Updates';
    }
    function el(tag, cls, text) {
        const node = document.createElement(tag);
        if (cls) node.className = cls;
        if (text != null) node.textContent = String(text);
        return node;
    }
    function sameOriginUrl(value) {
        try {
            const url = new URL(value || '/', location.origin);
            return url.origin === location.origin && /^https?:$/.test(url.protocol) ? url.href : null;
        } catch { return null; }
    }
    function broadcast() { try { localStorage.setItem('notification-reads', String(Date.now())); } catch { } }
    async function post(url, body) {
        const headers = { RequestVerificationToken: token };
        if (body) headers['Content-Type'] = 'application/x-www-form-urlencoded; charset=UTF-8';
        const response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers, body });
        if (!response.ok) throw Error('Could not save read status.');
    }
    function toastNotification(item) {
        if (!window.bootstrap || menu.querySelector('.dropdown-menu')?.classList.contains('show')) return;
        const holder = el('div', 'toast-container position-fixed top-0 end-0 p-3');
        holder.style.zIndex = '1090';
        const toast = el('div', 'toast'); toast.setAttribute('role', 'alert');
        const head = el('div', 'toast-header');
        head.append(el('strong', 'me-auto', `${category(item.type)} · ${item.title || 'Update'}`));
        const close = el('button', 'btn-close'); close.type = 'button'; close.setAttribute('data-bs-dismiss', 'toast'); head.append(close);
        toast.append(head, el('div', 'toast-body', item.message || ''));
        holder.append(toast); document.body.append(holder);
        new bootstrap.Toast(toast, { delay: 7000 }).show();
        toast.addEventListener('hidden.bs.toast', () => holder.remove(), { once: true });
    }
    function render(item) {
        const link = el('a', 'dropdown-item text-wrap px-3 py-3 border-bottom ' + (item.isRead ? '' : 'bg-primary-subtle'));
        link.href = sameOriginUrl(item.url) || '#';
        link.dataset.notificationId = String(item.id);
        const head = el('div', 'd-flex justify-content-between gap-2');
        head.append(el('strong', 'small', item.title || 'Notification'));
        if (!item.isRead) head.append(el('span', 'badge bg-primary', 'New'));
        const detail = el('div', 'small text-muted mt-1', item.message || '');
        const date = item.createdAt ? new Date(item.createdAt) : null;
        const meta = el('div', 'small text-secondary mt-2', `${category(item.type)} · ${date && !Number.isNaN(date.valueOf()) ? date.toLocaleString() : 'Earlier'}`);
        link.append(head, detail, meta);
        link.addEventListener('click', async event => {
            event.preventDefault();
            if (!sameOriginUrl(item.url)) { list.prepend(el('div', 'text-danger small p-2', 'Destination unavailable.')); return; }
            link.style.pointerEvents = 'none';
            try {
                await post('/Student/Notification/MarkRead', new URLSearchParams({ ids: String(item.id) }));
                broadcast(); location.assign(link.href);
            } catch (error) {
                link.style.pointerEvents = '';
                list.prepend(el('div', 'text-danger small p-2', error.message));
            }
        });
        return link;
    }
    async function loadNotifications(showToast = false) {
        if (loading) { rerun = true; return; }
        loading = true;
        try {
            const response = await fetch(`/Student/Notification/Recent?history=${history}&page=${page}`, { cache: 'no-store', credentials: 'same-origin' });
            if (!response.ok) throw Error('Notifications unavailable.');
            const data = await response.json();
            const unread = Number(data.unread || 0);
            badge.textContent = unread > 99 ? '99+' : String(unread);
            badge.classList.toggle('d-none', unread === 0);
            const items = Array.isArray(data.items) ? data.items : [];
            list.replaceChildren();
            if (!items.length) list.append(el('div', 'small text-muted text-center py-4', history ? 'No notifications yet.' : 'No unread notifications. Use All history to view earlier updates.'));
            for (const item of items) list.append(render(item));
            older.disabled = items.length < 8;
            if (showToast && lastUnread != null && unread > lastUnread && items.length) {
                const newest = items.find(x => !x.isRead);
                if (newest) toastNotification(newest);
            }
            lastUnread = unread;
            // Opening the menu is not a read acknowledgement.
        } catch (error) {
            list.replaceChildren(el('div', 'small text-danger p-3', error.message || 'Could not load notifications.'));
        } finally {
            loading = false;
            if (rerun) { rerun = false; void loadNotifications(false); }
        }
    }
    async function markReadOnView() {
        if (marking) return;
        marking = true;
        try {
            await post('/Student/Notification/MarkAllRead');
            broadcast(); lastUnread = 0;
            await loadNotifications(false);
        } catch (error) {
            list.prepend(el('div', 'small text-danger p-2', error.message));
        } finally { marking = false; }
    }
    filter.addEventListener('click', () => {
        history = !history; page = 0;
        filter.textContent = history ? 'Unread only' : 'All history';
        void loadNotifications(false);
    });
    older.addEventListener('click', () => { page++; void loadNotifications(false); });
    markAll?.addEventListener('click', () => { page = 0; void markReadOnView(); });
    menu.addEventListener('shown.bs.dropdown', () => {
        history = true; page = 0; filter.textContent = 'Unread only';
        void loadNotifications(false);
    });
    window.addEventListener('storage', event => { if (event.key === 'notification-reads') { page = 0; void loadNotifications(false); } });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { page = 0; void loadNotifications(false); } });
    void loadNotifications(false);
    const timer = window.setInterval(() => {
        if (!document.hidden) { page = 0; void loadNotifications(true); }
    }, 30000);
    window.addEventListener('pagehide', () => clearInterval(timer), { once: true });
})();

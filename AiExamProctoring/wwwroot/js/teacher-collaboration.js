(() => {
    if (!window.signalR || window.teacherCollaboration) return;
    const connection = new window.signalR.HubConnectionBuilder().withUrl('/hubs/teachers').withAutomaticReconnect().build();
    window.teacherCollaboration = connection;
    function receive(event) {
        const chat = document.getElementById('live-chat');
        if (chat && chat.dataset.kind === event.kind && chat.dataset.room === String(event.roomId)) {
            if ([...chat.children].some(item => item.dataset.messageId === String(event.entryId))) return;
            const item = document.createElement('article'); item.className = 'card p-3 mb-2'; item.dataset.messageId = String(event.entryId);
            const label = document.createElement('small'); label.textContent = `${event.sender || 'Teacher'} · ${new Date(event.sentAtUtc).toLocaleString()}`;
            const body = document.createElement('p'); body.textContent = event.body; body.style.whiteSpace = 'pre-wrap'; body.style.overflowWrap = 'anywhere';
            item.append(label, body);
            const later = [...chat.children].find(child => Number(child.dataset.messageId) > Number(event.entryId));
            if (later) chat.insertBefore(item, later); else chat.append(item);
            return;
        }
        let notice = document.getElementById('collaboration-notice');
        if (!notice) {
            notice = document.createElement('aside'); notice.id = 'collaboration-notice';
            notice.className = 'alert alert-info position-fixed bottom-0 end-0 m-3 shadow';
            notice.style.zIndex = '1080'; notice.style.maxWidth = 'min(420px, 90vw)'; notice.setAttribute('role', 'status');
            document.body.append(notice);
        }
        notice.replaceChildren();
        const message = document.createElement('p'); message.textContent = event.message; notice.append(message);
        const link = document.createElement('a'); link.href = '/Teacher/Invitations'; link.textContent = 'Exam invitations'; link.className = 'me-3'; notice.append(link);
        const directory = document.createElement('a'); directory.href = '/Teacher/Connections'; directory.textContent = 'Teacher connections'; notice.append(directory);
        const refresh = document.createElement('button'); refresh.type = 'button'; refresh.className = 'btn btn-sm btn-primary ms-2'; refresh.textContent = 'Refresh messages'; refresh.onclick = () => {
            const draft = [...document.querySelectorAll('textarea')].some(t => t.value.trim());
            if (!draft || window.confirm('You have an unsent draft. Refresh anyway?')) window.location.reload();
        }; notice.append(refresh);
        const close = document.createElement('button'); close.type = 'button'; close.className = 'btn-close ms-2'; close.setAttribute('aria-label', 'Dismiss notification'); close.onclick = () => notice.remove(); notice.append(close);
    }
    connection.on('CollaborationChanged', receive);
    let recovering = false;
    let recoveredThrough = Math.max(0, ...[...(document.getElementById("live-chat")?.children || [])].map(item => Number(item.dataset.messageId) || 0));
    async function recover() {
        const chat = document.getElementById('live-chat');
        if (!chat || recovering) return;
        recovering = true;
        try {
            for (let page = 0; page < 20; page++) {
                const after = recoveredThrough;
                const endpoint = chat.dataset.kind === 'team' ? '/Teacher/Invitations/Messages?examId=' : '/Teacher/Connections/Messages?id=';
                const response = await fetch(endpoint + encodeURIComponent(chat.dataset.room) + '&after=' + after, { credentials: 'same-origin', cache: 'no-store' });
                if (!response.ok) return;
                const messages = await response.json(); messages.forEach(receive);
                recoveredThrough = Math.max(recoveredThrough, ...messages.map(message => Number(message.entryId) || 0));
                if (messages.length < 100) break;
            }
        } catch { /* Persisted messages remain available on the next reconnect or refresh. */ }
        finally { recovering = false; }
    }
    connection.onreconnected(recover);
    async function start() { try { await connection.start(); await recover(); } catch { setTimeout(start, 5000); } }
    connection.onclose(start); start();
})();

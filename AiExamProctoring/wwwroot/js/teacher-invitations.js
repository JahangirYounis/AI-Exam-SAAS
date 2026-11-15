(() => {
  const badge = document.getElementById('invitationBadge');
  if (!badge) return;
  let busy = false;
  async function refresh() {
    if (busy || document.hidden) return;
    busy = true;
    try {
      const response = await fetch('/Teacher/Invitations/Pending', {
        cache: 'no-store', credentials: 'same-origin'
      });
      if (response.ok) {
        const data = await response.json();
        const count = Number.isInteger(data.count) ? Math.max(0, data.count) : 0;
        badge.textContent = `Invites (${count})`;
        badge.classList.toggle('btn-warning', count > 0);
        badge.setAttribute('aria-label', `${count} pending exam collaboration invitations`);
      }
    } catch { /* Preserve the last known badge; polling retries. */ }
    finally { busy = false; }
  }
  window.addEventListener('teacher-collaboration-changed', refresh);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  window.setInterval(refresh, 15000);
  refresh();
})();

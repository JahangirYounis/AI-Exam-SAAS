(() => {
  const $ = (s, r=document) => r.querySelector(s);
  const $$ = (s, r=document) => [...r.querySelectorAll(s)];
  const sidebar = $('#studentSidebar'), backdrop = $('#studentBackdrop');
  const open = () => { sidebar?.classList.add('open'); backdrop?.classList.add('show'); };
  const close = () => { sidebar?.classList.remove('open'); backdrop?.classList.remove('show'); };
  $('#openSidebar')?.addEventListener('click', open); $('#closeSidebar')?.addEventListener('click', close); backdrop?.addEventListener('click', close);
  $$('.student-nav-link').forEach(x => x.addEventListener('click', close));
  const nav = $('.student-nav');
  const activeLink = $('.student-nav-link.active', nav || document);
  if (nav && activeLink) {
    const wantedTop = activeLink.offsetTop - nav.offsetTop - Math.max(16, (nav.clientHeight - activeLink.offsetHeight) / 2);
    nav.scrollTop = Math.max(0, wantedTop);
  }
  $$('.student-alert').forEach(x => setTimeout(() => bootstrap.Alert.getOrCreateInstance(x)?.close(), 6500));
  document.addEventListener('submit', e => { const f=e.target; if(f.matches('[data-prevent-double-submit]')) { const b=f.querySelector('button[type=submit]'); if(b){b.disabled=true;b.dataset.original=b.innerHTML;b.innerHTML='<span class="spinner-border spinner-border-sm me-2"></span>Processing...';} } });
})();

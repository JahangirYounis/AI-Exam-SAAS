(() => {
 'use strict';
 for(const picker of document.querySelectorAll('.teacher-picker')){
  const target=picker.querySelector('[data-picked-emails]');
  const summary=picker.querySelector('[data-selected-summary]');
  const count=picker.querySelector('[data-picked-count]');
  const results=picker.querySelector('[data-directory-results]');
  const status=picker.querySelector('[data-directory-status]');
  const more=picker.querySelector('[data-directory-more]');
  const search=picker.querySelector('[data-directory-search]');
  const selected=new Map();let page=1;let lastSearch='';let request=0;
  const renderSelection=()=>{
    target.value=Array.from(selected.values()).join(',');
    summary.textContent=selected.size ? `${selected.size} teacher(s) selected` : 'No teachers selected';
    count.textContent=`${selected.size} selected`;
  };
  async function load(append=false){
    const current=++request;const query=search.value.trim();
    if(!append){page=1;results.replaceChildren();lastSearch=query;}
    try{
      const url=new URL(picker.dataset.directoryUrl,location.href);
      url.searchParams.set('page',String(page));url.searchParams.set('examId',picker.dataset.examId||'0');
      if(query)url.searchParams.set('search',query);
      const res=await fetch(url,{credentials:'same-origin',cache:'no-store'});
      if(!res.ok)throw new Error('Teacher directory is unavailable.');
      const data=await res.json();
      if(request!==current||query!==lastSearch)return;
      for(const item of data.teachers){
       const label=document.createElement('label');label.className='d-flex align-items-center gap-3 border rounded-3 p-3';
       const check=document.createElement('input');check.type='checkbox';check.className='form-check-input';
       check.disabled=Boolean(item.invited||!item.email);check.checked=selected.has(item.id)||Boolean(item.invited);
       const text=document.createElement('span');
       const name=document.createElement('strong');name.textContent=item.name;
       const email=document.createElement('small');email.className='d-block text-secondary';email.textContent=item.email||'No email';
       text.append(name,email);
       if(item.invited){const badge=document.createElement('small');badge.className='text-success ms-auto';badge.textContent='Already invited';text.append(badge);}
       label.append(check,text);results.append(label);
       check.addEventListener('change',()=>{
         const limit=Number(picker.dataset.max)||20;
         if(check.checked&&selected.size>=limit){check.checked=false;alert(`Select at most ${limit} teachers at a time.`);return;}
         if(check.checked)selected.set(item.id,item.email);else selected.delete(item.id);renderSelection();
       });
      }
      status.textContent=data.teachers.length?'Select the teachers to invite. Already invited teachers are disabled.':'No matching teachers.';
      more.hidden=!data.hasMore;
    }catch(e){status.textContent=e.message||'Failed to load teachers.';more.hidden=true;}
  }
  more.addEventListener('click',()=>{page++;load(true);});
  let delay;search.addEventListener('input',()=>{clearTimeout(delay);delay=setTimeout(()=>load(false),250);});
  const modal=picker.querySelector('.modal');modal.addEventListener('show.bs.modal',()=>load(false));
  // Do not send a blank/permissionless invitation; keep the exam creation itself optional.
  const outerForm=picker.closest('form');
  if(outerForm)outerForm.addEventListener('submit',e=>{
    const permissions=[...picker.querySelectorAll('input[type=checkbox][name]:not([disabled])')].filter(i=>i.checked);
    if(selected.size && !permissions.length){e.preventDefault();alert('Select Question preparation, Live monitoring, or both.');}
  });
  renderSelection();
 }
})();

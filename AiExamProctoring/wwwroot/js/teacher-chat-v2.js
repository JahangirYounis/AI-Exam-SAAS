(() => {
  'use strict';
  const root = document.getElementById('teacher-chat');
  if (!root) return;
  const history = document.getElementById('tc-history');
  const form = document.getElementById('tc-form');
  const box = document.getElementById('tc-body');
  const errorBox = document.getElementById('tc-error');
  const reply = document.getElementById('tc-reply');
  const replyId = document.getElementById('tc-reply-id');
  const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
  const room = Number(root.dataset.room);
  const kind = root.dataset.kind;
  const actor = root.dataset.actor;
  const emojis = ['👍', '❤️', '😂', '😮', '😢', '😡'];
  let entries = [];
  let busy = false;
  let sending = false;
  let expanded = null;
  const el = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined && text !== null) node.textContent = String(text);
    return node;
  };
  function error(message) { errorBox.textContent = message; errorBox.hidden = !message; }
  function when(date) { const parsed = new Date(date); return Number.isNaN(parsed.valueOf()) ? '' : parsed.toLocaleString([], { hour: '2-digit', minute:'2-digit', day:'numeric',month:'short' }); }
  function render() {
    const nearBottom = history.scrollHeight - history.scrollTop - history.clientHeight < 120;
    const previousHeight = history.scrollHeight;
    history.replaceChildren();
    if (!entries.length) history.append(el('div','tc-loading','No messages yet. Start the conversation.'));
    const lookup = new Map(entries.map(m => [m.entryId, m]));
    entries.forEach(m => {
      const mine = m.senderId === actor;
      const line = el('article', `tc-line ${mine ? 'tc-mine' : 'tc-other'}`);
      line.dataset.id = m.entryId;
      const bubble = el('div','tc-bubble');
      const top = el('div','tc-message-top');
      top.append(el('strong','',mine ? 'You' : m.sender), el('time','tc-time',when(m.sentAtUtc)));
      bubble.append(top);
      if (m.replyToId) {
        const original = lookup.get(m.replyToId);
        const quote = el('div','tc-quote');
        quote.append(el('strong','',original?.sender || m.replySender || 'Earlier message'));
        quote.append(el('div','tc-ellipsis', original?.body || m.replyBody || 'Earlier message'));
        bubble.append(quote);
      }
      bubble.append(el('div','tc-text',m.body));
      const reactions = el('div','tc-reactions');
      for (const reaction of (m.reactions || [])) {
        const b = el('button', `tc-reaction ${reaction.mine ? 'tc-reacted' : ''}`,`${reaction.emoji} ${reaction.count}`);
        b.type = 'button'; b.dataset.react = reaction.emoji; b.dataset.id = m.entryId;
        b.setAttribute('aria-label', `React ${reaction.emoji}, ${reaction.count} reactions`);
        reactions.append(b);
      }
      bubble.append(reactions);
      const actions = el('div','tc-actions');
      const replyBtn = el('button','tc-action','↩ Reply');
      replyBtn.type = 'button'; replyBtn.dataset.reply = m.entryId;
      actions.append(replyBtn);
      const reactBtn = el('button','tc-action','☺ React');
      reactBtn.type = 'button'; reactBtn.dataset.picker = m.entryId;
      actions.append(reactBtn);
      bubble.append(actions);
      if (expanded === m.entryId) {
        const picker = el('div','tc-picker');
        emojis.forEach(emoji => {
          const b = el('button','tc-emoji',emoji);
          b.type='button'; b.dataset.react=emoji; b.dataset.id=m.entryId;
          b.setAttribute('aria-label',`React ${emoji}`);picker.append(b);
        });
        bubble.append(picker);
      }
      line.append(bubble);history.append(line);
    });
    if (nearBottom || previousHeight === 0) history.scrollTop = history.scrollHeight;
  }
  async function load() {
    if (busy || document.hidden) return;
    busy = true;
    try {
      const response = await fetch(root.dataset.readUrl, {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
      if (!response.ok) throw new Error(`Unable to retrieve messages (${response.status}).`);
      const data = await response.json();
      if (!Array.isArray(data)) throw new Error('Unexpected chat response.');
      entries = data;
      render();error('');
    } catch (e) { error(e.message || 'Could not load messages.'); }
    finally { busy = false; }
  }
  async function react(messageId, emoji) {
    try {
      const params = new URLSearchParams({kind,roomId:String(room),entryId:String(messageId),emoji,__RequestVerificationToken:token});
      const response = await fetch(root.dataset.reactUrl, {method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:params});
      if (!response.ok) throw new Error(`Reaction could not be saved (${response.status}).`);
      expanded=null;await load();
    } catch (e) {error(e.message);}
  }
  history.addEventListener('click',event=> {
    const reactButton=event.target.closest('[data-react]');
    if (reactButton) {react(Number(reactButton.dataset.id),reactButton.dataset.react);return;}
    const replyButton=event.target.closest('[data-reply]');
    if (replyButton) {
      const message=entries.find(e=>e.entryId === Number(replyButton.dataset.reply));
      if (!message)return;
      replyId.value=String(message.entryId);
      document.getElementById('tc-reply-author').textContent=message.senderId===actor?'You':message.sender;
      document.getElementById('tc-reply-text').textContent=message.body;
      reply.hidden=false;box.focus();return;
    }
    const pickerButton=event.target.closest('[data-picker]');
    if (pickerButton){const id=Number(pickerButton.dataset.picker);expanded=expanded===id?null:id;render();}
  });
  document.getElementById('tc-cancel-reply').addEventListener('click',()=>{replyId.value='';reply.hidden=true;box.focus();});
  box.addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();form.requestSubmit();}});
  form.addEventListener('submit',async e=>{
    e.preventDefault();if (sending)return;const body=box.value.trim();if (!body)return;
    sending=true;
    const button=document.getElementById('tc-send');button.disabled=true;
    try {
      const payload=new URLSearchParams(new FormData(form));
      const response=await fetch(form.action,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/x-www-form-urlencoded','X-Requested-With':'XMLHttpRequest'},body:payload});
      if (!response.ok) throw new Error(response.status === 400 ? 'Message could not be sent. Refresh the page and check your reply before trying again.' : response.status === 401 || response.status === 403 || response.status === 404 || response.redirected ? 'Your session or chat access has changed. Refresh the page to continue.' : 'Could not send your message. Your text is still here; please try again.');
      if (response.redirected || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Your session has expired. Sign in again to send this message.');
      box.value='';replyId.value='';reply.hidden=true;
      await load();history.scrollTop=history.scrollHeight;error('');
    } catch(e){error(e.message || 'Message was not sent.');}
    finally{sending=false;button.disabled=false;}
  });
  window.addEventListener('teacher-collaboration-changed', event => {
    const change = event.detail || {};
    if (!change.kind || (change.kind === kind && Number(change.roomId) === room)) load();
  });
  document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
  load();const timer=window.setInterval(load,5000);
  window.addEventListener('pagehide',()=>clearInterval(timer),{once:true});
})();

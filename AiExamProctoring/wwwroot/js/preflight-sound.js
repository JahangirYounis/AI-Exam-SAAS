(() => {
    const anchor = document.getElementById('checkDevices');
    if (!anchor) return;
    const button = document.createElement('button'); button.type='button';
    button.className='btn btn-outline-secondary mt-2'; button.textContent='Test warning sound';
    const note=document.createElement('p'); note.className='small mt-2'; note.setAttribute('role','status');
    anchor.after(button,note);
    const synth=window.speechSynthesis;
    const refresh=()=>{if(!synth)note.textContent='Voice playback is unavailable. Written warnings will remain visible.';
        else if(!synth.getVoices().some(v=>/^ur(?:-|$)/i.test(v.lang)))note.textContent='An Urdu voice is not available yet. The sound test will use English; written warnings remain visible.';};
    synth?.addEventListener('voiceschanged',refresh);refresh();
    button.addEventListener('click',()=>{
        if(!synth){refresh();return;}
        synth.cancel();
        const voice=synth.getVoices().find(v=>/^ur(?:-|$)/i.test(v.lang));
        const utterance=new SpeechSynthesisUtterance(voice?'براہ کرم امتحان کی اسکرین پر توجہ دیں۔':'Please keep your attention on the exam screen.');
        utterance.lang=voice?.lang||'en-US';if(voice)utterance.voice=voice;
        button.disabled=true;note.textContent='Testing warning sound. Confirm that you can hear it.';
        let ended=false;const finish=(text)=>{if(ended)return;ended=true;clearTimeout(timer);button.disabled=false;note.textContent=text;};
        const timer=setTimeout(()=>{synth.cancel();finish('Sound playback did not complete. Written warnings remain available.');},10000);
        utterance.onend=()=>finish(voice?'Urdu sound test completed.':'English sound test completed. No Urdu voice is installed.');
        utterance.onerror=()=>finish('Sound could not play. Check browser sound settings; written warnings remain available.');
        synth.speak(utterance);
    });
})();

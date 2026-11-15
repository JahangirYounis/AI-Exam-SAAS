/* Native browser signals cannot identify a destination tab, URL or minimization.
   All elapsed time uses the caller's monotonic clock. */
(function(root){
 function create(options){
  let episode=null,prompts=0,settleUntil=0,fullscreenSeen=false,awaitingFocus=false;
  const restored=options.restore?.();
  if(['ExamPageHidden','FullScreenExit'].includes(restored?.type) && typeof restored.id==='string' &&
     Number.isFinite(restored.duration) && restored.duration>=0 && restored.duration<=86400){
   const now=options.now();
   episode={...restored,since:now-restored.duration*1000,published:now};
   fullscreenSeen=episode.type==='FullScreenExit';
  }
  const remember=now=>options.persist?.(['ExamPageHidden','FullScreenExit'].includes(episode?.type)
   ? {id:episode.id,type:episode.type,sent:episode.sent,signals:episode.signals,startedAt:episode.startedAt,duration:Math.min(86400,Math.max(0,(now-episode.since)/1000))}
   : null);
  const emit=(phase,now)=>{
   if(!episode || (phase==='stop' && !episode.sent))return;
   const duration=Math.min(86400,Math.max(0,(now-episode.since)/1000));
   if(episode.type==='ExamWindowLeft' && phase!=='end')return;
   const threshold=episode.type==='ExamPageHidden'?.6:1.5;
   if(!episode.sent && duration<threshold)return;
   const first=!episode.sent;episode.sent=true;
   options.report(episode.type,'Browser monitoring observation',{id:episode.id,phase:first&&phase!=='end'?'start':phase,duration,signals:episode.signals,startedAt:episode.startedAt});
   episode.published=now;
   remember(now);
  };
  const close=(now,phase='end')=>{if(episode){emit(phase,now);episode=null;remember(now);}};
  const stop=now=>close(now,episode?.type==='FullScreenExit'?'stop':'end');
  return {
   suspend(){
    prompts++;
    if(episode?.type!=='FullScreenExit'){
     close(options.now());
     fullscreenSeen=false;
    }
    let done=false;
    return()=>{if(!done){done=true;prompts--;settleUntil=options.now()+1000;}};
   },
   sample(){
    const now=options.now(),s=options.state();
    if(!s.active){stop(now);return;}
    // The display picker can cause focus and fullscreen changes of its own.
    if(prompts||now<settleUntil)return;
    if(episode?.type==='FullScreenExit'&&s.fullscreen)close(now);
    if(s.fullscreen)fullscreenSeen=true;
    let type=s.hidden?'ExamPageHidden':s.requireFullscreen&&fullscreenSeen&&!s.fullscreen?'FullScreenExit':s.focused===false?'ExamWindowLeft':null;
    // A hidden-page departure can also cause fullscreen loss. Rearm only after re-entry.
    if(episode?.type==='ExamPageHidden'&&!s.hidden){
     fullscreenSeen=!!s.fullscreen;
     awaitingFocus=s.focused===false;
     close(now);
     return;
    }
    if(awaitingFocus){
     if(s.hidden||s.focused!==false)awaitingFocus=false;
     else if(type==='ExamWindowLeft')return;
    }
    if(episode?.type==='FullScreenExit'&&episode.sent&&!s.fullscreen)type='FullScreenExit';
    if(!type){close(now);return;}
    if(episode && episode.type!==type){
     if(episode.sent)close(now);
     else {
      // Keep the ID, but measure from the stronger signal itself.
      episode={...episode,type,since:now,published:now,signals:[type],startedAt:options.wallTime?.(now)};
     }
    }
    if(!episode)episode={id:(options.id||(()=>crypto.randomUUID()))(),type,since:now,published:now,sent:false,signals:[type],startedAt:options.wallTime?.(now)};
    if(!episode.sent || (episode.type!=='FullScreenExit' && now-episode.published>=1000))emit('update',now);
    if(episode.type!=='ExamWindowLeft' && !episode.remembered){remember(now);episode.remembered=true;}
   },
   pagehide(){
    const now=options.now();
    if(episode?.type==='ExamPageHidden'){emit('update',now);remember(now);}
    else if(episode?.type==='FullScreenExit')remember(now);
    else close(now);
   },
   stop(){stop(options.now());}
  };
 }
 root.ExamBrowserGuard={create};
})(typeof window==='undefined'?globalThis:window);

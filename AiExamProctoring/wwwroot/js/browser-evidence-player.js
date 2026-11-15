/* Playback offsets are capture-clock estimates, never claims of frame-accurate synchronization. */
(function(root){
 function position(meta, timeline, time){
  if(!meta || meta.timelineId!==timeline || !Number.isFinite(meta.monotonicStart)||!Number.isFinite(meta.monotonicEnd))return null;
  return time>=meta.monotonicStart && time<meta.monotonicEnd?(time-meta.monotonicStart)/1000:null;
 }
 root.BrowserEvidenceTiming={position};
 if(typeof document==='undefined')return;
 for(const panel of document.querySelectorAll('.browser-evidence-player')){
  const clips=[...panel.querySelectorAll('.review-clip')].map(el=>{let meta;try{meta=JSON.parse(el.dataset.timing)}catch{}return {el,meta,video:el.querySelector('video')};});
  const timelines=[...new Set(clips.map(c=>c.meta?.timelineId).filter(Boolean))];
  const select=document.createElement('select');select.setAttribute('aria-label','Capture session');
  for(const [i,id] of timelines.entries()){const option=document.createElement('option');option.value=id;option.textContent=`Capture session ${i+1}`;select.append(option);}panel.append(select);
  const slider=panel.querySelector('.media-position'),output=panel.querySelector('.media-time');let start=0,end=0,playing=false,last=0;
  function render(seek){const t=start+Number(slider.value)*1000;output.textContent=`${Number(slider.value).toFixed(1)} seconds`;
   for(const c of clips){const offset=position(c.meta,select.value,t);c.el.hidden=offset===null;
    if(offset===null){c.video.pause();continue;}
    if(seek||Math.abs(c.video.currentTime-offset)>.5)c.video.currentTime=offset;
    if(playing)c.video.play().catch(()=>{playing=false;output.textContent='Playback unavailable. Open the original evidence link to review.';});else c.video.pause();
   }
  }
  function reset(){playing=false;const usable=clips.filter(c=>c.meta?.timelineId===select.value&&Number.isFinite(c.meta.monotonicStart)&&Number.isFinite(c.meta.monotonicEnd));start=usable.length?Math.min(...usable.map(c=>c.meta.monotonicStart)):0;end=usable.length?Math.max(...usable.map(c=>c.meta.monotonicEnd)):0;slider.max=(end-start)/1000;slider.value=0;render(true);}
  panel.querySelector('.play-media').onclick=()=>{playing=true;last=performance.now();render(true);};
  panel.querySelector('.pause-media').onclick=()=>{playing=false;render(false);};slider.oninput=()=>render(true);select.onchange=reset;
  panel.addEventListener('toggle',()=>{if(!panel.open){playing=false;render(false);}});
  setInterval(()=>{if(!playing)return;const now=performance.now();slider.value=Math.min(Number(slider.max),Number(slider.value)+(now-last)/1000);last=now;if(Number(slider.value)>=Number(slider.max))playing=false;render(false);},200);reset();
 }
})(globalThis);

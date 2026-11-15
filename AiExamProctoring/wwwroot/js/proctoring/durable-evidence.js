// Bounded IndexedDB outbox. Delete only after server persistence acknowledgment.
export class DurableEvidenceQueue {
    constructor(scope, token, status) { this.scope=scope; this.token=token; this.status=status; this.busy=false; }
    async database() {
        if (!this.db) this.db = new Promise((resolve,reject) => {
            const req=indexedDB.open('exam-absence-evidence-v1',1);
            req.onupgradeneeded=()=>req.result.createObjectStore('uploads',{keyPath:'id'});
            req.onsuccess=()=>resolve(req.result);req.onerror=()=>reject(req.error);
        });
        return this.db;
    }
    async rows() {
        const db=await this.database();
        return new Promise((resolve,reject)=>{const req=db.transaction('uploads').objectStore('uploads').getAll();req.onsuccess=()=>resolve(req.result);req.onerror=()=>reject(req.error);});
    }
    async mutate(action) {
        const db=await this.database();
        return new Promise((resolve,reject)=>{const tx=db.transaction('uploads','readwrite');action(tx.objectStore('uploads'));tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error);});
    }
    enqueue(item) {
        // Serialize quota check + enqueue within this recorder; no silent eviction.
        this.chain=(this.chain||Promise.resolve()).then(async()=>{
            const size=(item.blob?.size||0)+(item.screenshot?.length||0)*2;
            const row={...item,id:item.browserEvent ? `browser-${item.browserEvent.id}-${item.browserEvent.phase}` : crypto.randomUUID(),scope:this.scope,size,revision:crypto.randomUUID(),queuedAt:Date.now()};
            const db=await this.database();
            await new Promise((resolve,reject)=>{
                const tx=db.transaction('uploads','readwrite'), store=tx.objectStore('uploads');
                const req=store.getAll();
                req.onsuccess=()=>{
                    const rows=req.result;
                    const pending=rows.filter(r=>r.id!==row.id);
                    // Reserve space for completed incident clips when routine media backs up.
                    const reserved=row.incidentClip || row.absenceSegment || row.browserEvent || row.recording;
                    const recordingGap = row.recording && !row.blob && row.metadata?.status === 'missing';
                    const mediaRows = pending.filter(r => !r.recording || r.blob);
                    if(recordingGap ? pending.length >= 10000 :
                        mediaRows.length >= (reserved ? 64 : 60) ||
                        mediaRows.reduce((n,r)=>n+r.size,0)+size > (reserved ? 128 : 88)*1024*1024) {tx.abort();return;}
                    store.put(row);
                };
                tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error);
            });
            this.flush();
            return true;
        }).catch(() => {
            this.status('Evidence could not be saved locally. Please tell your teacher.');
            return false;
        });
        return this.chain;
    }
    async flush(recordingsFor = null) {
        if(this.busy)return;this.busy=true;
        try {
            for(const row of (await this.rows()).filter(r=>recordingsFor ? r.recording && r.owner===recordingsFor : r.scope===this.scope).sort((a,b)=>Number(!!b.browserEvent)-Number(!!a.browserEvent)||Number(!!b.incidentClip)-Number(!!a.incidentClip)||a.queuedAt-b.queuedAt||({start:0,update:1,end:2,stop:2}[a.browserEvent?.phase]??0)-({start:0,update:1,end:2,stop:2}[b.browserEvent?.phase]??0))) {
                const data=new FormData();data.append('__RequestVerificationToken',this.token);
                if(row.browserEvent){
                    const e=row.browserEvent;
                    data.append('attemptId',e.attemptId);data.append('sessionAttemptId',e.sessionAttemptId);
                    data.append('eventId',e.id);data.append('kind',e.kind);data.append('phase',e.phase);
                    data.append('elapsed',e.duration);data.append('surface',e.surface||'unknown');
                    if(e.observedAt)data.append('observedAt',e.observedAt);
                    if(e.startedAt)data.append('startedAt',e.startedAt);
                }
                if(row.browserEventId){data.append('browserEventId',row.browserEventId);data.append('attemptId',row.attemptId);data.append('sessionAttemptId',row.sessionAttemptId);}
                if (row.logId && row.endpoint && !row.browserEventId && row.attemptId && row.sessionAttemptId) {
                    data.append("attemptId",row.attemptId);
                    data.append("sessionAttemptId",row.sessionAttemptId);
                }
                if(row.audioGap) {
                    data.append('attemptId',row.attemptId);data.append('sessionAttemptId',row.sessionAttemptId);
                    data.append('eventId',row.id);data.append('reason',row.audioGap.reason);data.append('durationSeconds',row.audioGap.durationSeconds);
                }
                if(row.recording) {
                    data.append('attemptId',row.attemptId);data.append('sessionAttemptId',row.sessionAttemptId);
                    data.append('eventId',row.id.replaceAll('-',''));data.append('includesAudio',String(row.includesAudio));
                    if (row.blob) data.append('recording',row.blob,'session.webm');
                }
                if(row.incidentClip) {
                    data.append('EvidenceFile',row.blob,'incident.webm');
                    if(row.screenshot)data.append('ScreenshotDataUrl',row.screenshot);
                    data.append('IncludesAudio',String(row.includesAudio));
                    data.append('attemptId',row.attemptId);
                    data.append('sessionAttemptId',row.sessionAttemptId);
                }
                if(row.metadata)data.append('metadata',JSON.stringify(row.metadata));
                data.append('logId',row.logId||0);data.append('uploadId',row.id);data.append('stage',row.stage);
                data.append('capturedAt',new Date(row.capturedAt||Date.now()).toISOString());
                if(row.blob && !row.recording && !row.incidentClip)data.append('media',row.blob,row.stage === 'audio-original' ? 'microphone.wav' : 'incident.webm');
                if(row.screenshot && !row.incidentClip)data.append('screenshot',row.screenshot);
                let response;
                try { response=await fetch(row.incidentClip ? '/Student/Exam/AttachProctoringEvidence' : row.audioGap ? '/Student/Exam/MonitoringGap' : row.recording ? '/Student/Exam/UploadRecordingSegment' : row.browserEvent ? '/Student/Exam/BrowserObservation' : row.endpoint === '/Student/Exam/IncidentEvidence' ? row.endpoint : '/Student/Exam/AbsenceEvidence',{method:'POST',credentials:'same-origin',headers:{Accept:'application/json'},body:data,signal:AbortSignal.timeout(row.incidentClip ? 60000 : 20000)}); }
                catch {
                    this.status('Evidence upload pending. Saved local evidence will retry.');
                    if (row.browserEvent) break; // Preserve ordering of browser event phases.
                    continue;
                }
                if(!response.ok || !(response.headers.get('content-type') || '').includes('application/json') || !((await response.json()).acknowledged)) {
                    this.status('Evidence upload pending. Saved local evidence will retry.');
                    // Retain rejected media locally, but do not let one unusable asset block all later files.
                    if (!row.browserEvent || (response.status >= 400 && response.status < 500 && response.status !== 429)) continue;
                    break;
                }
                await this.mutate(store=>{
                    const request=store.getAll();
                    request.onsuccess=()=>{if(request.result.find(r=>r.id===row.id)?.revision===row.revision)store.delete(row.id);};
                });
            }
        } catch {this.status('Evidence upload pending. Check your connection.');}
        finally {this.busy=false;}
    }
}



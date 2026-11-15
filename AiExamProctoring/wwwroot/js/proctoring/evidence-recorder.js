const wait = milliseconds => new Promise(resolve => window.setTimeout(resolve, milliseconds));

/**
 * Keeps one short, low-bitrate event buffer in browser memory. Nothing is sent
 * until the server accepts a violation and returns its owned log ID.
 */
export class EvidenceRecorder {
    constructor(options = {}) {
        this.options = {
            cameraStream: options.cameraStream || null,
            cameraOnly: true,
            clipSeconds: Math.max(4, Math.min(20, Number(options.clipSeconds ?? 10))),
            width: Math.max(640, Math.min(1280, Number(options.width ?? 960))),
            height: Math.max(360, Math.min(720, Number(options.height ?? 540))),
            framesPerSecond: Math.max(4, Math.min(12, Number(options.framesPerSecond ?? 8))),
            videoBitsPerSecond: Math.max(400_000, Math.min(1_500_000, Number(options.videoBitsPerSecond ?? 850_000))),
            onScreenEnded: options.onScreenEnded || (() => {}),
            onScreenHealth: options.onScreenHealth || (() => {}),
            onMediaSegment: options.onMediaSegment || (() => {}),
            onSegment: options.onSegment || (() => {}),
            onOriginalSegment: options.onOriginalSegment || (() => {})
        };

        this.combinedStream = null;
        this.cameraVideo = null;
        this.canvas = null;
        this.context = null;
        this.recorder = null;
        this.currentChunks = [];
        this.lastCompletedSegment = null;
        this.segmentStartedAt = 0;
        this.segmentTimer = 0;
        this.drawTimer = 0;
        this.running = false;
        this.stoppingIntentionally = false;
        this.mimeType = '';
        this.timelineId = globalThis.crypto?.randomUUID?.() || 'unavailable';
        this.captureOrigin = globalThis.performance?.timeOrigin ?? Date.now() - (globalThis.performance?.now?.() ?? 0);
        this.sequence = { camera: 0, screen: 0, composite: 0 };
    }

    async requestAndStart() {
        if (this.running) return this.status();
        const cameraTrack = this.options.cameraStream?.getVideoTracks?.()
            .find(track => track.readyState === 'live');
        if (!cameraTrack)
            throw new Error('A live camera stream is required for event evidence recording.');
        if (typeof MediaRecorder === 'undefined')
            throw new Error('This browser does not support event evidence recording.');

        this.mimeType = chooseMimeType();
        if (!this.mimeType) throw new Error('This browser cannot record the supported WebM evidence format.');
        this.stoppingIntentionally = false;
        if (this.options.cameraStream?.getVideoTracks?.().length)
            this.cameraVideo = await this.createVideo(this.options.cameraStream);
        if (this.stoppingIntentionally) {
            this.cameraVideo?.pause();
            if (this.cameraVideo) this.cameraVideo.srcObject = null;
            return this.status();
        }

        this.canvas = document.createElement('canvas');
        this.canvas.width = this.options.width;
        this.canvas.height = this.options.height;
        this.context = this.canvas.getContext('2d', { alpha: false });
        const canvasCaptureAvailable = typeof this.canvas.captureStream === 'function';
        if (!this.context) {
            this.stop();
            throw new Error('Secure screen composition is not supported by this browser.');
        }

        this.running = true;
        this.drawFrame();
        this.drawTimer = window.setInterval(
            () => this.drawFrame(),
            Math.round(1000 / this.options.framesPerSecond));

        this.combinedStream = canvasCaptureAvailable
            ? this.canvas.captureStream(this.options.framesPerSecond)
            : new MediaStream();
        if (!canvasCaptureAvailable && cameraTrack)
            this.combinedStream.addTrack(cameraTrack.clone());
        const microphoneTrack = this.options.cameraStream?.getAudioTracks?.()
            .find(track => track.readyState === 'live');
        if (microphoneTrack) this.combinedStream.addTrack(microphoneTrack.clone());

        this.mimeType = chooseMimeType();
        this.startSegment();
        this.startOriginalSegment();
        return this.status();
    }

    async createVideo(stream) {
        const video = document.createElement('video');
        video.srcObject = stream;
        video.muted = true;
        video.playsInline = true;
        await video.play();
        return video;
    }

    async attachScreen(original) {
        if (!this.running || !this.canvas?.captureStream) throw new Error('Camera recording must be active first.');
        const source = original.getVideoTracks().find(track => track.readyState === 'live');
        if (!source) throw new Error('The shared display is no longer available.');
        const screen = new MediaStream([source.clone()]);
        try {
            const selectedTrack = screen.getVideoTracks()[0];
            if (!selectedTrack || selectedTrack.readyState !== 'live') throw new Error('The selected screen is no longer available. Please share it again.');
            const video = await this.createVideo(screen);
            if (!this.running || source.readyState !== 'live') throw new Error('The selected display is no longer available.');
            const old = this.screenOriginal; this.screenOriginal = null;
            old?.stop();
            this.screenStream?.getTracks().forEach(track => track.stop());
            this.screenStream = screen;
            this.screenSource = source;
            this.screenVideo = video;
            const track=screen.getVideoTracks()[0];
            this.captureSurface=['monitor','window','browser'].includes(track.getSettings?.().displaySurface)?track.getSettings().displaySurface:'unknown';
            source.addEventListener?.('mute', () => {
                if (this.screenStream !== screen) return;
                const segment = this.screenOriginal;
                if (segment?.recorder.state === 'recording') { segment.interrupted = true; segment.stop(); }
            });
            source.addEventListener?.('unmute', () => {
                if (this.screenStream === screen && !this.screenOriginal) this.startScreenSegment(screen);
            });
            if(this.startScreenSegment(screen)===false)throw new Error("Screen evidence recording could not start.");

            const onScreenEnded = () => {
                if (this.screenStream !== screen) return;
                const segment=this.screenOriginal; this.screenOriginal=null;
                if (segment) { segment.interrupted = true; segment.stop(); }
                this.screenVideo?.pause(); this.screenVideo = null; this.screenStream = null;
                screen.getTracks().forEach(track => track.stop());
                this.options.onScreenEnded();
            };
            screen.getVideoTracks()[0].addEventListener('ended', onScreenEnded, { once: true });
            source.addEventListener('ended', onScreenEnded, { once: true });
        } catch (error) { screen.getTracks().forEach(track => track.stop()); if(this.screenStream===screen){this.screenVideo?.pause();this.screenVideo=null;this.screenStream=null;} throw error; }
    }

    drawFrame() {
        if (!this.running || !this.context || !this.canvas) return;
        const { width, height } = this.canvas;
        const context = this.context;
        context.fillStyle = '#07111f';
        context.fillRect(0, 0, width, height);

        const screenActive = this.screenVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && this.screenStream?.getVideoTracks().some(t=>t.readyState==='live'&&!t.muted);
        const cameraActive=this.cameraVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && this.options.cameraStream?.getVideoTracks().some(t=>t.readyState==='live'&&!t.muted);
        if (screenActive) {
            const scale = Math.min(width * .84 / this.screenVideo.videoWidth, height / this.screenVideo.videoHeight);
            const sw = this.screenVideo.videoWidth * scale, sh = this.screenVideo.videoHeight * scale;
            context.drawImage(this.screenVideo, (width * .84 - sw) / 2, (height - sh) / 2, sw, sh);
        }
        if (screenActive && cameraActive) {
            drawCover(context, this.cameraVideo, width * .85, height * .70, width * .14, height * .28);
        } else if (cameraActive && !this.screenStream) {
            context.save();
            context.translate(width, 0);
            context.scale(-1, 1);
            drawCover(context, this.cameraVideo, 0, 0, width, height);
            context.restore();
        }

        context.font = '600 15px system-ui, sans-serif';
        context.fillStyle = '#ffffff';
        context.fillText(screenActive ? 'Review copy' : 'Screen unavailable', screenActive ? width*.85 : 16, 24);
        context.font = '500 13px system-ui, sans-serif';
        context.fillStyle = '#dbeafe';
        context.font = '10px system-ui';
        context.fillText(new Date().toISOString().slice(11,23)+' UTC', screenActive ? width*.85 : 16, 44);
    }

    startSegment() {
        if (!this.running || !this.combinedStream) return;
        this.currentChunks = [];
        let overflow=false;
        this.segmentStartedAt = performance.now();
        const metadata=this.segmentMetadata('composite');
        const recorderOptions = {
            videoBitsPerSecond: this.options.videoBitsPerSecond
        };
        if (this.mimeType) recorderOptions.mimeType = this.mimeType;

        this.recorder = new MediaRecorder(this.combinedStream, recorderOptions);
        this.recorder.addEventListener('dataavailable', event => {
            if (event.data?.size) {
                if(this.currentChunks.reduce((n,b)=>n+b.size,0)+event.data.size<=6000000)this.currentChunks.push(event.data);
                else {overflow=true;this.currentChunks=[];this.options.onScreenHealth(false);}
                if(this.currentChunks.reduce((n,b)=>n+b.size,0)>=5000000 && this.recorder.state==='recording')this.recorder.stop();
            }
        });
        this.recorder.addEventListener('stop', () => {
            if (!overflow && this.currentChunks.length) {
                const type = this.mimeType || this.currentChunks[0]?.type || 'video/webm';
                this.lastCompletedSegment = new Blob(this.currentChunks, { type });
                this.options.onSegment(this.lastCompletedSegment);
                this.options.onMediaSegment(this.lastCompletedSegment,this.finishMetadata(metadata));
            }
            if (this.running)
                window.setTimeout(() => this.startSegment(), 50);
        }, { once: true });
        this.recorder.start(1000);
        window.clearTimeout(this.segmentTimer);
        this.segmentTimer = window.setTimeout(() => {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        }, this.options.clipSeconds * 1000);
    }

    // Original camera media is encoded directly, without canvas overlays/enhancement.
    // A completed WebM is returned; independent recordings are never concatenated.
    resumeCameraOriginal() {
        if (this.running && !this.original) this.startOriginalSegment();
    }

    startOriginalSegment() {
        if (!this.running || this.options.cameraStream?.getVideoTracks?.().every(t=>t.readyState==='ended'||t.muted)) return;
        const chunks = [];
        let overflow=false;
        let recorder;
        try {
            // Keep the recorder's track list stable when the exam replaces a device.
            const tracks = this.options.cameraStream.getTracks?.();
            const source = tracks ? new MediaStream(tracks.filter(t => t.readyState === 'live')) : this.options.cameraStream;
            recorder = new MediaRecorder(source, {
                ...(this.mimeType ? { mimeType: this.mimeType } : {}),
                videoBitsPerSecond: this.options.videoBitsPerSecond,
                audioBitsPerSecond: 64000
            });
        } catch { this.original = null; return; }
        const metadata=this.segmentMetadata('camera');
        const segment = { recorder, startedAt: Date.now(), timer: 0, requested: false, includesMicrophone: metadata.includesMicrophone };
        const onCameraEnded = () => {
            segment.captureFailure = 'camera_lost';
            if (recorder.state === 'recording') recorder.stop();
        };
        const cameraTracks = this.options.cameraStream?.getVideoTracks?.() || [];
        for (const track of cameraTracks) {
            track.addEventListener?.('ended', onCameraEnded, { once: true });
            track.addEventListener?.('mute', onCameraEnded, { once: true });
        }
        segment.done = new Promise(resolve => {
            recorder.addEventListener('dataavailable', event => {
                if (event.data?.size) {
                    const bytes = chunks.reduce((total, chunk) => total + chunk.size, 0);
                    const limit = segment.heldForAbsence ? 18000000 : 6000000;
                    if (bytes + event.data.size <= limit) chunks.push(event.data);
                    else if (segment.heldForAbsence) segment.absenceTruncated = true;
                    else { overflow = true; this.options.onScreenHealth(false); }
                    if ((segment.absenceTruncated || bytes + event.data.size >= limit - 2000000) &&
                        recorder.state === 'recording') {
                        if (segment.heldForAbsence) segment.absenceTruncated = true;
                        recorder.stop();
                    }
                }
            });
            recorder.addEventListener('stop', () => {
                window.clearTimeout(segment.timer);
                window.clearTimeout(segment.rolloverTimer);
                this.originalSegments?.delete(segment);
                for (const track of cameraTracks) {
                    track.removeEventListener?.('ended', onCameraEnded);
                    track.removeEventListener?.('mute', onCameraEnded);
                }
                segment.endedAt = Date.now();
                const blob = !overflow && chunks.length ? new Blob(chunks, {
                    type: recorder.mimeType || this.mimeType || 'video/webm'
                }) : null;
                if (blob?.size) this.options.onOriginalSegment(blob, segment.startedAt, segment.endedAt);
                this.options.onMediaSegment(blob, this.finishMetadata(metadata,
                    !blob ? 'missing' : segment.captureFailure || segment.absenceTruncated ? 'interrupted' : 'recorded'));
                resolve(blob);
                if (this.original === segment) {
                    this.original = null;
                    if (this.running) this.startOriginalSegment();
                }
            }, { once: true });
            recorder.addEventListener('error', () => {
                segment.captureFailure = 'recorder_error';
                // Preserve the encoder's available chunks; completeness is explicit.
                if (recorder.state === 'inactive') {
                    segment.endedAt = Date.now();
                    resolve(chunks.length ? new Blob(chunks, {type: recorder.mimeType || this.mimeType}) : null);
                }
                if(this.original===segment)this.original=null;
                try { if (recorder.state !== 'inactive') recorder.stop(); } catch {}
            }, { once: true });
        });
        this.originalSegments ??= new Set();
        this.originalSegments.add(segment);
        this.original = segment;
        recorder.start(1000);
        // Overlap independent encoders: select one complete WebM with pre-event
        // context instead of concatenating files across a segment boundary.
        segment.rolloverTimer = window.setTimeout(() => {
            if (this.running && this.original === segment && !segment.heldForAbsence)
                this.startOriginalSegment();
        }, Math.max(1000, (this.originalSegmentMilliseconds || 20000) - 5000));
        segment.timer = window.setTimeout(() => {
            if (recorder.state === 'recording') recorder.stop();
        }, this.originalSegmentMilliseconds || 20000);
    }

    segmentMetadata(source) {
        const monotonicStart = globalThis.performance?.now?.() ?? 0;
        return {source,timelineId:this.timelineId,sequence:++this.sequence[source],
            startedAt:Math.round(this.captureOrigin + monotonicStart),monotonicStart,
            includesMicrophone:this.options.cameraStream?.getAudioTracks?.().some(t=>t.readyState==='live'&&t.enabled!==false&&!t.muted)===true,
            surface:this.captureSurface||'unknown',derived:source==='composite',screenAvailable:!!this.screenStream,
            documentHidden:globalThis.document?.hidden===true};
    }
    finishMetadata(metadata, status = 'recorded', monotonicEnd = globalThis.performance?.now?.() ?? metadata.monotonicStart) {
        return { ...metadata, endedAt: Math.round(this.captureOrigin + monotonicEnd), monotonicEnd, status };
    }
    startScreenSegment(stream) {
        if(!this.running || this.screenStream!==stream || this.screenSource?.muted ||
            !stream.getVideoTracks().some(t=>t.readyState==='live'&&!t.muted))return;
        // The webcam recording already carries the available exam microphone.
        const screenTrack=stream.getVideoTracks().find(t=>t.readyState==='live');
        const clones=[screenTrack.clone()];
        const recordingStream=new MediaStream(clones);
        let recorder;
        try {recorder=new MediaRecorder(recordingStream,{...(this.mimeType?{mimeType:this.mimeType}:{}),videoBitsPerSecond:1500000});}
        catch {clones.forEach(t=>t.stop());this.options.onScreenHealth(false);return false;}
        let overflow=false;
        const chunks=[],metadata={...this.segmentMetadata('screen'),includesMicrophone:false},segment={recorder,timer:0,clones};
        segment.stop = () => {
            if (recorder.state !== 'recording') return;
            segment.end = performance.now();
            recorder.stop();
        };
        let finish;
        segment.done = new Promise(resolve => { finish = resolve; });
        this.screenOriginal=segment;
        recorder.addEventListener('dataavailable',e=>{if(e.data?.size){
            if(chunks.reduce((n,b)=>n+b.size,0)+e.data.size<=6000000)chunks.push(e.data);
            else {overflow=true;this.options.onScreenHealth(false);}
            if(chunks.reduce((n,b)=>n+b.size,0)>=5000000 || overflow)segment.stop();
        }});
        recorder.addEventListener('stop',()=>{
            window.clearTimeout(segment.timer);
            segment.clones.forEach(t=>t.stop());
            const blob = !overflow && chunks.length ? new Blob(chunks,{type:recorder.mimeType||this.mimeType||'video/webm'}) : null;
            this.options.onMediaSegment(blob, this.finishMetadata(metadata,
                !blob ? 'missing' : segment.interrupted ? 'interrupted' : 'recorded', segment.end));
            finish();
            if(this.screenOriginal===segment){this.screenOriginal=null;this.startScreenSegment(stream);}
        },{once:true});
        recorder.addEventListener('error',()=>{segment.interrupted=true;this.options.onScreenHealth(false);segment.stop();},{once:true});
        recorder.start(1000);segment.timer=window.setTimeout(segment.stop,10000);return true;
    }
    captureScreenDataUrl() {
        if(this.screenSource?.muted||!this.screenStream?.getVideoTracks().some(t=>t.readyState==='live'&&!t.muted)||!this.screenVideo?.videoWidth)return null;
        try {
            const canvas=document.createElement('canvas'),ratio=Math.min(1,1920/this.screenVideo.videoWidth);
            canvas.width=Math.round(this.screenVideo.videoWidth*ratio);canvas.height=Math.round(this.screenVideo.videoHeight*ratio);
            canvas.getContext('2d').drawImage(this.screenVideo,0,0,canvas.width,canvas.height);
            return canvas.toDataURL('image/jpeg',.85);
        } catch{return null;}
    }

    holdOriginalForAbsence() {
        return this.holdOriginalForIncident();
    }

    holdOriginalForIncident() {
        const segment = [...(this.originalSegments || [this.original])]
            .filter(s => s?.recorder.state === 'recording' && !s.heldForAbsence)
            .sort((a,b) => a.startedAt - b.startedAt)[0];
        if (!segment || segment.recorder.state !== 'recording') return null;
        segment.heldForAbsence = true;
        clearTimeout(segment.timer);
        clearTimeout(segment.rolloverTimer);
        if (this.original === segment) this.startOriginalSegment();
        return segment;
    }

    async captureEvidenceBlob() {
        const segment = this.original;
        if (!segment || segment.heldForAbsence || segment.recorder.state !== 'recording') return null;
        if (!segment.requested && !segment.heldForAbsence) {
            segment.requested = true;
            // Preserve available pre-event context and ten seconds after first request.
            // Near a segment boundary, earlier context remains in session recordings.
            window.clearTimeout(segment.timer);
            segment.timer = window.setTimeout(() => {
                if (segment.recorder.state === 'recording') segment.recorder.stop();
            }, 10000);
        }
        return segment.done;
    }

    captureEvidenceDataUrl() {
        if (!this.running || !this.canvas) return null;
        try { this.drawFrame(); return this.canvas.toDataURL('image/jpeg', .68); }
        catch { return null; }
    }

    captureOriginalDataUrl() {
        if (!this.running || this.cameraVideo?.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) return null;
        try {
            const snapshot = document.createElement('canvas');
            snapshot.width = this.cameraVideo.videoWidth;
            snapshot.height = this.cameraVideo.videoHeight;
            if (!snapshot.width || !snapshot.height) return null;
            snapshot.getContext('2d').drawImage(this.cameraVideo, 0, 0);
            return snapshot.toDataURL('image/jpeg', .9);
        } catch { return null; }
    }

    status() {
        return {
            active: this.running && (this.recorder?.state === 'recording' || this.original?.recorder?.state === 'recording') &&
                this.options.cameraStream?.getVideoTracks?.().some(item => item.readyState === 'live') === true,
            includesMicrophone: this.combinedStream?.getAudioTracks?.().length > 0,
            cameraOnly: !this.screenStream?.getVideoTracks().some(track => track.readyState === 'live')
        };
    }

    handleScreenEnded() {
        if (this.stoppingIntentionally) return;
        this.running = false;
        for (const segment of this.originalSegments || []) {
            window.clearTimeout(segment.timer);
            window.clearTimeout(segment.rolloverTimer);
            try { if (segment.recorder.state === 'recording') segment.recorder.stop(); } catch {}
        }
        window.clearTimeout(this.original?.timer);
        try { if (this.original?.recorder.state === 'recording') this.original.recorder.stop(); } catch {}
        window.clearInterval(this.drawTimer);
        window.clearTimeout(this.segmentTimer);
        try {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        } catch { }
    }

    stop() {
        this.stoppingIntentionally = true;
        this.running = false;
        const screenSegment=this.screenOriginal;this.screenOriginal=null;
        const finalizing = [...(this.originalSegments || [])].map(segment => segment.done);
        if (screenSegment?.done) finalizing.push(screenSegment.done);
        window.clearTimeout(screenSegment?.timer);
        try { screenSegment?.stop(); } catch {}
        for (const segment of this.originalSegments || []) {
            window.clearTimeout(segment.timer);
            window.clearTimeout(segment.rolloverTimer);
            try { if (segment.recorder.state === 'recording') segment.recorder.stop(); } catch {}
        }
        window.clearTimeout(this.original?.timer);
        try { if (this.original?.recorder.state === 'recording') this.original.recorder.stop(); } catch {}
        window.clearInterval(this.drawTimer);
        window.clearTimeout(this.segmentTimer);
        try {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        } catch { }
        this.combinedStream?.getTracks?.().forEach(track => track.stop());
        this.cameraVideo?.pause?.();
        if (this.cameraVideo) this.cameraVideo.srcObject = null;
        this.screenStream?.getTracks().forEach(track => track.stop());
        this.screenVideo?.pause?.();
        return Promise.allSettled(finalizing);
    }
}

function chooseMimeType() {
    const candidates = [
        'video/webm;codecs=vp8,opus',
        'video/webm;codecs=vp8',
        'video/webm'
    ];
    return candidates.find(type => MediaRecorder.isTypeSupported?.(type)) || '';
}

function drawCover(context, video, x, y, width, height) {
    const sourceWidth = Math.max(1, video.videoWidth || width);
    const sourceHeight = Math.max(1, video.videoHeight || height);
    const sourceRatio = sourceWidth / sourceHeight;
    const destinationRatio = width / height;
    let sx = 0;
    let sy = 0;
    let sw = sourceWidth;
    let sh = sourceHeight;
    if (sourceRatio > destinationRatio) {
        sw = sourceHeight * destinationRatio;
        sx = (sourceWidth - sw) / 2;
    } else {
        sh = sourceWidth / destinationRatio;
        sy = (sourceHeight - sh) / 2;
    }
    context.drawImage(video, sx, sy, sw, sh, x, y, width, height);
}


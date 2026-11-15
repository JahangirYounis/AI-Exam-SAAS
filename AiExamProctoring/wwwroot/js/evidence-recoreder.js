
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
            onScreenEnded: options.onScreenEnded || (() => { }),
            onScreenHealth: options.onScreenHealth || (() => { }),
            onMediaSegment: options.onMediaSegment || (() => { }),
            onSegment: options.onSegment || (() => { }),
            onOriginalSegment: options.onOriginalSegment || (() => { })
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

    async enableScreen() {
        if (!this.running || !this.canvas?.captureStream) throw new Error('Camera recording must be active first.');
        if (!navigator.mediaDevices?.getDisplayMedia) throw new Error('Screen recording is not supported by this browser.');
        const screen = await navigator.mediaDevices.getDisplayMedia({ video: { displaySurface: 'monitor', frameRate: { ideal: 8, max: 12 } }, audio: false });
        try {
            const selectedTrack = screen.getVideoTracks()[0];
            if (!selectedTrack || selectedTrack.readyState !== 'live') throw new Error('The selected screen is no longer available. Please share it again.');
            const video = await this.createVideo(screen);
            if (!this.running) throw new Error('The recording has ended.');
            const old = this.screenOriginal; this.screenOriginal = null;
            if (old?.recorder.state === 'recording') old.recorder.stop();
            this.screenStream?.getTracks().forEach(track => track.stop());
            this.screenStream = screen;
            this.screenVideo = video;
            const track = screen.getVideoTracks()[0];
            this.captureSurface = ['monitor', 'window', 'browser'].includes(track.getSettings?.().displaySurface) ? track.getSettings().displaySurface : 'unknown';
            track.addEventListener('mute', () => { if (this.screenStream === screen) this.options.onScreenHealth(false); });
            track.addEventListener('unmute', () => { if (this.screenStream === screen) this.options.onScreenHealth(true); });
            if (this.startScreenSegment(screen) === false) throw new Error("Screen evidence recording could not start.");

            screen.getVideoTracks()[0].addEventListener('ended', () => {
                if (this.screenStream !== screen) return;
                const segment = this.screenOriginal; this.screenOriginal = null;
                if (segment?.recorder.state === 'recording') segment.recorder.stop();
                this.screenVideo?.pause(); this.screenVideo = null; this.screenStream = null;
                this.options.onScreenEnded();
            }, { once: true });
        } catch (error) { screen.getTracks().forEach(track => track.stop()); if (this.screenStream === screen) { this.screenVideo?.pause(); this.screenVideo = null; this.screenStream = null; } throw error; }
    }

    drawFrame() {
        if (!this.running || !this.context || !this.canvas) return;
        const { width, height } = this.canvas;
        const context = this.context;
        context.fillStyle = '#07111f';
        context.fillRect(0, 0, width, height);

        const screenActive = this.screenVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && this.screenStream?.getVideoTracks().some(t => t.readyState === 'live' && !t.muted);
        const cameraActive = this.cameraVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && this.options.cameraStream?.getVideoTracks().some(t => t.readyState === 'live' && !t.muted);
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
        context.fillText(screenActive ? 'Review copy' : 'Screen unavailable', screenActive ? width * .85 : 16, 24);
        context.font = '500 13px system-ui, sans-serif';
        context.fillStyle = '#dbeafe';
        context.font = '10px system-ui';
        context.fillText(new Date().toISOString().slice(11, 23) + ' UTC', screenActive ? width * .85 : 16, 44);
    }

    startSegment() {
        if (!this.running || !this.combinedStream) return;
        this.currentChunks = [];
        let overflow = false;
        this.segmentStartedAt = performance.now();
        const metadata = this.segmentMetadata('composite');
        const recorderOptions = {
            videoBitsPerSecond: this.options.videoBitsPerSecond
        };
        if (this.mimeType) recorderOptions.mimeType = this.mimeType;

        this.recorder = new MediaRecorder(this.combinedStream, recorderOptions);
        this.recorder.addEventListener('dataavailable', event => {
            if (event.data?.size) {
                if (this.currentChunks.reduce((n, b) => n + b.size, 0) + event.data.size <= 6000000) this.currentChunks.push(event.data);
                else { overflow = true; this.currentChunks = []; this.options.onScreenHealth(false); }
                if (this.currentChunks.reduce((n, b) => n + b.size, 0) >= 5000000 && this.recorder.state === 'recording') this.recorder.stop();
            }
        });
        this.recorder.addEventListener('stop', () => {
            if (!overflow && this.currentChunks.length) {
                const type = this.mimeType || this.currentChunks[0]?.type || 'video/webm';
                this.lastCompletedSegment = new Blob(this.currentChunks, { type });
                this.options.onSegment(this.lastCompletedSegment);
                this.options.onMediaSegment(this.lastCompletedSegment, this.finishMetadata(metadata));
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
    startOriginalSegment() {
        if (!this.running || this.options.cameraStream?.getVideoTracks?.().every(t => t.readyState === 'ended')) return;
        const chunks = [];
        let overflow = false;
        let recorder;
        try {
            recorder = new MediaRecorder(this.options.cameraStream, {
                ...(this.mimeType ? { mimeType: this.mimeType } : {}),
                videoBitsPerSecond: this.options.videoBitsPerSecond
            });
        } catch { this.original = null; return; }
        const metadata = this.segmentMetadata('camera');
        const segment = { recorder, startedAt: Date.now(), timer: 0, requested: false };
        segment.done = new Promise(resolve => {
            recorder.addEventListener('dataavailable', event => {
                if (event.data?.size) {
                    if (chunks.reduce((n, b) => n + b.size, 0) + event.data.size <= 6000000) chunks.push(event.data);
                    else { overflow = true; chunks.length = 0; this.options.onScreenHealth(false); }
                    if (chunks.reduce((n, b) => n + b.size, 0) >= 5000000 && recorder.state === 'recording') recorder.stop();
                }
            });
            recorder.addEventListener('stop', () => {
                window.clearTimeout(segment.timer);
                segment.endedAt = Date.now();
                const blob = !overflow && chunks.length ? new Blob(chunks, {
                    type: recorder.mimeType || this.mimeType || 'video/webm'
                }) : null;
                if (blob?.size) {
                    this.options.onOriginalSegment(blob, segment.startedAt, segment.endedAt);
                    this.options.onMediaSegment(blob, this.finishMetadata(metadata));
                }
                resolve(blob);
                if (this.original === segment) {
                    this.original = null;
                    if (this.running) this.startOriginalSegment();
                }
            }, { once: true });
            recorder.addEventListener('error', () => {
                resolve(null); // No fabricated evidence or stale fallback.
                if (this.original === segment) this.original = null;
                try { if (recorder.state !== 'inactive') recorder.stop(); } catch { }
            }, { once: true });
        });
        this.original = segment;
        recorder.start(1000);
        segment.timer = window.setTimeout(() => {
            if (recorder.state === 'recording') recorder.stop();
        }, this.originalSegmentMilliseconds || 20000);
    }

    segmentMetadata(source) {
        return {
            source, timelineId: this.timelineId, startedAt: Date.now(), monotonicStart: globalThis.performance?.now?.() ?? null,
            surface: this.captureSurface || 'unknown', derived: source === 'composite', screenAvailable: !!this.screenStream,
            documentHidden: globalThis.document?.hidden === true
        };
    }
    finishMetadata(metadata) { return { ...metadata, endedAt: Date.now(), monotonicEnd: globalThis.performance?.now?.() ?? null }; }
    startScreenSegment(stream) {
        if (!this.running || this.screenStream !== stream || !stream.getVideoTracks().some(t => t.readyState === 'live')) return;
        // Record the ORIGINAL user-authorized screen track and the existing exam
        // microphone together. Screen selection remains permission-gated by the
        // browser; no second microphone permission request is made.
        const mic = this.options.cameraStream?.getAudioTracks?.()
            .find(track => track.readyState === 'live');
        // Use a clone so stopping one short screen clip never stops proctoring audio.
        const micClone = mic ? mic.clone() : null;
        const media = new MediaStream([
            ...stream.getVideoTracks(), ...(micClone ? [micClone] : [])
        ]);
        let recorder;
        try { recorder = new MediaRecorder(media, { ...(this.mimeType ? { mimeType: this.mimeType } : {}), videoBitsPerSecond: 1500000 }); }
        catch {
            micClone?.stop();
            this.options.onScreenHealth(false);
            return false;
        }
        let overflow = false;
        const chunks = [], metadata = { ...this.segmentMetadata('screen'), includesMicrophone: !!micClone }, segment = { recorder, timer: 0 }; this.screenOriginal = segment;
        recorder.addEventListener('dataavailable', e => {
            if (e.data?.size) {
                if (chunks.reduce((n, b) => n + b.size, 0) + e.data.size <= 6000000) chunks.push(e.data);
                else { overflow = true; chunks.length = 0; this.options.onScreenHealth(false); }
                if (chunks.reduce((n, b) => n + b.size, 0) >= 5000000 && recorder.state === 'recording') recorder.stop();
            }
        });
        recorder.addEventListener('stop', () => {
            window.clearTimeout(segment.timer);
            micClone?.stop();
            if (!overflow && chunks.length) this.options.onMediaSegment(new Blob(chunks, { type: recorder.mimeType || this.mimeType || 'video/webm' }), this.finishMetadata(metadata));
            if (this.screenOriginal === segment) { this.screenOriginal = null; this.startScreenSegment(stream); }
        }, { once: true });
        recorder.addEventListener('error', () => { if (this.screenOriginal === segment) this.screenOriginal = null; this.options.onScreenHealth(false); try { recorder.stop(); } catch { } }, { once: true });
        try {
            recorder.start(1000);
        } catch {
            micClone?.stop();
            if (this.screenOriginal === segment) this.screenOriginal = null;
            this.options.onScreenHealth(false);
            return false;
        }
        segment.timer = window.setTimeout(() => { if (recorder.state === 'recording') recorder.stop(); }, 10000);
        return true;
    }
    captureScreenDataUrl() {
        if (!this.screenStream?.getVideoTracks().some(t => t.readyState === 'live' && !t.muted) || !this.screenVideo?.videoWidth) return null;
        try {
            // ASP.NET limits QuestionScreenshotDataUrl to 300k characters.
            // Preserve a legible question screenshot while bounding upload size.
            const canvas = document.createElement('canvas');
            let scale = Math.min(1, 1600 / this.screenVideo.videoWidth);
            for (let attempt = 0; attempt < 5; attempt++) {
                canvas.width = Math.max(1, Math.round(this.screenVideo.videoWidth * scale));
                canvas.height = Math.max(1, Math.round(this.screenVideo.videoHeight * scale));
                const ctx = canvas.getContext('2d'); if (!ctx) return null;
                ctx.drawImage(this.screenVideo, 0, 0, canvas.width, canvas.height);
                const image = canvas.toDataURL('image/jpeg', Math.max(.5, .82 - attempt * .07));
                if (image.length <= 245000) return image;
                scale *= .8;
            }
            return null; // Avoid rejecting the whole ASR request for an oversized image.
        } catch { return null; }
    }

    async captureEvidenceBlob() {
        const segment = this.original;
        if (!segment || segment.recorder.state !== 'recording') return null;
        if (!segment.requested) {
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
        window.clearTimeout(this.original?.timer);
        try { if (this.original?.recorder.state === 'recording') this.original.recorder.stop(); } catch { }
        window.clearInterval(this.drawTimer);
        window.clearTimeout(this.segmentTimer);
        try {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        } catch { }
    }

    stop() {
        this.stoppingIntentionally = true;
        this.running = false;
        const screenSegment = this.screenOriginal; this.screenOriginal = null;
        window.clearTimeout(screenSegment?.timer);
        try { if (screenSegment?.recorder.state === 'recording') screenSegment.recorder.stop(); } catch { }
        window.clearTimeout(this.original?.timer);
        try { if (this.original?.recorder.state === 'recording') this.original.recorder.stop(); } catch { }
        window.clearInterval(this.drawTimer);
        window.clearTimeout(this.segmentTimer);
        try {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        } catch { }
        this.combinedStream?.getTracks?.().forEach(track => track.stop());
        this.cameraVideo?.pause?.();
        this.screenStream?.getTracks().forEach(track => track.stop());
        this.screenVideo?.pause?.();
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

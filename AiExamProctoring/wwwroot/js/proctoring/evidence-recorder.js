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
            onScreenEnded: () => { },
            onSegment: options.onSegment || (() => {})
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
    }

    async requestAndStart() {
        if (this.running) return this.status();
        const cameraTrack = this.options.cameraStream?.getVideoTracks?.()
            .find(track => track.readyState === 'live');
        if (!cameraTrack)
            throw new Error('A live camera stream is required for event evidence recording.');
        if (typeof MediaRecorder === 'undefined')
            throw new Error('This browser does not support event evidence recording.');

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
        const screen = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 8 }, audio: false });
        try {
            const video = await this.createVideo(screen);
            if (!this.running) throw new Error('The recording has ended.');
            this.screenStream?.getTracks().forEach(track => track.stop());
            this.screenStream = screen;
            this.screenVideo = video;
            screen.getVideoTracks()[0].addEventListener('ended', () => {
                if (this.screenStream !== screen) return;
                this.screenVideo?.pause(); this.screenVideo = null; this.screenStream = null;
                this.options.onScreenEnded();
            }, { once: true });
        } catch (error) { screen.getTracks().forEach(track => track.stop()); throw error; }
    }

    drawFrame() {
        if (!this.running || !this.context || !this.canvas) return;
        const { width, height } = this.canvas;
        const context = this.context;
        context.fillStyle = '#07111f';
        context.fillRect(0, 0, width, height);

        const screenActive = this.screenVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA;
        if (screenActive) {
            const scale = Math.min(width / this.screenVideo.videoWidth, height / this.screenVideo.videoHeight);
            const sw = this.screenVideo.videoWidth * scale, sh = this.screenVideo.videoHeight * scale;
            context.drawImage(this.screenVideo, (width - sw) / 2, (height - sh) / 2, sw, sh);
        }
        if (screenActive && this.cameraVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
            drawCover(context, this.cameraVideo, width * .73, height * .68, width * .26, height * .31);
        } else if (this.cameraVideo?.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
            context.save();
            context.translate(width, 0);
            context.scale(-1, 1);
            drawCover(context, this.cameraVideo, 0, 0, width, height);
            context.restore();
        }

        context.font = '600 15px system-ui, sans-serif';
        context.fillStyle = '#ffffff';
        context.fillText(screenActive ? 'Exam evidence · shared screen + camera + microphone' : 'Exam evidence · camera + microphone', 16, 24);
        context.font = '500 13px system-ui, sans-serif';
        context.fillStyle = '#dbeafe';
        context.fillText(new Date().toISOString(), 16, 44);
    }

    startSegment() {
        if (!this.running || !this.combinedStream) return;
        this.currentChunks = [];
        this.segmentStartedAt = performance.now();
        const recorderOptions = {
            videoBitsPerSecond: this.options.videoBitsPerSecond
        };
        if (this.mimeType) recorderOptions.mimeType = this.mimeType;

        this.recorder = new MediaRecorder(this.combinedStream, recorderOptions);
        this.recorder.addEventListener('dataavailable', event => {
            if (event.data?.size) this.currentChunks.push(event.data);
        });
        this.recorder.addEventListener('stop', () => {
            if (this.currentChunks.length) {
                const type = this.mimeType || this.currentChunks[0]?.type || 'video/webm';
                this.lastCompletedSegment = new Blob(this.currentChunks, { type });
                this.options.onSegment(this.lastCompletedSegment);
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

    async captureEvidenceBlob() {
        if (this.recorder?.state === 'recording') {
            try {
                const currentAge = performance.now() - this.segmentStartedAt;
                if (currentAge < 1_500)
                    await wait(1_500 - currentAge);
                this.recorder.requestData();
                await wait(320);
            } catch { }
        } else {
            await wait(120);
        }

        const type = this.mimeType || this.currentChunks[0]?.type || 'video/webm';
        const current = this.currentChunks.length
            ? new Blob(this.currentChunks, { type })
            : null;
        const currentAge = performance.now() - this.segmentStartedAt;
        if (current && current.size >= 12_000 && currentAge >= 900)
            return current;
        return this.lastCompletedSegment?.size ? this.lastCompletedSegment : current;
    }

    captureEvidenceDataUrl() {
        if (!this.running || !this.canvas) return null;
        try {
            this.drawFrame(); // Snapshot current media, not the previous recording tick.
            return this.canvas.toDataURL('image/jpeg', 0.68);
        } catch {
            return null;
        }
    }

    status() {
        return {
            active: this.running &&
                this.options.cameraStream?.getVideoTracks?.().some(item => item.readyState === 'live') === true,
            includesMicrophone: this.combinedStream?.getAudioTracks?.().length > 0,
            cameraOnly: !this.screenStream?.getVideoTracks().some(track => track.readyState === 'live')
        };
    }

    handleScreenEnded() {
        if (this.stoppingIntentionally) return;
        this.running = false;
        window.clearInterval(this.drawTimer);
        window.clearTimeout(this.segmentTimer);
        try {
            if (this.recorder?.state === 'recording') this.recorder.stop();
        } catch { }
    }

    stop() {
        this.stoppingIntentionally = true;
        this.running = false;
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

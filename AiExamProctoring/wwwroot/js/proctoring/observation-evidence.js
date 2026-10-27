import { EvidenceRecorder } from './evidence-recorder.js?v=20261001-screen';

// Event clips contain camera + microphone only. Sound level is an observation,
// never speech recognition, speaker identification or proof of assistance.
export class ObservationEvidence {
    constructor({ stream, token, uploadUrl, recordingUrl, attemptId, sessionAttemptId, onStatus }) {
        this.recordingUrl = recordingUrl; this.attemptId = attemptId; this.sessionAttemptId = sessionAttemptId;
        this.segmentQueue = Promise.resolve(); this.pendingSegments = 0;
        this.recorder = new EvidenceRecorder({ cameraStream: stream, videoBitsPerSecond: 400000,
            onSegment: blob => this.saveSegment(blob) });
        this.stream = stream; this.token = token; this.uploadUrl = uploadUrl;
        this.onStatus = onStatus;
        this.pending = new Set(); this.chain = Promise.resolve(); this.stopped = false;
    }
    async start() {
        await this.recorder.requestAndStart();
        this.onStatus('Camera + microphone session recording active');
    }

    screenshot() { return this.recorder.captureEvidenceDataUrl(); }
    saveSegment(blob) {
        if (!this.recordingUrl || !blob?.size) return;
        if (this.pendingSegments >= 6) {
            this.onStatus('Recording gap: uploads cannot keep up. Check your connection and tell your teacher.');
            return;
        }
        this.pendingSegments++;
        const eventId = crypto.randomUUID().replaceAll('-', '');
        const includesAudio = this.stream.getAudioTracks().length > 0;
        this.segmentQueue = this.segmentQueue.then(async () => {
            let saved = false;
            for (let retry = 0; retry < 3 && !saved; retry++) {
                const body = new FormData();
                body.append('attemptId', this.attemptId); body.append('sessionAttemptId', this.sessionAttemptId);
                body.append('eventId', eventId); body.append('includesAudio', String(includesAudio));
                body.append('recording', blob, 'session.webm'); body.append('__RequestVerificationToken', this.token);
                try {
                    const response = await fetch(this.recordingUrl, { method: 'POST', credentials: 'same-origin', body,
                        signal: AbortSignal.timeout(20000) });
                    saved = response.ok;
                    if (response.status >= 400 && response.status < 500 && response.status !== 429) break;
                } catch {}
                if (!saved) await new Promise(resolve => setTimeout(resolve, 1000 * (retry + 1)));
            }
            if (!saved) this.onStatus('Recording gap: a session clip was not saved. Please tell your teacher.');
        }).catch(() => this.onStatus('Recording upload interrupted.')).finally(() => this.pendingSegments--);
    }
    async attach(logIds) {
        const ids = [...new Set(logIds)].filter(id => Number.isInteger(id) && id > 0 && !this.pending.has(id));
        if (!ids.length || this.stopped) return;
        if (this.pending.size + ids.length > 20) { this.onStatus('Evidence uploads are delayed. Please check your connection.'); return; }
        ids.forEach(id => this.pending.add(id));
        const screenshot = this.screenshot();
        let blob;
        try { blob = await this.recorder.captureEvidenceBlob(); } catch {}
        if (!blob?.size) { ids.forEach(id => this.pending.delete(id)); this.onStatus('A clip could not be captured for this observation.'); return; }
        const includesAudio = this.recorder.status().includesMicrophone;
        this.chain = this.chain.then(async () => {
            for (const id of ids) {
                let saved = false;
                for (let retry = 0; retry < 3 && !saved; retry++) {
                    const body = new FormData();
                    body.append('LogId', String(id)); if(screenshot)body.append('ScreenshotDataUrl',screenshot); body.append('IncludesAudio', String(includesAudio));
                    body.append('EvidenceFile', blob, 'observation.webm');
                    body.append('__RequestVerificationToken', this.token);
                    try {
                        const response = await fetch(this.uploadUrl, { method: 'POST', credentials: 'same-origin', body, signal: AbortSignal.timeout(20000) });
                        saved = response.ok;
                        if (response.status >= 400 && response.status < 500 && response.status !== 429) break;
                    } catch {}
                    if (!saved) await new Promise(resolve => setTimeout(resolve, 1000 * (retry + 1)));
                }
                this.pending.delete(id);
                if (!saved) this.onStatus('A clip upload failed. Your saved answers are unaffected; tell your teacher.');
            }
        }).catch(() => this.onStatus('Evidence upload interrupted. Check your connection.'));
    }
    stop() {
        this.stopped = true; this.recorder.stop(); clearInterval(this.soundTimer);
        document.removeEventListener('pointerdown', this.resumeAudio);
        document.removeEventListener('keydown', this.resumeAudio);
        this.audio?.close().catch(() => {});
    }
}

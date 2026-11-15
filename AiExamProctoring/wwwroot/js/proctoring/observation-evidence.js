import { DurableEvidenceQueue } from './durable-evidence.js?v=session-timeline';
import { EvidenceRecorder } from './evidence-recorder.js?v=session-timeline';

// Event clips contain camera + microphone only. Sound level is an observation,
// never speech recognition, speaker identification or proof of assistance.
export class ObservationEvidence {
    constructor({ stream, token, uploadUrl, recordingUrl, attemptId, sessionAttemptId, owner, onStatus }) {
        this.recordingUrl = recordingUrl; this.attemptId = attemptId; this.sessionAttemptId = sessionAttemptId;
        this.segmentQueue = Promise.resolve(); this.pendingSegments = 0;
        this.recorder = new EvidenceRecorder({
            cameraStream: stream, videoBitsPerSecond: 400000,
            onMediaSegment: (blob, metadata) => this.mediaSegment(blob, metadata)
        });
        this.stream = stream; this.token = token; this.uploadUrl = uploadUrl;
        this.onStatus = onStatus;
        this.mediaWindows = new Map(); this.recentMedia = [];
        this.outbox = new DurableEvidenceQueue(`${attemptId}:${sessionAttemptId}`, token, onStatus);
        this.owner = owner;
        this.recordingWrites = new Set();
        this.speechClips = new Map();
        this.savedClips = new Set(); this.pending = new Set(); this.chain = Promise.resolve(); this.stopped = false;
        // Separate genuine incidents keep separate IDs. Limit copies of one
        // continuous incident, never the inference/policy that created it.
        this.mediaSources = new Set();
        this.closedAbsenceId = null;
    }
    async start() {
        await this.recorder.requestAndStart();
        if (this.stopped) return;
        this.outbox.flush(); this.retryTimer = setInterval(() => this.outbox.flush(), 15000);
        this.onStatus('Camera + microphone session recording active');
    }

    mediaSegment(blob, metadata) {
        if (metadata.source === 'camera' || metadata.source === 'screen') {
            const write = this.saveSegment(blob, metadata);
            this.recordingWrites.add(write);
            void write.finally(() => this.recordingWrites.delete(write));
        }
        if (!blob?.size) return;
        const packet = { blob, metadata };
        this.recentMedia.push(packet);
        for (const source of ['camera', 'screen', 'composite']) {
            const group = this.recentMedia.filter(p => p.metadata.source === source);
            for (const old of group.slice(0, -2)) this.recentMedia.splice(this.recentMedia.indexOf(old), 1);
        }
        for (const [id, window] of this.mediaWindows) {
            if (metadata.monotonicEnd >= window.start - 5000 && metadata.monotonicStart <= window.end) this.queueBrowserMedia(id, packet);
            if (performance.now() > window.end + 30000) this.mediaWindows.delete(id);
        }
    }
    queueBrowserMedia(id, packet) {
        const source = packet.metadata.source;
        this.queuedMedia ??= new Map();
        if (source === 'composite') return; // Originals already have independent evidence.
        const sourceKey = `${id}:${source}`;
        if (this.mediaSources.has(sourceKey)) return;
        const key = `${id}:${source}:${packet.metadata.timelineId}:${packet.metadata.monotonicStart}`;
        if (this.queuedMedia.has(key)) return;
        this.mediaSources.add(sourceKey);
        this.queuedMedia.set(key, performance.now());
        for (const [old, at] of this.queuedMedia) if (performance.now() - at > 120000) this.queuedMedia.delete(old);
        const generic = typeof id === "number";
        if (generic && source === "camera") return; // Its original is attached by attach().
        const destination = generic ? { logId: id } : { browserEventId: id };
        this.outbox.enqueue({
            endpoint: '/Student/Exam/IncidentEvidence', ...destination, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId,
            stage: source === 'composite' ? 'composite' : source + '-original', blob: packet.blob, metadata: packet.metadata, capturedAt: packet.metadata.startedAt
        });
    }
    browserEvent(kind, event, sendObservation = true) {
        const now = performance.now(), surface = this.recorder.captureSurface || 'unknown';
        if (sendObservation)
            this.outbox.enqueue({ browserEvent: { ...event, kind, surface, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId }, capturedAt: Date.now() });
        const closed = event.phase === 'end' || event.phase === 'stop';
        if (!this.mediaWindows.has(event.id)) {
            if (this.mediaWindows.size >= 8) { this.onStatus('Evidence capture capacity reached. Please notify your teacher.'); return; }
            const window = { start: now - event.duration * 1000, end: closed ? now + 10000 : Infinity }; this.mediaWindows.set(event.id, window);
            for (const packet of this.recentMedia) if (packet.metadata.monotonicEnd >= window.start - 5000) this.queueBrowserMedia(event.id, packet);
            const screenshot = this.recorder.captureScreenDataUrl();
            if (screenshot) this.outbox.enqueue({
                endpoint: '/Student/Exam/IncidentEvidence', browserEventId: event.id, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId,
                stage: 'screen-image', screenshot, capturedAt: Date.now(), metadata: { source: 'screen', surface, timelineId: this.recorder.timelineId, derived: false }
            });
        }
        if (closed && this.mediaWindows.has(event.id))
            this.mediaWindows.get(event.id).end = now + 10000;
    }
    absence(result) {
        const incident = result.absenceIncident;
        const state = result.presenceStatus?.state;
        const away = state === 'POSSIBLE_ABSENCE' || state === 'VERIFIED_PHYSICAL_ABSENCE';
        if (this.absenceCapture && !this.absenceCapture.segment && away)
            this.holdAbsenceSegment(this.absenceCapture);
        if (!this.absenceCapture && away && (!incident || incident.id !== this.closedAbsenceId || incident.endedAt)) {
            const segment = this.recorder.holdOriginalForAbsence();
            if (segment) {
                const capture = this.absenceCapture = { segment, logId: null, part: 0, screenshot: this.recorder.captureOriginalDataUrl?.() };
                this.watchAbsenceSegment(capture, segment);
            }
        }
        if (this.absenceCapture && incident && !incident.endedAt && incident.id !== this.closedAbsenceId)
            this.absenceCapture.logId ??= incident.id;
        if (this.absenceCapture && (state === 'PERSON_VISIBLE_IDENTITY_UNCERTAIN' ||
            (incident?.endedAt && incident.id === this.absenceCapture.logId) ||
            result.state?.status === 'ProctoringBlocked')) {
            void this.finishAbsenceCapture(result.state?.status === 'ProctoringBlocked' ? 'exam_blocked' : 'physical_return');
        }
    }

    holdAbsenceSegment(capture) {
        const segment = this.recorder.holdOriginalForAbsence();
        if (!segment) return;
        capture.segment = segment;
        this.watchAbsenceSegment(capture, segment);
    }

    watchAbsenceSegment(capture, segment) {
        // Keep each WebM independently playable and below the upload size limit.
        capture.rollover = setTimeout(() => {
            if (this.absenceCapture === capture && capture.segment === segment &&
                capture.logId && segment.recorder.state === 'recording') {
                segment.recorder.stop();
            }
        }, 90000);
        // Browser cleanup also releases this timer after a return or block.
        capture.rollover?.unref?.();
        void segment.done.then(async blob => {
            if (this.absenceCapture !== capture || capture.segment !== segment || !capture.logId) return;
            clearTimeout(capture.rollover);
            capture.segment = null;
            const reason = segment.captureFailure || (segment.absenceTruncated ? 'recording_limit' : 'segment_rollover');
            if (!segment.captureFailure) this.holdAbsenceSegment(capture);
            await this.saveAbsenceSegment(capture, segment, blob, reason);
        });
    }

    people(result) {
        const incident = result.peopleIncident;
        if (!this.peopleCapture && result.peopleSignal === true &&
            (!incident || incident.id !== this.closedPeopleId || incident.endedAt)) {
            const segment = this.recorder.holdOriginalForIncident();
            if (segment) {
                this.peopleCapture = { segment, logId: null, screenshot: this.recorder.captureOriginalDataUrl?.() };
                void segment.done.then(() => {
                    if (this.peopleCapture?.segment === segment && this.peopleCapture.logId)
                        void this.finishPeopleCapture(segment.captureFailure || 'recording_limit');
                });
            }
        }
        if (this.peopleCapture && incident?.confirmed && !incident.endedAt && incident.id !== this.closedPeopleId)
            this.peopleCapture.logId ??= incident.id;
        if (this.peopleCapture?.logId && this.peopleCapture.segment.recorder.state === 'inactive')
            void this.finishPeopleCapture(this.peopleCapture.segment.captureFailure || 'recording_limit');
        if (this.peopleCapture && (incident?.endedAt && incident.id === this.peopleCapture.logId ||
            result.peopleSignal === false && !this.peopleCapture.logId || result.state?.status === 'ProctoringBlocked'))
            void this.finishPeopleCapture(result.state?.status === 'ProctoringBlocked' ? 'exam_blocked' : 'physical_return');
    }

    async finishPeopleCapture(reason = 'exam_ended') {
        const capture = this.peopleCapture;
        if (!capture) return;
        this.peopleCapture = null;
        const { segment, logId } = capture;
        if (logId) this.closedPeopleId = logId;
        if (segment.recorder.state === 'recording') segment.recorder.stop();
        if (!logId) return;
        const blob = await segment.done;
        if (!blob?.size) {
            this.onStatus('Multiple Persons video unavailable. Tell your teacher.');
            return;
        }
        await this.outbox.enqueue({ incidentClip: true, logId, blob, screenshot: capture.screenshot, includesAudio: segment.includesMicrophone,
            attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId,
            metadata: { stopReason: segment.captureFailure || (segment.absenceTruncated ? 'recording_limit' : reason) } });
    }

    async finishAbsenceCapture(reason = 'exam_ended') {
        const capture = this.absenceCapture;
        if (!capture) return;
        this.absenceCapture = null;
        clearTimeout(capture.rollover);
        const { segment, logId } = capture;
        if (logId) this.closedAbsenceId = logId;
        if (!segment) return;
        if (segment.recorder.state === 'recording') segment.recorder.stop();
        if (!logId) return;
        const blob = await segment.done;
        await this.saveAbsenceSegment(capture, segment, blob, reason);
    }

    async saveAbsenceSegment(capture, segment, blob, reason) {
        const { logId } = capture;
        if (!blob?.size) {
            this.onStatus('Absence video unavailable. Tell your teacher.');
            return;
        }
        const part = capture.part++;
        const timestamp = segment.startedAt;
        const continued = part > 0 || this.absenceCapture === capture;
        const queued = await this.outbox.enqueue({ ...(continued
                ? { endpoint: '/Student/Exam/IncidentEvidence', stage: 'absence-segment', capturedAt: timestamp, absenceSegment: true }
                : { incidentClip: true, screenshot: capture.screenshot }),
            logId, blob,
            includesAudio: segment.includesMicrophone, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId,
            metadata: { stopReason: segment.absenceTruncated ? 'recording_limit' : segment.captureFailure || reason,
                startedAt: segment.startedAt, endedAt: segment.endedAt || Date.now(),
                includesMicrophone: segment.includesMicrophone === true, reportedBy: 'browser', part } });
        // Attach the authorized screen segment that covers the most of this episode.
        const overlap = packet => Math.max(0, Math.min(packet.metadata.endedAt, segment.endedAt || Date.now()) -
            Math.max(packet.metadata.startedAt, segment.startedAt));
        const screen = this.recentMedia.filter(packet => packet.metadata.source === 'screen' && overlap(packet) > 0)
            .sort((a, b) => overlap(b) - overlap(a))[0];
        if (screen) this.queueBrowserMedia(logId, screen);
        if (queued && segment.absenceTruncated)
            this.onStatus('Absence clip reached the recording limit. Only the available footage was saved.');
    }
    screenshot() { return this.recorder.captureOriginalDataUrl(); }
    async saveSegment(blob, metadata) {
        if (!this.recordingUrl || metadata.endedAt <= metadata.startedAt) return;
        // Successful IndexedDB commit precedes retries; server acknowledges the same ID.
        const row = { recording: true, blob, attemptId: this.attemptId, owner: this.owner,
            sessionAttemptId: this.sessionAttemptId, includesAudio: metadata.includesMicrophone,
            capturedAt: metadata.startedAt, metadata };
        if (await this.outbox.enqueue(row)) return;
        // Quota errors must leave an explicit missing interval, even without the blob.
        if (blob && !await this.outbox.enqueue({ ...row, blob: null,
            metadata: { ...metadata, status: 'missing', includesMicrophone: false } }))
            this.onStatus('Recording and its gap could not be saved locally. Please notify your teacher.');
    }
    startIdentityCapture() {
        if (this.identityCapture || !this.recorder.mimeType) return;
        let recorder;
        try {
            recorder = new MediaRecorder(this.stream, { mimeType: this.recorder.mimeType, videoBitsPerSecond: 400000 });
        } catch { return; }
        const includesAudio = this.stream.getAudioTracks().some(track =>
            track.readyState === 'live' && track.enabled && !track.muted);
        const chunks = [];
        let bytes = 0, exceeded = false;
        const capture = { recorder, includesAudio };
        capture.stop = () => {
            clearTimeout(capture.timer);
            if (recorder.state !== 'inactive') recorder.stop();
        };
        capture.done = new Promise(resolve => {
            recorder.addEventListener('dataavailable', ({ data }) => {
                if (!data?.size || exceeded) return;
                if (bytes + data.size > 18000000) {
                    exceeded = true;
                    capture.incomplete = true;
                    capture.stop();
                    return;
                }
                bytes += data.size;
                chunks.push(data);
            });
            recorder.addEventListener('stop', () => {
                clearTimeout(capture.timer);
                resolve(chunks.length ? new Blob(chunks, { type: recorder.mimeType }) : null);
            }, { once: true });
            recorder.addEventListener('error', () => { clearTimeout(capture.timer); capture.stopReason = 'recorder_error'; resolve(chunks.length ? new Blob(chunks, { type: recorder.mimeType }) : null); }, { once: true });
        });
        try {
            recorder.start(1000);
            capture.timer = setTimeout(() => { capture.incomplete = true; capture.stop(); }, 90000);
            this.identityCapture = capture;
        } catch { /* Identity checks continue if the recorder cannot start. */ }
    }

    async finishIdentityCapture(logId) {
        if (logId && this.identityEvidenceId === logId) return;
        const capture = this.identityCapture;
        if (!capture) {
            if (logId) this.onStatus('Identity sequence video is unavailable. Tell your teacher.');
            return;
        }
        if (logId) this.identityEvidenceId = logId;
        const screenshot = logId ? this.recorder.captureOriginalDataUrl?.() : null;
        this.identityCapture = null;
        capture.stop();
        if (!logId) return;
        const blob = await capture.done;
        if (!blob?.size) {
            this.onStatus('Identity sequence video is unavailable. Tell your teacher.');
            return;
        }
        await this.outbox.enqueue({ incidentClip: true, logId, blob, screenshot, includesAudio: capture.includesAudio,
            metadata: { stopReason: capture.stopReason || (capture.incomplete ? 'recording_limit' : 'exam_blocked') },
            attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId });
    }

    stageSpeech(logId) {
        if (!Number.isInteger(logId) || this.speechClips.has(logId) || this.savedClips.has(logId)) return;
        if (this.speechClips.size >= 32) this.speechClips.delete(this.speechClips.keys().next().value);
        const screenshot = this.recorder.captureOriginalDataUrl();
        const includesAudio = this.recorder.original?.includesMicrophone === true;
        const clip = this.recorder.captureEvidenceBlob()
            .then(blob => ({ blob, screenshot, includesAudio }))
            .catch(() => null);
        this.speechClips.set(logId, clip);
    }

    async commitSpeech(logId) {
        if (this.savedClips.has(logId) || this.pending.has(logId)) return;
        this.pending.add(logId);
        try {
            const media = await this.speechClips.get(logId);
            if (!media?.blob?.size) {
                if (media?.screenshot && await this.outbox.enqueue({
                    endpoint: '/Student/Exam/IncidentEvidence', stage: 'during',
                    logId, screenshot: media.screenshot, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId
                })) this.savedClips.add(logId);
                this.onStatus('The camera clip for this speech incident is unavailable.');
                return;
            }
            const queued = await this.outbox.enqueue({ ...media, incidentClip: true, logId,
                attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId });
            if (queued) {
                this.savedClips.add(logId);
                this.speechClips.delete(logId);
            }
        } finally { this.pending.delete(logId); }
    }

    async attach(logIds) {
        const ids = [...new Set(logIds)].filter(id => Number.isInteger(id) && id > 0 && !this.pending.has(id) && !this.savedClips.has(id));
        if (!ids.length || this.stopped) return;
        if (this.pending.size + ids.length > 20) { this.onStatus('Evidence uploads are delayed. Please check your connection.'); return; }
        ids.forEach(id => this.pending.add(id));
        const now = performance.now();
        for (const id of ids) {
            if (!this.recorder.screenStream || this.mediaWindows.has(id) || this.mediaWindows.size >= 8) continue;
            this.mediaWindows.set(id, { start: now, end: now + 10000 });
            for (const packet of this.recentMedia) {
                if (packet.metadata.monotonicEnd >= now - 5000) this.queueBrowserMedia(id, packet);
            }
            const screen = this.recorder.captureScreenDataUrl();
            if (screen) this.outbox.enqueue({
                endpoint: "/Student/Exam/IncidentEvidence", logId: id, stage: "screen-image",
                attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId, screenshot: screen, capturedAt: Date.now()
            });
        }
        const screenshot = this.recorder.captureOriginalDataUrl();
        const includesAudio = this.recorder.original?.includesMicrophone === true;
        let blob;
        try { blob = await this.recorder.captureEvidenceBlob(); } catch { }
        if (!blob?.size) {
            for (const id of ids) {
                if (screenshot) await this.outbox.enqueue({
                    endpoint: '/Student/Exam/IncidentEvidence', logId: id,
                    attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId, stage: 'during',
                    screenshot, capturedAt: Date.now()
                });
                this.pending.delete(id);
            }
            this.onStatus('An incident screenshot was queued, but a WebM clip could not be captured. Tell your teacher.');
            return;
        }
        for (const id of ids) {
            const queued = await this.outbox.enqueue({
                incidentClip: true, logId: id, blob, screenshot,
                includesAudio, attemptId: this.attemptId, sessionAttemptId: this.sessionAttemptId
            });
            if (queued) this.savedClips.add(id);
            this.pending.delete(id);
        }
    }
    stop() {
        if (this.stopped) return this.finalizing;
        void this.finishAbsenceCapture();
        void this.finishPeopleCapture();
        this.stopped = true; clearInterval(this.retryTimer); this.identityCapture?.stop(); clearInterval(this.soundTimer);
        this.finalizing = Promise.resolve(this.recorder.stop()).then(() => Promise.allSettled(this.recordingWrites));
        return this.finalizing;
        document.removeEventListener('pointerdown', this.resumeAudio);
        document.removeEventListener('keydown', this.resumeAudio);
        this.audio?.close().catch(() => { });
    }
}



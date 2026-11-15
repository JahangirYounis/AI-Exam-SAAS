(() => {
    'use strict';
    const root = document.getElementById('examPrecheck');
    if (!root || root.dataset.initialized) return;
    root.dataset.initialized = 'true';
    const config = root.dataset;
    const el = id => document.getElementById(id);
    const form = el('startExamForm');
    const video = el('precheckCamera');
    const check = el('checkDevices');
    const start = el('startExam');
    const agreement = el('examAgreement');
    const ticket = el('verificationTicket');
    const microphoneRequired = config.microphone === 'true';
    const fullscreenRequired = config.fullscreen === 'true';
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const selectors = {};
    const micHelp = document.createElement('p'); micHelp.className = 'small mt-2'; micHelp.setAttribute('role', 'status');
    if (microphoneRequired) check.before(micHelp);
    for (const [kind, title, key] of [['audioinput', 'Microphone', 'examMicrophoneId'], ['videoinput', 'Camera', 'examCameraId']]) {
        const panel = document.createElement('div'); panel.hidden = true; panel.className = 'mb-3';
        const select = document.createElement('select'); select.className = 'form-select'; select.id = 'precheck-' + kind;
        const label = document.createElement('label'); label.htmlFor = select.id; label.textContent = title + ' input';
        panel.append(label, select); check.before(panel); selectors[kind] = { panel, select, key };
        select.addEventListener('change', () => {
            try { sessionStorage.setItem(key, select.value); } catch { }
            const trackKind = kind === 'audioinput' ? 'audio' : 'video';
            stream?.getTracks().filter(t => t.kind === trackKind).forEach(t => { stream.removeTrack(t); t.stop(); });
            if (trackKind === 'audio') closeAudio(); else clearIdentity();
            micHelp.textContent = title + ' changed. Run the check again.'; updateDeviceLabels(); update();
        });
    }
    const micSelect = selectors.audioinput.select;
    async function listMicrophones() {
        if (!navigator.mediaDevices?.enumerateDevices) return;
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            for (const [kind, { panel, select, key }] of Object.entries(selectors)) {
                // Chrome exposes default/communications aliases alongside the actual endpoint.
                const unique = [...new Map(devices.filter(d => d.kind === kind && d.deviceId && !['default', 'communications'].includes(d.deviceId)).map(d => [d.deviceId, d])).values()];
                let selected = select.value; try { selected = selected || sessionStorage.getItem(key) || ''; } catch { }
                const old = devices.find(d => d.kind === kind && d.deviceId === selected);
                if (old && ['default', 'communications'].includes(selected)) selected = unique.find(d => d.groupId && d.groupId === old.groupId)?.deviceId || '';
                select.replaceChildren(); unique.forEach((d, i) => select.add(new Option(d.label || `${kind === 'audioinput' ? 'Microphone' : 'Camera'} ${i + 1}`, d.deviceId)));
                if (unique.some(d => d.deviceId === selected)) select.value = selected;
                panel.hidden = unique.length < 2 || (kind === 'audioinput' && !microphoneRequired);
                try { if (!panel.hidden && select.value) sessionStorage.setItem(key, select.value); else sessionStorage.removeItem(key); } catch { }
            }
        } catch { micHelp.textContent = 'Allow device permission to identify available inputs.'; }
    }
    navigator.mediaDevices?.addEventListener?.('devicechange', listMicrophones);

    // Audio level is diagnostic only. Silence does not prove disconnection, and
    // previously buffered samples must never override current track health.
    let audioContext = null, audioSource = null, audioSink = null, audioAnalyser = null;
    let audioFlowing = false;
    function closeAudio() {
        audioSource?.disconnect(); audioAnalyser?.disconnect(); audioSink?.disconnect();
        audioSource = null; audioAnalyser = null; audioSink = null; audioFlowing = false;
        const old = audioContext; audioContext = null; old?.close().catch(() => { });
    }
    function primeAudio() {
        const Context = window.AudioContext || window.webkitAudioContext;
        if (!Context) return;
        if (!audioContext || audioContext.state === 'closed') audioContext = new Context();
        audioContext.resume().catch(() => { });
    }
    function consumeAudio() {
        primeAudio(); if (!audioContext || !stream) return;
        audioSource?.disconnect(); audioAnalyser?.disconnect(); audioSink?.disconnect();
        audioFlowing = false;
        const tracks = stream.getAudioTracks();
        if (!tracks.length) return;
        audioSource = audioContext.createMediaStreamSource(new MediaStream(tracks));
        audioAnalyser = audioContext.createAnalyser(); audioAnalyser.fftSize = 2048;
        audioSink = audioContext.createGain(); audioSink.gain.value = 0;
        audioSource.connect(audioAnalyser); audioAnalyser.connect(audioSink); audioSink.connect(audioContext.destination);
    }
    function detectSignal() {
        if (!audioAnalyser) return false;
        const buffer = new Float32Array(audioAnalyser.fftSize);
        audioAnalyser.getFloatTimeDomainData(buffer);
        for (let i = 0; i < buffer.length; i++) if (buffer[i] !== 0) return true;
        return false;
    }

    const faceReady = config.faceReady === 'true';
    const frameCount = Number(config.frames);
    const supported = window.isSecureContext && !!navigator.mediaDevices?.getUserMedia &&
        typeof fetch === 'function' && typeof AbortController === 'function';
    const fullscreenSupported = !fullscreenRequired || (document.fullscreenEnabled === true &&
        typeof document.documentElement.requestFullscreen === 'function');
    let mediaGeneration = 0;
    let microphoneProblem = "";
    let stream = null, busy = false, submitting = false, disposed = false;
    let identityDeadline = 0, deviceDeadline = 0, currentAbort = null;
    let released = config.released === 'true', terminal = config.terminal === 'true';
    let reusable = config.reusable === 'true';
    let startsAt = Date.parse(config.start), latestAt = Date.parse(config.latest);
    let serverAnchor = Date.parse(config.serverNow), timeAnchor = performance.now();
    let serverConfirmedAt = -Infinity, pollBusy = false, pollAbort = null;
    const initialLabel = 'Check devices & verify face';
    const text = (id, value, tone = '') => {
        const node = el(id);
        if (node) { node.textContent = value; node.dataset.tone = tone; }
    };
    const now = () => serverAnchor + performance.now() - timeAnchor;
    // Current track health determines availability; silence is not a failure.
    const live = kind => !!stream?.getTracks().some(track => track.kind === kind &&
        track.readyState === 'live' && track.enabled &&
        !track.muted);
    const devicesReady = () => live('video') && (!microphoneRequired || live('audio'));
    function clearIdentity() {
        ticket.value = '';
        identityDeadline = 0;
        text('identityState', faceReady ? 'Not verified' : 'Unavailable');
    }
    function entryOpen() {
        return !terminal && released && (reusable ||
            (Number.isFinite(startsAt) && Number.isFinite(latestAt) && now() >= startsAt && now() <= latestAt));
    }
    function update() {
        if (disposed || submitting) return;
        const freshServer = navigator.onLine && performance.now() - serverConfirmedAt < 25000;
        if (!freshServer) text('connectionState', navigator.onLine ? 'Checking connection…' : 'Offline', 'warning');
        const freshIdentity = !!ticket.value && performance.now() < identityDeadline;
        if (ticket.value && !freshIdentity) {
            clearIdentity();
            text('identityState', 'Verification expired', 'warning');
            text('checkMessage', microphoneProblem || 'Verification expired. Check your face again before starting.', 'warning');
        }
        const open = entryOpen();
        text('entryState', terminal ? 'This exam is no longer available.' : !released ? 'Waiting for your teacher to open entry.' :
            open ? 'Entry open' : Number.isFinite(startsAt) && now() < startsAt ? 'Entry opens at the scheduled start time.' : 'The entry window has closed.', open ? 'success' : 'warning');
        check.disabled = busy || !supported || !fullscreenSupported || !faceReady || terminal ||
            !Number.isInteger(frameCount) || frameCount < 1 || frameCount > 30;
        start.disabled = busy || !open || !freshServer || !devicesReady() || !freshIdentity || !agreement.checked || !fullscreenSupported;
        const seconds = Math.max(0, Math.ceil((identityDeadline - performance.now()) / 1000));
        text('startHint', busy ? 'Please wait for the check to finish.' : !open ? 'You can start only while exam entry is open.' :
            !freshServer ? 'Waiting for a connection to the exam server.' : !devicesReady() ? 'Complete the device and identity check first.' :
                !freshIdentity ? 'Verify your face before starting.' : !agreement.checked ? 'Read and confirm the instructions to continue.' :
                    `Ready. Start within ${seconds} seconds, or verify your face again.`, start.disabled ? '' : 'success');
    }
    function stopMedia() {
        closeAudio();
        mediaGeneration++;
        const oldStream = stream;
        stream = null;
        oldStream?.getTracks().forEach(track => track.stop());
        video.srcObject = null;
        video.hidden = true;
        el('cameraPlaceholder').hidden = false;
        el('cameraIndicator').hidden = true;
        deviceDeadline = 0;
        clearIdentity();
    }
    function deviceLost() {
        if (disposed || submitting) return;
        if (!live('video')) { currentAbort?.abort(); clearIdentity(); }
        text('cameraState', live('video') ? 'Connected' : 'Disconnected', live('video') ? 'success' : 'error');
        text('microphoneState', live('audio') ? 'Connected' : 'Disconnected', live('audio') ? 'success' : 'error');
        el('cameraIndicator').hidden = !live('video');
        text('checkMessage', !live('video') ? 'The camera was interrupted. Reconnect it and verify your face again.' : !live('audio') && microphoneRequired ? 'The microphone is not sending audio. Check microphone access and retry. Your camera is still connected.' : 'Devices connected. Complete face verification to continue.', 'warning');
        update();
    }
    function failureMessage(error) {
        if (error.name === 'NotAllowedError' || error.name === 'SecurityError') return 'Camera or microphone permission was denied. Allow access in site settings, then retry.';
        if (error.name === 'NotFoundError') return 'A required camera or microphone was not found. Connect your device, then retry.';
        if (error.name === 'NotReadableError') return 'Your device is busy or unavailable. Close other apps using it, then retry.';
        if (error.name === 'TimeoutError') return 'Identity verification took too long. Your microphone may still be connected. Retry or contact your teacher.';
        if (error.name === 'AbortError') return 'The check was interrupted or took too long. Please retry.';
        return error.message || 'The check could not finish. Please retry.';
    }
    async function connectMedia() {
        // Acquire independently: an unavailable microphone must not close a working camera.
        if (!stream) stream = new MediaStream();
        const generation = mediaGeneration;
        const current = () => generation === mediaGeneration && !disposed && !document.hidden;

        // Adds a freshly captured track to the exam stream and wires its listeners.
        function attach(track) {
            stream.addTrack(track);
            const choice = selectors[track.kind === 'video' ? 'videoinput' : 'audioinput'];
            const actual = track.getSettings?.().deviceId;
            if (actual && [...choice.select.options].some(o => o.value === actual)) choice.select.value = actual;
            try { if (actual && !choice.panel.hidden) sessionStorage.setItem(choice.key, actual); else sessionStorage.removeItem(choice.key); } catch { }
            track.addEventListener('ended', deviceLost);
            track.addEventListener('mute', deviceLost);
            track.addEventListener('unmute', () => {
                if (!current()) return;
                if (track.kind === 'audio') {
                    microphoneProblem = '';
                    micHelp.textContent = 'Microphone connected. You can complete the identity check.';
                    text('checkMessage', 'Microphone recovered. Complete face verification, then start your exam.');
                }
                updateDeviceLabels(); update();
            });
        }

        async function acquire(kind) {
            if (live(kind)) return;
            stream.getTracks().filter(t => t.kind === kind).forEach(t => { stream.removeTrack(t); t.stop(); });
            let expired = false, timeout;
            const constraints = kind === 'video'
                ? { video: !selectors.videoinput.panel.hidden && selectors.videoinput.select.value ? { deviceId: { ideal: selectors.videoinput.select.value } } : (window.MediaCapabilities?.constraints?.('video') || { width: { ideal: 640 }, height: { ideal: 480 } }), audio: false }
                : { video: false, audio: !selectors.audioinput.panel.hidden && micSelect.value ? { deviceId: { ideal: micSelect.value } } : true };
            const pending = navigator.mediaDevices.getUserMedia(constraints).catch(error => {
                if (['OverconstrainedError', 'NotFoundError'].includes(error.name) && current() && !expired) {
                    const choice = selectors[kind === 'video' ? 'videoinput' : 'audioinput'];
                    try { sessionStorage.removeItem(choice.key); } catch { }
                    return navigator.mediaDevices.getUserMedia(kind === 'video' ? { video: true, audio: false } : { video: false, audio: true });
                }
                throw error;
            }).then(media => {
                if (expired || !current()) { media.getTracks().forEach(t => t.stop()); throw new DOMException('Interrupted', 'AbortError'); }
                media.getTracks().forEach(attach);
            });
            try {
                await Promise.race([pending, new Promise((_, reject) => {
                    timeout = setTimeout(() => { expired = true; reject(new Error(kind === 'video' ? 'Allow camera access, then retry.' : 'Allow microphone access, then retry.')); }, 15000);
                })]);
            } finally { clearTimeout(timeout); }
        }

        // Wait for a live unmuted track; do not require speech or a minimum sound level.
        async function waitAudio(ms) {
            const limit = performance.now() + ms;
            while (current() && performance.now() < limit) {
                if (live('audio')) return true;
                if (audioContext?.state === 'suspended') audioContext.resume().catch(() => { });
                const trackOk = stream.getAudioTracks().some(t => t.readyState === 'live' && t.enabled);
                if (trackOk && detectSignal()) { audioFlowing = true; } // Signal is diagnostic; a muted track is not ready.
                await sleep(100);
            }
            return live('audio');
        }

        // Opens one specific microphone and reports whether it really delivers audio.
        async function tryMicrophone(deviceId) {
            stream.getAudioTracks().forEach(t => { stream.removeTrack(t); t.stop(); });
            closeAudio();
            primeAudio();
            let media;
            try {
                media = await navigator.mediaDevices.getUserMedia({ video: false, audio: { deviceId: { exact: deviceId } } });
            } catch { return false; }
            if (!current()) { media.getTracks().forEach(t => t.stop()); throw new DOMException('Interrupted', 'AbortError'); }
            media.getTracks().forEach(attach);
            consumeAudio();
            return waitAudio(3000);
        }

        // A muted source is unavailable; silence from a healthy live track is valid. Try distinct physical inputs only.
        async function recoverMicrophone() {
            let devices = [];
            try { devices = (await navigator.mediaDevices.enumerateDevices()).filter(d => d.kind === 'audioinput' && d.deviceId); } catch { }
            const alias = d => ['default', 'communications'].includes(d.deviceId) ? 1 : 0;
            const seen = new Set();
            const candidates = devices.filter(d => !['default', 'communications'].includes(d.deviceId) && !seen.has(d.groupId || d.deviceId) && seen.add(d.groupId || d.deviceId)).sort((a, b) => alias(a) - alias(b));
            const currentId = stream.getAudioTracks()[0]?.getSettings?.().deviceId;
            if (candidates.length < 2) return false;
            for (const device of candidates.filter(d => d.deviceId !== currentId)) {
                if (!current()) throw new DOMException('Interrupted', 'AbortError');
                micHelp.textContent = 'Trying ' + (device.label || 'another microphone') + '…';
                if (await tryMicrophone(device.deviceId)) {
                    const id = device.deviceId;
                    if ([...micSelect.options].some(o => o.value === id)) micSelect.value = id;
                    try { sessionStorage.setItem(selectors.audioinput.key, id); } catch { }
                    return true;
                }
            }
            return false;
        }

        await acquire('video');
        if (!current()) throw new DOMException('Interrupted', 'AbortError');
        video.srcObject = stream; video.hidden = false; el('cameraPlaceholder').hidden = true;
        let previewTimeout;
        try { await Promise.race([video.play(), new Promise((_, reject) => { previewTimeout = setTimeout(() => reject(new Error('Camera preview did not start. Please retry.')), 8000); })]); }
        finally { clearTimeout(previewTimeout); }
        const deadline = performance.now() + 8000;
        while ((!video.videoWidth || !video.videoHeight || video.readyState < 2 || !live('video')) && performance.now() < deadline) {
            await sleep(100);
            if (!current()) throw new DOMException('Interrupted', 'AbortError');
        }
        if (!live('video') || !video.videoWidth || video.readyState < 2) throw new Error('The camera is connected but is not delivering video. Close other camera apps and retry.');
        updateDeviceLabels();
        microphoneProblem = '';
        if (microphoneRequired && !live('audio')) {
            text('microphoneState', 'Connecting…'); micSelect.disabled = true;
            try {
                await acquire('audio');
                consumeAudio();
                if (!(await waitAudio(3000))) {
                    // Default input is silent. Try the other microphones automatically.
                    if (!(await recoverMicrophone()))
                        throw new Error('No microphone is sending audio. In Windows open Settings > System > Sound > Input, select your real microphone and speak: the volume bar must move. Also check Settings > Privacy & security > Microphone > "Let desktop apps access your microphone" is On. Then retry.');
                }
            } catch (error) { microphoneProblem = error.name === 'NotAllowedError' ? 'Microphone permission is blocked. Allow Microphone for this site in Chrome, then retry.' : error.name === 'NotReadableError' ? 'Microphone cannot start. Close other recording apps or choose another input below.' : error.name === 'OverconstrainedError' ? 'The microphone could not be opened, including the system default. Check Windows microphone access and reconnect the device.' : failureMessage(error); }
            finally { micSelect.disabled = false; await listMicrophones(); micHelp.textContent = microphoneProblem || 'Microphone connected.'; }
        }
        if (!current()) throw new DOMException('Interrupted', 'AbortError');
        updateDeviceLabels();
        deviceDeadline = performance.now() + 120000;
    }
    function updateDeviceLabels() {
        text('cameraState', live('video') ? 'Connected' : 'Disconnected', live('video') ? 'success' : 'error');
        text('microphoneState', !microphoneRequired ? 'Not required' : live('audio') ? 'Connected' : 'Microphone check required', !microphoneRequired || live('audio') ? 'success' : 'warning');
        el('cameraIndicator').hidden = !live('video');
    }
    async function verify() {
        const body = new FormData();
        body.append('ExamId', config.examId);
        body.append('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value);
        const canvas = document.createElement('canvas');
        canvas.width = Math.min(960, video.videoWidth);
        canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
        const context = canvas.getContext('2d');
        if (!context || !canvas.width || !canvas.height) throw new Error('Camera images are unavailable. Please retry.');
        const controller = new AbortController();
        currentAbort = controller;
        const timeout = setTimeout(() => controller.abort(new DOMException('Identity service timed out', 'TimeoutError')), 45000);
        try {
            for (let i = 0; i < frameCount; i++) {
                if (disposed || controller.signal.aborted || !live('video')) throw new DOMException('Interrupted', 'AbortError');
                context.drawImage(video, 0, 0, canvas.width, canvas.height);
                const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', .94));
                if (!blob) throw new Error('Could not capture a camera image. Please retry.');
                body.append('Frames', blob, `check-${i}.jpg`);
                if (i < frameCount - 1) await sleep(280);
            }
            const response = await fetch(config.verifyUrl, { method: 'POST', body, headers: { Accept: 'application/json' }, signal: controller.signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
            if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Your session may have expired. Reload this page and sign in again if requested.');
            const result = await response.json();
            if (!response.ok || result.verified !== true || typeof result.ticket !== 'string' || !result.ticket.trim()) {
                // Do not expose service internals, scores or exception messages to candidates.
                const message = response.status === 401 ? 'Your session expired. Reload and sign in again.' :
                    response.status === 428 ? 'Return to examinations and accept the exam instructions first.' :
                        response.status === 429 ? 'Too many checks. Wait a moment before retrying.' :
                            response.status >= 500 ? 'Identity verification is temporarily unavailable. Retry shortly or contact your teacher.' :
                                'Identity could not be confirmed. Keep the registered candidate alone and clearly visible, then retry. Contact your teacher if this continues.';
                const reasons = {
                    poor_lighting: ['Quality check failed', 'Your face is too dark or overexposed. Face the available light and retry.'],
                    blurry_face: ['Quality check failed', 'The face image is blurred. Hold still, clean the lens and retry.'],
                    low_face_quality: ['Quality check failed', 'Facial details are unclear. Hold still and face the camera.'],
                    low_detection_confidence: ['Face detection uncertain', 'The face could not be located reliably. Adjust the camera or lighting.'],
                    face_too_small: ['Face too far away', 'Move closer until your face is clearly visible.'],
                    no_face: ['Face not detected', 'Face the camera with your whole face visible.'],
                    multiple_faces: ['Multiple faces detected', 'Only the registered candidate should be visible.'],
                    presentation_framing: ['Face too close for live-face check', 'Move slightly farther from the camera so your whole head and shoulders are visible, with space around your face. Hold still and retry.'],
                    presentation_unconfirmed: ['Live-face check inconclusive', 'The camera-based live-face check is inconclusive. Keep your head and shoulders visible, avoid glare and retry. If this repeats, contact your supervisor for an alternative identity check. This is not an identity mismatch.'],
                    identity_mismatch: ['Identity did not match', 'Usable face samples did not match your registered photos. Retry with your face clearly visible or ask your teacher to review the registered photos.'],
                    borderline_score: ['Identity match inconclusive', 'The comparison was not strong enough to verify identity. Retry; this is not a confirmed mismatch.'],
                    insufficient_clear_frames: ['Not enough clear frames', 'Hold still while several fresh camera images are captured.'],
                    template_not_found: ['Registration reference unavailable', 'Ask your teacher to check your registered face photos.']
                };
                const reason = response.status >= 500 ? ['Verification service unavailable', message] : reasons[result.reasonCode];
                throw Object.assign(new Error(reason ? reason[1] : message), { identityLabel: reason?.[0] || 'Verification unsuccessful' });
            }
            if (disposed || controller.signal.aborted || !live('video')) throw new DOMException('Interrupted', 'AbortError');
            ticket.value = result.ticket;
            // Start rejects verification older than 30 seconds. Leave a network margin.
            identityDeadline = performance.now() + 28000;
            deviceDeadline = performance.now() + 120000;
            text('identityState', 'Verified', 'success');
            text('checkMessage', devicesReady() ? 'Identity verified. Confirm the instructions, then select Start exam.' : `Identity verified. ${microphoneProblem || 'Reconnect your microphone and run the check again before starting.'}`, devicesReady() ? 'success' : 'warning');
        } finally { clearTimeout(timeout); currentAbort = null; }
    }
    check.addEventListener('click', async () => {
        if (check.disabled || busy || disposed) return;
        if (microphoneRequired) primeAudio();
        busy = true;
        clearIdentity();
        check.textContent = 'Checking…';
        update();
        try {
            text('checkMessage', 'Allow camera and any required microphone access.');
            if (!devicesReady()) await connectMedia();
            if (disposed) return;
            text('identityState', 'Checking…');
            text('checkMessage', 'Look at the camera and keep your face visible.');
            await verify();
            await refreshStatus();
        } catch (error) {
            if (!disposed) {
                clearIdentity();
                text('identityState', error.identityLabel || (error.name === 'TimeoutError' ? 'Verification timed out' : 'Not verified'), 'error');
                text('checkMessage', failureMessage(error), 'error');
                updateDeviceLabels();
            }
        } finally { busy = false; check.textContent = initialLabel; update(); }
    });
    async function refreshStatus() {
        if (disposed || submitting || pollBusy || document.hidden) return;
        pollBusy = true;
        const controller = new AbortController();
        pollAbort = controller;
        const began = performance.now();
        const timeout = setTimeout(() => controller.abort(), 7000);
        try {
            const response = await fetch(config.statusUrl, { signal: controller.signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Unavailable');
            const data = await response.json();
            if (typeof data.released !== 'boolean') throw new Error('Invalid response');
            released = data.released;
            if (typeof data.terminal === 'boolean') terminal = data.terminal;
            if (typeof data.reusable === 'boolean') reusable = data.reusable;
            if (!reusable) {
                const begin = Date.parse(data.start), end = Date.parse(data.latest);
                if (!Number.isFinite(begin) || !Number.isFinite(end)) throw new Error('Invalid schedule');
                startsAt = begin; latestAt = end;
            }
            const serverTime = Date.parse(data.serverNow);
            if (Number.isFinite(serverTime)) { serverAnchor = serverTime + (performance.now() - began) / 2; timeAnchor = performance.now(); }
            serverConfirmedAt = performance.now();
            text('connectionState', 'Connected', 'success');
        } catch {
            serverConfirmedAt = -Infinity;
            text('connectionState', navigator.onLine ? 'Cannot reach exam server' : 'Offline', 'warning');
        } finally { clearTimeout(timeout); pollAbort = null; pollBusy = false; update(); }
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        update();
        if (disposed || submitting || start.disabled || !form.reportValidity()) return;
        submitting = true;
        start.disabled = true;
        check.disabled = true;
        start.textContent = 'Choose your entire screen…';
        let display;
        try {
            display = await ExamDisplayCapture.requestDisplay();
            if (disposed || !devicesReady()) throw new Error('The camera or required microphone is unavailable. Run the device check and retry.');
            if (!ticket.value || performance.now() >= identityDeadline)
                throw new Error('Your identity check expired while choosing the display. Verify again before starting.');
            start.textContent = 'Opening exam…';
            const body = new FormData(form);
            body.append('displaySurface', 'monitor');
            const response = await fetch(form.action, {
                method: 'POST', body, headers: { Accept: 'application/json' }, credentials: 'same-origin'
            });
            let sessionUrl;
            if (response.redirected) {
                const destination = new URL(response.url);
                if (destination.origin !== location.origin || destination.pathname !== '/Student/Exam/Session') {
                    location.assign(destination.href);
                    return;
                }
                sessionUrl = destination.href;
            } else {
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json'))
                    throw new Error('The exam could not start. Refresh the device check and try again.');
                sessionUrl = (await response.json()).sessionUrl;
            }
            ExamDisplayCapture.openSession(sessionUrl, stream, display);
            display = null;
            stream = null;
            disposed = true;
            currentAbort?.abort();
            pollAbort?.abort();
            clearInterval(tick);
            clearInterval(polling);
            closeAudio();
        } catch (error) {
            text('checkMessage', error.message, 'error');
        } finally {
            ExamDisplayCapture.stop(display);
            submitting = false;
            start.textContent = 'Start exam';
            if (!disposed) update();
        }
    });
    agreement.addEventListener('change', update);
    window.addEventListener('offline', () => { serverConfirmedAt = -Infinity; text('connectionState', 'Offline', 'warning'); update(); });
    window.addEventListener('online', refreshStatus);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden && !submitting) {
            currentAbort?.abort();
            stopMedia();
            text('cameraState', 'Check required', 'warning');
            text('microphoneState', 'Check required', 'warning');
            text('checkMessage', 'Run the check again after returning to this page.');
        } else refreshStatus();
        update();
    });
    const tick = setInterval(() => {
        if (!busy && !submitting && deviceDeadline && performance.now() >= deviceDeadline) {
            stopMedia();
            text('cameraState', 'Check required', 'warning');
            text('microphoneState', 'Check required', 'warning');
            text('checkMessage', 'Camera paused while idle. Run the check when you are ready.');
        }
        update();
    }, 1000);
    const polling = setInterval(refreshStatus, 10000);
    window.addEventListener('pagehide', () => {
        disposed = true;
        clearInterval(tick); clearInterval(polling);
        currentAbort?.abort(); pollAbort?.abort(); stopMedia();
    });
    window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });
    agreement.checked = false;
    clearIdentity();
    text('fullscreenState', fullscreenSupported ? 'Available' : 'Not supported', fullscreenSupported ? 'success' : 'error');
    text('checkMessage', !supported ? 'Use a supported browser over HTTPS with camera access enabled.' :
        !fullscreenSupported ? 'This exam requires a browser that supports fullscreen. Use a desktop browser.' :
            !faceReady ? 'Identity verification is unavailable. Reload shortly or contact your teacher.' :
                'Select the check button when you are ready. Your exam will not start automatically.', !supported || !fullscreenSupported || !faceReady ? 'warning' : '');
    listMicrophones();
    update();
    refreshStatus();
})();

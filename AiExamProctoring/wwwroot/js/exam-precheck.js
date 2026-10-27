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
    const faceReady = config.faceReady === 'true';
    const frameCount = Number(config.frames);
    const supported = window.isSecureContext && !!navigator.mediaDevices?.getUserMedia &&
        typeof fetch === 'function' && typeof AbortController === 'function';
    const fullscreenSupported = !fullscreenRequired || (document.fullscreenEnabled === true &&
        typeof document.documentElement.requestFullscreen === 'function');
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
    const live = kind => !!stream?.getTracks().some(track => track.kind === kind &&
        track.readyState === 'live' && track.enabled && !track.muted);
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
        const freshIdentity = !!ticket.value && performance.now() < identityDeadline;
        if (ticket.value && !freshIdentity) {
            clearIdentity();
            text('checkMessage', 'Verification expired. Check your face again before starting.', 'warning');
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
        currentAbort?.abort();
        clearIdentity();
        text('cameraState', live('video') ? 'Connected' : 'Disconnected', live('video') ? 'success' : 'error');
        text('microphoneState', live('audio') ? 'Connected' : 'Disconnected', live('audio') ? 'success' : 'error');
        el('cameraIndicator').hidden = !live('video');
        text('checkMessage', 'A device was interrupted. Reconnect it and run the check again.', 'warning');
        update();
    }
    function failureMessage(error) {
        if (error.name === 'NotAllowedError' || error.name === 'SecurityError') return 'Camera or microphone permission was denied. Allow access in site settings, then retry.';
        if (error.name === 'NotFoundError') return 'A required camera or microphone was not found. Connect your device, then retry.';
        if (error.name === 'NotReadableError') return 'Your device is busy or unavailable. Close other apps using it, then retry.';
        if (error.name === 'AbortError') return 'The check was interrupted or took too long. Please retry.';
        return error.message || 'The check could not finish. Please retry.';
    }
    async function connectMedia() {
        stopMedia();
        let expired = false, timeout;
        const pending = navigator.mediaDevices.getUserMedia({
            video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' },
            audio: microphoneRequired
        }).then(camera => {
            if (expired || disposed || document.hidden) { camera.getTracks().forEach(track => track.stop()); throw new Error('Camera request expired. Retry the check.'); }
            return camera;
        });
        try {
            stream = await Promise.race([pending, new Promise((_, reject) => {
                timeout = setTimeout(() => { expired = true; reject(new Error('Allow the requested device permissions, then retry the check.')); }, 20000);
            })]);
        } finally { clearTimeout(timeout); }
        if (disposed) { stopMedia(); return; }
        video.srcObject = stream;
        video.hidden = false;
        el('cameraPlaceholder').hidden = true;
        // Bound video startup too: a connected device can still fail to deliver frames.
        await Promise.race([video.play(), new Promise((_, reject) => setTimeout(() => reject(new Error('Camera preview did not start. Please retry.')), 8000))]);
        const deadline = performance.now() + 8000;
        while ((!video.videoWidth || !video.videoHeight || video.readyState < 2 || !devicesReady()) && performance.now() < deadline) {
            await new Promise(resolve => setTimeout(resolve, 100));
            if (disposed || document.hidden) { stopMedia(); return; }
        }
        if (!video.videoWidth || !video.videoHeight || video.readyState < 2 || !devicesReady()) throw new Error('A required device is not sending data. Reconnect it and retry.');
        stream.getTracks().forEach(track => { track.addEventListener('ended', deviceLost); track.addEventListener('mute', deviceLost); });
        el('cameraIndicator').hidden = false;
        text('cameraState', 'Connected', 'success');
        text('microphoneState', 'Connected', 'success');
        deviceDeadline = performance.now() + 120000;
    }
    async function verify() {
        const body = new FormData();
        body.append('ExamId', config.examId);
        body.append('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value);
        const canvas = document.createElement('canvas');
        canvas.width = Math.min(640, video.videoWidth);
        canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
        const context = canvas.getContext('2d');
        if (!context || !canvas.width || !canvas.height) throw new Error('Camera images are unavailable. Please retry.');
        const controller = new AbortController();
        currentAbort = controller;
        const timeout = setTimeout(() => controller.abort(), 45000);
        try {
            for (let i = 0; i < frameCount; i++) {
                if (disposed || controller.signal.aborted || !devicesReady()) throw new DOMException('Interrupted', 'AbortError');
                context.drawImage(video, 0, 0, canvas.width, canvas.height);
                const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', .88));
                if (!blob) throw new Error('Could not capture a camera image. Please retry.');
                body.append('Frames', blob, `check-${i}.jpg`);
                if (i < frameCount - 1) await new Promise(resolve => setTimeout(resolve, 280));
            }
            const response = await fetch(config.verifyUrl, { method: 'POST', body, signal: controller.signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
            if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Your session may have expired. Reload this page and sign in again if requested.');
            const result = await response.json();
            if (!response.ok || result.verified !== true || typeof result.ticket !== 'string' || !result.ticket.trim()) {
                // Do not expose service internals, scores or exception messages to candidates.
                const message = response.status === 401 ? 'Your session expired. Reload and sign in again.' :
                    response.status === 428 ? 'Return to examinations and accept the exam instructions first.' :
                        response.status === 429 ? 'Too many checks. Wait a moment before retrying.' :
                            response.status >= 500 ? 'Identity verification is temporarily unavailable. Retry shortly or contact your teacher.' :
                                'Identity could not be confirmed. Keep the registered candidate alone and clearly visible, then retry. Contact your teacher if this continues.';
                throw new Error(message);
            }
            if (disposed || controller.signal.aborted || !devicesReady()) throw new DOMException('Interrupted', 'AbortError');
            ticket.value = result.ticket;
            // Start rejects verification older than 30 seconds. Leave a network margin.
            identityDeadline = performance.now() + 20000;
            deviceDeadline = performance.now() + 120000;
            text('identityState', 'Verified', 'success');
            text('checkMessage', 'Identity verified. Confirm the instructions, then select Start exam.', 'success');
        } finally { clearTimeout(timeout); currentAbort = null; }
    }
    check.addEventListener('click', async () => {
        if (check.disabled || busy || disposed) return;
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
                text('checkMessage', failureMessage(error), 'error');
                if (!devicesReady()) { stopMedia(); text('cameraState', 'Check required', 'warning'); text('microphoneState', 'Check required', 'warning'); }
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
    form.addEventListener('submit', event => {
        event.preventDefault();
        update();
        if (disposed || submitting || start.disabled || !form.reportValidity()) return;
        // The server validates student, exam access, schedule and the signed ticket again.
        submitting = true;
        start.disabled = true;
        check.disabled = true;
        start.textContent = 'Opening exam…';
        currentAbort?.abort();
        pollAbort?.abort();
        stream?.getTracks().forEach(track => track.stop());
        HTMLFormElement.prototype.submit.call(form);
    });
    agreement.addEventListener('change', update);
    window.addEventListener('offline', () => { serverConfirmedAt = -Infinity; text('connectionState', 'Offline', 'warning'); update(); });
    window.addEventListener('online', refreshStatus);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
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
    update();
    refreshStatus();
})();

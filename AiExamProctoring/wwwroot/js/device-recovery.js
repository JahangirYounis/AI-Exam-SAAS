// Technical holds only. Browser device observations are not a cheating verdict.
// A Teacher/disciplinary block is never released by this page.
(() => {
    'use strict';
    const root = document.getElementById('technical-device-recovery');
    if (!root) return;
    const attemptId = Number(root.dataset.attempt);
    const status = root.querySelector('[data-device-status]');
    const start = root.querySelector('[data-device-retry]');
    const preview = root.querySelector('video');
    const token = root.querySelector('[name="__RequestVerificationToken"]')?.value;
    const setStatus = value => { status.textContent = value; };
    const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
    let details = null, busy = false;

    async function json(response) {
        let data; try { data = await response.json(); } catch { throw new Error('Exam server returned an invalid response.'); }
        if (!response.ok) throw new Error(data.message || `Exam server error ${response.status}`);
        return data;
    }
    async function loadStatus() {
        const response = await fetch(`/Student/Exam/TechnicalHoldStatus?attemptId=${encodeURIComponent(attemptId)}`, {
            credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
        });
        const data = await json(response);
        if (!data.technical) { root.hidden = true; return; }
        root.hidden = false; details = data;
        const required = [data.cameraAffected ? 'camera' : null,
            data.microphoneAffected ? 'microphone' : null].filter(Boolean).join(' and ') || 'media';
        setStatus(`Technical pause: ${data.issue.replaceAll('_', ' ')}. Restore ${required}, then select Check devices. This pause adds no cheating strike.`);
        document.querySelector('.lock-badge')?.replaceChildren(document.createTextNode('TECHNICAL DEVICE HOLD — NOT CHEATING'));
        const heading = document.getElementById('lock-summary-title');
        if (heading) heading.textContent = 'Exam paused due to a device problem';
        const paragraph = document.querySelector('.lock-summary-top p');
        if (paragraph) paragraph.textContent = 'Your exam time is frozen. Recover the required devices and pass fresh checks to resume.';
    }
    async function verify() {
        if (busy || !details || !token) return;
        busy = true; start.disabled = true;
        let media, audioContext;
        try {
            setStatus('Requesting fresh camera and microphone permissions…');
            // Ask for both, even if only one originally failed: both must be
            // usable before examination monitoring restarts.
            media = await navigator.mediaDevices.getUserMedia({
                video: window.MediaCapabilities?.constraints('video') || true,
                audio: window.MediaCapabilities?.constraints('audio') || true
            });
            preview.srcObject = media;
            await preview.play();
            const camera = media.getVideoTracks()[0], microphone = media.getAudioTracks()[0];
            if (!camera || !microphone) throw new Error('Both camera and microphone are required.');
            audioContext = new AudioContext();
            const audioSource = audioContext.createMediaStreamSource(new MediaStream([microphone]));
            const analyzer = audioContext.createAnalyser();
            analyzer.fftSize = 512;
            audioSource.connect(analyzer); // No audio output: avoid feedback.
            await audioContext.resume();
            let lastFingerprint = null, lastFrameTime = -1;
            for (let index = 0; index < 5; index++) {
                await pause(1250);
                const cameraTrackLive = camera.readyState === 'live' && camera.enabled && !camera.muted;
                const microphoneTrackLive = microphone.readyState === 'live' && microphone.enabled && !microphone.muted;
                const frame = window.MediaCapabilities?.captureState(preview);
                const cameraFreshFrame = cameraTrackLive && frame !== null &&
                    frame.brightness >= 12 && frame.fingerprint !== lastFingerprint &&
                    preview.currentTime > lastFrameTime;
                // Silence is valid during an exam. This check validates an active
                // AudioContext, microphone track and readable PCM buffer, not loudness.
                const audio = new Float32Array(analyzer.fftSize);
                analyzer.getFloatTimeDomainData(audio);
                const microphoneAudioFramesAvailable = microphoneTrackLive && audioContext.state === 'running' &&
                    audio.length === analyzer.fftSize && audio.every(Number.isFinite);
                lastFingerprint = frame?.fingerprint || lastFingerprint;
                lastFrameTime = preview.currentTime;
                const request = new FormData();
                request.append('__RequestVerificationToken', token);
                request.append('AttemptId', String(attemptId));
                request.append('SessionAttemptId', String(details.sessionAttemptId));
                request.append('CapturedAtUtc', new Date().toISOString());
                request.append('CameraTrackLive', String(cameraTrackLive));
                request.append('MicrophoneTrackLive', String(microphoneTrackLive));
                request.append('CameraFreshFrame', String(cameraFreshFrame));
                request.append('MicrophoneAudioFramesAvailable', String(microphoneAudioFramesAvailable));
                request.append('CameraWidth', String(frame?.width || 0));
                request.append('CameraHeight', String(frame?.height || 0));
                request.append('CameraFrameFingerprint', frame?.fingerprint || '');
                const response = await fetch('/Student/Exam/DeviceReadiness', {
                    method: 'POST', credentials: 'same-origin', body: request,
                    headers: { Accept: 'application/json' }
                });
                const outcome = await json(response);
                setStatus(outcome.message);
                if (!outcome.ok) break;
                if (outcome.recovered) {
                    setStatus('Technical hold released. Reopening the exam with the normal identity check…');
                    window.location.assign(outcome.resumeUrl || `/Student/Exam/Resume?attemptId=${attemptId}`);
                    return;
                }
            }
            setStatus('Device checks did not all pass. Improve lighting, unfreeze the video and retry. Your exam remains safely paused.');
        } catch (error) {
            setStatus(`Device readiness could not be verified: ${error.message}. Check browser permissions, camera and microphone.`);
        } finally {
            media?.getTracks().forEach(track => track.stop());
            if (audioContext) await audioContext.close().catch(() => {});
            preview.srcObject = null;
            busy = false; start.disabled = false;
        }
    }
    start.addEventListener('click', verify);
    loadStatus().catch(error => { root.hidden = false; setStatus(`Unable to check technical hold: ${error.message}`); start.disabled = true; });
})();

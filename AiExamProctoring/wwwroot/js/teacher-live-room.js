
(() => {
    const root = document.getElementById('live-room');
    if (!root) return;
    const examId = Number(root.dataset.exam), viewer = crypto.randomUUID().replaceAll('-', '');
    let targetAttempt = Number(new URLSearchParams(location.search).get('attemptId'));
    const targetIncident = Number(new URLSearchParams(location.search).get('incidentId'));
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const grid = document.getElementById('camera-grid'), status = document.getElementById('room-status');
    const tiles = new Map();
    const liveAlertKinds = new Set([
        'FaceNotDetected', 'MultipleFaces', 'AdditionalPersonObservation', 'IdentityMismatch',
        'HeadAway', 'GazeAway', 'LookAway', 'LookingAwaySpeaking', 'ElectronicDeviceDetected',
        'ObjectObservation', 'AudioDetected', 'TalkingDetected', 'AnswerPromptSpeech',
        'TabSwitch', 'ExamPageHidden', 'FullScreenExit', 'ExamBlocked', 'ReviewRequested',
        'CameraAccessLost', 'MicrophoneAccessLost', 'CameraMonitoringInterrupted', 'AiMonitoringUnavailable', 'ScreenShareLost', 'ScreenCaptureUnavailable'
    ]);
    const speechPlayer = document.getElementById('speech-alert-player');
    const speechEnable = document.getElementById('enable-speech-playback');
    const speechReplay = document.getElementById('replay-speech-alert');
    const speechStatus = document.getElementById('speech-playback-status');
    const playedSpeechIds = new Set();
    let playbackEnabled = false, currentSpeechId = 0, currentSpeechHasVideo = false;
    let page = 1, pages = 1, configRequest, stopped = false, pauseAttempt;
    function cameraConfiguration() {
        return configRequest ??= LiveProctoring.configuration().catch(error => {
            configRequest = null;
            throw error;
        });
    }

    // Browsers may reject programmatic unmuted playback: teacher must first opt in.
    // Only authorized, recent, committed exam-related speech evidence is eligible.
    function offerSpeechPlayback(row) {
        const entry = row.speechAlert;
        if (!entry || !Number.isInteger(entry.logId) || entry.logId <= 0 || playedSpeechIds.has(entry.logId)) return;
        const created = new Date(entry.createdDate).getTime();
        if (!Number.isFinite(created) || Math.abs(Date.now() - created) > 120000) return;
        playedSpeechIds.add(entry.logId);
        if (playedSpeechIds.size > 250) playedSpeechIds.delete(playedSpeechIds.values().next().value);
        currentSpeechId = entry.logId;
        currentSpeechHasVideo = !!entry.hasVideo;
        speechReplay.disabled = false;
        speechStatus.textContent = `Exam-related speech evidence received for ${row.name}. ` +
            (playbackEnabled ? 'Opening authorized audio clip…' : 'Enable playback to hear the recording.');
        if (playbackEnabled) playSpeech(entry.logId);
    }
    async function playSpeech(logId) {
        if (!playbackEnabled || !Number.isInteger(logId) || logId <= 0) return;
        speechPlayer.pause();
        speechPlayer.src = `/Teacher/Monitoring/Evidence?id=${logId}&kind=${currentSpeechHasVideo ? 'video' : 'recording'}`;
        speechPlayer.load();
        try {
            await speechPlayer.play();
            speechStatus.textContent = `Playing protected exam-related speech evidence #${logId}.`;
        } catch {
            speechStatus.textContent = 'Browser blocked automatic sound or the clip is unavailable. Click Replay latest speech evidence.';
        }
    }
    speechEnable?.addEventListener('click', () => {
        playbackEnabled = !playbackEnabled;
        speechEnable.textContent = playbackEnabled ? 'Disable speech evidence playback' : 'Enable exam-related speech playback';
        speechStatus.textContent = playbackEnabled ? 'Audio enabled for new question-related incidents. Your browser may require manual Replay.' : 'Speech evidence playback is off.';
        if (!playbackEnabled) { speechPlayer.pause(); speechPlayer.removeAttribute('src'); speechPlayer.load(); }
    });
    speechReplay?.addEventListener('click', () => {
        if (!playbackEnabled) { speechStatus.textContent = 'Enable audio playback first.'; return; }
        playSpeech(currentSpeechId);
    });
    const send = (attemptId, kind, data = '') => LiveProctoring.post('/live-proctoring/signal', { attemptId, viewer, kind, data }, token);
    function remove(id) {
        const tile = tiles.get(id);
        tile?.peer?.pc.close(); tile?.element.remove(); tiles.delete(id);
        send(id, 'bye').catch(() => { });
    }
    function make(row) {
        const element = document.createElement('article'); element.className = 'camera-tile';
        element.innerHTML = '<div class="tile-video-wrap"><video class="tile-screen" autoplay muted playsinline aria-label="Shared screen"></video><video class="tile-webcam" autoplay muted playsinline aria-label="Webcam"></video><span class="tile-overlay-warning" role="alert" hidden></span></div><div class="tile-info"><div class="tw14-tile-title"><strong class="name"></strong><strong class="strikes"></strong></div><p class="connection small">Connecting live video…</p><div class="tw14-inline-actions"><button type="button" class="btn btn-outline-secondary btn-sm enlarge-screen" disabled>Enlarge screen</button><button type="button" class="btn btn-outline-secondary btn-sm enlarge-camera" disabled>Enlarge webcam</button></div><p class="tile-alert" role="status"></p><p class="tile-tab-alert small" role="status" hidden></p><p class="tile-fullscreen-alert small" role="status" hidden></p><div class="tw14-tile-actions"><a class="btn btn-outline-primary btn-sm tab-evidence" hidden>Open Evidence</a><a class="btn btn-outline-primary btn-sm fullscreen-evidence" hidden>Fullscreen evidence</a><a class="btn btn-outline-primary btn-sm review">Review evidence</a><button class="btn btn-outline-danger btn-sm pause">Pause attempt</button><a class="btn btn-outline-warning btn-sm review-requests" href="/Teacher/ReviewRequests" hidden>Review hold</a></div><details class="tw14-tile-more"><summary>More controls</summary><div class="tw14-inline-actions"><button type="button" class="btn btn-outline-success btn-sm acknowledge">Mark alert seen</button><button type="button" class="btn btn-outline-primary btn-sm verify">Recheck identity</button></div></details></div>';
        element.querySelector('.name').textContent = row.name;
        element.querySelector('.review').href = `/Teacher/Monitoring/ExamFolder?examId=${examId}&studentId=${encodeURIComponent(row.studentId)}&attemptId=${row.attemptId}`;
        const tile = { element, videos: { screen: element.querySelector('.tile-screen'), camera: element.querySelector('.tile-webcam') },
            streams: {}, availability: {}, row, seen: 0, watchAt: 0, created: Date.now() };
        for (const [role, video] of Object.entries(tile.videos)) {
            const enlarge = () => { if (video.srcObject) void video.requestFullscreen?.().catch(() => {}); };
            video.onclick = enlarge;
            element.querySelector(`.enlarge-${role}`).onclick = enlarge;
        }
        element.querySelector('.acknowledge').onclick = () => { tile.seen = tile.row.alert?.logId || 0; update(tile, tile.row); };
        element.querySelector('.verify').onclick = async () => {
            try { await window.ProctoringRealtime.connection.invoke('RequestIdentityVerification', row.attemptId); status.textContent = 'Fresh identity verification requested.'; }
            catch { status.textContent = 'Could not request verification. Check that the exam is active and retry.'; }
        };
        element.querySelector('.pause').onclick = () => {
            pauseAttempt = row.attemptId;
            document.getElementById('pause-name').textContent = row.name;
            document.getElementById('pause-reason').value = '';
            document.getElementById('pause-error').textContent = '';
            document.getElementById('pause-dialog').showModal();
        };
        grid.append(element); tiles.set(row.attemptId, tile); return tile;
    }
    function paintVideo(tile) {
        for (const [role, video] of Object.entries(tile.videos)) {
            const source = tile.availability[role] === false ? null : tile.streams[role] || null;
            if (video.srcObject !== source) {
                video.srcObject = source;
                if (source) void video.play().catch(() => {});
            }
            tile.element.querySelector(`.enlarge-${role}`).disabled = !source;
        }
        tile.element.classList.toggle('screen-live', !!tile.videos.screen.srcObject);
        if (tile.peer?.pc.connectionState === 'connected') {
            const screen = tile.videos.screen.srcObject ? 'Screen live' : 'Screen unavailable';
            const camera = tile.videos.camera.srcObject ? 'webcam live' : 'webcam unavailable';
            tile.element.querySelector('.connection').textContent = `${screen} · ${camera}`;
        }
    }
    function resetVideo(tile) {
        tile.streams = {};
        tile.availability = {};
        paintVideo(tile);
    }
    function update(tile, row) {
        // An HTTP roster started before a push must not undo a newer lock/resume.
        if ((row.version || 0) < (tile.row.version || 0)) return;
        tile.row = row;
        const latestId = Number(row.alert?.logId);
        tile.element.querySelector('.review').href =
            `/Teacher/Monitoring/ExamFolder?examId=${examId}&studentId=${encodeURIComponent(row.studentId)}&attemptId=${row.attemptId}` +
            (row.attemptId === targetAttempt && targetIncident > 0 ? `&incidentId=${targetIncident}` :
                Number.isInteger(latestId) && latestId > 0 ? `&incidentId=${latestId}` : '');
        const tab = row.tabObservation;
        const tabAlert = tile.element.querySelector('.tile-tab-alert');
        const tabEvidence = tile.element.querySelector('.tab-evidence');
        const linkToTab = tab || (row.attemptId === targetAttempt && targetIncident === latestId &&
            ['TabSwitch', 'ExamPageHidden'].includes(row.alert?.kind) ? { logId: targetIncident } : null);
        tile.element.classList.toggle('tab-observation-active', !!tab && !tab.ended && row.status === 'InProgress');
        tabAlert.hidden = !tab;
        tabEvidence.hidden = !linkToTab;
        if (tab) {
            const media = tab.hasScreen && tab.hasWebcam ? 'Screen and webcam saved at start'
                : tab.hasScreen ? 'Screen saved at start · Webcam unavailable'
                : tab.hasWebcam ? 'Webcam saved at start · Screen unavailable'
                : tab.evidencePending ? 'Checking saved footage' : 'No timeline footage saved at start';
            const time = tab.at ? new Date(tab.at).toLocaleString() : 'Time unavailable';
            tabAlert.textContent = [
                tab.ended ? 'Tab Switching observation · Returned' : 'Tab Switching observation · Exam page hidden',
                row.examName, 'Attempt #' + row.attemptId, time,
                'Warnings ' + (row.strikeCount ?? 0) + '/' + (row.allowedWarnings ?? 3), media
            ].filter(Boolean).join(' · ');
        } else {
            tabAlert.textContent = '';
        }
        if (linkToTab) {
            const query = new URLSearchParams({
                examId, studentId: row.studentId, attemptId: row.attemptId,
                incidentId: linkToTab.logId
            });
            tabEvidence.href = '/Teacher/Monitoring/ExamFolder?' + query + '#session-timeline';
        } else {
            tabEvidence.removeAttribute('href');
        }
        const fullscreen = row.fullscreenObservation;
        const fullscreenAlert = tile.element.querySelector('.tile-fullscreen-alert');
        const fullscreenEvidence = tile.element.querySelector('.fullscreen-evidence');
        fullscreenAlert.hidden = fullscreenEvidence.hidden = !fullscreen;
        if (fullscreen) {
            const recovery = fullscreen.recovery === 'MonitoringEnded' ? 'Monitoring ended before return'
                : fullscreen.ended ? 'Restored' : 'Awaiting return';
            const sources = [fullscreen.hasScreen && 'Screen', fullscreen.hasWebcam && 'Webcam'].filter(Boolean);
            const footage = sources.length ? sources.join(' + ') + ' saved at start'
                : fullscreen.evidencePending ? 'Checking saved footage' : 'No timeline footage saved at start';
            const query = new URLSearchParams({
                examId, studentId: row.studentId, attemptId: row.attemptId, incidentId: fullscreen.logId
            });
            fullscreenEvidence.href = '/Teacher/Monitoring/ExamFolder?' + query + '#session-timeline';
            fullscreenAlert.textContent = [
                'Fullscreen exit', recovery, row.examName, 'Attempt #' + row.attemptId,
                fullscreen.at && new Date(fullscreen.at).toLocaleString(),
                'Warnings ' + (row.strikeCount ?? 0) + '/' + (row.allowedWarnings ?? 3), footage
            ].filter(Boolean).join(' · ');
        } else {
            fullscreenAlert.textContent = '';
            fullscreenEvidence.removeAttribute('href');
        }
        const current = row.tileStatus || { state: 'uncertain', label: 'Refreshing monitoring status' };
        const unread = !!(row.alert && row.alert.logId > tile.seen);
        // Show committed, unreviewed incidents even if the rolling tile state is stale.
        const identityHold = current.state === 'identity_hold' ||
            (row.status === 'ProctoringBlocked' && row.alert?.kind === 'IdentityMismatch');
        // A non-cheating technical pause needs a conspicuous red review tile,
        // but must never be displayed as a confirmed misconduct strike.
        const technicalHold = current.state === 'technical_hold' || row.status === 'ProctoringBlocked' &&
            (row.alert?.description?.startsWith('[DEVICE_HOLD]') ||
                row.alert?.kind === 'AiMonitoringUnavailable');
        const flagged = identityHold || technicalHold || row.status === 'ProctoringBlocked' ||
            current.state === 'active';
        tile.element.querySelector('.strikes').textContent = row.status === 'ProctoringBlocked' ? 'BLOCKED' : row.blockingMode === "MANUAL_ONLY" ? `${row.strikeCount || 0} recorded · Teacher decision` : `${Math.min(row.allowedWarnings ?? 3, row.strikeCount || 0)}/${row.allowedWarnings ?? 3}`;
        tile.element.querySelector('.review-requests').hidden = row.status !== 'ProctoringBlocked';
        tile.element.querySelector('.pause').hidden = row.status !== 'InProgress';
        tile.element.querySelector('.verify').disabled = row.status !== 'InProgress';
        tile.element.classList.toggle('needs-review', flagged);
        tile.element.classList.toggle('technical-hold', !!technicalHold);
        tile.element.classList.toggle('identity-review-hold', identityHold);
        tile.element.classList.toggle('monitoring-uncertain', current.state === 'uncertain');
        tile.element.classList.toggle('monitoring-technical', current.state === 'technical');
        tile.element.classList.toggle('exam-blocked', current.state === 'blocked');
        const reasons = { HeadAway: 'Head turned away from the screen', GazeAway: 'Eyes looking away from the screen', LookAway: 'Looking away from the screen', FaceNotDetected: 'Candidate physically absent for at least 3 seconds', FaceNotVisible: 'Face not visible — candidate presence uncertain', MultipleFaces: 'Multiple people detected in camera view', TalkingDetected: 'Candidate speech detected', AnswerPromptSpeech: 'Exam-related discussion detected', ElectronicDeviceDetected: 'Prohibited object detected — inspect evidence', TabSwitch: 'Exam page left', FullScreenExit: 'Fullscreen exited', IdentityMismatch: 'Candidate identity mismatch — urgent review', CameraMonitoringInterrupted: 'Camera monitoring interrupted', AiMonitoringUnavailable: 'Technical monitoring issue' };
        const duplicateBrowserLabel = tab?.logId === latestId && !row.alert?.countsAsViolation ||
            fullscreen?.logId === latestId && current.state !== 'active' && row.status !== 'ProctoringBlocked';
        const incidentLabel = duplicateBrowserLabel ? null
            : row.alert?.description || reasons[row.alert?.kind] || row.alert?.kind;
        const incidentAt = row.alert?.frameCapturedAt || row.alert?.createdDate;
        const incidentTime = incidentAt ? new Date(incidentAt).toLocaleTimeString() : '';
        const warning = row.alert?.countsAsViolation && row.alert?.warningNumber > 0
            ? ` · Warning ${row.alert.warningNumber}/${row.allowedWarnings || 3}` : '';
        const score = row.alert?.confidenceScore != null && Number.isFinite(Number(row.alert.confidenceScore))
            ? ` · Confidence ${Math.round(Number(row.alert.confidenceScore) * 100)}% (model estimate)` : '';
        const question = row.alert?.matchedQuestionId ? ` · Question #${row.alert.matchedQuestionId}` : '';
        const evidence = row.alert?.hasScreenshot || row.alert?.hasVideo ? ' · Evidence available' : ' · Evidence pending/unavailable';
        tile.element.querySelector('.tile-alert').textContent = unread && incidentLabel
            ? `${incidentLabel}${warning}${question}${score}${incidentTime ? ' · ' + incidentTime : ''}${evidence}`
            : current.label;
        const overlay = tile.element.querySelector('.tile-overlay-warning');
        overlay.hidden = !flagged; // A blocked exam must remain visibly red until authorized review.
        overlay.textContent = (current.state === 'active' && unread && incidentLabel ? incidentLabel : current.label).slice(0, 140);
        tile.element.querySelector('.tile-alert').title = row.examName || '';
        tile.element.querySelector('.acknowledge').hidden = !unread;
        tile.element.querySelector('.pause').disabled = row.status !== 'InProgress';
        if (!['InProgress', 'ProctoringBlocked'].includes(row.status)) {
            tile.peer?.pc.close(); tile.peer = null; resetVideo(tile);
            tile.element.querySelector('.connection').textContent = 'Paused — open review to authorize resume';
        }
    }
    let realtimeEpoch = 0;
    let rosterBusy = false;
    let rosterTimer;
    let pageGeneration = 0;
    async function roster() {
        if (stopped || rosterBusy) return;
        rosterBusy = true;
        const startedEpoch = realtimeEpoch;
        const generation = pageGeneration;
        try {
            const response = await fetch(`/Teacher/Monitoring/LiveRoster?examId=${examId}&page=${page}${targetAttempt > 0 ? `&attemptId=${targetAttempt}` : ''}`, { signal: AbortSignal.timeout(10000) });
            if (response.status === 404) { location.replace('/Teacher/Monitoring/Live'); stopped = true; return; }
            if ([401, 403].includes(response.status)) { for (const id of [...tiles.keys()]) remove(id); stopped = true; status.textContent = 'This monitoring invitation is no longer available.'; return; }
            if (!response.ok) throw new Error('Room unavailable');
            const result = await response.json();
            if (stopped || generation !== pageGeneration) return;
            page = result.page; pages = result.pages;
            for (const id of tiles.keys()) if (!result.rows.some(r => r.attemptId === id)) remove(id);
            for (const row of result.rows) {
                const tile = tiles.get(row.attemptId) || make(row);
                if ((tile.updatedAtEpoch || 0) <= startedEpoch) update(tile, row);
                offerSpeechPlayback(row);
            }
            status.textContent = result.count ? `${result.count} candidates · ${result.rows.length} shown` : 'No active candidates yet. This room updates automatically.';
            document.getElementById('live-pagination').hidden = pages <= 1;
            document.getElementById('live-page').textContent = `Page ${page} of ${pages}`;
            document.getElementById('live-prev').disabled = page <= 1;
            document.getElementById('live-next').disabled = page >= pages;
        } catch { status.textContent = 'Connection interrupted. Reconnecting…'; for (const tile of tiles.values()) update(tile, { ...tile.row, tileStatus: { state: 'uncertain', label: 'Connection interrupted' } }); }
        finally { rosterBusy = false; if (!stopped) rosterTimer = setTimeout(roster, generation === pageGeneration ? 2000 : 0); }
    }
    function watch(tile) {
        if (!['InProgress', 'ProctoringBlocked'].includes(tile.row.status)) return;
        const now = Date.now();
        const connected = tile.peer?.pc.connectionState === 'connected';
        if (tile.watchPending || now - tile.watchAt < (connected ? 10000 : 2500)) return;
        if (!connected && now - tile.created > 12000)
            tile.element.querySelector('.connection').textContent = tile.wasLive ? 'Reconnecting…' : 'Live video unavailable — retrying';
        tile.watchAt = now;
        tile.watchPending = true;
        void send(tile.row.attemptId, 'watch').catch(() => {}).finally(() => { tile.watchPending = false; });
    }
    async function poll() {
        try {
            if (tiles.size === 0) return;
            for (const tile of tiles.values()) watch(tile);
            const response = await LiveProctoring.post('/live-proctoring/poll', { viewer }, token);
            for (const message of await response.json()) {
                const tile = tiles.get(message.attemptId);
                if (!tile || !['InProgress', 'ProctoringBlocked'].includes(tile.row.status)) continue;
                tile.signaling = (tile.signaling || Promise.resolve()).then(async () => {
                if (tiles.get(message.attemptId) !== tile || stopped) return;
                if (message.kind === 'offer') {
                    if (tile.offer === message.data && tile.peer &&
                        !['failed', 'closed', 'disconnected'].includes(tile.peer.pc.connectionState)) return;
                    const config = await cameraConfiguration();
                    if (tiles.get(message.attemptId) !== tile || stopped) return;
                    tile.peer?.pc.close();
                    resetVideo(tile);
                    tile.offer = message.data;
                    const roles = JSON.parse(message.data).media || {};
                    tile.peer = LiveProctoring.peer(config, (kind, data) => send(message.attemptId, kind, data),
                        (stream, event) => {
                            const role = event?.transceiver.mid === roles.screen && roles.screen != null ? 'screen' : 'camera';
                            tile.streams[role] = stream;
                            paintVideo(tile);
                        },
                        state => {
                            if (state === 'connected') { tile.wasLive = true; paintVideo(tile); }
                            else tile.element.querySelector('.connection').textContent = tile.wasLive ? 'Reconnecting…' : 'Connecting live video…';
                        });
                    tile.peer.pc.ondatachannel = event => {
                        if (event.channel.label !== 'exam-video-status') return;
                        event.channel.onmessage = update => {
                            try {
                                const available = JSON.parse(update.data);
                                tile.availability = { camera: available.camera === true, screen: available.screen === true };
                                paintVideo(tile);
                            } catch {}
                        };
                    };
                    await tile.peer.receive('offer', message.data);
                    await tile.peer.pc.setLocalDescription(await tile.peer.pc.createAnswer());
                    await send(message.attemptId, 'answer', JSON.stringify(tile.peer.pc.localDescription));
                    for (const ice of tile.pending || []) await tile.peer.receive('ice', ice);
                    tile.pending = [];
                } else if (message.kind === 'ice') {
                    if (tile.peer) await tile.peer.receive('ice', message.data).catch(() => {});
                    else { tile.pending ??= []; if (tile.pending.length < 64) tile.pending.push(message.data); }
                } else if (message.kind === 'bye') {
                    tile.peer?.pc.close(); tile.peer = null; tile.pending = []; resetVideo(tile);
                    tile.element.querySelector('.connection').textContent = 'Live video unavailable — retrying';
                }
                }).catch(() => {
                    tile.peer?.pc.close(); tile.peer = null; tile.pending = [];
                    resetVideo(tile);
                    tile.element.querySelector('.connection').textContent = 'Reconnecting…';
                });
            }
        } catch { /* Room status and connection labels remain visible during retries. */ }
        finally { if (!stopped) setTimeout(poll, tiles.size && [...tiles.values()].some(t => t.peer?.pc.connectionState !== 'connected') ? 250 : 750); }
    }
    function changePage(next) {
        if (next < 1 || next > pages) return;
        targetAttempt = 0; page = next; pageGeneration++;
        for (const id of tiles.keys()) remove(id);
        clearTimeout(rosterTimer);
        void roster();
    }
    document.getElementById('live-prev').onclick = () => changePage(page - 1);
    document.getElementById('live-next').onclick = () => changePage(page + 1);
    document.getElementById('pause-cancel').onclick = () => document.getElementById('pause-dialog').close();
    document.getElementById('pause-form').onsubmit = async event => {
        event.preventDefault(); const button = event.submitter; button.disabled = true;
        try {
            await LiveProctoring.post('/Teacher/Monitoring/PauseLive', { attemptId: pauseAttempt, reason: document.getElementById('pause-reason').value }, token);
            document.getElementById('pause-dialog').close();
        } catch { document.getElementById('pause-error').textContent = 'Could not pause. Check your reason and connection, then retry.'; }
        finally { button.disabled = false; }
    };
    window.addEventListener('pagehide', () => { stopped = true; speechPlayer?.pause(); for (const id of tiles.keys()) remove(id); });
    for (const event of ['CandidateStateChanged', 'BrowserObservationUpdated', 'SpeechContextUpdated']) window.ProctoringRealtime?.connection.on(event, state => {
        const tile = tiles.get(state.attemptId);
        if (state.status === 'InProgress' && state.violationType && !liveAlertKinds.has(state.violationType)) return;
        if (tile && state.version < (tile.row.version || 0)) return;
        if (state.violationType === 'ScreenShareLost' || state.violationType === 'ScreenCaptureUnavailable') {
            // The roster resolves the current sharing episode along with camera/mic status.
            if (tile && !rosterBusy) {
                clearTimeout(rosterTimer);
                void roster();
            }
            return;
        }
        if (tile && state.status === 'InProgress' && !state.eventId && typeof state.absenceActive === 'boolean') {
            // A preliminary presence sample must not replace an active incident or its evidence link.
            if (tile.row.tileStatus?.state !== 'active') {
                tile.updatedAtEpoch = ++realtimeEpoch;
                update(tile, { ...tile.row, version: state.version, strikeCount: state.strikeCount,
                    tileStatus: { state: 'uncertain', label: state.absenceActive
                        ? 'Possible absence — checking camera view' : 'Refreshing camera presence' } });
            }
            return;
        }
        if (tile && typeof state.tabObservationEnded === 'boolean' && state.eventId) {
            const previous = tile.row.tabObservation;
            if (previous && (state.eventId < previous.logId ||
                (state.eventId === previous.logId && (previous.ended || !state.tabObservationEnded)))) return;
            tile.updatedAtEpoch = ++realtimeEpoch;
            update(tile, {
                ...tile.row,
                strikeCount: state.strikeCount,
                version: state.version,
                tabObservation: {
                    ...(previous?.logId === state.eventId ? previous : { evidencePending: true }),
                    logId: state.eventId, at: state.occurredAt, ended: state.tabObservationEnded
                }
            });
            return;
        }
        if (tile && typeof state.fullscreenObservationEnded === 'boolean' && state.eventId) {
            const previous = tile.row.fullscreenObservation;
            if (!previous || state.eventId > previous.logId ||
                (state.eventId === previous.logId && state.fullscreenObservationEnded && !previous.ended)) {
                tile.row = {
                    ...tile.row,
                    fullscreenObservation: {
                        ...(previous?.logId === state.eventId ? previous : { evidencePending: true }),
                        logId: state.eventId, at: state.occurredAt, ended: state.fullscreenObservationEnded,
                        recovery: state.reason?.includes('Monitoring ended before') ? 'MonitoringEnded' : null
                    }
                };
            }
            if (state.status === 'InProgress' && state.fullscreenObservationEnded) {
                tile.updatedAtEpoch = ++realtimeEpoch;
                const closingCurrent = tile.row.alert?.logId === state.eventId || !tile.row.alert;
                update(tile, {
                    ...tile.row, strikeCount: state.strikeCount, version: state.version,
                    tileStatus: closingCurrent
                        ? { state: 'uncertain', label: 'Refreshing current monitoring status' }
                        : tile.row.tileStatus
                });
                return;
            }
        }
        if (tile && state.violationType === 'FaceNotDetected' && state.status === 'InProgress' &&
            state.eventId === tile.row.alert?.logId && state.version <= (tile.row.version || 0) && !!state.absenceEnded === !!tile.row.alert?.absenceEnded &&
            state.absenceActive === tile.row.alert?.absenceActive) return;
        realtimeEpoch++;
        if (tile) tile.updatedAtEpoch = realtimeEpoch;
        if (tile && state.version >= (tile.row.version || 0)) update(tile, {
            ...tile.row, ...state,
            tileStatus: { state: state.status === 'ProctoringBlocked' ? (state.eventCode === 'IDENTITY_REVIEW_HOLD' || state.violationType === 'IdentityMismatch' ? 'identity_hold' : 'blocked') : state.eventCode === 'DEVICE_WARNING' ? 'technical' : state.eventCountsAsViolation && !state.absenceEnded && state.absenceActive !== false && !state.peopleEnded && !state.attentionEnded ? 'active' : 'uncertain', label: state.eventCode === 'DEVICE_WARNING' ? (state.reason || 'Technical device issue') : state.eventCode === 'DEVICE_HOLD' ? 'Technical device hold — check recovery' : state.eventCode === 'IDENTITY_REVIEW_HOLD' ? 'Candidate identity mismatch — urgent teacher review' : state.status === 'ProctoringBlocked' ? 'Blocked — open review for details' : 'Monitoring update — refreshing current state' },
            alert: state.violationType ? {
                logId: state.eventId, kind: state.violationType,
                description: state.reason || null, createdDate: state.occurredAt || new Date().toISOString(),
                frameCapturedAt: state.occurredAt, hasScreenshot: !!state.hasScreenshot,
                hasVideo: !!state.hasRecording, matchedQuestionId: state.matchedQuestionId || null,
                countsAsViolation: state.eventCountsAsViolation === true, warningNumber: state.eventWarningNumber, absenceEnded: state.absenceEnded === true, absenceActive: state.absenceActive, peopleEnded: state.peopleEnded === true, attentionEnded: state.attentionEnded === true
            } : tile.row.alert
        });
    });
    const appealIds = new Set();
    window.ProctoringRealtime?.connection.on('LockReviewRequested', appeal => {
        if (appealIds.has(appeal.appealId)) return;
        appealIds.add(appeal.appealId);
        let notices = document.getElementById('lock-review-notices');
        if (!notices) { notices = document.createElement('div'); notices.id = 'lock-review-notices'; notices.setAttribute('aria-live', 'polite'); document.getElementById('live-room').prepend(notices); }
        const notice = document.createElement('div'); notice.className = 'alert alert-warning d-flex flex-wrap gap-2 align-items-center';
        const text = document.createElement('strong'); text.textContent = `${appeal.candidateName} requests lock review · Attempt #${appeal.attemptId}`;
        const link = document.createElement('a'); link.className = 'btn btn-sm btn-dark'; link.textContent = 'Review violation audit'; link.href = appeal.reviewUrl;
        notice.append(text, link); notices.prepend(notice);
        while (notices.children.length > 10) notices.lastElementChild.remove();
    });
    if (examId) { roster(); poll(); } else status.textContent = 'Create an exam first, then open its live room.';
})();

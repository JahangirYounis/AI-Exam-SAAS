(() => {
    const panel = document.getElementById('session-timeline');
    if (!panel) return;
    const find = name => panel.querySelector(`[data-${name}]`);
    const session = find('session'), marker = find('marker'), seek = find('seek');
    const playButton = find('play'), status = find('status');
    const eventStart = find('event-start'), eventReturn = find('event-return'), eventSummary = find('event-summary');
    const sources = {
        screen: { video: find('screen'), title: 'Screen' },
        camera: { video: find('camera'), title: 'Webcam' }
    };
    let data, current, start, end, last, cursor = 0, playing = false, clips = [];

    function frame(source, at) {
        const times = row => row.metadata.startedAt <= at && at < row.metadata.endedAt;
        const active = clips.filter(row => row.metadata.source === source && times(row));
        // Camera clips overlap for incident prebuffering. Keep the available source
        // continuous even if one overlapping encoder reported an interruption.
        return active.findLast(row => row.url) || active.at(-1);
    }

    function render(forceSeek = false) {
        if (!current) return;
        const at = cursor;
        const messages = [];
        for (const [role, view] of Object.entries(sources)) {
            const clip = frame(role, at);
            const url = clip?.url || null;
            const video = view.video;
            if (view.url !== url) {
                view.url = url;
                video.pause();
                if (url) video.src = url;
                else { video.removeAttribute('src'); video.load(); }
            }
            video.hidden = !url || video.error;
            if (!url || video.error) {
                messages.push(`${view.title} unavailable`);
                continue;
            }
            const offset = (at - clip.metadata.startedAt) / 1000;
            if (video.readyState >= HTMLMediaElement.HAVE_METADATA) {
                if (offset >= video.duration && Number.isFinite(video.duration)) {
                    video.hidden = true;
                    video.pause();
                    messages.push(`${view.title} footage ended before the recorded interval`);
                    continue;
                }
                if (forceSeek || Math.abs(video.currentTime - offset) > .35)
                    video.currentTime = Math.max(0, offset);
            }
            messages.push(`${view.title} ${clip.metadata.status === 'interrupted' ? 'partial' : 'recorded'}`);
            if (role === 'camera' && !clip.metadata.includesMicrophone) messages.push('Microphone unavailable');
            if (playing) void video.play().catch(() => {});
            else video.pause();
        }
        find('time').textContent = new Date(at).toLocaleString();
        status.textContent = messages.join(' · ');
    }

    function jump(at) {
        cursor = Math.max(start, Math.min(end, at));
        seek.value = (cursor - start) / 1000;
        last = performance.now();
        render(true);
    }

    function setSession(id) {
        playing = false;
        current = data.sessions.find(row => row.id === id);
        if (!current) return;
        clips = data.segments.filter(row => row.sessionAttemptId === id && row.metadata)
            .sort((a, b) => a.metadata.startedAt - b.metadata.startedAt || a.metadata.sequence - b.metadata.sequence);
        const sessionStart = current.start ? Date.parse(current.start) : NaN;
        start = Math.min(Number.isFinite(sessionStart) ? sessionStart : Infinity, ...clips.map(row => row.metadata.startedAt));
        if (!Number.isFinite(start)) start = Date.now();
        const observations = data.markers.filter(row => row.sessionAttemptId === id);
        const eventTimes = observations.flatMap(row => [Date.parse(row.at), Date.parse(row.returnedAt)])
            .filter(Number.isFinite);
        start = Math.min(start, ...eventTimes);
        end = Math.max(start, current.end ? Date.parse(current.end) : 0,
            ...clips.map(row => row.metadata.endedAt), ...eventTimes);
        seek.max = Math.max(0, (end - start) / 1000);
        playButton.disabled = !clips.some(row => row.url);
        marker.replaceChildren(new Option('Choose an observation', ''));
        for (const item of observations) {
            const option = new Option(`${item.kind || 'Observation'} #${item.logId} · ${item.at ? new Date(item.at).toLocaleTimeString() : 'capture time unavailable'}`, item.logId);
            marker.append(option);
        }
        showMarkerDetails(null);
        jump(start);
    }

    function showMarkerDetails(item) {
        eventStart.disabled = !item?.at;
        eventReturn.disabled = !item?.returnedAt;
        eventStart.onclick = item?.at ? () => jump(Date.parse(item.at)) : null;
        eventReturn.onclick = item?.returnedAt ? () => jump(Date.parse(item.returnedAt)) : null;
        eventSummary.textContent = item ? [
            'Observation #' + item.logId,
            item.at ? 'Start ' + new Date(item.at).toLocaleTimeString() : 'Start capture time unavailable',
            item.returnedAt ? 'Return ' + new Date(item.returnedAt).toLocaleTimeString()
                : item.monitoringEnded ? 'Monitoring ended before return'
                : item.ended ? 'Return capture time unavailable' : 'No return recorded',
            Number.isFinite(item.durationSeconds) ? item.durationSeconds.toFixed(1) + ' s observed' : null
        ].filter(Boolean).join(' · ') : '';
    }

    function showMarker(id) {
        const item = data.markers.find(row => row.logId === Number(id));
        if (!item) { showMarkerDetails(null); return; }
        if (current?.id !== item.sessionAttemptId) {
            session.value = item.sessionAttemptId;
            setSession(item.sessionAttemptId);
        }
        marker.value = item.logId;
        showMarkerDetails(item);
        if (item.at) jump(Date.parse(item.at) - 5000);
    }

    for (const { video } of Object.values(sources)) {
        video.addEventListener('loadedmetadata', () => render(true));
        video.addEventListener('error', () => render());
    }
    playButton.onclick = () => { playing = true; last = performance.now(); render(true); };
    find('pause').onclick = () => { playing = false; render(); };
    seek.oninput = () => jump(start + Number(seek.value) * 1000);
    marker.onchange = () => showMarker(marker.value);
    session.onchange = () => setSession(Number(session.value));
    const timer = setInterval(() => {
        if (!playing) return;
        const now = performance.now();
        if (Object.values(sources).some(view => view.url && !view.video.error &&
            view.video.readyState < HTMLMediaElement.HAVE_METADATA)) {
            last = now;
            return;
        }
        cursor = Math.min(end, cursor + now - last);
        last = now;
        if (cursor === end) playing = false;
        seek.value = (cursor - start) / 1000;
        render();
    }, 150);
    window.addEventListener('pagehide', () => clearInterval(timer), { once: true });

    fetch(panel.dataset.url, { signal: AbortSignal.timeout(15000) })
        .then(response => { if (!response.ok) throw new Error('Recording unavailable'); return response.json(); })
        .then(result => {
            data = result;
            for (const [index, row] of data.sessions.entries()) session.append(new Option(`Session ${index + 1}`, row.id));
            if (!data.sessions.length) { status.textContent = 'No recording sessions are available.'; return; }
            setSession(data.sessions[0].id);
            showMarker(panel.dataset.markerId || new URLSearchParams(location.search).get('marker'));
        })
        .catch(() => { status.textContent = 'Could not load the recording timeline. Reload to retry.'; });
})();

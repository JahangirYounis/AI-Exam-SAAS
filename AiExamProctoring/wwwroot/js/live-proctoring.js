/* Authenticated signalling for video-only WebRTC camera monitoring. */
window.LiveProctoring = (() => {
    const signals = [];
    const received = new Set();
    let subscribed = false;
    function socket() {
        const connection = window.ProctoringRealtime?.connection;
        if (connection && !subscribed) {
            subscribed = true;
            connection.on('VideoSignal', message => { if (signals.length < 128) signals.push(message); });
        }
        return connection?.state === 'Connected' ? connection : null;
    }
    async function post(path, values, token) {
        const connection = socket();
        if (connection && path === '/live-proctoring/signal') {
            await connection.invoke('Relay', Number(values.attemptId), values.viewer, values.kind, values.data || '');
            return { ok: true };
        }
        const body = new URLSearchParams({ ...values, __RequestVerificationToken: token });
        const response = await fetch(path, { method: 'POST', body, credentials: 'same-origin', signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error(String(response.status));
        if (path === '/live-proctoring/poll') {
            const batch = [...signals.splice(0), ...await response.json()];
            const unique = batch.filter(m => {
                if (values.viewer ? m.viewer !== values.viewer : m.attemptId !== Number(values.attemptId)) return false;
                if (m.id && received.has(m.id)) return false;
                if (m.id) received.add(m.id);
                if (received.size > 512) received.delete(received.values().next().value);
                return true;
            });
            return { ok: true, json: async () => unique };
        }
        return response;
    }
    async function configuration() {
        const response = await fetch('/live-proctoring/configuration', { signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error('Live monitoring configuration unavailable');
        return response.json();
    }
    function peer(config, send, onTrack, onState) {
        const pc = new RTCPeerConnection(config);
        const pending = [];
        pc.onicecandidate = event => { if (event.candidate) send('ice', JSON.stringify(event.candidate)).catch(() => {}); };
        pc.ontrack = event => onTrack?.(event.streams[0]);
        pc.onconnectionstatechange = () => onState?.(pc.connectionState);
        return {
            pc,
            async receive(kind, data) {
                if (kind === 'ice') {
                    const candidate = JSON.parse(data);
                    if (pc.remoteDescription) await pc.addIceCandidate(candidate);
                    else if (pending.length < 64) pending.push(candidate);
                } else {
                    await pc.setRemoteDescription(JSON.parse(data));
                    for (const candidate of pending.splice(0)) await pc.addIceCandidate(candidate);
                }
            }
        };
    }
    function student(attemptId, token, getStream, onStatus) {
        const peers = new Map();
        let stopped = false, config;
        const send = (viewer, kind, data = '') => post('/live-proctoring/signal', { attemptId, viewer, kind, data }, token);
        const close = viewer => { peers.get(viewer)?.pc.close(); peers.delete(viewer); };
        async function tick() {
            if (stopped) return;
            try {
                const stream = getStream();
                if (!stream?.getVideoTracks().some(t => t.readyState === 'live')) {
                    for (const viewer of peers.keys()) close(viewer);
                    onStatus?.('Camera sharing unavailable');
                    return;
                }
                config ??= await configuration();
                const response = await post('/live-proctoring/poll', { attemptId }, token);
                for (const message of await response.json()) {
                    const viewer = message.viewer;
                    if (message.kind === 'bye') { close(viewer); continue; }
                    let current = peers.get(viewer);
                    if (message.kind === 'watch') {
                        if (current && !['closed', 'failed', 'disconnected'].includes(current.pc.connectionState)
                            && (current.pc.connectionState === 'connected' || Date.now() - current.created < 20000)) {
                            current.seen = Date.now(); continue;
                        }
                        close(viewer);
                        if (peers.size >= Math.max(1, Math.min(16, Number(config.maxViewersPerCandidate) || 8))) continue;
                        current = peer(config, (kind, data) => send(viewer, kind, data));
                        current.seen = Date.now();
                        current.created = Date.now();
                        peers.set(viewer, current);
                        for (const track of stream.getVideoTracks()) {
                            const sender = current.pc.addTrack(track, stream);
                            const parameters = sender.getParameters();
                            parameters.encodings = [{ maxBitrate: 220000, maxFramerate: 8, scaleResolutionDownBy: 2 }];
                            await sender.setParameters(parameters).catch(() => {});
                        }
                        await current.pc.setLocalDescription(await current.pc.createOffer());
                        await send(viewer, 'offer', JSON.stringify(current.pc.localDescription));
                    } else if (current) {
                        await current.receive(message.kind, message.data);
                    }
                }
                for (const [viewer, current] of peers) if (Date.now() - current.seen > 30000) close(viewer);
                onStatus?.(peers.size ? 'Live camera shared with your teacher' : 'Your teacher can view your live camera during this exam');
            } catch (error) {
                if (error.message === '404' || error.message === '401' || error.message === '403') {
                    stopped = true;
                    for (const viewer of peers.keys()) close(viewer);
                }
                onStatus?.('Live camera connection interrupted');
            } finally { if (!stopped) setTimeout(tick, 2000); }
        }
        tick();
        return () => { stopped = true; for (const viewer of peers.keys()) close(viewer); };
    }
    return { post, configuration, peer, student };
})();

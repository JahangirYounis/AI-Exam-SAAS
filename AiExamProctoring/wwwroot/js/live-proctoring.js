/* Authenticated signalling for separate, video-only camera and display tracks. */
window.LiveProctoring = (() => {
    async function post(path, values, token) {
        const body = new URLSearchParams({ ...values, __RequestVerificationToken: token });
        const response = await fetch(path, { method: 'POST', body, credentials: 'same-origin', signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error(String(response.status));
        if (path === '/live-proctoring/poll') {
            const batch = await response.json();
            const unique = batch.filter(m => {
                if (values.viewer ? m.viewer !== values.viewer : m.attemptId !== Number(values.attemptId)) return false;
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
        pc.ontrack = event => onTrack?.(event.streams[0] || new MediaStream([event.track]), event);
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
    function student(attemptId, token, getStream, onStatus, getDisplay) {
        const peers = new Map();
        let stopped = false, config;
        const send = (viewer, kind, data = '') => post('/live-proctoring/signal', { attemptId, viewer, kind, data }, token);
        const close = viewer => { peers.get(viewer)?.pc.close(); peers.delete(viewer); };
        const liveTrack = stream => stream?.getVideoTracks().find(track => track.readyState === 'live');
        async function syncTracks(current, tracks) {
            for (const [role, transceiver] of Object.entries(current.media)) {
                const track = tracks[role] || null;
                if (transceiver.sender.track !== track)
                    await transceiver.sender.replaceTrack(track);
            }
            const availability = JSON.stringify(Object.fromEntries(
                Object.entries(tracks).map(([role, track]) => [role, !!track && !track.muted])));
            if (current.channel.readyState === 'open' && availability !== current.availability) {
                current.channel.send(availability);
                current.availability = availability;
            }
        }
        async function tick() {
            if (stopped) return;
            try {
                const tracks = { camera: liveTrack(getStream()), screen: liveTrack(getDisplay?.()) };
                if (!tracks.camera && !tracks.screen) {
                    for (const viewer of peers.keys()) {
                        close(viewer);
                        void send(viewer, 'bye').catch(() => {});
                    }
                    onStatus?.('Live video unavailable');
                    return;
                }
                config ??= await configuration();
                if (stopped) return;
                const response = await post('/live-proctoring/poll', { attemptId }, token);
                if (stopped) return;
                for (const [viewer, current] of peers) {
                    try { await syncTracks(current, tracks); }
                    catch { close(viewer); }
                }
                for (const message of await response.json()) {
                    if (stopped) return;
                    const viewer = message.viewer;
                    if (message.kind === 'bye') { close(viewer); continue; }
                    let current = peers.get(viewer);
                    if (message.kind === 'watch') {
                        if (current && !['closed', 'failed'].includes(current.pc.connectionState)
                            && (current.pc.connectionState === 'connected' || Date.now() - current.created < 12000)) {
                            current.seen = Date.now(); continue;
                        }
                        close(viewer);
                        if (peers.size >= Math.max(1, Math.min(16, Number(config.maxViewersPerCandidate) || 8))) continue;
                        current = peer(config, (kind, data) => send(viewer, kind, data));
                        current.seen = Date.now();
                        current.created = Date.now();
                        peers.set(viewer, current);
                        current.media = {
                            camera: current.pc.addTransceiver('video', { direction: 'sendonly',
                                sendEncodings: [{ maxBitrate: 160000, maxFramerate: 8, scaleResolutionDownBy: 2 }] }),
                            screen: current.pc.addTransceiver('video', { direction: 'sendonly',
                                sendEncodings: [{ maxBitrate: 480000, maxFramerate: 5, scaleResolutionDownBy: 1.5 }] })
                        };
                        current.channel = current.pc.createDataChannel('exam-video-status');
                        try {
                            await syncTracks(current, tracks);
                            if (stopped) { close(viewer); return; }
                            await current.pc.setLocalDescription(await current.pc.createOffer());
                            await send(viewer, 'offer', JSON.stringify({
                                type: current.pc.localDescription.type, sdp: current.pc.localDescription.sdp,
                                media: Object.fromEntries(Object.entries(current.media).map(([role, transceiver]) => [role, transceiver.mid]))
                            }));
                        } catch { close(viewer); }
                    } else if (current) {
                        // A stale ICE candidate can arrive after a new offer.
                        await current.receive(message.kind, message.data).catch(() => {});
                    }
                }
                for (const [viewer, current] of peers) if (Date.now() - current.seen > 30000) close(viewer);
                onStatus?.(peers.size ? 'Live monitoring connected' : 'Ready for teacher live monitoring');
            } catch (error) {
                if (error.message === '404' || error.message === '401' || error.message === '403') {
                    stopped = true;
                    for (const viewer of peers.keys()) close(viewer);
                }
                onStatus?.('Live monitoring connection interrupted');
            } finally {
                if (!stopped) setTimeout(tick, [...peers.values()].some(p => p.pc.connectionState !== 'connected') ? 250 : peers.size ? 750 : 1000);
            }
        }
        tick();
        return () => { stopped = true; for (const viewer of peers.keys()) close(viewer); };
    }
    return { post, configuration, peer, student };
})();

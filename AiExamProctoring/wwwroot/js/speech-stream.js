window.ExamSpeechStream = (() => {
    function wav(parts) {
        const length = parts.reduce((n, p) => n + p.length, 0);
        const data = new ArrayBuffer(44 + length * 2), view = new DataView(data);
        const text = (offset, value) => [...value].forEach((c,i) => view.setUint8(offset+i,c.charCodeAt(0)));
        text(0,'RIFF'); view.setUint32(4,36+length*2,true); text(8,'WAVE'); text(12,'fmt ');
        view.setUint32(16,16,true); view.setUint16(20,1,true); view.setUint16(22,1,true);
        view.setUint32(24,16000,true); view.setUint32(28,32000,true); view.setUint16(32,2,true); view.setUint16(34,16,true);
        text(36,'data'); view.setUint32(40,length*2,true);
        let offset = 44; for (const part of parts) for (const value of part) { view.setInt16(offset,value,true); offset+=2; }
        return data;
    }
    async function start(options) {
        const context = new AudioContext();
        try { await context.audioWorklet.addModule('/js/speech-worklet.js?v=20261006-500ms'); }
        catch (error) { await context.close(); throw error; }
        const source = context.createMediaStreamSource(options.stream);
        const worklet = new AudioWorkletNode(context,'exam-pcm');
        const mute = context.createGain(); mute.gain.value = 0;
        source.connect(worklet); worklet.connect(mute); mute.connect(context.destination);
        let socket, ready = false, closed = false, retry, timeout;
        const pending = [];
        let ring = [], lastSpeech = null, lastAsr = -Infinity;
        function transcribe(captured, detectionLogId) {
            if (!detectionLogId) return;
            options.transcribe({ clip: new Blob([wav(captured.parts.filter(p => p.question === captured.question).map(p => p.pcm))], {type:'audio/wav'}),
                questionScreenshot: captured.screenshot, questionId: captured.question, questionIds: captured.question ? [captured.question] : [], detectionLogId });
        }
        const resume = () => context.resume().catch(() => {});
        document.addEventListener('pointerdown', resume);
        document.addEventListener('keydown', resume);
        context.onstatechange = () => { if (!closed && ready) options.status(context.state === 'running' ? 'Live speech monitoring active' : 'Click the exam to activate microphone processing'); };
        resume();
        function connect() {
            if (closed) return;
            socket = new WebSocket(options.url);
            socket.onopen = () => socket.send(options.token);
            socket.onmessage = event => {
                let result; try { result = JSON.parse(event.data); } catch { socket.close(); return; }
                if (result.ready && !('speechDetected' in result)) { ready = true; options.status(context.state === 'suspended' ? 'Click the exam to activate microphone processing' : 'Live speech monitoring active'); return; }
                const captured = pending.shift();
                if (!captured) { socket.close(); return; }
                clearTimeout(timeout);
                if (pending.length) timeout = setTimeout(() => socket.close(), 12000);
                // These packets were captured before any warning voice. Do not discard
                // their detection/evidence just because a later warning is now speaking.
                const detectionLogId = result.detectionLogId || result.logIds?.[0];
                if (lastSpeech && (!result.speechDetected || lastSpeech.captured.question !== captured.question)) {
                    // Flush short utterances too: waiting only for 1.5 seconds of positive
                    // VAD responses silently lost short spoken answers such as "option A".
                    const completed = lastSpeech.captured.question === captured.question ? captured : lastSpeech.captured;
                    transcribe(completed, lastSpeech.detectionLogId);
                    lastSpeech = null;
                }
                if (result.speechDetected && detectionLogId) {
                    lastSpeech = { captured, detectionLogId };
                    if (captured.parts.filter(p => p.question === captured.question).length >= 3 && performance.now() - lastAsr > 2000) {
                        lastAsr = performance.now();
                        transcribe(captured, detectionLogId);
                    } else if (result.softReminderId) {
                        // Queue the clean pre-warning audio before our own voice suppresses capture.
                        transcribe(captured, detectionLogId);
                    }
                }
                if (result.speechDetected) options.status(result.softReminderId || result.logIds?.length ? 'Speech warning recorded' : 'Speech heard; checking speaker and question context');
                options.result(result);
            };
            socket.onclose = () => { ready = false; pending.length = 0; ring = []; lastSpeech = null;
                clearTimeout(timeout); if (!closed) { options.status('Speech stream reconnecting'); retry = setTimeout(connect,2000); } };
            socket.onerror = () => socket.close();
        }
        worklet.port.onmessage = event => {
            if (closed || options.suppressed()) { ring = []; return; }
            const pcm = new Int16Array(event.data);
            const question = options.question();
            ring.push({pcm, question}); if (ring.length > 12) ring.shift(); // bounded six-second ASR context, partitioned by captured question
            if (!ready) return;
            // Apply bounded backpressure; report skipped audio explicitly. A genuine
            // stalled connection is still closed by the response timeout.
            if (pending.length >= 4 || socket.bufferedAmount > 96000) {
                // Keep the bounded in-flight queue and its capture-time context intact.
                // A slow response must not continually tear down the connection/ASR history.
                options.status('Audio monitoring gap: server catching up; some audio was skipped'); return;
            }
            const bytes = new Uint8Array(wav([pcm]));
            const audio = btoa(String.fromCharCode(...bytes));
            pending.push({question, parts: ring.slice(), screenshot: options.snapshot?.() || null});
            socket.send(JSON.stringify({audio,questionId:question ? Number(question) : null}));
            if (pending.length === 1) timeout = setTimeout(() => socket.close(),12000);
        };
        connect();
        return () => { closed=true; clearTimeout(retry); clearTimeout(timeout); socket?.close();
            source.disconnect(); worklet.disconnect(); mute.disconnect(); context.close();
            document.removeEventListener('pointerdown',resume); document.removeEventListener('keydown',resume); };
    }
    return { start, wav };
})();

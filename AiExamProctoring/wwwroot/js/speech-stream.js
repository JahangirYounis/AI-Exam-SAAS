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
    function createAnalysisQueue({ process, active, onFull, onError, capacity = 24 }) {
        const waiting = [];
        let running = false;
        async function drain() {
            if (running) return;
            running = true;
            try {
                while (waiting.length && active()) {
                    const packet = waiting.shift();
                    try { await process(packet); }
                    catch (error) { onError(error, packet); }
                }
            } finally {
                running = false;
                if (!active()) waiting.length = 0;
            }
        }
        return packet => {
            if (!active()) return false;
            if (waiting.length >= capacity) {
                onFull(packet);
                return false;
            }
            waiting.push(packet);
            void drain();
            return true;
        };
    }

    async function start(options) {
        const context = new AudioContext();
        let source, worklet, mute;
        try {
            await context.audioWorklet.addModule('/js/speech-worklet.js?v=20261006-500ms');
            source = context.createMediaStreamSource(options.stream);
            worklet = new AudioWorkletNode(context,'exam-pcm');
            mute = context.createGain();
            mute.gain.value = 0;
            source.connect(worklet); worklet.connect(mute); mute.connect(context.destination);
        } catch (error) {
            await context.close().catch(() => {});
            throw error;
        }
        let socket, ready = false, closed = false, retry, timeout, failures = 0;
        const pending = [];
        let ring = [], utterance = null;
        let gap = null, sequence = 0;
        function flushGap() { if(gap) { options.gap?.(gap); gap=null; } }
        function missed(reason, milliseconds) {
            if(gap && gap.reason!==reason)flushGap();
            gap ||= {reason,durationSeconds:0,firstSequence:sequence,lastSequence:sequence};
            gap.durationSeconds+=milliseconds/1000;gap.lastSequence=sequence;
            if(gap.durationSeconds>=5)flushGap();
        }

        let evidenceId = null, evidenceParts = [], evidenceEnd = -Infinity;
        function flushEvidence() {
            if (!evidenceId || !evidenceParts.length) return;
            const parts = evidenceParts; evidenceParts = [];
            options.evidence?.({ logId:evidenceId, clip:new Blob([wav(parts.map(p=>p.pcm))], {type:'audio/wav'}),
                capturedAt:parts[0].at, metadata:{source:'microphone', includesMicrophone:true,
                monotonicStart:parts[0].start, monotonicEnd:parts.at(-1).end,
                questionIds:[...new Set(parts.map(p=>p.question))], enhanced:false} });
        }
        function recordEvidence(capture, result) {
            if (!options.evidence) return;
            const id = result.detectionLogId || result.logIds?.[0];
            if (id && id !== evidenceId) { flushEvidence(); evidenceId=id; evidenceEnd=-Infinity; }
            if (!evidenceId) return;
            for (const part of capture.parts) if (part.sequence > evidenceEnd) {
                evidenceParts.push(part); evidenceEnd=part.sequence;
            }
            const continuing = result.speechDetected || result.speechKind === 'environmental_noise_uncertain';
            if (evidenceParts.length >= 6 || !continuing) flushEvidence();
            if (!continuing) evidenceId=null;
        }
        function transcribe(captured, detectionLogId) {
            if (!detectionLogId) return;
            const selected=captured.parts.filter(p=>p.question===captured.question).slice(-12);
            if (!selected.length) return; // A question transition must not create empty audio.
            options.transcribe({ capturedAt:selected[0]?.at, monotonicStart:selected[0]?.start, monotonicEnd:selected.at(-1)?.end, clip: new Blob([wav(selected.map(p => p.pcm))], {type:'audio/wav'}),
                questionScreenshot: captured.screenshot, questionId: captured.question, questionIds: captured.question ? [captured.question] : [], detectionLogId });
        }
        function flushUtterance() {
            if (!utterance) return;
            const completed = utterance;
            utterance = null;
            transcribe(completed, completed.detectionLogId);
        }

        const resume = () => context.resume().catch(() => {});
        document.addEventListener('pointerdown', resume);
        document.addEventListener('keydown', resume);
        context.onstatechange = () => { if (!closed && ready) options.status(context.state === 'running' ? 'Live speech monitoring active' : 'Click the exam to activate microphone processing'); };
        resume();
        let fallbackBusy = false, fallbackParts = [];
        async function screenWithoutSocket(part) {
            if (!options.detectFallback || fallbackBusy) {
                missed(fallbackBusy ? 'backpressure' : 'reconnecting', part.pcm.length/16);
                return;
            }
            if (fallbackParts.length && fallbackParts[0].question !== part.question) {
                missed('reconnecting', fallbackParts.reduce((n,p)=>n+p.pcm.length/16,0));
                fallbackParts = [];
            }
            fallbackParts.push(part);
            if (fallbackParts.length < 2) return;
            const batch = fallbackParts; fallbackParts = []; fallbackBusy = true;
            const capture = { question:part.question, parts:batch, screenshot:options.snapshot?.() || null };
            try {
                const result = await options.detectFallback(new Blob([wav(batch.map(p=>p.pcm))], {type:'audio/wav'}), part.question);
                if (closed) return;
                flushGap();
                recordEvidence(capture, result);
                options.result(result);
                const id = result.detectionLogId || result.logIds?.[0];
                if (result.speechDetected && id) transcribe(capture, id);
                if (!result.speechDetected && result.speechKind !== 'environmental_noise_uncertain')
                    options.status('Microphone screening active (HTTP)');
            } catch {
                if (!closed) missed('reconnecting',batch.reduce((n,p)=>n+p.pcm.length/16,0));
            } finally { fallbackBusy=false; }
        }
        function connect() {
            if (closed) return;
            try {
                socket = new WebSocket(options.url);
            } catch {
                reconnect();
                return;
            }
            socket.onopen = () => socket.send(options.token);
            socket.onmessage = event => {
                let result; try { result = JSON.parse(event.data); } catch { socket.close(); return; }
                if (!result || typeof result !== 'object' || Array.isArray(result)) { socket.close(); return; }
                if (result.ready && !('speechDetected' in result)) { ready = true; options.status(context.state === 'suspended' ? 'Click the exam to activate microphone processing' : 'Live speech monitoring active'); return; }
                if (typeof result.speechDetected !== 'boolean') { socket.close(); return; }
                failures = 0;
                const captured = pending.shift();
                if (!captured) { socket.close(); return; }
                clearTimeout(timeout);
                if (pending.length) timeout = setTimeout(() => socket.close(), 12000);
                // These packets were captured before any warning voice. Do not discard
                // their detection/evidence just because a later warning is now speaking.
                recordEvidence(captured, result);
                const detectionLogId = result.detectionLogId || result.logIds?.[0];
                const part = captured.parts.at(-1);
                if (utterance && (!result.speechDetected || utterance.question !== captured.question ||
                    utterance.detectionLogId !== detectionLogId || part.start - utterance.parts.at(-1).end > 750))
                    flushUtterance();
                if (result.speechDetected && detectionLogId) {
                    utterance ??= { question: captured.question, detectionLogId, parts: [], screenshot: captured.screenshot };
                    utterance.parts.push(part);
                    // Send short speech after the first quiet packet. A longer
                    // conversation is analyzed in bounded four-second clips.
                    if (utterance.parts.length >= 8) flushUtterance();
                }
                if (result.speechDetected) options.status(result.softReminderId || result.logIds?.length ? 'Speech warning recorded' : 'Speech heard; speaker attribution requires review');
                else options.status(context.state === 'suspended' ? 'Click the exam to activate microphone processing' : 'Live speech monitoring active');
                options.result(result);
            };
            socket.onclose = () => {
                ready = false;
                if (!closed) {
                    flushUtterance();
                    if (pending.length) {
                        const missingMilliseconds = pending.reduce((total, packet) => total + (packet.parts.at(-1)?.pcm.length || 0) / 16, 0);
                        missed('connection_lost', missingMilliseconds);
                    }
                    flushGap();
                }
                flushEvidence();
                pending.length = 0; ring = []; utterance = null;
                clearTimeout(timeout);
                if (!closed) reconnect();
            };
            socket.onerror = () => socket.close();
        }
        function reconnect() {
            options.status('Speech stream reconnecting');
            retry = setTimeout(connect, Math.min(30000, 2000 * 2 ** Math.min(failures++, 4)));
        }
        worklet.port.onmessage = event => {
            if (closed) return;
            const pcm = new Int16Array(event.data); sequence++;
            // Health heartbeat from actual AudioWorklet PCM delivery. A live
            // MediaStreamTrack alone does not prove that audio callbacks continue.
            window.dispatchEvent?.(new CustomEvent('exam-microphone-pcm',
                { detail: { observedAt: performance.now(), samples: pcm.length } }));
            const question = options.question();
            const end=performance.now();
            const part={pcm, question, sequence, at:new Date(Date.now()-pcm.length/16).toISOString(),start:end-pcm.length/16,end};
            // Original evidence continues during inference backpressure and our own
            // audible advisory. Those intervals are excluded only from AI screening.
            if (evidenceId && part.sequence > evidenceEnd) {
                evidenceParts.push(part); evidenceEnd=part.sequence;
                if(evidenceParts.length>=6)flushEvidence();
            }
            if (options.suppressed()) {
                missed('warning_playback', pcm.length / 16);
                ring = [];
                fallbackParts = [];
                return;
            }
            ring.push(part); if (ring.length > 30) ring.shift(); // fifteen-second prebuffer; ASR remains bounded to six seconds
            // A close can race with AudioWorklet packets before onclose fires.
            // Sending on a CLOSING/CLOSED socket otherwise throws and can stop
            // further microphone handling without triggering the HTTP fallback.
            if (!ready || socket?.readyState !== WebSocket.OPEN) {
                screenWithoutSocket(ring[ring.length-1]);
                return;
            }
            if (fallbackParts.length) { missed('reconnecting', fallbackParts.reduce((n,p)=>n+p.pcm.length/16,0)); fallbackParts=[]; }
            if (fallbackBusy) { missed('backpressure',pcm.length/16); return; }
            // Apply bounded backpressure; report skipped audio explicitly. A genuine
            // stalled connection is still closed by the response timeout.
            if (pending.length >= 4 || socket.bufferedAmount > 96000) {
                // Keep the bounded in-flight queue and its capture-time context intact.
                // A slow response must not continually tear down the connection/ASR history.
                missed('backpressure',pcm.length/16);
                options.status('Audio monitoring gap: server catching up; some audio was skipped'); return;
            }
            flushGap();
            const bytes = new Uint8Array(wav([pcm]));
            const audio = btoa(String.fromCharCode(...bytes));
            pending.push({question, parts: ring.slice(), screenshot: options.snapshot?.() || null});
            try {
                socket.send(JSON.stringify({audio,sequence,questionId:question ? Number(question) : null}));
            } catch {
                pending.pop();
                ready = false;
                missed('connection_lost', pcm.length / 16);
                screenWithoutSocket(ring[ring.length - 1]);
                try { socket.close(); } catch { }
                return;
            }
            if (pending.length === 1) timeout = setTimeout(() => socket.close(),12000);
        };
        connect();
        return () => { flushEvidence(); flushGap(); closed=true; clearTimeout(retry); clearTimeout(timeout); socket?.close();
            source.disconnect(); worklet.disconnect(); mute.disconnect(); context.close();
            document.removeEventListener('pointerdown',resume); document.removeEventListener('keydown',resume); };
    }
    return { start, wav, createAnalysisQueue };
})();



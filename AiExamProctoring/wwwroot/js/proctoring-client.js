window.ProctoringVoice = class {
    constructor() { this.enabled = true; this.generation = 0; this.until = 0; }
    get blocked() { return Date.now() < this.until || !!window.speechSynthesis?.speaking; }
    speak(message, urdu = '', forcedLanguage = null) {
        if (!this.enabled || !window.speechSynthesis || this.blocked) return Promise.resolve();
        let finish;
        const completion = new Promise(resolve => { finish = resolve; });
        this.finishSpeech = finish;
        const generation = ++this.generation;
        const voices = window.speechSynthesis.getVoices?.() || [];
        const urduVoice = voices.find(voice => /^ur(?:-|$)/i.test(voice.lang));
        const text = urdu || (/[؀-ۿ]/.test(message) ? message : 'براہ کرم امتحان کی ہدایات پر عمل کریں۔');
        const englishVoice = voices.find(voice => /^en(?:-|$)/i.test(voice.lang));
        const parts = [];
        let language = 'en-US';
        try { language = localStorage.getItem('proctoring-language') || language; } catch {}
        if (forcedLanguage === 'ur-PK' || forcedLanguage === 'en-US') language = forcedLanguage;
        if (language === 'ur-PK') parts.push({ text, lang: 'ur-PK', voice: urduVoice });
        else parts.push({ text: message, lang: 'en-US', voice: englishVoice });
        // Keep our own audible reminder out of speech/sound observations.
        const next = () => {
            if (generation !== this.generation || !this.enabled) return;
            const part = parts.shift();
            if (!part) { this.until = Date.now() + 2500; finish(); return; }
            const utterance = new SpeechSynthesisUtterance(part.text);
            utterance.lang = part.lang; utterance.rate = .92;
            if (part.voice) utterance.voice = part.voice;
            this.until = Date.now() + 45000;
            let finished = false;
            utterance.onend = utterance.onerror = () => {
                if (finished) return;
                finished = true;
                next();
            };
            window.speechSynthesis.speak(utterance);
        };
        next();
        return completion;
    }
    stop() { this.generation++; window.speechSynthesis?.cancel(); this.finishSpeech?.(); this.until = Date.now() + 2500; }
};

/* Counts and versions are always supplied by the server, never incremented by the browser. */
window.ProctoringRealtime = (() => {
    const form = document.getElementById('examForm');
    const marker = document.querySelector('[data-proctoring-attempt]');
    const attemptId = Number(form?.dataset.attempt || marker?.dataset.proctoringAttempt || 0);
    const blockedPage = marker?.dataset.blocked === 'true';
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/proctoring')
        .withAutomaticReconnect([0, 1000, 3000, 10000]).build();
    let version = -1, blocked = blockedPage, stopped = false, starting;
    const messages = {
        IdentityMismatch: ["Identity mismatch confirmed after fresh rechecks. Your examination is blocked.", "دوبارہ جانچ کے بعد شناخت میں فرق کی تصدیق ہوئی ہے۔ آپ کا امتحان بند کر دیا گیا ہے۔"],
        FaceNotDetected: ['Your face has remained outside the camera view. Please return and keep your face visible.', 'آپ کا چہرہ کچھ دیر سے کیمرے میں نظر نہیں آ رہا۔ براہ کرم کیمرے کے سامنے واپس آئیں۔'],
        MultipleFaces: ["More than one person was detected in the camera view. Please ask other people to move out of view; only the candidate should remain visible.", "اسکرین پر ایک سے زیادہ افراد نظر آ رہے ہیں۔ براہ کرم دوسرے افراد کو ہٹائیں، ورنہ امتحان بند ہو جائے گا۔"],
        TalkingDetected: ["Violation: Talking during exam is against rules!", "آپ رول کے خلاف جا رہے ہیں، آپ امتحان کے دوران بات کر رہے ہیں۔"],
        HeadAway: ["Looking away detected. Please focus on the exam screen.", "براہ کرم ادھر ادھر نہ دیکھیں، امتحان کی اسکرین پر توجہ دیں۔"],
        GazeAway: ["Looking away detected. Please focus on the exam screen.", "براہ کرم ادھر ادھر نہ دیکھیں، امتحان کی اسکرین پر توجہ دیں۔"],
        LookAway: ['Keep your attention on the examination.', 'اپنی توجہ امتحان پر رکھیں۔'],
        ElectronicDeviceDetected: ["Warning: Banned object detected in camera view. Remove it immediately.", "ممنوعہ چیز نظر آئی ہے۔ براہ کرم اسے ہٹائیں، ورنہ امتحان بند ہو جائے گا۔"],
        AnswerPromptSpeech: ["CRITICAL VIOLATION: Exam Question Discussion Detected!", "آپ نے امتحان کے سوال کے بارے میں بات کی ہے۔ یہ سنگین خلاف ورزی ہے، آپ کا ٹیسٹ بلاک ہو سکتا ہے۔"]
    };
    for (const [direction, urdu] of Object.entries({LEFT: 'بائیں', RIGHT: 'دائیں', UP: 'اوپر', DOWN: 'نیچے', TILTED: 'جھکا ہوا', LEFT_UP: 'بائیں اوپر', LEFT_DOWN: 'بائیں نیچے', RIGHT_UP: 'دائیں اوپر', RIGHT_DOWN: 'دائیں نیچے'})) {
        messages['GAZE_' + direction] = [`Please keep your eyes on the exam screen. Looking ${direction.toLowerCase().replaceAll('_', ' ')} was detected.`, `آپ ${urdu} دیکھ رہے ہیں۔ براہ کرم امتحان کی اسکرین پر نظر رکھیں۔`];
        messages['HEAD_' + direction] = [`Please face the exam screen. Head movement ${direction.toLowerCase()} was detected.`, `آپ کا سر ${urdu} ہے۔ براہ کرم اسکرین کی طرف رخ کریں۔`];
    }
    messages.TabSwitch = ['Please return to the exam. Leaving the exam page was detected.', 'براہ کرم امتحان کے صفحے پر واپس آئیں۔'];
    messages.FullScreenExit = ['Please return to fullscreen examination mode.', 'براہ کرم فل اسکرین امتحان پر واپس آئیں۔'];
    messages.CANDIDATE_SPEECH = messages.TalkingDetected;
    messages.SPEECH_DETECTED = messages.TalkingDetected;
    messages.QUESTION_RELATED_SPEECH = messages.AnswerPromptSpeech;
    messages.MULTIPLE_FACES = messages.MultipleFaces;
    messages.NO_FACE = messages.FaceNotDetected;
    const objectNames = {
        MOBILE_PHONE: ['Mobile phone', 'موبائل فون'], TABLET: ['Tablet', 'ٹیبلٹ'],
        SMART_WATCH: ['Smartwatch', 'اسمارٹ گھڑی'], CALCULATOR: ['Calculator', 'کیلکولیٹر'],
        BOOK: ['Book', 'کتاب'], NOTES_PAPER: ['Notes or paper', 'نوٹس یا کاغذ'],
        EARPHONES: ['Earphones', 'ایئر فون'], HEADPHONES: ['Headphones', 'ہیڈ فون'],
        MICROPHONE: ['Microphone', 'مائیکروفون'], BOTTLE: ['Bottle', 'بوتل'],
        LAPTOP: ['Additional laptop', 'اضافی لیپ ٹاپ'], MONITOR: ['Monitor', 'مانیٹر'],
        PEN_PENCIL: ['Pen or pencil', 'قلم یا پنسل'], BAG: ['Bag', 'بیگ']
    };
    for (const [code, names] of Object.entries(objectNames)) {
        messages['OBJECT_' + code] = [`${names[0]} detected in the camera view. This item is not allowed under this exam's monitoring rules. Please remove it.`, `کیمرے میں ${names[1]} نظر آیا ہے۔ اس امتحان کے قواعد کے مطابق یہ چیز ممنوع ہے۔ براہ کرم اسے ہٹائیں۔`];
    }
    function reasonFor(state) {
        if (state.eventCode === 'DEVICE_HOLD') return ['Monitoring stopped because of a camera or microphone issue. Your exam is paused for technical review, not a cheating finding.', 'کیمرہ یا مائیکروفون کے مسئلے کی وجہ سے امتحان روک دیا گیا ہے۔ یہ تکنیکی مسئلہ ہے، نقل کا فیصلہ نہیں۔ استاد سے رابطہ کریں۔'];
        if (state.eventCode === 'DEVICE_WARNING') return ['Please check your camera, microphone and lighting. This is a technical warning, not a cheating strike.', 'کیمرہ، مائیکروفون اور روشنی چیک کریں۔ یہ تکنیکی تنبیہ ہے۔'];
        if (state.violationType === 'MultipleFaces' || state.eventCode === 'MULTIPLE_FACES') {
            const name = state.candidateName || 'the registered candidate';
            return [`Warning: Multiple persons detected. Only ${name} is allowed!`, `${name}، کیمرے میں ایک سے زیادہ افراد نظر آ رہے ہیں۔ صرف امیدوار کیمرے میں موجود رہے۔`];
        }
        if (state.violationType === 'FaceNotDetected') return [
            'The candidate has not been visible for 10 seconds. Please return to the camera view.',
            'امیدوار دس سیکنڈ سے کیمرے میں نظر نہیں آ رہا۔ براہ کرم واپس آئیں۔'];
        if (state.violationType === 'AnswerPromptSpeech' || state.eventCode === 'QUESTION_RELATED_SPEECH') return [
            'Critical Warning: Exam question discussion detected!',
            'امتحان کے سوال سے متعلق گفتگو سنائی دی ہے۔ براہ کرم ایسی گفتگو بند کریں۔ استاد کو اطلاع دی گئی ہے۔'];
        if (state.violationType === 'IdentityMismatch') return [
            state.status === 'ProctoringBlocked' ? 'Identity mismatch confirmed. The exam has been blocked.' : 'Identity mismatch confirmed. Your teacher has been notified for urgent review.',
            state.status === 'ProctoringBlocked' ? 'شناخت میں فرق کی تصدیق ہوئی ہے۔ امتحان روک دیا گیا ہے۔' : 'شناخت میں فرق کی تصدیق ہوئی ہے۔ استاد کو فوری جائزے کے لیے اطلاع دی گئی ہے۔'];
        if (state.eventCode?.startsWith('OBJECT_')) {
            const key = state.eventCode.slice(7);
            const names = objectNames[key] || [key.toLowerCase().replaceAll('_', ' '), key.toLowerCase().replaceAll('_', ' ')];
            return [`Warning: Banned object detected: ${names[0]}. Remove it immediately!`, `${names[1]} کیمرے میں نظر آیا ہے۔ براہ کرم اسے ہٹائیں۔`];
        }
        return messages[state.eventCode] || messages[state.violationType] ||
            ['An exam rule violation was recorded. Please follow your instructor’s guidance.', 'امتحان کے قاعدے کی خلاف ورزی ریکارڈ ہوئی ہے۔ براہ کرم استاد کی ہدایات پر عمل کریں۔'];
    }
    let banner, overlay, modal;
    if (attemptId && !blockedPage) {
        const bar = document.createElement('div');
        bar.className = 'alert alert-light border';
        bar.innerHTML = '<span>Urdu voice · English warnings</span> <strong class="strike-count">0/3</strong><div class="strike-message" role="alert" aria-live="assertive"></div>';
        (form || marker).before(bar);
        banner = bar;
        overlay = document.createElement('div'); overlay.hidden = true;
        overlay.setAttribute('role', 'alertdialog'); overlay.setAttribute('aria-modal', 'true');
        overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;background:#101827f5;color:white;padding:12vh 8vw;text-align:center;';
        overlay.innerHTML = '<h1>Exam Blocked due to multiple violations or candidate swap.</h1><p class="lock-reason"></p><p>Your timer and saved answers are frozen. Only your instructor can authorize resume.</p><p role="status">Connecting to your teacher…</p><a class="btn btn-light" href="/Student/Exam/Available">Exit exam</a>';
        document.body.append(overlay);
        modal = document.createElement('dialog');
        modal.innerHTML = '<h2>Strike 2 — strict warning</h2><p></p><button type="button">I understand / میں سمجھ گیا ہوں</button>';
        modal.querySelector('button').onclick = () => modal.close();
        modal.addEventListener('cancel', event => event.preventDefault());
        document.body.append(modal);
        form?.addEventListener('submit', event => { if (blocked) { event.preventDefault(); event.stopImmediatePropagation(); } }, true);
    }
    function apply(state, announce = true) {
        if (!state || state.attemptId !== attemptId || state.version < version) return;
        // Equal-version or delayed messages cannot release a persisted lock.
        if (blocked && state.status !== 'ProctoringBlocked' && state.version <= version) return;
        const fresh = state.version > version;
        version = state.version;
        const wasBlocked = blocked;
        const manualOnly = state.blockingMode === 'MANUAL_ONLY';
        const submitted = ['Completed','AutoSubmitted','Disqualified'].includes(state.status);
        blocked = state.status === 'ProctoringBlocked' || submitted;
        if (submitted) {
            if(form)form.inert=true;
            const retry=document.getElementById('verifyEntry');if(retry){retry.hidden=true;retry.disabled=true;}
            if(overlay){overlay.hidden=false;overlay.innerHTML='<h1>Exam submitted</h1><p>Your saved answers have been submitted. This attempt cannot be resumed. Flagged results await instructor review.</p><a href="/Student/Result" style="color:white">Open my results</a>';}
            window.dispatchEvent(new CustomEvent('proctoring-state',{detail:state}));
            if(fresh&&announce){const v=window.ProctoringVoiceInstance ||= new ProctoringVoice();v.stop();v.until=0;v.speak('Your exam has been submitted. Your instructor will review the result.','آپ کا امتحان جمع ہو گیا ہے۔ استاد نتیجے کا جائزہ لیں گے۔','ur-PK');}
            return;
        }
        if (banner) banner.querySelector('.strike-count').textContent = blocked ? 'BLOCKED' : manualOnly ? `${state.strikeCount} recorded · Teacher decision` : `${Math.min(3, state.strikeCount)}/3`;
        if (overlay) {
            overlay.hidden = !blocked;
            overlay.querySelector('h1').textContent = state.eventCode === 'DEVICE_HOLD' ? 'Exam paused for a device check' : 'Exam blocked — instructor review required';
            overlay.querySelector('.lock-reason').textContent = state.eventCode === 'DEVICE_HOLD' ? 'Camera or microphone monitoring could not continue. This is a technical hold, not a cheating finding. Contact your teacher.' : state.strikeCount >= 4
                ? `${reasonFor(state)[0]} Your exam has been locked due to exceeding maximum allowed violations (3/3). Please contact your instructor/invigilator.`
                : state.violationType === 'IdentityMismatch' ? messages.IdentityMismatch[0] : 'Your exam has been locked for instructor review. Please contact your instructor/invigilator.';
            overlay.querySelector('[role="status"]').textContent = `Frozen time: ${state.remainingTimeSeconds} seconds. Your saved answers are retained.`;
            if (blocked && modal?.open) modal.close();
        }
        if (form) form.inert = blocked;
        const identityForm = document.getElementById('entryIdentityForm');
        if (identityForm) identityForm.inert = blocked;
        const retry = document.getElementById('verifyEntry');
        if (blocked && retry) { retry.hidden = true; retry.disabled = true; }
        window.dispatchEvent(new CustomEvent('proctoring-state', { detail: state }));
        if (!blocked && (wasBlocked || blockedPage) && state.status === 'InProgress') { location.replace(state.resumeUrl); return; }
        if (!fresh || !announce || (!blocked && !state.strikeCount && state.violationType !== 'IdentityMismatch')) return;
        const copy = reasonFor(state);
        const english = blocked ? `${copy[0]} The examination is locked.` : `${manualOnly ? 'Teacher review required.' : `Strike ${state.strikeCount} of 3.`} ${copy[0]} ${!manualOnly && state.strikeCount === 3 ? (state.violationAction === 'FinalSubmit' ? 'Final warning. The next violation submits your exam.' : 'Final warning. The next violation locks your exam.') : ''}`;
        const urdu = state.violationType === 'IdentityMismatch' ? copy[1] : blocked ? `${copy[1]} امتحان روک دیا گیا ہے۔` : `تنبیہ ${state.strikeCount}۔ ${copy[1]} ${state.blockingMode !== 'MANUAL_ONLY' && state.strikeCount === 3 ? (state.violationAction === 'FinalSubmit' ? 'آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان جمع ہو جائے گا۔' : 'آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان بند ہو جائے گا۔') : ''}`;
        const text = english;
        if (banner) { banner.querySelector('.strike-message').textContent = text; banner.className = `alert ${state.violationType === 'TalkingDetected' || state.violationType === 'AnswerPromptSpeech' || state.strikeCount >= 3 || blocked ? 'alert-danger' : 'alert-warning'}`; }
        if (!blocked && state.strikeCount === 2 && modal) { modal.querySelector('p').textContent = text; if (!modal.open) modal.showModal(); }
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0; voice.enabled = true;
        voice.speak(english, urdu, 'ur-PK');
    }
    const enrichedEvents = new Set();
    connection.on('SpeechContextUpdated', state => {
        if (!state || state.attemptId !== attemptId || blocked || state.version !== version ||
            enrichedEvents.has(state.eventId)) return;
        enrichedEvents.add(state.eventId);
        if (enrichedEvents.size > 32) enrichedEvents.delete(enrichedEvents.values().next().value);
        const copy = reasonFor({ ...state, violationType: "AnswerPromptSpeech" });
        const ending = state.blockingMode !== 'MANUAL_ONLY' && state.strikeCount === 3 ? (state.violationAction === 'FinalSubmit' ? ' Final warning. The next violation submits your exam.' : ' Final warning. The next violation locks your exam.') : '';
        if (banner) banner.querySelector('.strike-message').textContent = `${state.blockingMode === 'MANUAL_ONLY' ? 'Teacher review required.' : `Strike ${state.strikeCount} of 3.`} ${copy[0]}${ending}`;
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0;
        voice.speak(copy[0] + ending, copy[1] + (state.strikeCount === 3 ? (state.violationAction === 'FinalSubmit' ? ' آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان جمع ہو جائے گا۔' : ' آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان بند ہو جائے گا۔') : ''), 'ur-PK');
    });
    const softSpeechIds = new Set();
    function speechReminder(id) {
        if (!id || blocked || softSpeechIds.has(id)) return;
        softSpeechIds.add(id);
        const english = 'Please do not talk during the examination.';
        if (banner) { banner.querySelector('.strike-message').textContent = english; banner.className = 'alert alert-warning'; }
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0; voice.enabled = true;
        voice.speak(english, 'براہ کرم خاموش رہیں، ٹیسٹ کے دوران بات نہ کریں۔', 'ur-PK');
    }
    connection.on('SpeechReminder', state => { if (state.attemptId === attemptId && state.version >= version) { version = state.version; speechReminder(state.eventId); } });
    ['StrikeIssued', 'ExamLocked', 'ExamSubmitted', 'ResumeExamSignal', 'UnblockSignal'].forEach(name => connection.on(name, state => apply(state)));
    async function sync() {
        if (!attemptId) return;
        try {
            const response = await fetch(`/proctoring-state/${attemptId}`, { cache: 'no-store', signal: AbortSignal.timeout(8000) });
            if (response.ok) apply(await response.json());
        } catch { /* Persisted state is retried; server rejects locked writes regardless of the UI. */ }
    }
    async function join() { if (attemptId) apply(await connection.invoke('JoinAttempt', attemptId)); }
    async function start() {
        if (starting || stopped || connection.state !== signalR.HubConnectionState.Disconnected) return starting;
        starting = (async () => { try { await connection.start(); await join(); } catch { if (!stopped) setTimeout(start, 5000); } finally { starting = null; } })();
        return starting;
    }
    connection.onreconnected(async () => { try { await join(); } catch { await sync(); } });
    connection.onclose(() => { if (!stopped) setTimeout(start, 5000); });
    window.addEventListener('pagehide', () => { stopped = true; connection.stop(); });
    start(); if (attemptId) { sync(); setInterval(sync, 5000); }
    return { connection, apply, sync, speechReminder, get blocked() { return blocked; } };
})();

/* Independent capture/inference lanes. SQL state, never this module, owns strikes. */
window.ExamProctoring = {
    start(config) {
        const form = document.getElementById('examForm');
        const token = form.querySelector('[name="__RequestVerificationToken"]').value;
        const video = document.getElementById('proctorCamera');
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        const lifetime = new AbortController();
        const timers = new Set();
        let stream, evidence, stopAudio, stopSharing, stopped = false, identityBusy = false;
        let behaviorReady = false, calibrationBusy = false;
        const active = () => !stopped && !config.submitted() && !window.ProctoringRealtime.blocked;
        const status = (id, text, warning = false) => {
            const node = document.getElementById(id);
            if (node) { node.textContent = text; node.className = warning ? 'ai-status-pill ai-status-warning' : 'ai-status-pill ai-status-normal'; }
        };
        const later = (fn, ms) => {
            if (stopped) return;
            const timer = setTimeout(() => { timers.delete(timer); if (!stopped) fn(); }, ms);
            timers.add(timer);
        };
        const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
        const body = () => {
            const data = new FormData();
            data.append('AttemptId', config.attemptId); data.append('SessionAttemptId', config.sessionId);
            data.append('__RequestVerificationToken', token);
            return data;
        };
        async function request(action, data, timeout = 30000) {
            const response = await fetch(config.urls[action], { method: 'POST', body: data,
                credentials: 'same-origin', signal: AbortSignal.any([lifetime.signal, AbortSignal.timeout(timeout)]) });
            const result = await response.json();
            if (result.state) window.ProctoringRealtime.apply(result.state);
            if (!response.ok) throw Object.assign(new Error(result.message || `${action}: HTTP ${response.status}`), { status: response.status });
            if (result.logIds?.length) evidence?.attach(result.logIds);
            return result;
        }
        async function frame(maximumDimension = 640) {
            if (!video.videoWidth) throw new Error('Camera frame unavailable');
            const canvas = document.createElement('canvas');
            const ratio = Math.min(1, maximumDimension / Math.max(video.videoWidth, video.videoHeight));
            canvas.width = Math.round(video.videoWidth * ratio); canvas.height = Math.round(video.videoHeight * ratio);
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', .88));
            if (!blob) throw new Error('Camera encoding failed');
            return blob;
        }
        let departureSnapshot = null;
        async function report(type, description) {
            if (!active()) return;
            const data = body();
            data.append('AlertType', type); data.append('Description', description);
            data.append('ClientEventId', crypto.randomUUID());
            data.append('EvidenceDataUrl', (type === 'TabSwitch' && !evidence?.recorder.status().cameraOnly ? evidence?.screenshot() : departureSnapshot) || evidence?.screenshot() || '');
            let result;
            for (let attempt = 0; attempt < 3; attempt++) {
                try { result = await request('log', data); break; }
                catch (error) {
                    if (attempt === 2 || lifetime.signal.aborted || (error.status && error.status < 500)) throw error;
                    await delay(500 * (attempt + 1));
                }
            }
            // The same event ID and captured image survive transient upload failures.
            if (result.logId) evidence?.attach([result.logId]);
            await window.ProctoringRealtime.sync();
        }
        const guard = ExamBrowserGuard.create({ now: () => Date.now(),
            state: () => ({ active: active(), hidden: document.hidden, focused: document.hasFocus(),
                fullscreen: !!document.fullscreenElement, requireFullscreen: true }),
            report: (type, text) => report(type, text).catch(error => status('mediaStatus', error.message, true)) });
        async function fullscreen() {
            if (!active() || document.fullscreenElement) return;
            try { await document.documentElement.requestFullscreen({ navigationUI: 'hide' }); }
            catch { status('fullscreenStatusPill', 'Click to enter fullscreen', true); }
        }
        function on(target, event, handler) { target?.addEventListener(event, handler, { signal: lifetime.signal }); }
        on(document, 'pointerdown', fullscreen);
        on(document, 'keydown', fullscreen);
        on(document, 'visibilitychange', () => {
            if (document.hidden && active()) departureSnapshot = evidence?.screenshot() || null;
            guard.sample();
            document.getElementById('examForm')?.classList.toggle('exam-content-hidden', document.hidden);
        });
        on(document, 'fullscreenchange', () => guard.sample());
        const protectionStyle = document.createElement('style');
        protectionStyle.textContent = '#examForm .question-card {user-select:none;-webkit-user-select:none} #examForm.exam-content-hidden .question-card, #examForm.exam-capture-mask .question-card {visibility:hidden} @media print {body {visibility:hidden!important}}';
        document.head.appendChild(protectionStyle);
        lifetime.signal.addEventListener('abort', () => protectionStyle.remove(), {once:true});
        function questionTarget(target) {
            const element = target?.nodeType === 3 ? target.parentElement : target;
            return !!element?.closest?.('#examForm .question-card');
        }
        for (const event of ['copy', 'cut', 'contextmenu', 'dragstart']) on(document, event, e => {
            const selection = document.getSelection?.();
            if (active() && (questionTarget(e.target) || questionTarget(selection?.anchorNode) || questionTarget(selection?.focusNode))) {
                e.preventDefault();
                if (event === 'copy' || event === 'cut') e.clipboardData?.setData('text/plain', '');
                status('mediaStatus', 'Copying exam questions is disabled.', true);
            }
        });
        on(document, 'keydown', e => {
            if (!active()) return;
            if ((e.ctrlKey || e.metaKey) && ['p', 's'].includes(e.key.toLowerCase())) {
                e.preventDefault(); status('mediaStatus', 'Printing and saving exam questions are disabled.', true);
            }
            // Best effort only: OS screenshot tools may intercept this key before JS.
            if (e.key === 'PrintScreen' || (e.metaKey && e.shiftKey && ['3', '4', '5'].includes(e.key))) {
                e.preventDefault();
                document.getElementById('examForm')?.classList.toggle('exam-capture-mask', true);
                later(() => document.getElementById('examForm')?.classList.toggle('exam-capture-mask', false), 1500);
                status('mediaStatus', 'Please do not capture or share exam questions.', true);
            }
        });
        for (const event of ['blur', 'focus']) on(window, event, () => {
            // Hide only question content while an OS tool/window has focus. Blur
            // remains a privacy measure, never sufficient evidence for a strike.
            document.getElementById('examForm')?.classList.toggle('exam-content-hidden', document.hidden || !document.hasFocus());
            guard.sample();
        });
        const guardTick = () => { guard.sample(); later(guardTick, 100); }; guardTick();
        const attached = new Set();
        on(window, 'proctoring-state', event => {
            const state = event.detail;
            if (state.attemptId !== Number(config.attemptId) || !state.eventId || attached.has(state.eventId)) return;
            if (evidence) { attached.add(state.eventId); evidence.attach([state.eventId]); }
        });
        let lastVideoTime = -1, lastVideoAdvance = performance.now();
        async function deviceHealth() {
            if (!active() || !stream || !config.urls.health || document.hidden) return;
            let issue = 'healthy';
            if (video.currentTime !== lastVideoTime) { lastVideoTime=video.currentTime; lastVideoAdvance=performance.now(); }
            const camera = stream.getVideoTracks()[0], mic = stream.getAudioTracks()[0];
            if (!camera || camera.readyState !== 'live' || camera.muted || performance.now()-lastVideoAdvance > 6000) issue = 'camera_disconnected';
            else if (!mic || mic.readyState !== 'live' || mic.muted) issue = 'microphone_disconnected';
            else if (video.videoWidth && video.readyState >= 2) {
                const c = document.createElement('canvas'); c.width=32;c.height=24;
                const context=c.getContext('2d',{willReadFrequently:true}); context.drawImage(video,0,0,32,24);
                const pixels=context.getImageData(0,0,32,24).data;let sum=0;
                for(let i=0;i<pixels.length;i+=4)sum+=(pixels[i]+pixels[i+1]+pixels[i+2])/3;
                if(sum/(pixels.length/4)<12)issue='camera_dark';
            }
            const data=body();data.append('issue',issue);
            const result=await request('health',data,8000);
            if(result.warning) {
                const message=`Device warning ${result.warningCount}/3: ${issue.replaceAll('_',' ')}. Please check your camera, microphone and lighting. This is not a cheating strike.`;
                status('mediaStatus',message,true);
                voice.speak(message,'کیمرہ، مائیکروفون اور روشنی چیک کریں۔ مسئلہ برقرار رہنے پر استاد سے رابطہ کریں۔','ur-PK');
                if(result.state?.eventId)evidence?.attach([result.state.eventId]);
            } else if(issue==='healthy') status('mediaStatus','Camera and microphone connected');
        }
        async function identity() {
            if (!active() || identityBusy || !stream) return;
            identityBusy = true;
            let retry = false;
            try {
                const data = body();
                for (let i = 0; i < 4; i++) {
                    data.append('Frames', await frame(), `identity-${i}.jpg`);
                    if (i + 1 < 4) await delay(150);
                }
                const result = await request('identity', data, 50000);
                retry = result.confirmationPending === true;
                status('identityMonitorStatus', retry ? 'Checking identity again with fresh frames' : result.verified ? 'Identity verified' : result.message || 'Identity needs teacher review', !result.verified);
            } catch (error) { status('identityMonitorStatus', error.message, true); }
            finally { identityBusy = false; if (retry) later(identity, 500); }
        }
        async function calibrate(screenCalibration = false) {
            if (!active() || calibrationBusy) return;
            const previousReady = behaviorReady;
            calibrationBusy = true; behaviorReady = false;
            let guide;
            try {
                status('behaviorMonitorStatus', 'Look at the screen centre for calibration');
                const data = body();
                for (let i = 0; i < config.baselineFrames; i++) {
                    data.append('Frames', await frame(), `baseline-${i}.jpg`); await delay(150);
                }
                if (screenCalibration) {
                    data.append('ScreenCalibration', 'true');
                    guide = document.createElement('div');
                    guide.style.cssText = 'position:fixed;inset:0;z-index:10000;background:#f8fafcf5';
                    const dot = document.createElement('span');
                    dot.style.cssText = 'position:absolute;width:24px;height:24px;border-radius:50%;background:#1266e8';
                    const label = document.createElement('p'); label.textContent = 'Keep your head still and follow the blue dot with your eyes.';
                    guide.append(label, dot); document.body.append(guide);
                    for (const [x, y] of [[10, 10], [90, 10], [90, 90], [10, 90]]) {
                        dot.style.left = `${x}%`; dot.style.top = `${y}%`; await delay(1000);
                        for (let i = 0; i < 2; i++) { data.append('Frames', await frame(), `corner-${x}-${y}-${i}.jpg`); await delay(100); }
                    }
                }
                const result = await request('baseline', data);
                behaviorReady = result.started === true;
                if (!behaviorReady) throw new Error('Calibration needs a clear face');
                status('behaviorMonitorStatus', 'Head pose and iris tracking active');
            } catch (error) {
                behaviorReady = previousReady;
                status('behaviorMonitorStatus', error.message, true);
                if (!behaviorReady) later(calibrate, 3000);
            }
            finally { guide?.remove(); calibrationBusy = false; }
        }
        on(document.getElementById('calibrateScreenView'), 'click', () => calibrate(true));
        // A lane schedules its next fresh capture after completion. No shared busy flag,
        // no stale frame backlog, and a slow detector never stalls another detector.
        function lane(fn, interval, id) {
            const run = async () => {
                const started = performance.now();
                try { if (active()) await fn(); }
                catch (error) { status(id, error.message, true); }
                finally { later(run, Math.max(50, interval - (performance.now() - started))); }
            };
            run();
        }
        async function videoModel(action, id) {
            if (action === 'behavior' && !behaviorReady) return;
            const data = body(); data.append('Frame', await frame(action === 'objects' ? 1280 : 640), `${action}.jpg`);
            try {
                const result = await request(action, data);
                status(id, result.detections?.length ? `Detected: ${result.detections.map(x => x.label).join(', ')}` : `${action} monitoring active`);
            } catch (error) {
                if (action === 'behavior' && error.status === 409) { behaviorReady = false; calibrate(); }
                throw error;
            }
        }
        let asrBusy = false, nextTranscript;
        async function transcribe(packet) {
            nextTranscript = packet;
            if (asrBusy) { status('speechMonitorStatus', 'Transcription catching up; speech detection remains active', true); return; }
            asrBusy = true;
            try {
                while (nextTranscript && active()) {
                    const current = nextTranscript; nextTranscript = null;
                    const data = body(); data.append('Audio', current.clip, 'speech.wav');
                    if (current.detectionLogId) data.append('DetectionLogId', current.detectionLogId);
                    if (current.questionScreenshot) data.append('QuestionScreenshotDataUrl', current.questionScreenshot);
                    if (current.questionId) data.append('QuestionId', current.questionId);
                    current.questionIds.forEach(id => data.append('QuestionIds', id));
                    try {
                        const result = await request('transcribe', data, 30000);
                        if (result.questionRelated) {
                            status('speechMonitorStatus', 'Exam question discussion detected', true);
                            await window.ProctoringRealtime.sync();
                        }
                        else if (result.semanticReady === false) status('speechMonitorStatus', 'Speech heard, but question matching is unavailable. Tell your teacher.', true);
                        else status('speechMonitorStatus', result.transcript ? 'Speech analyzed; no confirmed question match' : 'Speech heard; waiting for clearer words');
                    } catch (error) { status('speechMonitorStatus', error.message, true); }
                }
            } finally { asrBusy = false; }
        }
        async function audio() {
            if (!active() || !stream?.getAudioTracks().length) return;
            try {
                const url = new URL(config.urls.speech, location.href);
                url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
                url.searchParams.set('attemptId', config.attemptId); url.searchParams.set('sessionAttemptId', config.sessionId);
                stopAudio = await ExamSpeechStream.start({ stream, url: url.href, token, question: config.question,
                    suppressed: () => !active() || voice.blocked,
                    snapshot: () => evidence && !evidence.recorder.status().cameraOnly && !document.hidden ? evidence.screenshot() : null,
                    status: text => status('speechMonitorStatus', text), transcribe,
                    result: result => {
                        if (result.state) window.ProctoringRealtime.apply(result.state, !result.softReminderId);
                        if (result.softReminderId) window.ProctoringRealtime.speechReminder(result.softReminderId);
                        evidence?.attach(result.logIds || []);
                    } });
                if (stopped) stopAudio();
            } catch (error) { status('speechMonitorStatus', error.message, true); later(audio, 3000); }
        }
        async function startMedia() {
            const resumeGuard = guard.suspend();
            try {
                stream = new MediaStream();
                const acquire = async (constraints, start) => {
                    const source = await navigator.mediaDevices.getUserMedia(constraints);
                    if (stopped) { source.getTracks().forEach(track => track.stop()); return; }
                    source.getTracks().forEach(track => stream.addTrack(track));
                    await start();
                };
                const results = await Promise.allSettled([
                    acquire({ video: { width: { ideal: 1280 }, height: { ideal: 720 } } }, async () => {
                        video.srcObject = stream; await video.play();
                        lane(identity, 10000, 'identityMonitorStatus');
                        calibrate(); lane(() => videoModel('behavior', 'behaviorMonitorStatus'), 250, 'behaviorMonitorStatus');
                        lane(() => videoModel('objects', 'objectMonitorStatus'), 350, 'objectMonitorStatus');
                    }),
                    acquire({ audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 } }, audio)
                ]);
                if (stopped) return;
                const failed = results.filter(result => result.status === 'rejected');
                status('mediaStatus', failed.length ? 'A required media device is unavailable. Contact your teacher.' : 'Camera and microphone active', failed.length > 0);
                for (const track of stream.getTracks()) on(track, 'ended', () => {
                    if (!stopped) deviceHealth().catch(error => status('mediaStatus', error.message, true));
                });
                stopSharing = LiveProctoring.student(config.attemptId, token, () => stopped ? null : stream, () => {});
                const { ObservationEvidence } = await import('/js/proctoring/observation-evidence.js');
                if (stopped) return;
                evidence = new ObservationEvidence({ stream, token, attemptId: config.attemptId, sessionAttemptId: config.sessionId,
                    uploadUrl: config.urls.evidence, recordingUrl: config.urls.recording,
                    onStatus: text => status('evidenceStatus', text) });
                await evidence.start();
                const screenButton = document.createElement('button');
                screenButton.type = 'button'; screenButton.className = 'btn btn-outline-primary btn-sm mt-2';
                screenButton.textContent = 'Share entire screen for exam evidence';
                document.getElementById('evidenceStatus')?.after(screenButton);
                on(screenButton, 'click', async () => {
                    const resume = guard.suspend(); screenButton.disabled = true;
                    try {
                        await evidence.recorder.enableScreen();
                        const surface = evidence.recorder.screenStream?.getVideoTracks()[0]?.getSettings().displaySurface;
                        status('evidenceStatus', surface === 'monitor' ? 'Shared desktop, camera and microphone recording active' : 'Only the selected window/tab is recorded. Choose Entire screen to include other visible apps.');
                        screenButton.textContent = 'Change shared screen';
                    } catch (error) { status('evidenceStatus', 'Screen sharing unavailable or cancelled. Camera recording continues.', true); }
                    finally { screenButton.disabled = false; resume(); }
                });
                lifetime.signal.addEventListener('abort', () => screenButton.remove(), { once: true });
                lane(deviceHealth, 3000, 'mediaStatus');
                await window.ProctoringRealtime.sync();
            } catch (error) { status('mediaStatus', `Monitoring needs attention: ${error.message}`, true); }
            finally { resumeGuard(); }
        }
        function stop() {
            if (stopped) return;
            stopped = true; lifetime.abort(); timers.forEach(clearTimeout); timers.clear();
            stopAudio?.(); stopSharing?.(); evidence?.stop(); voice.stop();
            stream?.getTracks().forEach(track => track.stop());
        }
        on(window, 'pagehide', stop);
        startMedia();
        return { identity, stop };
    }
};

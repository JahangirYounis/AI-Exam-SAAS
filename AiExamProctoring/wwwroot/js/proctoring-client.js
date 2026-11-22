
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
        try { language = localStorage.getItem('proctoring-language') || language; } catch { }
        if (forcedLanguage === 'ur-PK' || forcedLanguage === 'en-US') language = forcedLanguage;
        if (language === 'ur-PK' && urduVoice) parts.push({ text, lang: 'ur-PK', voice: urduVoice });
        else if (language === 'ur-PK') {
            window.dispatchEvent?.(new CustomEvent('proctoring-voice-unavailable', { detail: { language: 'ur-PK' } }));
            parts.push({ text: message, lang: 'en-US', voice: englishVoice });
        }
        else parts.push({ text: message, lang: 'en-US', voice: englishVoice });
        // Keep our own audible reminder out of speech/sound observations.
        const next = () => {
            if (generation !== this.generation || !this.enabled) return;
            const part = parts.shift();
            if (!part) { this.until = Date.now() + 2500; finish(); return; }
            const utterance = new SpeechSynthesisUtterance(part.text);
            utterance.lang = part.lang; utterance.rate = .92;
            if (part.voice) utterance.voice = part.voice;
            this.until = Date.now() + 10000;
            let finished = false;
            const watchdog = typeof setTimeout === 'function' ? setTimeout(() => {
                if (!finished) { window.speechSynthesis.cancel(); utterance.onerror(); }
            }, 10000) : null;
            utterance.onend = utterance.onerror = () => {
                if (finished) return;
                finished = true;
                if (watchdog !== null) clearTimeout(watchdog);
                next();
            };
            window.speechSynthesis.speak(utterance);
        };
        next();
        return completion;
    }
    stop() { this.generation++; window.speechSynthesis?.cancel(); this.finishSpeech?.(); this.until = Date.now() + 2500; }
};

function behaviorCopy(observation) {
    if (observation.type === 'FaceNotDetected') return [`You have been outside the camera view for ${Number(observation.duration || 3).toFixed(1)} seconds. Please return immediately.`, 'آپ کا چہرہ نظر نہیں آ رہا۔ کیمرے کے سامنے آ جائیں۔'];
    if (observation.type === 'MultipleFaces' || observation.type === 'AdditionalPersonObservation') return ['More than one possible person is visible in the examination area. Only the registered candidate should remain.', 'کیمرے میں ایک سے زیادہ افراد نظر آ رہے ہیں۔'];
    if (['LookAway', 'HeadAway', 'GazeAway'].includes(observation.type)) return ['Please keep your attention on the examination screen.', 'براہ کرم امتحان کی اسکرین پر توجہ دیں۔'];
    return ['Your face is not visible. Please face the camera.', 'آپ کا چہرہ نظر نہیں آ رہا۔ کیمرے کے سامنے آ جائیں۔'];
}

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
        LookingAwaySpeaking: ["You appear to be looking away from the exam screen while speaking. Please focus on your exam and avoid talking.", ""],
        IdentityMismatch: ["Your identity could not be verified against the registered candidate. Please face the camera and complete the required verification.", "دوبارہ جانچ کے بعد شناخت میں فرق کی تصدیق ہوئی ہے۔ آپ کا امتحان بند کر دیا گیا ہے۔"],
        FaceNotDetected: ['Your face has remained outside the camera view. Please return and keep your face visible.', 'آپ کا چہرہ کچھ دیر سے کیمرے میں نظر نہیں آ رہا۔ براہ کرم کیمرے کے سامنے واپس آئیں۔'],
        MultipleFaces: ["More than one person was detected in the camera view. Please ask other people to move out of view; only the candidate should remain visible.", "اسکرین پر ایک سے زیادہ افراد نظر آ رہے ہیں۔ براہ کرم دوسرے افراد کو ہٹائیں، ورنہ امتحان بند ہو جائے گا۔"],
        FaceNotVisible: ["Your face is not visible to the camera. Please return to view. This is a review observation, not a confirmed cheating incident.", "آپ کا چہرہ نظر نہیں آ رہا۔ براہ کرم کیمرے کے سامنے آئیں۔"],
        TalkingDetected: ["Repeated speech was detected near the microphone. Please keep the examination area quiet.", "امتحان کے دوران بات نہ کریں۔"],
        HeadAway: ["Please keep your attention on the examination screen. Looking away may violate exam rules.", "براہ کرم ادھر ادھر نہ دیکھیں، امتحان کی اسکرین پر توجہ دیں۔"],
        GazeAway: ["Please keep your attention on the examination screen. Looking away may violate exam rules.", "براہ کرم ادھر ادھر نہ دیکھیں، امتحان کی اسکرین پر توجہ دیں۔"],
        LookAway: ['Please keep your attention on the examination screen. Looking away may violate exam rules.', 'اپنی توجہ امتحان پر رکھیں۔'],
        ElectronicDeviceDetected: ["Warning: Banned object detected in camera view. Remove it immediately.", "ممنوعہ چیز نظر آئی ہے۔ براہ کرم اسے ہٹائیں، ورنہ امتحان بند ہو جائے گا۔"],
        AnswerPromptSpeech: ["Possible discussion of examination content has been detected. Please stop communicating about exam questions or answers.", ""]
    };
    for (const [direction, urdu] of Object.entries({ LEFT: 'بائیں', RIGHT: 'دائیں', UP: 'اوپر', DOWN: 'نیچے', TILTED: 'جھکا ہوا', LEFT_UP: 'بائیں اوپر', LEFT_DOWN: 'بائیں نیچے', RIGHT_UP: 'دائیں اوپر', RIGHT_DOWN: 'دائیں نیچے' })) {
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
            return [state.reason?.startsWith('Multiple persons still detected') ? state.reason : 'Multiple people have been detected in your examination area. Only the registered student should participate in the exam.', 'کیمرے میں ایک سے زیادہ افراد نظر آ رہے ہیں۔'];
        }
        if (state.violationType === 'ElectronicDeviceDetected' && /^\[OBJECT_[A-Z_]+\]/.test(state.reason || '')) {
            const name = state.reason.replace(/^\[OBJECT_[A-Z_]+\]\s*/, '').split(' across multiple camera frames')[0];
            return [`${name}. Please remove the prohibited item from the camera view.`, messages.ElectronicDeviceDetected[1]];
        }
        if (state.violationType === 'FaceNotDetected') return [
            `${state.reason?.startsWith('Student still absent') ? state.reason : 'Student absent'}. Please return to the camera view.`, 'آپ کا چہرہ نظر نہیں آ رہا۔ کیمرے کے سامنے آ جائیں۔'];
        if (state.violationType === 'AnswerPromptSpeech' || state.eventCode === 'QUESTION_RELATED_SPEECH') return [
            'Possible discussion of examination content has been detected. Please stop communicating about exam questions or answers.',
            'امتحان کے سوال سے متعلق گفتگو سنائی دی ہے۔ براہ کرم ایسی گفتگو بند کریں۔ استاد کو اطلاع دی گئی ہے۔'];
        if (state.violationType === 'IdentityMismatch' || state.eventCode === 'IDENTITY_REVIEW_HOLD') return [
            state.status === 'ProctoringBlocked' ? 'Identity could not be verified. The exam is paused for supervisor review.' : 'Identity could not be verified. Your teacher has been notified for urgent review.',
            state.status === 'ProctoringBlocked' ? 'شناخت کی دوبارہ جانچ ضروری ہے۔ امتحان استاد کے جائزے کے لیے روک دیا گیا ہے۔' : 'شناخت میں ممکنہ فرق ہے۔ استاد کو جائزے کے لیے اطلاع دی گئی ہے۔'];
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
        bar.innerHTML = '<span>Exam warnings </span><strong class="strike-count">…</strong><div class="strike-message" role="alert" aria-live="assertive"></div>';
        (form || marker).before(bar);
        banner = bar;
        overlay = document.createElement('div'); overlay.hidden = true;
        overlay.setAttribute('role', 'alertdialog'); overlay.setAttribute('aria-modal', 'true');
        overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;background:#101827f5;color:white;padding:12vh 8vw;text-align:center;';
        overlay.innerHTML = '<h1>Examination paused for supervisor review</h1><p class="lock-reason"></p><p>Your timer and saved answers are frozen. Only your instructor can authorize resume.</p><p role="status">Connecting to your teacher…</p><a class="btn btn-light" target="_top" href="/Student/Exam/Available">Exit exam</a>';
        document.body.append(overlay);
        modal = document.createElement('dialog');
        modal.innerHTML = '<h2>Strike 2 — strict warning</h2><p></p><button type="button">I understand / میں سمجھ گیا ہوں</button>';
        modal.querySelector('button').onclick = () => modal.close();
        modal.addEventListener('cancel', event => event.preventDefault());
        document.body.append(modal);
        form?.addEventListener('submit', event => { if (blocked) { event.preventDefault(); event.stopImmediatePropagation(); } }, true);
    }
    const announcedWarnings = new Set();
    function apply(state, announce = true) {
        if (!state || state.attemptId !== attemptId || state.version < version) return;
        // Equal-version or delayed messages cannot release a persisted lock.
        if (blocked && state.status !== 'ProctoringBlocked' && state.version <= version) return;
        const fresh = state.version > version;
        version = state.version;
        const wasBlocked = blocked;
        const manualOnly = state.blockingMode === 'MANUAL_ONLY';
        const submitted = ['Completed', 'AutoSubmitted', 'Disqualified'].includes(state.status);
        blocked = state.status === 'ProctoringBlocked' || submitted;
        if (submitted) {
            if (form) form.inert = true;
            const retry = document.getElementById('verifyEntry'); if (retry) { retry.hidden = true; retry.disabled = true; }
            if (overlay) { overlay.hidden = false; overlay.innerHTML = '<h1>Exam submitted</h1><p>Your saved answers have been submitted. This attempt cannot be resumed. Flagged results await instructor review.</p><a href="/Student/Result" target="_top" style="color:white">Open my results</a>'; }
            window.dispatchEvent(new CustomEvent('proctoring-state', { detail: state }));
            if (fresh && announce) { const v = window.ProctoringVoiceInstance ||= new ProctoringVoice(); v.stop(); v.until = 0; v.speak('Your exam has been submitted. Your instructor will review the result.', 'آپ کا امتحان جمع ہو گیا ہے۔ استاد نتیجے کا جائزہ لیں گے۔', 'ur-PK'); }
            return;
        }
        if (banner) banner.querySelector('.strike-count').textContent = blocked ? 'BLOCKED' : manualOnly ? `${state.strikeCount} recorded · Teacher decision` : `${Math.min(state.allowedWarnings ?? 3, state.strikeCount)}/${state.allowedWarnings ?? 3}`;
        if (overlay) {
            overlay.hidden = !blocked;
            overlay.querySelector('h1').textContent = state.eventCode === 'DEVICE_HOLD' ? 'Exam paused for a device check' : 'Exam blocked — instructor review required';
            overlay.querySelector('.lock-reason').textContent = state.eventCode === 'DEVICE_HOLD' ? 'Camera or microphone monitoring could not continue. This is a technical hold, not a cheating finding. Contact your teacher.' : state.eventCode === 'IDENTITY_REVIEW_HOLD' || state.violationType === 'IdentityMismatch' ? 'Student Changed: the initial check and one fresh recheck did not match your registered identity. Your examination is paused for supervisor review.' : state.reason?.startsWith('Teacher ') ? 'Your examination has been blocked by an authorized examination supervisor. Please contact your supervisor.' : 'Your examination has been blocked after repeated rule violations. Please contact your examination supervisor for review. Latest observation: ' + reasonFor(state)[0];
            overlay.querySelector('[role="status"]').textContent = `Frozen time: ${state.remainingTimeSeconds} seconds. Your saved answers are retained.`;
            if (blocked && modal?.open) modal.close();
        }
        if (form) form.inert = blocked;
        const identityForm = document.getElementById('entryIdentityForm');
        if (identityForm) identityForm.inert = blocked;
        const retry = document.getElementById('verifyEntry');
        if (blocked && retry) { retry.hidden = true; retry.disabled = true; }
        window.dispatchEvent(new CustomEvent('proctoring-state', { detail: state }));
        if (!blocked && state.violationType === 'FaceNotDetected' && state.absenceEnded) {
            if (banner) banner.querySelector('.strike-message').textContent = 'Student returned to the camera view.';
            if (modal?.open) modal.close();
            if (fresh) window.ProctoringVoiceInstance?.stop();
            return;
        }
        if (!blocked && (wasBlocked || blockedPage) && state.status === 'InProgress') { location.replace(state.resumeUrl); return; }
        if (!fresh || !announce || (!blocked && state.eventCountsAsViolation !== true &&
            !state.eventCode?.startsWith('DEVICE'))) return;
        if (!blocked && state.eventCountsAsViolation && state.eventId) {
            if (announcedWarnings.has(state.eventId)) return;
            announcedWarnings.add(state.eventId);
        }
        const copy = reasonFor(state);
        const limit = state.allowedWarnings ?? 3;
        const ordinal = state.strikeCount === 1 ? 'first' : state.strikeCount === 2 ? 'second' : String(state.strikeCount);
        const specific = state.violationType === 'TalkingDetected' ? `Repeated speech was detected near the microphone. This is your ${ordinal} warning. Please keep the examination area quiet.` : ['LookAway', 'HeadAway', 'GazeAway'].includes(state.violationType) ? `Please keep your attention on the examination screen. Looking away may violate exam rules. This is your ${ordinal} warning.` : copy[0];
        const english = state.eventCode === 'IDENTITY_REVIEW_HOLD' ? 'Student Changed: the initial check and one fresh recheck did not match your registered identity. Your examination is paused for supervisor review.' : blocked ? (state.eventCode === 'DEVICE_HOLD' || state.violationType === 'IdentityMismatch' ? copy[0] : state.reason?.startsWith('Teacher ') ? 'Examination Blocked. Your examination has been blocked by an authorized examination supervisor. Please contact your supervisor.' : `Examination Blocked. ${copy[0]} Please contact your examination supervisor for review.`) : manualOnly ? `Warning ${state.strikeCount}. ${specific} Your supervisor will review this incident.` : state.strikeCount === limit ? `Final Warning — ${limit} of ${limit}. ${specific} Another examination rule violation may cause your exam to be blocked automatically. Please follow all examination rules.` : `Warning ${state.strikeCount} of ${limit}. ${specific}`;
        const urdu = state.violationType === 'IdentityMismatch' ? copy[1] : blocked ? `${copy[1]} امتحان روک دیا گیا ہے۔` : `تنبیہ ${state.strikeCount}۔ ${copy[1]} ${state.blockingMode !== 'MANUAL_ONLY' && state.strikeCount === (state.allowedWarnings ?? 3) ? (state.violationAction === 'FinalSubmit' ? 'آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان جمع ہو جائے گا۔' : 'آخری تنبیہ۔ اگلی خلاف ورزی پر امتحان بند ہو جائے گا۔') : ''}`;
        const text = english;
        if (banner) { banner.querySelector('.strike-message').textContent = text; banner.className = `alert ${state.violationType === 'TalkingDetected' || state.violationType === 'AnswerPromptSpeech' || state.strikeCount >= (state.allowedWarnings ?? 3) || blocked ? 'alert-danger' : 'alert-warning'}`; }
        if (!blocked && state.strikeCount === 2 && modal) { modal.querySelector('p').textContent = text; if (!modal.open) modal.showModal(); }
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0; voice.enabled = true;
        // Keep the mic free for a second utterance during a speech warning.
        if (blocked || !['TalkingDetected', 'AnswerPromptSpeech', 'LookingAwaySpeaking'].includes(state.violationType))
            voice.speak(english, urdu, 'ur-PK');
    }
    const enrichedEvents = new Set();
    connection.on('SpeechContextUpdated', state => {
        if (!state || state.attemptId !== attemptId || blocked || state.version !== version ||
            enrichedEvents.has(state.eventId)) return;
        enrichedEvents.add(state.eventId);
        if (enrichedEvents.size > 32) enrichedEvents.delete(enrichedEvents.values().next().value);
        const copy = reasonFor({ ...state, violationType: state.violationType === "LookingAwaySpeaking" ? "LookingAwaySpeaking" : "AnswerPromptSpeech" });
        const ending = state.blockingMode !== 'MANUAL_ONLY' && state.strikeCount === (state.allowedWarnings ?? 3) ? (state.violationAction === 'FinalSubmit' ? ' Final warning. The next violation submits your exam.' : ' Final warning. The next violation locks your exam.') : '';
        if (banner) banner.querySelector('.strike-message').textContent = `${state.blockingMode === 'MANUAL_ONLY' ? 'Teacher review required.' : `Warning ${state.strikeCount} of ${state.allowedWarnings ?? 3}.`} ${copy[0]}${ending}`;
    });
    const behaviorObservationIds = new Set();
    window.addEventListener('behavior-observation', event => {
        const observation = event.detail;
        if (!observation?.id || blocked || behaviorObservationIds.has(observation.id)) return;
        behaviorObservationIds.add(observation.id);
        if (behaviorObservationIds.size > 64) behaviorObservationIds.delete(behaviorObservationIds.values().next().value);
        const copy = behaviorCopy(observation);
        // Review-only observations already appear in the detector status. Keep the counted warning visible.
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0;
        voice.speak(copy[0], copy[1], 'ur-PK');
    });
    const softSpeechIds = new Set();
    function speechReminder(id, noticeNumber) {
        if (!id || blocked || softSpeechIds.has(id)) return;
        softSpeechIds.add(id);
        const english = noticeNumber === 1 ? 'First Speech Notice. Please remain silent. Speech notice 1 of 2.' :
            noticeNumber === 2 ? 'Final Speech Notice. Further talking may result in a cheating warning.' :
            'Please do not talk during the examination.';
        if (banner) { banner.querySelector('.strike-message').textContent = english; banner.className = 'alert alert-warning'; }
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        voice.stop(); voice.until = 0; voice.enabled = true;
        voice.speak(english, 'براہ کرم خاموش رہیں، ٹیسٹ کے دوران بات نہ کریں۔', 'ur-PK');
    }
    connection.on('IdentityReverificationRequested', state => window.dispatchEvent(new CustomEvent('identity-reverification-requested', { detail: state })));
    connection.on('SpeechReminder', state => { if (state.attemptId === attemptId && state.version >= version) { version = state.version; speechReminder(state.eventId, state.eventCode === 'SPEECH_NOTICE_1' ? 1 : state.eventCode === 'SPEECH_NOTICE_2' ? 2 : undefined); } });
    ['StrikeIssued', 'ExamLocked', 'ExamSubmitted', 'ResumeExamSignal', 'UnblockSignal'].forEach(name => connection.on(name, state => apply(state)));
    connection.on('AbsenceUpdated', state => { if (state.absenceEnded) apply(state, false); });
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
    window.addEventListener('pagehide', event => { if (!event.persisted) { stopped = true; connection.stop(); } });
    window.addEventListener('pageshow', event => { if (event.persisted) void sync(); });
    start(); if (attemptId) { sync(); setInterval(sync, 5000); }
    return { connection, apply, sync, speechReminder, get blocked() { return blocked; } };
})();

/* Independent capture/inference lanes. SQL state, never this module, owns strikes. */
const examMediaSessions = new WeakMap();
window.ExamProctoring = {
    start(config) {
        const form = document.getElementById('examForm');
        if (examMediaSessions.has(form)) return examMediaSessions.get(form);
        const token = form.querySelector('[name="__RequestVerificationToken"]').value;
        const video = document.getElementById('proctorCamera');
        const voice = window.ProctoringVoiceInstance ||= new ProctoringVoice();
        const lifetime = new AbortController();
        const timers = new Set();
        const stream = new MediaStream();
        const capture = window.ExamDisplayCapture?.session(config.sessionId);
        let display = capture?.display;
        const mediaRequests = new Map();
        let evidence, stopAudio, stopSharing, stopped = false, identityBusy = false;
        // Prevent the 60-second lane from overlapping an active mismatch recheck
        // sequence. This affects identity only, not behavior/object/audio lanes.
        let identitySequenceActive = false, identitySequenceId = 0, identitySequenceToken;
        let behaviorReady = false, calibrationBusy = false, calibrationAttempts = 0, lastCalibrationAttempt = -Infinity;
        let calibrationRetryPending = false;
        let cameraCalibrationRequired = false;
        const active = () => !stopped && !config.submitted() && !window.ProctoringRealtime.blocked;
        const observedHealth = new Map();
        const notifiedIncidents = new Set();
        function incidentAlert(key, title, message, state, spoken=false) {
            if (notifiedIncidents.has(key)) return;
            notifiedIncidents.add(key);
            let notice = document.getElementById('examIncidentNotice');
            if (!notice) { notice = document.createElement('div'); notice.id='examIncidentNotice'; notice.setAttribute('role','alert'); form.before(notice); }
            notice.className='alert alert-danger border border-danger shadow';
            const warning = state?.strikeCount > 0 ? ` Warning ${Math.min(state.strikeCount,state.allowedWarnings ?? 3)}/${state.allowedWarnings ?? 3}.` : '';
            notice.textContent = `${title} — ${message}${warning} ${new Date().toLocaleTimeString()}`;
            if (spoken && !voice.blocked) voice.speak(message, '', 'en-US');
        }
        const healthBar = document.createElement('div'); healthBar.className = 'alert alert-light border';
        healthBar.setAttribute('role', 'status');
        const healthItems = {};
        for (const label of ['Camera', 'Microphone', 'Monitoring', 'Recording', 'Screen sharing']) {
            const item = document.createElement('span'); item.className = 'badge bg-warning text-dark me-2';
            item.textContent = label + ': checking'; healthBar.append(item); healthItems[label] = item;
        }
        const deviceDetails = document.getElementById('examDeviceDetails');
        if (deviceDetails) deviceDetails.append(healthBar);
        else form.before(healthBar);

        const status = (id, text, warning = false) => {
            observedHealth.set(id, { at: performance.now(), healthy: !warning && /\b(active|analyzed|verified|connected)\b/i.test(text) && !/\b(not|unavailable|disconnected|inactive|reconnecting|failed|expired)\b/i.test(text) });
            const node = document.getElementById(id);
            if (node) { node.textContent = text; node.className = warning ? 'ai-status-pill ai-status-warning' : 'ai-status-pill ai-status-normal'; }
            if (id === 'mediaStatus') {
                const problem = document.getElementById('examMediaProblem');
                if (problem) { problem.textContent = warning ? text : ''; problem.hidden = !warning; }
            }
        };
        const later = (fn, ms) => {
            if (stopped) return;
            const timer = setTimeout(() => { timers.delete(timer); if (!stopped) fn(); }, ms);
            timers.add(timer);
        };
        const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
        const paintHealth = (label, healthy, text, unavailable = false) => {
            const item = healthItems[label];
            const previous = item.dataset.qualityState;
            const state = healthy ? (previous === 'DEGRADED' || previous === 'UNAVAILABLE' ? 'RECOVERED' : 'ACTIVE') : unavailable ? 'UNAVAILABLE' : 'DEGRADED';
            item.dataset.qualityState = state;
            item.textContent = label + ': ' + state + ' — ' + text;
            item.className = 'badge me-2 ' + (healthy ? 'bg-success' : unavailable ? 'bg-danger' : 'bg-warning text-dark');
        };
        let previewTime = -1, previewAt = performance.now();
        function refreshHealth() {
            if (video.currentTime !== previewTime) { previewTime = video.currentTime; previewAt = performance.now(); }
            const cam = stream?.getVideoTracks()[0], mic = stream?.getAudioTracks()[0];
            const cameraOk = cam?.readyState === 'live' && !cam.muted && video.readyState >= 2 && video.videoWidth > 0 && performance.now() - previewAt < 6000;
            const micOk = mic?.readyState === 'live' && !mic.muted;
            paintHealth('Camera', cameraOk, cameraOk ? 'receiving video' : 'unavailable', !cameraOk);
            paintHealth('Microphone', micOk, micOk ? 'connected' : 'unavailable', !micOk);
            const monitorOk = ['identityMonitorStatus', 'behaviorMonitorStatus', 'objectMonitorStatus', 'speechMonitorStatus'].every(id => {
                const h = observedHealth.get(id); return h?.healthy && performance.now() - h.at < (id === 'identityMonitorStatus' ? 180000 : 15000);
            });
            paintHealth('Monitoring', monitorOk, monitorOk ? 'checks current' : 'degraded / checking');
            const recording = evidence?.recorder.status();
            paintHealth('Recording', recording?.active === true, recording?.active ? 'capturing; upload tracked separately' : 'unavailable', !recording?.active);
            paintHealth('Screen sharing', recording?.cameraOnly === false, recording?.cameraOnly === false ? 'selected surface only' : 'not shared');
            later(refreshHealth, 2000);
        }
        later(refreshHealth, 1000);

        const body = () => {
            const data = new FormData();
            data.append('AttemptId', config.attemptId); data.append('SessionAttemptId', config.sessionId);
            data.append('__RequestVerificationToken', token);
            return data;
        };
        async function request(action, data, timeout = 30000) {
            const headers = { Accept: 'application/json' };
            if (action === 'behavior' && ['attention', 'absence', 'persons'].includes(data.get('Feature')))
                headers['X-Proctoring-Feature'] = data.get('Feature');
            const response = await fetch(config.urls[action], {
                method: 'POST', body: data,
                credentials: 'same-origin', headers, signal: AbortSignal.any([lifetime.signal, AbortSignal.timeout(timeout)])
            });
            const result = await window.ExamHttp.readJson(response, { allowBlockedState: true });
            if (result.state) window.ProctoringRealtime.apply(result.state);
            // Even a protective-lock response can carry an incident needing its original clip.
            const evidenceIds = result.evidenceLogIds ?? result.logIds;
            if (action === 'identity' && (result.identityMismatchConfirmed || result.state?.eventCode === 'IDENTITY_REVIEW_HOLD') && evidenceIds?.length)
                void evidence?.finishIdentityCapture(evidenceIds[0]);
            else if (evidenceIds?.length && data.get('Feature') !== 'persons')
                evidence?.attach(evidenceIds.filter(id => id !== result.absenceIncident?.id));
            if (!response.ok && !(action === 'identity' && result.blocked)) throw Object.assign(new Error(result.message || `${action}: HTTP ${response.status}`), { status: response.status });
            return result;
        }
        async function frame(maximumDimension = 640) {
            const camera = stream.getVideoTracks().find(track => track.readyState === 'live' && track.enabled && !track.muted);
            if (!camera || video.readyState < 2 || !video.videoWidth)
                throw new Error('Camera frame unavailable');
            const canvas = document.createElement('canvas');
            const ratio = Math.min(1, maximumDimension / Math.max(video.videoWidth, video.videoHeight));
            canvas.width = Math.round(video.videoWidth * ratio); canvas.height = Math.round(video.videoHeight * ratio);
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', .88));
            if (camera.readyState !== 'live' || camera.muted || !stream.getVideoTracks().includes(camera))
                throw new Error('Camera changed during capture');
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
        const browserScope = `${config.attemptId}:${config.sessionId}:browser`;
        const episodeKey = `exam-browser-episode:${browserScope}`;
        const browserQueue = import('/js/proctoring/durable-evidence.js?v=fullscreen-observations')
            .then(({ DurableEvidenceQueue }) => new DurableEvidenceQueue(browserScope, token,
                text => status('evidenceStatus', text)))
            .catch(() => {
                status('evidenceStatus', 'Browser observation delivery is unavailable.', true);
                return null;
            });
        function flushBrowserObservations() {
            void browserQueue.then(queue => queue?.flush());
        }
        async function deliverBrowserObservation(packet) {
            const queue = await browserQueue;
            if (await queue?.enqueue({ browserEvent: packet })) return;
            const data = body();
            for (const [key, value] of Object.entries({
                eventId: packet.id, phase: packet.phase, kind: packet.kind,
                elapsed: packet.duration, surface: packet.surface, observedAt: packet.observedAt,
                startedAt: packet.startedAt
            })) if (value != null) data.append(key, value);
            try {
                const response = await fetch('/Student/Exam/BrowserObservation', {
                    method: 'POST', body: data, credentials: 'same-origin', keepalive: true
                });
                if (response.ok && (await response.json()).acknowledged) return;
            } catch { /* Status below also covers network failures. */ }
            status('evidenceStatus', 'Browser observation could not be sent. Please tell your teacher.', true);
        }
        flushBrowserObservations();
        on(window, 'online', flushBrowserObservations);
        lane(() => browserQueue.then(queue => queue?.flush()), 5000, 'evidenceStatus');
        const pendingFullscreenEvidence = [];
        let fullscreenEntered = false, initialFullscreenTried = false;
        const guard = ExamBrowserGuard.create({
            now: () => performance.now(),
            wallTime: time => new Date(performance.timeOrigin + time).toISOString(),
            restore: () => {
                try {
                    const saved = JSON.parse(sessionStorage.getItem(episodeKey));
                    fullscreenEntered = saved?.type === 'FullScreenExit';
                    return saved;
                } catch { return null; }
            },
            persist: episode => {
                try {
                    if (episode) sessionStorage.setItem(episodeKey, JSON.stringify(episode));
                    else sessionStorage.removeItem(episodeKey);
                } catch { /* The outbox can still deliver events without a reload journal. */ }
            },
            state: () => ({
                active: active(), hidden: document.hidden, focused: document.hasFocus(),
                fullscreen: document.fullscreenElement === document.documentElement,
                requireFullscreen: config.requireFullscreen === true
            }),
            report: (type, text, event) => {
                const packet = { ...event, kind: type,
                    surface: window.ExamDisplayCapture?.live(display)?.getSettings?.().displaySurface ||
                        evidence?.recorder.captureSurface || 'unknown',
                    attemptId: config.attemptId, sessionAttemptId: config.sessionId,
                    observedAt: new Date(performance.timeOrigin + performance.now()).toISOString() };
                void deliverBrowserObservation(packet);
                if (type === 'FullScreenExit' && (!evidence?.recorder.running ||
                    pendingFullscreenEvidence.some(pending => pending.event.id === event.id))) {
                    if (pendingFullscreenEvidence.length === 8) pendingFullscreenEvidence.shift();
                    pendingFullscreenEvidence.push({ event, at: performance.now() });
                } else evidence?.browserEvent(type, event, false);
                if (event.phase !== 'update' && type === 'ExamPageHidden') incidentAlert(event.id,
                    'TAB SWITCHING OBSERVATION',
                    event.phase === 'end' ? 'The browser recorded an interruption for teacher review.' :
                        'Return to the examination page. The browser recorded an interruption for teacher review.');
                if (event.phase === 'start' && type === 'ExamPageHidden') status('mediaStatus', 'Your exam page became hidden. Please return to your exam.', true);
            }
        });
        function on(target, event, handler) { target?.addEventListener(event, handler, { signal: lifetime.signal }); }
        on(document, 'visibilitychange', () => {
            if (document.hidden && active()) departureSnapshot = evidence?.recorder?.captureScreenDataUrl() || null;
            guard.sample();
            // Restore the server-authoritative strike count if realtime updates were delayed while hidden.
            if (!document.hidden && active()) void window.ProctoringRealtime.sync();
            document.getElementById('examForm')?.classList.toggle('exam-content-hidden', document.hidden);
        });
        const fullscreenButton = document.getElementById('fullscreenStatusPill');
        const fullscreenStatus = document.getElementById('examFullscreenStatus');
        function showFullscreen() {
            if (!fullscreenButton) return;
            const enabled = document.fullscreenEnabled === true && typeof document.documentElement.requestFullscreen === 'function';
            const isFullscreen = document.fullscreenElement === document.documentElement;
            fullscreenEntered ||= isFullscreen;
            fullscreenButton.hidden = isFullscreen || !enabled;
            fullscreenButton.textContent = fullscreenEntered ? 'Restore fullscreen' : 'Enter fullscreen';
            fullscreenStatus.textContent = isFullscreen ? 'Exam fullscreen active.'
                : !enabled ? 'Fullscreen is unavailable in this browser. Contact your teacher.'
                : fullscreenEntered ? 'Fullscreen exited. Select Restore fullscreen to return.'
                : 'Select Enter fullscreen to enable exam fullscreen.';
        }
        async function enterFullscreen() {
            if (!active() || !fullscreenButton || fullscreenButton.hidden || fullscreenButton.disabled) return;
            initialFullscreenTried = true;
            fullscreenButton.disabled = true;
            try {
                await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
                showFullscreen();
            } catch {
                fullscreenStatus.textContent = 'The browser could not enter fullscreen. Select the fullscreen button to retry or contact your teacher.';
            } finally {
                fullscreenButton.disabled = false;
            }
        }
        on(fullscreenButton, 'click', enterFullscreen);
        on(document, 'pointerdown', event => {
            // Keep the initial entry on the first exam interaction. After an exit,
            // recovery uses the explicit button and does not interrupt other work.
            if (!fullscreenEntered && !initialFullscreenTried &&
                !event.target?.closest?.('#fullscreenStatusPill, [data-screen-permission]'))
                void enterFullscreen();
        });
        showFullscreen();
        on(document, 'fullscreenchange', () => {
            showFullscreen();
            guard.sample();
        });
        const protectionStyle = document.createElement('style');
        protectionStyle.textContent = '#examForm .question-card {user-select:none;-webkit-user-select:none} #examForm.exam-content-hidden .question-card, #examForm.exam-capture-mask .question-card {visibility:hidden} @media print {body {visibility:hidden!important}}';
        document.head.appendChild(protectionStyle);
        lifetime.signal.addEventListener('abort', () => protectionStyle.remove(), { once: true });
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
            if (state.attemptId !== Number(config.attemptId)) return;
            if (['Completed', 'AutoSubmitted', 'Disqualified'].includes(state.status)) {
                stop();
                capture?.release();
                return;
            }
            if (!state.eventId) return;
            if (!evidence) return;
            if (state.status === 'ProctoringBlocked' && evidence.peopleCapture?.logId)
                void evidence.finishPeopleCapture('exam_blocked');
            if (state.absenceRootId) {
                evidence.absence({ absenceIncident: { id: state.absenceRootId, endedAt: state.absenceEnded },
                    presenceStatus: { state: state.absenceEnded ? 'PERSON_VISIBLE_IDENTITY_UNCERTAIN' : 'VERIFIED_PHYSICAL_ABSENCE' },
                    state });
                return;
            }
            if (state.status === 'ProctoringBlocked' && evidence.absenceCapture?.logId)
                void evidence.finishAbsenceCapture('exam_blocked');
            if (attached.has(state.eventId)) return;
            attached.add(state.eventId);
            if (state.eventCode === 'IDENTITY_REVIEW_HOLD' && state.violationType === 'IdentityMismatch') {
                void evidence.finishIdentityCapture(state.eventId);
                return;
            }
            if (state.violationType !== 'MultipleFaces' && state.violationType !== 'AdditionalPersonObservation')
                evidence.attach([state.eventId]);
        });
        let lastVideoTime = -1, lastVideoAdvance = performance.now();
        let multiplePeoplePreviouslyVisible = false;
        let identityContinuityLost = false, lastIdentityTrigger = -Infinity;
        let physicallyAway = false;
        function invalidateIdentitySequence() {
            identityContinuityLost = true;
            identitySequenceId++;
            identitySequenceActive = false;
        }
        function reverifyAfterContinuityLoss() {
            if (!active() || physicallyAway || identityBusy || identitySequenceActive ||
                !stream?.getVideoTracks().some(track => track.readyState === 'live') ||
                performance.now() - lastIdentityTrigger < 5000) return;
            lastIdentityTrigger = performance.now(); identityContinuityLost = false; identity();
        }
        on(window, "identity-reverification-requested", event => {
            if (Number(event.detail?.attemptId) === Number(config.attemptId)) reverifyAfterContinuityLoss();
        });
        const mediaRequestedAt = performance.now();
        let initialMediaSettled = false;
        let lastVisualFingerprint = null, lastVisualChangeAt = performance.now();
        let cameraPermissionDenied = false, microphonePermissionDenied = false;
        let observedCamera, observedMicrophone, microphoneSince = 0, lastPcmAt = null;
        on(window, 'exam-microphone-pcm', event => {
            const track = stream?.getAudioTracks()[0];
            if (track?.readyState === 'live' && track.enabled && !track.muted && event.detail?.track === track && event.detail.samples > 0) {
                if (track !== observedMicrophone) {
                    observedMicrophone = track;
                    microphoneSince = performance.now();
                }
                lastPcmAt = performance.now();
            }
        });
        async function deviceHealth() {
            if (!active() || !config.urls.health) return;
            const now = performance.now();
            const camera = stream?.getVideoTracks()[0], mic = stream?.getAudioTracks()[0];
            if (camera !== observedCamera) {
                observedCamera = camera;
                lastVideoTime = -1;
                lastVideoAdvance = lastVisualChangeAt = now;
                lastVisualFingerprint = null;
            }
            if (mic !== observedMicrophone) {
                observedMicrophone = mic;
                microphoneSince = now;
                lastPcmAt = null;
            }
            if (video.currentTime !== lastVideoTime) {
                lastVideoTime = video.currentTime;
                lastVideoAdvance = now;
            }
            const cameraLive = camera?.readyState === 'live' && camera.enabled && !camera.muted;
            const previewReady = video.readyState >= 2 && video.videoWidth > 0;
            const visual = cameraLive && previewReady ? window.MediaCapabilities?.captureState(video) : null;
            if (visual?.fingerprint && visual.fingerprint !== lastVisualFingerprint) {
                lastVisualFingerprint = visual.fingerprint;
                lastVisualChangeAt = now;
            }
            const initialPermissionPending = !initialMediaSettled && now - mediaRequestedAt < 15000;
            let cameraIssue = !cameraLive
                ? cameraPermissionDenied ? 'camera_denied' : camera?.muted ? 'camera_muted' :
                    !camera && initialPermissionPending ? 'checking' : 'camera_disconnected'
                : 'healthy';
            if (cameraLive) {
                if (now - lastVideoAdvance > 6500 || (visual && now - lastVisualChangeAt > 6500))
                    cameraIssue = 'camera_frozen';
                else if (!previewReady) cameraIssue = 'checking';
                else if (!visual) cameraIssue = 'camera_unclear';
                else if (visual.brightness < 12) cameraIssue = 'camera_dark';
            }
            const micLive = mic?.readyState === 'live' && mic.enabled && !mic.muted;
            if (!micLive) {
                microphoneSince = now;
                lastPcmAt = null;
            }
            const micIssue = !micLive
                ? microphonePermissionDenied ? 'microphone_denied' : mic?.muted ? 'microphone_muted' :
                    !mic && initialPermissionPending ? 'checking' : 'microphone_disconnected'
                : now - (lastPcmAt ?? microphoneSince) > 6500 ? 'microphone_frozen' :
                    lastPcmAt === null ? 'checking' : 'healthy';
            const isFault = value => value !== 'checking' && value !== 'healthy';
            const cameraFault = isFault(cameraIssue), micFault = isFault(micIssue);
            if (cameraFault) identityContinuityLost = true;
            else if (cameraIssue === 'healthy' && identityContinuityLost) reverifyAfterContinuityLoss();

            const issue = cameraFault && micFault ? 'camera_and_microphone_disconnected' :
                cameraFault ? cameraIssue : micFault ? micIssue : 'healthy';
            if (issue === 'healthy' && (cameraIssue === 'checking' || micIssue === 'checking')) return;
            const descriptions = {
                camera_denied: 'Camera permission denied', microphone_denied: 'Microphone permission denied',
                camera_disconnected: 'Camera disconnected', microphone_disconnected: 'Microphone disconnected',
                camera_muted: 'Camera track muted', microphone_muted: 'Microphone track muted',
                camera_frozen: 'Camera frames stopped updating', microphone_frozen: 'Microphone audio capture interrupted',
                camera_dark: 'Camera image is too dark', camera_unclear: 'Camera frames cannot be checked'
            };
            const problem = [cameraIssue, micIssue].filter(isFault).map(value => descriptions[value]).join('. ');
            if (problem) status('mediaStatus', problem + '. Check your device and permissions.', true);
            const data = body(); data.append('issue', issue);
            const result = await request('health', data, 8000);
            if (problem && result.warningCount > 0) {
                const message = `Device warning ${result.warningCount}/3: ${problem}. A persistent failure pauses the exam without a cheating strike.`;
                status('mediaStatus', message, true);
                if (result.warning && result.state?.eventId) evidence?.attach([result.state.eventId]);
            } else if (issue === 'healthy') {
                const recovered = result.issue === 'healthy';
                status('mediaStatus', recovered ? 'Camera and microphone connected' : 'Checking device recovery…', !recovered);
            }
        }
        async function identity(opportunity = 0, sequenceId = null) {
            if (!active() || physicallyAway || identityBusy || !stream?.getVideoTracks().some(t => t.readyState === 'live' && !t.muted)) {
                if (opportunity && sequenceId === identitySequenceId) {
                    identitySequenceActive = false;
                    identityContinuityLost = true;
                }
                return;
            }
            if (opportunity === 0) {
                if (identitySequenceActive) return;
                identitySequenceActive = true;
                sequenceId = ++identitySequenceId;
                identitySequenceToken = crypto.randomUUID();
                evidence?.startIdentityCapture();
            } else if (sequenceId !== identitySequenceId) return;
            identityBusy = true;
            let retry = false, retryDelay = 3000;
            try {
                const data = body();
                data.append('IdentitySequenceId', identitySequenceToken);
                const count = Math.max(1, Math.min(10, Number(config.identityFrames) || 3));
                for (let i = 0; i < count; i++) {
                    data.append('Frames', await frame(), `identity-${i}.jpg`);
                    if (i + 1 < count) await delay(150);
                }
                if (sequenceId !== identitySequenceId) return;
                const result = await request('identity', data, 50000);
                if (sequenceId !== identitySequenceId) return;
                retry = opportunity === 0 && result.confirmationPending === true;
                retryDelay = Math.max(2000, Math.min(10000, Number(result.retryAfterMilliseconds) || 3000));
                const progress = Number.isInteger(result.confirmedMismatchBatches)
                    ? ` — ${result.confirmedMismatchBatches}/${result.requiredMismatchBatches} checks` : '';
                status('identityMonitorStatus', retry ? 'Identity recheck in progress' + progress
                    : result.verified ? 'Identity verified'
                        : result.message || 'Identity needs teacher review', !result.verified);
            } catch (error) {
                status('identityMonitorStatus', window.ExamHttp.describe(error), true);
                // Uncertain results are left to the normal identity lane.
                // A suspected change gets only one dedicated follow-up request.
            } finally {
                identityBusy = false;
                if (retry && active() && sequenceId === identitySequenceId)
                    later(() => identity(opportunity + 1, sequenceId), retryDelay);
                else if (sequenceId === identitySequenceId) {
                    identitySequenceActive = false;
                    void evidence?.finishIdentityCapture();
                }
                if (identityContinuityLost && !physicallyAway && !identitySequenceActive)
                    later(reverifyAfterContinuityLoss, Math.max(0, 5000 - (performance.now() - lastIdentityTrigger)));
            }
        }
        async function calibrate(screenCalibration = false) {
            if (!active() || calibrationBusy || (!screenCalibration && behaviorReady)) return;
            if (performance.now() - lastCalibrationAttempt < 10000) {
                return;
            }
            const calibrationTrack = stream.getVideoTracks()[0];
            if (!calibrationTrack || calibrationTrack.readyState !== 'live') return;
            calibrationAttempts++; lastCalibrationAttempt = performance.now();
            const previousReady = behaviorReady;
            calibrationBusy = true; behaviorReady = false;
            const calibrationButton = document.getElementById('calibrateScreenView');
            if (calibrationButton) calibrationButton.disabled = true;
            try {
                status('behaviorMonitorStatus', 'Look at the screen centre for calibration');
                const data = body();
                for (let i = 0; i < config.baselineFrames; i++) {
                    data.append('Frames', await frame(), `baseline-${i}.jpg`); await delay(150);
                }
                if (screenCalibration) {
                    data.append('ScreenCalibration', 'true'); data.append('ScreenPoints', '6');
                    // Inline, silent instructions keep the examination page visible.
                    // Preserve measured screen boundaries without a fullscreen overlay or moving dot.
                    for (const [x, y, position] of [[10, 10, 'top-left'], [90, 10, 'top-right'], [90, 90, 'bottom-right'], [10, 90, 'bottom-left'], [50, 25, 'upper centre'], [50, 75, 'lower centre']]) {
                        status('behaviorMonitorStatus', `Quick screen check: look at the ${position} of your screen. Keep your head comfortable.`);
                        await delay(1500);
                        for (let i = 0; i < 2; i++) { data.append('Frames', await frame(), `corner-${x}-${y}-${i}.jpg`); await delay(100); }
                    }
                }
                if (!active()) return;
                if (stream.getVideoTracks()[0] !== calibrationTrack) throw new Error('Camera changed during calibration');
                const result = await request('baseline', data, 20000);
                if (stream.getVideoTracks()[0] !== calibrationTrack) throw new Error('Camera changed during calibration');
                behaviorReady = result.started === true;
                if (!behaviorReady) throw new Error('Calibration needs a clear face');
                cameraCalibrationRequired = false;
                calibrationAttempts = 0;
                status('behaviorMonitorStatus', 'Attention monitoring active');
            } catch (error) {
                behaviorReady = previousReady && !cameraCalibrationRequired;
                status('behaviorMonitorStatus', behaviorReady
                    ? 'Calibration update failed. Previous calibration retained.'
                    : 'Attention calibration unavailable. Other monitoring continues.', !behaviorReady);
                if (!behaviorReady && !calibrationRetryPending) {
                    calibrationRetryPending = true;
                    later(() => { calibrationRetryPending = false; void calibrate(screenCalibration); },
                        Math.min(60000, 10000 * calibrationAttempts));
                }
            }
            finally {
                calibrationBusy = false;
                if (calibrationButton) calibrationButton.disabled = false;
            }
        }
        on(document.getElementById('calibrateScreenView'), 'click', () => calibrate(true));
        // A lane schedules its next fresh capture after completion. No shared busy flag,
        // no stale frame backlog, and a slow detector never stalls another detector.
        function lane(fn, interval, id) {
            let failures = 0;
            const run = async () => {
                const started = performance.now();
                let retryDelay = 0;
                try {
                    if (active()) await fn();
                    failures = 0;
                }
                catch (error) {
                    if (!lifetime.signal.aborted) status(id, window.ExamHttp.describe(error), true);
                    if ([429, 500, 502, 503, 504].includes(error.status) || error.name === 'TimeoutError') {
                        failures = Math.min(failures + 1, 5);
                        retryDelay = Math.min(60000, Math.max(Number(error.retryAfterMilliseconds) || 0,
                            Math.min(10000, Math.max(1000, interval) * 2 ** (failures - 1))));
                    }
                }
                finally { later(run, Math.max(retryDelay, 50, interval - (performance.now() - started))); }
            };
            run();
        }
        const objectMessages = new Map();
        async function videoModel(action, id, feature) {
            if (feature === 'attention' && calibrationBusy) return;
            if (feature === 'attention' && cameraCalibrationRequired) {
                if (!calibrationRetryPending) void calibrate();
                return;
            }
            const data = body();
            const cameraFrame = await frame(action === 'objects' ? 1280 : 960);
            const capturedAt = Date.now();
            data.append('Frame', cameraFrame, `${action}.jpg`);
            if (action === 'objects') data.append('QueueEvidence', 'true');
            if (feature) data.append('Feature', feature);
            try {
                const result = await request(action, data, feature === 'absence' ? 5000 :
                    (feature === 'persons' || feature === 'attention' || action === 'objects') ? 6500 : 30000);
                if (action === 'objects' && evidence && result.logIds?.length) {
                    const reader = new FileReader();
                    reader.onload = () => {
                        for (const logId of result.logIds) void evidence.outbox.enqueue({
                            endpoint: '/Student/Exam/IncidentEvidence', stage: 'before',
                            logId, screenshot: reader.result, capturedAt,
                            attemptId: config.attemptId, sessionAttemptId: config.sessionId
                        });
                    };
                    reader.readAsDataURL(cameraFrame);
                }
                if (feature === 'attention' && !calibrationBusy && result.attentionStatus) {
                    behaviorReady = !cameraCalibrationRequired && result.attentionStatus.calibrated === true;
                    if ((cameraCalibrationRequired || result.attentionStatus.calibration_required) && !calibrationRetryPending)
                        void calibrate();
                }
                if (feature === 'absence') {
                    evidence?.absence(result);
                    const presence = result.presenceStatus?.state;
                    if (presence === 'TRACKING_UNCERTAIN' || presence === 'CAMERA_MONITORING_INTERRUPTED') {
                        status(id, presence === 'TRACKING_UNCERTAIN'
                            ? 'Presence uncertain — waiting for fresh, usable observations'
                            : 'Presence monitoring interrupted — check camera view', true);
                        return;
                    }
                    if (presence === 'POSSIBLE_ABSENCE' || presence === 'VERIFIED_PHYSICAL_ABSENCE') {
                        status(id, presence === 'POSSIBLE_ABSENCE' ? 'Student out of camera view — confirming absence' : 'Student absent from camera view', true);
                        if (!physicallyAway) {
                            physicallyAway = true;
                            invalidateIdentitySequence();
                            evidence?.startIdentityCapture();
                        }
                        return;
                    } else if (presence === 'PERSON_VISIBLE_IDENTITY_UNCERTAIN') {
                        const returned = physicallyAway;
                        physicallyAway = false;
                        if (returned) lastIdentityTrigger = -Infinity;
                        if (identityContinuityLost) reverifyAfterContinuityLoss();
                    }
                }
                const quality = result.metrics?.camera_quality?.state;
                if (feature === 'persons') {
                    evidence?.people(result);
                    if (result.peopleSignal === true) multiplePeoplePreviouslyVisible = true;
                    if (result.peopleSignal === false && multiplePeoplePreviouslyVisible) {
                        multiplePeoplePreviouslyVisible = false;
                        identityContinuityLost = true;
                        reverifyAfterContinuityLoss();
                    }
                }
                if (feature === 'attention' && result.metrics) {
                    if (result.metrics.face_count !== 1 || result.metrics.presence_ok === false) identityContinuityLost = true;
                    else if (identityContinuityLost && result.metrics.quality_ok) reverifyAfterContinuityLoss();
                }
                if (action === "objects" && Array.isArray(result.activeObjectIncidentIds)) {
                    for (const key of objectMessages.keys()) if (!result.activeObjectIncidentIds.includes(key)) objectMessages.delete(key);
                    for (const item of result.objectWarnings || []) {
                        if (item.reviewOnly && result.logIds?.includes(item.id)) {
                            incidentAlert(`object:${item.id}`, 'OBJECT DETECTION',
                                `${item.name || 'Possible object detected'}. Your teacher can review the camera evidence.`);
                        }
                        if (result.activeObjectIncidentIds.includes(item.id))
                            objectMessages.set(item.id, String(item.name || "material").replace(/ Detected$/, "").toLowerCase());
                    }
                    if (result.cameraUncertain) {
                        status(id, 'Object monitoring needs a clearer camera view', true);
                        return;
                    }
                    if (objectMessages.size) {
                        const names = [...new Set(objectMessages.values())].join(", ");
                        status(id, "A possible " + names + " was detected. Please remove unauthorized materials. Recorded for review.", true);
                        return;
                    }
                }
                if (action === 'behavior' && result.behaviorWarnings?.length) {
                    const warnings = result.behaviorWarnings;
                    // A confirmed multiple-person or attention incident must not
                    // be hidden by an older face-visibility observation.
                    const priorities = ['MultipleFaces','HeadAway','GazeAway','LookAway','FaceNotDetected','AdditionalPersonObservation','FaceNotVisible'];
                    const warning = warnings.find(w => w.countsAsViolation && priorities.includes(w.type)) ||
                        priorities.map(type => warnings.find(w => w.type === type)).find(Boolean) || warnings[0];
                    const copy = behaviorCopy(warning);
                    status(id, `${warning.title}: ${copy[0]} Recorded for teacher review.`, true);
                    if (!warning.countsAsViolation) window.dispatchEvent(new CustomEvent('behavior-observation', { detail: warning }));
                    return;
                }
                const unclear = action === 'behavior' && (quality === 'UNUSABLE' || result.metrics?.quality_ok === false);
                const physicalPersonUnavailable = (feature === 'absence' || feature === 'persons') && result.capabilities?.personDetector === false;
                const gazeNeedsCalibration = feature === 'attention' && result.attentionStatus?.calibration_required === true;
                const gazeUncertain = feature === 'attention' && result.attentionStatus?.state === 'TRACKING_UNCERTAIN';
                const unsupportedObjects = action === 'objects' && Array.isArray(result.unsupportedClasses) ? result.unsupportedClasses : [];
                if (action === 'behavior' && result.faceNotVisibleEvent) {
                    status(id, 'Your face is not visible. Please face the camera. No rule strike was added.', true);
                    voice.speak('Please face the camera.', 'آپ کا چہرہ نظر نہیں آ رہا۔ کیمرے کے سامنے آ جائیں۔', 'ur-PK');
                    return;
                }
                status(id, unclear
                    ? (quality === 'UNUSABLE' ? 'Camera View Unavailable. Please check your camera position and lighting.' : 'Camera Quality Is Low. Please improve the lighting and make sure your face is visible.')
                    : physicalPersonUnavailable ? 'Person detector unavailable'
                        : gazeNeedsCalibration ? 'Screen calibration required before looking-away warnings'
                        : gazeUncertain ? 'Attention uncertain — waiting for a clear camera view'
                        : unsupportedObjects.length ? `Object detector active, but ${unsupportedObjects.length} configured classes have no trained detection support: ${unsupportedObjects.slice(0, 4).join(', ')}`
                            : result.detections?.length ? 'Object observations available for review' : `${action} monitoring active`,
                    unclear || physicalPersonUnavailable || gazeNeedsCalibration || gazeUncertain || unsupportedObjects.length > 0);
            } catch (error) {
                if (feature === 'attention' && error.status === 409 && !calibrationBusy) {
                    behaviorReady = false;
                    if (!calibrationRetryPending) void calibrate();
                }
                throw error;
            }
        }
        let speechAnalysisIssue = null;
        const transcribe = ExamSpeechStream.createAnalysisQueue({
            active,
            process: analyzeSpeech,
            onFull: packet => {
                const seconds = Math.max(0, (packet.monotonicEnd - packet.monotonicStart) / 1000);
                evidence?.outbox.enqueue({ audioGap: { reason: 'backpressure', durationSeconds: seconds },
                    attemptId: config.attemptId, sessionAttemptId: config.sessionId, capturedAt: Date.now() });
            },
            onError: error => {
                speechAnalysisIssue = window.ExamHttp.describe(error);
                if (active()) status('speechMonitorStatus', speechAnalysisIssue, true);
            }
        });
        async function analyzeSpeech(current) {
            const data = body(); data.append('Audio', current.clip, 'speech.wav');
            if (current.capturedAt) data.append('AudioCapturedAtUtc', current.capturedAt);
            if (Number.isFinite(current.monotonicStart)) data.append('AudioMonotonicStart', current.monotonicStart);
            if (Number.isFinite(current.monotonicEnd)) data.append('AudioMonotonicEnd', current.monotonicEnd);
            if (current.detectionLogId) data.append('DetectionLogId', current.detectionLogId);
            if (current.questionScreenshot) data.append('QuestionScreenshotDataUrl', current.questionScreenshot);
            if (current.questionId) data.append('QuestionId', current.questionId);
            current.questionIds.forEach(id => data.append('QuestionIds', id));
            let result;
            try { result = await request('transcribe', data, 30000); }
            catch (error) {
                if (![429, 503].includes(error.status) || !active()) throw error;
                await delay(400);
                if (!active()) return;
                result = await request('transcribe', data, 30000);
            }
            speechAnalysisIssue = result.semanticReady === false ? 'Question context analysis unavailable; speech detection continues' : null;
            if (result.speechNoticeNumber === 1 || result.speechNoticeNumber === 2)
                window.ProctoringRealtime.speechReminder(result.speechNoticeId, result.speechNoticeNumber);
            if (result.speechEvidenceId) {
                void evidence?.commitSpeech(result.speechEvidenceId);
                void window.ProctoringRealtime.sync();
            }
            if (result.questionRelated && current.questionScreenshot) {
                void evidence?.outbox.enqueue({
                    endpoint: '/Student/Exam/IncidentEvidence', stage: 'screen-image',
                    logId: current.detectionLogId, screenshot: current.questionScreenshot, capturedAt: current.capturedAt,
                    attemptId: config.attemptId, sessionAttemptId: config.sessionId
                });
            }
            if (result.contextAlert) {
                incidentAlert(`speech-context:${current.detectionLogId}`, 'POSSIBLE EXAM-RELATED CONVERSATION DETECTED',
                    'Speech near the microphone may relate to an exam question. Your teacher can review the context and recording.');
                const question = Number(result.questionId || current.questionId || 0);
                status('speechMonitorStatus', `Possible discussion related to ${question > 0 ? 'question #' + question : 'the examination'} — teacher review requested.`, true);
                void window.ProctoringRealtime.sync();
            }
        }
        let audioStarting = false;
        async function audio() {
            if (!active() || !stream?.getAudioTracks().length) return;
            if (audioStarting || stopAudio) return;
            audioStarting = true;
            const track = stream.getAudioTracks()[0];
            try {
                const url = new URL(config.urls.speech, location.href);
                url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
                url.searchParams.set('attemptId', config.attemptId); url.searchParams.set('sessionAttemptId', config.sessionId);
                const cleanup = await ExamSpeechStream.start({
                    stream: new MediaStream([track]), url: url.href, token, question: config.question,
                    suppressed: () => !active() || voice.blocked,
                    // Preserve speech monitoring when the streaming WebSocket is still
                    // connecting or reconnecting; the server remains authoritative.
                    detectFallback: async (clip, questionId) => {
                        const data = body();
                        data.append('Audio', clip, 'speech.wav');
                        if (questionId) data.append('QuestionId', questionId);
                        return request('detect', data, 5000);
                    },
                    gap: gap => evidence?.outbox.enqueue({ audioGap: gap, attemptId: config.attemptId, sessionAttemptId: config.sessionId, capturedAt: Date.now() }),
                    evidence: packet => evidence?.outbox.enqueue({
                        endpoint: '/Student/Exam/IncidentEvidence', stage: 'audio-original',
                        logId: packet.logId, blob: packet.clip, capturedAt: packet.capturedAt,
                        metadata: packet.metadata, attemptId: config.attemptId, sessionAttemptId: config.sessionId
                    }),
                    snapshot: () => evidence && !evidence.recorder.status().cameraOnly && !document.hidden ? evidence.recorder.captureScreenDataUrl?.() : null,
                    status: text => status('speechMonitorStatus', speechAnalysisIssue ? `${text} · ${speechAnalysisIssue}` : text,
                        !!speechAnalysisIssue || !['Live speech monitoring active', 'Microphone screening active (HTTP)'].includes(text)), transcribe,
                    result: result => {
                        if (result.speechDetected && result.detectionLogId) {
                            evidence?.stageSpeech(result.detectionLogId);
                            void evidence?.commitSpeech(result.detectionLogId);
                        }
                        if (result.state) window.ProctoringRealtime.apply(result.state, !result.softReminderId);
                        if (result.softReminderId) window.ProctoringRealtime.speechReminder(result.softReminderId);
                        if (result.notifyStudent) {
                            const candidate = result.speechKind === 'candidate_speech_supported';
                            const noise = result.speechKind === 'environmental_noise_uncertain';
                            const message = noise
                                ? 'Loud environmental sound was heard near your device. Please choose a quieter place for your next session. Your teacher can review this observation.'
                                : candidate
                                    ? 'Speech was detected while your mouth movement was visible. Please do not speak during this examination.'
                                    : 'Speech was heard near your microphone. Please avoid conversations during this examination; your teacher has been notified for review.';
                            incidentAlert(`speech:${result.detectionLogId || result.logIds?.[0]}`,
                                candidate ? 'PLEASE DO NOT TALK' : noise ? 'BACKGROUND NOISE DETECTED' : 'SPEECH DETECTED',
                                message);
                            status('speechMonitorStatus', message, true);
                        }
                        evidence?.attach(result.logIds || []);
                    }
                });
                if (stopped || track.readyState !== 'live' || !stream.getAudioTracks().includes(track)) {
                    cleanup();
                    if (!stopped) later(audio, 0);
                } else stopAudio = cleanup;
            } catch (error) { status('speechMonitorStatus', window.ExamHttp.describe(error), true); later(audio, 3000); }
            finally { audioStarting = false; }
        }
        let videoLanesStarted = false;
        let evidenceStarting = false;
        function startEvidence() {
            if (!evidence || evidenceStarting || !stream.getVideoTracks().length) return;
            if (evidence.recorder.running) {
                evidence.recorder.resumeCameraOriginal();
                return;
            }
            evidenceStarting = true;
            void evidence.start().then(() => {
                if (identitySequenceActive || physicallyAway) evidence?.startIdentityCapture();
                const deferred = pendingFullscreenEvidence.splice(0);
                const endedBeforeCapture = new Set(deferred.filter(({ event }) => event.phase === 'end' || event.phase === 'stop')
                    .map(({ event }) => event.id));
                for (const { event, at } of deferred) {
                    const delaySeconds = (performance.now() - at) / 1000;
                    // An episode that finished before capture cannot be represented by later footage.
                    if (!endedBeforeCapture.has(event.id) && delaySeconds <= 15)
                        evidence?.browserEvent('FullScreenExit', { ...event, duration: event.duration + delaySeconds }, false);
                }
                return attachScreenEvidence();
            }).catch(error => {
                evidence.recorder.stop();
                status('evidenceStatus', 'Recording unavailable: ' + error.message, true);
            }).finally(() => { evidenceStarting = false; });
        }
        async function startCameraConsumers() {
            if (video.srcObject !== stream) video.srcObject = stream;
            await video.play();
            if (stopped || videoLanesStarted) return;
            videoLanesStarted = true;
            lane(identity, Math.max(10000, Math.min(30000, Number(config.identitySeconds || 30) * 1000)), 'identityMonitorStatus');
            lane(() => videoModel('behavior', 'behaviorMonitorStatus', 'attention'), 250, 'behaviorMonitorStatus');
            lane(() => videoModel('behavior', 'absenceMonitorStatus', 'absence'), 400, 'absenceMonitorStatus');
            lane(() => videoModel('behavior', 'peopleMonitorStatus', 'persons'), 400, 'peopleMonitorStatus');
            lane(() => videoModel('objects', 'objectMonitorStatus'), 350, 'objectMonitorStatus');
        }
        function acquireDevice(kind) {
            if (stopped || stream.getTracks().some(t => t.kind === kind && t.readyState === 'live')) return;
            if (mediaRequests.has(kind)) return mediaRequests.get(kind);
            const constraints = window.MediaCapabilities?.constraints(kind) ||
                (kind === 'video' ? { width: { ideal: 1280 }, height: { ideal: 720 } } :
                    { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 });
            const pending = (async () => {
                const inherited = capture?.camera.getTracks().find(t => t.kind === kind && t.readyState === 'live');
                let source;
                try {
                    if (!inherited) source = await navigator.mediaDevices.getUserMedia({ [kind]: constraints });
                    if (kind === 'video') cameraPermissionDenied = false;
                    else microphonePermissionDenied = false;
                } catch (error) {
                    const denied = ['NotAllowedError', 'PermissionDeniedError', 'SecurityError'].includes(error.name);
                    if (kind === 'video') cameraPermissionDenied = denied;
                    else microphonePermissionDenied = denied;
                    throw error;
                }
                if (stopped || !active()) { source?.getTracks().forEach(track => track.stop()); return; }
                const track = inherited || source.getTracks().find(t => t.kind === kind && t.readyState === 'live');
                source?.getTracks().filter(t => t !== track).forEach(t => t.stop());
                if (!track) throw new Error(`No live ${kind} track returned`);
                if (capture && !inherited) capture.camera.addTrack(track);
                stream.addTrack(track);
                if (kind === 'video') on(track, 'unmute', () => evidence?.recorder.resumeCameraOriginal());
                on(track, 'ended', () => {
                    stream.removeTrack(track);
                    capture?.camera.removeTrack(track);
                    if (kind === 'audio') {
                        const cleanup = stopAudio;
                        stopAudio = null;
                        try { cleanup?.(); } catch (error) { console.warn('Microphone cleanup failed', error); }
                    }
                    if (kind === 'video') {
                        invalidateIdentitySequence();
                        cameraCalibrationRequired = true;
                        behaviorReady = false;
                    }
                    if (!active()) return;
                    later(async () => {
                        if (!active()) return;
                        const name = kind === 'video' ? 'camera' : 'microphone';
                        try {
                            const permission = await navigator.permissions?.query({ name });
                            if (permission?.state === 'denied') {
                                if (kind === 'video') cameraPermissionDenied = true;
                                else microphonePermissionDenied = true;
                            }
                            if (permission?.state === 'granted' && active()) await acquireDevice(kind);
                        } catch { /* The next health sample reports failure and offers the existing recovery path. */ }
                    }, 500);
                });
                const detail = window.MediaCapabilities?.inspect(track);
                if (detail) window.dispatchEvent(new CustomEvent('media-capabilities', { detail }));
                if (kind === 'video') void startCameraConsumers().catch(error => status('mediaStatus', error.message, true));
                else void audio();
                if (kind === 'video') startEvidence();
            })().finally(() => mediaRequests.delete(kind));
            mediaRequests.set(kind, pending);
            return pending;
        }
        async function attachScreenEvidence() {
            if (!evidence?.recorder.running || !window.ExamDisplayCapture?.live(display)) return;
            try { await evidence.recorder.attachScreen(display); }
            catch { status('evidenceStatus', 'Screen evidence is unavailable. Live sharing continues.', true); }
        }
        const restoreDisplay = document.getElementById('restoreExamDisplay');
        let observedDisplayTrack;
        let displayIssueSince = null;
        const displayEpisodeKey = episodeKey + ':display';
        let displayEpisode = null;
        try { displayEpisode = JSON.parse(sessionStorage.getItem(displayEpisodeKey)); } catch { }
        function reportDisplayHealth() {
            if (!active()) return;
            const track = window.ExamDisplayCapture?.live(display);
            const now = Date.now();
            if (!track || track.muted) {
                displayIssueSince ??= now;
                if (displayEpisode || now - displayIssueSince < 3000) return;
                displayEpisode = {
                    id: crypto.randomUUID(),
                    kind: observedDisplayTrack ? 'ScreenShareLost' : 'ScreenCaptureUnavailable',
                    startedAt: new Date(displayIssueSince).toISOString(),
                    surface: observedDisplayTrack?.getSettings?.().displaySurface || 'unknown'
                };
                try { sessionStorage.setItem(displayEpisodeKey, JSON.stringify(displayEpisode)); } catch { }
                void deliverBrowserObservation({ ...displayEpisode, phase: 'start', duration: (now - displayIssueSince) / 1000,
                    observedAt: new Date(now).toISOString(), attemptId: config.attemptId, sessionAttemptId: config.sessionId });
                return;
            }
            displayIssueSince = null;
            if (!displayEpisode) return;
            const ended = displayEpisode;
            displayEpisode = null;
            try { sessionStorage.removeItem(displayEpisodeKey); } catch { }
            void deliverBrowserObservation({ ...ended, phase: 'end',
                duration: Math.max(0, (now - Date.parse(ended.startedAt)) / 1000),
                observedAt: new Date(now).toISOString(), attemptId: config.attemptId, sessionAttemptId: config.sessionId });
        }
        function showDisplay() {
            const track = window.ExamDisplayCapture?.live(display);
            if (restoreDisplay) restoreDisplay.hidden = !!track && !track.muted;
            status('liveDisplayStatus', !track ? 'Display sharing unavailable. Restore sharing to continue monitoring.' :
                track.muted ? 'Display sharing is interrupted. Restore sharing if it does not resume.' : 'Entire display shared for live monitoring',
                !track || track.muted);
            reportDisplayHealth();
            if (!track || track === observedDisplayTrack) return;
            observedDisplayTrack = track;
            for (const event of ['ended', 'mute', 'unmute']) on(track, event, () => {
                if (display?.getVideoTracks().includes(track)) showDisplay();
            });
        }
        on(restoreDisplay, 'click', async () => {
            const resume = guard.suspend();
            restoreDisplay.disabled = true;
            try {
                const replacement = await ExamDisplayCapture.requestDisplay();
                if (stopped || config.submitted()) { ExamDisplayCapture.stop(replacement); return; }
                ExamDisplayCapture.stop(display);
                display = replacement;
                if (capture) capture.display = replacement;
                showDisplay();
                void attachScreenEvidence();
            } catch (error) { status('liveDisplayStatus', error.message, true); }
            finally { restoreDisplay.disabled = false; resume(); }
        });
        showDisplay();
        try { stopSharing = LiveProctoring.student(config.attemptId, token, () => stopped ? null : stream,
            () => {}, () => stopped ? null : display); }
        catch (error) { console.warn('Live monitoring startup failed', error); }
        async function startMedia() {
            try {
                const results = await Promise.allSettled([
                    acquireDevice('video'), acquireDevice('audio')
                ]);
                if (stopped) return;
                initialMediaSettled = true;
                const failed = results.some(result => result.status === 'rejected');
                if (!failed) status('mediaStatus', 'Checking camera frames and microphone audio…');
                try {
                const { ObservationEvidence } = await import('/js/proctoring/observation-evidence.js?v=fullscreen-observations');
                if (stopped) return;
                evidence = new ObservationEvidence({
                    stream, token, attemptId: config.attemptId, sessionAttemptId: config.sessionId,
                    owner: config.owner,
                    uploadUrl: config.urls.evidence, recordingUrl: config.urls.recording,
                    onStatus: text => status('evidenceStatus', text)
                });
                // Start evidence first: early face/voice incidents must not be lost
                // while the recorder is still initializing.
                startEvidence();
                } catch (error) { status('evidenceStatus', 'Evidence initialization failed: ' + error.message, true); }
                await window.ProctoringRealtime.sync();
            } catch (error) { status('mediaStatus', `Monitoring needs attention: ${error.message}`, true); }
        }
        function stop(preserveBrowserEpisode = false) {
            if (stopped) return;
            if (!preserveBrowserEpisode) guard.stop();
            stopped = true; lifetime.abort(); timers.forEach(clearTimeout); timers.clear();
            for (const cleanup of [stopAudio, stopSharing, () => evidence?.stop(), () => voice.stop()]) {
                try { cleanup?.(); } catch (error) { console.warn('Exam media cleanup failed', error); }
            }
            if (!capture) {
                stream.getTracks().forEach(track => track.stop());
                window.ExamDisplayCapture?.stop(display);
            }
            video.srcObject = null;
            examMediaSessions.delete(form);
        }
        on(window, 'pagehide', event => {
            guard.pagehide();
            if (!event.persisted) stop(true);
        });
        on(window, 'pageshow', () => { guard.sample(); flushBrowserObservations(); });
        // Device health runs independently of getUserMedia and all AI lanes.
        // Camera/mic denial, disconnect or failed initialization must reach SQL.
        lane(deviceHealth, 3000, 'mediaStatus');
        lane(reportDisplayHealth, 3000, 'liveDisplayStatus');
        const session = { identity, stop, finishEvidence: () => {
            const finalized = evidence?.stop();
            window.ExamDisplayCapture?.stop(display);
            return finalized;
        } };
        examMediaSessions.set(form, session);
        void startMedia();
        return session;
    }
};






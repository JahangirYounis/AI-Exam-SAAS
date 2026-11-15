(() => {
    let capture;
    const stop = stream => stream?.getTracks().forEach(track => track.stop());
    const live = stream => stream?.getVideoTracks().find(track => track.readyState === 'live');

    async function requestDisplay() {
        if (!navigator.mediaDevices?.getDisplayMedia)
            throw new Error('This browser cannot share your display. Open the exam in a supported desktop browser over HTTPS.');
        let stream;
        try {
            stream = await navigator.mediaDevices.getDisplayMedia({
                video: { displaySurface: 'monitor', width: { ideal: 1280 }, frameRate: { ideal: 5, max: 8 } },
                audio: false
            });
        } catch (error) {
            if (['NotAllowedError', 'AbortError'].includes(error.name))
                throw new Error('Screen sharing was not granted. Select Start exam again to retry.');
            throw new Error('Screen sharing could not start. Check your browser and system permissions, then retry.');
        }
        if (live(stream)?.getSettings?.().displaySurface !== 'monitor') {
            stop(stream);
            throw new Error('Select Entire Screen in the browser picker. This exam requires a verified entire display.');
        }
        return stream;
    }

    function release() {
        stop(capture?.display);
        stop(capture?.camera);
        capture = undefined;
    }

    function openSession(path, camera, display) {
        const url = new URL(path, location.href);
        const id = Number(url.searchParams.get('sessionAttemptId'));
        if (url.origin !== location.origin || url.pathname !== '/Student/Exam/Session' || !id || !live(display))
            throw new Error('The exam or display is unavailable. Please try again.');
        release();
        capture = { id, camera: camera || new MediaStream(), display, release };
        const frame = document.createElement('iframe');
        frame.title = 'Examination';
        frame.allow = 'camera; microphone; fullscreen; display-capture';
        frame.allowFullscreen = true;
        frame.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;border:0;background:white';
        frame.src = url.href;
        document.body.replaceChildren(frame);
        history.replaceState(null, '', `/Student/Exam/Session?sessionAttemptId=${id}`);
        frame.addEventListener('load', () => {
            try {
                const destination = frame.contentWindow.location;
                if (destination.pathname === '/Student/Exam/Session') return;
                if (destination.origin === location.origin) location.replace(destination.href);
            } catch { /* The frame only permits same-origin exam responses. */ }
        });
    }

    function session(id) {
        try {
            const host = window.parent !== window ? window.parent.ExamDisplayCapture : window.ExamDisplayCapture;
            return host?.current(Number(id));
        } catch { return undefined; }
    }

    window.ExamDisplayCapture = {
        requestDisplay, openSession, session, live, stop, release,
        current: id => capture?.id === id ? capture : undefined
    };
    window.addEventListener('pagehide', release);
})();

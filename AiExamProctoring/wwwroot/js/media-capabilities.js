(function (root) {
    'use strict';
    function constraints(kind, supported = navigator.mediaDevices.getSupportedConstraints?.() || {}) {
        const desired = kind === 'video'
            ? { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 24, max: 30 }, facingMode: 'user' }
            : { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: { ideal: 1 } };
        const constraints=Object.fromEntries(Object.entries(desired).filter(([key]) => supported[key]));
        if(kind==='audio'||kind==='video') {
            try {const selected=sessionStorage.getItem(kind==='audio'?'examMicrophoneId':'examCameraId');if(selected)constraints.deviceId={ideal:selected};}catch{}
        }
        return constraints;
    }
    function inspect(track) {
        // Deliberately omit device/group IDs from diagnostics.
        let settings = {}, capabilities = {};
        try { settings = track.getSettings?.() || {}; } catch { /* Older browser */ }
        try { capabilities = track.getCapabilities?.() || {}; } catch { /* Optional API */ }
        const keys = ['width', 'height', 'frameRate', 'sampleRate', 'channelCount',
            'echoCancellation', 'noiseSuppression', 'autoGainControl', 'exposureMode', 'focusMode', 'whiteBalanceMode'];
        const pick = source => Object.fromEntries(keys.filter(key => source[key] !== undefined).map(key => [key, source[key]]));
        return { kind: track.kind, settings: pick(settings), capabilities: pick(capabilities) };
    }
    function captureState(video) {
        if (!video || video.readyState < 2 || video.videoWidth < 160 || video.videoHeight < 120)
            return null;
        try {
            const canvas = document.createElement('canvas');
            canvas.width = 32; canvas.height = 24;
            const context = canvas.getContext('2d', { willReadFrequently: true });
            if (!context) return null;
            context.drawImage(video, 0, 0, 32, 24);
            const pixels = context.getImageData(0, 0, 32, 24).data;
            let hash = 2166136261, sum = 0;
            for (let i = 0; i < pixels.length; i += 4) {
                sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
                for (let k = 0; k < 3; k++) {
                    hash ^= pixels[i + k];
                    hash = Math.imul(hash, 16777619) >>> 0;
                }
            }
            return { fingerprint: hash.toString(16).padStart(8, '0'),
                brightness: sum / (pixels.length / 4),
                width: video.videoWidth, height: video.videoHeight };
        } catch { return null; }
    }
    root.MediaCapabilities = { constraints, inspect, captureState };
})(typeof window === 'undefined' ? globalThis : window);

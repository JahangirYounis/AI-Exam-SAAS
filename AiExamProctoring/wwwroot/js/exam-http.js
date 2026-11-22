window.ExamHttp = (() => {
    async function readJson(response, { allowBlockedState = false } = {}) {
        const correlationId = response.headers.get('x-correlation-id') || '';
        const failure = (code, message) => Object.assign(new Error(message), { code, status: response.status, correlationId });
        const type = (response.headers.get('content-type') || '').split(';')[0].trim().toLowerCase();
        if (type !== 'application/json' && !type.endsWith('+json'))
            throw failure('INVALID_RESPONSE', response.status === 401 || response.redirected
                ? 'Your session may have expired. Sign in again before continuing.'
                : 'The exam service returned an unreadable response. Retry or contact your supervisor.');
        let result;
        try { result = await response.json(); }
        catch { throw failure('INVALID_JSON', 'The exam service returned an incomplete response. Please retry.'); }
        if (!result || typeof result !== 'object' || Array.isArray(result))
            throw failure('INVALID_JSON', 'The exam service returned an unexpected response. Please retry.');
        // Only the monitoring caller opts in to a validated protective-lock reply.
        // This lets it freeze the UI and attach evidence before handling HTTP 423.
        if (!response.ok && response.status === 423 && allowBlockedState && result.blocked === true &&
            result.state && typeof result.state === 'object' && !Array.isArray(result.state)) return result;
        if (!response.ok) {
            const identityQualityReasons = ['poor_lighting', 'blurry_face', 'low_face_quality',
                'presentation_framing', 'presentation_unconfirmed', 'no_face', 'multiple_faces',
                'face_too_small', 'low_detection_confidence', 'borderline_score'];
            const identityQuality = response.status === 422 && result.identityState === 'FACE_TRACKING_UNCERTAIN' &&
                identityQualityReasons.includes(result.reasonCode) && typeof result.message === 'string' &&
                result.message.length <= 500;
            const error = failure('HTTP_ERROR', response.status >= 500
                ? 'The exam service is temporarily unavailable. Your device may still be connected.'
                : response.status === 401 ? 'Your session has expired. Please sign in again.'
                    : response.status === 429 ? 'Monitoring request limit reached. Waiting before retrying.'
                    : identityQuality ? result.message
                    : response.status === 413 ? 'The monitoring upload is too large. Retry with a fresh capture.'
                    : response.status === 415 ? 'The monitoring capture format was not accepted. Retry with a fresh capture.'
                    : 'The exam service could not accept this request. Retry or contact your supervisor.');
            error.correlationId = result.correlationId || correlationId;
            const retrySeconds = Number(response.headers.get('retry-after'));
            if (Number.isFinite(retrySeconds) && retrySeconds > 0)
                error.retryAfterMilliseconds = Math.min(60000, retrySeconds * 1000);
            if (identityQuality) error.reasonCode = result.reasonCode;
            throw error;
        }
        return result;
    }
    function describe(error) {
        if (error?.name === 'TimeoutError') return 'The monitoring service took too long to respond. Retrying; this is not a device disconnection.';
        if (error?.name === 'AbortError') return 'The monitoring check was cancelled.';
        if (error?.code) return error.message;
        return 'Monitoring is temporarily unavailable. Retry or contact your supervisor.';
    }
    return { readJson, describe };
})();

/* Pure examination UI state helpers. The server alone decides actual write eligibility. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    root.ExamAttemptState = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    return Object.freeze({
        canNavigate(current, target, count, allowBack) {
            return Number.isInteger(current) && Number.isInteger(target) &&
                Number.isInteger(count) && count > 0 && target >= 0 && target < count &&
                (allowBack === true || target >= current);
        },
        secondsRemaining(expiresAt, serverAtLoad, elapsedMilliseconds) {
            if (![expiresAt, serverAtLoad, elapsedMilliseconds].every(Number.isFinite)) return 0;
            return Math.max(0, Math.ceil((expiresAt - serverAtLoad - Math.max(0, elapsedMilliseconds)) / 1000));
        },
        answeredCount(values) {
            return [...values].filter(value => typeof value === 'string' && /^[ABCDE]$/.test(value)).length;
        },
        hasUnsaved(items) {
            return [...items].some(item => item.value !== item.persisted);
        }
    });
});

/* Coalesce blur, visibility and fullscreen exit into one departure episode.
   Explicit browser permission prompts are suspended by the caller. */
(function (root) {
    function create(options) {
        let hiddenSince = null, exitSince = null, fullscreenSeen = false;
        let incidentSent = false, prompts = 0, settleUntil = 0;
        return {
            suspend() {
                prompts++;
                hiddenSince = exitSince = null;
                fullscreenSeen = false;
                let ended = false;
                return () => { if (!ended) { ended = true; prompts--; settleUntil = options.now() + 1000; } };
            },
            sample() {
                const now = options.now();
                const s = options.state();
                if (!s.active || prompts || now < settleUntil) {
                    hiddenSince = exitSince = null;
                    return;
                }
                if (s.fullscreen) { fullscreenSeen = true; exitSince = null; }
                const away = s.hidden === true; // Focus loss alone is not a page departure.
                // Background tabs can pause timers. Evaluate the completed hidden
                // interval on return before clearing it, otherwise departures disappear.
                const completedDeparture = !away && hiddenSince !== null && now - hiddenSince >= 600;
                if (!away) hiddenSince = null;
                else hiddenSince ??= now;
                if (s.requireFullscreen && fullscreenSeen && !s.fullscreen) exitSince ??= now;
                else exitSince = null;
                if (!completedDeparture && !away && (!s.requireFullscreen || s.fullscreen || !fullscreenSeen)) incidentSent = false;
                if (incidentSent) return;
                let type = null;
                if (completedDeparture || (hiddenSince !== null && now - hiddenSince >= 600)) type = 'TabSwitch';
                else if (!s.hidden && exitSince !== null && now - exitSince >= 1500) type = 'FullScreenExit';
                if (type) {
                    incidentSent = true;
                    options.report(type, type === 'TabSwitch'
                        ? 'The exam page was hidden for at least 0.6 seconds.'
                        : 'Fullscreen was exited and not restored for at least 1.5 seconds.');
                }
            }
        };
    }
    root.ExamBrowserGuard = { create };
})(typeof window === 'undefined' ? globalThis : window);

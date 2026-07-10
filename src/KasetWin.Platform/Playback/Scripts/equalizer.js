// equalizer.js — a best-effort 9-band Web Audio graphic equalizer for the hidden YouTube Music
// playback <video> element. Injected at document-created and driven at runtime by the native
// controller via ExecuteScriptAsync('window.__kasetSetEq(enabled, gainsArray)').
//
// DESIGN / SAFETY
//  - The audio element is only ever routed through the Web Audio graph the FIRST time the user
//    ENABLES the equalizer. A user who never opens the EQ keeps the browser's default audio path
//    untouched, so this can never silence playback for them.
//  - Once hooked, "disabled" simply flattens every band to 0 dB (a peaking filter at 0 dB gain is
//    transparent), so toggling off is audibly identical to the original signal.
//  - Every DOM / Web Audio call is wrapped in try/catch; a failure leaves audio as-is.
//
// SECURITY: never read or post cookies, tokens, or SAPISID values.
(function () {
    'use strict';

    if (window.__kasetEqInstalled) {
        return;
    }
    window.__kasetEqInstalled = true;

    // Standard 9-band centre frequencies (Hz), matching the settings UI labels.
    var FREQS = [62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    var state = {
        enabled: false,
        gains: [0, 0, 0, 0, 0, 0, 0, 0, 0],
        ctx: null,
        source: null,
        filters: null,
        hookedEl: null
    };
    window.__kasetEqState = state;

    function currentVideo() {
        try {
            return document.querySelector('video');
        } catch (e) {
            return null;
        }
    }

    // Build the AudioContext + filter chain and splice it between the media element and the
    // speakers. Returns true when the graph is live. Only called once we actually need EQ.
    function ensureGraph() {
        var el = currentVideo();
        if (!el) {
            return false;
        }

        // Already hooked to this very element — nothing to do.
        if (state.hookedEl === el && state.filters) {
            return true;
        }

        try {
            if (!state.ctx) {
                var Ctor = window.AudioContext || window.webkitAudioContext;
                if (!Ctor) {
                    return false;
                }
                state.ctx = new Ctor();
            }

            // A media element can be tapped by createMediaElementSource only once per element.
            if (el.__kasetEqSource) {
                state.source = el.__kasetEqSource;
            } else {
                state.source = state.ctx.createMediaElementSource(el);
                el.__kasetEqSource = state.source;
            }

            var filters = [];
            for (var i = 0; i < FREQS.length; i++) {
                var f = state.ctx.createBiquadFilter();
                f.type = 'peaking';
                f.frequency.value = FREQS[i];
                f.Q.value = 1.0;
                f.gain.value = state.enabled ? state.gains[i] : 0;
                filters.push(f);
            }

            // source → f0 → f1 → … → f8 → destination
            state.source.disconnect();
            var node = state.source;
            for (var j = 0; j < filters.length; j++) {
                node.connect(filters[j]);
                node = filters[j];
            }
            node.connect(state.ctx.destination);

            state.filters = filters;
            state.hookedEl = el;

            // Autoplay policies can leave the context suspended; resume best-effort.
            if (state.ctx.state === 'suspended' && typeof state.ctx.resume === 'function') {
                state.ctx.resume();
            }
            return true;
        } catch (e) {
            // Hooking failed — leave the default audio path in place.
            state.filters = null;
            state.hookedEl = null;
            return false;
        }
    }

    function applyGains() {
        if (!state.filters) {
            return;
        }
        for (var i = 0; i < state.filters.length; i++) {
            try {
                state.filters[i].gain.value = state.enabled ? (state.gains[i] || 0) : 0;
            } catch (e) { /* best-effort */ }
        }
    }

    // Public entry point: enabled flag + array of 9 dB gains (-12..+12).
    window.__kasetSetEq = function (enabled, gains) {
        state.enabled = !!enabled;
        if (Object.prototype.toString.call(gains) === '[object Array]') {
            for (var i = 0; i < FREQS.length; i++) {
                var g = Number(gains[i]);
                state.gains[i] = isNaN(g) ? 0 : Math.max(-12, Math.min(12, g));
            }
        }

        // Only touch the audio path once EQ is actually turned on.
        if (state.enabled) {
            if (ensureGraph()) {
                applyGains();
            }
        } else if (state.filters) {
            applyGains(); // flatten to transparent, keep the (harmless) graph in place
        }
        return !!state.filters;
    };

    // If the SPA swaps the <video> element while EQ is on, re-hook it lazily.
    setInterval(function () {
        if (state.enabled && state.hookedEl && state.hookedEl !== currentVideo()) {
            if (ensureGraph()) {
                applyGains();
            }
        }
    }, 4000);
})();

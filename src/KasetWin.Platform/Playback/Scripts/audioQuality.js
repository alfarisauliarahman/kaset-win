// audioQuality.js — installs a best-effort audio-quality preference applier on the
// YouTube Music watch page (Req 7). Injected at document-created so the preference is
// honored as the player boots, and re-invoked at runtime by the native controller via
// ExecuteScriptAsync('window.__kasetApplyAudioQuality("highres")') when the user changes
// the preference mid-playback (Req 7.2).
//
// The YouTube-facing values are: small (low) / medium (medium) / highres (high).
// Treat this as a *preference request* plus best-effort instrumentation, NOT a guarantee:
// YouTube may still pick the final stream by entitlement, availability, or network.
//
// SECURITY: never read or post cookies, tokens, or SAPISID values.
(function () {
    'use strict';

    if (window.__kasetAudioQualityInstalled) {
        return;
    }
    window.__kasetAudioQualityInstalled = true;

    function candidatePlayers() {
        var players = [];
        try {
            var ytmusic = document.querySelector('ytmusic-player');
            if (ytmusic) {
                players.push(ytmusic);
                if (ytmusic.playerApi) {
                    players.push(ytmusic.playerApi);
                }
            }
            var movie = document.getElementById('movie_player');
            if (movie) {
                players.push(movie);
            }
            if (window.yt && window.yt.player) {
                players.push(window.yt.player);
            }
        } catch (e) {
            // best-effort
        }
        return players;
    }

    function applyToPlayer(player, quality) {
        if (!player) {
            return false;
        }
        var applied = false;
        try {
            if (typeof player.setAudioQuality === 'function') {
                player.setAudioQuality(quality);
                applied = true;
            }
        } catch (e) { /* best-effort */ }

        var optionAttempts = [
            ['audio', 'quality'],
            ['audio', 'audioQuality'],
            ['player', 'audioQuality'],
            ['player', 'audio_quality'],
            ['playback', 'audioQuality'],
            ['playback', 'audio_quality']
        ];
        for (var i = 0; i < optionAttempts.length; i++) {
            try {
                if (typeof player.setOption === 'function') {
                    player.setOption(optionAttempts[i][0], optionAttempts[i][1], quality);
                    applied = true;
                }
            } catch (e) { /* best-effort */ }
        }
        return applied;
    }

    // Public entry point invoked at boot and by the native controller at runtime.
    window.__kasetApplyAudioQuality = function (quality) {
        if (typeof quality !== 'string' || quality === '') {
            quality = window.__kasetPlaybackAudioQuality;
        }
        if (typeof quality !== 'string' || quality === '') {
            return false;
        }
        window.__kasetPlaybackAudioQuality = quality;

        var players = candidatePlayers();
        var appliedAny = false;
        for (var i = 0; i < players.length; i++) {
            if (applyToPlayer(players[i], quality)) {
                appliedAny = true;
            }
        }
        return appliedAny;
    };

    // Apply the stored preference once the player has had a chance to boot.
    function bootApply() {
        if (typeof window.__kasetPlaybackAudioQuality === 'string') {
            window.__kasetApplyAudioQuality(window.__kasetPlaybackAudioQuality);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            setTimeout(bootApply, 1500);
        });
    } else {
        setTimeout(bootApply, 1500);
    }
})();

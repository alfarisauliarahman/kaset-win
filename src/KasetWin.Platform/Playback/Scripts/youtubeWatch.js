// youtubeWatch.js — injected into every www.youtube.com/watch page via
// CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync (Req 32.2). It is the
// regular-YouTube (video) counterpart of observer.js (music). It targets the
// youtube.com watch-page DOM (#movie_player) rather than the ytmusic-* player,
// and is the ONLY channel from the (untrusted) page to the native
// YouTubeWatchController; native validates every message shape.
//
// Mirrors the macOS WKUserScript observer/extraction in
// Sources/Kaset/Views/YouTube/YouTubeWatchWebView+Scripts.swift.
//
// Messages posted to native via window.chrome.webview.postMessage:
//   { type: 'STATE_UPDATE', isPlaying, progress, duration, videoId, title, isAd }
//   { type: 'VIDEO_ENDED', videoId }
//
// It also hides all YouTube chrome and leaves only the video surface visible so
// the WebView can be docked into the native watch page (metadata/comments/related
// are rendered natively). Defines window.__kasetExtractVideo() and runs it; the
// controller re-invokes it on NavigationCompleted for cached/fast loads.
//
// SECURITY: never read or post cookies, tokens, or SAPISID values.
(function () {
    'use strict';

    // Guard against double-injection on the same document.
    if (window.__kasetWatchObserverInstalled) {
        return;
    }
    window.__kasetWatchObserverInstalled = true;

    // Native owns audio; default to full volume unless the host overrides it.
    if (typeof window.__kasetTargetVolume !== 'number') {
        window.__kasetTargetVolume = 1;
    }

    var bridge = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;

    var lastVideoId = '';

    function post(message) {
        if (!bridge) {
            return;
        }
        try {
            bridge.postMessage(message);
        } catch (e) {
            // Swallow: the page must never break because the bridge is unavailable.
        }
    }

    function moviePlayer() {
        return document.getElementById('movie_player');
    }

    function videoEl() {
        return document.querySelector('#movie_player video') || document.querySelector('video');
    }

    function videoData() {
        try {
            var player = moviePlayer();
            if (player && typeof player.getVideoData === 'function') {
                return player.getVideoData();
            }
        } catch (e) {
            return null;
        }
        return null;
    }

    function currentVideoId() {
        var data = videoData();
        return (data && (data.video_id || data.videoId)) || '';
    }

    function currentTitle() {
        var data = videoData();
        if (data && data.title) {
            return '' + data.title;
        }
        return (document.title || '').replace(/ - YouTube$/, '');
    }

    function isAdShowing() {
        var player = moviePlayer();
        return !!(player && player.classList && player.classList.contains('ad-showing'));
    }

    function enforceVolume(video) {
        if (!video || window.__kasetIsSettingVolume) {
            return;
        }
        var target = window.__kasetTargetVolume;
        if (typeof target !== 'number') {
            return;
        }
        // YouTube persists its own mute state across sessions; Kaset owns audio,
        // so unmute whenever the target volume is audible.
        if (target > 0 && video.muted) {
            video.muted = false;
            var player = moviePlayer();
            if (player && typeof player.unMute === 'function') {
                try { player.unMute(); } catch (e) { /* best-effort */ }
            }
        }
        if (Math.abs(video.volume - target) > 0.01) {
            window.__kasetIsSettingVolume = true;
            video.volume = target;
            setTimeout(function () { window.__kasetIsSettingVolume = false; }, 50);
        }
    }

    function sendUpdate() {
        var video = videoEl();
        if (!video) {
            return;
        }
        var videoId = currentVideoId();
        if (videoId !== '') {
            lastVideoId = videoId;
        }
        post({
            type: 'STATE_UPDATE',
            isPlaying: !video.paused && !video.ended,
            progress: (video.currentTime && isFinite(video.currentTime)) ? video.currentTime : 0,
            duration: (video.duration && isFinite(video.duration)) ? video.duration : 0,
            videoId: videoId,
            title: currentTitle(),
            isAd: isAdShowing()
        });
    }

    function sendEnded() {
        post({
            type: 'VIDEO_ENDED',
            videoId: lastVideoId || currentVideoId()
        });
    }

    function disableAutonav() {
        try {
            var toggle = document.querySelector('.ytp-autonav-toggle-button');
            if (toggle && toggle.getAttribute('aria-checked') === 'true') {
                toggle.click();
            }
        } catch (e) {
            // best-effort
        }
    }

    function attach() {
        var video = videoEl();
        if (!video || video.__kasetAttached) {
            return;
        }
        video.__kasetAttached = true;

        ['play', 'playing', 'pause', 'seeked', 'loadedmetadata'].forEach(function (evt) {
            video.addEventListener(evt, sendUpdate);
        });
        video.addEventListener('ended', sendEnded);
        video.addEventListener('volumechange', function () { enforceVolume(video); });

        disableAutonav();
        enforceVolume(video);
        sendUpdate();
    }

    // ---- Video-surface extraction: hide YouTube chrome, keep only the video ----
    var extractStyleId = 'kaset-yt-video-style';

    window.__kasetExtractVideo = function () {
        var style = document.getElementById(extractStyleId);
        if (!style) {
            style = document.createElement('style');
            style.id = extractStyleId;
            (document.head || document.documentElement).appendChild(style);
        }

        style.textContent =
            'html, body, * { visibility: hidden !important; }' +
            '.kaset-visible {' +
            '  visibility: visible !important; opacity: 1 !important;' +
            '  padding: 0 !important; margin: 0 !important; background: #000 !important;' +
            '  width: 100vw !important; height: 100vh !important;' +
            '  position: fixed !important; top: 0 !important; left: 0 !important; overflow: visible !important;' +
            '}' +
            'video.kaset-visible { z-index: 2147483647 !important; object-fit: contain !important; }' +
            '.ytp-caption-window-container, .ytp-caption-window-container *,' +
            '.caption-window, .caption-window * { visibility: visible !important; z-index: 2147483647 !important; }' +
            '.caption-window.ytp-caption-window-bottom { bottom: 4% !important; top: auto !important; margin-bottom: 0 !important; }' +
            '.ytp-chrome-bottom, .ytp-chrome-top, .ytp-gradient-bottom, .ytp-gradient-top,' +
            '.ytp-ce-element, .ytp-cards-teaser, .ytp-pause-overlay, .ytp-endscreen-content { display: none !important; }' +
            'html, body, #movie_player, #movie_player *, video { cursor: auto !important; }' +
            'html, body { background: #000 !important; overflow: hidden !important; visibility: visible !important; }';

        var markAncestors = function () {
            var video = videoEl();
            if (!video) {
                return;
            }
            document.querySelectorAll('.kaset-visible').forEach(function (el) {
                el.classList.remove('kaset-visible');
            });
            var current = video;
            while (current && current !== document.documentElement) {
                current.classList.add('kaset-visible');
                current = current.parentElement;
            }
        };

        var enforce = function () {
            markAncestors();
            if (window.__kasetYTVideoActive) {
                requestAnimationFrame(enforce);
            }
        };

        window.__kasetYTVideoActive = true;
        requestAnimationFrame(enforce);
        return true;
    };

    function tick() {
        attach();
        sendUpdate();
    }

    function start() {
        attach();
        sendUpdate();
        try { window.__kasetExtractVideo(); } catch (e) { /* best-effort */ }
        // Re-attach periodically: YouTube swaps <video> elements across SPA
        // navigations and ad transitions.
        setInterval(attach, 2000);
        setInterval(tick, 1000);
        try {
            var observer = new MutationObserver(function () { attach(); });
            observer.observe(document.documentElement, { childList: true, subtree: true });
        } catch (e) {
            // MutationObserver unavailable — heartbeat still re-binds via tick().
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();

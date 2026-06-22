// observer.js — injected into every music.youtube.com watch page via
// CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync (Req 2, equivalent of the
// macOS WKUserScript observer in docs/playback.md). It is the ONLY channel from the
// (untrusted) page to the native controller; native validates every message shape.
//
// Messages posted to native via window.chrome.webview.postMessage:
//   { type: 'STATE_UPDATE', isPlaying, progress, duration, videoId, title, artist, trackChanged, hasVideo }
//   { type: 'TRACK_ENDED', videoId }
//   { type: 'DRM_STATUS', available }   // best-effort Widevine/EME probe (Req 1.7)
//
// SECURITY: never read or post cookies, tokens, or SAPISID values.
(function () {
    'use strict';

    // Guard against double-injection on the same document.
    if (window.__kasetObserverInstalled) {
        return;
    }
    window.__kasetObserverInstalled = true;

    var bridge = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
    if (!bridge) {
        return;
    }

    var lastTitle = '';
    var lastArtist = '';
    var lastVideoId = '';

    function post(message) {
        try {
            bridge.postMessage(message);
        } catch (e) {
            // Swallow: the page must never break because the bridge is unavailable.
        }
    }

    function playerElement() {
        return document.querySelector('ytmusic-player');
    }

    function videoData() {
        try {
            var p = playerElement();
            var api = p && p.playerApi;
            var data = api && api.getVideoData ? api.getVideoData() : null;
            return data || null;
        } catch (e) {
            return null;
        }
    }

    function currentVideoId() {
        var data = videoData();
        if (data && (data.video_id || data.videoId)) {
            return data.video_id || data.videoId;
        }
        return '';
    }

    function readTitle() {
        var domTitle = document.querySelector('.title.ytmusic-player-bar');
        var text = domTitle ? (domTitle.textContent || '').trim() : '';
        if (text === '') {
            var data = videoData();
            if (data && data.title) {
                text = ('' + data.title).trim();
            }
        }
        return text;
    }

    function readArtist() {
        var byline = document.querySelector('.byline.ytmusic-player-bar');
        var text = byline ? (byline.textContent || '').trim() : '';
        if (text === '') {
            var data = videoData();
            if (data && data.author) {
                text = ('' + data.author).trim();
            }
        }
        return text;
    }

    // Apply native-requested target volume / mute the moment a <video> appears.
    function applyPlaybackPreferences(video) {
        if (!video) {
            return;
        }
        try {
            if (typeof window.__kasetTargetVolume === 'number') {
                video.volume = Math.max(0, Math.min(1, window.__kasetTargetVolume));
            }
            if (typeof window.__kasetMuted === 'boolean') {
                video.muted = window.__kasetMuted;
            }
        } catch (e) {
            // best-effort
        }
    }

    function sendUpdate() {
        var video = document.querySelector('video');
        applyPlaybackPreferences(video);

        var progressBar = document.querySelector('#progress-bar');
        var progress = video ? (video.currentTime || 0)
            : parseFloat((progressBar && progressBar.getAttribute('value')) || '0');
        var duration = video ? (isFinite(video.duration) ? video.duration : 0)
            : parseFloat((progressBar && progressBar.getAttribute('aria-valuemax')) || '0');

        var title = readTitle();
        var artist = readArtist();
        var videoId = currentVideoId();

        var trackChanged =
            (title !== '' && (title !== lastTitle || artist !== lastArtist)) ||
            (videoId !== '' && videoId !== lastVideoId);

        if (trackChanged) {
            if (title !== '') {
                lastTitle = title;
                lastArtist = artist;
            }
            if (videoId !== '') {
                lastVideoId = videoId;
            }
        }

        var hasVideo = null;
        if (video) {
            hasVideo = (video.videoWidth || 0) > 0 && (video.videoHeight || 0) > 0;
        }

        post({
            type: 'STATE_UPDATE',
            isPlaying: video ? !video.paused : false,
            progress: isFinite(progress) ? progress : 0,
            duration: isFinite(duration) ? duration : 0,
            videoId: videoId,
            title: title,
            artist: artist,
            trackChanged: trackChanged,
            hasVideo: hasVideo
        });
    }

    function sendTrackEnded() {
        post({
            type: 'TRACK_ENDED',
            videoId: lastVideoId || currentVideoId()
        });
    }

    // Best-effort DRM / Widevine probe via EME (Req 1.3 / 1.7). Posts once.
    function probeDrm() {
        try {
            if (!navigator.requestMediaKeySystemAccess) {
                post({ type: 'DRM_STATUS', available: false });
                return;
            }
            var config = [{
                initDataTypes: ['cenc'],
                audioCapabilities: [{ contentType: 'audio/mp4;codecs="mp4a.40.2"' }],
                videoCapabilities: [{ contentType: 'video/mp4;codecs="avc1.42E01E"' }]
            }];
            navigator.requestMediaKeySystemAccess('com.widevine.alpha', config)
                .then(function () { post({ type: 'DRM_STATUS', available: true }); })
                .catch(function () { post({ type: 'DRM_STATUS', available: false }); });
        } catch (e) {
            post({ type: 'DRM_STATUS', available: false });
        }
    }

    // Attach media element listeners (re-attach as the DOM swaps the <video>).
    var boundVideo = null;
    function bindVideo() {
        var video = document.querySelector('video');
        if (!video || video === boundVideo) {
            return;
        }
        boundVideo = video;
        applyPlaybackPreferences(video);
        video.addEventListener('play', sendUpdate);
        video.addEventListener('pause', sendUpdate);
        video.addEventListener('timeupdate', sendUpdate);
        video.addEventListener('loadedmetadata', sendUpdate);
        video.addEventListener('ended', sendTrackEnded);
    }

    function tick() {
        bindVideo();
        sendUpdate();
    }

    // ~1 Hz heartbeat plus DOM mutation re-binding.
    function start() {
        probeDrm();
        bindVideo();
        sendUpdate();
        setInterval(tick, 1000);
        try {
            var observer = new MutationObserver(function () { bindVideo(); });
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

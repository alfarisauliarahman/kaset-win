// observer.js — injected into every music.youtube.com watch page via
// CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync (Req 2, equivalent of the
// macOS WKUserScript observer in docs/playback.md). It is the ONLY channel from the
// (untrusted) page to the native controller; native validates every message shape.
//
// Messages posted to native via window.chrome.webview.postMessage:
//   { type: 'STATE_UPDATE', isPlaying, progress, duration, videoId, title, artist, trackChanged, hasVideo, thumbnailUrl }
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

    // Throttle routine progress/time updates (the `timeupdate` flood fires several times a second).
    // Significant changes — play/pause transitions, track changes, videoId changes — always post
    // immediately; only progress-only updates are rate-limited. The STATE_UPDATE message shape is
    // unchanged (PlaybackMessageParser still receives the same fields).
    var STATE_UPDATE_MIN_INTERVAL_MS = 500;
    var lastStateUpdatePost = 0;
    var lastIsPlaying = null;
    var enforcingVolume = false;

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
            if (data) {
                return data;
            }
            var moviePlayer = document.getElementById('movie_player');
            if (moviePlayer && moviePlayer.getVideoData) {
                return moviePlayer.getVideoData() || null;
            }
            return null;
        } catch (e) {
            return null;
        }
    }

    function currentVideoId() {
        var data = videoData();
        if (data && (data.video_id || data.videoId)) {
            return data.video_id || data.videoId;
        }
        try {
            return new URL(window.location.href).searchParams.get('v') || '';
        } catch (e) {
            return '';
        }
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

    function readThumbnailUrl() {
        var selectors = [
            'ytmusic-player-bar img.image',
            'ytmusic-player-bar img',
            '.thumbnail-image img',
            'img.ytmusic-player-bar'
        ];
        for (var i = 0; i < selectors.length; i++) {
            var img = document.querySelector(selectors[i]);
            var src = img ? (img.currentSrc || img.src || '') : '';
            if (src && src.indexOf('data:') !== 0 && src.indexOf('blob:') !== 0) {
                return src;
            }
        }
        return '';
    }

    // Apply native-requested target volume / mute the moment a <video> appears. YouTube Music
    // keeps a separate internal player volume, so video.volume alone is not enough; enforce the
    // target through every available player API and immediately undo YouTube's own resets.
    function applyPlaybackPreferences(video) {
        if (!video) {
            return;
        }
        try {
            if (typeof window.__kasetTargetVolume === 'number') {
                var target = Math.max(0, Math.min(1, window.__kasetTargetVolume));
                var ytVolume = Math.round(target * 100);
                enforcingVolume = true;
                if (Math.abs(video.volume - target) > 0.01) {
                    video.volume = target;
                }
                var player = playerElement();
                if (player && player.playerApi && player.playerApi.setVolume) {
                    player.playerApi.setVolume(ytVolume);
                }
                var moviePlayer = document.getElementById('movie_player');
                if (moviePlayer && moviePlayer.setVolume) {
                    moviePlayer.setVolume(ytVolume);
                }
                setTimeout(function () { enforcingVolume = false; }, 50);
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
        var thumbnailUrl = readThumbnailUrl();

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

        var isPlaying = video ? !video.paused : false;

        // Post immediately on meaningful state changes; otherwise rate-limit progress-only updates
        // so a single playing track doesn't flood native with a STATE_UPDATE every ~130–280ms.
        var significant = trackChanged || (isPlaying !== lastIsPlaying);
        if (!significant) {
            var nowMs = (typeof performance !== 'undefined' && performance.now)
                ? performance.now() : Date.now();
            if (nowMs - lastStateUpdatePost < STATE_UPDATE_MIN_INTERVAL_MS) {
                return;
            }
            lastStateUpdatePost = nowMs;
        } else {
            lastStateUpdatePost = (typeof performance !== 'undefined' && performance.now)
                ? performance.now() : Date.now();
        }
        lastIsPlaying = isPlaying;

        post({
            type: 'STATE_UPDATE',
            isPlaying: isPlaying,
            progress: isFinite(progress) ? progress : 0,
            duration: isFinite(duration) ? duration : 0,
            videoId: videoId,
            title: title,
            artist: artist,
            trackChanged: trackChanged,
            hasVideo: hasVideo,
            thumbnailUrl: thumbnailUrl
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
        video.addEventListener('playing', function () {
            applyPlaybackPreferences(video);
            sendUpdate();
        });
        video.addEventListener('pause', sendUpdate);
        video.addEventListener('timeupdate', sendUpdate);
        video.addEventListener('loadedmetadata', function () {
            applyPlaybackPreferences(video);
            sendUpdate();
        });
        video.addEventListener('loadeddata', function () {
            applyPlaybackPreferences(video);
            sendUpdate();
        });
        video.addEventListener('canplay', function () {
            applyPlaybackPreferences(video);
            sendUpdate();
        });
        video.addEventListener('volumechange', function () {
            if (!enforcingVolume) {
                applyPlaybackPreferences(video);
            }
        });
        video.addEventListener('ended', sendTrackEnded);

        var burstCount = 0;
        var burst = setInterval(function () {
            applyPlaybackPreferences(video);
            if (++burstCount >= 15) {
                clearInterval(burst);
            }
        }, 200);
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

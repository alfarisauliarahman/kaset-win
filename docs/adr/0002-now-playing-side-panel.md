# ADR 0002 — Docked now-playing side panel (queue / lyrics / related)

## Status

Accepted (2026-07-05)

## Context

The queue and lyrics surfaces were originally full content pages (`QueuePage`, `LyricsPage`)
navigated via the shell's content `Frame`. User feedback (with Apple Music / YouTube Music web
screenshots) asked for the platform-conventional shape instead: a **docked right-hand panel** that
the player-bar buttons toggle, showing **Berikutnya / Riwayat / Terkait** tabs for the queue and a
synced-lyrics view — without navigating away from the current page.

## Decision

- A singleton **`SidePanelController`** (App layer) holds the panel mode
  (`None / Queue / Lyrics`) and raises `Changed`. The `PlayerBar` buttons toggle modes; the shell
  and the buttons observe the same instance.
- **`MainWindow`** hosts a second grid column (0 ↔ 380 px) containing **`NowPlayingPanel`**, shown
  and hidden from the controller's `Changed` event. Content shrinks rather than being overlaid.
- **`NowPlayingPanel`** composes the existing `QueueViewModel` and `LyricsViewModel` (unchanged
  observability contracts) plus a "Terkait" tab backed by
  `IYTMusicClient.GetSongRelatedAsync(videoId)` — a two-step InnerTube call
  (`next` → Related tab browseId → `browse`) parsed by the existing resilient
  `HomeResponseParser`.
- The old `QueuePage` / `LyricsPage` and their `NavigationHelper` entry points were **deleted**;
  the ViewModels stay (headless-testable) and now back the panel.

## Consequences

- One live queue/lyrics surface instead of page navigation; no back-stack pollution.
- The panel's ViewModels subscribe to singleton services; `NowPlayingPanel` disposes them on
  unload (window teardown).
- Related content is best-effort: parse failures degrade to an empty tab (never a crash).
- Session like-state (`ILikeStateStore`, Core) is shared by the player bar and track lists so
  like == collection stays in sync across surfaces and navigation; UI updates are optimistic with
  revert-on-failure.

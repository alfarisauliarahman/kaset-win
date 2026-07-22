# ADR 0003 — Mini player (CompactOverlay) and sleep timer

## Status

Accepted (2026-07-22)

## Context

Two staples of a desktop music client were missing from the Windows port:

- **Mini player.** The macOS app has one; on Windows the platform-conventional shape is
  `AppWindowPresenterKind.CompactOverlay` (picture-in-picture) — a small, system-managed,
  always-on-top frame.
- **Sleep timer.** Stop playback after N minutes, or at the end of the current track.

Both touch playback, which in KasetWin is anchored to a **single hidden WebView2** mounted on
`MainWindow.RootGrid`. Anything that re-parents that element tears playback down, which rules out the
obvious "open a second small window" design for the mini player.

The sleep timer also has a subtle correctness trap: "stop at the end of this track" must trigger on a
genuine track end, not on any `CurrentTrack` change — the latter also fires when the user skips
manually, which would pause playback the instant they pressed Next.

## Decision

### Mini player — reuse the window, swap the chrome

- `MainWindow.MiniPlayer.cs` (a new partial) flips the **existing** window's presenter to
  `CompactOverlay` and swaps which chrome is visible: `AppTitleBar` and `NavView` collapse, the new
  `MiniPlayerView` becomes visible. `RootGrid` — and therefore the playback WebView2 — is untouched.
- `MiniPlayerView` binds to the same singleton `IPlayerService` as `PlayerBar`, so the two surfaces
  cannot disagree and switching modes carries no state. It exposes a deliberately reduced control
  set (artwork, title/artist, previous / play-pause / next, seek, restore) — at 400×150 there is no
  room for shuffle, repeat, volume, lyrics or queue, and `PlayerBar`'s three-column layout does not
  survive the width.
- `MainWindowLayout` gained `SuspendMinimumSize()` / `RestoreMinimumSize()`. The `WM_GETMINMAXINFO`
  floor (980×600) would otherwise veto the compact size; it is waived while compact and reinstated
  on restore rather than being removed and re-installed.
- The docked side panel is closed on entry and its previous mode restored on exit.

### Sleep timer — pure policy in Core, enforcement in the player

- `KasetWin.Core.Services.Player.SleepTimer` is a **pure state machine**: `StartDuration`,
  `StartEndOfTrack`, `Cancel`, `Advance(elapsed)`, `NotifyTrackEnded()`. It decides *when* playback
  should stop and never stops anything itself. Time is supplied by the caller rather than read from
  the clock, so the whole thing is deterministic and headless-testable.
- `Advance` and `NotifyTrackEnded` return `true` **exactly once**, on the call that expires the
  timer, and the timer disarms itself — so a caller pauses once, not on every subsequent tick.
- Registered as a **DI singleton** and shared by two consumers:
  - `PlayerBar` arms it, ticks it with a one-second `DispatcherTimer` that runs *only while armed*,
    and shows the countdown on the button (icon tinted, accessible name carrying the remaining time).
  - `PlayerService.HandleTrackEndedAsync` consults it for the end-of-track mode, **after**
    `WebQueueSync` has classified the event. That ordering is the point: a stray `TRACK_ENDED` for
    some other video (YouTube autoplay drift) must not disarm the timer and pause the wrong thing.

## Consequences

- Playback survives entering and leaving the mini player, because the WebView2 never moves. The cost
  is that mini-player mode is a *state of the main window*, not an independent window — the full
  shell cannot be used while the mini player is showing. That matches how CompactOverlay is meant to
  be used and is what the macOS app does too.
- While the minimum size is suspended, nothing prevents a user from dragging the window below
  980×600. In practice CompactOverlay owns the frame for that whole period, but the suspend/restore
  pair must stay balanced — an early return between them would leave the floor off.
- The sleep timer's correctness is covered headless (`SleepTimerTests`, 9 tests including a
  100-iteration property that it fires exactly once and never before the full duration). What is
  *not* covered by tests is the wiring: that `PlayerService` actually pauses, and that the ticker
  starts and stops with the armed state. Manual testing on 2026-07-22 confirmed the "end of this
  track" path, including that a manual Next does **not** trigger it.
- **Closing the window is not closing the window.** Kaset's close button hides the window to the
  tray so background audio continues, and the tray reopens *the same instance*. Mini-player mode is
  window state, so it survives that round trip: without intervention the user gets a 400×150 window
  with no chrome and no obvious way out. `OnAppWindowClosing` therefore leaves mini-player mode —
  but **after** `AppWindow.Hide()`, not before. Doing it before is also correct and was the first
  attempt; it just looks broken, because the window visibly grows back to full size before
  vanishing. Both orderings "work"; only one of them is acceptable to watch.
- Persisted geometry must never be read from the live frame while compact. `SaveGeometry` takes an
  `overrideFrame` so a close from mini-player mode stores the frame captured on the way in
  (`FrameBeforeMiniPlayer`) rather than 400×150.
- Leaving mini-player mode restores the captured frame explicitly rather than trusting the presenter
  switch. `CompactOverlay` manages its own size, and returning to `Overlapped` does not reliably hand
  back the size the user had chosen.
- `PlayerService`'s constructor gained a sixth optional parameter. Existing call sites and tests are
  unaffected (it defaults to `null`, which leaves the track-end flow exactly as it was).

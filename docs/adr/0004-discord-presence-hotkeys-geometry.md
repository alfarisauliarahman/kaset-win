# ADR 0004 — Discord Rich Presence, global hotkeys, window geometry

## Status

Accepted (2026-07-22)

## Context

Three desktop-client staples were missing, and each one had a decision in it that is easy to get
wrong in a way that is invisible until someone else uses the app:

- **Discord Rich Presence** — "Listening to …" on the user's profile.
- **Global hotkeys** — playback control from other applications.
- **Window geometry** — reopening at the size the user left.

## Decision

### Discord: hand-rolled protocol, one shared Application ID

- The IPC protocol is implemented directly (`KasetWin.Platform/RichPresence/DiscordRpcClient.cs`):
  connect to `\\.\pipe\discord-ipc-N` (0–9 are probed; which one is free depends on how many Discord
  clients are running) and exchange `[int32 opcode][int32 length][utf8 json]` frames, little-endian.
  No third-party RPC library, per AGENTS.md.
- The pure mapping from player state to an activity lives in `KasetWin.Core` so it is headless-testable
  (`DiscordActivityBuilder`, 12 tests).
- **Kaset ships one shared Application ID** (`Hosting/DiscordRichPresenceOptions.cs`); the user only
  flips a toggle. A per-user ID field remains as an optional override under "Advanced".
- The feature is **off by default**.

### Global hotkeys: conservative combinations, per-combination failure tolerated

`Ctrl+Alt+↓/→/←/↑` via `RegisterHotKey`, with `WM_HOTKEY` handled through a second window subclass
(id 2) alongside the minimum-size subclass in `MainWindowLayout`. Off by default. A combination that
fails to register is skipped individually.

### Window geometry: frame **and** maximised state, saved on close, validated on restore

Saved in `OnAppWindowClosing`, restored in `MainWindowLayout.Configure`, guarded on both ends
(see ADR 0003 for the mini-player interaction).

Persisted value: `x,y,width,height,maximized` in `window.geometry`. Four-field values written by
earlier builds still parse, as not-maximised.

- **A maximised window reopens maximised.** The first implementation persisted geometry only when
  the presenter state was `Restored`, on the theory that "what should be restored is the size the
  user chose, not the state". That was wrong: on Windows a maximised window is expected to reopen
  maximised, and skipping the save also meant a maximise-then-quit silently kept whatever stale frame
  was saved before. The state is now stored beside the frame.
- **The frame stored beside it is always the *restored* frame, never the maximised one.**
  `AppWindow.Size`/`Position` report the live frame, which while maximised is the whole work area.
  Storing that would hand the user a work-area-sized "restored" window the moment they un-maximise.
  The restored frame is only available from Win32 `GetWindowPlacement` → `rcNormalPosition`, so
  `SaveGeometry` reads it there when maximised.
- **`Minimized` is still never saved** (the frame is meaningless), and neither is mini-player mode —
  `CompactOverlay` is not an `OverlappedPresenter` at all, so the presenter type check already
  excludes it, and the close-from-mini-player path passes an `overrideFrame` instead (ADR 0003).
- **`IsFrameVisibleOnAnyDisplay` still gates the frame**, maximised or not. The restored frame under
  a maximised window is exactly what the user gets when they un-maximise, so it can still strand a
  window on a monitor that was unplugged. The maximise itself is applied even when the frame is
  rejected and the default size is used instead: a maximised window always lands on an attached
  display, so it cannot go off-screen.
- **A trip to the tray must not un-maximise.** `BringToForeground` used to call
  `OverlappedPresenter.Restore()` unconditionally to undo a minimise; on a maximised window that
  threw the maximised state away, so ✕-then-reopen quietly downgraded it before anything could be
  persisted. It now calls `Restore()` only when the state is actually `Minimized`.

## Rationale for the parts that look arbitrary

**Why one shared Discord Application ID, not one per user.** The first implementation required every
user to register their own Discord application and paste its ID before anything happened. That was
wrong on both counts:

- An Application ID is **not a secret**. It is transmitted in every presence payload and is visible
  to anyone who looks. The value that must never be committed is the *Client Secret*, which is used
  only for OAuth flows — local IPC presence (`SET_ACTIVITY`) does not use it and Kaset never touches
  it. So there is nothing being protected by pushing the id onto users.
- Requiring a trip to the Discord Developer Portal before a feature does anything is the same as the
  feature not existing, for almost everyone.

**Why off by default.** It publishes what the user is listening to onto a profile others can see.
That is a privacy choice, and it is theirs to make. This is a different reason from the hotkeys'
default, below — do not "simplify" the two into one rule.

**Why the hotkeys are off by default, and why the combinations are dull.** `RegisterHotKey` grants a
combination to exactly one process system-wide, first come first served. Anything popular
(`Ctrl+Shift+`letter) risks stealing a shortcut from the user's editor or browser — or silently
failing because that application started first. `Ctrl+Alt+Arrow` is rarely claimed. Enabling this by
default would take four combinations away from every other application on the machine without asking.

**Three Discord failure modes that report nothing.** Each is handled in code because none of them
produce an error to debug from:

1. `details`/`state` outside 2–128 characters — Discord drops the activity silently. Presence simply
   never appears. Hence clamping in one tested place.
2. A `start` timestamp of "now" — Discord renders elapsed time from it, so the counter restarts at
   zero on every seek and every reconnect. It must be `now - progress`.
3. Updates faster than ~1 per 15 seconds — silently discarded. So presence is pushed on track and
   play/pause changes, never on the ~1 Hz progress tick; the timestamps let Discord animate the
   counter itself.

## Consequences

- `DiscordRichPresenceOptions.DefaultApplicationId` is **empty until the project's Discord
  application is created**. Until then the toggle cannot work — so the Settings card says so
  explicitly rather than presenting a switch that does nothing.
- Album artwork is sent as the YouTube thumbnail URL in `large_image`, so it works without uploading
  anything to Discord's Art Assets. Art Assets would only add a small corner icon.
- Rich presence, hotkeys and geometry are all best-effort: Discord absent or restarted, a hotkey
  already claimed, a monitor unplugged since last launch. None of them may affect playback, and all
  of them swallow their failures.
- The Win32 surface (`RegisterHotKey`, `WM_GETMINMAXINFO`, `DisplayArea`) has **no automated test
  coverage** and is only exercised by the manual checklist.
- Two window subclasses now share one HWND. They use distinct ids (1 = minimum size, 2 = hotkeys) and
  both chain to `DefSubclassProc`; a third must follow the same rule.

namespace KasetWin.Core.Services.Player;

/// <summary>How a running sleep timer decides it is finished.</summary>
public enum SleepTimerMode
{
    /// <summary>No timer is armed.</summary>
    Off,

    /// <summary>Stop once a fixed duration has elapsed, regardless of track boundaries.</summary>
    Duration,

    /// <summary>Stop when the track that was playing at arm time finishes.</summary>
    EndOfTrack,
}

/// <summary>
/// A snapshot of the sleep timer, safe to hand to the UI.
/// </summary>
/// <param name="Mode">What the armed timer is waiting for; <see cref="SleepTimerMode.Off"/> when idle.</param>
/// <param name="Remaining">
/// Time left for a <see cref="SleepTimerMode.Duration"/> timer, clamped at zero. Always
/// <see cref="TimeSpan.Zero"/> for the other modes, whose end is not clock-based.
/// </param>
public readonly record struct SleepTimerState(SleepTimerMode Mode, TimeSpan Remaining)
{
    /// <summary>Whether a timer is currently armed.</summary>
    public bool IsArmed => Mode != SleepTimerMode.Off;

    /// <summary>The idle state.</summary>
    public static SleepTimerState Idle => new(SleepTimerMode.Off, TimeSpan.Zero);
}

/// <summary>
/// Pure sleep-timer policy: decides <b>when</b> playback should stop, never stops it itself.
///
/// Time is supplied by the caller rather than read from the clock, so the whole state machine is
/// deterministic and testable headless — the WinUI layer owns the ticking and the actual pause.
/// The class is not thread-safe by design; drive it from the UI thread like the rest of the
/// player-facing state.
/// </summary>
/// <remarks>
/// Arming replaces any previous timer, so a user who picks 30 minutes and then 15 gets 15 — not
/// both. <see cref="Advance"/> and <see cref="NotifyTrackEnded"/> return whether this call is the
/// one that expired the timer, so a caller can pause exactly once; the timer disarms itself on
/// expiry and further calls return <c>false</c>.
/// </remarks>
public sealed class SleepTimer
{
    private SleepTimerMode _mode = SleepTimerMode.Off;
    private TimeSpan _remaining = TimeSpan.Zero;

    /// <summary>Raised whenever the observable state changes (armed, cancelled, ticked, expired).</summary>
    public event EventHandler<SleepTimerState>? StateChanged;

    /// <summary>
    /// Raised the moment the timer actually runs out — never when it is merely cancelled.
    /// </summary>
    /// <remarks>
    /// <see cref="StateChanged"/> cannot answer "did it fire or did the user turn it off?": both end
    /// at <see cref="SleepTimerMode.Off"/>. That ambiguity is why the "end of this track" mode ended
    /// up with no toast and no sound — it stops playback inside the player, far from the UI that
    /// announces it, and there was no signal a listener could trust. This event is that signal, and
    /// it fires for BOTH ways of expiring so the announcement lives in exactly one place.
    /// </remarks>
    public event EventHandler? Expired;

    /// <summary>The current state.</summary>
    public SleepTimerState State => new(_mode, _remaining);

    /// <summary>
    /// Arms a timer that expires after <paramref name="duration"/>. A non-positive duration is
    /// treated as a cancel, so "0 minutes" can never leave a timer that expires on the next tick.
    /// </summary>
    public void StartDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            Cancel();
            return;
        }

        _mode = SleepTimerMode.Duration;
        _remaining = duration;
        RaiseChanged();
    }

    /// <summary>Arms a timer that expires when the current track ends.</summary>
    public void StartEndOfTrack()
    {
        _mode = SleepTimerMode.EndOfTrack;
        _remaining = TimeSpan.Zero;
        RaiseChanged();
    }

    /// <summary>Disarms any running timer. A no-op (and silent) when nothing is armed.</summary>
    public void Cancel()
    {
        if (_mode == SleepTimerMode.Off)
        {
            return;
        }

        _mode = SleepTimerMode.Off;
        _remaining = TimeSpan.Zero;
        RaiseChanged();
    }

    /// <summary>
    /// Advances a <see cref="SleepTimerMode.Duration"/> timer by <paramref name="elapsed"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> exactly once, on the call that drives the remaining time to zero — the caller's
    /// cue to pause playback. The timer disarms itself at that point.
    /// </returns>
    public bool Advance(TimeSpan elapsed)
    {
        if (_mode != SleepTimerMode.Duration || elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        _remaining -= elapsed;
        if (_remaining > TimeSpan.Zero)
        {
            RaiseChanged();
            return false;
        }

        _mode = SleepTimerMode.Off;
        _remaining = TimeSpan.Zero;
        RaiseChanged();
        Expired?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Reports that the current track finished.
    /// </summary>
    /// <returns>
    /// <c>true</c> when an <see cref="SleepTimerMode.EndOfTrack"/> timer was armed — the caller's
    /// cue to pause instead of advancing the queue. A duration timer ignores track boundaries and
    /// keeps running, which is what "stop in 30 minutes" means.
    /// </returns>
    public bool NotifyTrackEnded()
    {
        if (_mode != SleepTimerMode.EndOfTrack)
        {
            return false;
        }

        _mode = SleepTimerMode.Off;
        _remaining = TimeSpan.Zero;
        RaiseChanged();
        Expired?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, State);
}

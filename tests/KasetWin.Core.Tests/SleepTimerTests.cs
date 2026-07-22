using CsCheck;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Tests for <see cref="SleepTimer"/>, the pure "when should playback stop" policy. The timer never
/// pauses anything itself; it returns the single moment at which the caller should.
/// </summary>
public class SleepTimerTests
{
    [Fact]
    public void Starts_idle()
    {
        var timer = new SleepTimer();

        Assert.Equal(SleepTimerMode.Off, timer.State.Mode);
        Assert.False(timer.State.IsArmed);
        Assert.Equal(TimeSpan.Zero, timer.State.Remaining);
    }

    [Fact]
    public void Duration_timer_expires_exactly_once()
    {
        var timer = new SleepTimer();
        timer.StartDuration(TimeSpan.FromMinutes(1));

        Assert.False(timer.Advance(TimeSpan.FromSeconds(59)));
        Assert.Equal(TimeSpan.FromSeconds(1), timer.State.Remaining);

        Assert.True(timer.Advance(TimeSpan.FromSeconds(1)));

        // Disarmed on expiry: a later tick must not ask the caller to pause a second time.
        Assert.False(timer.State.IsArmed);
        Assert.False(timer.Advance(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Overshooting_the_last_tick_clamps_remaining_at_zero()
    {
        var timer = new SleepTimer();
        timer.StartDuration(TimeSpan.FromSeconds(2));

        Assert.True(timer.Advance(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.Zero, timer.State.Remaining);
    }

    [Fact]
    public void Non_positive_duration_cancels_rather_than_arming()
    {
        var timer = new SleepTimer();

        timer.StartDuration(TimeSpan.Zero);
        Assert.False(timer.State.IsArmed);

        timer.StartDuration(TimeSpan.FromMinutes(-5));
        Assert.False(timer.State.IsArmed);

        // And it must not fire on the next tick either.
        Assert.False(timer.Advance(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Arming_again_replaces_the_previous_timer()
    {
        var timer = new SleepTimer();
        timer.StartDuration(TimeSpan.FromMinutes(30));
        timer.StartDuration(TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.State.Remaining);

        // The replaced 30-minute timer must be gone, not merely shadowed.
        Assert.True(timer.Advance(TimeSpan.FromMinutes(15)));
        Assert.False(timer.State.IsArmed);
    }

    [Fact]
    public void End_of_track_fires_on_track_end_and_ignores_ticks()
    {
        var timer = new SleepTimer();
        timer.StartEndOfTrack();

        Assert.False(timer.Advance(TimeSpan.FromHours(3)));
        Assert.True(timer.State.IsArmed);

        Assert.True(timer.NotifyTrackEnded());
        Assert.False(timer.State.IsArmed);
        Assert.False(timer.NotifyTrackEnded());
    }

    [Fact]
    public void Duration_timer_survives_track_boundaries()
    {
        var timer = new SleepTimer();
        timer.StartDuration(TimeSpan.FromMinutes(30));

        // "Stop in 30 minutes" spans however many songs fit in 30 minutes.
        Assert.False(timer.NotifyTrackEnded());
        Assert.True(timer.State.IsArmed);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.State.Remaining);
    }

    [Fact]
    public void Cancel_disarms_and_is_silent_when_idle()
    {
        var timer = new SleepTimer();
        var changes = 0;
        timer.StateChanged += (_, _) => changes++;

        timer.Cancel();
        Assert.Equal(0, changes); // nothing was armed: no spurious notification

        timer.StartDuration(TimeSpan.FromMinutes(10));
        timer.Cancel();
        Assert.Equal(2, changes);
        Assert.False(timer.State.IsArmed);
        Assert.False(timer.Advance(TimeSpan.FromMinutes(10)));
    }

    // Feature: kaset-winui3, Property: Sleep timer fires exactly once and never early
    [Fact]
    public void Property_duration_timer_fires_once_and_only_after_the_full_duration()
    {
        var scenario =
            from totalSeconds in Gen.Int[1, 3600]
            from stepSeconds in Gen.Int[1, 120]
            select (totalSeconds, stepSeconds);

        scenario.Sample(
            s =>
            {
                var (totalSeconds, stepSeconds) = s;
                var timer = new SleepTimer();
                var total = TimeSpan.FromSeconds(totalSeconds);
                var step = TimeSpan.FromSeconds(stepSeconds);
                timer.StartDuration(total);

                var elapsed = TimeSpan.Zero;
                var fired = 0;

                // Tick well past the deadline so a timer that never fires is caught too.
                for (var i = 0; i < (totalSeconds / stepSeconds) + 5; i++)
                {
                    var didFire = timer.Advance(step);
                    elapsed += step;

                    if (didFire)
                    {
                        fired++;
                        // Never early: it can only fire once the full duration has been ticked off.
                        Assert.True(elapsed >= total);
                    }
                }

                Assert.Equal(1, fired);
                Assert.False(timer.State.IsArmed);
            },
            iter: 100);
    }
}

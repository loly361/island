using System;

namespace Island.Services;

/// <summary>Countdown with pause/resume. Time is passed in so it can be tested without a clock.</summary>
public sealed class CountdownTimer
{
    private DateTime _endsAt;

    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(5);

    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(5);

    public bool Running { get; private set; }

    public bool Paused => !Running && Remaining > TimeSpan.Zero && Remaining < Total;

    public double Fraction => Total <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / Total, 0, 1);

    /// <summary>Selects a duration and stops any running countdown.</summary>
    public void Set(TimeSpan total)
    {
        Total = total;
        Remaining = total;
        Running = false;
    }

    public void Start(DateTime now)
    {
        if (Remaining <= TimeSpan.Zero) Remaining = Total;
        _endsAt = now + Remaining;
        Running = true;
    }

    public void Pause(DateTime now)
    {
        if (!Running) return;
        Remaining = Max(_endsAt - now);
        Running = false;
    }

    public void Reset()
    {
        Remaining = Total;
        Running = false;
    }

    /// <summary>Advances the countdown; returns true exactly once, when it reaches zero.</summary>
    public bool Tick(DateTime now)
    {
        if (!Running) return false;
        Remaining = Max(_endsAt - now);
        if (Remaining > TimeSpan.Zero) return false;
        Running = false;
        Remaining = Total;
        return true;
    }

    private static TimeSpan Max(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

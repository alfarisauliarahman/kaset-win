using System;

namespace KasetWin.App.Notifications;

/// <summary>
/// One in-app notification. A plain informational message auto-dismisses; when
/// <see cref="ActionText"/>/<see cref="OnAction"/> are set the notification is actionable and stays
/// open (with its own close button) until the user acts or dismisses it.
/// </summary>
public sealed record InAppNotification(
    string Message,
    string? Title = null,
    string? ActionText = null,
    Action? OnAction = null)
{
    /// <summary>Whether this notification carries an action button (and therefore stays open).</summary>
    public bool IsActionable => !string.IsNullOrEmpty(ActionText) && OnAction is not null;
}

/// <summary>
/// A tiny app-wide channel for in-app notifications (the native-style bar above the account footer).
/// ViewModels call <see cref="Show(string)"/> or <see cref="Show(InAppNotification)"/>; the shell
/// window subscribes to <see cref="Shown"/> and renders an InfoBar. Kept WinRT-free.
/// </summary>
public interface IInAppNotifier
{
    /// <summary>Raised when a notification should be shown.</summary>
    event Action<InAppNotification>? Shown;

    /// <summary>Shows a plain, auto-dismissing informational message.</summary>
    void Show(string message);

    /// <summary>Shows a fully-specified notification (may be actionable/persistent).</summary>
    void Show(InAppNotification notification);
}

/// <summary>Default <see cref="IInAppNotifier"/>: a simple event relay, registered as a singleton.</summary>
public sealed class InAppNotifier : IInAppNotifier
{
    /// <inheritdoc />
    public event Action<InAppNotification>? Shown;

    /// <inheritdoc />
    public void Show(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Shown?.Invoke(new InAppNotification(message));
        }
    }

    /// <inheritdoc />
    public void Show(InAppNotification notification)
    {
        if (notification is not null && !string.IsNullOrWhiteSpace(notification.Message))
        {
            Shown?.Invoke(notification);
        }
    }
}

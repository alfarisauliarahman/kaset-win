using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Errors;
using KasetWin.Core.Services;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Base class for all page/section ViewModels (Task 14.2, Req 16.1/16.2/16.3).
/// </summary>
/// <remarks>
/// <para>
/// Derives from CommunityToolkit.Mvvm's <see cref="ObservableObject"/> so derived types can use the
/// <c>[ObservableProperty]</c> / <c>[RelayCommand]</c> source generators. It centralises the two
/// pieces of presentation state every async-loading ViewModel needs — <see cref="IsLoading"/> and
/// <see cref="ErrorMessage"/> — and the two execution helpers that keep that state consistent:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="RunSafeAsync"/> runs an operation and converts a thrown <see cref="KasetError"/>
///     into a user-facing <see cref="ErrorMessage"/> instead of letting it crash the UI thread.
///   </item>
///   <item>
///     <see cref="LoadAsync(string, Func{CancellationToken, Task}, CancellationToken)"/> wraps
///     <see cref="RunSafeAsync"/> in a single-flight (<see cref="ISingleFlight"/>) keyed by a stable
///     surface key so that re-entrant triggers (e.g. a WinUI page whose <c>Loaded</c>/navigation
///     fires more than once, or rapid back/forward navigation) <em>join</em> the in-flight load
///     rather than starting a duplicate. This is the Windows counterpart of the macOS
///     single-flight ".task-restart" pattern and lets a load <b>survive re-entry</b> (Req 16.3).
///   </item>
/// </list>
/// <para>
/// All state mutation happens on the awaiting caller's context (<c>ConfigureAwait(true)</c>), so as
/// long as <see cref="LoadAsync(string, Func{CancellationToken, Task}, CancellationToken)"/> /
/// <see cref="RunSafeAsync"/> are invoked from the UI thread (the normal case for a ViewModel bound
/// to a page) the observable properties are always updated on that thread.
/// </para>
/// </remarks>
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly ISingleFlight _singleFlight;

    /// <summary>
    /// Creates the base ViewModel.
    /// </summary>
    /// <param name="singleFlight">
    /// Optional shared request coalescer used by
    /// <see cref="LoadAsync(string, Func{CancellationToken, Task}, CancellationToken)"/>. When
    /// omitted a private instance is created, which still coalesces re-entrant loads <em>within</em>
    /// this ViewModel. Pass the DI-registered singleton when loads should coalesce across
    /// collaborators that share keys.
    /// </param>
    protected ViewModelBase(ISingleFlight? singleFlight = null)
    {
        _singleFlight = singleFlight ?? new SingleFlight();
    }

    /// <summary>True while an async load/operation is in progress; bind to show a progress affordance.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// The most recent user-facing error message, or <see langword="null"/> when there is none.
    /// Set automatically by <see cref="RunSafeAsync"/> when a <see cref="KasetError"/> is caught and
    /// cleared at the start of every <see cref="RunSafeAsync"/> call.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>Convenience flag for binding visibility of an error affordance.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Runs <paramref name="operation"/>, translating a thrown <see cref="KasetError"/> into
    /// <see cref="ErrorMessage"/> rather than propagating it. Cancellation
    /// (<see cref="OperationCanceledException"/>) is swallowed silently because it is a normal
    /// outcome of navigating away / superseding a request, not an error to surface.
    /// </summary>
    /// <param name="operation">The asynchronous work to perform.</param>
    /// <param name="ct">Cancels the operation.</param>
    public async Task RunSafeAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        ErrorMessage = null;
        try
        {
            await operation(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the caller cancels (navigation away / superseded load). Not an error.
        }
        catch (KasetError ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Loads a surface exactly once per in-flight <paramref name="key"/>: concurrent/re-entrant
    /// calls for the same key share the single underlying load and observe the same outcome, so the
    /// load survives re-entry (Req 16.3). <see cref="IsLoading"/> is raised for the duration and any
    /// <see cref="KasetError"/> is captured into <see cref="ErrorMessage"/> via
    /// <see cref="RunSafeAsync"/>.
    /// </summary>
    /// <param name="key">
    /// Stable identity of the load (e.g. <c>"home"</c>, <c>$"playlist:{playlistId}"</c>). Re-entrant
    /// calls with an equal key are coalesced; once the load completes the key is released so a later
    /// refresh re-executes.
    /// </param>
    /// <param name="operation">The asynchronous load to perform.</param>
    /// <param name="ct">Cancels this caller's wait without disturbing other joined callers.</param>
    /// <returns><see langword="true"/> when the load completed without surfacing an error.</returns>
    public Task<bool> LoadAsync(string key, Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(operation);

        return _singleFlight.RunAsync(key, () => LoadCoreAsync(operation, ct), ct);
    }

    private async Task<bool> LoadCoreAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            await RunSafeAsync(operation, ct).ConfigureAwait(true);
            return !HasError;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

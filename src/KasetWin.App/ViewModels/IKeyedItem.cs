namespace KasetWin.App.ViewModels;

/// <summary>
/// Optional marker for view-layer items that expose a <b>stable identity</b> for list
/// virtualization (Task 14.2, Req 16.1/16.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention.</b> Core domain models are immutable <c>record</c>s whose identity is their
/// <c>Id</c> (<c>videoId</c> for songs, <c>browseId</c> for playlists/albums/artists). Because
/// records have value equality, an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
/// bound to a virtualized <c>ItemsRepeater</c>/<c>ListView</c> only realises containers for visible
/// items, and unchanged items keep the same identity across reloads — preventing needless container
/// re-creation and image re-fetches.
/// </para>
/// <para>
/// <b>Usage.</b> Most ViewModels can bind Core records directly; the <c>Id</c> string is the natural
/// key. This interface/extension exists for the few cases where the UI needs a uniform key selector
/// (e.g. a generic key-derivation function, an <c>x:Key</c> resource, or a custom
/// <c>ItemTemplateSelector</c>) without switching on the concrete model type. Wrapper/adornment
/// view-models that do not derive their key from a Core record can implement
/// <see cref="IKeyedItem"/> to participate in the same convention.
/// </para>
/// <code>
/// // ItemsRepeater item template keyed by stable identity:
/// //   &lt;ItemsRepeater ItemsSource="{x:Bind Songs}" /&gt;
/// // where Songs is ObservableCollection&lt;Song&gt; and each Song.Id (videoId) is stable.
///
/// // Uniform key access regardless of model type:
/// string key = KeyedItem.KeyOf(item, static s =&gt; s.Id);
/// </code>
/// </remarks>
public interface IKeyedItem
{
    /// <summary>A stable, non-empty identity for the item (e.g. <c>videoId</c>/<c>browseId</c>).</summary>
    string Key { get; }
}

/// <summary>Small helpers for deriving the stable identity used by virtualized lists (Req 16.1).</summary>
public static class KeyedItem
{
    /// <summary>
    /// Returns the stable key for <paramref name="item"/>: its <see cref="IKeyedItem.Key"/> when it
    /// implements <see cref="IKeyedItem"/>, otherwise the key produced by
    /// <paramref name="keySelector"/> (typically <c>x =&gt; x.Id</c> for a Core record).
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="item">The item to identify.</param>
    /// <param name="keySelector">Fallback key projection for items that do not implement <see cref="IKeyedItem"/>.</param>
    /// <returns>The stable key string.</returns>
    public static string KeyOf<T>(T item, Func<T, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(keySelector);

        return item is IKeyedItem keyed ? keyed.Key : keySelector(item);
    }
}

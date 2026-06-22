using CsCheck;
using KasetWin.Core.Services.Activation;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for the pure <c>kaset://</c> URL-scheme parser (Property 43). A single
/// <see cref="FactAttribute"/> running a minimum of 100 CsCheck iterations covers both facets:
/// every well-formed <c>kaset://{action}?{key}={id}</c> URI parses to the matching kind + exact id,
/// and every malformed / unknown URI is ignored (null) without throwing.
/// </summary>
public class UriSchemeProperties
{
    /// <summary>The four valid actions, paired with the query key that carries their id.</summary>
    private static readonly (string Action, string Key, KasetUriKind Kind)[] Actions =
    {
        ("play", "v", KasetUriKind.Play),
        ("playlist", "list", KasetUriKind.Playlist),
        ("album", "id", KasetUriKind.Album),
        ("artist", "id", KasetUriKind.Artist),
    };

    // Feature: kaset-winui3, Property 43: Parsing URL kaset:// menghasilkan konten yang benar atau diabaikan
    // Validates: Requirements 33.1, 33.2, 33.3, 33.4, 33.5
    [Fact]
    public void Property43_Kaset_uri_parses_to_correct_content_or_is_ignored()
    {
        // (a) A well-formed kaset://{action}?{key}={id} with a non-empty id parses to the matching
        //     kind and returns the id verbatim (Req 33.1–33.4).
        var validScenario =
            from action in Gen.OneOfConst(Actions)
            from id in PbtGenerators.Token
            select (action.Kind, action.Action, action.Key, id);

        validScenario.Sample(
            s =>
            {
                var (kind, action, key, id) = s;
                var uri = $"{KasetUriParser.Scheme}://{action}?{key}={id}";

                var command = KasetUriParser.Parse(uri);

                Assert.NotNull(command);
                Assert.Equal(kind, command!.Kind);
                Assert.Equal(id, command.Id);
            },
            iter: 100);

        // (b) Malformed / unknown URIs are ignored: the parser returns null (never a partial
        //     command) and never throws (Req 33.5). Generated invalid shapes:
        //       0 wrong scheme            (https://play?v={id})
        //       1 unknown action          (kaset://{garbage}?v={id})
        //       2 missing query/id        (kaset://{action})
        //       3 empty id value          (kaset://{action}?{key}=)
        //       4 wrong query key         (kaset://{action}?bogus={id})
        //       5 raw garbage token       (no scheme at all)
        var invalidScenario =
            from shape in Gen.Int[0, 5]
            from action in Gen.OneOfConst(Actions)
            from id in PbtGenerators.Token
            // An "unknown action" token of length >= 5 can never collide with play/playlist/album/artist
            // (it could equal "playlist"/"artist" by length, so prefix it to stay clearly distinct).
            from unknown in PbtGenerators.ShortToken.Select(t => "x" + t)
            from bogusKey in PbtGenerators.ShortToken.Select(t => "k" + t)
            select shape switch
            {
                0 => $"https://{action.Action}?{action.Key}={id}",
                1 => $"{KasetUriParser.Scheme}://{unknown}?{action.Key}={id}",
                2 => $"{KasetUriParser.Scheme}://{action.Action}",
                3 => $"{KasetUriParser.Scheme}://{action.Action}?{action.Key}=",
                4 => $"{KasetUriParser.Scheme}://{action.Action}?{bogusKey}={id}",
                _ => unknown,
            };

        invalidScenario.Sample(
            uri => Assert.Null(KasetUriParser.Parse(uri)),
            iter: 100);

        // Null / blank inputs are also ignored (total parser, no throw).
        Assert.Null(KasetUriParser.Parse(null));
        Assert.Null(KasetUriParser.Parse(string.Empty));
        Assert.Null(KasetUriParser.Parse("   "));
    }
}

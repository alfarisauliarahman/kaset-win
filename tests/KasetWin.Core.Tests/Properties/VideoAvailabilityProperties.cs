using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for the pure video-availability policy of the kaset-winui3 feature
/// (<see cref="VideoAvailability"/>). The App layer gates the "pop out video" affordance against
/// this rule, so it must be total over the <see cref="MusicVideoType"/> domain and agree with the
/// authoritative OMV rule (Property 37, Req 26.1). A single <see cref="FactAttribute"/> running a
/// minimum of 100 CsCheck iterations covers it.
/// </summary>
public class VideoAvailabilityProperties
{
    /// <summary>Every defined <see cref="MusicVideoType"/> member — exhaustive domain coverage.</summary>
    private static readonly Gen<MusicVideoType> MusicVideoTypes = Gen.OneOfConst(
        MusicVideoType.Omv,
        MusicVideoType.Atv,
        MusicVideoType.Ugc,
        MusicVideoType.PodcastEpisode,
        MusicVideoType.Unknown);

    // Feature: kaset-winui3, Property 37: Deteksi ketersediaan video dari tipe video musik
    // Validates: Requirements 26.1
    [Fact]
    public void Property37_Video_available_iff_Omv()
    {
        // For any MusicVideoType: IsVideoAvailable returns true if and only if the type is Omv.
        // The call is total (it never throws) and the nullable overload agrees with the value
        // overload for every defined member, so ATV/UGC/PodcastEpisode/Unknown are audio-only.
        MusicVideoTypes.Sample(
            type =>
            {
                var available = VideoAvailability.IsVideoAvailable(type);
                Assert.Equal(type == MusicVideoType.Omv, available);
                Assert.Equal(available, VideoAvailability.IsVideoAvailable((MusicVideoType?)type));
            },
            iter: 100);
    }
}

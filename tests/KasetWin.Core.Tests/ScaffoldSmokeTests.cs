using KasetWin.Core;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Smoke test confirming the test project builds, references Core, and the
/// xUnit runner executes. Real unit/property tests are added in later tasks.
/// </summary>
public class ScaffoldSmokeTests
{
    [Fact]
    public void Core_layer_marker_is_exposed()
    {
        Assert.Equal("Core", CoreInfo.Layer);
    }
}

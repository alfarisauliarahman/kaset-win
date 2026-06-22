using KasetWin.ApiExplorer;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Smoke tests for the API Explorer CLI command dispatch and argument parsing (task 16.2,
/// Req 24.2). These verify that <c>auth</c> / <c>list</c> (and the usage/help/unknown paths)
/// route correctly and run headlessly without performing any real network or auth calls —
/// <c>auth</c> only resolves locally-supplied cookies, and <c>list</c> prints the static
/// endpoint catalog. The networked <c>browse &lt;id&gt;</c> happy path is intentionally not
/// exercised (only its missing-argument guard, which returns before any I/O).
///
/// SECURITY: no real cookies/SAPISID are supplied; nothing here reads or prints secret values.
/// </summary>
public class ApiExplorerCliSmokeTests
{
    // Serializes Console.Out/Error redirection across the capturing tests in this class.
    private static readonly object ConsoleLock = new();

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        int exitCode;
        lock (ConsoleLock)
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                // RunAsync completes synchronously for auth/list/usage paths (no awaited I/O).
                exitCode = ApiExplorerCli.RunAsync(args).GetAwaiter().GetResult();
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        await Task.CompletedTask;
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    // ── list (Req 24.2) ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_command_runs_and_prints_the_known_endpoints()
    {
        var (exitCode, output, _) = await RunAsync("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("Known browse endpoints", output);
        // A couple of catalog entries should appear in the table.
        Assert.Contains("FEmusic_home", output);
        Assert.Contains("FEmusic_library_landing", output);
    }

    [Fact]
    public void List_command_catalog_is_non_empty_and_unique()
    {
        Assert.NotEmpty(KnownEndpoints.All);

        var ids = KnownEndpoints.All.Select(e => e.BrowseId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // ── auth (Req 24.2) ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Auth_command_runs_and_reports_status()
    {
        var (exitCode, output, _) = await RunAsync("auth");

        Assert.Equal(0, exitCode);
        Assert.Contains("Authentication status", output);
        // Status is reported without ever printing a cookie/SAPISID value.
        Assert.Contains("SAPISID", output);
    }

    // ── routing / argument parsing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task No_arguments_prints_usage_and_returns_nonzero()
    {
        var (exitCode, output, _) = await RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", output);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public async Task Help_command_runs_and_returns_zero(string arg)
    {
        var (exitCode, output, _) = await RunAsync(arg);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task Unknown_command_reports_error_and_returns_nonzero()
    {
        var (exitCode, _, error) = await RunAsync("bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public async Task Browse_without_an_id_returns_nonzero_before_any_network_call()
    {
        var (exitCode, _, error) = await RunAsync("browse");

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a browse id", error);
    }

    [Fact]
    public async Task Command_routing_is_case_insensitive()
    {
        var (exitCode, output, _) = await RunAsync("LIST");

        Assert.Equal(0, exitCode);
        Assert.Contains("Known browse endpoints", output);
    }

    // ── CliOptions argument parsing ────────────────────────────────────────────────────────

    [Fact]
    public void Options_parse_positional_verbose_and_flags()
    {
        var options = CliOptions.Parse(new[] { "FEmusic_home", "-v", "--authuser", "3", "--brand", "BRAND123" });

        Assert.Equal("FEmusic_home", options.Positional);
        Assert.True(options.Verbose);
        Assert.Equal(3, options.AuthUser);
        Assert.Equal("BRAND123", options.Brand);
    }

    [Fact]
    public void Options_first_non_flag_token_is_the_positional()
    {
        var options = CliOptions.Parse(new[] { "--verbose", "VLLM" });

        Assert.Equal("VLLM", options.Positional);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Options_ignore_non_integer_authuser()
    {
        var options = CliOptions.Parse(new[] { "--authuser", "notanint" });

        Assert.Null(options.AuthUser);
    }
}

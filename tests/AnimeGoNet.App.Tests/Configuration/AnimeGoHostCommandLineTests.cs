using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Hosting;
using AnimeGoNet.App.Logging;
using AnimeGoNet.Core.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class AnimeGoHostCommandLineTests
{
    [Fact]
    public void NormalizesOnlyTheFourPinnedUpstreamSwitches()
    {
        string[] normalized = AnimeGoHostCommandLine.Normalize(
        [
            "-config",
            "C:/fixture/animego.yaml",
            "-debug",
            "-web=false",
            "--backup",
            "--urls=http://127.0.0.1:0",
            "-unknown",
        ]);

        Assert.Equal(
        [
            "--config",
            "C:/fixture/animego.yaml",
            "--debug=true",
            "--web=false",
            "--backup=true",
            "--urls=http://127.0.0.1:0",
            "-unknown",
        ],
            normalized);
    }

    [Fact]
    public void HelpListsPinnedUpstreamSwitchesWithoutStartingTheHost()
    {
        using var writer = new StringWriter();

        Assert.True(AnimeGoHostCommandLine.TryWriteHelp(["-h"], writer));
        string help = writer.ToString();

        Assert.Contains("--config", help, StringComparison.Ordinal);
        Assert.Contains("--debug", help, StringComparison.Ordinal);
        Assert.Contains("--web", help, StringComparison.Ordinal);
        Assert.Contains("--backup", help, StringComparison.Ordinal);
        Assert.Contains("ANIMEGO_CONFIG_BACKUP", help, StringComparison.Ordinal);
        Assert.False(AnimeGoHostCommandLine.TryWriteHelp([], TextWriter.Null));
    }

    [Fact]
    public async Task WebFalseRunsHostedServicesWithoutCreatingAListener()
    {
        string root = CreateRoot();
        try
        {
            AnimeGoOptions options = AnimeGoDefaults.CreateNative(root);
            await using var app = await AnimeGoApplication.BuildAsync(
                ["-web=false"],
                options,
                startBackgroundWorkers: false);

            var server = Assert.IsType<HeadlessServer>(
                app.Services.GetRequiredService<IServer>());
            await app.StartAsync();

            Assert.Empty(
                server.Features.Get<IServerAddressesFeature>()!.Addresses);

            await app.StopAsync();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DebugSwitchEnablesDebugEntriesInTheRollingLog()
    {
        string root = CreateRoot();
        try
        {
            AnimeGoOptions options = AnimeGoDefaults.CreateNative(root);
            var app = await AnimeGoApplication.BuildAsync(
                ["--debug"],
                options,
                startBackgroundWorkers: false);
            await using (app)
            {
                var provider = app.Services.GetRequiredService<RollingFileLoggerProvider>();
                ILogger logger = provider.CreateLogger("AnimeGoNet.Tests.Cli");

                Assert.True(logger.IsEnabled(LogLevel.Debug));
                logger.Log(
                    LogLevel.Debug,
                    default,
                    "cli-debug-marker",
                    null,
                    static (state, _) => state);
            }

            Assert.Contains(
                "cli-debug-marker",
                await File.ReadAllTextAsync(
                    Path.Combine(options.Paths.DataPath, "logs", "animego.log")),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task InvalidWebValueFailsBeforeCreatingRuntimeDirectories()
    {
        string root = CreateRoot();
        try
        {
            AnimeGoOptions options = AnimeGoDefaults.CreateNative(root);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AnimeGoApplication.BuildAsync(
                    ["--web=not-a-boolean"],
                    options,
                    startBackgroundWorkers: false));

            Assert.Equal("web must be true or false.", exception.Message);
            Assert.False(Directory.Exists(options.Paths.DataPath));
            Assert.DoesNotContain(
                "not-a-boolean",
                exception.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-cli-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

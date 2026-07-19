using System.Net;
using System.Text;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Torrents;

public sealed class TorrentStagingServiceTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("8.8.8.8");

    [Fact]
    public async Task StagesValidatedTorrentAndDeletesItAfterConsumerDisposes()
    {
        await using var fixture = new StagingFixture();
        var transport = new FakeTransport(_ => Response(HttpStatusCode.OK, ValidTorrent()));
        var service = fixture.CreateService(new FakeDnsResolver(PublicAddress), transport);

        var staged = await service.StageAsync(
            new Uri("https://tracker.example/private-passkey/item.torrent?token=secret"),
            Policy("tracker.example"));

        Assert.True(File.Exists(staged.FilePath));
        Assert.StartsWith(fixture.Layout.StagingPath, staged.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", staged.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(40, staged.Metadata.InfoHash.Length);
        Assert.Equal("episode.mkv", Assert.Single(staged.Metadata.Files).RelativePath);
        Assert.Equal(PublicAddress, Assert.Single(transport.Addresses));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(staged.FilePath));
        }

        await staged.DisposeAsync();
        Assert.False(File.Exists(staged.FilePath));
    }

    [Fact]
    public async Task RejectsRedirectToUnlistedHostBeforeSecondDnsLookup()
    {
        await using var fixture = new StagingFixture();
        var dns = new FakeDnsResolver(PublicAddress);
        var transport = new FakeTransport(_ => Response(
            HttpStatusCode.Redirect,
            [],
            new Uri("https://evil.example/stolen-passkey.torrent")));
        var service = fixture.CreateService(dns, transport);

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/private-passkey/item.torrent"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.HostNotAllowed, exception.Code);
        Assert.Equal(1, dns.CallCount);
        Assert.DoesNotContain("private-passkey", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.Layout.StagingPath));
    }

    [Fact]
    public async Task AllowedRedirectPerformsASecondDnsValidation()
    {
        await using var fixture = new StagingFixture();
        var dns = new FakeDnsResolver(PublicAddress);
        var transport = new FakeTransport(uri => uri.IdnHost == "tracker.example"
            ? Response(HttpStatusCode.Redirect, [], new Uri("https://cdn.example/file.torrent"))
            : Response(HttpStatusCode.OK, ValidTorrent()));
        var service = fixture.CreateService(dns, transport);

        await using var staged = await service.StageAsync(
            new Uri("https://tracker.example/passkey/file.torrent"),
            Policy("tracker.example", "cdn.example"));

        Assert.Equal(2, dns.CallCount);
        Assert.Equal(2, transport.CallCount);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("192.168.1.17")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public async Task RejectsPrivateOrSpecialDnsAddresses(string address)
    {
        await using var fixture = new StagingFixture();
        var transport = new FakeTransport(_ => Response(HttpStatusCode.OK, ValidTorrent()));
        var service = fixture.CreateService(new FakeDnsResolver(IPAddress.Parse(address)), transport);

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/private-passkey/item.torrent"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.AddressNotAllowed, exception.Code);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task RejectsMixedPublicAndPrivateDnsAnswerSet()
    {
        await using var fixture = new StagingFixture();
        var transport = new FakeTransport(_ => Response(HttpStatusCode.OK, ValidTorrent()));
        var service = fixture.CreateService(
            new FakeDnsResolver(PublicAddress, IPAddress.Parse("192.168.1.17")),
            transport);

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/passkey/file.torrent"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.AddressNotAllowed, exception.Code);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task EnforcesStreamingLimitAndNeverLeaksSecretUrlInException()
    {
        await using var fixture = new StagingFixture();
        var options = new TorrentFetchOptions
        {
            MaxResponseBytes = 32,
            Timeout = TimeSpan.FromSeconds(5),
            StagingTtl = TimeSpan.FromMinutes(1),
        };
        var transport = new FakeTransport(_ => Response(HttpStatusCode.OK, new byte[33]));
        var service = fixture.CreateService(new FakeDnsResolver(PublicAddress), transport, options);

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/passkey-should-never-escape/file.torrent?token=secret"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.ResponseTooLarge, exception.Code);
        Assert.DoesNotContain("passkey-should-never-escape", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.Layout.StagingPath));
    }

    [Fact]
    public async Task InvalidMetainfoIsRemovedFromStaging()
    {
        await using var fixture = new StagingFixture();
        var service = fixture.CreateService(
            new FakeDnsResolver(PublicAddress),
            new FakeTransport(_ => Response(HttpStatusCode.OK, Encoding.UTF8.GetBytes("not-bencode"))));

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/passkey/file.torrent"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.InvalidTorrent, exception.Code);
        Assert.Empty(Directory.GetFiles(fixture.Layout.StagingPath));
    }

    [Fact]
    public async Task NetworkFailureDiscardsSecretBearingTransportException()
    {
        await using var fixture = new StagingFixture();
        var service = fixture.CreateService(
            new FakeDnsResolver(PublicAddress),
            new FakeTransport(_ => throw new HttpRequestException(
                "Failed https://tracker.example/private-passkey/file.torrent?token=secret")));

        var exception = await Assert.ThrowsAsync<TorrentStagingException>(() => service.StageAsync(
            new Uri("https://tracker.example/private-passkey/file.torrent?token=secret"),
            Policy("tracker.example")));

        Assert.Equal(TorrentStagingFailureCode.NetworkFailure, exception.Code);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("private-passkey", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupOnlyRemovesExpiredStagingArtifacts()
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = new StagingFixture();
        var service = fixture.CreateService(
            new FakeDnsResolver(PublicAddress),
            new FakeTransport(_ => Response(HttpStatusCode.OK, ValidTorrent())),
            new TorrentFetchOptions { StagingTtl = TimeSpan.FromMinutes(15) },
            new FixedTimeProvider(now));
        var expired = Path.Combine(fixture.Layout.StagingPath, "expired.part");
        var current = Path.Combine(fixture.Layout.StagingPath, "current.torrent");
        var unrelated = Path.Combine(fixture.Layout.StagingPath, "keep.txt");
        await File.WriteAllTextAsync(expired, "secret");
        await File.WriteAllTextAsync(current, "current");
        await File.WriteAllTextAsync(unrelated, "other");
        File.SetLastWriteTimeUtc(expired, now.AddMinutes(-16).UtcDateTime);
        File.SetLastWriteTimeUtc(current, now.AddMinutes(-14).UtcDateTime);
        File.SetLastWriteTimeUtc(unrelated, now.AddHours(-1).UtcDateTime);

        var deleted = await service.CleanupExpiredAsync();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void HostPolicySupportsExplicitSubdomainWildcardsWithoutMatchingApex()
    {
        Assert.True(TorrentNetworkPolicy.IsHostAllowed("cdn.example.com", ["*.example.com"]));
        Assert.False(TorrentNetworkPolicy.IsHostAllowed("example.com", ["*.example.com"]));
        Assert.False(TorrentNetworkPolicy.IsHostAllowed("notexample.com", ["*.example.com"]));
    }

    private static TorrentSourcePolicy Policy(params string[] hosts) => new("test", hosts);

    private static byte[] ValidTorrent()
    {
        const string info = "d6:lengthi5e4:name11:episode.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaae";
        return Encoding.UTF8.GetBytes($"d8:announce20:https://secret/token4:info{info}e");
    }

    private static TorrentHttpResponse Response(HttpStatusCode statusCode, byte[] content, Uri? redirect = null) =>
        new(statusCode, redirect, null, new MemoryStream(content, writable: false));

    private sealed class FakeDnsResolver(params IPAddress[] addresses) : ITorrentDnsResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            _ = host;
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }
    }

    private sealed class FakeTransport(Func<Uri, TorrentHttpResponse> responseFactory) : ITorrentHttpTransport
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<IPAddress> Addresses { get; private set; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Addresses = validatedAddresses;
            return ValueTask.FromResult(responseFactory(uri));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StagingFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-staging-tests",
            Guid.NewGuid().ToString("N"));

        public StagingFixture()
        {
            var paths = AnimeGoDefaults.CreateNative(_root).Paths;
            Layout = DirectoryLayout.From(paths);
            Layout.CreateDataDirectories();
        }

        public DirectoryLayout Layout { get; }

        public TorrentStagingService CreateService(
            ITorrentDnsResolver resolver,
            ITorrentHttpTransport transport,
            TorrentFetchOptions? options = null,
            TimeProvider? timeProvider = null) =>
            new(Layout, options ?? new TorrentFetchOptions(), resolver, transport, timeProvider);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

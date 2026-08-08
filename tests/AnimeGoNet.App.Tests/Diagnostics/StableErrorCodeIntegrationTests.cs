using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Deletion;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Diagnostics;

public sealed class StableErrorCodeIntegrationTests
{
    [Fact]
    public void ApplicationExceptionsRejectUnsafeCodes()
    {
        const string invalid = "unsafe code";

        Assert.Throws<ArgumentException>(() => new SafeFileDeleteException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new SafeFileMoveException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new PluginScheduleException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new DataUpdateServiceException(invalid, "message"));
        Assert.Throws<ArgumentException>(() =>
            new BangumiClientException(MetadataFailureKind.Protocol, invalid));
    }
}

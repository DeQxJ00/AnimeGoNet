using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.Data.Tests.Diagnostics;

public sealed class StableErrorCodeIntegrationTests
{
    [Fact]
    public void DataLayerExceptionsRejectUnsafeCodes()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DataPackageException("unsafe code", "message"));

        Assert.Equal("code", exception.ParamName);
    }
}

using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Sources;

namespace AnimeGoNet.Core.Tests.Sources;

public sealed class MikanIdentityCookieTests
{
    [Theory]
    [InlineData("cookie-value", "cookie-value")]
    [InlineData(
        ".AspNetCore.Identity.Application=encoded%2Fvalue%3D",
        "encoded%2Fvalue%3D")]
    public void NormalizesRawOrUpstreamFullCookie(
        string value,
        string expected)
    {
        Assert.Equal(expected, MikanIdentityCookie.NormalizeOptional(value));
    }

    [Theory]
    [InlineData("value;other=leak")]
    [InlineData("value\r\nX-Leak: injected")]
    [InlineData("value with spaces")]
    [InlineData("\"quoted\"")]
    public void RejectsCookieHeaderInjectionAndInvalidOctets(string value)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => MikanIdentityCookie.NormalizeOptional(value));

        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOversizedValueAndRecordFormattingIsRedacted()
    {
        var secret = new string('a', MikanIdentityCookie.MaximumLength + 1);

        _ = Assert.Throws<ArgumentException>(
            () => MikanIdentityCookie.NormalizeOptional(secret));
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0]
            with { MikanIdentityCookie = "do-not-format-this-secret" };

        Assert.True(seed.ToString().Contains(
            "CredentialsConfigured = True",
            StringComparison.Ordinal));
        Assert.DoesNotContain(
            "do-not-format-this-secret",
            seed.ToString(),
            StringComparison.Ordinal);
    }
}

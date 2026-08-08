using AnimeGoNet.Core.Compatibility;

namespace AnimeGoNet.Core.Tests.Compatibility;

public sealed class StableHashTests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("葬送的芙莉莲", "a9decc9dad40d06f821a3f6a78bad36cebf2389c302a8ff5c07e6e807a930bcd")]
    public void StringHashUsesUtf8AndLowercaseHex(string value, string expected)
    {
        Assert.Equal(expected, StableHash.Sha256LowerHex(value));
    }

    [Fact]
    public void ByteHashDoesNotPerformTextConversion()
    {
        Assert.Equal(
            "2da45f2cd1f9c8e69a67abf7a6b26c282533d0a7686787a9533265418680d4d2",
            StableHash.Sha256LowerHex(new byte[] { 0x00, 0xff, 0x10 }));
    }

    [Fact]
    public void NullStringIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => StableHash.Sha256LowerHex((string)null!));
    }
}

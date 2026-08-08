using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Feeds;

public sealed record RssFeedItem(
    string Title,
    string MikanUrl,
    string TorrentUrl,
    string ContentType,
    long Length,
    string? PublishedDate);

public sealed record RssFeedDocument(
    IReadOnlyList<RssFeedItem> Items,
    int? MikanId);

public sealed class RssFeedException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException), IStableError
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));

    public StableErrorSemantic Semantics => StableErrorSemantic.ParseFailed;
}

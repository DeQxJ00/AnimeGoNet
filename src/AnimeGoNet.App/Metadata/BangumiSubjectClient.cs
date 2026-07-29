using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class BangumiSubjectClient : IBangumiSubjectClient, IBangumiEpisodeClient, IDisposable
{
    private const int EpisodePageSize = 200;
    private const int MaximumEpisodes = 10_000;
    private readonly HttpClient _httpClient;
    private readonly BangumiClientOptions _options;
    private readonly bool _ownsHttpClient;

    public BangumiSubjectClient(HttpClient httpClient, bool ownsHttpClient = false)
        : this(httpClient, new BangumiClientOptions(), ownsHttpClient)
    {
    }

    public BangumiSubjectClient(
        HttpClient httpClient,
        BangumiClientOptions options,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _options = options;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<BangumiSubject?> GetSubjectAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0)
        {
            throw Failure(MetadataFailureKind.InvalidInput, "bangumi_subject_id_invalid");
        }

        var value = await GetAsync(
            new Uri(_options.BaseUrl, $"v0/subjects/{subjectId}"),
            BangumiJsonContext.Default.BangumiSubjectDto,
            allowNotFound: true,
            notFoundCode: null,
            cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return null;
        }

        if (value.Id != subjectId || string.IsNullOrWhiteSpace(value.Name))
        {
            throw Failure(MetadataFailureKind.Protocol, "bangumi_subject_invalid");
        }

        return new BangumiSubject(
            value.Id,
            value.Name.Trim(),
            value.ChineseName?.Trim() ?? string.Empty,
            ParseDate(value.Date),
            value.EpisodeCount > 0
                ? value.EpisodeCount
                : Math.Max(0, value.TotalEpisodeCount));
    }

    public async Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0)
        {
            throw Failure(MetadataFailureKind.InvalidInput, "bangumi_subject_id_invalid");
        }

        var values = await GetAsync(
            new Uri(_options.BaseUrl, $"v0/subjects/{subjectId}/subjects"),
            BangumiJsonContext.Default.BangumiSubjectRelationDtoArray,
            allowNotFound: false,
            "bangumi_relations_not_found",
            cancellationToken).ConfigureAwait(false)
            ?? throw Failure(
                MetadataFailureKind.Protocol,
                "bangumi_empty_response");
        if (values.Any(value => value.Id <= 0
            || string.IsNullOrWhiteSpace(value.Name)
            || string.IsNullOrWhiteSpace(value.Relation)))
        {
            throw Failure(MetadataFailureKind.Protocol, "bangumi_relations_invalid");
        }

        return values.Select(value => new BangumiSubjectRelation(
            value.Id,
            value.Type,
            value.Name!.Trim(),
            value.ChineseName?.Trim() ?? string.Empty,
            value.Relation!.Trim())).ToArray();
    }

    public async Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0)
        {
            throw Failure(MetadataFailureKind.InvalidInput, "bangumi_subject_id_invalid");
        }

        var episodes = new List<BangumiEpisode>();
        var offset = 0;
        while (true)
        {
            var page = await GetAsync(
                new Uri(
                    _options.BaseUrl,
                    $"v0/episodes?subject_id={subjectId}&type=0&limit={EpisodePageSize}&offset={offset}"),
                BangumiJsonContext.Default.BangumiEpisodePageDto,
                allowNotFound: true,
                notFoundCode: null,
                cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                return [];
            }

            var data = page.Data ?? [];
            if (page.Total < 0
                || page.Total > MaximumEpisodes
                || page.Limit is < 1 or > EpisodePageSize
                || page.Offset != offset
                || data.Length > page.Limit
                || episodes.Count + data.Length > page.Total
                || episodes.Count + data.Length > MaximumEpisodes)
            {
                throw Failure(
                    MetadataFailureKind.Protocol,
                    "bangumi_episode_page_invalid");
            }

            episodes.AddRange(data.Select(value => new BangumiEpisode(
                value.Id,
                value.Type,
                value.EpisodeNumber,
                ParseOptionalDate(value.AirDate))));

            var nextOffset = offset + data.Length;
            if (nextOffset >= page.Total)
            {
                break;
            }

            if (data.Length == 0 || nextOffset <= offset)
            {
                throw Failure(
                    MetadataFailureKind.Protocol,
                    "bangumi_episode_page_invalid");
            }

            offset = nextOffset;
        }

        return episodes;
    }

    private async Task<T?> GetAsync<T>(
        Uri uri,
        JsonTypeInfo<T> jsonTypeInfo,
        bool allowNotFound,
        string? notFoundCode,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await MetadataRetryExecutor.ExecuteAsync(
                async attemptToken =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        uri);
                    request.Headers.UserAgent.ParseAdd("AnimeGoNet/0.1");
                    using var response = await _httpClient
                        .SendAsync(request, attemptToken)
                        .ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (allowNotFound)
                        {
                            return null;
                        }

                        throw Failure(
                            MetadataFailureKind.SemanticNoMatch,
                            notFoundCode ?? "bangumi_not_found");
                    }

                    ThrowForStatus(response.StatusCode);
                    return await response.Content
                        .ReadFromJsonAsync(jsonTypeInfo, attemptToken)
                        .ConfigureAwait(false)
                        ?? throw Failure(
                            MetadataFailureKind.Protocol,
                            "bangumi_empty_response");
                },
                _options.HttpTimeout,
                _options.RetryCount,
                _options.RetryDelay,
                static exception =>
                    exception is BangumiClientException
                    {
                        Kind: MetadataFailureKind.RemoteService,
                    },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure(MetadataFailureKind.Network, "bangumi_timeout");
        }
        catch (HttpRequestException)
        {
            throw Failure(
                MetadataFailureKind.Network,
                "bangumi_network_error");
        }
        catch (JsonException)
        {
            throw Failure(MetadataFailureKind.Protocol, "bangumi_invalid_json");
        }
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : throw Failure(MetadataFailureKind.Protocol, "bangumi_date_invalid");
    }

    private static DateOnly? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static void ThrowForStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and <= 299)
        {
            return;
        }

        throw statusCode switch
        {
            HttpStatusCode.TooManyRequests => Failure(MetadataFailureKind.RemoteService, "bangumi_rate_limited"),
            _ when code >= 500 => Failure(MetadataFailureKind.RemoteService, "bangumi_service_error"),
            _ => Failure(MetadataFailureKind.Protocol, "bangumi_http_error"),
        };
    }

    private static BangumiClientException Failure(MetadataFailureKind kind, string code) => new(kind, code);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

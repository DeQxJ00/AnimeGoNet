using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class BangumiSubjectClient : IBangumiSubjectClient, IDisposable
{
    private static readonly Uri BaseUrl = new("https://api.bgm.tv/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public BangumiSubjectClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
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

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, $"v0/subjects/{subjectId}"));
        request.Headers.UserAgent.ParseAdd("AnimeGoNet/0.1");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            ThrowForStatus(response.StatusCode);
            var value = await response.Content.ReadFromJsonAsync(
                BangumiJsonContext.Default.BangumiSubjectDto,
                timeout.Token).ConfigureAwait(false)
                ?? throw Failure(MetadataFailureKind.Protocol, "bangumi_empty_response");
            if (value.Id != subjectId || string.IsNullOrWhiteSpace(value.Name))
            {
                throw Failure(MetadataFailureKind.Protocol, "bangumi_subject_invalid");
            }

            return new BangumiSubject(
                value.Id,
                value.Name.Trim(),
                value.ChineseName?.Trim() ?? string.Empty,
                ParseDate(value.Date),
                value.EpisodeCount > 0 ? value.EpisodeCount : Math.Max(0, value.TotalEpisodeCount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure(MetadataFailureKind.Network, "bangumi_timeout");
        }
        catch (HttpRequestException)
        {
            throw Failure(MetadataFailureKind.Network, "bangumi_network_error");
        }
        catch (JsonException)
        {
            throw Failure(MetadataFailureKind.Protocol, "bangumi_invalid_json");
        }
    }

    public async Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0)
        {
            throw Failure(MetadataFailureKind.InvalidInput, "bangumi_subject_id_invalid");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(BaseUrl, $"v0/subjects/{subjectId}/subjects"));
        request.Headers.UserAgent.ParseAdd("AnimeGoNet/0.1");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw Failure(MetadataFailureKind.SemanticNoMatch, "bangumi_relations_not_found");
            }

            ThrowForStatus(response.StatusCode);
            var values = await response.Content.ReadFromJsonAsync(
                BangumiJsonContext.Default.BangumiSubjectRelationDtoArray,
                timeout.Token).ConfigureAwait(false)
                ?? throw Failure(MetadataFailureKind.Protocol, "bangumi_empty_response");
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure(MetadataFailureKind.Network, "bangumi_timeout");
        }
        catch (HttpRequestException)
        {
            throw Failure(MetadataFailureKind.Network, "bangumi_network_error");
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

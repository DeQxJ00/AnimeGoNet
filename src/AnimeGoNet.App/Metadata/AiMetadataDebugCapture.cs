using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

internal sealed class AiMetadataDebugCapture
{
    private readonly string _traceId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly AiMetadataDebugIdentity? _identity;
    private readonly string _apiMode;
    private readonly string _model;
    private readonly AiMetadataDebugPreAiContext? _preAiContext;
    private readonly List<AiMetadataDebugExchange> _exchanges = [];
    private int _sequence;
    private string _promptTemplate = string.Empty;
    private string _prompt = string.Empty;

    public AiMetadataDebugCapture(AiMetadataMatchInput input, AiMatchingOptions options)
    {
        _identity = input.DebugIdentity;
        _apiMode = options.ApiMode == AiApiMode.Responses
            ? "responses"
            : "chat_completions";
        _model = options.Model ?? string.Empty;
        _preAiContext = input.DebugPreAiContext;
    }

    public void SetPrompt(string promptTemplate, string prompt)
    {
        _promptTemplate = promptTemplate;
        _prompt = prompt;
    }

    public void Record(
        string channel,
        string operation,
        Uri endpoint,
        string? requestBody,
        int? statusCode,
        string? responseBody,
        long durationMilliseconds,
        string? error = null)
    {
        _exchanges.Add(new AiMetadataDebugExchange(
            ++_sequence,
            channel,
            operation,
            endpoint.AbsoluteUri,
            requestBody,
            statusCode,
            responseBody,
            durationMilliseconds,
            error));
    }

    public AiMetadataDebugChain Complete(
        string? rawOutput,
        AiMetadataMatchCandidate? candidate,
        AiMetadataProviderUsage? usage,
        string? failureCode = null) =>
        new(
            _traceId,
            _identity?.RunId,
            _identity?.TaskId,
            _startedAtUtc,
            DateTimeOffset.UtcNow,
            AiMetadataPromptRenderer.PromptVersion,
            _apiMode,
            _model,
            _preAiContext,
            _promptTemplate,
            _prompt,
            _exchanges.ToArray(),
            rawOutput,
            candidate,
            usage,
            failureCode);
}

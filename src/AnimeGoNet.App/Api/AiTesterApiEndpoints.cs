using System.Buffers;
using System.Text.Json;
using AnimeGoNet.App.AiTesterCompat;

namespace AnimeGoNet.App.Api;

internal static class AiTesterApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/ai-test/run", (HttpContext context, AiTesterCoordinator _) => RunAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/run", (HttpContext context, AiTesterCoordinator _) => RunAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/v1/ai-test/run-stream", (HttpContext context, AiTesterCoordinator _) => RunStreamAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/run-stream", (HttpContext context, AiTesterCoordinator _) => RunStreamAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/v1/ai-test/stop", (HttpContext context, AiTesterCoordinator _) => StopAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/stop", (HttpContext context, AiTesterCoordinator _) => StopAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/v1/ai-test/torrent-import", (HttpContext context, AiTesterCoordinator _) => ImportTorrentAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/import-torrent", (HttpContext context, AiTesterCoordinator _) => ImportTorrentAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/v1/ai-test/mikan-import", (HttpContext context, AiTesterCoordinator _) => ImportMikanAsync(context)).WithTags("AI Tester");
        app.MapPost("/api/import-mikan-episode", (HttpContext context, AiTesterCoordinator _) => ImportMikanAsync(context)).WithTags("AI Tester");
        app.MapGet("/api/v1/ai-test/bootstrap", (HttpContext context, AiTesterCoordinator _) => BootstrapAsync(context)).WithTags("AI Tester");
    }

    private static async Task BootstrapAsync(HttpContext context)
    {
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        var response = new TesterBootstrapResponse(
            coordinator.Defaults,
            coordinator.EffectivePromptTemplate);
        await WriteJsonAsync(
            context,
            response,
            AiTesterJsonContext.Default.TesterBootstrapResponse).ConfigureAwait(false);
    }

    private static async Task ImportTorrentAsync(HttpContext context)
    {
        TorrentImportRequest? request = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            AiTesterJsonContext.Default.TorrentImportRequest,
            context.RequestAborted).ConfigureAwait(false);
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        TorrentImportResponse response = coordinator.ImportTorrent(request);
        context.Response.StatusCode = response.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        await WriteJsonAsync(context, response, AiTesterJsonContext.Default.TorrentImportResponse)
            .ConfigureAwait(false);
    }

    private static async Task ImportMikanAsync(HttpContext context)
    {
        MikanEpisodeImportRequest? request = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            AiTesterJsonContext.Default.MikanEpisodeImportRequest,
            context.RequestAborted).ConfigureAwait(false);
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        MikanEpisodeImportResponse response = await coordinator
            .ImportMikanAsync(request, context.RequestAborted)
            .ConfigureAwait(false);
        context.Response.StatusCode = response.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        await WriteJsonAsync(context, response, AiTesterJsonContext.Default.MikanEpisodeImportResponse)
            .ConfigureAwait(false);
    }

    private static async Task RunAsync(HttpContext context)
    {
        UiRunRequest? request = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            AiTesterJsonContext.Default.UiRunRequest,
            context.RequestAborted).ConfigureAwait(false);
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        var validationError = coordinator.ValidateRequest(request);
        if (validationError is not null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJsonAsync(
                context,
                FailedRun(validationError),
                AiTesterJsonContext.Default.UiRunResponse).ConfigureAwait(false);
            return;
        }

        (Guid runId, CancellationTokenSource cancellation) = coordinator.RegisterRun(request!.RunId);
        try
        {
            UiRunResponse response = await coordinator.ExecuteAsync(request, null, cancellation.Token)
                .ConfigureAwait(false);
            context.Response.StatusCode = response.Success
                ? StatusCodes.Status200OK
                : StatusCodes.Status502BadGateway;
            await WriteJsonAsync(context, response, AiTesterJsonContext.Default.UiRunResponse)
                .ConfigureAwait(false);
        }
        finally
        {
            coordinator.CompleteRun(runId, cancellation);
        }
    }

    private static async Task RunStreamAsync(HttpContext context)
    {
        context.Response.ContentType = "application/x-ndjson; charset=utf-8";
        UiRunRequest? request = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            AiTesterJsonContext.Default.UiRunRequest,
            context.RequestAborted).ConfigureAwait(false);
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        var validationError = coordinator.ValidateRequest(request);
        if (validationError is not null)
        {
            await WriteEnvelopeAsync(context, new("error", Error: validationError), context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        (Guid runId, CancellationTokenSource cancellation) = coordinator.RegisterRun(request!.RunId);
        try
        {
            await WriteEnvelopeAsync(
                context,
                new("progress", new ExecutionProgress("status", 0, "开始执行")),
                cancellation.Token).ConfigureAwait(false);
            UiRunResponse result = await coordinator.ExecuteAsync(
                request,
                (progress, token) => WriteEnvelopeAsync(context, new("progress", progress), token),
                cancellation.Token).ConfigureAwait(false);
            await WriteEnvelopeAsync(context, new("result", Result: result), cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await WriteEnvelopeAsync(
                context,
                new("stopped", Error: "Execution stopped by user."),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException
                or ArgumentException
                or IOException)
        {
            await WriteEnvelopeAsync(
                context,
                new("error", Error: exception.Message),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            coordinator.CompleteRun(runId, cancellation);
        }
    }

    private static async Task StopAsync(HttpContext context)
    {
        UiStopRequest? request = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            AiTesterJsonContext.Default.UiStopRequest,
            context.RequestAborted).ConfigureAwait(false);
        var coordinator = context.RequestServices.GetRequiredService<AiTesterCoordinator>();
        UiStopResponse response = coordinator.Stop(request);
        context.Response.StatusCode = response.Stopped
            ? StatusCodes.Status200OK
            : StatusCodes.Status400BadRequest;
        await WriteJsonAsync(context, response, AiTesterJsonContext.Default.UiStopResponse)
            .ConfigureAwait(false);
    }

    private static UiRunResponse FailedRun(string message) =>
        new(false, 0, string.Empty, null, ApiUsageParser.Unavailable, 0, message, false, null, null);

    private static async Task WriteJsonAsync<T>(
        HttpContext context,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            value,
            typeInfo,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async ValueTask WriteEnvelopeAsync(
        HttpContext context,
        UiStreamEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, envelope, AiTesterJsonContext.Default.UiStreamEnvelope);
        }
        await context.Response.Body.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await context.Response.Body.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

using System.Text.Json;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Serialization;

namespace AnimeGoNet.App.Api;

internal static class ConfigurationArchiveEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/configuration-archive/export", Export);
        app.MapPost("/api/v1/configuration-archive/import/preview", PreviewImport);
        app.MapPost("/api/v1/configuration-archive/import", Import);
        app.MapGet("/api/v1/configuration-archive/backups", ListBackups);
        app.MapPost("/api/v1/configuration-archive/backups", CreateBackup);
        app.MapGet("/api/v1/configuration-archive/automation", GetAutomation);
        app.MapPut("/api/v1/configuration-archive/automation", UpdateAutomation);
        app.MapGet("/api/v1/configuration-archive/backups/{backupId}/download", DownloadBackup);
        app.MapPost("/api/v1/configuration-archive/backups/{backupId}/restore", RestoreBackup);
        app.MapDelete("/api/v1/configuration-archive/backups/{backupId}", DeleteBackup);
    }

    private static async Task<IResult> Export(
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        var bytes = await service.ExportAsync(cancellationToken).ConfigureAwait(false);
        return Results.File(
            bytes,
            "application/json; charset=utf-8",
            $"animegonet-config-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static async Task<IResult> PreviewImport(
        HttpContext context,
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        try
        {
            SetMaximumRequestBody(context);
            var preview = await service.PreviewAsync(
                context.Request.Body, context.Request.ContentLength, cancellationToken).ConfigureAwait(false);
            return Results.Json(
                preview,
                ConfigurationArchiveJsonContext.Default.ConfigurationArchivePreview);
        }
        catch (ConfigurationArchiveException exception)
        {
            return Failure(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failure(new ConfigurationArchiveException(
                "configuration_archive_validation_failed", exception.Message));
        }
    }

    private static async Task<IResult> Import(
        HttpContext context,
        string? expected_sha256,
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected_sha256))
        {
            return Failure(new ConfigurationArchiveException(
                "configuration_archive_preview_required",
                "Preview the archive first and supply its SHA-256 digest."));
        }
        try
        {
            SetMaximumRequestBody(context);
            var result = await service.ImportAsync(
                context.Request.Body,
                context.Request.ContentLength,
                expected_sha256,
                cancellationToken).ConfigureAwait(false);
            return Results.Json(
                result,
                ConfigurationArchiveJsonContext.Default.ConfigurationArchiveApplyResult);
        }
        catch (ConfigurationArchiveException exception)
        {
            return Failure(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Json(
                new ApiErrorResponse("configuration_archive_apply_failed", exception.Message),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ListBackups(
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        var backups = await service.ListBackupsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            backups,
            ConfigurationArchiveJsonContext.Default.IReadOnlyListConfigurationArchiveBackup);
    }

    private static async Task<IResult> CreateBackup(
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        var backup = await service.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            backup,
            ConfigurationArchiveJsonContext.Default.ConfigurationArchiveBackup,
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAutomation(
        ConfigurationBackupAutomationStore store,
        CancellationToken cancellationToken)
    {
        var policy = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            policy,
            ConfigurationBackupAutomationJsonContext.Default.ConfigurationBackupAutomationPolicy);
    }

    private static async Task<IResult> UpdateAutomation(
        HttpContext context,
        ConfigurationBackupAutomationStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            var policy = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                ConfigurationBackupAutomationJsonContext.Default.ConfigurationBackupAutomationPolicy,
                cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException("Automatic configuration backup policy is required.");
            var saved = await store.SaveAsync(policy, cancellationToken).ConfigureAwait(false);
            return Results.Json(
                saved,
                ConfigurationBackupAutomationJsonContext.Default.ConfigurationBackupAutomationPolicy);
        }
        catch (JsonException exception)
        {
            return Results.Json(
                new ApiErrorResponse("configuration_backup_automation_json_invalid", exception.Message),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException exception)
        {
            return Results.Json(
                new ApiErrorResponse("configuration_backup_automation_invalid", exception.Message),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> DownloadBackup(
        string backupId,
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await service.ReadBackupAsync(backupId, cancellationToken).ConfigureAwait(false);
            return Results.File(bytes, "application/json; charset=utf-8", backupId + ".json");
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ConfigurationArchiveException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> RestoreBackup(
        string backupId,
        ConfigurationArchiveService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RestoreAsync(backupId, cancellationToken).ConfigureAwait(false);
            return Results.Json(
                result,
                ConfigurationArchiveJsonContext.Default.ConfigurationArchiveApplyResult);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ConfigurationArchiveException exception)
        {
            return Failure(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Json(
                new ApiErrorResponse("configuration_archive_restore_failed", exception.Message),
                ApiJsonContext.Default.ApiErrorResponse,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> DeleteBackup(
        string backupId,
        ConfigurationArchiveService service)
    {
        try
        {
            await service.DeleteBackupAsync(backupId).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ConfigurationArchiveException exception)
        {
            return Failure(exception);
        }
    }

    private static void SetMaximumRequestBody(HttpContext context)
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = ConfigurationArchiveService.MaximumArchiveBytes;
    }

    private static IResult Failure(ConfigurationArchiveException exception) =>
        Results.Json(
            new ApiErrorResponse(exception.Code, exception.Message),
            ApiJsonContext.Default.ApiErrorResponse,
            statusCode: exception.Code.Contains("size", StringComparison.Ordinal)
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest);
}

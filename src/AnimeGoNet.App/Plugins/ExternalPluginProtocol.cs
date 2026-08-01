using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Plugins;

public static class ExternalPluginMethods
{
    public const string Initialize = "initialize";
    public const string Execute = "execute";
    public const string Health = "health";
    public const string Shutdown = "shutdown";
}

public sealed record ExternalPluginWireRequest(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("operation")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Operation,
    [property: JsonPropertyName("payload")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Payload,
    [property: JsonPropertyName("config")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Config);

public sealed record ExternalPluginWireResponse(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] JsonElement? Result,
    [property: JsonPropertyName("error")] ExternalPluginWireError? Error);

public sealed record ExternalPluginWireError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message);

public sealed record ExternalPluginInitializePayload(
    [property: JsonPropertyName("hostVersion")] string HostVersion,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("pluginVersion")] string PluginVersion,
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities);

public sealed record ExternalPluginInitializeResult(
    [property: JsonPropertyName("pluginId")] string? PluginId,
    [property: JsonPropertyName("pluginVersion")] string? PluginVersion,
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string>? Capabilities);

public sealed record ExternalPluginHealthResult(
    [property: JsonPropertyName("healthy")] bool Healthy);

public sealed record ExternalPluginShutdownPayload(
    [property: JsonPropertyName("reason")] string Reason);

public sealed record ExternalPluginSessionOptions
{
    public TimeSpan InitializeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ExecuteTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaximumRequestBytes { get; init; } = 1024 * 1024;

    public int MaximumResponseBytes { get; init; } = 1024 * 1024;

    public int StderrBufferBytes { get; init; } = 4096;

    public void Validate()
    {
        ValidateTimeout(InitializeTimeout, nameof(InitializeTimeout));
        ValidateTimeout(ExecuteTimeout, nameof(ExecuteTimeout));
        ValidateTimeout(HealthTimeout, nameof(HealthTimeout));
        ValidateTimeout(ShutdownTimeout, nameof(ShutdownTimeout));
        if (MaximumRequestBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRequestBytes),
                "External plugin request limit must be between 1 KiB and 16 MiB.");
        }
        if (MaximumResponseBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumResponseBytes),
                "External plugin response limit must be between 1 KiB and 16 MiB.");
        }
        if (StderrBufferBytes is < 256 or > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StderrBufferBytes),
                "External plugin stderr buffer must be between 256 bytes and 64 KiB.");
        }
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                name,
                "External plugin timeouts must be positive and no longer than one hour.");
        }
    }
}

public enum ExternalPluginSessionState
{
    Created,
    Ready,
    Faulted,
    Stopped,
}

public class ExternalPluginProtocolException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class ExternalPluginRemoteException(
    string code,
    string message) : ExternalPluginProtocolException(code, message);

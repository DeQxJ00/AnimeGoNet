using System.Diagnostics;

namespace AnimeGoNet.App.Networking;

internal sealed partial class OutboundHttpLogSink(params ILogger[] loggers)
{
    public OutboundHttpRequestTrace Start(string service, HttpMethod method, Uri uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);

        var endpoint = SafeEndpoint(uri);
        foreach (var logger in loggers)
        {
            LogStarted(logger, service, method.Method, endpoint);
        }

        return new OutboundHttpRequestTrace(
            this,
            service,
            method.Method,
            endpoint,
            Stopwatch.StartNew());
    }

    private void Complete(
        string service,
        string method,
        string endpoint,
        int statusCode,
        long durationMilliseconds)
    {
        foreach (var logger in loggers)
        {
            LogCompleted(
                logger,
                service,
                method,
                endpoint,
                statusCode,
                durationMilliseconds);
        }
    }

    private void Fail(
        string service,
        string method,
        string endpoint,
        string failure,
        long durationMilliseconds)
    {
        foreach (var logger in loggers)
        {
            LogFailed(
                logger,
                service,
                method,
                endpoint,
                failure,
                durationMilliseconds);
        }
    }

    private static string SafeEndpoint(Uri uri) =>
        uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);

    [LoggerMessage(
        EventId = 4700,
        Level = LogLevel.Information,
        Message = "External HTTP request started: {Service} {Method} {Endpoint}")]
    private static partial void LogStarted(
        ILogger logger,
        string service,
        string method,
        string endpoint);

    [LoggerMessage(
        EventId = 4701,
        Level = LogLevel.Information,
        Message = "External HTTP request completed: {Service} {Method} {Endpoint} status {StatusCode} in {DurationMilliseconds} ms")]
    private static partial void LogCompleted(
        ILogger logger,
        string service,
        string method,
        string endpoint,
        int statusCode,
        long durationMilliseconds);

    [LoggerMessage(
        EventId = 4702,
        Level = LogLevel.Warning,
        Message = "External HTTP request failed: {Service} {Method} {Endpoint} failure {Failure} in {DurationMilliseconds} ms")]
    private static partial void LogFailed(
        ILogger logger,
        string service,
        string method,
        string endpoint,
        string failure,
        long durationMilliseconds);

    internal sealed class OutboundHttpRequestTrace(
        OutboundHttpLogSink owner,
        string service,
        string method,
        string endpoint,
        Stopwatch stopwatch)
    {
        private int _finished;

        public void Complete(int statusCode)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            owner.Complete(service, method, endpoint, statusCode, stopwatch.ElapsedMilliseconds);
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (Interlocked.Exchange(ref _finished, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            var failure = exception is OperationCanceledException
                ? "canceled_or_timed_out"
                : exception.GetType().Name;
            owner.Fail(service, method, endpoint, failure, stopwatch.ElapsedMilliseconds);
        }
    }
}

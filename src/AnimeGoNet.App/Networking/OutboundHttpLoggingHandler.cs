namespace AnimeGoNet.App.Networking;

internal sealed class OutboundHttpLoggingHandler(
    HttpMessageHandler innerHandler,
    OutboundHttpLogSink logSink,
    string service) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var trace = logSink.Start(service, request.Method, request.RequestUri);
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            trace.Complete((int)response.StatusCode);
            return response;
        }
        catch (Exception exception)
        {
            trace.Fail(exception);
            throw;
        }
    }
}

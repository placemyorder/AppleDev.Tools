using Serilog;

namespace AppleAppStoreConnect;

public class MetaDataHandler(bool shouldLogHttpRequests) : DelegatingHandler(new HttpClientHandler())
{
    private static readonly ILogger Logger = Log.ForContext<MetaDataHandler>();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (shouldLogHttpRequests)
        {
            Logger.Information($">> {request.Method} REQUEST TO: {request.RequestUri}");

            if (request.Headers.Any())
            {
                Logger.Information($">> HEADERS START");
            }

            foreach (var header in request.Headers)
            {
                Logger.Information($">> {header.Key.ToUpper()}      : {header.Value?.FirstOrDefault()}");
            }

            if (request.Headers.Any())
            {
                Logger.Information($">> HEADERS END");
            }


            if (request.Content != null)
            {
                Logger.Information($">> CONTENT   : {request.Content.ReadAsStringAsync(cancellationToken).Result}");
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (shouldLogHttpRequests)
        {
            Logger.Information($">> RESPONSE TO: {request.RequestUri}");

            if (response.Content != null)
            {
                Logger.Information(
                    $">> CONTENT   : {response.Content.ReadAsStringAsync(cancellationToken).Result}");
            }
        }


        return response;
    }
}
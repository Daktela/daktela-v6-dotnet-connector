using System.Net;
using System.Text;

namespace Daktela.Connector.Tests;

/// <summary>
/// Minimal HTTP stub that records requests and answers from a callback receiving the
/// 1-based request number and the request.
/// </summary>
internal sealed class StubHandler(Func<int, HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private int _count;

    public StubHandler(Func<int, HttpResponseMessage> responder)
        : this((number, _) => responder(number))
    {
    }

    public List<Uri> Urls { get; } = new();
    public List<string?> Bodies { get; } = new();
    public List<string> UserAgents { get; } = new();

    /// <summary>
    /// The X-AUTH-TOKEN header of each request, or null when it was not sent.
    /// </summary>
    public List<string?> AuthTokens { get; } = new();

    /// <summary>
    /// The endpoint file name of each request, such as <c>login.json</c>.
    /// </summary>
    public IEnumerable<string> Endpoints => Urls.Select(url => url.Segments[^1]);

    public static StubHandler Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(_ => Response(json, statusCode));

    public static HttpResponseMessage Response(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public DaktelaClient CreateClient(Action<DaktelaConfig>? configure = null, TimeProvider? timeProvider = null)
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "https://tenant.daktela.test",
            AccessToken = "secret-token",
            Timeout = TimeSpan.FromSeconds(2)
        };
        configure?.Invoke(config);
        return new DaktelaClient(config, new HttpClient(this), timeProvider ?? TimeProvider.System);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        int number;
        lock (Urls)
        {
            number = ++_count;
            Urls.Add(request.RequestUri!);
            Bodies.Add(body);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            AuthTokens.Add(request.Headers.TryGetValues("X-AUTH-TOKEN", out var values)
                ? values.Single()
                : null);
        }

        return responder(number, request);
    }
}

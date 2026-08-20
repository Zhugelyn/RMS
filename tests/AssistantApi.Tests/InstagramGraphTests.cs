using System.Net;
using System.Net.Http;
using AssistantApi.Instagram;
using AssistantApi.Options;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class InstagramGraphTests
{
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "AssistantApi.Tests", "Fixtures", "instagram", name);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            var outputCopy = Path.Combine(dir.FullName, "Fixtures", "instagram", name);
            if (File.Exists(outputCopy))
            {
                return File.ReadAllText(outputCopy);
            }

            dir = dir.Parent;
        }

        throw new System.IO.FileNotFoundException($"Fixture not found: {name}");
    }

    [Fact]
    public void Mapper_maps_media_list_fixture()
    {
        var json = Fixture("media-list.json");
        var items = InstagramMediaMapper.MapMediaList(json);
        Assert.Equal(2, items.Count);

        Assert.Equal("179000111222333", items[0].Id);
        Assert.Equal("IMAGE", items[0].MediaType);
        Assert.Equal("Morning glow at Babor", items[0].Caption);
        Assert.Equal("https://scontent.cdninstagram.com/v/t51.2885-15/img1.jpg", items[0].MediaUrl);
        Assert.Equal("https://www.instagram.com/p/ABC123/", items[0].Permalink);
        Assert.NotNull(items[0].Timestamp);
        Assert.Equal(2024, items[0].Timestamp!.Value.Year);

        Assert.Equal("179000444555666", items[1].Id);
        Assert.Equal("VIDEO", items[1].MediaType);
        Assert.Contains("fbcdn.net", items[1].MediaUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mapper_maps_insights_fixture()
    {
        var json = Fixture("media-insights.json");
        var insights = InstagramMediaMapper.MapInsights(json);
        Assert.Equal(1200, insights.Impressions);
        Assert.Equal(900, insights.Reach);
        Assert.Equal(85, insights.Engagement);
        Assert.Equal(12, insights.Saved);
        Assert.Equal(400, insights.VideoViews);
        Assert.True(insights.Raw.ContainsKey("impressions"));
    }

    [Theory]
    [InlineData("https://scontent.cdninstagram.com/v/t51/x.jpg", true)]
    [InlineData("https://instagram.fxyz1-1.fna.fbcdn.net/v/t51/x.jpg", true)]
    [InlineData("https://evil.example.com/x.jpg", false)]
    [InlineData("http://scontent.cdninstagram.com/v/t51/x.jpg", false)]
    [InlineData("https://127.0.0.1/x.jpg", false)]
    [InlineData("https://cdninstagram.com.evil.com/x.jpg", false)]
    public void MediaUrlGuard_allowlist(string url, bool expected)
    {
        var ok = InstagramMediaUrlGuard.IsAllowedMediaUrl(url, out var uri, out _);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.NotNull(uri);
        }
    }

    [Fact]
    public async Task Stub_client_skips_without_token()
    {
        var client = new StubInstagramGraphClient();
        var result = await client.FetchOwnMediaAsync();
        Assert.Equal(InstagramFetchStatus.SkippedNoToken, result.Status);
        Assert.Empty(result.Items);
        Assert.Equal("instagram-token-missing", result.ErrorCode);
    }

    [Fact]
    public async Task Fallback_skips_when_store_empty()
    {
        var live = CreateLiveClient(
            new EmptyInstagramTokenStore(),
            new InstagramOptions { IgUserId = "17841400000000000" },
            new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var fallback = new FallbackInstagramGraphClient(
            new EmptyInstagramTokenStore(),
            MsOptions.Create(new InstagramOptions { IgUserId = "17841400000000000" }),
            live,
            new StubInstagramGraphClient());

        var result = await fallback.FetchOwnMediaAsync();
        Assert.Equal(InstagramFetchStatus.SkippedNoToken, result.Status);
        Assert.False(fallback.IsConfigured);
    }

    [Fact]
    public async Task Live_client_maps_fixture_response()
    {
        var mediaJson = Fixture("media-list.json");
        var insightsJson = Fixture("media-insights.json");
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            Assert.DoesNotContain("IGQVJ-REAL-SECRET", path, StringComparison.Ordinal);
            // Token is in query; tests must not assert/log the secret value beyond presence checks.
            Assert.Contains("access_token=", path, StringComparison.Ordinal);

            if (path.Contains("/insights", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(insightsJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mediaJson)
            };
        });

        var options = new InstagramOptions
        {
            IgUserId = "17841400000000000",
            GraphBaseUrl = "https://graph.facebook.com/v21.0/"
        };
        var store = new FakeTokenStore("test-token-not-a-real-secret");
        var client = CreateLiveClient(store, options, handler);

        var result = await client.FetchOwnMediaAsync(includeInsights: true);
        Assert.Equal(InstagramFetchStatus.Ok, result.Status);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.InsightsAttempted);
        Assert.True(result.InsightsAvailable);
        Assert.Equal(1200, result.Items[0].Insights!.Impressions);
    }

    [Fact]
    public async Task Live_client_rate_limit_is_soft_error()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("""{"error":{"message":"(#4) Application request limit reached","type":"OAuthException","code":4}}""")
        });
        var client = CreateLiveClient(
            new FakeTokenStore("tok"),
            new InstagramOptions { BusinessAccountId = "17841400000000000" },
            handler);

        var result = await client.FetchOwnMediaAsync(includeInsights: false);
        Assert.Equal(InstagramFetchStatus.RateLimited, result.Status);
        Assert.Equal("instagram-rate-limited", result.ErrorCode);
        Assert.DoesNotContain("tok", result.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_client_token_expiry_is_soft_error()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"Error validating access token: Session has expired","type":"OAuthException","code":190}}""")
        });
        var client = CreateLiveClient(
            new FakeTokenStore("expired-token-value"),
            new InstagramOptions { IgUserId = "17841400000000000" },
            handler);

        var result = await client.FetchOwnMediaAsync(includeInsights: false);
        Assert.Equal(InstagramFetchStatus.TokenExpired, result.Status);
        Assert.Equal("instagram-token-expired", result.ErrorCode);
        Assert.DoesNotContain("expired-token-value", result.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_token_store_scrubs_plaintext_and_roundtrips()
    {
        const string master = "ig-test-master-key-32chars!!";
        const string token = "IGQVJ-test-access-token-value";
        var protector = new AesGcmSecretProtector(master);
        var options = MsOptions.Create(new InstagramOptions
        {
            AccessToken = token,
            MasterKey = master,
            IgUserId = "1"
        });
        var store = new EncryptedInstagramTokenStore(options, protector);
        Assert.True(store.HasToken);
        Assert.Equal(string.Empty, options.Value.AccessToken);
        Assert.True(store.TryGetAccessToken(out var decrypted));
        Assert.Equal(token, decrypted);
    }

    [Fact]
    public void ClassifyGraphError_does_not_echo_body_secrets()
    {
        var body = """{"error":{"message":"Invalid OAuth access token secret-ABC","type":"OAuthException","code":190}}""";
        var (code, message, status) = HttpInstagramGraphClient.ClassifyGraphError(HttpStatusCode.Unauthorized, body);
        Assert.Equal(InstagramFetchStatus.TokenExpired, status);
        Assert.Equal("instagram-token-expired", code);
        Assert.DoesNotContain("secret-ABC", message, StringComparison.Ordinal);
    }

    private static HttpInstagramGraphClient CreateLiveClient(
        IInstagramTokenStore store,
        InstagramOptions options,
        HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new HttpInstagramGraphClient(
            http,
            store,
            MsOptions.Create(options),
            NullLogger<HttpInstagramGraphClient>.Instance);
    }

    private sealed class FakeTokenStore : IInstagramTokenStore
    {
        private readonly string _token;
        public FakeTokenStore(string token) => _token = token;
        public bool HasToken => true;
        public bool TryGetAccessToken(out string accessToken)
        {
            accessToken = _token;
            return true;
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}

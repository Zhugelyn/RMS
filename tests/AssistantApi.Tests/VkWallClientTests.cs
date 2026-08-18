using System.Net;
using System.Net.Http;
using AssistantApi.Options;
using AssistantApi.Security;
using AssistantApi.Vk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

public sealed class VkWallClientTests
{
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "AssistantApi.Tests", "Fixtures", "vk", name);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            var outputCopy = Path.Combine(dir.FullName, "Fixtures", "vk", name);
            if (File.Exists(outputCopy))
            {
                return File.ReadAllText(outputCopy);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Fixture not found: {name}");
    }

    [Fact]
    public void Mapper_maps_resolve_group_fixture()
    {
        var json = Fixture("resolve-group.json");
        var result = VkWallMapper.MapResolve(json);
        Assert.Equal(VkFetchStatus.Ok, result.Status);
        Assert.Equal("group", result.ObjectType);
        Assert.Equal(123456789, result.ObjectId);
        Assert.Equal(-123456789, result.OwnerId);
    }

    [Fact]
    public void Mapper_maps_wall_and_skips_donut()
    {
        var json = Fixture("wall-get.json");
        var posts = VkWallMapper.MapWallItems(json, out var skippedDonut);
        Assert.Equal(1, skippedDonut);
        Assert.Equal(2, posts.Count);

        Assert.Equal(101, posts[0].Id);
        Assert.Equal(-123456789, posts[0].OwnerId);
        Assert.Contains("Babor", posts[0].Text);
        Assert.Single(posts[0].Photos);
        Assert.Equal("https://sun9-12.userapi.com/impg/large.jpg", posts[0].Photos[0].Url);
        Assert.Equal(1280, posts[0].Photos[0].Width);
        Assert.DoesNotContain("evil.example.com", posts[0].Photos[0].Url);

        Assert.Equal(103, posts[1].Id);
        Assert.Empty(posts[1].Photos);
    }

    [Fact]
    public void Mapper_closed_wall_is_soft_skip()
    {
        var json = Fixture("wall-closed.json");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(VkWallMapper.TryMapApiError(doc.RootElement, out var status, out var code, out var message));
        Assert.Equal(VkFetchStatus.SoftSkippedClosed, status);
        Assert.Equal("vk-wall-closed", code);
        Assert.DoesNotContain("Access denied", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://sun9-12.userapi.com/impg/x.jpg", true)]
    [InlineData("https://userapi.com/x.jpg", true)]
    [InlineData("https://evil.example.com/x.jpg", false)]
    [InlineData("http://sun9-12.userapi.com/impg/x.jpg", false)]
    [InlineData("https://127.0.0.1/x.jpg", false)]
    [InlineData("https://userapi.com.evil.com/x.jpg", false)]
    [InlineData("https://vk.com/photo1_2", false)]
    public void CdnUrlGuard_allowlist(string url, bool expected)
    {
        var ok = VkCdnUrlGuard.IsAllowedMediaUrl(url, out var uri, out _);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.NotNull(uri);
        }
    }

    [Theory]
    [InlineData("@babor_bryansk", "babor_bryansk")]
    [InlineData("https://vk.com/club123", "club123")]
    [InlineData("vk.com/public456?w=wall", "public456")]
    [InlineData("", null)]
    [InlineData("bad name!", null)]
    public void NormalizeScreenName(string raw, string? expected)
    {
        Assert.Equal(expected, HttpVkWallClient.NormalizeScreenName(raw));
    }

    [Fact]
    public async Task Stub_client_skips_without_token()
    {
        var client = new StubVkWallClient();
        var result = await client.GetWallAsync(screenName: "club1");
        Assert.Equal(VkFetchStatus.SkippedNoToken, result.Status);
        Assert.Empty(result.Posts);
        Assert.Equal("vk-token-missing", result.ErrorCode);
        Assert.False(client.IsConfigured);
    }

    [Fact]
    public async Task Fallback_skips_when_store_empty()
    {
        var live = CreateLiveClient(
            new EmptyVkTokenStore(),
            new VkOptions(),
            new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var fallback = new FallbackVkWallClient(
            new EmptyVkTokenStore(),
            live,
            new StubVkWallClient());

        var result = await fallback.GetWallAsync(ownerId: -1);
        Assert.Equal(VkFetchStatus.SkippedNoToken, result.Status);
        Assert.False(fallback.IsConfigured);
    }

    [Fact]
    public async Task Live_client_resolve_then_wall()
    {
        var resolveJson = Fixture("resolve-group.json");
        var wallJson = Fixture("wall-get.json");
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            Assert.DoesNotContain("vk-real-secret-token", path, StringComparison.Ordinal);
            Assert.Contains("access_token=", path, StringComparison.Ordinal);

            if (path.Contains("utils.resolveScreenName", StringComparison.Ordinal))
            {
                Assert.Contains("screen_name=babor_test", path, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(resolveJson)
                };
            }

            Assert.Contains("wall.get", path, StringComparison.Ordinal);
            Assert.Contains("owner_id=-123456789", path, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(wallJson)
            };
        });

        var client = CreateLiveClient(
            new FakeTokenStore("test-token-not-a-real-secret"),
            new VkOptions { ApiBaseUrl = "https://api.vk.com/method/" },
            handler);

        var result = await client.GetWallAsync(screenName: "babor_test", count: 25);
        Assert.Equal(VkFetchStatus.Ok, result.Status);
        Assert.Equal(-123456789, result.OwnerId);
        Assert.Equal(2, result.Posts.Count);
        Assert.Equal(1, result.SkippedDonutCount);
    }

    [Fact]
    public async Task Live_client_closed_wall_soft_skip()
    {
        var closed = Fixture("wall-closed.json");
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(closed)
        });
        var client = CreateLiveClient(
            new FakeTokenStore("tok"),
            new VkOptions(),
            handler);

        var result = await client.GetWallAsync(ownerId: -1);
        Assert.Equal(VkFetchStatus.SoftSkippedClosed, result.Status);
        Assert.Equal("vk-wall-closed", result.ErrorCode);
        Assert.DoesNotContain("tok", result.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_client_rate_limit_is_soft_error()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"error":{"error_code":6,"error_msg":"Too many requests per second"}}""")
        });
        var client = CreateLiveClient(
            new FakeTokenStore("tok"),
            new VkOptions(),
            handler);

        var result = await client.GetWallAsync(ownerId: -42);
        Assert.Equal(VkFetchStatus.RateLimited, result.Status);
        Assert.Equal("vk-rate-limited", result.ErrorCode);
    }

    [Fact]
    public async Task Live_client_token_invalid_is_soft_error()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"error":{"error_code":5,"error_msg":"User authorization failed: invalid access_token secret-ABC"}}""")
        });
        var client = CreateLiveClient(
            new FakeTokenStore("expired-token-value"),
            new VkOptions(),
            handler);

        var result = await client.GetWallAsync(ownerId: -42);
        Assert.Equal(VkFetchStatus.TokenInvalid, result.Status);
        Assert.Equal("vk-token-invalid", result.ErrorCode);
        Assert.DoesNotContain("secret-ABC", result.Message ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("expired-token-value", result.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_token_store_scrubs_plaintext_and_roundtrips()
    {
        const string master = "vk-test-master-key-32chars!!";
        const string token = "vk-test-service-token-value";
        var protector = new AesGcmSecretProtector(master);
        var options = MsOptions.Create(new VkOptions
        {
            ServiceToken = token,
            MasterKey = master
        });
        var store = new EncryptedVkTokenStore(options, protector);
        Assert.True(store.HasToken);
        Assert.Equal(string.Empty, options.Value.ServiceToken);
        Assert.True(store.TryGetServiceToken(out var decrypted));
        Assert.Equal(token, decrypted);
    }

    [Fact]
    public void Fetch_hard_cap_matches_ig()
    {
        Assert.Equal(50, VkFetchLimits.MaxWallFetch);
        Assert.Equal(InstagramFetchLimits.MaxMediaFetch, VkFetchLimits.MaxWallFetch);
        Assert.Equal(VkFetchLimits.MaxWallFetch, Math.Clamp(999, 1, VkFetchLimits.MaxWallFetch));
    }

    private static HttpVkWallClient CreateLiveClient(
        IVkTokenStore store,
        VkOptions options,
        HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new HttpVkWallClient(
            http,
            store,
            MsOptions.Create(options),
            NullLogger<HttpVkWallClient>.Instance);
    }

    private sealed class FakeTokenStore : IVkTokenStore
    {
        private readonly string _token;
        public FakeTokenStore(string token) => _token = token;
        public bool HasToken => true;
        public bool TryGetServiceToken(out string serviceToken)
        {
            serviceToken = _token;
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

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TelegramGateway.Contracts;
using TelegramGateway.Services;

namespace TelegramGateway.Tests;

/// <summary>
/// Phase 5 UI hardening: studio polish markers, VK allowlist/gallery paths,
/// limits/a11y hints, non-goals (no RAG/Apify/MinIO/Direct in Mini App).
/// </summary>
public sealed class Phase5UiHardeningTests
{
    private static WebApplicationFactory<Program> CreateFactory(IAssistantApiClient assistant) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Telegram:BotToken"] = "000000000:TESTTOKEN_FOR_UNIT_TESTS",
                    ["Telegram:UsePolling"] = "false",
                    ["Assistant:BaseUrl"] = "http://assistant-api:8080",
                    ["Assistant:ServiceKey"] = "test-service-key-32chars-min!"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAssistantApiClient>();
                services.AddSingleton(assistant);
            });
        });

    [Fact]
    public async Task Studio_html_has_polish_a11y_and_vk_markers()
    {
        await using var factory = CreateFactory(new FakeAssistant());
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        var css = await client.GetStringAsync("/styles.css");

        Assert.Contains("skip-link", html);
        Assert.Contains("role=\"tablist\"", html);
        Assert.Contains("aria-busy", html);
        Assert.Contains("posts-feed", html);
        Assert.Contains("studio-source", html);
        Assert.Contains("data-vk-limit=\"10\"", html);
        Assert.Contains("research-vk", html);
        Assert.Contains("VK паблики", html);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.DoesNotContain("INSTAGRAM__ACCESSTOKEN", html);
        Assert.DoesNotContain("VK__SERVICETOKEN", html);
        Assert.DoesNotContain("access_token", html);
    }

    [Fact]
    public async Task Studio_js_enforces_limits_and_vk_allowlist_guards()
    {
        await using var factory = CreateFactory(new FakeAssistant());
        var client = factory.CreateClient();
        var js = await client.GetStringAsync("/app.js");

        Assert.Contains("galleryCap: 14", js);
        Assert.Contains("postsCap: 20", js);
        Assert.Contains("vkMax: 10", js);
        Assert.Contains("cadenceMin: 1", js);
        Assert.Contains("cadenceMax: 90", js);
        Assert.Contains("parseVkCommunities", js);
        Assert.Contains("Не вставляй VK/IG token", js);
        Assert.Contains("VK allowlist пуст", js);
        Assert.Contains("paintPosts", js);
        Assert.Contains("mediaLooksVk", js);
        Assert.Contains("aria-busy", js);
        Assert.Contains("clampCadence", js);
        Assert.DoesNotContain("openai.com/v1/images", js);
        Assert.DoesNotContain("api.apify.com", js);
        Assert.DoesNotContain("elasticsearch", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minio", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("yandex", js, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wwwroot_non_goals_not_wired()
    {
        var www = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "TelegramGateway", "wwwroot"));
        Assert.True(Directory.Exists(www), www);

        var forbidden = new[]
        {
            "api.apify.com",
            "using Apify",
            "openai.com/v1/images",
            "Elasticsearch",
            "embeddings",
            "MinIO",
            "Яндекс Директ",
            "api.direct.yandex"
        };

        foreach (var file in Directory.EnumerateFiles(www, "*.*", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file, Encoding.UTF8);
            foreach (var needle in forbidden)
            {
                Assert.DoesNotContain(needle, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Latest_proxy_passes_additive_source()
    {
        var assistant = new FakeAssistant
        {
            LatestOverride = new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto
                {
                    UserId = "tg-1",
                    VkCommunities = [new VkCommunityTargetDto { ScreenName = "babor" }]
                },
                Source = "vk",
                SnapshotSummary = "source=vk posts=3",
                Posts =
                [
                    new ResearchPostMetricDto { Caption = "vk post", MediaId = "1" }
                ],
                Items =
                [
                    new ResearchPlanItemDto
                    {
                        Date = DateOnly.FromDateTime(DateTime.UtcNow),
                        Caption = "day1",
                        MediaPath = "research-media/r1/vk/photo-01.jpg",
                        Status = "ready"
                    }
                ]
            }
        };

        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/miniapp/research/latest?userId=tg-1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ResearchLatestResponse>();
        Assert.NotNull(body);
        Assert.Equal("vk", body!.Source);
        Assert.Contains("source=vk", body.SnapshotSummary);
        Assert.Single(body.Posts);
        Assert.Single(body.Items);
        Assert.Contains("/vk/", body.Items[0].MediaPath);
        Assert.StartsWith("/api/miniapp/research/media?path=", body.Items[0].ImageUrl);
    }

    private sealed class FakeAssistant : IAssistantApiClient
    {
        public ResearchLatestResponse? LatestOverride { get; set; }

        public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = "m",
                Text = "[stub]",
                Provider = "stub"
            });

        public Task<ResearchSettingsDto> GetResearchSettingsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchSettingsDto { UserId = userId, CadenceDays = 14 });

        public Task<ResearchSettingsDto> PutResearchSettingsAsync(
            ResearchSettingsUpdateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchSettingsDto { UserId = request.UserId });

        public Task<ResearchRunResponse> RunResearchAsync(ResearchRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchRunResponse
            {
                Outcome = "NoOp",
                Settings = new ResearchSettingsDto { UserId = request.UserId }
            });

        public Task<ResearchLatestResponse> GetResearchLatestAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(LatestOverride ?? new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto { UserId = userId }
            });
    }
}

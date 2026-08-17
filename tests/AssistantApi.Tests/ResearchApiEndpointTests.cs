using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantApi.Research;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssistantApi.Tests;

public sealed class ResearchApiEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ServiceKey = "test-service-key-32chars-min!";

    private static WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Assistant:ServiceKey"] = ServiceKey
                    // Gateway:BaseUrl unset → NoOp notify in tests
                });
            });
            if (configure is not null)
            {
                builder.ConfigureTestServices(configure);
            }
        });

    private static HttpClient Authed(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
        return client;
    }

    [Fact]
    public async Task Settings_get_put_rejects_anonymous_and_accepts_tg_user()
    {
        await using var factory = CreateFactory();
        var client = Authed(factory);

        var anon = await client.GetAsync("/v1/research/settings?userId=mini-anonymous");
        Assert.Equal(HttpStatusCode.BadRequest, anon.StatusCode);

        var get = await client.GetAsync("/v1/research/settings?userId=tg-42");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var initial = await get.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.Equal("tg-42", initial!.UserId);
        Assert.False(initial.Enabled);

        var put = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-42",
            enabled = true,
            instagramHandle = "@babor_bryansk",
            cadenceDays = 14,
            timezone = "Europe/Moscow",
            notifyChatId = "42"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.Content.ReadFromJsonAsync<ResearchSettingsDto>(JsonOptions);
        Assert.True(saved!.Enabled);
        Assert.Equal("babor_bryansk", saved.InstagramHandle);
        Assert.Equal(14, saved.CadenceDays);
        Assert.Equal("Europe/Moscow", saved.Timezone);
        Assert.Equal("42", saved.NotifyChatId);
        Assert.NotNull(saved.NextRunAt);
    }

    [Fact]
    public async Task Settings_put_rejects_ig_token_like_handle()
    {
        await using var factory = CreateFactory();
        var client = Authed(factory);

        var put = await client.PutAsJsonAsync("/v1/research/settings", new
        {
            userId = "tg-7",
            instagramHandle = "IGQVJxxxxxxxxxxxxxxxxxxxxxxxx"
        });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Run_and_latest_work_without_token_as_noop()
    {
        await using var factory = CreateFactory();
        var client = Authed(factory);

        var run = await client.PostAsJsonAsync("/v1/research/run", new
        {
            userId = "tg-99",
            notifyChatId = "99"
        });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var body = await run.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.Equal(nameof(ResearchScheduleOutcome.NoOp), body!.Outcome);
        Assert.Contains("token", body.Message!, StringComparison.OrdinalIgnoreCase);

        var latest = await client.GetAsync("/v1/research/latest?userId=tg-99");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var latestBody = await latest.Content.ReadFromJsonAsync<ResearchLatestResponse>(JsonOptions);
        Assert.Equal("tg-99", latestBody!.Settings.UserId);
    }

    [Fact]
    public async Task Research_endpoints_require_service_key()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/research/settings?userId=tg-1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_notify_hook_posts_when_configured()
    {
        var handler = new CapturingHandler();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IResearchNotifyHook>();
            services.AddSingleton<IResearchNotifyHook>(sp =>
            {
                var http = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://gateway.test/")
                };
                http.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
                return new GatewayResearchNotifyHook(
                    http,
                    Microsoft.Extensions.Options.Options.Create(new AssistantApi.Options.GatewayNotifyOptions
                    {
                        BaseUrl = "http://gateway.test",
                        ServiceKey = ServiceKey
                    }),
                    sp.GetRequiredService<IResearchArtifactStore>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayResearchNotifyHook>.Instance);
            });
        });

        // Seed a plan so preview is attached.
        var artifacts = factory.Services.GetRequiredService<IResearchArtifactStore>();
        await artifacts.SavePlanAsync(new ResearchPlan
        {
            UserId = "tg-5",
            CreatedAt = DateTimeOffset.UtcNow,
            WindowStart = DateOnly.FromDateTime(DateTime.UtcNow),
            WindowEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(13)),
            Items =
            [
                new ResearchPlanItem
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    Caption = "Test caption",
                    Hashtags = ["#beauty"],
                    ImagePrompt = "soft light"
                }
            ]
        }, CancellationToken.None);

        var hook = factory.Services.GetRequiredService<IResearchNotifyHook>();
        Assert.IsType<GatewayResearchNotifyHook>(hook);

        await hook.OnResearchRunAsync(new ResearchNotifyEvent
        {
            UserId = "tg-5",
            NotifyChatId = "5",
            PeriodKey = "2026-08-17",
            Success = true,
            Message = "research-ok"
        }, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/internal/notify", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("Research OK", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("Test caption", handler.LastBody, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
        }
    }
}

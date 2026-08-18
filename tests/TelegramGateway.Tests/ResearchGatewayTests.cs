using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TelegramGateway.Contracts;
using TelegramGateway.Services;

namespace TelegramGateway.Tests;

public sealed class ResearchGatewayTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        IAssistantApiClient assistant,
        ITelegramBotClient? telegram = null,
        string? imageVolumePath = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Telegram:BotToken"] = "000000000:TESTTOKEN_FOR_UNIT_TESTS",
                    ["Telegram:UsePolling"] = "false",
                    ["Assistant:BaseUrl"] = "http://assistant-api:8080",
                    ["Assistant:ServiceKey"] = "test-service-key-32chars-min!"
                };
                if (!string.IsNullOrWhiteSpace(imageVolumePath))
                {
                    values["Research:ImageVolumePath"] = imageVolumePath;
                }

                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAssistantApiClient>();
                services.AddSingleton(assistant);
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(telegram ?? new CapturingTelegram());
            });
        });

    [Fact]
    public async Task MiniApp_research_settings_proxy_rejects_anonymous()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/miniapp/research/settings", new
        {
            userId = "mini-anonymous",
            enabled = true,
            instagramHandle = "@x"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(assistant.LastPut);
    }

    [Fact]
    public async Task MiniApp_research_settings_and_run_proxy_to_assistant()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/miniapp/research/settings", new
        {
            userId = "tg-42",
            enabled = true,
            instagramHandle = "@babor",
            cadenceDays = 14,
            timezone = "Europe/Moscow",
            notifyChatId = "42"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.NotNull(assistant.LastPut);
        Assert.Equal("tg-42", assistant.LastPut!.UserId);
        Assert.True(assistant.LastPut.Enabled);
        Assert.Equal("@babor", assistant.LastPut.InstagramHandle);

        var get = await client.GetAsync("/api/miniapp/research/settings?userId=tg-42");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var run = await client.PostAsJsonAsync("/api/miniapp/research/run", new
        {
            userId = "tg-42",
            notifyChatId = "42"
        });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        Assert.NotNull(assistant.LastRun);

        var latest = await client.GetAsync("/api/miniapp/research/latest?userId=tg-42");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
    }

    [Fact]
    public async Task MiniApp_research_rejects_ig_token_in_handle()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/miniapp/research/settings", new
        {
            userId = "tg-1",
            instagramHandle = "IGQVJxxxxxxxxxxxxxxxxxxxxxxxx"
        });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Null(assistant.LastPut);
    }

    [Fact]
    public async Task Internal_notify_requires_service_key_and_sends_telegram()
    {
        var telegram = new CapturingTelegram();
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant, telegram);
        var client = factory.CreateClient();

        var unauthorized = await client.PostAsJsonAsync("/internal/notify", new { chatId = 5L, text = "hi" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Post, "/internal/notify")
        {
            Content = JsonContent.Create(new { chatId = 5L, text = "Research OK" })
        };
        req.Headers.Add("X-Service-Key", "test-service-key-32chars-min!");
        var ok = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Single(telegram.Sent);
        Assert.Equal(5, telegram.Sent[0].ChatId);
        Assert.Equal("Research OK", telegram.Sent[0].Text);
    }

    [Fact]
    public async Task Internal_notify_sendPhoto_keeps_text_when_photos_missing()
    {
        var telegram = new CapturingTelegram();
        var volume = Path.Combine(Path.GetTempPath(), "gw-vol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(volume, "research-media", "r1", "out"));
        var photoPath = Path.Combine(volume, "research-media", "r1", "out", "day-01.png");
        await File.WriteAllBytesAsync(photoPath, [0x89, 0x50, 0x4E, 0x47]);

        await using var factory = CreateFactory(new FakeResearchAssistant(), telegram, volume);
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/internal/notify")
        {
            Content = JsonContent.Create(new
            {
                chatId = 9L,
                text = "Plan text must survive",
                photoPaths = new[] { "research-media/r1/out/day-01.png", "research-media/missing.png" }
            })
        };
        req.Headers.Add("X-Service-Key", "test-service-key-32chars-min!");
        var ok = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Single(telegram.Sent);
        Assert.Equal("Plan text must survive", telegram.Sent[0].Text);
        Assert.Single(telegram.Photos);
        Assert.Equal("day-01.png", telegram.Photos[0].FileName);

        try { Directory.Delete(volume, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Serves_research_panel_in_mini_app()
    {
        await using var factory = CreateFactory(new FakeResearchAssistant());
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("research-panel", html);
        Assert.Contains("Instagram Research", html);
        Assert.DoesNotContain("INSTAGRAM__ACCESSTOKEN", html);
        Assert.DoesNotContain("access_token", html);
    }

    [Fact]
    public async Task Bot_research_on_and_plan_commands()
    {
        var assistant = new FakeResearchAssistant();
        var telegram = new CapturingTelegram();
        var sut = new UpdateProcessingService(
            assistant,
            telegram,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateProcessingService>.Instance);

        await sut.ProcessAsync(new TelegramUpdate
        {
            UpdateId = 10,
            Message = new TelegramMessage
            {
                MessageId = 1,
                Text = "/research on",
                Chat = new TelegramChat { Id = 42 },
                From = new TelegramUser { Id = 7 }
            }
        }, CancellationToken.None);

        Assert.NotNull(assistant.LastPut);
        Assert.Equal("tg-7", assistant.LastPut!.UserId);
        Assert.True(assistant.LastPut.Enabled);
        Assert.Equal("42", assistant.LastPut.NotifyChatId);
        Assert.Contains(telegram.Sent, s => s.Text.Contains("включ", StringComparison.OrdinalIgnoreCase));

        telegram.Sent.Clear();
        await sut.ProcessAsync(new TelegramUpdate
        {
            UpdateId = 11,
            Message = new TelegramMessage
            {
                MessageId = 2,
                Text = "/research plan",
                Chat = new TelegramChat { Id = 42 },
                From = new TelegramUser { Id = 7 }
            }
        }, CancellationToken.None);
        Assert.True(assistant.LatestCalls > 0);
        Assert.Single(telegram.Sent);
    }

    private sealed class FakeResearchAssistant : IAssistantApiClient
    {
        public ResearchSettingsUpdateRequest? LastPut { get; private set; }
        public ResearchRunRequest? LastRun { get; private set; }
        public int LatestCalls { get; private set; }

        public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = "m",
                Text = "[stub] ok",
                Provider = "stub"
            });

        public Task<ResearchSettingsDto> GetResearchSettingsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchSettingsDto { UserId = userId, CadenceDays = 14 });

        public Task<ResearchSettingsDto> PutResearchSettingsAsync(
            ResearchSettingsUpdateRequest request,
            CancellationToken cancellationToken)
        {
            LastPut = request;
            return Task.FromResult(new ResearchSettingsDto
            {
                UserId = request.UserId,
                Enabled = request.Enabled ?? false,
                InstagramHandle = request.InstagramHandle?.TrimStart('@'),
                CadenceDays = request.CadenceDays ?? 14,
                Timezone = request.Timezone,
                NotifyChatId = request.NotifyChatId
            });
        }

        public Task<ResearchRunResponse> RunResearchAsync(ResearchRunRequest request, CancellationToken cancellationToken)
        {
            LastRun = request;
            return Task.FromResult(new ResearchRunResponse
            {
                Outcome = "NoOp",
                Message = "ok",
                Settings = new ResearchSettingsDto { UserId = request.UserId }
            });
        }

        public Task<ResearchLatestResponse> GetResearchLatestAsync(string userId, CancellationToken cancellationToken)
        {
            LatestCalls++;
            return Task.FromResult(new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto { UserId = userId },
                PlanPreview = "План preview"
            });
        }
    }

    private sealed class CapturingTelegram : ITelegramBotClient
    {
        public List<(long ChatId, string Text)> Sent { get; } = [];
        public List<(long ChatId, string FileName)> Photos { get; } = [];

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            Sent.Add((chatId, text));
            return Task.CompletedTask;
        }

        public Task SendPhotoAsync(long chatId, Stream photo, string fileName, string? caption, CancellationToken cancellationToken)
        {
            Photos.Add((chatId, fileName));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
    }
}

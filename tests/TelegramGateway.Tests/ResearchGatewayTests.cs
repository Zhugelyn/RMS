using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TelegramGateway.Contracts;
using TelegramGateway.Security;
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
    public async Task MiniApp_research_mutation_requires_initData()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/miniapp/research/settings", new
        {
            userId = "tg-42",
            enabled = true,
            instagramHandle = "@babor"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Null(assistant.LastPut);

        var run = await client.PostAsJsonAsync("/api/miniapp/research/run", new
        {
            userId = "tg-42"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, run.StatusCode);
        Assert.Null(assistant.LastRun);
    }

    [Fact]
    public async Task MiniApp_research_mutation_rejects_userId_mismatch()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(
            "000000000:TESTTOKEN_FOR_UNIT_TESTS", 42);

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/miniapp/research/settings");
        req.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
        req.Content = JsonContent.Create(new
        {
            userId = "tg-99",
            enabled = true,
            instagramHandle = "@x"
        });
        var put = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Null(assistant.LastPut);
    }

    [Fact]
    public async Task MiniApp_research_settings_and_run_proxy_to_assistant()
    {
        var assistant = new FakeResearchAssistant();
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(
            "000000000:TESTTOKEN_FOR_UNIT_TESTS", 42);

        using (var req = new HttpRequestMessage(HttpMethod.Put, "/api/miniapp/research/settings"))
        {
            req.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
            req.Content = JsonContent.Create(new
            {
                userId = "tg-42",
                enabled = true,
                instagramHandle = "@babor",
                cadenceDays = 14,
                timezone = "Europe/Moscow",
                notifyChatId = "42"
            });
            var put = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        Assert.NotNull(assistant.LastPut);
        Assert.Equal("tg-42", assistant.LastPut!.UserId);
        Assert.True(assistant.LastPut.Enabled);
        Assert.Equal("@babor", assistant.LastPut.InstagramHandle);

        var get = await client.GetAsync("/api/miniapp/research/settings?userId=tg-42");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/miniapp/research/run"))
        {
            req.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
            req.Content = JsonContent.Create(new
            {
                userId = "tg-42",
                notifyChatId = "42"
            });
            var run = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        }

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
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(
            "000000000:TESTTOKEN_FOR_UNIT_TESTS", 1);

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/miniapp/research/settings");
        req.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
        req.Content = JsonContent.Create(new
        {
            userId = "tg-1",
            instagramHandle = "IGQVJxxxxxxxxxxxxxxxxxxxxxxxx"
        });
        var put = await client.SendAsync(req);
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
    public async Task Serves_research_studio_markers_in_mini_app()
    {
        await using var factory = CreateFactory(new FakeResearchAssistant());
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("research-studio", html);
        Assert.Contains("data-studio=\"research\"", html);
        Assert.Contains("plan-gallery", html);
        Assert.Contains("analytics-grid", html);
        Assert.Contains("Research Studio", html);
        Assert.DoesNotContain("id=\"research-plan\"", html);
        Assert.DoesNotContain("INSTAGRAM__ACCESSTOKEN", html);
        Assert.DoesNotContain("access_token", html);
    }

    [Fact]
    public async Task Media_proxy_requires_initData_and_denies_traversal()
    {
        var volume = Path.Combine(Path.GetTempPath(), "media-vol-" + Guid.NewGuid().ToString("N"));
        var rel = Path.Combine("research-media", "r1", "out", "day-01.png");
        var abs = Path.Combine(volume, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        await File.WriteAllBytesAsync(abs, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);

        await using var factory = CreateFactory(new FakeResearchAssistant(), imageVolumePath: volume);
        var client = factory.CreateClient();

        var noAuth = await client.GetAsync(
            "/api/miniapp/research/media?path=" + Uri.EscapeDataString("research-media/r1/out/day-01.png"));
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);

        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(
            "000000000:TESTTOKEN_FOR_UNIT_TESTS", 42);

        using (var trav = new HttpRequestMessage(
                   HttpMethod.Get,
                   "/api/miniapp/research/media?path=" + Uri.EscapeDataString("../etc/passwd.png")))
        {
            trav.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
            var denied = await client.SendAsync(trav);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var ok = new HttpRequestMessage(
                   HttpMethod.Get,
                   "/api/miniapp/research/media?path=" + Uri.EscapeDataString("research-media/r1/out/day-01.png")))
        {
            ok.Headers.TryAddWithoutValidation("X-Telegram-Init-Data", init);
            var allowed = await client.SendAsync(ok);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal("image/png", allowed.Content.Headers.ContentType?.MediaType);
        }

        try { Directory.Delete(volume, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Latest_proxy_enriches_imageUrl_as_media_proxy()
    {
        var assistant = new FakeResearchAssistant
        {
            LatestOverride = new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto { UserId = "tg-42" },
                PlanPreview = "preview",
                Items =
                [
                    new ResearchPlanItemDto
                    {
                        Date = DateOnly.FromDateTime(DateTime.UtcNow),
                        Caption = "c",
                        ImagePrompt = "p",
                        MediaPath = "research-media/r1/out/day-01.png",
                        Status = "ready"
                    }
                ]
            }
        };
        await using var factory = CreateFactory(assistant);
        var client = factory.CreateClient();
        var latest = await client.GetAsync("/api/miniapp/research/latest?userId=tg-42");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var body = await latest.Content.ReadFromJsonAsync<ResearchLatestResponse>();
        Assert.NotNull(body);
        Assert.Single(body!.Items);
        Assert.StartsWith("/api/miniapp/research/media?path=", body.Items[0].ImageUrl);
        Assert.DoesNotContain("access_token", body.Items[0].ImageUrl!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bot_research_on_and_plan_commands()
    {
        var assistant = new FakeResearchAssistant();
        var telegram = new CapturingTelegram();
        var sut = new UpdateProcessingService(
            assistant,
            telegram,
            Microsoft.Extensions.Options.Options.Create(new TelegramGateway.Options.TelegramOptions
            {
                BotToken = "000000000:TESTTOKEN_FOR_UNIT_TESTS",
                WebAppUrl = "https://example.com/miniapp"
            }),
            Microsoft.Extensions.Options.Options.Create(new TelegramGateway.Options.ResearchImageOptions()),
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
        Assert.Contains(telegram.Sent, s => s.ReplyMarkup is not null);

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
        Assert.NotNull(telegram.Sent[0].ReplyMarkup);
    }

    [Fact]
    public async Task Bot_start_includes_web_app_keyboard_when_url_set()
    {
        var telegram = new CapturingTelegram();
        var sut = new UpdateProcessingService(
            new FakeResearchAssistant(),
            telegram,
            Microsoft.Extensions.Options.Options.Create(new TelegramGateway.Options.TelegramOptions
            {
                BotToken = "000000000:TESTTOKEN_FOR_UNIT_TESTS",
                WebAppUrl = "https://example.com/app"
            }),
            Microsoft.Extensions.Options.Options.Create(new TelegramGateway.Options.ResearchImageOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateProcessingService>.Instance);

        await sut.ProcessAsync(new TelegramUpdate
        {
            UpdateId = 1,
            Message = new TelegramMessage
            {
                MessageId = 1,
                Text = "/start",
                Chat = new TelegramChat { Id = 1 },
                From = new TelegramUser { Id = 1 }
            }
        }, CancellationToken.None);

        Assert.Single(telegram.Sent);
        Assert.NotNull(telegram.Sent[0].ReplyMarkup);
        var json = System.Text.Json.JsonSerializer.Serialize(telegram.Sent[0].ReplyMarkup);
        Assert.Contains("web_app", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Открыть студию", json);
    }

    [Fact]
    public void WebApp_keyboard_null_when_url_missing()
    {
        Assert.Null(TelegramWebAppKeyboard.TryCreate(null));
        Assert.Null(TelegramWebAppKeyboard.TryCreate(""));
        Assert.NotNull(TelegramWebAppKeyboard.TryCreate("https://example.com/x"));
    }

    private sealed class FakeResearchAssistant : IAssistantApiClient
    {
        public ResearchSettingsUpdateRequest? LastPut { get; private set; }
        public ResearchRunRequest? LastRun { get; private set; }
        public int LatestCalls { get; private set; }
        public ResearchLatestResponse? LatestOverride { get; set; }

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
            if (LatestOverride is not null)
            {
                return Task.FromResult(LatestOverride);
            }

            return Task.FromResult(new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto { UserId = userId },
                PlanPreview = "План preview"
            });
        }
    }

    private sealed class CapturingTelegram : ITelegramBotClient
    {
        public List<(long ChatId, string Text, object? ReplyMarkup)> Sent { get; } = [];
        public List<(long ChatId, string FileName)> Photos { get; } = [];
        public int MenuButtonCalls { get; private set; }

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken, object? replyMarkup = null)
        {
            Sent.Add((chatId, text, replyMarkup));
            return Task.CompletedTask;
        }

        public Task SendPhotoAsync(long chatId, Stream photo, string fileName, string? caption, CancellationToken cancellationToken)
        {
            Photos.Add((chatId, fileName));
            return Task.CompletedTask;
        }

        public Task SetChatMenuButtonWebAppAsync(string text, string url, CancellationToken cancellationToken)
        {
            MenuButtonCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
    }
}

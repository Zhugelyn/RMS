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

public sealed class MiniAppEndpointTests
{
    [Fact]
    public async Task MiniApp_chat_proxies_to_assistant_without_exposing_service_key()
    {
        var fakeAssistant = new CapturingAssistant();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
                    services.AddSingleton<IAssistantApiClient>(fakeAssistant);
                    services.RemoveAll<ITelegramBotClient>();
                    services.AddSingleton<ITelegramBotClient>(new NoopTelegram());
                });
            });

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/miniapp/chat", new
        {
            text = "прайс",
            intent = "marketing",
            conversationId = "mini-1",
            userId = "tg-1"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(fakeAssistant.LastRequest);
        Assert.Equal("marketing", fakeAssistant.LastRequest!.Intent);
        Assert.Equal("прайс", fakeAssistant.LastRequest.Text);

        var body = await response.Content.ReadFromJsonAsync<AssistantChatResponse>();
        Assert.Equal("stub", body!.Provider);
    }

    [Fact]
    public async Task Serves_mini_app_shell()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
                    services.RemoveAll<ITelegramBotClient>();
                    services.AddSingleton<ITelegramBotClient>(new NoopTelegram());
                });
            });

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("Telegram AI", html);
        Assert.Contains("data-intent=\"salon\"", html);
        Assert.Contains("data-intent=\"marketing\"", html);
        Assert.Contains("data-intent=\"tasks\"", html);
    }

    private sealed class CapturingAssistant : IAssistantApiClient
    {
        public AssistantChatRequest? LastRequest { get; private set; }

        public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AssistantChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = "m",
                Text = "[stub/marketing] ok",
                Provider = "stub"
            });
        }
    }

    private sealed class NoopTelegram : ITelegramBotClient
    {
        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
    }
}

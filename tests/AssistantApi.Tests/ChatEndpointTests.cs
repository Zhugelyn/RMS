using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantApi.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AssistantApi.Tests;

public sealed class ChatEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ServiceKey = "test-service-key-32chars-min!";
    private readonly WebApplicationFactory<Program> _factory;

    public ChatEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Assistant:ServiceKey"] = ServiceKey
                });
            });
        });
    }

    [Fact]
    public async Task Health_endpoints_are_public()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Chat_requires_service_key()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/chat", ValidRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Chat_returns_stub_response_without_cursor_key()
    {
        var client = CreateAuthedClient();
        var response = await client.PostAsJsonAsync("/v1/chat", ValidRequest(intent: ChatIntent.Salon));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(1, body!.SchemaVersion);
        Assert.Equal("stub", body.Provider);
        Assert.Contains("salon", body.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(body.MessageId));
        Assert.DoesNotContain("sk-", body.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_accepts_optional_agentId_for_resume_contract()
    {
        var client = CreateAuthedClient();
        var request = ValidRequest(intent: ChatIntent.Tasks);
        request.AgentId = "agent-resume-test";
        var response = await client.PostAsJsonAsync("/v1/chat", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("stub", body!.Provider);
        Assert.Equal("agent-resume-test", body.AgentId);
    }

    [Fact]
    public async Task Chat_rejects_cursor_key_in_text()
    {
        var client = CreateAuthedClient();
        var request = ValidRequest();
        request.Text = "here is CURSOR_API_KEY=sk-abcdefghijklmnopqrstuvwxyz";
        var response = await client.PostAsJsonAsync("/v1/chat", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateAuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);
        return client;
    }

    private static ChatRequest ValidRequest(ChatIntent? intent = null) => new()
    {
        SchemaVersion = 1,
        ConversationId = "c1",
        UserId = "u1",
        Text = "привет",
        TraceId = "t1",
        Intent = intent
    };
}

using Microsoft.Extensions.Logging.Abstractions;
using TelegramGateway.Contracts;
using TelegramGateway.Services;

namespace TelegramGateway.Tests;

public sealed class UpdateProcessingTests
{
    [Fact]
    public async Task Routes_message_to_assistant_and_replies()
    {
        var assistant = new FakeAssistant();
        var telegram = new FakeTelegram();
        var sut = new UpdateProcessingService(assistant, telegram, NullLogger<UpdateProcessingService>.Instance);

        await sut.ProcessAsync(new TelegramUpdate
        {
            UpdateId = 1,
            Message = new TelegramMessage
            {
                MessageId = 10,
                Text = "/salon записаться",
                Chat = new TelegramChat { Id = 42 },
                From = new TelegramUser { Id = 7 }
            }
        }, CancellationToken.None);

        Assert.NotNull(assistant.LastRequest);
        Assert.Equal("salon", assistant.LastRequest!.Intent);
        Assert.Equal("записаться", assistant.LastRequest.Text);
        Assert.Equal("tg-42", assistant.LastRequest.ConversationId);
        Assert.Single(telegram.Sent);
        Assert.Equal(42, telegram.Sent[0].ChatId);
        Assert.Contains("stub", telegram.Sent[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_secret_from_chat_without_calling_assistant()
    {
        var assistant = new FakeAssistant();
        var telegram = new FakeTelegram();
        var sut = new UpdateProcessingService(assistant, telegram, NullLogger<UpdateProcessingService>.Instance);

        await sut.ProcessAsync(new TelegramUpdate
        {
            UpdateId = 2,
            Message = new TelegramMessage
            {
                MessageId = 11,
                Text = "CURSOR_API_KEY=sk-should-not-pass",
                Chat = new TelegramChat { Id = 99 },
                From = new TelegramUser { Id = 1 }
            }
        }, CancellationToken.None);

        Assert.Null(assistant.LastRequest);
        Assert.Single(telegram.Sent);
        Assert.Contains("Не принимаю", telegram.Sent[0].Text);
    }

    private sealed class FakeAssistant : IAssistantApiClient
    {
        public AssistantChatRequest? LastRequest { get; private set; }

        public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AssistantChatResponse
            {
                SchemaVersion = 1,
                ConversationId = request.ConversationId,
                MessageId = "m1",
                Text = "[stub] ok",
                Provider = "stub"
            });
        }

        public Task<ResearchSettingsDto> GetResearchSettingsAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchSettingsDto { UserId = userId });

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
            Task.FromResult(new ResearchLatestResponse
            {
                Settings = new ResearchSettingsDto { UserId = userId }
            });
    }

    private sealed class FakeTelegram : ITelegramBotClient
    {
        public List<(long ChatId, string Text)> Sent { get; } = [];

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            Sent.Add((chatId, text));
            return Task.CompletedTask;
        }

        public Task SendPhotoAsync(long chatId, Stream photo, string fileName, string? caption, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
    }
}

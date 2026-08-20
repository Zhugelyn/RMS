using TelegramGateway.Security;

namespace TelegramGateway.Tests;

public sealed class SecretScannerTests
{
    [Theory]
    [InlineData("обычный текст", false)]
    [InlineData("CURSOR_API_KEY=abc", true)]
    [InlineData("my key sk-abcdefghijklmnopqrstuv", true)]
    [InlineData("api_key=secret", true)]
    [InlineData("INSTAGRAM__ACCESSTOKEN=xyz", true)]
    [InlineData("VK__SERVICETOKEN=xyz", true)]
    [InlineData("RAG__SERVICEKEY=xyz", true)]
    [InlineData("RAG_SERVICE_KEY=xyz", true)]
    [InlineData("ELASTICSEARCH__PASSWORD=secret", true)]
    [InlineData("ELASTIC_PASSWORD=secret", true)]
    [InlineData("MINIO__SECRETKEY=secret", true)]
    [InlineData("MINIO__ROOTPASSWORD=secret", true)]
    [InlineData("access_token=IGQVJxxxx", true)]
    [InlineData("service_token=vk-secret", true)]
    [InlineData("token EAAabcdefghijklmnopqrstuvwxyz012345", true)]
    public void Detects_forbidden_secrets(string text, bool expected)
    {
        Assert.Equal(expected, SecretScanner.ContainsForbiddenSecret(text));
    }
}

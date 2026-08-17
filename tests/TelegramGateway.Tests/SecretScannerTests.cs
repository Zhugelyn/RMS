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
    [InlineData("access_token=IGQVJxxxx", true)]
    [InlineData("token EAAabcdefghijklmnopqrstuvwxyz012345", true)]
    public void Detects_forbidden_secrets(string text, bool expected)
    {
        Assert.Equal(expected, SecretScanner.ContainsForbiddenSecret(text));
    }
}

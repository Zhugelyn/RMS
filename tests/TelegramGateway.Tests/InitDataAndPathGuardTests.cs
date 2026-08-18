using System.Net;
using System.Net.Http.Json;
using System.Text;
using TelegramGateway.Security;

namespace TelegramGateway.Tests;

public sealed class TelegramInitDataValidatorTests
{
    private const string BotToken = "000000000:TESTTOKEN_FOR_UNIT_TESTS";

    [Fact]
    public void Valid_initData_extracts_user()
    {
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(BotToken, 42);
        var result = TelegramInitDataValidator.Validate(init, BotToken);
        Assert.True(result.Ok);
        Assert.Equal(42, result.UserId);
    }

    [Fact]
    public void Tampered_hash_rejected()
    {
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(BotToken, 7);
        var bad = init.Replace("hash=", "hash=00", StringComparison.Ordinal);
        var result = TelegramInitDataValidator.Validate(bad, BotToken);
        Assert.False(result.Ok);
        Assert.Equal("hash-invalid", result.ErrorCode);
    }

    [Fact]
    public void Expired_auth_date_rejected()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        var init = TelegramInitDataValidator.BuildSignedInitDataForTests(BotToken, 1, old);
        var result = TelegramInitDataValidator.Validate(init, BotToken, maxAgeSeconds: 86_400);
        Assert.False(result.Ok);
        Assert.Equal("auth-date-expired", result.ErrorCode);
    }

    [Fact]
    public void Missing_initData_rejected()
    {
        var result = TelegramInitDataValidator.Validate(null, BotToken);
        Assert.False(result.Ok);
        Assert.Equal("init-data-missing", result.ErrorCode);
    }
}

public sealed class ResearchPhotoPathGuardTests
{
    [Fact]
    public void Rejects_path_traversal()
    {
        var volume = Path.Combine(Path.GetTempPath(), "rg-vol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(volume);
        try
        {
            Assert.False(ResearchPhotoPathGuard.TryResolve(
                volume, "../etc/passwd.png", 1024 * 1024, out _, out var err));
            Assert.Equal("path-traversal", err);

            Assert.False(ResearchPhotoPathGuard.TryResolve(
                volume, "research-media/../../escape.png", 1024 * 1024, out _, out err));
            Assert.Equal("path-traversal", err);
        }
        finally
        {
            Directory.Delete(volume, recursive: true);
        }
    }

    [Fact]
    public void Resolves_safe_relative_under_volume()
    {
        var volume = Path.Combine(Path.GetTempPath(), "rg-vol-" + Guid.NewGuid().ToString("N"));
        var rel = Path.Combine("research-media", "run1", "out", "day-01.png");
        var abs = Path.Combine(volume, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, Encoding.UTF8.GetBytes("png-bytes"));
        try
        {
            Assert.True(ResearchPhotoPathGuard.TryResolve(
                volume, "research-media/run1/out/day-01.png", 1024 * 1024, out var resolved, out var err));
            Assert.Null(err);
            Assert.Equal(Path.GetFullPath(abs), resolved);
        }
        finally
        {
            Directory.Delete(volume, recursive: true);
        }
    }
}

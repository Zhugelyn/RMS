using AssistantApi.Files;
using AssistantApi.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

/// <summary>
/// phase8-hardening: TTL clamps, scanning hook stub, private buckets,
/// token-rotation notes, non-goals (no Direct/Apify/public bucket/byte proxy).
/// </summary>
public sealed class Phase8HardeningTests
{
    private static FilePresignService CreateService(
        IFileObjectStore? store = null,
        IObjectStoragePresigner? presigner = null,
        MinioOptions? options = null,
        IFileContentScanner? scanner = null) =>
        new(
            store ?? new InMemoryFileObjectStore(),
            presigner ?? new FakePresigner(),
            scanner ?? new PassThroughFileContentScanner(NullLogger<PassThroughFileContentScanner>.Instance),
            MsOptions.Create(options ?? DefaultOptions()),
            NullLogger<FilePresignService>.Instance);

    private static MinioOptions DefaultOptions() => new()
    {
        Endpoint = "http://minio:9000",
        AccessKey = "minioadmin",
        SecretKey = "dev-only-minio-root-password",
        BucketSalon = "tg-ai-salon",
        BucketMarketing = "tg-ai-marketing",
        PresignTtlSeconds = 300,
        PendingTtlSeconds = 3600,
        MaxUploadBytes = 20 * 1024 * 1024
    };

    [Fact]
    public void Ttl_clamps_are_explicit_and_short()
    {
        Assert.Equal(30, FileLimits.MinPresignTtlSeconds);
        Assert.Equal(3600, FileLimits.MaxPresignTtlSeconds);
        Assert.Equal(300, FileLimits.DefaultPresignTtlSeconds);
        Assert.Equal(60, FileLimits.MinPendingTtlSeconds);
        Assert.Equal(86_400, FileLimits.MaxPendingTtlSeconds);
        Assert.Equal(3600, FileLimits.DefaultPendingTtlSeconds);

        Assert.Equal(30, FileLimits.ClampPresignTtlSeconds(1));
        Assert.Equal(3600, FileLimits.ClampPresignTtlSeconds(99_999));
        Assert.Equal(300, FileLimits.ClampPresignTtlSeconds(300));
        Assert.Equal(60, FileLimits.ClampPendingTtlSeconds(1));
        Assert.Equal(86_400, FileLimits.ClampPendingTtlSeconds(999_999));

        var opts = new MinioOptions();
        Assert.Equal(FileLimits.DefaultPresignTtlSeconds, opts.PresignTtlSeconds);
        Assert.Equal(FileLimits.DefaultPendingTtlSeconds, opts.PendingTtlSeconds);
    }

    [Fact]
    public async Task Presign_put_and_get_use_clamped_ttl()
    {
        var store = new InMemoryFileObjectStore();
        var presigner = new RecordingPresigner();
        var svc = CreateService(
            store,
            presigner,
            new MinioOptions
            {
                Endpoint = "http://minio:9000",
                AccessKey = "a",
                SecretKey = "password1",
                PresignTtlSeconds = 99_999,
                PendingTtlSeconds = 1,
                MaxUploadBytes = 20 * 1024 * 1024
            });

        var intent = await svc.CreateUploadIntentAsync(ValidIntent("salon"), CancellationToken.None);
        Assert.Equal(FileLimits.MaxPresignTtlSeconds, presigner.LastPutTtl);
        Assert.True(intent.PendingExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(FileLimits.MinPendingTtlSeconds + 5));

        await svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);

        await svc.CreateDownloadUrlAsync(intent.FileId, new FileDownloadUrlRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);
        Assert.Equal(FileLimits.MaxPresignTtlSeconds, presigner.LastGetTtl);
    }

    [Fact]
    public async Task Confirm_runs_scan_stub_to_active_without_byte_fetch()
    {
        var store = new InMemoryFileObjectStore();
        var scanner = new CountingPassThroughScanner();
        var svc = CreateService(store, scanner: scanner);

        var intent = await svc.CreateUploadIntentAsync(ValidIntent("marketing"), CancellationToken.None);
        var confirmed = await svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);

        Assert.Equal(FileStatuses.Active, confirmed.Status);
        Assert.Equal(1, scanner.Calls);
        Assert.False(scanner.FetchedBytes);

        // Idempotent re-confirm
        var again = await svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);
        Assert.Equal(FileStatuses.Active, again.Status);
        Assert.Equal(1, scanner.Calls);
    }

    [Fact]
    public async Task Rejecting_scanner_marks_rejected_and_blocks_download()
    {
        var store = new InMemoryFileObjectStore();
        var svc = CreateService(store, scanner: new RejectingScanner("malware-stub"));

        var intent = await svc.CreateUploadIntentAsync(ValidIntent("salon"), CancellationToken.None);
        await Assert.ThrowsAsync<FileForbiddenException>(() =>
            svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
            {
                SchemaVersion = 1,
                UserId = "tg-1001"
            }, CancellationToken.None));

        var meta = await store.GetAsync(intent.FileId, CancellationToken.None);
        Assert.Equal(FileStatuses.Rejected, meta!.Status);

        await Assert.ThrowsAsync<FileForbiddenException>(() =>
            svc.CreateDownloadUrlAsync(intent.FileId, new FileDownloadUrlRequest
            {
                SchemaVersion = 1,
                UserId = "tg-1001"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Scanner_exception_soft_fails_to_active()
    {
        var store = new InMemoryFileObjectStore();
        var svc = CreateService(store, scanner: new ThrowingScanner());

        var intent = await svc.CreateUploadIntentAsync(ValidIntent("salon"), CancellationToken.None);
        var confirmed = await svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);
        Assert.Equal(FileStatuses.Active, confirmed.Status);
    }

    [Fact]
    public void Status_model_includes_scanning()
    {
        Assert.Equal("scanning", FileStatuses.Scanning);
        Assert.Equal("pending", FileStatuses.Pending);
        Assert.Equal("uploaded", FileStatuses.Uploaded);
        Assert.Equal("active", FileStatuses.Active);
        Assert.Equal("rejected", FileStatuses.Rejected);
    }

    [Fact]
    public void Compose_private_buckets_no_public_acl()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));
        var init = ExtractServiceBlock(yaml, "minio-init");
        Assert.Contains("anonymous set none", init, StringComparison.Ordinal);
        Assert.DoesNotContain("anonymous set download", init, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anonymous set public", init, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anonymous set upload", init, StringComparison.OrdinalIgnoreCase);

        var minio = ExtractServiceBlock(yaml, "minio");
        Assert.DoesNotContain("ports:", minio, StringComparison.Ordinal);
        Assert.Contains("expose:", minio, StringComparison.Ordinal);
    }

    [Fact]
    public void Secret_scanner_and_assistant_reject_minio_markers()
    {
        var repoRoot = FindRepoRoot();
        var scanner = File.ReadAllText(Path.Combine(
            repoRoot, "src", "TelegramGateway", "Security", "SecretScanner.cs"));
        Assert.Contains("MINIO__SECRETKEY", scanner, StringComparison.Ordinal);
        Assert.Contains("MINIO__ROOTPASSWORD", scanner, StringComparison.Ordinal);
        Assert.Contains("MINIO_SECRET_KEY", scanner, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(
            repoRoot, "src", "AssistantApi", "Program.cs"));
        Assert.Contains("MINIO__SECRETKEY", program, StringComparison.Ordinal);
        Assert.Contains("MINIO__ROOTPASSWORD", program, StringComparison.Ordinal);
        Assert.Contains("MINIO_SECRET_KEY", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_has_minio_token_rotation_notes()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepoRoot(), "README.md"));
        Assert.Contains("Token rotation", readme, StringComparison.Ordinal);
        Assert.Contains("MINIO__ROOTPASSWORD", readme, StringComparison.Ordinal);
        Assert.Contains("MINIO__SECRETKEY", readme, StringComparison.Ordinal);
        Assert.Contains("MINIO__ACCESSKEY", readme, StringComparison.Ordinal);
        // Rotation steps present (not just placeholder).
        Assert.Contains("restart `minio`", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart `assistant-api`", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_goals_not_wired_phase8()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "AssistantApi"),
            Path.Combine(repoRoot, "src", "TelegramGateway"),
            Path.Combine(repoRoot, "src", "CursorSdkBridge"),
            Path.Combine(repoRoot, "src", "RagService")
        };

        var wiringPatterns = new[]
        {
            "using Apify",
            "api.apify.com",
            "new Apify",
            "Yandex.Direct",
            "api.direct.yandex",
            "yandex-direct"
        };

        var packagePatterns = new[]
        {
            "Apify",
            "Yandex.Direct"
        };

        var hits = new List<string>();

        foreach (var root in roots)
        {
            Assert.True(Directory.Exists(root), root);

            foreach (var csproj in Directory.EnumerateFiles(root, "*.csproj"))
            {
                var text = File.ReadAllText(csproj);
                foreach (var pkg in packagePatterns)
                {
                    if (text.Contains($"Include=\"{pkg}", StringComparison.OrdinalIgnoreCase)
                        || text.Contains($"Include=\"{pkg}.", StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add($"{Path.GetRelativePath(repoRoot, csproj)}: package {pkg}");
                    }
                }
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, i) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var pattern in wiringPatterns)
                    {
                        if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{Path.GetRelativePath(repoRoot, file)}:{i}: {pattern}");
                        }
                    }
                }
            }
        }

        var compose = File.ReadAllText(Path.Combine(repoRoot, "docker-compose.yml"));
        Assert.DoesNotContain("yandex-direct", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anonymous set public", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anonymous set download", compose, StringComparison.OrdinalIgnoreCase);

        // No byte-proxy upload endpoints on assistant-api.
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "Program.cs"));
        Assert.DoesNotContain("IFormFile", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost(\"/v1/files/upload\"", program, StringComparison.Ordinal);
        Assert.Contains("IFileContentScanner", program, StringComparison.Ordinal);
        Assert.Contains("PassThroughFileContentScanner", program, StringComparison.Ordinal);

        // Scanner stub must not call MinIO GetObject.
        var scannerPath = Path.Combine(repoRoot, "src", "AssistantApi", "Files", "IFileContentScanner.cs");
        var scannerText = File.ReadAllText(scannerPath);
        Assert.DoesNotContain("GetObject", scannerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PresignedGet", scannerText, StringComparison.OrdinalIgnoreCase);

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    private static FileUploadIntentRequest ValidIntent(string domain) => new()
    {
        SchemaVersion = 1,
        UserId = "tg-1001",
        Domain = domain,
        ContentType = "image/png",
        SizeBytes = 4096,
        OriginalFilename = "shot.png"
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    private static string ExtractServiceBlock(string yaml, string serviceName)
    {
        var marker = $"  {serviceName}:";
        var start = yaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"service '{serviceName}' not found");

        var from = start + marker.Length;
        var next = yaml.IndexOf("\n  ", from, StringComparison.Ordinal);
        while (next >= 0)
        {
            var lineStart = next + 1;
            var lineEnd = yaml.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = yaml.Length;
            }

            var line = yaml[lineStart..lineEnd];
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("   ", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith(':')
                && !line.TrimStart().StartsWith('#'))
            {
                break;
            }

            next = yaml.IndexOf("\n  ", lineEnd, StringComparison.Ordinal);
        }

        var rootVolumes = yaml.IndexOf("\nvolumes:", from, StringComparison.Ordinal);
        var end = yaml.Length;
        if (next >= 0)
        {
            end = Math.Min(end, next);
        }

        if (rootVolumes >= 0)
        {
            end = Math.Min(end, rootVolumes);
        }

        return yaml[start..end];
    }

    private sealed class FakePresigner : IObjectStoragePresigner
    {
        public bool IsAvailable => true;

        public Task<PresignResult> PresignPutAsync(
            string bucket,
            string objectKey,
            string contentType,
            int ttlSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PresignResult
            {
                Url = $"https://presign.test/put/{bucket}/{objectKey}?X-Amz-Expires={ttlSeconds}",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds),
                RequiredHeaders = new Dictionary<string, string> { ["Content-Type"] = contentType }
            });

        public Task<PresignResult> PresignGetAsync(
            string bucket,
            string objectKey,
            int ttlSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PresignResult
            {
                Url = $"https://presign.test/get/{bucket}/{objectKey}?X-Amz-Expires={ttlSeconds}",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
            });
    }

    private sealed class RecordingPresigner : IObjectStoragePresigner
    {
        public bool IsAvailable => true;
        public int LastPutTtl { get; private set; }
        public int LastGetTtl { get; private set; }

        public Task<PresignResult> PresignPutAsync(
            string bucket,
            string objectKey,
            string contentType,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            LastPutTtl = ttlSeconds;
            return Task.FromResult(new PresignResult
            {
                Url = $"https://presign.test/put/{bucket}/{objectKey}",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds),
                RequiredHeaders = new Dictionary<string, string> { ["Content-Type"] = contentType }
            });
        }

        public Task<PresignResult> PresignGetAsync(
            string bucket,
            string objectKey,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            LastGetTtl = ttlSeconds;
            return Task.FromResult(new PresignResult
            {
                Url = $"https://presign.test/get/{bucket}/{objectKey}",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
            });
        }
    }

    private sealed class CountingPassThroughScanner : IFileContentScanner
    {
        public int Calls { get; private set; }
        public bool FetchedBytes { get; private set; }

        public Task<FileScanResult> ScanAsync(FileObject file, CancellationToken cancellationToken)
        {
            Calls++;
            FetchedBytes = false; // stub never fetches
            return Task.FromResult(FileScanResult.Clean());
        }
    }

    private sealed class RejectingScanner : IFileContentScanner
    {
        private readonly string _reason;

        public RejectingScanner(string reason) => _reason = reason;

        public Task<FileScanResult> ScanAsync(FileObject file, CancellationToken cancellationToken) =>
            Task.FromResult(FileScanResult.Reject(_reason));
    }

    private sealed class ThrowingScanner : IFileContentScanner
    {
        public Task<FileScanResult> ScanAsync(FileObject file, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("scanner-down");
    }
}

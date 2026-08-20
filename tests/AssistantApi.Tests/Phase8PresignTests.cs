using AssistantApi.Files;
using AssistantApi.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AssistantApi.Tests;

/// <summary>phase8-presign: metadata + size/MIME + opaque keys + domain buckets; no byte proxy.</summary>
public sealed class Phase8PresignTests
{
    private static FilePresignService CreateService(
        IFileObjectStore? store = null,
        IObjectStoragePresigner? presigner = null,
        MinioOptions? options = null) =>
        new(
            store ?? new InMemoryFileObjectStore(),
            presigner ?? new FakePresigner(),
            MsOptions.Create(options ?? new MinioOptions
            {
                Endpoint = "http://minio:9000",
                AccessKey = "minioadmin",
                SecretKey = "dev-only-minio-root-password",
                BucketSalon = "tg-ai-salon",
                BucketMarketing = "tg-ai-marketing",
                PresignTtlSeconds = 300,
                PendingTtlSeconds = 3600,
                MaxUploadBytes = 20 * 1024 * 1024
            }),
            NullLogger<FilePresignService>.Instance);

    [Fact]
    public async Task Upload_intent_issues_presigned_put_and_opaque_key()
    {
        var store = new InMemoryFileObjectStore();
        var presigner = new FakePresigner();
        var svc = CreateService(store, presigner);

        var intent = await svc.CreateUploadIntentAsync(new FileUploadIntentRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001",
            Domain = "salon",
            ContentType = "image/jpeg",
            SizeBytes = 12_000,
            OriginalFilename = "../../evil.jpg"
        }, CancellationToken.None);

        Assert.Equal(1, intent.SchemaVersion);
        Assert.NotEqual(Guid.Empty, intent.FileId);
        Assert.Equal("salon", intent.Domain);
        Assert.Equal(FileStatuses.Pending, intent.Status);
        Assert.Equal("PUT", intent.HttpMethod);
        Assert.StartsWith("https://presign.test/put/", intent.UploadUrl, StringComparison.Ordinal);
        Assert.Contains("Content-Type", intent.RequiredHeaders.Keys);

        var meta = await store.GetAsync(intent.FileId, CancellationToken.None);
        Assert.NotNull(meta);
        Assert.Equal("tg-ai-salon", meta!.Bucket);
        Assert.Equal(intent.FileId.ToString("N"), meta.ObjectKey);
        Assert.DoesNotContain("evil", meta.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1001", meta.ObjectKey, StringComparison.Ordinal);
        Assert.Equal("____evil.jpg", meta.OriginalFilename);
        Assert.Equal("image/jpeg", meta.ContentType);
    }

    [Fact]
    public async Task Marketing_uses_separate_bucket_from_salon()
    {
        var store = new InMemoryFileObjectStore();
        var svc = CreateService(store);

        var salon = await svc.CreateUploadIntentAsync(ValidIntent("salon"), CancellationToken.None);
        var marketing = await svc.CreateUploadIntentAsync(ValidIntent("marketing"), CancellationToken.None);

        var s = await store.GetAsync(salon.FileId, CancellationToken.None);
        var m = await store.GetAsync(marketing.FileId, CancellationToken.None);
        Assert.Equal("tg-ai-salon", s!.Bucket);
        Assert.Equal("tg-ai-marketing", m!.Bucket);
        Assert.NotEqual(s.ObjectKey, m.ObjectKey);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("_router")]
    [InlineData("general")]
    public async Task Rejects_non_file_domains(string domain)
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<FileValidationException>(() =>
            svc.CreateUploadIntentAsync(ValidIntent(domain), CancellationToken.None));
    }

    [Theory]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    [InlineData("")]
    public async Task Rejects_disallowed_mime(string contentType)
    {
        var svc = CreateService();
        var req = ValidIntent("salon");
        req = new FileUploadIntentRequest
        {
            SchemaVersion = 1,
            UserId = req.UserId,
            Domain = req.Domain,
            ContentType = contentType,
            SizeBytes = req.SizeBytes
        };
        await Assert.ThrowsAsync<FileValidationException>(() =>
            svc.CreateUploadIntentAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_oversized_upload()
    {
        var svc = CreateService(options: new MinioOptions
        {
            Endpoint = "http://minio:9000",
            AccessKey = "a",
            SecretKey = "password1",
            MaxUploadBytes = 1024
        });
        var req = new FileUploadIntentRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1",
            Domain = "salon",
            ContentType = "image/png",
            SizeBytes = 2048
        };
        await Assert.ThrowsAsync<FileValidationException>(() =>
            svc.CreateUploadIntentAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_and_download_url_owner_only()
    {
        var store = new InMemoryFileObjectStore();
        var svc = CreateService(store);

        var intent = await svc.CreateUploadIntentAsync(ValidIntent("marketing"), CancellationToken.None);
        var confirmed = await svc.ConfirmUploadAsync(intent.FileId, new FileConfirmRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);

        Assert.Equal(FileStatuses.Active, confirmed.Status);
        Assert.NotNull(confirmed.UploadedAt);

        var download = await svc.CreateDownloadUrlAsync(intent.FileId, new FileDownloadUrlRequest
        {
            SchemaVersion = 1,
            UserId = "tg-1001"
        }, CancellationToken.None);
        Assert.StartsWith("https://presign.test/get/", download.DownloadUrl, StringComparison.Ordinal);

        await Assert.ThrowsAsync<FileObjectNotFoundException>(() =>
            svc.CreateDownloadUrlAsync(intent.FileId, new FileDownloadUrlRequest
            {
                SchemaVersion = 1,
                UserId = "tg-9999"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Unavailable_presigner_returns_storage_unavailable_not_chat_path()
    {
        var svc = CreateService(presigner: new UnavailableObjectStoragePresigner());
        await Assert.ThrowsAsync<FileStorageUnavailableException>(() =>
            svc.CreateUploadIntentAsync(ValidIntent("salon"), CancellationToken.None));
    }

    [Fact]
    public void Metadata_dto_does_not_expose_bucket_or_object_key()
    {
        var props = typeof(FileMetadataDto).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Bucket", props);
        Assert.DoesNotContain("ObjectKey", props);
        Assert.Contains("FileId", props);
        Assert.Contains("Status", props);
    }

    [Fact]
    public void Compose_assistant_api_wires_minio_env_and_depends()
    {
        var yaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.yml"));
        var api = ExtractServiceBlock(yaml, "assistant-api");
        Assert.Contains("Minio__Endpoint", api, StringComparison.Ordinal);
        Assert.Contains("Minio__AccessKey", api, StringComparison.Ordinal);
        Assert.Contains("Minio__SecretKey", api, StringComparison.Ordinal);
        Assert.Contains("Minio__BucketSalon", api, StringComparison.Ordinal);
        Assert.Contains("http://minio:9000", api, StringComparison.Ordinal);
        Assert.Contains("minio:", api, StringComparison.Ordinal);
        Assert.Contains("minio-init:", api, StringComparison.Ordinal);
        Assert.Contains("service_completed_successfully", api, StringComparison.Ordinal);

        // Gateway / bridge / rag still no MinIO wiring.
        foreach (var service in new[] { "telegram-gateway", "cursor-sdk-bridge", "rag-service" })
        {
            var block = ExtractServiceBlock(yaml, service);
            Assert.DoesNotContain("Minio__", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://minio", block, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Assistant_api_has_minio_package_but_no_byte_proxy_endpoints()
    {
        var repoRoot = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "AssistantApi.csproj"));
        Assert.Contains("Include=\"Minio\"", csproj, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "Program.cs"));
        Assert.Contains("/v1/files/upload-intent", program, StringComparison.Ordinal);
        Assert.Contains("/v1/files/{fileId:guid}/download-url", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost(\"/v1/files/upload\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IFormFile", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MultipartFormData", program, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("yandex-direct", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apify", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_objects_migration_and_entity_exist()
    {
        var repoRoot = FindRepoRoot();
        var migration = Path.Combine(repoRoot, "src", "AssistantApi", "Data", "Migrations", "20260819140000_AddFileObjects.cs");
        Assert.True(File.Exists(migration), migration);
        var text = File.ReadAllText(migration);
        Assert.Contains("file_objects", text, StringComparison.Ordinal);
        Assert.Contains("ux_file_objects_bucket_object", text, StringComparison.Ordinal);

        Assert.Contains(
            "FileObjects",
            File.ReadAllText(Path.Combine(repoRoot, "src", "AssistantApi", "Data", "AssistantDbContext.cs")),
            StringComparison.Ordinal);
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
}

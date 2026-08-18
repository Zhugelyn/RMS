using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagService.Contracts;
using RagService.Domain;
using RagService.Embedding;
using RagService.Indexing;
using RagService.Options;
using RagService.Security;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services
    .AddOptions<ServiceAuthOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ElasticsearchOptions>()
    .Bind(builder.Configuration.GetSection(ElasticsearchOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IEmbedder, StubEmbedder>();

var esUris = builder.Configuration.GetSection(ElasticsearchOptions.SectionName)["Uris"];
if (string.IsNullOrWhiteSpace(esUris))
{
    builder.Services.AddSingleton<IDocumentIndex, InMemoryDocumentIndex>();
}
else
{
    builder.Services.AddHttpClient<IDocumentIndex, ElasticsearchDocumentIndex>();
}

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

var app = builder.Build();

app.UseMiddleware<ServiceKeyAuthMiddleware>();

app.MapHealthChecks("/health/live");
app.MapGet("/health/ready", async (IDocumentIndex index, CancellationToken ct) =>
{
    try
    {
        await index.EnsureReadyAsync(ct);
        return Results.Ok(new { status = "Healthy", mode = index.Mode });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "Unhealthy", detail = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/v1/ingest", async Task<IResult> (
    [FromBody] IngestRequest request,
    IEmbedder embedder,
    IDocumentIndex index,
    CancellationToken ct) =>
{
    var validation = Validate(request);
    if (validation is not null)
    {
        return validation;
    }

    if (!IsAllowedDomain(request.Domain))
    {
        return Results.Json(Problem("domain must be salon|marketing; cross-domain / tasks / router rejected."),
            statusCode: StatusCodes.Status400BadRequest);
    }

    var indexName = RagIndexNames.For(request.Domain);
    var embedding = embedder.Embed($"{request.Title}\n{request.Text}");
    var doc = new IndexedDocument(
        DocumentId: request.DocumentId.Trim(),
        Domain: request.Domain,
        Index: indexName,
        Title: string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
        Text: request.Text,
        Embedding: embedding,
        Metadata: SanitizeMetadata(request.Metadata));

    await index.UpsertAsync(doc, ct);

    return Results.Ok(new IngestResponse
    {
        SchemaVersion = 1,
        DocumentId = doc.DocumentId,
        Domain = request.Domain.ToString().ToLowerInvariant(),
        Index = indexName,
        Status = "upserted"
    });
});

app.MapPost("/v1/search", async Task<IResult> (
    [FromBody] SearchRequest request,
    IEmbedder embedder,
    IDocumentIndex index,
    CancellationToken ct) =>
{
    var validation = Validate(request);
    if (validation is not null)
    {
        return validation;
    }

    if (!IsAllowedDomain(request.Domain))
    {
        return Results.Json(Problem("domain must be salon|marketing; cross-domain search rejected."),
            statusCode: StatusCodes.Status400BadRequest);
    }

    var indexName = RagIndexNames.For(request.Domain);
    var queryEmbedding = embedder.Embed(request.Query);
    var hits = await index.SearchAsync(
        request.Domain,
        indexName,
        queryEmbedding,
        request.Query,
        request.TopK <= 0 ? 5 : request.TopK,
        ct);

    return Results.Ok(new SearchResponse
    {
        SchemaVersion = 1,
        Domain = request.Domain.ToString().ToLowerInvariant(),
        Index = indexName,
        Hits = hits
    });
});

app.Run();

static bool IsAllowedDomain(RagDomain domain) =>
    domain is RagDomain.Salon or RagDomain.Marketing;

static IResult? Validate(object request)
{
    var ctx = new ValidationContext(request);
    var results = new List<ValidationResult>();
    if (Validator.TryValidateObject(request, ctx, results, validateAllProperties: true))
    {
        return null;
    }

    return Results.ValidationProblem(results
        .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
        .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? "invalid").ToArray()));
}

static Dictionary<string, string>? SanitizeMetadata(Dictionary<string, string>? metadata)
{
    if (metadata is null || metadata.Count == 0)
    {
        return null;
    }

    // Cap keys/values; drop suspicious secret-looking keys.
    var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (key, value) in metadata.Take(20))
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || value is null || value.Length > 512)
        {
            continue;
        }

        if (key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api_key", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        clean[key] = value;
    }

    return clean.Count == 0 ? null : clean;
}

static object Problem(string detail) => new
{
    type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
    title = "Bad Request",
    status = 400,
    detail
};

public partial class Program;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using TercihAI.Backend.Data;
using TercihAI.Backend.Models;
using TercihAI.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<YokAtlasService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
    client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Safari/605.1.15");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

var app = builder.Build();

app.UseCors();

var frontendRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, ".."));

if (File.Exists(Path.Combine(frontendRoot, "index.html")))
{
    var fileProvider = new PhysicalFileProvider(frontendRoot);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider
    });
}

app.MapGet("/api/meta", () =>
{
    var response = new MetadataResponse
    {
        Cities = AppMetadata.Cities,
        ScoreTypes = AppMetadata.ScoreTypes,
        UniversityTypes = AppMetadata.UniversityTypes,
        ForecastMethod = "2026 tahminleri, resmi 2024 ve 2025 verilerinin ortalaması alınarak üretilir."
    };

    return Results.Ok(response);
});

app.MapGet(
    "/api/programs/search",
    async (
        [FromQuery] string? query,
        [FromQuery] string? scoreType,
        [FromQuery] string? city,
        [FromQuery] string? university,
        [FromQuery] string? program,
        [FromQuery] string? universityType,
        [FromQuery] string? programCode,
        [FromQuery] int? minRank,
        [FromQuery] int? maxRank,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? includeNewPrograms,
        YokAtlasService yokAtlasService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var searchQuery = new ProgramSearchQuery
            {
                Query = query,
                ScoreType = scoreType,
                City = city,
                University = university,
                Program = program,
                UniversityType = universityType,
                ProgramCode = programCode,
                MinRank = minRank,
                MaxRank = maxRank,
                Page = page ?? 1,
                PageSize = pageSize ?? 24,
                IncludeNewPrograms = includeNewPrograms ?? true
            };

            var result = await yokAtlasService.SearchProgramsAsync(searchQuery, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.Problem(
                detail: exception.Message,
                title: "Program verisi alınamadı",
                statusCode: StatusCodes.Status502BadGateway);
        }
    });

app.MapGet(
    "/api/programs/{yopCode}",
    async (
        string yopCode,
        [FromQuery] string? scoreType,
        YokAtlasService yokAtlasService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var result = await yokAtlasService.GetProgramByCodeAsync(
                yopCode,
                scoreType,
                cancellationToken);

            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception exception)
        {
            return Results.Problem(
                detail: exception.Message,
                title: "Program detayı alınamadı",
                statusCode: StatusCodes.Status502BadGateway);
        }
    });

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        timestamp = DateTimeOffset.UtcNow
    });
});

app.Run();

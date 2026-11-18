using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Sahada.Api.Common;
using Sahada.Api.Features.Auth;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.Customers;
using Sahada.Api.Features.Demo;
using Sahada.Api.Features.Dispatch;
using Sahada.Api.Features.Parts;
using Sahada.Api.Features.Sync;
using Sahada.Api.Features.WorkOrders;
using Sahada.Api.Logging;
using Sahada.Infrastructure;
using Sahada.Infrastructure.Persistence;
using Sahada.Infrastructure.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = ConnectionStrings.FromUrl(builder.Configuration.GetConnectionString("Sahada"));
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Sahada is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Sahada environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSahadaPersistence(connectionString);
builder.Services.AddSahadaAuth(builder.Configuration);
builder.Services.AddSahadaStorage(builder.Configuration, connectionString);
builder.Services.AddScoped<SyncService>();

// The public demo's company; off unless Demo:Enabled.
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
var demo = builder.Configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
if (demo.Enabled)
{
    builder.Services.AddHttpClient(DemoWorker.HttpClientName, client => client.BaseAddress = new Uri(demo.BaseUrl));
    builder.Services.AddHostedService<DemoWorker>();
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-up and sign-in per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    // The demo's own people sign in from inside the container and are not held to it.
    options.AddPolicy(AuthEndpoints.RateLimit, context => demo.Enabled && context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
        ? RateLimitPartition.GetNoLimiter("demo")
        : RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
            title: "Too many requests",
            detail: "Slow down and try again in a minute.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = Sahada.Core.ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SahadaDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// One container for the public demo: the built web app is copied into wwwroot and served from here, with the same
// headers nginx sets in Compose. Without a wwwroot (tests, Compose), nothing changes.
var indexPage = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var serveWeb = File.Exists(indexPage);
app.UseMiddleware<CorrelationIdMiddleware>();
if (serveWeb)
{
    app.UseMiddleware<WebHeadersMiddleware>();
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    });
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Sahada")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());

if (!serveWeb)
{
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapDemoEndpoints();
app.MapAuthEndpoints();
app.MapCompanyEndpoints();
app.MapMemberEndpoints();
app.MapCustomerEndpoints();
app.MapPartEndpoints();
app.MapWorkOrderEndpoints();
app.MapTechnicianEndpoints();
app.MapBoardEndpoints();
app.MapSyncEndpoints();
app.MapPhotoEndpoints();
app.MapReportEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
if (serveWeb)
{
    // Every other path is a page of the web app; unknown API paths stay 404s.
    app.MapFallback(async context =>
    {
        if (WebHeadersMiddleware.IsApi(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.SendFileAsync(indexPage, context.RequestAborted);
    });
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DependencyInjection.MigrateAsync(app.Services);
}

await StorageSetup.InitializeAsync(app.Services);

await app.RunAsync();

public partial class Program;

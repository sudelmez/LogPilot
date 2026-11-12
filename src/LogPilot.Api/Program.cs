using System.Text.Json.Serialization;
using LogPilot.AI;
using LogPilot.Api;
using LogPilot.Api.Endpoints;
using LogPilot.Application;
using LogPilot.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Yapılandırılmış (JSON) loglar: Logging:Json=true (Docker'da açık). Geliştirmede okunur konsol logu.
if (builder.Configuration.GetValue<bool>("Logging:Json"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMongoAccessVerification();   // AI okuyucu hesabı gerçekten salt-okunur mu?
builder.Services.AddAi(builder.Configuration);
builder.Services.AddOptions<IncidentOptions>().Bind(builder.Configuration.GetSection(IncidentOptions.SectionName));
builder.Services.AddHostedService<IncidentDetectorWorker>();
builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"]);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // en dışta: istisna işleyici dahil her şeyin süresini/durumunu görür
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();            // /openapi/v1.json
    app.MapScalarApiReference(); // /scalar
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync });
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });

app.MapLogEndpoints();
app.MapAnalyticsEndpoints();
app.MapScenarioEndpoints();
app.MapIncidentEndpoints();
app.MapChatEndpoints();
app.MapKnowledgeEndpoints();

app.Run();

public partial class Program;   // WebApplicationFactory için

using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BattleShip.API.Endpoints;
using BattleShip.API.Services;
using BattleShip.API.Validation;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddValidatorsFromAssemblyContaining<CreateGameRequestValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<GameStoreOptions>().BindConfiguration("Games")
    .Validate(options => options.Capacity > 0 && options.LifetimeMinutes is > 0 and <= 1440,
        "Games doit définir une capacité positive et une durée comprise entre 1 et 1440 minutes.")
    .ValidateOnStart();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddCors(options => options.AddPolicy("Blazor", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithMethods("GET", "POST")
    .AllowAnyHeader()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("Blazor");
app.UseRateLimiter();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapGameEndpoints();
app.Run();

public partial class Program { }

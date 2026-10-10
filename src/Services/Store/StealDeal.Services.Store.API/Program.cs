using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StealDeal.Services.Store.API.Middlewares;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.EventHandlers;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Application.Services;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Infrastructure.BackgroundServices;
using StealDeal.Services.Store.Infrastructure.Configuration;
using StealDeal.Services.Store.Infrastructure.Messaging;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;
using StealDeal.Services.Store.Infrastructure.Services;
using StealDeal.Services.Store.Infrastructure.Storage;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StoreDb")));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<OutboxSettings>(builder.Configuration.GetSection("Outbox"));

builder.Services.Configure<S3Settings>(builder.Configuration.GetSection("Aws"));
builder.Services.Configure<GoongSettings>(builder.Configuration.GetSection("Goong"));
builder.Services.Configure<GeoapifySettings>(builder.Configuration.GetSection("Geoapify"));
builder.Services.AddMemoryCache();

// ── Repositories ──────────────────────────────────────────
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategorySuggestionRepository, CategorySuggestionRepository>();
builder.Services.AddScoped<IStoreProfileRepository, StoreProfileRepository>();
builder.Services.AddScoped<ISurpriseBagRepository, SurpriseBagRepository>();
builder.Services.AddScoped<IStoreReviewRepository, StoreReviewRepository>();
builder.Services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
builder.Services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();


// ── Application Services ───────────────────────────────────
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICategorySuggestionService, CategorySuggestionService>();
builder.Services.AddScoped<IStoreProfileService, StoreProfileService>();
builder.Services.AddScoped<ISurpriseBagService, SurpriseBagService>();
builder.Services.AddScoped<IStoreReviewService, StoreReviewService>();
builder.Services.AddScoped<IIntegrationEventHandler<CreateOrderEvent>, CreateOrderEventHandler>();
builder.Services.AddScoped<
    IIntegrationEventHandler<InventoryReleaseRequestedEvent>,
    InventoryReleaseRequestedEventHandler>();

builder.Services.Configure<OrderCreatedConsummerSettings>(
    builder.Configuration.GetSection("OrderCreatedConsumer"));
builder.Services.Configure<InventoryReleaseRequestedConsumerSettings>(
    builder.Configuration.GetSection("InventoryReleaseRequestedConsumer"));

builder.Services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
builder.Services.AddHostedService<CreatedOrderConsumer>();
builder.Services.AddHostedService<InventoryReleaseRequestedConsumer>();
builder.Services.AddHostedService<OutboxMessageProcessor>();

builder.Services.AddScoped<IS3StorageService, S3StorageService>();

// ── Authentication / JWT ──────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var secret = jwtSection["Secret"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded. Please wait a moment and try again.",
            Instance = context.HttpContext.Request.Path
        };
        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
    };

    options.AddPolicy("LocationPolicy", httpContext =>
    {
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.User.FindFirstValue("sub")
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("GoongLocationPolicy", httpContext =>
    {
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.User.FindFirstValue("sub")
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── Controllers & OpenAPI ─────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StealDeal Order API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        Description = "Enter a valid JWT access token."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, "JWT")] = []
    });
});

builder.Services.AddHttpClient<IOrderVerificationService, OrderVerificationService>(client =>
{
    var baseUrl = builder.Configuration["Services:OrderServiceUrl"] ?? "http://localhost:5165";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<IGoongLocationService, GoongLocationService>(client =>
{
    var baseUrl = builder.Configuration["Goong:BaseUrl"] ?? "https://rsapi.goong.io";
    var timeoutSeconds = int.TryParse(builder.Configuration["Goong:TimeoutSeconds"], out var parsedTimeout)
        ? parsedTimeout
        : 10;

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

builder.Services.AddHttpClient<GeoapifyLocationService>(client =>
{
    var baseUrl = builder.Configuration["Geoapify:BaseUrl"] ?? "https://api.geoapify.com";
    var timeoutSeconds = int.TryParse(builder.Configuration["Geoapify:TimeoutSeconds"], out var parsedTimeout)
        ? parsedTimeout
        : 10;

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

var locationProvider = builder.Configuration["LocationProvider"] ?? "Geoapify";
if (string.Equals(locationProvider, "Goong", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ILocationService>(sp => sp.GetRequiredService<IGoongLocationService>());
}
else
{
    builder.Services.AddScoped<ILocationService, GeoapifyLocationService>();
}

// ─────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            swaggerDoc.Servers = [new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }];
        });
    });
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StoreDb")));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<OutboxSettings>(builder.Configuration.GetSection("Outbox"));

// ── Repositories ──────────────────────────────────────────
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IStoreProfileRepository, StoreProfileRepository>();
builder.Services.AddScoped<ISurpriseBagRepository, SurpriseBagRepository>();
builder.Services.AddScoped<IStoreReviewRepository, StoreReviewRepository>();
builder.Services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
builder.Services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();


// ── Application Services ───────────────────────────────────
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IStoreProfileService, StoreProfileService>();
builder.Services.AddScoped<ISurpriseBagService, SurpriseBagService>();
builder.Services.AddScoped<IStoreReviewService, StoreReviewService>();
builder.Services.AddScoped<IIntegrationEventHandler<CreateOrderEvent>, CreateOrderEventHandler>();

builder.Services.Configure<OrderCreatedConsummerSettings>(
    builder.Configuration.GetSection("OrderCreatedConsumer"));

builder.Services.AddHostedService<CreatedOrderConsumer>();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

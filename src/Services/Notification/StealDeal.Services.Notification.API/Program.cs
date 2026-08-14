using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StealDeal.Services.Notification.API.Middlewares;
using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Application.EventHandlers;
using StealDeal.Services.Notification.Application.Messaging;
using StealDeal.Services.Notification.Application.Services;
using StealDeal.Services.Notification.Application.Services.Interfaces;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Infrastructure.BackgroundServices;
using StealDeal.Services.Notification.Infrastructure.Configuration;
using StealDeal.Services.Notification.Infrastructure.Persistence;
using StealDeal.Services.Notification.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NotificationDb")));

// ── Repositories ──────────────────────────────────────────
builder.Services.AddScoped<INotificationProfileRepository, NotificationProfileRepository>();
builder.Services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Application Services ───────────────────────────────────
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IIntegrationEventHandler<SendEmailVerificationOtpEvent>, SendEmailVerificationOtpEventHandler>();

// ── RabbitMQ & Consumers ────────────────────────────────────
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<EmailVerificationConsumerSettings>(builder.Configuration.GetSection("EmailVerificationConsumer"));
builder.Services.AddHostedService<EmailVerificationConsumer>();

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
        Title = "StealDeal Notification API",
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

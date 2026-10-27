using Microsoft.Extensions.Options;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Infrastructure.Services
{
    public class GeminiClient : IGeminiClient
    {
        private readonly HttpClient _httpClient;
        private readonly GeminiSettings _settings;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GeminiClient(HttpClient httpClient, IOptions<GeminiSettings> settings)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
        }

        public async Task<string> GenerateReplyAsync(
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            string? systemInstruction = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                throw new BadRequestException("Gemini API key is not configured. Set 'Gemini:ApiKey' in user secrets or configuration.");
            }

            var instruction = string.IsNullOrWhiteSpace(systemInstruction)
                ? _settings.SystemInstruction
                : systemInstruction;

            var contents = new List<object>();

            // Append historical turns (ensure roles match "user" and "model")
            foreach (var turn in history)
            {
                var role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : turn.Role.ToLowerInvariant();
                contents.Add(new
                {
                    role,
                    parts = new[] { new { text = turn.Content } }
                });
            }

            // Append current user message
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userMessage } }
            });

            var requestBody = new
            {
                system_instruction = string.IsNullOrWhiteSpace(instruction)
                    ? null
                    : new { parts = new[] { new { text = instruction } } },
                contents,
                generationConfig = new
                {
                    temperature = _settings.Temperature,
                    maxOutputTokens = _settings.MaxOutputTokens
                }
            };

            var endpoint = $"models/{_settings.Model}:generateContent?key={Uri.EscapeDataString(_settings.ApiKey)}";

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(endpoint, requestBody, JsonOptions, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BadRequestException("Failed to reach Google AI Studio service. Please try again later.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new BadRequestException($"AI service returned error ({response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);

            if (responseJson.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                return textElement.GetString() ?? string.Empty;
            }

            return "I apologize, but I could not generate a response. Please try again.";
        }
    }
}

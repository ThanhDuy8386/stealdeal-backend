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
            IEnumerable<object>? tools = null,
            Func<string, JsonElement, Task<object>>? toolHandler = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                throw new BadRequestException("Gemini API key is not configured. Set 'Gemini:ApiKey' in user secrets or configuration.");
            }

            var instruction = string.IsNullOrWhiteSpace(_settings.SystemInstruction)
                ? (systemInstruction ?? string.Empty)
                : string.IsNullOrWhiteSpace(systemInstruction)
                    ? _settings.SystemInstruction
                    : $"{_settings.SystemInstruction}\n\nContext:\n{systemInstruction}";

            var contents = new List<object>();

            // Append historical turns (ensure roles match "user" and "model")
            foreach (var turn in history)
            {
                var role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : turn.Role.ToLowerInvariant();
                contents.Add(new
                {
                    role,
                    parts = new object[] { new { text = turn.Content } }
                });
            }

            // Append current user message
            contents.Add(new
            {
                role = "user",
                parts = new object[] { new { text = userMessage } }
            });

            var endpoint = $"models/{_settings.Model}:generateContent?key={Uri.EscapeDataString(_settings.ApiKey)}";

            var responseJson = await SendGeminiRequestAsync(endpoint, instruction, contents, tools, cancellationToken);

            if (TryExtractFunctionCall(responseJson, out var functionName, out var functionArgs, out var modelContent) && toolHandler != null)
            {
                var toolResult = await toolHandler(functionName, functionArgs);

                // Preserve Gemini's authentic model turn (including thought_signature for Gemini 3.5 reasoning)
                contents.Add(modelContent);

                var responsePayload = toolResult is System.Collections.IEnumerable and not string and not IDictionary<string, object>
                    ? new { result = toolResult }
                    : toolResult;

                contents.Add(new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            functionResponse = new
                            {
                                name = functionName,
                                response = responsePayload
                            }
                        }
                    }
                });

                // Post second request to get the synthesized reply
                var secondResponseJson = await SendGeminiRequestAsync(endpoint, instruction, contents, tools, cancellationToken);
                if (TryExtractText(secondResponseJson, out var secondText))
                {
                    return secondText;
                }
            }

            if (TryExtractText(responseJson, out var replyText))
            {
                return replyText;
            }

            return "I apologize, but I could not generate a response. Please try again.";
        }

        private async Task<JsonElement> SendGeminiRequestAsync(
            string endpoint,
            string? systemInstruction,
            List<object> contents,
            IEnumerable<object>? tools,
            CancellationToken cancellationToken)
        {
            var requestBody = new Dictionary<string, object?>
            {
                ["contents"] = contents,
                ["generationConfig"] = new
                {
                    temperature = _settings.Temperature,
                    maxOutputTokens = _settings.MaxOutputTokens
                }
            };

            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                requestBody["system_instruction"] = new { parts = new[] { new { text = systemInstruction } } };
            }

            if (tools != null)
            {
                requestBody["tools"] = tools;
            }

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

            return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        }

        private static bool TryExtractFunctionCall(JsonElement root, out string name, out JsonElement args, out JsonElement modelContent)
        {
            name = string.Empty;
            args = default;
            modelContent = default;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out var fc) &&
                        fc.TryGetProperty("name", out var nameElement))
                    {
                        name = nameElement.GetString() ?? string.Empty;
                        args = fc.TryGetProperty("args", out var a) ? a : default;
                        modelContent = content;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryExtractText(JsonElement root, out string text)
        {
            text = string.Empty;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElem))
                    {
                        text = textElem.GetString() ?? string.Empty;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

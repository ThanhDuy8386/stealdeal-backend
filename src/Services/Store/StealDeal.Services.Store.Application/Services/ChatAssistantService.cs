using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services
{
    public class ChatAssistantService : IChatAssistantService
    {
        private readonly IChatSessionStore _sessionStore;
        private readonly IGeminiClient _geminiClient;
        private readonly IStoreProfileService _storeService;
        private readonly ISurpriseBagService _bagService;
        private readonly ICategoryRepository _categoryRepository;

        public ChatAssistantService(
            IChatSessionStore sessionStore,
            IGeminiClient geminiClient,
            IStoreProfileService storeService,
            ISurpriseBagService bagService,
            ICategoryRepository categoryRepository)
        {
            _sessionStore = sessionStore;
            _geminiClient = geminiClient;
            _storeService = storeService;
            _bagService = bagService;
            _categoryRepository = categoryRepository;
        }

        public async Task<ChatResponse> SendMessageAsync(Guid userId, ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                throw new BadRequestException("Message cannot be empty.");
            }

            var trimmedMessage = request.Message.Trim();
            if (trimmedMessage.Length > 500)
            {
                throw new BadRequestException("Message must not exceed 500 characters.");
            }

            var sessionId = userId.ToString();
            var history = await _sessionStore.GetRecentTurnsAsync(sessionId, cancellationToken);

            // Dynamically load active categories to inform Gemini
            var categories = (await _categoryRepository.GetAllAsync())
                .Where(c => c.IsActive)
                .Select(c => c.Name)
                .ToList();
            var categoryListStr = categories.Count > 0 ? string.Join(", ", categories) : "bakery, meals, groceries, vegan, dessert";

            var tools = BuildToolDeclarations(categoryListStr);

            var dynamicContext =
                $"Available platform categories: {categoryListStr}.\n" +
                "Rules:\n" +
                "- When searching for stores or bags, call the appropriate search tool.\n" +
                "- Treat general meal or food requests (e.g. 'what should I eat today?') as deal discovery and call the appropriate search tool.\n" +
                "- If a search tool returns 'location_required', politely ask the user to turn on GPS / enable location permissions or update their default address in their user profile before you can search.\n" +
                "- If a search tool returns 'no_results_found', politely let the user know and suggest widening the distance or trying another category or food item.\n" +
                "- Highlight prices, savings, distance, and pickup hours when presenting bags.";

            var suggestedBags = new List<SurpriseBagResponse>();
            var suggestedStores = new List<StoreProfileResponse>();

            Func<string, JsonElement, Task<object>> toolHandler = async (toolName, args) =>
            {
                if (!request.Latitude.HasValue || !request.Longitude.HasValue)
                {
                    return new
                    {
                        status = "location_required",
                        message = "User location coordinates are not available. Please kindly ask the user to turn on GPS / enable location permissions on their device or update their default address in their user profile."
                    };
                }

                if (string.Equals(toolName, "search_nearby_stores", StringComparison.OrdinalIgnoreCase))
                {
                    var nearbyReq = new NearbyQueryRequest
                    {
                        Latitude = request.Latitude,
                        Longitude = request.Longitude,
                        Query = GetStringArg(args, "query"),
                        Category = GetStringArg(args, "category"),
                        SortBy = GetStringArg(args, "sortBy"),
                        Limit = 5
                    };

                    var stores = await _storeService.GetNearbyAsync(nearbyReq);
                    suggestedStores.AddRange(stores);

                    if (stores.Count == 0)
                    {
                        return new
                        {
                            status = "no_results_found",
                            message = "No matching stores found within the search area with the requested filters."
                        };
                    }

                    return stores.Select(s => new
                    {
                        name = s.Name,
                        address = s.Address,
                        distanceKm = s.DistanceKm.HasValue ? Math.Round(s.DistanceKm.Value, 1) : (double?)null,
                        rating = s.RatingScore,
                        reviewCount = s.ReviewCount
                    }).ToList();
                }

                if (string.Equals(toolName, "search_nearby_bags", StringComparison.OrdinalIgnoreCase))
                {
                    var nearbyReq = new NearbyQueryRequest
                    {
                        Latitude = request.Latitude,
                        Longitude = request.Longitude,
                        Query = GetStringArg(args, "query"),
                        Category = GetStringArg(args, "category"),
                        MaxPrice = GetDecimalArg(args, "maxPrice"),
                        SortBy = GetStringArg(args, "sortBy"),
                        Limit = 5
                    };

                    var bags = await _bagService.GetNearbyAsync(nearbyReq);
                    suggestedBags.AddRange(bags);

                    if (bags.Count == 0)
                    {
                        return new
                        {
                            status = "no_results_found",
                            message = "No matching surprise bags found within the search area with the requested filters."
                        };
                    }

                    return bags.Select(b => new
                    {
                        name = b.Name,
                        storeName = b.StoreName,
                        salePrice = b.SalePrice,
                        originalPrice = b.OriginalPrice,
                        categories = b.Categories?.Select(c => c.Name).ToList(),
                        distanceKm = b.DistanceKm.HasValue ? Math.Round(b.DistanceKm.Value, 1) : (double?)null,
                        pickupTime = $"{b.PickupStartTime:HH:mm} - {b.PickupEndTime:HH:mm}"
                    }).ToList();
                }

                return new { status = "error", message = $"Unknown tool '{toolName}'" };
            };

            var reply = await _geminiClient.GenerateReplyAsync(
                history,
                trimmedMessage,
                systemInstruction: dynamicContext,
                tools: tools,
                toolHandler: toolHandler,
                cancellationToken: cancellationToken);

            await _sessionStore.AppendTurnsAsync(
                sessionId,
                new[]
                {
                    new ChatTurn("user", trimmedMessage),
                    new ChatTurn("model", reply)
                },
                cancellationToken);

            return new ChatResponse
            {
                SessionId = sessionId,
                Reply = reply,
                SuggestedBags = suggestedBags,
                SuggestedStores = suggestedStores
            };
        }

        public async Task<List<ChatTurn>> GetSessionHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _sessionStore.GetRecentTurnsAsync(userId.ToString(), cancellationToken);
        }

        public async Task ClearSessionAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await _sessionStore.ClearSessionAsync(userId.ToString(), cancellationToken);
        }

        private static List<object> BuildToolDeclarations(string activeCategories)
        {
            return new List<object>
            {
                new
                {
                    functionDeclarations = new object[]
                    {
                        new
                        {
                            name = "search_nearby_stores",
                            description = "Searches for nearby shops, bakeries, or restaurants by name or category. Use when the user asks for store locations, recommendations, or shop profiles.",
                            parameters = new
                            {
                                type = "object",
                                properties = new
                                {
                                    query = new
                                    {
                                        type = "string",
                                        description = "Store name or keyword to search for."
                                    },
                                    category = new
                                    {
                                        type = "string",
                                        description = $"Food category name or slug. Active categories: {activeCategories}."
                                    },
                                    sortBy = new
                                    {
                                        type = "string",
                                        description = "Sort order: 'distance' (default), 'rating', or 'name'."
                                    }
                                }
                            }
                        },
                        new
                        {
                            name = "search_nearby_bags",
                            description = "Searches for available surprise bags, food deals, or meals to purchase near the user. Use when the user asks to buy food, find deals, or get surprise bags.",
                            parameters = new
                            {
                                type = "object",
                                properties = new
                                {
                                    query = new
                                    {
                                        type = "string",
                                        description = "Food item, meal name, or keyword."
                                    },
                                    category = new
                                    {
                                        type = "string",
                                        description = $"Food category name or slug. Active categories: {activeCategories}."
                                    },
                                    maxPrice = new
                                    {
                                        type = "number",
                                        description = "Maximum price in VND (e.g. 50000)."
                                    },
                                    sortBy = new
                                    {
                                        type = "string",
                                        description = "Sort order: 'distance' (default), 'price', or 'rating'."
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static string? GetStringArg(JsonElement element, string propName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString();
                return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }
            return null;
        }

        private static decimal? GetDecimalArg(JsonElement element, string propName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
                {
                    return val;
                }
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var strVal))
                {
                    return strVal;
                }
            }
            return null;
        }
    }
}

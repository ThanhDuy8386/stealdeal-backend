using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class RedisChatSessionStore : IChatSessionStore
    {
        private const string KeyPrefix = "chat:session:";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IConnectionMultiplexer _redis;
        private readonly RedisSettings _settings;

        public RedisChatSessionStore(IConnectionMultiplexer redis, IOptions<RedisSettings> settings)
        {
            _redis = redis;
            _settings = settings.Value;
        }

        public async Task<List<ChatTurn>> GetRecentTurnsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}{sessionId}";
            var items = await db.ListRangeAsync(key, 0, -1);

            var turns = new List<ChatTurn>(items.Length);
            foreach (var item in items)
            {
                if (item.IsNullOrEmpty) continue;
                var turn = JsonSerializer.Deserialize<ChatTurn>((string)item!, JsonOptions);
                if (turn != null)
                {
                    turns.Add(turn);
                }
            }

            return turns;
        }

        public async Task AppendTurnsAsync(string sessionId, IEnumerable<ChatTurn> turns, CancellationToken cancellationToken = default)
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}{sessionId}";

            foreach (var turn in turns)
            {
                var payload = JsonSerializer.Serialize(turn, JsonOptions);
                await db.ListRightPushAsync(key, payload);
            }

            await db.ListTrimAsync(key, -_settings.MaxRecentTurns, -1);
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(_settings.SessionTtlMinutes));
        }

        public async Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}{sessionId}";
            await db.KeyDeleteAsync(key);
        }
    }
}

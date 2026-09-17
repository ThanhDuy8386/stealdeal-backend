using StackExchange.Redis;
using StealDeal.Services.Cart.Domain.Interfaces.Repositories;
using StealDeal.Services.Cart.Domain.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealDeal.Services.Cart.Infrastructure.Repositories
{
    public class RedisCartRepository : ICartRepository
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private const string CartKeyPrefix = "cart:user:";
        private const string CartStoresSuffix = ":stores";
        private const string CheckoutLockKeyPrefix = "checkout-lock:user:";
        private const string StoreIdField = "storeId";
        private const string CurrencyField = "currency";
        private const string VersionField = "version";
        private const string CreatedAtUtcField = "createdAtUtc";
        private const string UpdatedAtUtcField = "updatedAtUtc";
        private const string ExpiresAtUtcField = "expiresAtUtc";
        private const string ItemFieldPrefix = "item:";
        private const string QuantityFieldSuffix = ":quantity";
        private const string SnapshotFieldSuffix = ":snapshot";

        private const string AddOrIncrementItemScript = """
            local currentQuantity = tonumber(redis.call('HGET', KEYS[1], ARGV[6]) or '0')
            local requestedQuantity = tonumber(ARGV[3])
            local maxQuantity = tonumber(ARGV[10])

            if currentQuantity + requestedQuantity > maxQuantity then
                return -1
            end

            redis.call('HSETNX', KEYS[1], 'storeId', ARGV[1])
            redis.call('HSETNX', KEYS[1], 'createdAtUtc', ARGV[4])
            redis.call('HSET', KEYS[1],
                'currency', ARGV[2],
                'updatedAtUtc', ARGV[4],
                'expiresAtUtc', ARGV[5],
                ARGV[7], ARGV[8])

            local quantity = redis.call('HINCRBY', KEYS[1], ARGV[6], ARGV[3])
            redis.call('HINCRBY', KEYS[1], 'version', 1)
            redis.call('EXPIRE', KEYS[1], ARGV[9])
            redis.call('SADD', KEYS[2], ARGV[1])
            redis.call('EXPIRE', KEYS[2], ARGV[9])
            return quantity
            """;

        private const string SetItemQuantityScript = """
            if redis.call('HEXISTS', KEYS[1], ARGV[1]) == 0 then
                return 0
            end

            if tonumber(ARGV[3]) <= 0 then
                redis.call('HDEL', KEYS[1], ARGV[1], ARGV[2])
            else
                redis.call('HSET', KEYS[1], ARGV[1], ARGV[3])
            end

            local itemCount = 0
            local fields = redis.call('HKEYS', KEYS[1])
            for _, field in ipairs(fields) do
                if string.sub(field, -9) == ':quantity' then
                    itemCount = itemCount + 1
                end
            end

            if itemCount == 0 then
                redis.call('DEL', KEYS[1])
                redis.call('SREM', KEYS[2], ARGV[7])
                if redis.call('SCARD', KEYS[2]) == 0 then
                    redis.call('DEL', KEYS[2])
                end
            else
                redis.call('HINCRBY', KEYS[1], 'version', 1)
                redis.call('HSET', KEYS[1], 'updatedAtUtc', ARGV[4], 'expiresAtUtc', ARGV[5])
                redis.call('EXPIRE', KEYS[1], ARGV[6])
                redis.call('EXPIRE', KEYS[2], ARGV[6])
            end

            return 1
            """;

        private const string RemoveItemScript = """
            if redis.call('HEXISTS', KEYS[1], ARGV[1]) == 0 then
                return 0
            end

            redis.call('HDEL', KEYS[1], ARGV[1], ARGV[2])

            local itemCount = 0
            local fields = redis.call('HKEYS', KEYS[1])
            for _, field in ipairs(fields) do
                if string.sub(field, -9) == ':quantity' then
                    itemCount = itemCount + 1
                end
            end

            if itemCount == 0 then
                redis.call('DEL', KEYS[1])
                redis.call('SREM', KEYS[2], ARGV[6])
                if redis.call('SCARD', KEYS[2]) == 0 then
                    redis.call('DEL', KEYS[2])
                end
            else
                redis.call('HINCRBY', KEYS[1], 'version', 1)
                redis.call('HSET', KEYS[1], 'updatedAtUtc', ARGV[3], 'expiresAtUtc', ARGV[4])
                redis.call('EXPIRE', KEYS[1], ARGV[5])
                redis.call('EXPIRE', KEYS[2], ARGV[5])
            end

            return 1
            """;

        private const string ReleaseLockScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end
            """;

        private readonly IDatabase _database;

        public RedisCartRepository(IConnectionMultiplexer connectionMultiplexer)
        {
            _database = connectionMultiplexer.GetDatabase();
        }

        public async Task<IReadOnlyList<UserCart>> GetCartsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var storeIds = await _database.SetMembersAsync(GetCartStoresKey(userId));
            if (storeIds.Length == 0)
            {
                return [];
            }

            var carts = new List<UserCart>();
            foreach (var value in storeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Guid.TryParse(value.ToString(), out var storeId))
                {
                    await _database.SetRemoveAsync(GetCartStoresKey(userId), value);
                    continue;
                }

                var cart = await GetAsync(userId, storeId, cancellationToken);
                if (cart is null)
                {
                    await _database.SetRemoveAsync(GetCartStoresKey(userId), value);
                    continue;
                }

                carts.Add(cart);
            }

            if (carts.Count == 0)
            {
                await _database.KeyDeleteAsync(GetCartStoresKey(userId));
            }

            return carts
                .OrderBy(cart => cart.UpdatedAtUtc)
                .ToList();
        }

        public async Task<UserCart?> GetAsync(
            Guid userId,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = await _database.HashGetAllAsync(GetCartKey(userId, storeId));
            if (entries.Length == 0)
            {
                return null;
            }

            return BuildCart(userId, storeId, entries);
        }

        public async Task SetAsync(UserCart cart, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cart.Items.Count == 0)
            {
                await DeleteAsync(cart.UserId, cart.StoreId, cancellationToken);
                return;
            }

            var key = GetCartKey(cart.UserId, cart.StoreId);
            var indexKey = GetCartStoresKey(cart.UserId);
            var now = DateTime.UtcNow;
            cart.UpdatedAtUtc = now;
            cart.ExpiresAtUtc = now.Add(ttl);

            await _database.KeyDeleteAsync(key);
            await _database.HashSetAsync(key, ToHashEntries(cart));
            await _database.KeyExpireAsync(key, ttl);
            await _database.SetAddAsync(indexKey, cart.StoreId.ToString());
            await _database.KeyExpireAsync(indexKey, ttl);
        }

        public async Task DeleteAsync(
            Guid userId,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var indexKey = GetCartStoresKey(userId);

            await _database.KeyDeleteAsync(GetCartKey(userId, storeId));
            await _database.SetRemoveAsync(indexKey, storeId.ToString());

            if (await _database.SetLengthAsync(indexKey) == 0)
            {
                await _database.KeyDeleteAsync(indexKey);
            }
        }

        public async Task DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var indexKey = GetCartStoresKey(userId);
            var storeIds = await _database.SetMembersAsync(indexKey);

            foreach (var value in storeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Guid.TryParse(value.ToString(), out var storeId))
                {
                    await _database.KeyDeleteAsync(GetCartKey(userId, storeId));
                }
            }

            await _database.KeyDeleteAsync(indexKey);
        }

        public async Task<long?> AddOrIncrementItemAsync(
            Guid userId,
            CartItem item,
            int maxQuantity,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var expiresAt = now.Add(ttl);
            var quantityField = GetQuantityField(item.BagId);
            var snapshotField = GetSnapshotField(item.BagId);
            var snapshot = JsonSerializer.Serialize(ToSnapshot(item), SerializerOptions);
            var ttlSeconds = ToRedisTtlSeconds(ttl);

            var result = (long)await _database.ScriptEvaluateAsync(
                AddOrIncrementItemScript,
                [GetCartKey(userId, item.StoreId), GetCartStoresKey(userId)],
                [
                    item.StoreId.ToString(),
                    "VND",
                    item.Quantity,
                    FormatDateTime(now),
                    FormatDateTime(expiresAt),
                    quantityField,
                    snapshotField,
                    snapshot,
                    ttlSeconds,
                    maxQuantity
                ]);

            return result < 0 ? null : result;
        }

        public async Task<bool> SetItemQuantityAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            int quantity,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var ttlSeconds = ToRedisTtlSeconds(ttl);
            var result = (long)await _database.ScriptEvaluateAsync(
                SetItemQuantityScript,
                [GetCartKey(userId, storeId), GetCartStoresKey(userId)],
                [
                    GetQuantityField(bagId),
                    GetSnapshotField(bagId),
                    quantity,
                    FormatDateTime(now),
                    FormatDateTime(now.Add(ttl)),
                    ttlSeconds,
                    storeId.ToString()
                ]);

            return result == 1;
        }

        public async Task<bool> RemoveItemAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var ttlSeconds = ToRedisTtlSeconds(ttl);
            var result = (long)await _database.ScriptEvaluateAsync(
                RemoveItemScript,
                [GetCartKey(userId, storeId), GetCartStoresKey(userId)],
                [
                    GetQuantityField(bagId),
                    GetSnapshotField(bagId),
                    FormatDateTime(now),
                    FormatDateTime(now.Add(ttl)),
                    ttlSeconds,
                    storeId.ToString()
                ]);

            return result == 1;
        }

        public async Task<string?> AcquireCheckoutLockAsync(
            Guid userId,
            Guid storeId,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lockToken = Guid.NewGuid().ToString("N");
            var acquired = await _database.StringSetAsync(
                GetCheckoutLockKey(userId, storeId),
                lockToken,
                ttl,
                When.NotExists);

            return acquired ? lockToken : null;
        }

        public async Task ReleaseCheckoutLockAsync(
            Guid userId,
            Guid storeId,
            string lockToken,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(lockToken))
            {
                return;
            }

            await _database.ScriptEvaluateAsync(
                ReleaseLockScript,
                [GetCheckoutLockKey(userId, storeId)],
                [lockToken]);
        }

        private static HashEntry[] ToHashEntries(UserCart cart)
        {
            var entries = new List<HashEntry>
            {
                new(StoreIdField, cart.StoreId.ToString()),
                new(CurrencyField, cart.Currency),
                new(VersionField, cart.Version),
                new(CreatedAtUtcField, FormatDateTime(cart.CreatedAtUtc)),
                new(UpdatedAtUtcField, FormatDateTime(cart.UpdatedAtUtc)),
                new(ExpiresAtUtcField, FormatDateTime(cart.ExpiresAtUtc))
            };

            foreach (var item in cart.Items)
            {
                entries.Add(new HashEntry(GetQuantityField(item.BagId), item.Quantity));
                entries.Add(new HashEntry(
                    GetSnapshotField(item.BagId),
                    JsonSerializer.Serialize(ToSnapshot(item), SerializerOptions)));
            }

            return entries.ToArray();
        }

        private static UserCart BuildCart(Guid userId, Guid storeId, HashEntry[] entries)
        {
            var values = entries.ToDictionary(
                entry => entry.Name.ToString(),
                entry => entry.Value);

            var cart = new UserCart
            {
                UserId = userId,
                StoreId = TryParseGuid(values.GetValueOrDefault(StoreIdField)) ?? storeId,
                Currency = GetString(values, CurrencyField, "VND"),
                Version = GetInt(values, VersionField),
                CreatedAtUtc = GetDateTime(values, CreatedAtUtcField),
                UpdatedAtUtc = GetDateTime(values, UpdatedAtUtcField),
                ExpiresAtUtc = GetDateTime(values, ExpiresAtUtcField)
            };

            foreach (var (field, value) in values)
            {
                if (!field.StartsWith(ItemFieldPrefix, StringComparison.Ordinal) ||
                    !field.EndsWith(QuantityFieldSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var bagIdText = field[ItemFieldPrefix.Length..^QuantityFieldSuffix.Length];
                if (!Guid.TryParse(bagIdText, out var bagId))
                {
                    continue;
                }

                var snapshotField = GetSnapshotField(bagId);
                if (!values.TryGetValue(snapshotField, out var snapshotValue) ||
                    snapshotValue.IsNullOrEmpty)
                {
                    continue;
                }

                var snapshot = JsonSerializer.Deserialize<CartItemSnapshot>(
                    snapshotValue.ToString(),
                    SerializerOptions);

                if (snapshot is null)
                {
                    continue;
                }

                cart.Items.Add(new CartItem
                {
                    BagId = bagId,
                    StoreId = snapshot.StoreId,
                    BagNameSnapshot = snapshot.BagNameSnapshot,
                    UnitPriceSnapshot = snapshot.UnitPriceSnapshot,
                    Quantity = (int)value,
                    ImageUrlSnapshot = snapshot.ImageUrlSnapshot,
                    PickupStartUtc = snapshot.PickupStartUtc,
                    PickupEndUtc = snapshot.PickupEndUtc,
                    AddedAtUtc = snapshot.AddedAtUtc
                });
            }

            cart.Items = cart.Items
                .OrderBy(item => item.AddedAtUtc)
                .ToList();

            return cart;
        }

        private static CartItemSnapshot ToSnapshot(CartItem item)
        {
            return new CartItemSnapshot
            {
                StoreId = item.StoreId,
                BagNameSnapshot = item.BagNameSnapshot,
                UnitPriceSnapshot = item.UnitPriceSnapshot,
                ImageUrlSnapshot = item.ImageUrlSnapshot,
                PickupStartUtc = item.PickupStartUtc,
                PickupEndUtc = item.PickupEndUtc,
                AddedAtUtc = item.AddedAtUtc
            };
        }

        private static string GetCartKey(Guid userId, Guid storeId)
        {
            return $"{CartKeyPrefix}{userId}:store:{storeId}";
        }

        private static string GetCartStoresKey(Guid userId)
        {
            return $"{CartKeyPrefix}{userId}{CartStoresSuffix}";
        }

        private static string GetCheckoutLockKey(Guid userId, Guid storeId)
        {
            return $"{CheckoutLockKeyPrefix}{userId}:store:{storeId}";
        }

        private static string GetQuantityField(Guid bagId)
        {
            return $"{ItemFieldPrefix}{bagId}{QuantityFieldSuffix}";
        }

        private static string GetSnapshotField(Guid bagId)
        {
            return $"{ItemFieldPrefix}{bagId}{SnapshotFieldSuffix}";
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        private static long ToRedisTtlSeconds(TimeSpan ttl)
        {
            return Math.Max(1, (long)Math.Ceiling(ttl.TotalSeconds));
        }

        private static string GetString(
            IReadOnlyDictionary<string, RedisValue> values,
            string field,
            string defaultValue)
        {
            return values.TryGetValue(field, out var value) && !value.IsNullOrEmpty
                ? value.ToString()
                : defaultValue;
        }

        private static int GetInt(IReadOnlyDictionary<string, RedisValue> values, string field)
        {
            return values.TryGetValue(field, out var value) && int.TryParse(value.ToString(), out var result)
                ? result
                : 0;
        }

        private static DateTime GetDateTime(
            IReadOnlyDictionary<string, RedisValue> values,
            string field)
        {
            return values.TryGetValue(field, out var value) &&
                   DateTime.TryParse(
                       value,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out var result)
                ? result.ToUniversalTime()
                : DateTime.UtcNow;
        }

        private static Guid? TryParseGuid(RedisValue value)
        {
            return !value.IsNullOrEmpty && Guid.TryParse(value.ToString(), out var result)
                ? result
                : null;
        }

        private sealed class CartItemSnapshot
        {
            public Guid StoreId { get; set; }
            public string BagNameSnapshot { get; set; } = null!;
            public decimal UnitPriceSnapshot { get; set; }
            public string? ImageUrlSnapshot { get; set; }
            public DateTime PickupStartUtc { get; set; }
            public DateTime PickupEndUtc { get; set; }
            public DateTime AddedAtUtc { get; set; }
        }
    }
}

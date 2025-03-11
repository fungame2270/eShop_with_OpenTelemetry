using System.Diagnostics;
using System.Text.Json.Serialization;
using eShop.Basket.API.Model;

namespace eShop.Basket.API.Repositories;

public class RedisBasketRepository(ILogger<RedisBasketRepository> logger, IConnectionMultiplexer redis) : IBasketRepository
{
    private static readonly ActivitySource activitySource = new("BasketService");
    private readonly IDatabase _database = redis.GetDatabase();

    // implementation:

    // - /basket/{id} "string" per unique basket
    private static RedisKey BasketKeyPrefix = "/basket/"u8.ToArray();
    // note on UTF8 here: library limitation (to be fixed) - prefixes are more efficient as blobs

    private static RedisKey GetBasketKey(string userId) => BasketKeyPrefix.Append(userId);

    public async Task<bool> DeleteBasketAsync(string id)
    {
        return await _database.KeyDeleteAsync(GetBasketKey(id));
    }

    public async Task<CustomerBasket> GetBasketAsync(string customerId)
    {
        using var activity = activitySource.StartActivity("redisGetBasket");
        activity.AddEvent(new ActivityEvent("get values from db"));
        using var data = await _database.StringGetLeaseAsync(GetBasketKey(customerId));

        if (data is null || data.Length == 0)
        {
            return null;
        }
        return JsonSerializer.Deserialize(data.Span, BasketSerializationContext.Default.CustomerBasket);
    }

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket)
    {
        using var activity = activitySource.StartActivity("redisUpdateBucket");
        activity?.AddEvent(new ActivityEvent("updating values in redis"));
        var json = JsonSerializer.SerializeToUtf8Bytes(basket, BasketSerializationContext.Default.CustomerBasket);
        var created = await _database.StringSetAsync(GetBasketKey(basket.BuyerId), json);

        if (!created)
        {
            activity?.AddEvent(new ActivityEvent("Problem occurred persisting the item."));
            logger.LogInformation("Problem occurred persisting the item.");
            return null;
        }


        logger.LogInformation("Basket item persisted successfully.");
        return await GetBasketAsync(basket.BuyerId);
    }
}

[JsonSerializable(typeof(CustomerBasket))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class BasketSerializationContext : JsonSerializerContext
{

}

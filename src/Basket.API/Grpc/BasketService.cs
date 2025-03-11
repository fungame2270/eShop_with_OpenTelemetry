using System.Diagnostics.CodeAnalysis;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Extensions;
using eShop.Basket.API.Model;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger) : Basket.BasketBase
{
    private static readonly ActivitySource activitySource = new("BasketService");
    
    private static readonly  Meter meter = new("BasketService");
    private static readonly Counter<long> basketUpdateCounter = meter.CreateCounter<long>("basket.update_basket.count");
    private static readonly Counter<long> basketAddedItemsUpdateCounter = meter.CreateCounter<long>("basket.added_items.count");
    private static readonly Counter<long> basketUpdateErrors = meter.CreateCounter<long>("basket.update_basket.errors");

    private static readonly Histogram<double> addToBasketHistogramTime = meter.CreateHistogram<double>("basket.update_basket.duration","milliseconds");
    
    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {
        using var activity = activitySource.StartActivity("GetBasket",ActivityKind.Server);
        var userId = context.GetUserIdentity();
        activity?.SetTag("UserId",userId);
        if (string.IsNullOrEmpty(userId)){
            activity?.SetStatus(ActivityStatusCode.Error,"Error User not loggedIn");
            activity?.AddEvent(new ActivityEvent("User not logged in", DateTime.UtcNow));
            return new();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);

        if (data is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.AddEvent(new ActivityEvent("Successfully returned the basket", DateTime.UtcNow));
            return MapToCustomerBasketResponse(data);
        }
        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.AddEvent(new ActivityEvent("Successfully empty basket", DateTime.UtcNow));
        return new();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
    {
        using var activity = activitySource.StartActivity("UpdateBasket",ActivityKind.Server);
        var startTime = Stopwatch.StartNew();
        var userId = context.GetUserIdentity();
        activity?.SetTag("UserId",userId);
        if (string.IsNullOrEmpty(userId))
        {   
            activity?.SetStatus(ActivityStatusCode.Error,"Error User not loggedIn");
            activity?.AddEvent(new ActivityEvent("User not logged in", DateTime.UtcNow));
            basketUpdateErrors.Add(1);
            ThrowNotAuthenticated();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin UpdateBasket call from method {Method} for basket id {Id}", context.Method, userId);
        }

        activity?.AddEvent(new ActivityEvent("Mapping basket to CustomerBasket", DateTime.UtcNow));
        var customerBasket = MapToCustomerBasket(userId, request);
        var response = await repository.UpdateBasketAsync(customerBasket);
        if (response is null)
        {   
            activity?.SetStatus(ActivityStatusCode.Error, "Basket does not exist");
            activity?.AddEvent(new ActivityEvent("Basket not found", DateTime.UtcNow));
            basketUpdateErrors.Add(1);
            ThrowBasketDoesNotExist(userId);
        }

        startTime.Stop();
        var durationms = startTime.ElapsedMilliseconds;

        addToBasketHistogramTime.Record(durationms);
        basketUpdateCounter.Add(1);

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.AddEvent(new ActivityEvent("Successfully updated the basket", DateTime.UtcNow));
        activity?.AddEvent(new ActivityEvent("Mapping basket to CustomerBasket response", DateTime.UtcNow));
        return MapToCustomerBasketResponse(response);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        await repository.DeleteBasketAsync(userId);
        return new();
    }

    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        using var activity = activitySource.StartActivity("MapToCustomerBasketResponse",ActivityKind.Internal);
        var response = new CustomerBasketResponse();
        activity?.AddEvent(new ActivityEvent($"{customerBasket.Items.Count} returned items"));
        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }
        activity?.SetStatus(ActivityStatusCode.Ok);
        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest)
    {
        using var activity = activitySource.StartActivity("MapToCustomerBasket",ActivityKind.Internal);
        var response = new CustomerBasket
        {
            BuyerId = userId
        };

        foreach (var item in customerBasketRequest.Items)
        {
            response.Items.Add(new()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
            basketAddedItemsUpdateCounter.Add(item.Quantity);
            string eve = $"Update item {item.ProductId} to {item.Quantity}";
            activity?.AddEvent(new ActivityEvent(eve));
        }

        return response;
    }
}

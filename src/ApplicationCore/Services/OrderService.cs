using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);

        var orderDetails = new OrderDetails
        {
            BuyerId = basket.BuyerId,
            ShipToAddress = shippingAddress,
            OrderItems = items,
            TotalPrice = order.Total()
        };

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-functions-key", "N3aqHzMYPhsCGbpZ_ZZiZLJnjZ3lz7ocajJVmE9FYVLyAzFu9iwBFA==");

        var response = await httpClient.PostAsJsonAsync("https://cloudxcosmosdbfunction.azurewebsites.net/api/DeliveryOrderProcessorFunction", order);

        var responseString = response.Content.ReadAsStringAsync();
    }

    private record OrderDetails
    {
        public required string BuyerId { get; init; }
        public required Address ShipToAddress { get; init; }
        public required IReadOnlyCollection<OrderItem> OrderItems { get; init; }
        public required decimal TotalPrice { get; init; }
    }
}

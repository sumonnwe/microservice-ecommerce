using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NUnit.Framework;

namespace OrderService.Tests.Controllers;

[TestFixture]
public class OrdersControllerTests
{
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        var factory = new CustomWebApplicationFactory();
        _client = factory.CreateClient();
    }

    [Test]
    [Ignore("Temporarily disabled")]
    public async Task POST_CreateOrder_ShouldReturn201()
    {
        var request = new
        {
            userId = Guid.NewGuid(),
            product = "Phone",
            quantity = 1,
            price = 999
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}

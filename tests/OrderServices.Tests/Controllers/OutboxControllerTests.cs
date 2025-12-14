using FluentAssertions;
using NUnit.Framework;
using System.Net;

namespace OrderService.Tests.Controllers;

[TestFixture]
public class OutboxControllerTests
{
    [Test]
    public async Task GET_Outbox_ShouldReturn200()
    {
        var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/outbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

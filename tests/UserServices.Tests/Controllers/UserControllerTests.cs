using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using UserService.DTOs;

namespace UserServices.Tests.Controllers;

[TestFixture]
public class UserControllerTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [Test]
    public async Task POST_CreateUser_ShouldReturn201Created()
    {
        // Arrange
        var request = new
        {
            name = "Alice",
            email = "alice@test.com"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        //var user = await response.Content.ReadFromJsonAsync<UserCreateDto>();
        //user.Should().NotBeNull();
        //user!.Name.Should().Be("Alice");
    }

    [Test]
    public async Task POST_CreateUser_WithInvalidPayload_ShouldReturn400()
    {
        // Arrange
        var request = new { name = "", email = "" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

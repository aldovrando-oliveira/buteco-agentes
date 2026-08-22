using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Auth.Requests;
using Buteco.Api.Auth.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AuthLoginTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsTokenAndExpiration()
    {
        var request = new LoginRequest(ApiFactoryFixture.KnownOperatorUsername, ApiFactoryFixture.KnownOperatorPassword);

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.Token));
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsGenericUnauthorized()
    {
        var request = new LoginRequest("usuario-inexistente", ApiFactoryFixture.KnownOperatorPassword);

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsGenericUnauthorizedWithSameMessageAsUnknownUsername()
    {
        var wrongUsername = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("usuario-inexistente", ApiFactoryFixture.KnownOperatorPassword));
        var wrongPassword = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(ApiFactoryFixture.KnownOperatorUsername, "senha-errada"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var wrongUsernameBody = await wrongUsername.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();
        Assert.Equal(wrongUsernameBody, wrongPasswordBody);
    }

    [Fact]
    public async Task Login_WithoutConfiguredLifetime_ExpiresThirtyMinutesAfterIssuance()
    {
        var beforeLogin = DateTimeOffset.UtcNow;

        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(ApiFactoryFixture.KnownOperatorUsername, ApiFactoryFixture.KnownOperatorPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        var expectedExpiry = beforeLogin.AddMinutes(30);
        Assert.True(Math.Abs((body.ExpiresAt - expectedExpiry).TotalSeconds) < 5);
    }
}

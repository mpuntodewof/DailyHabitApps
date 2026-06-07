using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Integration;

// NOTE: tests in this class share one ApiFactory (IClassFixture) and therefore ONE SQLite
// database. They stay isolated by using distinct emails per test. As more integration tests
// are added here, switch to per-test DB isolation (e.g. IAsyncLifetime resetting the DB)
// rather than relying on unique data — shared mutable state gets brittle at scale.
public class AuthFlowIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthFlowIntegrationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_then_login_succeeds_over_http()
    {
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = "intuser", email = "intuser@test.local", password = "Secret@123", role = "User"
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "intuser@test.local", password = "Secret@123"
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        body.Should().NotBeNull(); // guards a silent camelCase-deserialization mismatch
        body!.IsSuccess.Should().BeTrue();
        body.Result!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = "intuser2", email = "intuser2@test.local", password = "Secret@123", role = "User"
        });

        var login = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "intuser2@test.local", password = "WRONG"
        });

        var body = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        body.Should().NotBeNull();
        body!.IsSuccess.Should().BeFalse();
    }

    // Mirrors the ApiResponse envelope. Property names map to the wire JSON via the API's
    // camelCase policy (isSuccess / result / accessToken) — ASP.NET's default and set
    // explicitly in the app's JSON options.
    private class LoginEnvelope
    {
        public bool IsSuccess { get; set; }
        public LoginResult? Result { get; set; }
    }
    private class LoginResult
    {
        public string? AccessToken { get; set; }
    }
}

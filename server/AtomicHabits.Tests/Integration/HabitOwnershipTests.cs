using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace AtomicHabits.Tests.Integration;

/// <summary>
/// Task 9 — IDOR protection: confirms the server always scopes habit reads to the
/// authenticated user's JWT sub, ignoring any userId supplied in the route.
/// Uses its own IClassFixture&lt;ApiFactory&gt; so it gets a dedicated SQLite DB
/// and cannot collide with AuthFlowIntegrationTests.
/// </summary>
public class HabitOwnershipTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public HabitOwnershipTests(ApiFactory factory) => _factory = factory;

    /// <summary>Registers a new user and returns their JWT access token.</summary>
    private static async Task<string> RegisterAndLogin(HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/Auth/register", new
        {
            username = email.Split('@')[0],
            email,
            password = "Secret@123",
            role = "User"
        });
        var login = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email,
            password = "Secret@123"
        });
        var env = await login.Content.ReadFromJsonAsync<LoginEnvelope>();
        return env!.Result!.AccessToken!;
    }

    [Fact]
    public async Task User_B_cannot_read_user_A_habits_via_route_id()
    {
        var client = _factory.CreateClient();

        // --- Set up two independent users ---
        var tokenA = await RegisterAndLogin(client, "owner-a@test.local");
        var tokenB = await RegisterAndLogin(client, "intruder-b@test.local");

        // --- A creates a habit ---
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var create = await client.PostAsJsonAsync("/api/Habit/post-habit", new
        {
            name          = "A's private habit",
            color         = "#ffffff",
            description   = "Should never be visible to user B",
            frequency     = "Daily",
            goalValue     = 1,
            goalUnit      = "times",
            goalFrequency = "daily"
        });
        var createBody = await create.Content.ReadAsStringAsync();
        create.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            because: $"habit creation should succeed; response body: {createBody}");

        // --- Positive control: A can see their own habit ---
        var asA = await client.GetAsync("/api/Habit/get-habits/1");
        asA.EnsureSuccessStatusCode();
        var bodyA = await asA.Content.ReadAsStringAsync();
        bodyA.Should().Contain("A's private habit",
            because: "the owner must be able to retrieve their own habit");

        // --- IDOR attempt: B reads habits, putting A's likely id (1) in the route ---
        // The server must ignore the route param and scope the result to B's JWT sub.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var asB = await client.GetAsync("/api/Habit/get-habits/1");
        asB.EnsureSuccessStatusCode();
        var bodyB = await asB.Content.ReadAsStringAsync();

        bodyB.Should().NotContain("A's private habit",
            because: "user B must never receive habits that belong to user A");
    }

    // Mirrors the camelCase ApiResponse envelope returned by /api/Auth/login
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

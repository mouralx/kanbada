using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OtpNet;
using Xunit;

public sealed class TwoFactorTests : IAsyncLifetime
{
    private const string Password = "Correct-Horse-Test-Password";
    private readonly ApiFixture fixture = new();
    private WebApplicationFactory<Program> factory = null!;
    private readonly TestClock clock = new();
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
    }
    public async Task DisposeAsync() { await factory.DisposeAsync(); await fixture.DisposeAsync(); }
    private HttpClient Client()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Kanbada-Request", "1");
        return client;
    }
    private static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }
    private async Task<(HttpClient Client, string Email, Guid Id)> Register(bool legacy = false)
    {
        var client = Client();
        var email = Guid.NewGuid() + "@example.test";
        var result = await Json(await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, name = "MFA Test" }));
        var id = Guid.Parse(result["id"]!.ToString());
        if (legacy)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<KanbadaDbContext>().Users.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorRequired, false));
        }
        return (client, email, id);
    }
    private async Task<string> Setup(HttpClient client) => (await Json(await client.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = Password })))["secret"]!.ToString();
    private string Code(string secret) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(clock.Now.UtcDateTime);
    private async Task<string[]> Enable(HttpClient client, string secret) =>
        (await Json(await client.PostAsJsonAsync("/api/auth/two-factor/confirm", new { password = Password, code = Code(secret) })))["codes"]!.AsArray().Select(x => x!.ToString()).ToArray();

    [Fact]
    public async Task SetupRequiresPasswordAndConfirmation_SecretsAreEncrypted_OtherSessionsRevoked()
    {
        var (client, email, id) = await Register();
        using var other = Client();
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/two-factor")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = "wrong" })).StatusCode);
        var secret = await Setup(client);
        Assert.False((await Json(await client.GetAsync("/api/auth/two-factor")))["enabled"]!.GetValue<bool>());
        var codes = await Enable(client, secret);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.Null((await Json(await other.GetAsync("/api/auth/session")))["user"]);
        Assert.NotNull((await Json(await client.GetAsync("/api/auth/session")))["user"]);
        var status = await Json(await client.GetAsync("/api/auth/two-factor"));
        Assert.True(status["enabled"]!.GetValue<bool>());
        Assert.Equal(10, status["recoveryCodesRemaining"]!.GetValue<int>());
        Assert.Null(status["secret"]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var stored = await db.Users.SingleAsync(x => x.Id == id);
        Assert.NotEqual(secret, stored.TwoFactorSecret);
        Assert.Null(stored.TwoFactorPendingSecret);
        Assert.All(await db.RecoveryCodes.Where(x => x.UserId == id).ToListAsync(), row =>
        {
            Assert.DoesNotContain(row.Hash, codes.Select(x => x.Replace("-", "")));
            Assert.Equal(64, row.Hash.Length);
        });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = Password })).StatusCode);
    }

    [Fact]
    public async Task LoginRequiresSecondFactor_RejectsReplay_AndConsumesRecoveryCodeAtomically()
    {
        var (client, email, _) = await Register();
        var secret = await Setup(client);
        var codes = await Enable(client, secret);
        using var login = Client();
        var required = await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.True((await Json(required))["twoFactorRequired"]!.GetValue<bool>());
        Assert.False(required.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await login.GetAsync("/api/workspaces")).StatusCode);
        // The code consumed during enrollment must not be reusable.
        Assert.Equal(HttpStatusCode.BadRequest, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = Code(secret) })).StatusCode);
        clock.Now = clock.Now.AddSeconds(30);
        var code = Code(secret);
        Assert.Equal(HttpStatusCode.NoContent, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code })).StatusCode);
        using var replay = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await replay.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code })).StatusCode);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var concurrent = Client();
            return (await concurrent.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = codes[0] })).StatusCode;
        }));
        Assert.Single(attempts, x => x == HttpStatusCode.NoContent);
        Assert.Single(attempts, x => x == HttpStatusCode.BadRequest);
        Assert.Equal(9, (await Json(await client.GetAsync("/api/auth/two-factor")))["recoveryCodesRemaining"]!.GetValue<int>());
    }

    [Fact]
    public async Task FailedAttemptsLockAccountAcrossClients_AndExpireAfterFiveMinutes()
    {
        var (client, email, _) = await Register();
        var secret = await Setup(client);
        var codes = await Enable(client, secret);
        for (var i = 0; i < 5; i++)
        {
            using var attempt = Client();
            Assert.Equal(HttpStatusCode.BadRequest, (await attempt.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = "invalid" })).StatusCode);
        }
        using var login = Client();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = codes[0] })).StatusCode);
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal(HttpStatusCode.NoContent, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = codes[0] })).StatusCode);
    }

    [Fact]
    public async Task ExpiredOrReplacedSetupCannotEnableProtection()
    {
        var (client, _, _) = await Register();
        var old = await Setup(client);
        var secret = await Setup(client);
        Assert.NotEqual(old, secret);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/confirm", new { password = Password, code = Code(old) })).StatusCode);
        clock.Now = clock.Now.AddMinutes(11);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/confirm", new { password = Password, code = Code(secret) })).StatusCode);
        Assert.False((await Json(await client.GetAsync("/api/auth/two-factor")))["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RecoveryReplacementAndDisableRequireBothFactors_AndRevokeOldCodes()
    {
        var (client, email, _) = await Register(legacy: true);
        var secret = await Setup(client);
        var codes = await Enable(client, secret);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = "wrong", code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password })).StatusCode);
        var replacement = await Json(await client.PostAsJsonAsync("/api/auth/two-factor/recovery-codes", new { password = Password, code = codes[0] }));
        using var login = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code = codes[1] })).StatusCode);
        var fresh = replacement["codes"]![0]!.ToString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password, code = fresh })).StatusCode);
        Assert.False((await Json(await client.GetAsync("/api/auth/two-factor")))["enabled"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NoContent, (await login.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
    }
    [Fact]
    public async Task NewRegistrationCannotAccessWorkspaceUntilEnrollment_OrDisableRequiredProtection()
    {
        var (client, email, _) = await Register();
        Assert.True((await Json(await client.GetAsync("/api/auth/session")))["twoFactorSetupRequired"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/workspaces", new { name = "Bypass" })).StatusCode);
        await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workspaces/studio")).StatusCode);
        var secret = await Setup(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/two-factor/confirm", new { password = Password, code = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workspaces")).StatusCode);
        var codes = await Enable(client, secret);
        Assert.False((await Json(await client.GetAsync("/api/auth/session")))["twoFactorSetupRequired"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password, code = codes[0] })).StatusCode);
    }

    [Fact]
    public async Task ExternalRegistrationsMustEnroll_AndReturningProviderSessionsMustVerify()
    {
        // Model the authenticated external identity delivered by OAuth middleware.
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<Auth>();
        var identity = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "external-test-subject"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "external@example.test")
        }, "external"));
        var id = await auth.External("microsoft", identity);
        async Task<HttpClient> ProviderSession()
        {
            using var sessionScope = factory.Services.CreateScope();
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = sessionScope.ServiceProvider };
            await sessionScope.ServiceProvider.GetRequiredService<Auth>().Session(context, id);
            var client = Client();
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.ToString().Split(';')[0]);
            return client;
        }
        using var client = await ProviderSession();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workspaces")).StatusCode);
        var status = await Json(await client.GetAsync("/api/auth/two-factor"));
        Assert.True(status["required"]!.GetValue<bool>());
        Assert.False(status["passwordRequired"]!.GetValue<bool>());
        var secret = await Setup(client);
        var codes = await Enable(client, secret);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workspaces")).StatusCode);
        using var returning = await ProviderSession();
        Assert.True((await Json(await returning.GetAsync("/api/auth/session")))["twoFactorVerificationRequired"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await returning.GetAsync("/api/workspaces")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await returning.PostAsJsonAsync("/api/auth/two-factor/verify", new { password = "", code = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await returning.PostAsJsonAsync("/api/auth/two-factor/verify", new { password = "", code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await returning.GetAsync("/api/workspaces")).StatusCode);
    }

}

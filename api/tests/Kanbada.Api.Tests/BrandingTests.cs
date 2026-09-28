using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Xunit;

public sealed class BrandingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Path = "/api/platform/branding";
    private const string Password = "Branding-test-password";
    private static BrandingInput Input(long version = 0) => new(version, "Team Studio", AvatarTests.Photo, "#8040c0", "#f0b040", "#fff8ef", "#181225", "dark");

    private async Task<(HttpClient Client, Guid Id, string Email)> Account(bool enroll = true)
    {
        var client = fixture.Client();
        var email = "branding-" + Guid.NewGuid() + "@example.test";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, name = "Branding Owner", password = Password });
        registration.EnsureSuccessStatusCode();
        var user = (await registration.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = Guid.Parse(user["id"]!.ToString());
        if (enroll)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            await scope.ServiceProvider.GetRequiredService<AccountAvatar>().Save(id, new AvatarInput(AvatarTests.Photo), default);
            var factor = scope.ServiceProvider.GetRequiredService<TwoFactor>();
            var secret = await factor.Setup(id, Password);
            var sid = await db.Sessions.Where(s => s.UserId == id).Select(s => s.Id).SingleAsync();
            await factor.Confirm(id, new TwoFactorInput(Password, new Totp(Base32Encoding.ToBytes(secret.Secret)).ComputeTotp()), sid);
        }
        return (client, id, email);
    }

    [Fact]
    public async Task PublicReadAdminAuthorizationValidationConcurrencyAndReset()
    {
        using var anonymous = fixture.Client();
        var original = await anonymous.GetFromJsonAsync<PlatformBrandingEntity>(Path);
        Assert.Equal("kanbada", original!.Name);
        Assert.True(original.ShowName);
        Assert.Null(original.CollapsedLogo);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Path, Input())).StatusCode);
        var (owner, id, email) = await Account();
        using (owner)
        {
            Assert.False((await owner.GetFromJsonAsync<JsonObject>(Path + "/access"))!["canManage"]!.GetValue<bool>());
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync(Path, Input())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/jira/hosts", new { baseUrl = "https://jira.example.test:8443/jira" })).StatusCode);
            using var scope = fixture.Factory.Services.CreateScope();
            var admins = scope.ServiceProvider.GetRequiredService<PlatformAdmins>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:AdminEmails"] = email.ToUpperInvariant() }).Build();
            await admins.Initialize(config, scope.ServiceProvider.GetRequiredService<KanbadaDbContext>());
            Assert.True(admins.Contains(id));
            var destination = new JiraDestinationPolicy(new ConfigurationBuilder().Build(), scope.ServiceProvider.GetRequiredService<KanbadaDbContext>());
            Assert.Throws<ApiError>(() => destination.Validate("https://jira.example.test:8443/jira", "data-center"));
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/jira/hosts", new { baseUrl = "http://jira.example.test" })).StatusCode);
            (await owner.PostAsJsonAsync("/api/jira/hosts", new { baseUrl = "https://jira.example.test:8443/jira" })).EnsureSuccessStatusCode();
            Assert.Equal("jira.example.test:8443", destination.Validate("https://jira.example.test:8443/jira", "data-center").Authority);
            Assert.Throws<ApiError>(() => destination.Validate("https://jira.example.test/jira", "data-center"));
            using (var workerScope = fixture.Factory.Services.CreateScope())
            {
                var workerPolicy = new JiraDestinationPolicy(new ConfigurationBuilder().Build(), workerScope.ServiceProvider.GetRequiredService<KanbadaDbContext>());
                Assert.Equal("jira.example.test:8443", workerPolicy.Validate("https://jira.example.test:8443", "data-center").Authority);
            }
            using (var revoke = new HttpRequestMessage(HttpMethod.Delete, "/api/jira/hosts") { Content = JsonContent.Create(new { baseUrl = "https://jira.example.test:8443" }) })
                Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(revoke)).StatusCode);
            Assert.Throws<ApiError>(() => destination.Validate("https://jira.example.test:8443/jira", "data-center"));
            Assert.True((await owner.GetFromJsonAsync<JsonObject>(Path + "/access"))!["canManage"]!.GetValue<bool>());
            foreach (var invalid in new[] {
                Input() with { Primary = "red; background:url(https://evil.test)" },
                Input() with { Name = " " },
                Input() with { DefaultTheme = "invalid" },
                Input() with { LightSurface = "red" },
                Input() with { FontFamily = "url(https://example.test)" },
                Input() with { FontScale = 121 },
                Input() with { CornerRadius = 17 },
                Input() with { Logo = "data:image/svg+xml;base64,PHN2Zz4=" },
                Input() with { CollapsedLogo = "data:image/svg+xml;base64,PHN2Zz4=" }
            })
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(Path, invalid)).StatusCode);
            var response = await owner.PutAsJsonAsync(Path, Input() with {
                LightSurface = "#ffffff", DarkSurface = "#202040", LightText = "#223344", DarkText = "#eeeeff",
                LightBorder = "#778899", DarkBorder = "#667788", SidebarBackground = "#001122",
                SidebarText = "#eeeeff", FontFamily = "system", FontScale = 115, CornerRadius = 12, ShowName = false,
                CollapsedLogo = AvatarTests.Photo });
            response.EnsureSuccessStatusCode();
            var saved = (await response.Content.ReadFromJsonAsync<PlatformBrandingEntity>())!;
            Assert.Equal(1, saved.Version);
            Assert.Equal("Team Studio", saved.Name);
            Assert.Equal(AvatarTests.Photo, saved.Logo);
            Assert.False(saved.ShowName);
            Assert.Equal(AvatarTests.Photo, saved.CollapsedLogo);
            Assert.Equal("#202040", saved.DarkSurface);
            Assert.Equal("#001122", saved.SidebarBackground);
            Assert.Equal("system", saved.FontFamily);
            Assert.Equal(115, saved.FontScale);
            Assert.Equal(12, saved.CornerRadius);
            var publicResponse = await anonymous.GetAsync(Path);
            Assert.Equal("Team Studio", (await publicResponse.Content.ReadFromJsonAsync<PlatformBrandingEntity>())!.Name);
            Assert.DoesNotContain(email, await publicResponse.Content.ReadAsStringAsync());
            using var conditional = new HttpRequestMessage(HttpMethod.Get, Path);
            conditional.Headers.IfNoneMatch.Add(publicResponse.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotModified, (await anonymous.SendAsync(conditional)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(Path, Input())).StatusCode);
            var defaults = new PlatformBrandingEntity();
            var reset = new BrandingInput(saved.Version, defaults.Name, null, defaults.Primary, defaults.Accent, defaults.LightBackground, defaults.DarkBackground, defaults.DefaultTheme);
            (await owner.PutAsJsonAsync(Path, reset)).EnsureSuccessStatusCode();
            var restored = (await anonymous.GetFromJsonAsync<PlatformBrandingEntity>(Path))!;
            Assert.Equal(2, restored.Version);
            Assert.Equal("kanbada", restored.Name);
            Assert.Null(restored.Logo);
            Assert.True(restored.ShowName);
            Assert.Null(restored.CollapsedLogo);
            Assert.Null(restored.LightSurface);
            Assert.Equal("default", restored.FontFamily);
            Assert.Equal(100, restored.FontScale);
            Assert.Equal(8, restored.CornerRadius);
        }
    }

    [Fact]
    public async Task EnrollmentSessionsCanReadBrandingButCannotAdminister()
    {
        var (client, id, email) = await Account(false);
        using (client)
        {
            (await client.GetAsync(Path)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path + "/access")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Path, Input())).StatusCode);
        }
    }

    [Fact]
    public async Task BootstrapRejectsFutureAccounts()
    {
        using var client = fixture.Client();
        using var scope = fixture.Factory.Services.CreateScope();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:AdminEmails"] = "not-created-" + Guid.NewGuid() + "@example.test" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdmins().Initialize(config, scope.ServiceProvider.GetRequiredService<KanbadaDbContext>()));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Xunit;

public sealed class AvatarTests : IAsyncLifetime
{
    public const string Photo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAO0lEQVR4AezXwQkAMAgDwOIw3bV7dS8dwacgF8g/3C9x38/JxhmOAQQIECBAgAABAgQIECBAYL9Ad74LAAD//6p1CZ0AAAAGSURBVAMAUaROge/nJygAAAAASUVORK5CYII=";
    private readonly ApiFixture fixture = new();
    private WebApplicationFactory<Program> factory = null!;
    private readonly GravatarHandler handler = new();
    private sealed class GravatarHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Uri? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(Convert.FromBase64String(Photo.Split(',')[1])) };
            response.Content.Headers.ContentType = new("image/png");
            return Task.FromResult(response);
        }
    }
    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient("gravatar").ConfigurePrimaryHttpMessageHandler(() => handler)));
    }
    public async Task DisposeAsync() { await factory.DisposeAsync(); await fixture.DisposeAsync(); }
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kanbada-Request", "1");
        return client;
    }
    private static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
    [Fact]
    public async Task AvatarIsRequiredEvenAfterTwoFactor_AndCannotBeRemovedFromProfile()
    {
        using var client = Client();
        const string password = "Long-Test-Password-2026";
        var registered = await Json(await client.PostAsJsonAsync("/api/auth/register", new { email = "avatar@example.test", name = "Avatar", password }));
        Assert.True(registered["avatarRequired"]!.GetValue<bool>());
        var setup = await Json(await client.PostAsJsonAsync("/api/auth/two-factor/setup", new { password }));
        var code = new Totp(Base32Encoding.ToBytes(setup["secret"]!.ToString())).ComputeTotp();
        await Json(await client.PostAsJsonAsync("/api/auth/two-factor/confirm", new { password, code }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workspaces")).StatusCode);
        foreach (var photo in new[] { "", "https://example.test/photo.png", "data:image/png;base64,aGVsbG8=", "data:image/svg+xml;base64,PHN2Zy8+" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/avatar", new { photo })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/avatar", new { photo = Photo })).StatusCode);
        Assert.False((await Json(await client.GetAsync("/api/auth/session")))["avatarRequired"]!.GetValue<bool>());
        var state = await Json(await client.GetAsync("/api/workspaces/studio"));
        Assert.Equal(Photo, state["members"]![0]!["photo"]!.ToString());
        state["members"]![0]!["photo"] = null;
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio") { Content = JsonContent.Create(state) };
        request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }
    [Fact]
    public async Task GravatarRequiresExplicitSelection_UsesOwnEmailHash_AndHandlesMissingAndUnavailable()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/avatar/gravatar")).StatusCode);
        using var client = Client();
        await Json(await client.PostAsJsonAsync("/api/auth/register", new { email = "Gravatar@Example.test", name = "Avatar", password = "Long-Test-Password-2026" }));
        var preview = await Json(await client.GetAsync("/api/auth/avatar/gravatar"));
        Assert.Equal(Photo, preview["photo"]!.ToString());
        Assert.Equal("https://www.gravatar.com/avatar/" + Auth.Hash("gravatar@example.test").ToLowerInvariant() + "?d=404&s=256&r=g", handler.Uri!.ToString());
        Assert.True((await Json(await client.GetAsync("/api/auth/session")))["avatarRequired"]!.GetValue<bool>());
        handler.Status = HttpStatusCode.NotFound;
        Assert.Null((await Json(await client.GetAsync("/api/auth/avatar/gravatar")))["photo"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/avatar", new { useGravatar = true })).StatusCode);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/auth/avatar/gravatar")).StatusCode);
        handler.Status = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/avatar", new { useGravatar = true })).StatusCode);
        Assert.False((await Json(await client.GetAsync("/api/auth/session")))["avatarRequired"]!.GetValue<bool>());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(Photo, (await scope.ServiceProvider.GetRequiredService<KanbadaDbContext>().Users.SingleAsync()).Photo);
    }
}

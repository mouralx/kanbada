using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed class GitHubSecrets
{
    private readonly IDataProtector protector;
    public GitHubSecrets(string directory) => protector = DataProtectionProvider.Create(new DirectoryInfo(directory),
        options => options.SetApplicationName("Kanbada.GitHub")).CreateProtector("GitHubTokens.v1");
    public string Protect(string token) => protector.Protect(token);
    public string Unprotect(string token) => protector.Unprotect(token);
}

public sealed class GitHubDestinationPolicy(IConfiguration configuration, KanbadaDbContext? db = null)
{
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || uri.Host == "api.github.com")
            throw new ApiError(400, "Use https://github.com or your GitHub Enterprise Server HTTPS origin, without an API path, credentials, query or fragment.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public Uri Validate(string value)
    {
        var uri = Parse(value);
        var allowed = (configuration["GitHub:AllowedHosts"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!(uri.Host == "github.com" && uri.Port == 443) && !allowed.Contains(uri.Authority, StringComparer.OrdinalIgnoreCase)
            && db?.Set<GitHubApprovedHostEntity>().Any(h => h.Authority == uri.Authority.ToLowerInvariant()) != true)
            throw new ApiError(400, "This GitHub Enterprise Server needs approval from a platform administrator under Server access.");
        return uri;
    }

    public Uri Api(string value)
    {
        var uri = Validate(value);
        return uri.Host == "github.com" && uri.Port == 443 ? new Uri("https://api.github.com/graphql") : new Uri(uri, "api/graphql");
    }

    public static string BrowserUrl(string baseUrl, string value)
    {
        var origin = Parse(baseUrl);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Authority != origin.Authority || uri.UserInfo.Length != 0)
            throw new GitHubSyncException("GitHub returned a browser link outside the configured server.");
        return uri.AbsoluteUri;
    }
}

public static class GitHubConfiguration
{
    public static IServiceCollection AddGitHub(this IServiceCollection services, string keyDirectory)
    {
        services.AddSingleton(new GitHubSecrets(keyDirectory));
        services.AddScoped<GitHubDestinationPolicy>();
        services.AddScoped<GitHubSettings>();
        services.AddScoped<GitHubSyncEngine>();
        services.AddHttpClient<GitHubClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.MaxResponseContentBufferSize = 32 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Kanbada/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}

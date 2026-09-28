using Cronos;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Mail;

namespace Kanbada.Api;

public sealed class JiraSecrets
{
    private readonly IDataProtector protector;
    public JiraSecrets(string directory) =>
        protector = DataProtectionProvider.Create(new DirectoryInfo(directory),
            options => options.SetApplicationName("Kanbada.Jira")).CreateProtector("JiraTokens.v1");
    public string Protect(string token) => protector.Protect(token);
    public string Unprotect(string token) => protector.Unprotect(token);
}

public sealed class JiraDestinationPolicy(IConfiguration configuration, KanbadaDbContext? db = null)
{
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ApiError(400, "Jira URL must be an HTTPS base URL without credentials, query, or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public Uri Validate(string value, string edition)
    {
        var uri = Parse(value);
        var allowed = (configuration["Jira:AllowedHosts"] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cloud = edition == "cloud" && uri.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase)
            && uri.Port == 443 && uri.AbsolutePath is "/" or "";
        if (!cloud && !allowed.Contains(uri.Authority, StringComparer.OrdinalIgnoreCase)
            && db?.Set<JiraApprovedHostEntity>().Any(h => h.Authority == uri.Authority.ToLowerInvariant()) != true)
            throw new ApiError(400, "This Jira server needs approval. A platform administrator can approve it in Jira synchronization settings under Server access. No restart is needed.");
        return uri;
    }
}

public static class JiraSchedule
{
    public static DateTimeOffset Next(string cron, string zone, DateTimeOffset after)
    {
        if (string.IsNullOrWhiteSpace(cron) || cron.Length > 128 || string.IsNullOrWhiteSpace(zone) || zone.Length > 100)
            throw new ApiError(400, "Use a valid five-field cron expression and an IANA time zone.");
        try
        {
            return CronExpression.Parse(cron).GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(zone))?.ToUniversalTime()
                ?? throw new ApiError(400, "The cron expression has no future occurrence.");
        }
        catch (Exception error) when (error is CronFormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new ApiError(400, "Use a valid five-field cron expression and an IANA time zone.");
        }
    }
}

public sealed record JiraMappingInput(string Kind, string KanbadaValue, string JiraValue, bool IsDefault = true);
public sealed record JiraConnectionInput(
    long Version, string BaseUrl, string Edition, string Email, string? Token, string Jql,
    string JiraProjectKey, string IssueTypeId, string Direction, string Cron, string TimeZone,
    bool Enabled, List<JiraMappingInput> Mappings, bool SyncAssignees = false);

public static class JiraConfiguration
{
    public static IServiceCollection AddJira(this IServiceCollection services, IConfiguration configuration, string keyDirectory)
    {
        services.AddSingleton(new JiraSecrets(keyDirectory));
        services.AddScoped<JiraDestinationPolicy>();
        services.AddScoped<JiraSettings>();
        services.AddScoped<JiraSyncEngine>();
        services.AddHttpClient<JiraClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.MaxResponseContentBufferSize = 32 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }

    public static void Validate(JiraConnectionInput input, JiraDestinationPolicy policy)
    {
        if (input.Email is null) throw new ApiError(400, "Provide an email value (empty for a Data Center PAT).");
        if (input.Edition is not ("cloud" or "data-center")) throw new ApiError(400, "Select Jira Cloud or Data Center.");
        policy.Validate(input.BaseUrl, input.Edition);
        if (input.Direction is not ("jira-to-kanbada" or "kanbada-to-jira" or "bidirectional"))
            throw new ApiError(400, "Select a supported synchronization direction.");
        if (string.IsNullOrWhiteSpace(input.Jql) || input.Jql.Length > 10000) throw new ApiError(400, "JQL is required (maximum 10000 characters).");
        if (input.Edition == "cloud" && !MailAddress.TryCreate(input.Email, out _)) throw new ApiError(400, "Jira Cloud requires the account email and an API token.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.JiraProjectKey ?? "", "^[A-Z][A-Z0-9_]{0,79}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(input.IssueTypeId ?? "", "^[0-9]{1,20}$"))
            throw new ApiError(400, "Provide a Jira project key and numeric issue type ID.");
        if (input.Token is { Length: > 4096 } || input.Token?.Any(char.IsControl) == true)
            throw new ApiError(400, "Invalid Jira token.");
        if (input.Mappings is null || input.Mappings.Count > 200
            || input.Mappings.Any(m => m is null || m.Kind is not ("status" or "priority" or "assignee") || string.IsNullOrWhiteSpace(m.KanbadaValue)
                || (m.Kind == "assignee"
                    ? !Guid.TryParse(m.KanbadaValue, out _) || string.IsNullOrWhiteSpace(m.JiraValue)
                        || m.JiraValue.Length > 255 || m.JiraValue != m.JiraValue.Trim() || m.JiraValue.Any(char.IsControl)
                    : !System.Text.RegularExpressions.Regex.IsMatch(m.JiraValue ?? "", "^[0-9]{1,20}$")))
            || input.Mappings.Where(m => m.Kind == "priority").GroupBy(m => m.KanbadaValue).Any(g => g.Count() > 1)
            || input.Mappings.GroupBy(m => (m.Kind, m.JiraValue)).Any(g => g.Count() > 1)
            || input.Mappings.Where(m => m.Kind == "status").GroupBy(m => m.KanbadaValue).Any(g => g.Count(m => m.IsDefault) != 1)
            || !input.Mappings.Any(m => m.Kind == "status")
            || !input.Mappings.Where(m => m.Kind == "priority").Select(m => m.KanbadaValue).Order().SequenceEqual(new[] { "High", "Low", "Medium" }))
            throw new ApiError(400, "Map each Jira ID only once, choose one default Jira status per Kanbada status, map all three priorities, and use valid workspace members for assignees.");
        JiraSchedule.Next(input.Cron, input.TimeZone, DateTimeOffset.UtcNow);
    }
}

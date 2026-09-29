using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kanbada.Api;

public sealed record GitHubSnapshot(string Title, string Body, string Status, string? Priority, DateOnly? Due, string[]? Labels)
{
    public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this with
    {
        Labels = Labels?.Select(l => l.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray()
    }))));

    public static GitHubSnapshot FromCard(GitHubConnectionEntity c, CardEntity card, IEnumerable<string> labels, bool draft) =>
        new(card.Title, Normalize(card.Description), card.StatusId, c.PriorityFieldId.Length == 0 ? null : card.Priority,
            c.DueFieldId.Length == 0 ? null : card.Due, !c.SyncLabels || draft ? null : labels.ToArray());

    public static GitHubSnapshot FromItem(GitHubConnectionEntity c, GitHubItem item)
    {
        if (item.Archived) throw new GitHubSyncException("This GitHub project item is archived. No changes or deletions are propagated.");
        var content = item.Content ?? throw new GitHubSyncException("This GitHub project item is redacted, unsupported or inaccessible. No changes or deletions are propagated.");
        if (content.Title.Length is < 1 or > 500) throw new GitHubSyncException("GitHub titles must be 1 to 500 characters in Kanbada.");
        if (c.SyncLabels && content.Labels?.Any(l => l.Trim().Length is < 1 or > 120) == true)
            throw new GitHubSyncException("GitHub labels must be 1 to 120 characters in Kanbada.");
        DateOnly? due = null;
        if (c.DueFieldId.Length > 0 && item.Fields.TryGetValue(c.DueFieldId, out var date) && date != GitHubClient.Unset)
            due = DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed) ? parsed : throw new GitHubSyncException("GitHub returned an invalid due date.");
        return new(content.Title, Normalize(content.Body), ToKanbada(c, "status", item.Fields.GetValueOrDefault(c.StatusFieldId, GitHubClient.Unset)),
            c.PriorityFieldId.Length == 0 ? null : ToKanbada(c, "priority", item.Fields.GetValueOrDefault(c.PriorityFieldId, GitHubClient.Unset)),
            due, c.SyncLabels ? content.Labels : null);
    }

    public static void ValidateWrite(GitHubConnectionEntity c, GitHubSnapshot snapshot)
    {
        ToGitHub(c, "status", snapshot.Status);
        if (c.PriorityFieldId.Length > 0) ToGitHub(c, "priority", snapshot.Priority!);
        if (snapshot.Title.Length is < 1 or > 256 || snapshot.Body.Length > 65536)
            throw new GitHubSyncException("GitHub writes require a title of at most 256 characters and a description of at most 65,536 characters.");
        if (snapshot.Labels?.Any(l => l.Trim().Length is < 1 or > 50) == true)
            throw new GitHubSyncException("GitHub label names must be 1 to 50 characters.");
    }

    public static string ToGitHub(GitHubConnectionEntity c, string kind, string local) =>
        c.Mappings.SingleOrDefault(m => m.Kind == kind && m.KanbadaValue == local && m.IsDefault)?.GitHubValue
            ?? throw new GitHubSyncException($"Missing {kind} mapping for Kanbada value {local}.");
    private static string ToKanbada(GitHubConnectionEntity c, string kind, string remote) =>
        c.Mappings.SingleOrDefault(m => m.Kind == kind && m.GitHubValue == remote)?.KanbadaValue
            ?? throw new GitHubSyncException($"Missing {kind} mapping for GitHub option {remote}. Map empty values explicitly when needed.");
    private static string Normalize(string value) => value.Replace("\r\n", "\n").TrimEnd('\n');
}

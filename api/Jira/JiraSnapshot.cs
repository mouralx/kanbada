using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

public sealed record JiraSnapshot(string Title, string DescriptionText, string Status, string Priority, DateOnly? Due, string[] Labels)
{
    public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this with
    {
        Labels = Labels.Select(label => label.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray()
    }))));

    public static JiraSnapshot FromCard(CardEntity c, IEnumerable<string> labels) =>
        new(c.Title, Normalize(c.Description), c.StatusId, c.Priority, c.Due, labels.Distinct().Order(StringComparer.Ordinal).ToArray());

    public static JiraSnapshot FromIssue(JiraConnectionEntity c, JiraIssue issue)
    {
        var f = issue.Fields;
        var title = f["summary"]?.GetValue<string>() ?? "";
        if (title.Length is < 1 or > 500) throw new JiraSyncException("Jira issue has an invalid title.");
        var labels = f["labels"]?.AsArray().Select(l => l?.GetValue<string>() ?? "").Distinct().Order(StringComparer.Ordinal).ToArray() ?? [];
        if (labels.Any(l => l.Trim().Length is < 1 or > 120)) throw new JiraSyncException("Jira labels must be 1 to 120 characters in Kanbada.");
        DateOnly? due = null;
        if (f["duedate"] is JsonValue value && value.TryGetValue<string>(out var date) && !string.IsNullOrEmpty(date))
            due = DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed) ? parsed : throw new JiraSyncException("Jira returned an invalid due date.");
        return new(title, Description(f["description"]), ToKanbada(c, "status", f["status"]?["id"]?.GetValue<string>()),
            ToKanbada(c, "priority", f["priority"]?["id"]?.GetValue<string>()), due, labels);
    }

    public static string ToJira(JiraConnectionEntity c, string kind, string value) =>
        c.Mappings.SingleOrDefault(m => m.Kind == kind && m.KanbadaValue == value && (kind != "status" || m.IsDefault))?.JiraValue
            ?? throw new JiraSyncException($"Missing {kind} mapping for Kanbada value {value}.");
    private static string ToKanbada(JiraConnectionEntity c, string kind, string? value) =>
        c.Mappings.SingleOrDefault(m => m.Kind == kind && m.JiraValue == value)?.KanbadaValue
            ?? throw new JiraSyncException($"Missing {kind} mapping for Jira ID {value ?? "(none)"}.");
    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    public static string Description(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return Normalize(text);
        return Normalize(AdfText(node));
    }

    private static string AdfText(JsonNode? node)
    {
        if (node is not JsonObject obj) return "";
        var kind = obj["type"]?.GetValue<string>();
        if (kind == "text") return obj["text"]?.GetValue<string>() ?? "";
        if (kind == "hardBreak") return "\n";
        if (kind == "mention") return obj["attrs"]?["text"]?.GetValue<string>() ?? "@mention";
        if (kind == "inlineCard") return obj["attrs"]?["url"]?.GetValue<string>() ?? "";
        if (kind == "emoji") return obj["attrs"]?["text"]?.GetValue<string>() ?? obj["attrs"]?["shortName"]?.GetValue<string>() ?? "";
        if (kind is "media" or "mediaInline") return "[Jira media]";
        var content = string.Concat(obj["content"]?.AsArray().Select(AdfText) ?? []);
        return content + (kind is "paragraph" or "heading" or "codeBlock" ? "\n" : "");
    }

    public static string Winner(string direction, string origin, string localHash, string remoteHash, string? previousLocal, string? previousRemote)
        => CardSynchronization.Winner("jira", direction, origin, localHash, remoteHash, previousLocal, previousRemote);
}

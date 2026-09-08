using System.Text.Json.Nodes;

namespace Kanbada.Api;

public static class WorkspaceJson
{
    public static string Text(JsonNode? node, string key, string fallback = "") => node?[key]?.GetValue<string>() ?? fallback;
    public static JsonArray Items(JsonNode? node, string key) => node?[key] as JsonArray ?? new JsonArray();
}

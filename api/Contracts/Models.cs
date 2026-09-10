using System.Text.Json.Nodes;

namespace Kanbada.Api;

public record Credentials(string Email, string Password, string? Name, string? Code = null);
public record WorkspaceInput(string Name);
public record ShareInput(string Access, int Days);
public static class Initial
{
    public static JsonObject State(string id, string name, Guid user, string memberName, string email, bool personal) => new()
    {
        ["workspace"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["personal"] = personal,
            ["ownerId"] = user.ToString()
        },
        ["members"] = new JsonArray(Member(user, memberName, email)),
        ["projects"] = new JsonArray(new JsonObject { ["id"] = "my-activities", ["name"] = "My activities", ["color"] = "#aac3e2", ["description"] = "Your everyday work, ideas, and personal to-dos.", ["system"] = "activities", ["archived"] = false }),
        ["tasks"] = new JsonArray(),
        ["buckets"] = new JsonArray(),
        ["labels"] = new JsonArray(),
        ["swimlanes"] = new JsonArray(),
        ["activity"] = new JsonArray(),
        ["notifications"] = new JsonArray(),
        ["statuses"] = new JsonArray(Status("backlog", "Backlog", "#a5acb5", false), Status("progress", "In progress", "#c8a454", false), Status("review", "In review", "#ad9bc8", false), Status("done", "Done", "#829cbd", true))
    };
    public static JsonObject Member(Guid user, string name, string email) => new()
    {
        ["userId"] = user.ToString(),
        ["name"] = name,
        ["email"] = email,
        ["initials"] = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0]))),
        ["color"] = "#bec9d8"
    };
    static JsonObject Status(string id, string name, string color, bool complete) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["color"] = color,
        ["complete"] = complete
    };
}

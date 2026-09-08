namespace Kanbada.Api;

public static class CardEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/workspaces/{id}/cards", async (
            string id, CreateCardRequest input, HttpContext context, WorkspaceResolver database, CardService cards) =>
        {
            var user = Auth.User(context);
            var workspaceId = await database.WorkspaceId(id, user);
            var created = await cards.Create(workspaceId, user, input);
            context.Response.Headers.ETag = $"\"{created.WorkspaceVersion}\"";
            var location = $"/api/workspaces/{Uri.EscapeDataString(id)}/cards/{WorkspaceJson.Text(created.Card, "id")}";
            return Results.Created(location, created.Card);
        })
        .WithTags("Cards")
        .WithName("CreateCard")
        .Produces<System.Text.Json.Nodes.JsonObject>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}

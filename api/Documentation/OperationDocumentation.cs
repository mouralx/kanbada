using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

/// <summary>Adds usable examples and transport semantics without coupling documentation to handlers.</summary>
public static class OperationDocumentation
{
    public static Task Transform(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var method = context.Description.HttpMethod!;
        var path = "/" + context.Description.RelativePath!.Split('?')[0];
        path = Regex.Replace(path, @"\{([^}:]+):[^}]+\}", "{$1}");
        var example = ApiOperationCatalog.Operations.SingleOrDefault(item => item.Method == method && item.Path == path)
            ?? throw new InvalidOperationException($"Add API documentation for {method} {path}.");
        var mutation = method is "POST" or "PUT" or "PATCH" or "DELETE";
        var workspaceVersion = ((path == "/api/workspaces/{id}" || path == "/api/workspaces/{id}/changes") && method is "PUT" or "PATCH")
            || (path.EndsWith("/notification-feed") && method == "DELETE");
        operation.Summary = example.Summary;
        operation.OperationId ??= method.ToLowerInvariant() + Regex.Replace(path, "[^a-zA-Z0-9]", "_");
        operation.Description = example.Description + "\n\n### Example request\n```http\n" + method + " " + example.ExampleUrl
            + (mutation ? "\nX-Kanbada-Request: 1" : "")
            + (workspaceVersion ? "\nIf-Match: 1" : "")
            + "\n```\n"
            + (example.Request is null ? "This request has no body. Use the path/query examples above." : "Use the request-body examples below. Values are illustrative; replace IDs, tokens, and identities with values returned by your API.");

        operation.Parameters ??= [];
        if (mutation)
            operation.Parameters.Add(Parameter("X-Kanbada-Request", ParameterLocation.Header, "Required same-origin mutation marker; not an authentication token.", "1", required: true));
        if (workspaceVersion)
            operation.Parameters.Add(Parameter("If-Match", ParameterLocation.Header, "Version from the last workspace GET. Missing=428; stale=409. Reconcile changes instead of blindly retrying.", "1", required: true));

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            var (value, description) = ParameterExample(parameter.Name!, path, method);
            parameter.Example = JsonValue.Create(value);
            parameter.Description = description;
        }

        AddRequestExamples(operation, example);
        AddResponses(operation, example);
        return Task.CompletedTask;
    }

    private static OpenApiParameter Parameter(string name, ParameterLocation location, string description, string example, bool required) => new()
    {
        Name = name,
        In = location,
        Description = description,
        Required = required,
        Example = JsonValue.Create(example),
        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create(example) }
    };

    private static (string Value, string Description) ParameterExample(string name, string path, string method) => name.ToLowerInvariant() switch
    {
        "id" when path.Contains("/files/") => (ApiExamples.FileId, "File UUID from upload metadata; must be available to this account/card link."),
        "id" when method == "DELETE" => (ApiExamples.WorkspaceId, "Non-personal workspace UUID from GET /api/workspaces. Personal workspaces cannot be deleted."),
        "id" => ("studio", "Workspace UUID, or studio for the signed-in user's personal workspace."),
        "cardid" => (ApiExamples.CardId, "Card identifier returned by Create card. Case-insensitive lookup; stored IDs are uppercase."),
        "token" => (ApiExamples.Token, "Illustrative token only. Replace with a real, unexpired sharing or invitation token returned by the API."),
        "provider" => ("google", "External provider: google or microsoft. Both client ID and client secret must be configured."),
        "returnurl" => ("/", "Local portal path after sign-in. For example /?workspace=studio&card=KB-A1B2C3D4. External destinations are rejected in favor of /."),
        "project" => ("my-activities", "Optional exact project ID. Omit for all active projects."),
        "projectid" => ("my-activities", "Exact Kanbada project ID in this workspace."),
        "linkid" => ("73716be0-a56b-4589-a5f6-2bf96a531132", "Pending synchronization link UUID from connection.problems, not an external issue ID."),
        "login" => ("octocat", "GitHub username to resolve to a stable user node ID for assignee mappings."),
        "exportid" => ("73716be0-a56b-4589-a5f6-2bf96a531132", "Export UUID from the queue response or your private export history."),
        "bucket" => ("Discovery", "Optional exact stored bucket name. Omit for all buckets."),
        "swimlane" => (path.EndsWith("/metrics") ? "Research" : "lane-research", "For paged cards/summary: swimlane ID, empty for no swimlane, omitted for all. Legacy metrics uses the stored name."),
        "metadataonly" => ("true", "Return workspace metadata without loading cards. Omit for the legacy full snapshot."),
        "notificationid" => ("notice-1", "Notification ID to dismiss; omit to clear all visible notifications."),
        "limit" => ("40", "Page size from 1 to 100; defaults to 40."),
        "after" => ("", "Omit on the first request. Otherwise use nextCursor from the preceding page with unchanged filters."),
        "mine" => ("true", "Only cards assigned to the current account in active projects."),
        "active" => ("true", "Exclude archived projects."),
        "search" => ("design", "Case-insensitive search of card titles, IDs and labels."),
        "priority" => ("High", "Exact priority: Low, Medium or High."),
        "person" => ("Alex Morgan", "Exact assignee display name."),
        "status" => ("Backlog", "Exact stored status name."),
        "completion" => ("Open", "Open, Completed or Overdue; omit for all."),
        "from" => ("2026-09-01", "Inclusive due-date lower bound (yyyy-MM-dd)."),
        "to" => ("2026-09-30", "Inclusive due-date upper bound (yyyy-MM-dd)."),
        "today" => ("2026-09-29", "Caller-local date for overdue and seven-day metrics; defaults to UTC today."),
        "unassigned" => ("true", "Only cards with no assignees."),
        "collection" => ("tasks", "One of projects, tasks, members, statuses, buckets, labels, swimlanes, notifications, activity. Responses below include examples of every collection."),
        "if-match" => ("1", "Version from your latest workspace GET (not a fixed constant). Missing=428; stale=409."),
        "x-kanbada-request" => ("1", "Required custom header on every mutation. Cookies provide authentication."),
        _ => throw new InvalidOperationException("Add a parameter example for " + name)
    };

    private static void AddRequestExamples(OpenApiOperation operation, ApiOperationExample example)
    {
        if (example.Request is null) return;
        var requestBody = new OpenApiRequestBody { Required = true, Content = operation.RequestBody?.Content ?? new Dictionary<string, OpenApiMediaType>() };
        operation.RequestBody = requestBody;
        if (example.Path.EndsWith("/files") && example.Method == "POST")
        {
            requestBody.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "file" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary", Description = "Select a local document, at most 25 MiB." }
                        }
                    },
                    Example = example.Request.DeepClone()
                }
            };
            return;
        }
        foreach (var media in requestBody.Content.Values)
        {
            media.Example = example.Request.DeepClone();
            if (example.Method == "POST" && example.Path.EndsWith("/cards"))
            {
                var detailed = ApiExamples.Card();
                detailed.Remove("id");
                detailed.Remove("history");
                media.Example = null;
                media.Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["minimal"] = new OpenApiExample { Summary = "Create with only a title", Value = example.Request.DeepClone() },
                    ["detailed"] = new OpenApiExample { Summary = "Create with workflow, assignment, and checklist", Description = "Use names already defined in your workspace. Upload files first if adding attachments.", Value = detailed }
                };
            }
        }
    }

    private static void AddResponses(OpenApiOperation operation, ApiOperationExample example)
    {
        var previous = operation.Responses?.GetValueOrDefault(example.SuccessCode);
        operation.Responses ??= new OpenApiResponses();
        var response = new OpenApiResponse
        {
            Description = example.SuccessCode switch
            {
                "201" => "Created. Location points to the new card; ETag contains the current workspace version.",
                "204" => "Success. The response has no body.",
                "302" => "Browser redirect. Follow Location through the authentication flow.",
                _ => "Success. Example values illustrate the returned contract."
            }
        };
        if (example.Response is not null)
        {
            var media = new OpenApiMediaType
            {
                Schema = previous?.Content?.FirstOrDefault(item => item.Key == example.ResponseType).Value?.Schema,
                Example = example.Response.DeepClone()
            };
            response.Content = new Dictionary<string, OpenApiMediaType> { [example.ResponseType] = media };
            if (example.Path.EndsWith("/{collection}"))
            {
                media.Example = null;
                media.Examples = new Dictionary<string, IOpenApiExample>();
                foreach (var field in new[] { "projects", "tasks", "members", "statuses", "buckets", "labels", "swimlanes", "notifications", "activity" })
                    media.Examples[field] = new OpenApiExample { Summary = field + " collection", Value = ApiExamples.Workspace()[field]!.DeepClone() };
            }
        }
        if (example.SuccessCode is "201" or "302")
            response.Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Location"] = new OpenApiHeader { Description = example.SuccessCode == "201" ? "/api/workspaces/studio/cards/KB-A1B2C3D4 (actual generated ID varies)" : "Provider authorization URL or validated local returnUrl.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } }
            };
        if ((example.Path == "/api/workspaces/{id}" && example.Method is "GET" or "PATCH") || example.SuccessCode == "201")
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["ETag"] = new OpenApiHeader { Description = "Workspace version, e.g. \"2\". Use its value for subsequent workspace PATCH requests.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        }
        if (example.Path == "/api/workspaces/{id}" && example.Method == "GET")
        {
            (operation.Parameters ??= []).Add(Parameter("If-None-Match", ParameterLocation.Header, "Optional quoted workspace version. Matching versions return 304 with no body.", "\"1\"", false));
            operation.Responses["304"] = new OpenApiResponse { Description = "Workspace unchanged. No response body; retain your cached state." };
        }
        operation.Responses[example.SuccessCode] = response;
        if (example.SuccessCode != "200") operation.Responses.Remove("200");

        if (example.Path == "/api/auth/login")
            operation.Responses["200"] = new OpenApiResponse
            {
                Description = "Password accepted; a second factor is required. No session is issued. Resubmit credentials with code.",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Example = ApiExamples.Json(new { twoFactorRequired = true }) } }
            };
        AddProblem(operation, 500, "Unexpected server failure. Contact support with the traceId.");
        if (example.Path.StartsWith("/api/health"))
        {
            if (example.Path.EndsWith("/ready"))
                operation.Responses["503"] = new OpenApiResponse
                {
                    Description = "PostgreSQL health check failed.",
                    Content = new Dictionary<string, OpenApiMediaType> { ["text/plain"] = new() { Example = JsonValue.Create("Unhealthy") } }
                };
            return;
        }
        AddProblem(operation, 400, "Invalid request data or a protected workspace/card invariant was violated.");
        if (!example.Path.StartsWith("/api/auth") || example.Path.EndsWith("/login") || example.Path.EndsWith("/complete") || example.Path.StartsWith("/api/auth/two-factor") || (example.Path == "/api/auth/change-password" || example.Path.StartsWith("/api/auth/avatar")))
            AddProblem(operation, 401, "Sign in on this origin before requesting this resource.");
        AddProblem(operation, 403, "Request origin rejected, or this account does not have the required permission.");
        if (!example.Path.StartsWith("/api/auth")) AddProblem(operation, 404, "Resource not found, inaccessible, expired, or revoked.");
        if (example.Method is "POST" or "PUT" or "PATCH") AddProblem(operation, 409, "Conflict: duplicate resource, existing membership, or a competing workspace save. Reload and reconcile.");
        if (example.Path == "/api/workspaces/{id}" && example.Method is "PUT" or "PATCH") AddProblem(operation, 428, "An If-Match workspace version is required.");
        if (example.Path.EndsWith("/jira/test"))
        {
            AddProblem(operation, 429, "Jira connection test request limit reached.");
            AddProblem(operation, 502, "Jira connectivity or TLS failure.");
            AddProblem(operation, 504, "Jira request timed out.");
        }
        if (example.Path is "/api/auth/register" or "/api/auth/login" or "/api/auth/{provider}/start" || example.Path.StartsWith("/api/auth/two-factor") || (example.Path == "/api/auth/change-password" || example.Path.StartsWith("/api/auth/avatar")))
            AddProblem(operation, 429, "Authentication request limit reached. Wait before trying again.");
        if (example.Path == "/api/auth/{provider}/start") AddProblem(operation, 503, "This sign-in provider has not been configured.");
    }

    private static void AddProblem(OpenApiOperation operation, int status, string detail)
    {
        operation.Responses![status.ToString()] = new OpenApiResponse
        {
            Description = detail,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new()
                {
                    Example = ApiExamples.Json(new { title = status >= 500 ? "Server error" : "Request failed", status, detail, instance = "/api/workspaces/studio", traceId = "example-request-trace-id" })
                }
            }
        };
    }
}

using System.Text.Json.Nodes;
using Kanbada.Api;
using Xunit;

public sealed class DocumentationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task EveryOperation_HasDescriptionsParameterExamplesPayloadsAndResponses()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/openapi.json");
        response.EnsureSuccessStatusCode();
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("/", document["servers"]![0]!["url"]!.ToString());
        var count = 0;
        foreach (var path in document["paths"]!.AsObject())
        {
            foreach (var operation in path.Value!.AsObject())
            {
                count++;
                var value = operation.Value!;
                Assert.False(string.IsNullOrWhiteSpace(value["summary"]?.ToString()), path.Key);
                Assert.Contains("Example request", value["description"]!.ToString());
                foreach (var parameter in value["parameters"]?.AsArray() ?? [])
                {
                    Assert.NotNull(parameter!["example"]);
                    Assert.False(string.IsNullOrWhiteSpace(parameter["description"]?.ToString()));
                }
                if (value["requestBody"] is { } body)
                    foreach (var media in body["content"]!.AsObject())
                        Assert.True(media.Value!["example"] is not null || media.Value["examples"]?.AsObject().Count > 0, path.Key);
                foreach (var result in value["responses"]!.AsObject())
                {
                    Assert.False(string.IsNullOrWhiteSpace(result.Value!["description"]?.ToString()));
                    if (result.Value["content"] is { } content)
                        foreach (var media in content.AsObject())
                            Assert.True(media.Value!["example"] is not null || media.Value["examples"]?.AsObject().Count > 0, path.Key + " " + result.Key);
                }
            }
        }
        Assert.Equal(ApiOperationCatalog.Operations.Count, count);
        var create = document["paths"]!["/api/workspaces/{id}/cards"]!["post"]!;
        Assert.Equal("Create a card", create["summary"]!.ToString());
        Assert.NotNull(create["requestBody"]!["content"]!["application/json"]!["examples"]!["minimal"]);
        Assert.NotNull(create["responses"]!["201"]);
        var upload = document["paths"]!["/api/workspaces/{id}/files"]!["post"]!;
        Assert.NotNull(upload["requestBody"]!["content"]!["multipart/form-data"]);
    }
}

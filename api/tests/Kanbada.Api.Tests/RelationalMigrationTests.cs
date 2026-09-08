using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Text.Json.Nodes;
using Xunit;

public sealed class RelationalMigrationTests
{
    [Fact]
    public async Task ExistingDocumentsMigrateWithoutLosingBusinessDataOrCredentials()
    {
        var fixture = new ApiFixture();
        await fixture.InitializeAsync();
        try
        {
            var user = Guid.NewGuid();
            var workspace = Guid.NewGuid();
            var file = Guid.NewGuid();
            var state = Initial.State("studio", "My Workspace", user, "Owner", "owner@example.test", true);
            state["workspace"]!["icon"] = "data:image/png;base64,AQID";
            state["workspace"]!["banner"] = "data:image/png;base64,BAUG";
            state["workspace"]!["bannerPosition"] = 32;
            state["members"]![0]!["photo"] = "data:image/png;base64,BwgJ";
            state["members"]!.AsArray().Add(new JsonObject { ["email"] = "invite@example.test", ["name"] = "Invited", ["initials"] = "I", ["color"] = "#123456" });
            foreach (var (key, name) in new[] { ("buckets", "Operations"), ("labels", "Customer"), ("swimlanes", "Urgent") })
            {
                var definition = new JsonObject { ["id"] = key + "-id", ["name"] = name, ["color"] = "#123456", ["complete"] = false };
                if (key == "swimlanes") definition["project"] = "my-activities";
                state[key]!.AsArray().Add(definition);
            }
            state["projects"]!.AsArray().Add(new JsonObject { ["id"] = "archived", ["name"] = "Archived", ["color"] = "#123456", ["description"] = "Retained", ["archived"] = true });
            state["activity"]!.AsArray().Add("Existing activity");
            state["notifications"]!.AsArray().Add(new JsonObject { ["id"] = "n1", ["message"] = "Existing notification", ["at"] = "Just now" });
            var card = JsonNode.Parse("""
                {"id":"KB-MIGRATED","project":"my-activities","title":"Keep this card","description":"Descrição","status":"Backlog","priority":"High","due":"","bucket":"Operations","swimlane":"Urgent","cover":"#123456","labels":["Customer"],"assignees":["Owner","Invited"],"comments":["First","Second"],"checklist":[{"text":"One","done":true},{"text":"Two","done":false}],"history":[{"id":"h1","at":"2026-09-01T10:00:00Z","actor":"Owner","changes":["Created card","Added file"]}]}
                """)!.AsObject();
            card["attachments"] = new JsonArray(new JsonObject { ["id"] = file.ToString(), ["name"] = "brief.pdf", ["size"] = 3, ["type"] = "application/pdf", ["addedAt"] = "2026-09-01T10:00:00Z" });
            state["tasks"]!.AsArray().Add(card);
            await using (var connection = new NpgsqlConnection(fixture.Connection))
            {
                await connection.OpenAsync();
                using var stream = typeof(Program).Assembly.GetManifestResourceStream("Kanbada.Api.Persistence.Migrations.001_initial.sql")!;
                using var reader = new StreamReader(stream);
                await using (var initial = new NpgsqlCommand(await reader.ReadToEndAsync(), connection)) await initial.ExecuteNonQueryAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO users(id,email,name,password_hash) VALUES(@user,'owner@example.test','Owner','unchanged-password-hash');
                    INSERT INTO identities VALUES('google','provider-subject',@user);
                    INSERT INTO sessions VALUES('session-hash',@user,now()+interval '1 day');
                    INSERT INTO workspaces(id,owner_id,personal,state,version) VALUES(@workspace,@user,true,@state::jsonb,17);
                    INSERT INTO members VALUES(@workspace,'owner@example.test',@user,NULL),(@workspace,'invite@example.test',NULL,'pending-invitation');
                    INSERT INTO files(id,workspace_id,uploader_id,name,content_type,bytes) VALUES(@file,@workspace,@user,'brief.pdf','application/pdf',@bytes);
                    INSERT INTO shares(token,workspace_id,card_id,creator_id,access) VALUES('existing-share',@workspace,'KB-MIGRATED',@user,'signed-in');
                    """, connection);
                insert.Parameters.AddWithValue("user", user);
                insert.Parameters.AddWithValue("workspace", workspace);
                insert.Parameters.AddWithValue("file", file);
                insert.Parameters.AddWithValue("state", state.ToJsonString());
                insert.Parameters.AddWithValue("bytes", new byte[] { 1, 2, 3 });
                await insert.ExecuteNonQueryAsync();
            }
            using var client = fixture.Client(); // Startup applies the real EF migration over populated v1 tables.
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var migrated = (await scope.ServiceProvider.GetRequiredService<WorkspaceStore>().Read(workspace, user)).State;
            Assert.Equal(17L, migrated["version"]!.GetValue<long>());
            Assert.Equal("Keep this card", migrated["tasks"]![0]!["title"]!.ToString());
            Assert.Equal("Descrição", migrated["tasks"]![0]!["description"]!.ToString());
            Assert.Equal("", migrated["tasks"]![0]!["due"]!.ToString());
            foreach (var key in new[] { "labels", "assignees", "comments", "checklist", "bucket", "swimlane", "cover" })
                Assert.True(JsonNode.DeepEquals(card[key], migrated["tasks"]![0]![key]), key);
            Assert.True(JsonNode.DeepEquals(card["history"]![0]!["changes"], migrated["tasks"]![0]!["history"]![0]!["changes"]));
            Assert.Equal("pending-invitation", migrated["members"]![1]!["invitationToken"]!.ToString());
            Assert.Equal(32, migrated["workspace"]!["bannerPosition"]!.GetValue<double>());
            Assert.Equal(state["workspace"]!["banner"]!.ToString(), migrated["workspace"]!["banner"]!.ToString());
            Assert.True(migrated["projects"]![1]!["archived"]!.GetValue<bool>());
            Assert.True(JsonNode.DeepEquals(state["notifications"], migrated["notifications"]));
            Assert.True(JsonNode.DeepEquals(state["activity"], migrated["activity"]));
            Assert.Equal("unchanged-password-hash", (await db.Users.SingleAsync()).PasswordHash);
            Assert.Equal("provider-subject", (await db.Identities.SingleAsync()).Subject);
            Assert.Equal("session-hash", (await db.Sessions.SingleAsync()).Id);
            Assert.Equal(new byte[] { 1, 2, 3 }, (await db.Files.SingleAsync()).Bytes);
            Assert.Equal("existing-share", (await db.Shares.SingleAsync()).Token);
            Assert.Equal("brief.pdf", migrated["tasks"]![0]!["attachments"]![0]!["name"]!.ToString());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            await db.Database.MigrateAsync(); // Repeated startup is safe.
            await using var check = new NpgsqlConnection(fixture.Connection);
            await check.OpenAsync();
            await using var columns = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND data_type IN ('json','jsonb')", check);
            Assert.Equal(0L, await columns.ExecuteScalarAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task InvalidLegacyRelationshipsRollBackTheEntireMigration()
    {
        var fixture = new ApiFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var connection = new NpgsqlConnection(fixture.Connection);
            await connection.OpenAsync();
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("Kanbada.Api.Persistence.Migrations.001_initial.sql")!;
            using var reader = new StreamReader(stream);
            await using (var initial = new NpgsqlCommand(await reader.ReadToEndAsync(), connection)) await initial.ExecuteNonQueryAsync();
            var user = Guid.NewGuid();
            var state = Initial.State("studio", "My Workspace", user, "Owner", "owner@example.test", true);
            await using (var insert = new NpgsqlCommand("INSERT INTO users(id,email,name) VALUES(@id,'owner@example.test','Owner'); INSERT INTO workspaces(id,owner_id,personal,state) VALUES(@id,@id,true,@state::jsonb)", connection))
            {
                insert.Parameters.AddWithValue("id", user);
                insert.Parameters.AddWithValue("state", state.ToJsonString());
                await insert.ExecuteNonQueryAsync(); // The JSON member intentionally has no relational authorization row.
            }
            await using var db = new KanbadaDbContext(new DbContextOptionsBuilder<KanbadaDbContext>().UseNpgsql(fixture.Connection).Options);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
            await using var verify = new NpgsqlCommand("SELECT state::text FROM workspaces WHERE id=@id", connection);
            verify.Parameters.AddWithValue("id", user);
            Assert.True(JsonNode.DeepEquals(state, JsonNode.Parse((string)(await verify.ExecuteScalarAsync())!)));
        }
        finally { await fixture.DisposeAsync(); }
    }
}

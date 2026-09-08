using System.Text.Json.Nodes;

namespace Kanbada.Api;

public sealed record StateChange(string Op, string Path, JsonNode? Value = null);
public sealed record ChangeRequest(StateChange[] Changes);
public sealed record ChangeResult(long Version, IReadOnlyList<StateChange> Changes);

/// <summary>A bounded add/remove/replace change format using JSON Pointer paths. No scripts or reflection.</summary>
public static class StateChanges
{
    private static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
    private static JsonNode? Identity(JsonNode? value) => value is JsonObject item ? item["id"] ?? item["email"] ?? value : value;

    public static List<StateChange> Diff(JsonNode? before, JsonNode? after, string path = "")
    {
        if (JsonNode.DeepEquals(before, after)) return [];
        var result = new List<StateChange>();
        if (before is JsonObject a && after is JsonObject b)
        {
            foreach (var key in a.Select(x => x.Key).Except(b.Select(x => x.Key))) result.Add(new("remove", path + "/" + Escape(key)));
            foreach (var (key, value) in b)
                if (a.ContainsKey(key)) result.AddRange(Diff(a[key], value, path + "/" + Escape(key)));
                else result.Add(new("add", path + "/" + Escape(key), value?.DeepClone()));
        }
        else if (before is JsonArray source && after is JsonArray target)
        {
            if (target.Count == 0) return [new("replace", path, new JsonArray())];
            var work = source.ToList();
            for (var i = 0; i < target.Count; i++)
            {
                if (i < work.Count && JsonNode.DeepEquals(Identity(work[i]), Identity(target[i])))
                    result.AddRange(Diff(work[i], target[i], path + "/" + i));
                else
                {
                    var found = work.FindIndex(i < work.Count ? i : work.Count, x => JsonNode.DeepEquals(Identity(x), Identity(target[i])));
                    if (found >= 0)
                    {
                        for (var n = i; n < found; n++) result.Add(new("remove", path + "/" + i));
                        work.RemoveRange(i, found - i);
                        result.AddRange(Diff(work[i], target[i], path + "/" + i));
                    }
                    else
                    {
                        result.Add(new("add", path + "/" + i, target[i]?.DeepClone()));
                        work.Insert(i, target[i]);
                    }
                }
            }
            for (var i = work.Count - 1; i >= target.Count; i--) result.Add(new("remove", path + "/" + i));
        }
        else result.Add(new("replace", path, after?.DeepClone()));
        return result;
    }

    public static JsonObject Apply(JsonObject source, StateChange[] changes)
    {
        if (changes is null || changes.Length is < 1 or > 10000) throw new ApiError(400, "Send between 1 and 10000 changes.");
        var result = source.DeepClone().AsObject();
        foreach (var change in changes)
        {
            if (change is null || change.Op is not ("add" or "remove" or "replace") || change.Path is null || !change.Path.StartsWith('/') || change.Path.Length > 1000)
                throw new ApiError(400, "Invalid change operation or path.");
            var keys = change.Path[1..].Split('/').Select(x => x.Replace("~1", "/").Replace("~0", "~")).ToArray();
            if (keys.Any(x => x is "__proto__" or "constructor" or "prototype") || keys[0] is not ("tasks" or "projects" or "members" or "statuses" or "buckets" or "labels" or "swimlanes" or "activity" or "notifications" or "workspace"))
                throw new ApiError(400, "This path is not editable.");
            if (keys[0] == "workspace" && (keys.Length != 2 || keys[1] is not ("name" or "icon" or "banner" or "bannerPosition")))
                throw new ApiError(400, "This workspace field is server-controlled.");
            if (keys.Length >= 3 && ((keys[0] == "tasks" && keys[2] == "history") || (keys[0] == "members" && keys[2] is "userId" or "invitationToken")))
                throw new ApiError(400, "This field is server-controlled.");
            JsonNode? parent = result;
            foreach (var key in keys[..^1]) parent = Child(parent, key);
            var last = keys[^1];
            if (parent is JsonObject obj)
            {
                if (change.Op != "add" && !obj.ContainsKey(last)) throw new ApiError(400, "Change target does not exist.");
                if (change.Op == "remove") obj.Remove(last);
                else obj[last] = change.Value?.DeepClone();
            }
            else if (parent is JsonArray array)
            {
                var index = Index(last, array.Count, change.Op == "add");
                if (change.Op == "add") array.Insert(index, change.Value?.DeepClone());
                else if (change.Op == "remove") array.RemoveAt(index);
                else array[index] = change.Value?.DeepClone();
            }
            else throw new ApiError(400, "Change parent does not exist.");
        }
        return result;
    }

    private static JsonNode? Child(JsonNode? parent, string key) => parent switch
    {
        JsonObject obj when obj.ContainsKey(key) => obj[key],
        JsonArray array => array[Index(key, array.Count, false)],
        _ => throw new ApiError(400, "Change path does not exist.")
    };
    private static int Index(string key, int count, bool adding)
    {
        if (!int.TryParse(key, out var index) || index < 0 || index >= count + (adding ? 1 : 0) || index.ToString() != key)
            throw new ApiError(400, "Invalid array position.");
        return index;
    }
}

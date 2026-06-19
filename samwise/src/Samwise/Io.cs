using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

// Shared file/JSON helpers used by all renderers. Kept in a static class (not
// top-level local functions) so renderer classes can call them.
static class Io
{
    static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // ---- file copy --------------------------------------------------------

    public static void CopyTree(string src, string dst, bool force, bool dryRun, ChangeLog log, string label)
    {
        foreach (string srcFile in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, srcFile);
            CopyFile(srcFile, Path.Combine(dst, rel), force, dryRun, log, $"{label}/{rel.Replace('\\', '/')}");
        }
    }

    public static void CopyFile(string srcFile, string dstFile, bool force, bool dryRun, ChangeLog log, string shown)
    {
        if (File.Exists(dstFile) && !force)
        {
            log.Skipped(shown, "already exists");
            return;
        }
        bool existed = File.Exists(dstFile);
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
            File.Copy(srcFile, dstFile, overwrite: true);
        }
        if (existed) log.Overwritten(shown); else log.Created(shown);
    }

    // ---- plain text write (create-or-skip; --force overwrites) ------------

    public static void WriteText(string path, string content, bool force, bool dryRun, ChangeLog log, string shown)
    {
        if (File.Exists(path) && !force)
        {
            log.Skipped(shown, "already exists");
            return;
        }
        bool existed = File.Exists(path);
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        if (existed) log.Overwritten(shown); else log.Created(shown);
    }

    // ---- managed-block upsert (for AGENTS.md / copilot-instructions.md) ---
    // Idempotently maintains a Samwise-owned block between markers, leaving any
    // surrounding user content intact.

    public static void UpsertManagedBlock(string path, string blockBody, bool dryRun, ChangeLog log, string shown)
    {
        const string begin = "<!-- samwise:begin (managed - edits between these markers are overwritten) -->";
        const string end = "<!-- samwise:end -->";
        string block = begin + "\n" + blockBody.TrimEnd() + "\n" + end + "\n";

        string existing = File.Exists(path) ? File.ReadAllText(path) : "";
        string updated;
        bool changed;

        int b = existing.IndexOf(begin, StringComparison.Ordinal);
        int e = existing.IndexOf(end, StringComparison.Ordinal);
        if (b >= 0 && e > b)
        {
            string before = existing[..b];
            string after = existing[(e + end.Length)..].TrimStart('\n');
            updated = before + block + after;
            changed = updated != existing;
        }
        else if (existing.Length > 0)
        {
            updated = existing.TrimEnd() + "\n\n" + block;
            changed = true;
        }
        else
        {
            updated = block;
            changed = true;
        }

        if (!changed) { log.Skipped(shown, "already up to date"); return; }
        bool existed = File.Exists(path);
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, updated, new UTF8Encoding(false));
        }
        if (existed) log.Overwritten(shown); else log.Created(shown);
    }

    // ---- JSON ------------------------------------------------------------

    public static JsonObject LoadObject(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        string text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        var docOpts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(text, documentOptions: docOpts) as JsonObject
               ?? throw new InvalidOperationException($"{path} is not a JSON object.");
    }

    public static void Save(string path, JsonObject root, bool dryRun)
    {
        if (dryRun) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, root.ToJsonString(PrettyJson), new UTF8Encoding(false));
    }

    // Merge a set of MCP servers into a JSON file under a given top-level key
    // ("mcpServers" for Claude's .mcp.json, "servers" for VS Code's .vscode/mcp.json).
    public static void MergeServersInto(JsonObject incoming, string dstPath, string rootKey,
                                        MergeOptions options, ChangeLog log)
    {
        JsonObject root = LoadObject(dstPath);
        if (root[rootKey] is not JsonObject servers)
        {
            servers = new JsonObject();
            root[rootKey] = servers;
        }

        foreach (var kv in incoming)
        {
            string shown = $"{Path.GetFileName(dstPath)}#{rootKey}/{kv.Key}";
            if (servers.ContainsKey(kv.Key) && !options.Force)
            {
                log.Skipped(shown, "server already configured");
                continue;
            }
            bool existed = servers.ContainsKey(kv.Key);
            servers[kv.Key] = kv.Value!.DeepClone();
            if (existed) log.Overwritten(shown); else log.Created(shown);
        }

        Save(dstPath, root, options.DryRun);
    }

    public static void MergeSettingsObject(JsonObject incoming, string dstPath, MergeOptions options, ChangeLog log)
    {
        JsonObject root = LoadObject(dstPath);
        if (incoming["permissions"] is JsonObject incPerm) MergePermissions(root, incPerm, log);
        if (incoming["hooks"] is JsonObject incHooks) MergeHooks(root, incHooks, options, log);
        Save(dstPath, root, options.DryRun);
    }

    static void MergePermissions(JsonObject root, JsonObject incPerm, ChangeLog log)
    {
        if (root["permissions"] is not JsonObject perm)
        {
            perm = new JsonObject();
            root["permissions"] = perm;
        }

        foreach (var listName in new[] { "allow", "deny", "ask" })
        {
            if (incPerm[listName] is not JsonArray incList) continue;
            var existing = perm[listName] as JsonArray ?? new JsonArray();
            var seen = existing.Select(n => n?.GetValue<string>()).Where(s => s is not null).ToHashSet();
            int added = 0;
            foreach (var item in incList)
            {
                string? v = item?.GetValue<string>();
                if (v is null || seen.Contains(v)) continue;
                existing.Add(v);
                seen.Add(v);
                added++;
            }
            perm[listName] = existing;
            if (added > 0) log.Created($"settings.json#permissions/{listName} (+{added})");
            else log.Skipped($"settings.json#permissions/{listName}", "nothing new");
        }
    }

    static void MergeHooks(JsonObject root, JsonObject incHooks, MergeOptions options, ChangeLog log)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        foreach (var kv in incHooks)
        {
            string eventName = kv.Key;
            string shown = $"settings.json#hooks/{eventName}";

            if (!hooks.ContainsKey(eventName))
            {
                hooks[eventName] = kv.Value!.DeepClone();
                log.Created(shown);
                continue;
            }

            if (hooks[eventName] is JsonArray existingArray && kv.Value is JsonArray incomingArray)
            {
                int added = AppendMissingHookEntries(existingArray, incomingArray);
                if (added > 0) log.Created($"{shown} (+{added})");
                else log.Skipped(shown, "all hook entries already present");
                continue;
            }

            if (options.Force || ShouldReplaceHookEvent(eventName, options))
            {
                hooks[eventName] = kv.Value!.DeepClone();
                log.Overwritten(shown);
                continue;
            }

            log.Skipped(shown, "existing hook event has an incompatible shape; left untouched");
        }
    }

    static int AppendMissingHookEntries(JsonArray existing, JsonArray incoming)
    {
        var seen = existing.Select(CanonicalJson).ToHashSet(StringComparer.Ordinal);
        int added = 0;
        foreach (var item in incoming)
        {
            string fingerprint = CanonicalJson(item);
            if (seen.Contains(fingerprint)) continue;
            existing.Add(item?.DeepClone());
            seen.Add(fingerprint);
            added++;
        }
        return added;
    }

    static bool ShouldReplaceHookEvent(string eventName, MergeOptions options)
    {
        if (options.NoInput || Console.IsInputRedirected) return false;
        Console.Write($"Hook event '{eventName}' already exists with a different JSON shape. Replace it? [y/N] ");
        string? answer = Console.ReadLine();
        return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    static string CanonicalJson(JsonNode? node)
    {
        if (node is null) return "null";
        return node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });
    }
}

sealed record MergeOptions(bool Force, bool DryRun, bool NoInput);

sealed class ChangeLog
{
    private readonly List<string> _created = new();
    private readonly List<string> _overwritten = new();
    private readonly List<(string item, string why)> _skipped = new();

    public void Created(string item) => _created.Add(item);
    public void Overwritten(string item) => _overwritten.Add(item);
    public void Skipped(string item, string why) => _skipped.Add((item, why));

    public void Summarize()
    {
        foreach (var c in _created) Console.WriteLine($"  + {c}");
        foreach (var o in _overwritten) Console.WriteLine($"  ~ {o} (overwritten)");
        foreach (var (item, why) in _skipped) Console.WriteLine($"  = {item} ({why})");
        Console.WriteLine(
            $"\n{_created.Count} added, {_overwritten.Count} overwritten, {_skipped.Count} left as-is.");
    }
}

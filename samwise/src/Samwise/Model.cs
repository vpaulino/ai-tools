using System.Text.Json.Nodes;

// A single skill/playbook in neutral form: parsed once, rendered per platform.
sealed record Skill(
    string DirName,      // folder name, e.g. "migration-strategy"
    string SourceDir,    // absolute path to the skill folder (for Claude's verbatim copy)
    string Name,         // frontmatter "name"
    string Description,  // frontmatter "description"
    string? ApplyTo,     // optional frontmatter "applyTo" glob (Copilot scoping); null => "**"
    string Body);        // markdown after the frontmatter

// The vendor-neutral model that renderers translate into per-platform layouts.
sealed class CoreModel
{
    public required string PayloadRoot { get; init; }
    public required List<Skill> Skills { get; init; }
    public required JsonObject Servers { get; init; } // name -> server object (type/command/args/env or type/url)
}

static class SkillLoader
{
    // Load every skill under <dir>/<name>/SKILL.md.
    public static IEnumerable<Skill> LoadFrom(string skillsRoot)
    {
        if (!Directory.Exists(skillsRoot)) yield break;
        foreach (string dir in Directory.GetDirectories(skillsRoot).OrderBy(x => x, StringComparer.Ordinal))
        {
            string md = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(md)) continue;
            var (front, body) = SplitFrontmatter(File.ReadAllText(md));
            yield return new Skill(
                DirName: Path.GetFileName(dir),
                SourceDir: dir,
                Name: front.GetValueOrDefault("name") ?? Path.GetFileName(dir),
                Description: front.GetValueOrDefault("description") ?? "",
                ApplyTo: front.GetValueOrDefault("applyto"),
                Body: body.Trim());
        }
    }

    // Minimal YAML-frontmatter parser: handles `--- ... ---` with simple
    // `key: value` lines (the only shape our SKILL.md files use).
    static (Dictionary<string, string> front, string body) SplitFrontmatter(string text)
    {
        var front = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) return (front, text);

        int close = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (close < 0) return (front, text);

        string yaml = text[4..close];
        string body = text[(close + 4)..].TrimStart('\n');

        foreach (string line in yaml.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0) front[key] = value;
        }
        return (front, body);
    }
}

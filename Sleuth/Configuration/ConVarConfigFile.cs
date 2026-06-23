using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Objects;

namespace Sleuth.Configuration;

/// <summary>
/// Generates and loads an editable ConVar config file under <c>sharp/configs/&lt;file&gt;</c>,
/// mirroring the NukoLevelRank style: a header plus, for each ConVar, a <c>// help</c> comment
/// line followed by <c>convar_name value</c>. On first run the file is written from the ConVar
/// defaults; on every run the file's values are applied back onto the ConVars so admin edits
/// persist across restarts.
/// </summary>
internal static class ConVarConfigFile
{
    public static void Sync(string sharpPath, string fileName, string title, ILogger logger, IReadOnlyList<IConVar> cvars)
    {
        var path = Path.Combine(sharpPath, "configs", fileName);

        try
        {
            if (!File.Exists(path))
                WriteDefault(path, title, cvars, logger);

            var loaded = LoadInto(path, cvars);
            logger.LogInformation("[{Title}] Loaded {Count} value(s) from {Path}", title, loaded, path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Title}] Failed to sync config {Path} — using defaults", title, path);
        }
    }

    private static void WriteDefault(string path, string title, IReadOnlyList<IConVar> cvars, ILogger logger)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("// ").Append(title).Append(" Configuration\n");
        sb.Append("// Auto-generated - edit values as needed\n\n");

        foreach (var cv in cvars)
        {
            if (cv is null)
                continue;
            if (!string.IsNullOrWhiteSpace(cv.HelpString))
                sb.Append("// ").Append(cv.HelpString).Append('\n');
            sb.Append(cv.Name).Append(' ').Append(Quote(cv.GetString())).Append("\n\n");
        }

        File.WriteAllText(path, sb.ToString());
        logger.LogInformation("[{Title}] Wrote default config to {Path}", title, path);
    }

    private static int LoadInto(string path, IReadOnlyList<IConVar> cvars)
    {
        var count = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            // "name value" — split on the first run of whitespace; value keeps any inner spaces.
            var sep = FirstWhitespace(line);
            if (sep < 0)
                continue;

            var name  = line[..sep];
            var value = Unquote(line[(sep + 1)..].Trim());

            foreach (var cv in cvars)
            {
                if (cv is not null && string.Equals(cv.Name, name, StringComparison.Ordinal))
                {
                    cv.SetString(value);
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static int FirstWhitespace(string s)
    {
        for (var i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i]))
                return i;
        return -1;
    }

    private static string Quote(string value)
        => value.IndexOf(' ') >= 0 ? $"\"{value}\"" : value;

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}

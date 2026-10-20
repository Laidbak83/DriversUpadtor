using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Inf;

/// <summary>
/// Lecteur minimal de fichiers .inf : extrait DriverVer et la liste des identifiants
/// matériels pris en charge pour l'architecture courante.
/// </summary>
public static class InfParser
{
    public static IReadOnlyList<DriverIndexEntry> Parse(string infPath)
    {
        var sections = ReadSections(infPath);
        var strings = sections.TryGetValue("strings", out var s) ? ParseStrings(s) : new();

        string? version = null;
        DateTime? date = null;
        if (sections.TryGetValue("version", out var ver))
        {
            var driverVer = ver.Select(SplitKeyValue)
                .FirstOrDefault(kv => kv.Key.Equals("DriverVer", StringComparison.OrdinalIgnoreCase)).Value;
            if (driverVer is not null)
            {
                var parts = driverVer.Split(',', 2, StringSplitOptions.TrimEntries);
                if (DateTime.TryParseExact(parts[0], new[] { "MM/dd/yyyy", "M/d/yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    date = d;
                if (parts.Length > 1) version = parts[1];
            }
        }

        var result = new List<DriverIndexEntry>();
        if (!sections.TryGetValue("manufacturer", out var manufacturers)) return result;

        foreach (var line in manufacturers)
        {
            var (_, value) = SplitKeyValue(line);
            if (value is null) continue;

            // "Section,NTamd64,NTamd64.10.0..." => Section, Section.NTamd64, ...
            var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var baseName = parts[0];
            var modelSections = new List<string>();
            if (parts.Length == 1) modelSections.Add(baseName);
            foreach (var deco in parts.Skip(1))
            {
                if (IsDecorationForThisMachine(deco)) modelSections.Add($"{baseName}.{deco}");
            }

            foreach (var sectionName in modelSections)
            {
                if (!sections.TryGetValue(sectionName.ToLowerInvariant(), out var models)) continue;
                foreach (var modelLine in models)
                {
                    var (desc, rest) = SplitKeyValue(modelLine);
                    if (rest is null) continue;
                    var fields = rest.Split(',', StringSplitOptions.TrimEntries);
                    var description = Expand(desc, strings);
                    // fields[0] = section d'installation, puis les IDs
                    foreach (var hwid in fields.Skip(1))
                    {
                        if (hwid.Length == 0) continue;
                        result.Add(new DriverIndexEntry(hwid.ToUpperInvariant(), description, version, date, infPath));
                    }
                }
            }
        }
        return result;
    }

    private static bool IsDecorationForThisMachine(string decoration)
    {
        var d = decoration.ToLowerInvariant();
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "ntamd64",
            Architecture.Arm64 => "ntarm64",
            _ => "ntx86",
        };
        // "NT" seul = toutes architectures NT ; sinon il faut l'architecture courante.
        return d == "nt" || d.StartsWith("nt.", StringComparison.Ordinal) || d.StartsWith(arch, StringComparison.Ordinal);
    }

    private static Dictionary<string, List<string>> ReadSections(string path)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        // detectEncodingFromByteOrderMarks gère les INF en UTF-16 ; sinon on retombe sur ANSI.
        using var reader = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim().ToLowerInvariant();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new List<string>();
                    sections[name] = current;
                }
                continue;
            }
            current?.Add(line);
        }
        return sections;
    }

    private static string StripComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ';' && !inQuotes) return line[..i];
        }
        return line;
    }

    private static (string Key, string? Value) SplitKeyValue(string line)
    {
        int eq = line.IndexOf('=');
        return eq < 0 ? (line.Trim(), null) : (line[..eq].Trim(), line[(eq + 1)..].Trim());
    }

    private static Dictionary<string, string> ParseStrings(List<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines)
        {
            var (k, v) = SplitKeyValue(l);
            if (v is not null) map[k] = v.Trim('"');
        }
        return map;
    }

    private static string Expand(string token, Dictionary<string, string> strings)
    {
        var t = token.Trim();
        if (t.Length > 2 && t[0] == '%' && t[^1] == '%' && strings.TryGetValue(t[1..^1], out var v)) return v;
        return t.Trim('"');
    }
}

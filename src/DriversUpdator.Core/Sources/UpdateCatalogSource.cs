using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources;

/// <summary>
/// Recherche en ligne sur le Microsoft Update Catalog (catalog.update.microsoft.com).
/// Pas d'API officielle : on lit les pages HTML, comme les autres outils open source.
/// </summary>
public sealed partial class UpdateCatalogSource : IDriverSource
{
    private const string BaseUrl = "https://www.catalog.update.microsoft.com/";
    private readonly HttpClient _http;

    public string Name => "Update Catalog";

    /// <summary>Journal optionnel : pilotes écartés car d'une autre classe que le périphérique.</summary>
    public IProgress<string>? Log { get; set; }

    public UpdateCatalogSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DriversUpdator/0.1");
    }

    public bool Supports(DeviceInfo device) => device.HardwareIds.Count > 0;

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        // Les IDs compatibles (classes génériques) donneraient trop de bruit : ils ne sont pas interrogés.
        // Toutes les requêtes du périphérique partent en même temps ; on garde ensuite
        // la première (la plus précise) qui donne un résultat valable.
        var queries = CatalogQueries(device).ToList();
        var responses = await Task.WhenAll(queries.Select(q => SearchCachedAsync(q, ct)));
        for (int i = 0; i < queries.Count; i++)
        {
            var results = responses[i].Where(c => ClassMatches(c, device)).ToList();
            foreach (var rejected in responses[i].Except(results).Take(3))
                Log?.Report($"Écarté pour {device.Name} (classe {device.ClassName}) : {rejected.Description}");
            if (results.Count > 0) return results;
        }
        return Array.Empty<DriverCandidate>();
    }

    /// <summary>
    /// La recherche du catalogue est en texte libre et renvoie parfois d'autres pilotes du même fabricant
    /// (ex. un pilote graphique Intel pour un contrôleur RAID Intel). Les titres suivent la forme
    /// "Fabricant - Classe - Version" : on exige que la classe soit celle du périphérique.
    /// </summary>
    private static bool ClassMatches(DriverCandidate c, DeviceInfo device)
    {
        if (string.IsNullOrEmpty(device.ClassName)) return true; // pilote manquant : classe inconnue
        var parts = c.Description.Split(" - ", StringSplitOptions.TrimEntries);
        return parts.Length < 3 || parts[1].Equals(device.ClassName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requêtes à essayer : les 2 IDs les plus précis, puis la forme courte "VEN_xxxx&amp;DEV_yyyy"
    /// (ou "VID_&amp;PID_" en USB), sous laquelle le catalogue référence la plupart des pilotes.
    /// </summary>
    private static IEnumerable<string> CatalogQueries(DeviceInfo device)
    {
        var queries = device.HardwareIds.Take(2).ToList();
        foreach (var id in device.AllIds)
        {
            var ven = HardwareId.Vendor(id);
            var dev = HardwareId.Device(id);
            if (ven is null || dev is null) continue;
            var usb = id.Contains("VID_", StringComparison.OrdinalIgnoreCase);
            queries.Add(usb ? $"VID_{ven}&PID_{dev}" : $"VEN_{ven}&DEV_{dev}");
            break;
        }
        return queries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // Plusieurs périphériques partagent souvent le même VEN/DEV (ports USB, HID...) : une seule requête par terme.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<IReadOnlyList<DriverCandidate>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private Task<IReadOnlyList<DriverCandidate>> SearchCachedAsync(string query, CancellationToken ct)
        => _cache.GetOrAdd(query, q => SearchAsync(q, ct));

    public async Task<IReadOnlyList<DriverCandidate>> SearchAsync(string hardwareId, CancellationToken ct = default)
    {
        var url = BaseUrl + "Search.aspx?q=" + Uri.EscapeDataString(hardwareId);
        string html;
        try { html = await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException) { return Array.Empty<DriverCandidate>(); }

        var list = new List<DriverCandidate>();
        foreach (Match row in RowRegex().Matches(html))
        {
            var updateId = row.Groups["id"].Value;
            var cells = CellRegex().Matches(row.Groups["body"].Value)
                .Select(m => WebUtility.HtmlDecode(TagRegex().Replace(m.Groups[1].Value, "")).Trim())
                .ToList();
            // Colonnes : [case], Titre, Produits, Classification, Dernière mise à jour, Version, Taille, [bouton]
            if (cells.Count < 7) continue;
            var title = cells[1];
            var products = string.Join(" | ", cells.Skip(2).Take(2));

            // On ne garde que les pilotes pour Windows 10/11 (le catalogue contient aussi XP, 7, 8.1...).
            if (!products.Contains("Windows 1", StringComparison.OrdinalIgnoreCase)) continue;
            // ARM64 ne nous concerne pas sur un PC x64.
            if (title.Contains("ARM64", StringComparison.OrdinalIgnoreCase)) continue;

            // Pour les pilotes, la colonne Version vaut souvent "n/a" : la version est en fin de titre
            // (ex. "Intel - System - 10/3/2016 12:00:00 AM - 30.100.1634.2").
            var version = DriverVersion.TryParse(cells[5]) ?? DriverVersion.TryParse(TitleVersionRegex().Match(title).Groups[1].Value);
            DateTime.TryParse(cells[4], CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date);

            list.Add(new DriverCandidate
            {
                SourceName = Name,
                MatchedId = hardwareId,
                Description = title,
                Version = version,
                Date = date == default ? null : date,
                DownloadUrl = "catalog:" + updateId, // résolu au moment de l'installation
            });
        }
        return list;
    }

    /// <summary>Transforme un identifiant de mise à jour du catalogue en URL de fichier .cab.</summary>
    public async Task<string?> ResolveDownloadUrlAsync(string updateId, CancellationToken ct = default)
    {
        var payload = $"[{{\"size\":0,\"languages\":\"\",\"uidInfo\":\"{updateId}\",\"updateID\":\"{updateId}\"}}]";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["updateIDs"] = payload });
        using var resp = await _http.PostAsync(BaseUrl + "DownloadDialog.aspx", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var m = DownloadUrlRegex().Match(body);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("<tr id=\"(?<id>[0-9a-fA-F-]{36})_R\\d+\"[^>]*>(?<body>.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"-\s*(\d+(?:\.\d+){1,3})\s*$")]
    private static partial Regex TitleVersionRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\.url\s*=\s*'([^']+)'")]
    private static partial Regex DownloadUrlRegex();
}

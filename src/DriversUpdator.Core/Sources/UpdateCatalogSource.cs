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

    public UpdateCatalogSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DriversUpdator/0.1");
    }

    public bool Supports(DeviceInfo device) => device.HardwareIds.Count > 0;

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        // On interroge du plus précis au plus générique et on s'arrête au premier résultat.
        // Les IDs compatibles (classes génériques) donneraient trop de bruit.
        foreach (var id in device.HardwareIds.Take(3))
        {
            var results = await SearchAsync(id, ct);
            if (results.Count > 0) return results;
        }
        return Array.Empty<DriverCandidate>();
    }

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

            DateTime.TryParse(cells[4], CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date);
            list.Add(new DriverCandidate
            {
                SourceName = Name,
                MatchedId = hardwareId,
                Description = cells[1],
                Version = DriverVersion.TryParse(cells[5]),
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

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\.url\s*=\s*'([^']+)'")]
    private static partial Regex DownloadUrlRegex();
}

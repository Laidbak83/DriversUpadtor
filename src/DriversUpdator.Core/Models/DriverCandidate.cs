namespace DriversUpdator.Core.Models;

/// <summary>Un pilote proposé par une source pour un périphérique.</summary>
public sealed class DriverCandidate
{
    public required string SourceName { get; init; }
    public required string MatchedId { get; init; }
    public string Description { get; init; } = "";
    public DriverVersion? Version { get; init; }
    public DateTime? Date { get; init; }

    /// <summary>Chemin d'un .inf déjà présent localement (packs SDIO).</summary>
    public string? LocalInfPath { get; init; }

    /// <summary>URL de téléchargement (.cab, .zip, .exe) pour les sources en ligne.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Pour un installeur .exe : arguments d'installation silencieuse.
    /// Null = l'installeur est lancé normalement et l'utilisateur suit l'assistant.
    /// </summary>
    public string? InstallerArguments { get; init; }

    /// <summary>Rang de correspondance : plus petit = plus précis (index dans la liste des IDs).</summary>
    public int MatchRank { get; set; }
}

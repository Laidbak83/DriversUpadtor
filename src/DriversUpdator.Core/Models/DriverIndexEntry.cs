namespace DriversUpdator.Core.Models;

/// <summary>Une ligne d'index : un identifiant matériel pris en charge par un .inf.</summary>
public sealed record DriverIndexEntry(
    string HardwareId,
    string Description,
    string? Version,
    DateTime? Date,
    string InfPath);

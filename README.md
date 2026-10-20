# DriversUpdator

Outil Windows de détection et de mise à jour des pilotes, dans l'esprit de Snappy Driver Installer / DriversCloud.

## Fonctionnement

1. **Scan** du matériel via WMI (`Win32_PnPEntity` + `Win32_PnPSignedDriver`) : identifiants matériels, version et date du pilote installé, périphériques sans pilote.
2. **Recherche** dans plusieurs sources, puis choix du meilleur pilote (identifiant le plus précis, puis le plus récent) :
   - **Packs SDIO** (Snappy Driver Installer Origin) : base hors ligne. Les `.7z` sont décompressés avec 7-Zip puis tous les `.inf` sont indexés (`cache/sdio-index.json`).
   - **Microsoft Update Catalog** : recherche en ligne par identifiant matériel, téléchargement du `.cab`.
3. **Installation** avec `pnputil /add-driver ... /install`, après création d'un point de restauration.
4. **Sauvegarde** des pilotes actuels (`pnputil /export-driver *`).
5. Bouton **Chercher sur Touslesdrivers** pour une recherche manuelle.

## Prérequis

- Windows 10/11 x64, [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- [7-Zip](https://www.7-zip.org) (pour décompresser les packs SDIO)
- Les driverpacks SDIO (téléchargeables depuis https://www.glenn.delahoy.com/snappy-driver-installer-origin/ ou sdi-tool.org), à placer dans le dossier `packs` à côté de l'exécutable, ou à choisir via « Indexer les packs SDIO… ».

## Compilation

```
dotnet build -c Release
dotnet run --project src/DriversUpdator.App
```

L'application demande les droits administrateur (nécessaires pour installer des pilotes).

## Structure

```
src/DriversUpdator.Core   Bibliothèque : scan, parseur INF, sources, sélection, installation
src/DriversUpdator.App    Interface WPF
```

## Ajouter une source (fabricant)

Implémenter `IDriverSource` (`src/DriversUpdator.Core/Sources/IDriverSource.cs`) :
`Supports(device)` filtre les périphériques concernés (ex. `VEN_10DE` pour NVIDIA) et
`FindAsync(device)` renvoie des `DriverCandidate` (version, date, URL). Puis l'ajouter à la liste
des sources dans `MainWindow.Check_Click`.

Prévu : NVIDIA, AMD, Intel, Realtek.

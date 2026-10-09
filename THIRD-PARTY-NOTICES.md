# Bibliothèques et services utilisés par Wyrmhold

Wyrmhold est distribué sous licence **GNU GPL version 3 ou ultérieure** (voir `LICENSE`),
avec des conditions supplémentaires sur la mention de l'auteur, le nom et le logo (voir `NOTICE.md`).
Il utilise les bibliothèques ci-dessous, toutes sous des licences libres compatibles avec la GPL v3.
Licences relevées dans les paquets NuGet le 9 octobre 2026.

## Bibliothèques livrées avec Wyrmhold

| Bibliothèque | Version | Licence | Projet |
|---|---|---|---|
| AngleSharp | 1.8.3 | MIT | https://anglesharp.github.io/ |
| Google.Protobuf | 3.21.12 | BSD 3 clauses | https://github.com/protocolbuffers/protobuf |
| Microsoft.Data.Sqlite (+ .Core) | 10.0.12 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.Web.WebView2 (SDK) | 1.0.4258.31 | BSD 3 clauses (Microsoft) | https://aka.ms/webview |
| SharpZipLib | 1.4.1 | MIT | https://github.com/icsharpcode/SharpZipLib |
| SQLitePCLRaw (bundle, core, lib, provider) | 2.1.12 | Apache 2.0 | https://github.com/ericsink/SQLitePCL.raw |
| System.Security.Cryptography.ProtectedData | 10.0.12 | MIT | https://dot.net/ |
| UbiParser | 1.3.0 | MIT | https://github.com/UplayDB/Ubi-Parser |
| Uplay-Protobufs | 137.0.10799 | MIT | https://github.com/UplayDB/Uplay-Protobufs |
| Velopack | 1.2.158 | MIT | https://github.com/velopack/velopack |
| VirtualizingWrapPanel | 2.5.4 | MIT | https://github.com/sbaeumlisberger/VirtualizingWrapPanel |
| .NET (environnement d'exécution inclus) | 10 | MIT | https://github.com/dotnet/runtime |

SQLite lui-même (moteur de base de données inclus par SQLitePCLRaw) est dans le domaine public.

## Composants non livrés

- **Moteur Microsoft Edge WebView2** : composant de Windows. S'il manque, l'installateur le télécharge
  depuis Microsoft ; il n'est pas redistribué par Wyrmhold.

## Services en ligne

- **IsThereAnyDeal** (https://isthereanydeal.com/) : prix, promos et historique. Prix et liens affichés sans modification.
- **SteamGridDB** (https://www.steamgriddb.com/) : jaquettes faites par la communauté.
- **Steam Web API** (https://steamcommunity.com/dev) : bibliothèque, succès, patch notes, guides.
  Ce produit n'est pas approuvé par Valve.

## Idées reprises d'autres projets (sans copie de code)

Certaines adresses de services ou méthodes de connexion ont été trouvées en lisant ces projets libres :
Playnite (https://github.com/JosefNemec/Playnite), son extension SuccessStory
(https://github.com/Lacro59/playnite-successstory-plugin) et Legendary (outil libre de connexion à Epic Games).

## Marques

Wyrmhold n'est pas affilié à Valve, Epic Games, GOG, Electronic Arts, Ubisoft ni Blizzard Entertainment.
Les noms des plateformes et des jeux appartiennent à leurs propriétaires.

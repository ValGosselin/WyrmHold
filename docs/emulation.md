# Wyrmhold — Jeux émulés et succès (phases 9 à 11)

Document de travail pour Claude Code. Il complète la liste des tâches (phases 9, 10 et 11).

## Règles à suivre

- **Vérifier avant de coder.** Les chemins, options de ligne de commande, formats de fichiers et noms d'API
  donnés ici sont des pistes. Contrôle chacun sur la machine (fichiers réels des émulateurs) ou dans la
  documentation officielle avant de t'en servir. Si quelque chose ne correspond pas, dis-le.
- **Une phase à la fois.** Termine la phase, fais compiler la solution, puis arrête-toi.
- **À chaque livraison :** explique ce que fait le code (en français, simplement) et donne les étapes de test
  détaillées (quoi faire, résultat attendu).
- **Réutiliser l'existant :** `ILibraryProvider`, `IAchievementProvider`, `LibraryService`, `GameDatabase`
  (SQLite), `Secrets` / `SecureStore` pour les clés, l'onglet « Comptes », les badges de succès,
  `LiveAchievementTracker`. Suivre le style du code actuel (commentaires en français, une classe par source).
- `Platform` est aujourd'hui un enum de lanceurs (Steam, Epic…). Choisis la façon la plus propre de
  représenter « émulateur + console » (par exemple une valeur par émulateur et une propriété `Console` sur
  `Game`) et explique ton choix.

## Émulateurs utilisés

| Console | Émulateur | Succès |
|---|---|---|
| GB, GBC, GBA, DS | RetroArch | RetroAchievements (phase 10) |
| Wii, GameCube | Dolphin | RetroAchievements (phase 10) |
| Switch | Ryujinx (fork actuel, ex. Ryubing : vérifier lequel est installé) | Succès maison (phase 11) |
| 3DS | Azahar (remplace Citra, abandonné en mars 2024) | Succès maison en attendant RetroAchievements |

## Phase 9 — Jeux émulés (bibliothèque et lancement)

But : les jeux émulés apparaissent dans la bibliothèque et se lancent comme les autres.

1. Configuration : pour chaque émulateur, chemin de l'exécutable (détection automatique si possible,
   sinon choix manuel) et liste de dossiers de jeux. Écran dans les paramètres ou l'onglet « Comptes ».
2. Un `ILibraryProvider` par émulateur :
   - RetroArch : lire les playlists `.lpl` (JSON, dossier `playlists`) en plus des dossiers de ROM.
     Associer extension et console (.gb, .gbc, .gba, .nds…).
   - Dolphin : dossiers d'ISO déclarés dans sa configuration (`Dolphin.ini`, section des chemins) + dossiers ajoutés.
   - Ryujinx : dossiers de jeux de sa configuration (`Config.json`, clé des dossiers de jeux) + dossiers ajoutés.
     Lire le Title ID et le nom du jeu si possible.
   - Azahar : dossiers de sa configuration + dossiers ajoutés. Lire le Title ID si possible.
3. Nom propre (titre interne du jeu quand on peut le lire, sinon nom de fichier nettoyé des balises
   « (Europe) », « [!] »…), jaquette via les sources déjà utilisées (SteamGridDB, etc.).
4. Lancement en ligne de commande :
   - RetroArch : `retroarch.exe -L <cœur> "<rom>"`, le cœur choisi selon la console (paramétrable).
   - Dolphin : `Dolphin.exe -e "<iso>"` (vérifier l'option).
   - Ryujinx : `Ryujinx.exe "<jeu>"`.
   - Azahar : exécutable + chemin du jeu (vérifier le nom de l'exécutable).
5. Temps de jeu suivi via le processus de l'émulateur (même logique que `GameWatcher`).

## Phase 10 — Succès RetroAchievements

But : GB, GBA, DS, Wii et GameCube ont leurs succès automatiques, comme Steam.

1. Compte : pseudo + clé Web API (l'utilisateur la trouve dans les paramètres de son compte
   sur retroachievements.org). Stockée comme les autres secrets. Bouton de test de la clé (cf. `ApiKeyTester`).
2. Associer chaque jeu émulé à un identifiant de jeu RetroAchievements :
   - de préférence par hash de la ROM, avec l'algorithme de hash de RetroAchievements propre à chaque console
     (MD5 simple pour certaines, algorithme spécial pour DS, Wii, GameCube : vérifier dans la doc / rcheevos) ;
   - sinon par nom, en s'appuyant sur la liste des jeux par console de l'API ;
   - possibilité de corriger l'association à la main.
3. `RetroAchievementsProvider : IAchievementProvider`, via l'API web
   (`https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php`, paramètres `y` = clé,
   `u` = utilisateur, `g` = jeu). Gérer le mode hardcore (afficher les deux ou préférer hardcore).
4. Respecter les limites d'appels de l'API (cache en base, pas d'appel par jeu à chaque démarrage si rien n'a changé).
5. Suivi en direct (overlay / `LiveAchievementTracker`) si c'est simple : relire les déblocages récents de l'utilisateur.

Docs de l'API : https://api-docs.retroachievements.org/

## Phase 11 — Succès maison (Switch, 3DS)

But : des succès automatiques pour des jeux choisis un par un, en lisant leur sauvegarde. Pilote : Super Mario Odyssey.

### Moteur

1. Un fichier JSON par jeu, par exemple `WyrmHold.Core/Data/CustomAchievements/<TitleID>.json`, copié avec l'appli :
   - infos du jeu (titre, Title ID, console, version de save gérée) ;
   - liste de succès : `id`, `title`, `description`, `rule`, `source` (URL de la page qui prouve que ce succès existe dans le jeu).
2. Des règles déclaratives simples, sans code par succès. Exemples : « compteur ≥ N », « drapeau vrai »,
   « tous les éléments d'une liste présents ». Le chemin dans la save est décrit dans la règle.
3. Un lecteur de save par jeu (classe C#) qui transforme la save en valeurs nommées que les règles lisent.
4. `CustomAchievementProvider : IAchievementProvider` : trouve la save, la lit, évalue les règles.
   Relecture quand l'émulateur se ferme et à chaque actualisation. Ne jamais modifier la save (lecture seule, copie si besoin).
5. Trouver la save :
   - Ryujinx : dossier `bis/user/save/` dans le dossier de données de Ryujinx, avec des sous-dossiers numérotés ;
     retrouver lequel correspond au Title ID (vérifier comment Ryujinx fait cette association).
   - Azahar : arborescence `sdmc/Nintendo 3DS/.../title/.../<TitleID>/data/` dans son dossier de données (à vérifier).

### Pilote : Super Mario Odyssey

- Title ID supposé : `0100000000010000` (à vérifier).
- Format de save (https://switchbrew.org/wiki/Mario_Odyssey_(Save_Format)) : en-tête de 16 octets
  (0x0 CRC32 little-endian de 0x04 à la fin, 0x4 inconnu, 0x8 taille du fichier, 0xC inconnu) suivi de données BYAML.
  Le contenu (où sont les lunes) n'est pas documenté. Outil de référence qui convertit ces saves en XML :
  https://github.com/RR0000/Odyssave
- Étapes :
  1. Écrire un lecteur BYAML en C# (ou utiliser une bibliothèque .NET existante sous licence compatible ; l'indiquer
     dans THIRD-PARTY-NOTICES.md).
  2. Demander à l'utilisateur deux copies de sa save : une juste avant de prendre une lune, une juste après
     (voir « Ce que l'utilisateur doit fournir »). Comparer les deux pour trouver les clés des lunes,
     des royaumes, des boss, de l'histoire.
  3. Générer la liste des succès à partir du Super Mario Wiki (https://www.mariowiki.com/Lists_of_Power_Moons
     et pages liées) :
     - histoire : chaque royaume atteint, chaque boss principal, fin du jeu, Royaume Lune, contenu post-générique ;
     - paliers de lunes au total ;
     - pour chaque royaume : « toutes les lunes du royaume » ;
     - pièces violettes par royaume, tenues, souvenirs, captures si la save les contient.
     Réussir tous les succès doit vouloir dire « jeu à 100 % ».
  4. Vérification en deux passes : relire chaque succès contre sa page source (nombre de lunes par royaume,
     noms) et lister tout écart. Corriger, puis refaire une passe complète. Ne garder un succès que s'il est
     détectable dans la save.
  5. Livrer un rapport : nombre de succès, ceux qu'on ne peut pas détecter, sources utilisées.

### Ce que l'utilisateur doit fournir (à lui demander au bon moment)

- Phase 10 : son pseudo et sa clé Web API RetroAchievements ; RetroAchievements activé dans RetroArch et Dolphin.
- Phase 11 : il n'a plus de save de Mario Odyssey. Lui demander de :
  1. lancer une nouvelle partie dans Ryujinx, jouer jusqu'à pouvoir prendre une lune, sauvegarder et quitter ;
  2. copier le dossier de save (lui indiquer lequel) vers `saves-test/smo-avant/` ;
  3. relancer, prendre une lune, sauvegarder, quitter, copier vers `saves-test/smo-apres/`.
  Ajouter `saves-test/` au `.gitignore`.

### Jeux suivants

Même méthode, un jeu à la fois, à la demande de l'utilisateur.

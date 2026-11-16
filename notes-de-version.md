# Notes de version de Wyrmhold

Ce fichier est rangé dans l'appli et lu par `publier.ps1`. Une partie par version, la plus récente en haut.
Le titre de chaque partie est le numéro seul (`## 1.1.0`), celui de `Directory.Build.props`.
Dessous : des titres `###` et des lignes `- `, rien d'autre, en phrases courtes pour quelqu'un qui ne connaît pas le code.

## 1.2.0

### Nouveautés
- Bibliothèque : zoom des tuiles avec Ctrl + molette ou le curseur à côté du tri. Les tuiles remplissent maintenant toute la ligne.
- Logos des plateformes et des consoles dans les filtres de la bibliothèque. Une fois une plateforme choisie, les autres s'estompent.
- La fiche d'un jeu s'élargit en tirant son bord gauche. Un double-clic sur ce bord la remet à sa taille d'origine.
- Clic droit sur un jeu → « Choisir une jaquette… » : choisis parmi les jaquettes de SteamGridDB (clé SteamGridDB nécessaire).
- Badge bleu sur un jeu quand des amis Steam y jouent, avec leurs pseudos au survol.
- Badge « Famille » sur la jaquette des jeux de la famille Steam.
- Statistiques : nouvelle zone Succès, avec ta progression, le calendrier de tes succès, leur rareté et tes jeux récents. Elle était déjà arrivée avec la 1.1.2.
- Statistiques : le temps de jeu Steam compte maintenant dans « Temps de jeu par semaine », à partir de cette version.
- Réglages : les jeux émulés sont rangés dans une partie « Consoles » qui se déplie, avec une case par console, dans Lanceurs comme dans Succès.
- Comptes : un logo et un voyant sur chaque ligne (vert = OK, orange = à refaire, gris = désactivé ou pas connecté).
- Le bouton clair / sombre est en haut à droite, visible depuis tous les onglets.
- Nouvelle animation pendant la recherche de mise à jour.

### Corrections
- Succès Steam : la progression ne recule plus après une partie (par exemple 20/22 affiché alors que le jeu était à 22/22).
- Les jeux Steam que tu possèdes ne sont plus marqués « Famille » quand un membre de la famille les possède aussi. La correction se fait à la prochaine actualisation.
- Plus d'espaces gris entre les tuiles de la bibliothèque.
- La liste « Plateforme » de la fiche est plus courte : console, plateforme, installé ou non.

## 1.1.2

### Nouveautés
- Réglages → Démarrage : une case pour lancer Wyrmhold au démarrage de Windows. Il s'ouvre alors caché près de l'horloge, sans fenêtre, et se met à jour tout seul. Décochée par défaut.

### Corrections
- Inazuma Eleven GO Light : Axel Blaze et Byron Love ne sont plus dans les succès, car ils ne se recrutent pas dans cette version aujourd'hui. Xavier Foster, réservé à Light, les remplace.

## 1.1.1

### Nouveautés
- Inazuma Eleven GO Light : 11 nouveaux succès, un pour chaque chapitre terminé. Ils apparaissent quand tu sauvegardes dans le jeu.

## 1.1.0

### Nouveautés
- Succès RetroAchievements pour les jeux émulés sur RetroArch et Dolphin : connecte ton compte dans Comptes pour voir ta progression, et tes succès s'affichent pendant la partie.
- Clic droit sur un jeu émulé → « Jeu RetroAchievements… » pour corriger le jeu associé.
- Succès maison, lus dans ta sauvegarde : Pokémon Let's Go Évoli sur Ryujinx et Inazuma Eleven GO Light sur Azahar.
- Résumé des nouveautés pendant le téléchargement d'une mise à jour, puis au redémarrage. Tu le retrouves dans « À propos ».
- Le bouton Jouer ressort mieux, et devient « Installer » pour un jeu Steam, Epic, Ubisoft ou Battle.net pas encore installé : le launcher s'ouvre directement sur l'installation.
- Clic droit sur un jeu → « Collections » pour le ranger sans ouvrir sa fiche, ou en créer une nouvelle.
- Une étoile à côté du nom du jeu pour l'ajouter aux favoris.
- Message clair quand aucun jeu ne correspond à ta recherche, avec un bouton pour tout effacer.

## 1.0.9

### Nouveautés
- Le développeur des jeux Epic s'affiche même si le jeu n'est pas reconnu sur Steam.
- Taille estimée des jeux non installés.
- Les lignes vides disparaissent de la fiche d'un jeu.
- Bouton « Ouvrir le journal » dans Réglages.
- Wyrmhold démarre toujours sur la Bibliothèque, et l'onglet s'appelle maintenant « Boutique ».

### Corrections
- Boutique : la recherche s'annule en vidant la case, le prix max se vide quand on change de jeu, et le filtre montre toutes les boutiques, même sans jeu choisi.
- Boutique : message clair quand Steam limite les demandes ou que la clé IsThereAnyDeal manque.
- Battle.net reste connecté après un redémarrage.
- Une seule copie de Wyrmhold : la rouvrir réaffiche la fenêtre.
- Défilement de la bibliothèque bien plus fluide (le premier passage après le lancement peut encore saccader).
- La barre de défilement ne passe plus sur le texte.
- Les boutons de test des notifications ne s'empilent plus.
- Corrections d'affichage : badges des tuiles, titres de patch notes, barres de progression.
- Le filtre d'origine disparaît si tu n'as pas de jeux de la famille Steam.

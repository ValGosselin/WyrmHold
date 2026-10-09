# Fabrique l'installateur de Wyrmhold (Velopack) et le publie dans les « Releases » du dépôt GitHub.
#
#   Essai sans rien envoyer :   powershell -ExecutionPolicy Bypass -File publier.ps1 -LocalOnly
#   Publier pour de vrai :      powershell -ExecutionPolicy Bypass -File publier.ps1
#
# Avant de publier : augmenter <Version> dans Directory.Build.props (0.9.0 → 0.9.1…).
# Velopack refuse de publier deux fois le même numéro, et les testeurs ne verraient pas la mise à jour.
#
# Le jeton GitHub est demandé à chaque fois et n'est jamais enregistré.
# Pour le créer : GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens,
# dépôt ValGosselin/WyrmHold seulement, permission « Contents : Read and write ».

param(
    [switch]$LocalOnly   # fabrique l'installateur dans Releases\ sans rien envoyer
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$repoUrl = "https://github.com/ValGosselin/WyrmHold"

# « WyrmholdApp » et pas « Wyrmhold » : Velopack installe dans %LocalAppData%\<packId>,
# et %LocalAppData%\Wyrmhold contient les données (base, clés, journal). Une désinstallation les effacerait.
$packId = "WyrmholdApp"

# --- 1. Le numéro de version, lu au même endroit que l'appli (Directory.Build.props). ---
[xml]$props = Get-Content "Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
Write-Host "Version : $version" -ForegroundColor Cyan

# --- 2. L'outil vpk (version notée dans dotnet-tools.json). ---
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Impossible d'installer l'outil vpk." }

# --- 3. Compiler l'appli, avec .NET inclus : rien d'autre à installer chez le testeur. ---
# On repart de dossiers vides : un ancien essai (-LocalOnly) avec le même numéro de version
# ferait échouer la fabrication de l'installateur.
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
if (Test-Path "Releases") { Remove-Item -Recurse -Force "Releases" }
dotnet publish "WyrmHold.App\WyrmHold.App.csproj" -c Release --self-contained -r win-x64 -o "publish"
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

# --- 4. Le jeton GitHub (seulement pour publier). ---
$token = ""
if (-not $LocalOnly) {
    $secure = Read-Host "Jeton GitHub (il ne s'affiche pas)" -AsSecureString
    $token = [System.Net.NetworkCredential]::new("", $secure).Password
    if ($token.Length -eq 0) { throw "Aucun jeton saisi." }

    # Les versions déjà publiées : Velopack s'en sert pour fabriquer de petites mises à jour (« deltas »).
    # La première fois, il n'y en a pas : une erreur ici n'est pas grave.
    dotnet vpk download github --repoUrl $repoUrl --token $token --outputDir "Releases"
}

# --- 5. Fabriquer l'installateur. ---
# --framework webview2 : installe le moteur WebView2 (pages de connexion, Aide) s'il manque, par ex. sur Windows 10.
dotnet vpk pack `
    --packId $packId `
    --packVersion $version `
    --packDir "publish" `
    --mainExe "WyrmHold.App.exe" `
    --packTitle "Wyrmhold" `
    --packAuthors "Val" `
    --icon "WyrmHold.App\Assets\wyrmhold.ico" `
    --splashImage "WyrmHold.App\Assets\logo-512.png" `
    --framework "webview2" `
    --outputDir "Releases"
if ($LASTEXITCODE -ne 0) { throw "La fabrication de l'installateur a échoué." }

if ($LocalOnly) {
    Write-Host "Installateur prêt dans Releases\ (rien n'a été envoyé)." -ForegroundColor Green
    return
}

# --- 6. Publier sur GitHub, après confirmation. ---
$answer = Read-Host "Publier la version $version sur GitHub ? Tes testeurs la recevront. (o/n)"
if ($answer -ne "o") {
    Write-Host "Publication annulée. L'installateur reste dans Releases\."
    return
}

dotnet vpk upload github --repoUrl $repoUrl --token $token --outputDir "Releases" `
    --publish --releaseName "Wyrmhold $version" --tag "v$version"
if ($LASTEXITCODE -ne 0) { throw "L'envoi sur GitHub a échoué." }

Write-Host "Version $version publiée : $repoUrl/releases" -ForegroundColor Green

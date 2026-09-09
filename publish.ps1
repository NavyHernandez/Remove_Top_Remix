<#
.SYNOPSIS
    Publica One Dj App, crea el paquete Velopack y lo sube a GitHub Releases.

.USO
    .\publish.ps1                          # Versión leída del .csproj
    .\publish.ps1 -Version "1.0.0"         # Forzar versión
    .\publish.ps1 -SkipUpload              # Solo publicar + paquetear (sin subir)
    .\publish.ps1 -Token $env:GH_TOKEN     # Token por variable de entorno

.DESCPRIPCIÓN
    Requisitos:
      - .NET 8 SDK
      - Velopack CLI (vpk): dotnet tool install -g Velopack
      - GitHub token con permisos write:packages (variable de entorno GH_TOKEN o parámetro -Token)

    Flujo:
      1. Lee la versión del .csproj (o usa la del parámetro)
      2. dotnet publish (Release, win-x64, self-contained)
      3. vpk pack (crea el paquete .nupkg en releases/)
      4. vpk upload github (sube a GitHub Releases)
#>

param(
    [string]$Version,
    [string]$Token,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"

# ─── Configuración ───────────────────────────────────────────────
$RepoOwner = "NavyHernandez"
$RepoName  = "Remove_Top_Remix"
$AppId     = "OneDjApp"
$RID        = "win-x64"
$ProjectDir = Join-Path $PSScriptRoot "Remove_Top"
$Csproj     = Join-Path $ProjectDir "Remove_Top.csproj"
$PublishDir = Join-Path $ProjectDir "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$ReleaseDir = Join-Path $PSScriptRoot "releases"

# ─── 1. Leer versión del .csproj ────────────────────────────────
if (-not $Version) {
    $raw = Get-Content $Csproj -Raw
    $match = [regex]::Match($raw, '<Version>([^<]+)</Version>')
    if ($match.Success) { $Version = $match.Groups[1].Value.Trim() }
    if (-not $Version) {
        Write-Error "No se pudo leer la versión del .csproj. Usa -Version para especificarla."
        exit 1
    }
}
Write-Host "==> Versión: $Version" -ForegroundColor Cyan

# ─── 2. dotnet publish ──────────────────────────────────────────
Write-Host "`n==> Publicando $AppId (Release, $RID, self-contained)..." -ForegroundColor Yellow
dotnet publish $Csproj `
    -c Release `
    -r $RID `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish falló."
    exit 1
}
Write-Host "    Publicación completada en: $PublishDir" -ForegroundColor Green

# ─── 3. vpk pack ────────────────────────────────────────────────
Write-Host "`n==> Empaquetando con Velopack (vpk pack)..." -ForegroundColor Yellow

if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
}

vpk pack `
    --packId $AppId `
    --packVersion $Version `
    --packDir $PublishDir `
    --outputDir $ReleaseDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack falló."
    exit 1
}

$nupkg = Get-ChildItem -Path $ReleaseDir -Filter "*.nupkg" | 
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "    Paquete creado: $($nupkg.FullName)" -ForegroundColor Green

# ─── 4. vpk upload github ───────────────────────────────────────
if ($SkipUpload) {
    Write-Host "`n==> SkipUpload activado. No se sube a GitHub." -ForegroundColor DarkYellow
    Write-Host "`n==> ¡Listo! Paquete en: $ReleaseDir" -ForegroundColor Green
    exit 0
}

if (-not $Token) {
    $Token = $env:GH_TOKEN
}
if (-not $Token) {
    Write-Host "`n==> No se proporcionó token. Saltando upload a GitHub." -ForegroundColor DarkYellow
    Write-Host "    Para subir, ejecuta: .\publish.ps1 -Token TU_TOKEN" -ForegroundColor DarkYellow
    Write-Host "    O configura: `$env:GH_TOKEN = 'tu_token'" -ForegroundColor DarkYellow
    Write-Host "`n==> ¡Listo! Paquete en: $ReleaseDir" -ForegroundColor Green
    exit 0
}

$Tag = "v$Version"

# ─── 4a. Limpieza idempotente ────────────────────────────────────
# vpk upload github FALLA si ya existe una release con el mismo tag
# (incluidos drafts huérfanos que dejan los uploads fallidos a medias).
# Antes de subir, se borra cualquier release o tag v$Version existente.
Write-Host "`n==> Limpiando release/tag existente $Tag (idempotente)..." -ForegroundColor Yellow

$headers = @{
    Authorization = "Bearer $Token"
    "User-Agent"  = "publish.ps1"
    Accept        = "application/vnd.github+json"
}
$baseApi = "https://api.github.com/repos/$RepoOwner/$RepoName"

try {
    # 1. Buscar la release cuyo tag_name coincida (funciona con drafts sin tag visible).
    $releases = Invoke-RestMethod -Uri "$baseApi/releases?per_page=100" -Headers $headers -Method Get
    $existing = $releases | Where-Object { $_.tag_name -eq $Tag }
    if ($existing) {
        $id = $existing[0].id
        Invoke-RestMethod -Uri "$baseApi/releases/$id" -Headers $headers -Method Delete | Out-Null
        Write-Host "    Release $Tag borrada (id $id)." -ForegroundColor DarkYellow
    }

    # 2. Borrar el tag si quedó huérfano.
    try {
        Invoke-RestMethod -Uri "$baseApi/git/refs/tags/$Tag" -Headers $headers -Method Delete | Out-Null
        Write-Host "    Tag $Tag borrado." -ForegroundColor DarkYellow
    }
    catch {
        # El tag no existe: no es un error.
    }
}
catch {
    Write-Host "    No se pudo limpiar (se continúa): $($_.Exception.Message)" -ForegroundColor DarkYellow
}

# ─── 4b. vpk upload github (con salida real en vivo) ─────────────
Write-Host "`n==> Subiendo a GitHub Releases ($RepoOwner/$RepoName)..." -ForegroundColor Yellow

# Se captura la salida y el exit code del comando nativo por separado:
# con "2>&1 | Tee-Object" PowerShell pierde $LASTEXITCODE de vpk.
$vpkOutput = & vpk upload github `
    --repoUrl "https://github.com/$RepoOwner/$RepoName" `
    --tag $Tag `
    --releaseName $Tag `
    --token $Token `
    --outputDir $ReleaseDir 2>&1
$vpkExit = $LASTEXITCODE
$vpkOutput | ForEach-Object { Write-Host $_ }

if ($vpkExit -ne 0) {
    Write-Error "vpk upload github falló (código $vpkExit). Revisa el mensaje de vpk arriba."
    exit 1
}

Write-Host "`n==> ¡Publicado! Release: https://github.com/$RepoOwner/$RepoName/releases/tag/$Tag" -ForegroundColor Green

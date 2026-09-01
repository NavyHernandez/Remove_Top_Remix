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
    [xml]$csprojXml = Get-Content $Csproj -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($csprojXml.NameTable)
    $ns.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003")
    $Version = $csprojXml.SelectSingleNode("//ms:Version", $ns).'#InnerText'
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

Write-Host "`n==> Subiendo a GitHub Releases ($RepoOwner/$RepoName)..." -ForegroundColor Yellow
vpk upload github `
    --repoUrl "https://github.com/$RepoOwner/$RepoName" `
    --tag "v$Version" `
    --releaseName "v$Version" `
    --token $Token `
    --outputDir $ReleaseDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk upload github falló."
    exit 1
}

Write-Host "`n==> ¡Publicado! Release: https://github.com/$RepoOwner/$RepoName/releases/tag/v$Version" -ForegroundColor Green

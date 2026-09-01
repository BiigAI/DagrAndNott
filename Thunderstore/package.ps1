[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot "manifest.json"

if (-not (Test-Path $manifestPath))
{
    throw "Manifest file is missing: $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version_number
$stagingDirectory = Join-Path $PSScriptRoot "staging"
$archivePath = Join-Path $PSScriptRoot "$($manifest.name)-$version.zip"
$releaseDirectory = Join-Path $projectRoot "bin\Release\net472"
$pluginPath = Join-Path $releaseDirectory "DagrNott_CustomDayCycle.dll"
$pluginDirectory = Join-Path $stagingDirectory "BepInEx\plugins\DagrNott_CustomDayCycle"

if (-not $SkipBuild)
{
    $dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (-not (Test-Path $dotnetPath))
    {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -eq $dotnetCommand)
        {
            throw "The .NET SDK was not found. Install a current .NET SDK before packaging."
        }

        $dotnetPath = $dotnetCommand.Source
    }

    Write-Host "Building project in Release mode..." -ForegroundColor Cyan
    & $dotnetPath build (Join-Path $projectRoot "DagrAndNott.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Release build failed; package creation stopped."
    }
}

# Verify version match across CustomDayCyclePlugin.cs and manifest.json
$pluginFile = Join-Path $projectRoot "CustomDayCyclePlugin.cs"
$pluginVersion = Select-String -Path $pluginFile -Pattern 'PluginVersion\s*=\s*"([^"]+)"' |
    Select-Object -First 1

if ($null -eq $pluginVersion -or $pluginVersion.Matches[0].Groups[1].Value -ne $version)
{
    throw "CustomDayCyclePlugin.cs and manifest.json must use the same version number."
}

# Verify version match in DagrAndNott.csproj
$csprojFile = Join-Path $projectRoot "DagrAndNott.csproj"
$csprojVersion = Select-String -Path $csprojFile -Pattern '<Version>([^<]+)</Version>' |
    Select-Object -First 1

if ($null -ne $csprojVersion -and $csprojVersion.Matches[0].Groups[1].Value -ne $version)
{
    throw "DagrAndNott.csproj and manifest.json must use the same version number."
}

$requiredFiles = @(
    (Join-Path $projectRoot "README.md"),
    (Join-Path $projectRoot "CHANGELOG.md"),
    $manifestPath,
    (Join-Path $PSScriptRoot "icon.png"),
    $pluginPath
)

foreach ($file in $requiredFiles)
{
    if (-not (Test-Path $file))
    {
        throw "Required package file is missing: $file"
    }
}

Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
New-Item $stagingDirectory -ItemType Directory | Out-Null

foreach ($file in $requiredFiles)
{
    $fileName = Split-Path $file -Leaf
    if ($fileName -eq "DagrNott_CustomDayCycle.dll")
    {
        New-Item $pluginDirectory -ItemType Directory -Force | Out-Null
        Copy-Item $file -Destination $pluginDirectory
    }
    else
    {
        Copy-Item $file -Destination $stagingDirectory
    }
}

Write-Host "Creating Thunderstore archive..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

# Verify archive integrity and entry paths
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try
{
    $expectedEntries = @(
        "README.md",
        "CHANGELOG.md",
        "manifest.json",
        "icon.png",
        "BepInEx/plugins/DagrNott_CustomDayCycle/DagrNott_CustomDayCycle.dll"
    )
    $actualEntries = @(
        $archive.Entries |
            Where-Object { -not $_.FullName.EndsWith("\") -and -not $_.FullName.EndsWith("/") } |
            ForEach-Object { $_.FullName.Replace("\", "/") }
    )
    $missingEntries = @($expectedEntries | Where-Object { $_ -notin $actualEntries })
    $unexpectedEntries = @($actualEntries | Where-Object { $_ -notin $expectedEntries })

    if ($missingEntries.Count -gt 0 -or $unexpectedEntries.Count -gt 0)
    {
        throw "Invalid archive contents. Missing: $($missingEntries -join ', '); unexpected: $($unexpectedEntries -join ', ')."
    }
}
finally
{
    $archive.Dispose()
}

# Clean up staging directory
Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Created Thunderstore package: $archivePath" -ForegroundColor Green

<#
.SYNOPSIS
    Publishes and deploys WestMarch.Web to Azure App Service.

.DESCRIPTION
    Runs the reliable "run-from-package" deploy path that avoids the two Windows gotchas
    we hit by hand: it publishes a Release build, repacks the zip with forward-slash paths
    (Compress-Archive writes backslashes, which break the Linux SQL driver), uploads it to
    blob storage, and restarts the app so it mounts the new package.

    Requires: .NET SDK, Azure CLI (az) logged in (az login) to the target subscription.

.EXAMPLE
    ./deploy.ps1
#>
param(
    [string]$ResourceGroup = 'westmarch-rg',
    [string]$AppName       = 'westmarch-manager',
    [string]$StorageAccount = 'westmarchimges9bu5',
    [string]$Container      = 'packages'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Locate az whether or not it's on PATH (installer adds it, but the current shell may predate that).
$az = (Get-Command az -ErrorAction SilentlyContinue).Source
if (-not $az) { $az = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' }
if (-not (Test-Path $az)) { throw "Azure CLI not found. Install it and run 'az login'." }

$publish = Join-Path $root 'publish'
$zip     = Join-Path $root 'westmarch.zip'

Write-Host "==> Publishing (Release)…"
dotnet publish (Join-Path $root 'src/WestMarch.Web/WestMarch.Web.csproj') -c Release -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "==> Repacking zip with forward-slash paths…"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.File]::Delete($zip)
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
Get-ChildItem $publish -Recurse -File | ForEach-Object {
    $entry = $_.FullName.Substring($publish.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry) | Out-Null
}
$archive.Dispose()

Write-Host "==> Uploading package to blob…"
$conn = & $az storage account show-connection-string -g $ResourceGroup -n $StorageAccount --query connectionString -o tsv
& $az storage blob upload --container-name $Container --file $zip --name 'westmarch.zip' --overwrite --connection-string $conn -o none
if ($LASTEXITCODE -ne 0) { throw "blob upload failed" }

Write-Host "==> Restarting app…"
& $az webapp restart -g $ResourceGroup -n $AppName -o none

Write-Host "==> Done. https://$AppName.azurewebsites.net (first request after restart may take ~60s)"

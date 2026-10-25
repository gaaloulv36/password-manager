<#
.SYNOPSIS
  Builds the Windows installer (Velopack) and the browser-extension zips.
.DESCRIPTION
  Stages (CI signs the files between them; locally just use the default "All"):
    Publish  self-contained app + native host into artifacts/publish
    Pack     installer and update packages into artifacts/installer, extension zips into artifacts/extension
.EXAMPLE
  ./build/pack.ps1 -Version 1.0.0
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet("All", "Publish", "Pack")] [string] $Stage = "All",
    [string] $Runtime = "win-x64",
    # In CI: lets vpk fetch the previous release so it can build a small delta update.
    [string] $GitHubToken
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts/publish"
$installer = Join-Path $root "artifacts/installer"
$extension = Join-Path $root "artifacts/extension"

if ($Stage -in "All", "Publish") {
    Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
    # The app and the native host are published self-contained into the same folder: both run on the bundled
    # .NET runtime, so nothing needs to be installed on the user's PC.
    foreach ($project in "src/Vault.Desktop", "src/Vault.NativeHost") {
        dotnet publish (Join-Path $root $project) -c Release -r $Runtime --self-contained -o $publish -p:Version=$Version --nologo
        if ($LASTEXITCODE) { throw "publish $project failed" }
    }
}

if ($Stage -in "All", "Pack") {
    Remove-Item $installer, $extension -Recurse -Force -ErrorAction SilentlyContinue
    dotnet tool restore
    if ($GitHubToken) {
        dotnet vpk download github --repoUrl https://github.com/gaaloulv36/password-manager --token $GitHubToken --outputDir $installer
    }
    dotnet vpk pack `
        --packId PasswordManager `
        --packVersion $Version `
        --packDir $publish `
        --mainExe Vault.Desktop.exe `
        --packTitle "Password Manager" `
        --packAuthors gaaloulv36 `
        --icon (Join-Path $root "src/Vault.Desktop/Assets/app.ico") `
        --outputDir $installer
    if ($LASTEXITCODE) { throw "vpk pack failed" }

    # Browser extension: Chromium (Chrome/Edge/Brave/Opera) and Firefox builds, zipped for the stores.
    node (Join-Path $root "extension/build.mjs")
    New-Item -ItemType Directory -Force $extension | Out-Null
    foreach ($target in "chromium", "firefox") {
        Compress-Archive -Path (Join-Path $root "extension/dist/$target/*") -DestinationPath (Join-Path $extension "password-manager-extension-$target-$Version.zip") -Force
    }

    Get-ChildItem $installer, $extension -File | Format-Table Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
}

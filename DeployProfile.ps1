param(
    [string]$Profile,
    [string]$Dll = "$PSScriptRoot\bin\Release\MapTracker.dll",
    [switch]$IncludeMigration,
    [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim')
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Profile')) { $Profile = Join-Path $DataRoot 'profiles\Default' }
if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(valheim|r2modman.*|Thunderstore.*)$' }).Count) { throw 'Close Valheim and r2modman (and Thunderstore Mod Manager if running) before replacing plugins.' }
if (-not (Test-Path -LiteralPath "$Profile\BepInEx\core\BepInEx.dll")) { throw 'Profile BepInEx installation not found.' }
$sourcePath = (Resolve-Path -LiteralPath $Dll).Path
if (-not $IncludeMigration) {
    $profilesRoot = [IO.Path]::GetFullPath((Join-Path $DataRoot 'profiles'))
    $profilePath = [IO.Path]::GetFullPath($Profile).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($profilePath) -eq $profilesRoot -and (Test-Path -LiteralPath (Join-Path $profilePath 'mods.yml'))) {
        $releaseDll = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin\Release\MapTracker.dll'))
        if ($sourcePath -ne $releaseDll) { throw 'Registered installation requires the Release DLL used by BuildPackage.ps1.' }
        & (Join-Path $PSScriptRoot 'BuildPackage.ps1') -SkipBuild
        & (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile ([IO.Path]::GetFileName($profilePath)) -DataRoot $DataRoot
        return
    }
}
$version = [Reflection.AssemblyName]::GetAssemblyName($sourcePath).Version.ToString()
$pluginDirectory = Join-Path $Profile 'BepInEx\plugins\Slikfoul-MapTracker'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$destination = Join-Path $pluginDirectory 'MapTracker.dll'
if (Test-Path -LiteralPath $destination) {
    $backupDirectory = Join-Path $Profile 'BepInEx\config\MapTracker\backups'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backup = Join-Path $backupDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-MapTracker.dll.bak')
    Copy-Item -LiteralPath $destination -Destination $backup
}
Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) { throw 'Installed DLL hash does not match the build.' }
Write-Output "Installed MapTracker $version"
Write-Output $destination
if ($IncludeMigration) {
    $migrationSource = Join-Path $PSScriptRoot 'migration\bin\Release\MapTrackerMigration.dll'
    if (-not (Test-Path -LiteralPath $migrationSource)) { throw 'Temporary migration utility is not built.' }
    $migrationDirectory = Join-Path $Profile 'BepInEx\plugins\Slikfoul-MapTrackerMigration'
    New-Item -ItemType Directory -Path $migrationDirectory -Force | Out-Null
    $migrationDestination = Join-Path $migrationDirectory 'MapTrackerMigration.dll'
    if (Test-Path -LiteralPath $migrationDestination) {
        $migrationBackupDirectory = Join-Path $Profile 'BepInEx\config\MapTracker\backups'
        New-Item -ItemType Directory -Path $migrationBackupDirectory -Force | Out-Null
        Copy-Item -LiteralPath $migrationDestination -Destination (Join-Path $migrationBackupDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-MapTrackerMigration.dll.bak'))
    }
    Copy-Item -LiteralPath $migrationSource -Destination $migrationDestination -Force
    if ((Get-FileHash -LiteralPath $migrationSource).Hash -ne (Get-FileHash -LiteralPath $migrationDestination).Hash) { throw 'Migration utility hash mismatch.' }
    Write-Output 'Installed optional one-time migration utility; command has not been executed.'
    Write-Output $migrationDestination
}

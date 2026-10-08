param(
    [string]$Profile = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default",
    [string]$Dll = "$PSScriptRoot\bin\Release\MapTracker.dll",
    [switch]$IncludeMigration
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before replacing the plugin DLL.' }
if (-not (Test-Path -LiteralPath "$Profile\BepInEx\core\BepInEx.dll")) { throw 'Profile BepInEx installation not found.' }
$sourcePath = (Resolve-Path -LiteralPath $Dll).Path
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

param(
    [string]$Game = 'G:\Games\SteamLibrary\steamapps\common\Valheim',
    [string]$Profile = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default"
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $Profile 'BepInEx\core\Mono.Cecil.dll')
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$PSScriptRoot\bin\Release\MapTracker.dll")
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$Game\valheim_Data\Managed\assembly_valheim.dll")
$guiAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$Game\valheim_Data\Managed\assembly_guiutils.dll")
try {
    foreach ($iconName in @('bat', 'hen', 'chick', 'boar-young', 'wolf-cub', 'asksvin-hatchling', 'lox-calf')) {
        $iconResource = $plugin.MainModule.Resources | Where-Object Name -EQ ("MapTracker.CreatureIcons.$iconName.png") | Select-Object -First 1
        if ($null -eq $iconResource) { throw "Missing embedded creature icon: $iconName" }
        $iconBytes = $iconResource.GetResourceData()
        if ($iconBytes.Length -lt 32 -or $iconBytes[0] -ne 137 -or $iconBytes[1] -ne 80 -or $iconBytes[2] -ne 78 -or $iconBytes[3] -ne 71) { throw "Invalid PNG resource: $iconName" }
    }
    $imageAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$Game\valheim_Data\Managed\UnityEngine.ImageConversionModule.dll")
    try {
        $imageType = $imageAssembly.MainModule.Types | Where-Object FullName -EQ 'UnityEngine.ImageConversion' | Select-Object -First 1
        if (-not ($imageType.Methods | Where-Object { $_.Name -eq 'LoadImage' -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 3 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Texture2D' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Byte[]' -and $_.Parameters[2].ParameterType.FullName -eq 'System.Boolean' })) { throw 'Game PNG decoder signature not found.' }
    } finally { $imageAssembly.Dispose() }
    $references = @($plugin.MainModule.AssemblyReferences | ForEach-Object { $_.Name })
    $nameStyle = $plugin.MainModule.Types | Where-Object Name -EQ 'NameTextStyle' | Select-Object -First 1
    $nameStyleApply = $nameStyle.Methods | Where-Object Name -EQ 'Apply' | Select-Object -First 1
    $styleCalls = @($nameStyleApply.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
    foreach ($setter in @('set_enableAutoSizing', 'set_fontSize', 'set_fontSizeMin', 'set_fontSizeMax', 'ForceMeshUpdate')) {
        if ($styleCalls -notcontains $setter) { throw "Fixed font-size update operation missing: $setter" }
    }
    $entrypoint = $plugin.MainModule.Types | Where-Object Name -EQ 'Plugin' | Select-Object -First 1
    $entrypointStrings = @($entrypoint.Methods | Where-Object HasBody | ForEach-Object {
        $_.Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr } | ForEach-Object Operand
    })
    if ($entrypointStrings -contains 'Performance report' -or $entrypointStrings -contains 'Measure performance' -or $entrypointStrings -contains 'Shortcuts' -or
        ($plugin.MainModule.Types | Where-Object Name -EQ 'PerformanceMeter')) { throw 'Removed diagnostics/shortcuts are still exposed.' }
    if ($references -contains 'Jotunn' -or $references -contains 'System.ValueTuple') { throw 'Unexpected runtime dependency.' }
    $types = @($gameAssembly.MainModule.Types) + @($guiAssembly.MainModule.Types)
    $patches = @(
        @{ Type = 'Player'; Method = 'TakeInput'; Parameters = 0 },
        @{ Type = 'TextInput'; Method = 'IsVisible'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'UpdatePins'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'OnMapLeftClick'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'OnMapDblClick'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'RemovePinUnderPointer'; Parameters = 0 },
        @{ Type = 'Teleport'; Method = 'Interact'; Parameters = 3 }
    )
    foreach ($patch in $patches) {
        $type = $types | Where-Object Name -EQ $patch.Type | Select-Object -First 1
        $methods = @($type.Methods | Where-Object { $_.Name -eq $patch.Method -and $_.Parameters.Count -eq $patch.Parameters })
        if ($methods.Count -ne 1) { throw "Missing or ambiguous patch target: $($patch.Type).$($patch.Method)" }
    }
    $scene = $types | Where-Object Name -EQ 'ZNetScene' | Select-Object -First 1
    if (-not ($scene.Fields | Where-Object Name -EQ 'm_instances')) { throw 'Network-instance registry field not found.' }
    if (-not ($scene.Methods | Where-Object { $_.Name -eq 'IsAreaReady' -and $_.Parameters.Count -eq 1 -and $_.IsPublic })) { throw 'Loaded-area readiness API not found.' }
    $zdoManager = $types | Where-Object Name -EQ 'ZDOMan' | Select-Object -First 1
    if (-not ($zdoManager.Fields | Where-Object Name -EQ 'm_onZDODestroyed')) { throw 'Confirmed ZDO destruction event not found.' }
    if (-not ($zdoManager.Methods | Where-Object { $_.Name -eq 'FindSectorObjects' -and $_.Parameters.Count -eq 4 -and $_.IsPublic })) { throw 'Sector network-object API not found.' }
    $minimap = $types | Where-Object Name -EQ 'Minimap' | Select-Object -First 1
    if (-not ($minimap.Fields | Where-Object Name -EQ 'm_pins')) { throw 'Native pin registry field not found.' }
    foreach ($playerField in @('m_playerPins', 'm_tempPlayerInfo')) {
        if (-not ($minimap.Fields | Where-Object Name -EQ $playerField)) { throw "Native player navigation field not found: $playerField" }
    }
    $networkType = $types | Where-Object Name -EQ 'ZNet' | Select-Object -First 1
    if (-not ($networkType.Methods | Where-Object { $_.Name -eq 'GetOtherPublicPlayers' -and $_.Parameters.Count -eq 1 -and $_.IsPublic })) { throw 'Public player position API not found.' }
    if (-not ($minimap.Fields | Where-Object { $_.Name -eq 'm_showNamesZoom' -and $_.FieldType.FullName -eq 'System.Single' -and $_.IsPublic })) { throw 'Native label zoom threshold not found.' }
    foreach ($markerField in @('m_largeMarker', 'm_smallMarker', 'm_pinNameRootLarge', 'm_pinNameRootSmall')) {
        if (-not ($minimap.Fields | Where-Object { $_.Name -eq $markerField -and $_.FieldType.FullName -eq 'UnityEngine.RectTransform' -and $_.IsPublic })) { throw "Native marker/name layer field not found: $markerField" }
    }
    $characterType = $types | Where-Object Name -EQ 'Character' | Select-Object -First 1
    $hudType = $types | Where-Object Name -EQ 'Hud' | Select-Object -First 1
    if (-not ($hudType.Fields | Where-Object { $_.Name -eq 'm_rootObject' -and $_.IsPublic }) -or
        -not ($hudType.Methods | Where-Object { $_.Name -eq 'IsVisible' -and $_.Parameters.Count -eq 0 -and $_.IsPublic })) { throw 'Navigation HUD root or visibility API not found.' }
    if (-not ($characterType.Methods | Where-Object { $_.Name -eq 'GetEyePoint' -and $_.Parameters.Count -eq 0 -and $_.IsPublic })) { throw 'Character eye-position API not found.' }
    $mistType = $types | Where-Object Name -EQ 'ParticleMist' | Select-Object -First 1
    if (-not ($mistType.Methods | Where-Object { $_.Name -eq 'IsMistBlocked' -and $_.Parameters.Count -eq 2 -and $_.IsPublic -and $_.IsStatic })) { throw 'Native demister-aware mist API not found.' }
    if (-not ($minimap.Fields | Where-Object { $_.Name -eq 'm_pinUpdateRequired' -and $_.FieldType.FullName -eq 'System.Boolean' })) { throw 'Native pin redraw flag not found.' }
    if (-not ($minimap.Fields | Where-Object { $_.Name -eq 'm_mapOffset' -and $_.FieldType.FullName -eq 'UnityEngine.Vector3' })) { throw 'Native map offset for discovery navigation not found.' }
    if (-not ($minimap.Methods | Where-Object { $_.Name -eq 'CenterMap' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector3' })) { throw 'Native map centering method not found.' }
    if (-not ($minimap.Methods | Where-Object { $_.Name -eq 'ScreenToWorldPoint' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector3' })) { throw 'Native waypoint coordinate conversion not found.' }
    $localizer = $guiAssembly.MainModule.Types | Where-Object Name -EQ 'Localization' | Select-Object -First 1
    if (-not ($localizer.Fields | Where-Object { $_.Name -eq 'm_localizationSettings' -and $_.IsStatic }) -or
        -not ($localizer.Methods | Where-Object { $_.Name -eq 'DoQuoteLineSplit' -and $_.Parameters.Count -eq 1 })) { throw 'English label lookup APIs not found.' }
    $pinData = $minimap.NestedTypes | Where-Object Name -EQ 'PinData' | Select-Object -First 1
    if (-not ($pinData.Fields | Where-Object Name -EQ 'm_ownerID')) { throw 'Shared pin ownership field not found.' }
    if (-not ($minimap.Methods | Where-Object { $_.Name -eq 'GetMapData' -and $_.Parameters.Count -eq 0 })) { throw 'Map backup API not found.' }
    foreach ($reference in $references) {
        if ($reference -in @('mscorlib', 'System', 'System.Core')) { continue }
        $gameFile = Join-Path "$Game\valheim_Data\Managed" "$reference.dll"
        $coreFile = Join-Path "$Profile\BepInEx\core" "$reference.dll"
        if (-not (Test-Path -LiteralPath $gameFile) -and -not (Test-Path -LiteralPath $coreFile)) {
            throw "Runtime dependency not found: $reference"
        }
    }
    $tracking = $plugin.MainModule.Types | Where-Object Name -EQ 'Tracking' | Select-Object -First 1
    foreach ($method in @('Save', 'Reset', 'Tick')) {
        if (-not ($tracking.Methods | Where-Object { $_.Name -eq $method -and $_.Parameters.Count -eq 0 })) { throw "Missing utility integration method: $method" }
    }
    if ($tracking.Methods | Where-Object Name -In @('CleanLegacyPins', 'ImportLegacyPins')) { throw 'Migration code remains in the main plugin.' }
    $router = $types | Where-Object Name -EQ 'ZRoutedRpc' | Select-Object -First 1
    if (-not ($router.Methods | Where-Object { $_.Name -eq 'Register' -and $_.GenericParameters.Count -eq 1 })) { throw 'Routed RPC registration API not found.' }
    $utilityPath = "$PSScriptRoot\migration\bin\Release\MapTrackerMigration.dll"
    if (Test-Path -LiteralPath $utilityPath) {
        $utility = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($utilityPath)
        try {
            $utilityReferences = @($utility.MainModule.AssemblyReferences | ForEach-Object Name)
            if ($utilityReferences -contains 'Jotunn' -or $utilityReferences -contains 'System.ValueTuple') { throw 'Unexpected migration runtime dependency.' }
            $utilityType = $utility.MainModule.Types | Where-Object Name -EQ 'MigrationPlugin' | Select-Object -First 1
            if ($utilityType.Methods | Where-Object Name -EQ 'Update') { throw 'Temporary utility must not run an update loop.' }
        } finally { $utility.Dispose() }
    }
    Write-Output 'PASS: no Jotunn or ValueTuple dependency; runtime dependency files exist; seven Harmony targets and network registry field match.'
    Write-Output 'PASS: shared pin ownership, map backup, routed RPC and separate manual migration integration verified.'
    Write-Output 'This static check does not validate in-game behavior.'
    Write-Output 'PASS: seven embedded PNG icons and the native game decoder signature verified.'
    Write-Output 'PASS: portal destruction event, loaded-area readiness and sector network-object APIs verified.'
} finally {
    $plugin.Dispose(); $gameAssembly.Dispose(); $guiAssembly.Dispose()
}

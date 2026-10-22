param(
    [string]$Game = 'G:\Games\SteamLibrary\steamapps\common\Valheim',
    [string]$Profile,
    [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim')
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Profile')) { $Profile = Join-Path $DataRoot 'profiles\Default' }
Add-Type -LiteralPath (Join-Path $Profile 'BepInEx\core\Mono.Cecil.dll')
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$PSScriptRoot\bin\Release\MapTracker.dll")
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$Game\valheim_Data\Managed\assembly_valheim.dll")
$guiAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$Game\valheim_Data\Managed\assembly_guiutils.dll")
try {
    foreach ($iconName in @('bat', 'hen', 'chick', 'boar-young', 'wolf-cub', 'asksvin-hatchling', 'lox-calf', 'fallen-warrior', 'frozen-king', 'frysling', 'frost-wisp', 'mistile', 'shadow-person', 'thornroot', 'tentaroot', 'tentaroot-wild', 'tendril', 'aspect-tentaroot')) {
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
    if ($plugin.MainModule.Types | Where-Object FullName -eq 'Location') { throw 'A diagnostic copy of the native Location class was compiled into the mod.' }
    $locationType = $gameAssembly.MainModule.Types | Where-Object Name -eq 'Location' | Select-Object -First 1
    if (!($locationType.Methods | Where-Object { $_.Name -eq 'GetLocation' -and $_.IsPublic -and $_.IsStatic -and $_.Parameters.Count -eq 2 }) -or
        !($locationType.Methods | Where-Object { $_.Name -eq 'GetMaxRadius' -and $_.IsPublic -and $_.Parameters.Count -eq 0 })) { throw 'Native location ownership API was not found.' }
    $objectStyle = $plugin.MainModule.Types | Where-Object Name -eq 'ObjectIconContrast' | Select-Object -First 1
    $objectCalls = @($objectStyle.Methods | Where-Object HasBody | ForEach-Object { $_.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name } })
    if ($entrypointStrings -notcontains 'Contrast object icons' -or $entrypointStrings -notcontains 'Object outline thickness' -or
        $objectCalls -notcontains 'set_Thickness' -or $objectCalls -contains 'GetPixels' -or $objectCalls -contains 'ReadPixels') { throw 'Non-creature silhouette outline settings or mesh reuse are missing.' }
    Write-Output 'PASS: native location ownership API and separate non-creature mesh outline settings verified.'
    $patches = @(
        @{ Type = 'Player'; Method = 'TakeInput'; Parameters = 0 },
        @{ Type = 'TextInput'; Method = 'IsVisible'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'UpdatePins'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'OnMapLeftClick'; Parameters = 0 },
        @{ Type = 'Minimap'; Method = 'OnMapMiddleClick'; Parameters = 1 },
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
    Write-Output 'PASS: no Jotunn or ValueTuple dependency; runtime dependency files exist; Harmony targets and network registry field match.'
    Write-Output 'PASS: shared pin ownership, map backup, routed RPC and separate manual migration integration verified.'
    Write-Output 'This static check does not validate in-game behavior.'
    Write-Output 'PASS: embedded creature PNG icons and the native game decoder signature verified.'
    Write-Output 'PASS: portal destruction event, loaded-area readiness and sector network-object APIs verified.'
    $sharing = $plugin.MainModule.Types | Where-Object Name -EQ 'MarkerSharing' | Select-Object -First 1
    $networkWriters = @($sharing.Methods | Where-Object {
        $_.HasBody -and ($_.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'ZRoutedRpc' -and $_.Operand.Name -eq 'InvokeRoutedRPC'
        })
    })
    if ($networkWriters.Count -ne 1 -or $networkWriters[0].Name -ne 'Send') { throw 'Sharing bypasses the central outbound budget.' }
    foreach ($receiver in @('ReceiveData', 'ReceiveHello', 'ReceiveAck', 'ReceiveProcessed')) {
        $method = $sharing.Methods | Where-Object Name -EQ $receiver | Select-Object -First 1
        if (-not $method -or ($method.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('InvokeRoutedRPC', 'Read', 'ReceiveCreatures', 'ReceiveDiscoveries')
        })) { throw "Synchronous receive work remains in $receiver." }
    }
    foreach ($receiver in @('ReceiveCreatures', 'ReceiveDiscoveries')) {
        $method = $tracking.Methods | Where-Object Name -EQ $receiver | Select-Object -First 1
        if ($method.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('RefreshPins', 'Save')
        }) { throw "Full map/database refresh remains in $receiver." }
    }
    $socket = $gameAssembly.MainModule.Types | Where-Object Name -EQ 'ISocket' | Select-Object -First 1
    if (-not ($socket.Methods | Where-Object { $_.Name -eq 'GetSendQueueSize' -and $_.ReturnType.FullName -eq 'System.Int32' })) {
        throw 'Native socket queue API signature changed.'
    }
    Write-Output 'PASS: bounded RPC sender, deferred receive callbacks, incremental map updates and native socket queue API verified.'
    foreach ($name in @('StylePins', 'EnforceNameVisibility', 'UpdateNearbyNames', 'OwnCreatures', 'UpdateCreatureSearch', 'CanSearchCreature')) {
        $method = $tracking.Methods | Where-Object Name -EQ $name | Select-Object -First 1
        if ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'records' }) {
            throw "Saved discovery archive is traversed by per-frame path $name."
        }
    }
    $style = $tracking.Methods | Where-Object Name -EQ 'StylePins' | Select-Object -First 1
    $styleCalls = @($style.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    if ([Array]::IndexOf($styleCalls, 'MarkerGrouping.UpdateNames') -lt [Array]::IndexOf($styleCalls, 'CreatureNameGroups.Apply')) {
        throw 'Caption update precedes name grouping and may dirty TMP twice.'
    }
    $tick = $tracking.Methods | Where-Object Name -EQ 'Tick' | Select-Object -First 1
    if (@($tick.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'discoveryIndex' }).Count -ne 1) {
        throw 'Discovery index lifecycle changed; verify it is retained across scans.'
    }
    Write-Output 'PASS: per-frame map paths use active working sets; spatial index survives scans; captions update after name grouping.'
    $load = $tracking.Methods | Where-Object Name -EQ 'Load' | Select-Object -First 1
    if ($load.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('GetSavedDefinition', 'GetLegacyDungeon', 'RemoveDuplicates', 'RefreshPins')
    }) { throw 'Saved-file loading binds definitions or performs repeated map/archive reconciliation.' }
    if ($tracking.Fields | Where-Object Name -EQ 'nextEarlyRefresh') { throw 'Polling refresh during catalog startup remains.' }
    $tickCalls = @($tick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    if ($tickCalls -notcontains 'FrameWork.Advance') { throw 'Saved-marker preparation is not frame-budgeted.' }
    $sharingTick = $sharing.Methods | Where-Object Name -EQ 'Tick' | Select-Object -First 1
    if (-not ($sharingTick.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'Tracking' -and $_.Operand.Name -eq 'get_Ready'
    })) { throw 'Sharing starts before saved-marker preparation completes.' }
    Write-Output 'PASS: no startup polling refresh; deferred budgeted marker preparation and sharing readiness gate verified.'
    $pinAdvance = $tracking.Methods | Where-Object Name -EQ 'AdvancePinPreparation' | Select-Object -First 1
    if (-not ($pinAdvance.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'FrameWork' -and $_.Operand.Name -eq 'Advance' })) {
        throw 'Full pin preparation must use the frame scheduler.'
    }
    $ownDiscoveries = $tracking.Methods | Where-Object Name -EQ 'OwnDiscoveries' | Select-Object -First 1
    if ($ownDiscoveries.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] }) {
        throw 'Outbound discovery access must reuse the maintained roster without scanning or copying the archive.'
    }
    foreach ($methodName in @('Observe','ReceiveDiscoveries')) {
        $method = $tracking.Methods | Where-Object Name -EQ $methodName | Select-Object -First 1
        if (-not ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'CatalogContent' -and $_.Operand.Name -eq 'CanTrack' })) {
            throw ('Technical-definition exclusion missing from ' + $methodName)
        }
    }
    $catalog = $plugin.MainModule.Types | Where-Object Name -EQ 'Catalog' | Select-Object -First 1
    $export = $catalog.Methods | Where-Object Name -EQ 'Export' | Select-Object -First 1
    if (-not ($export.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'CatalogPersistence' -and $_.Operand.Name -eq 'Prepare' }) -or
        ($export.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -in @('DataJson','File') })) {
        throw 'Catalog export must prepare detached metadata incrementally instead of serializing/writing on the game thread.'
    }
    $advanceExport = $catalog.Methods | Where-Object Name -EQ 'AdvanceExport' | Select-Object -First 1
    if (-not ($advanceExport.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'FrameWork' -and $_.Operand.Name -eq 'Advance' })) { throw 'Catalog export preparation must use the frame scheduler.' }
    $exportCallbacks = @($catalog.NestedTypes | ForEach-Object { $_.Methods } | Where-Object HasBody)
    if (-not ($exportCallbacks | ForEach-Object { $_.Body.Instructions } | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'CatalogPersistence' -and $_.Operand.Name -eq 'Enqueue' })) { throw 'Prepared catalog metadata must be queued for background serialization.' }
    if ($load.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'File' -and $_.Operand.Name -eq 'ReadAllText' }) { throw 'Saved-marker loading must not allocate a whole archive-sized UTF-16 string.' }
    Write-Output 'PASS: budgeted full pin refresh, retained outbound roster, technical exclusions and background catalog export verified.'
    if ($tick.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_Item' -and
        $_.Operand.DeclaringType.FullName -like '*Dictionary*MapTracker.MarkerRecord*'
    }) { throw 'Tracking.Tick still dereferences a removed record through a dictionary indexer.' }
    $destroyed = $tracking.Methods | Where-Object Name -EQ 'Destroyed' | Select-Object -First 1
    if (-not ($destroyed.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ForgetRecord'
    }) -or ($destroyed.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshPins'
    })) { throw 'Destruction bypasses consistent retirement or refreshes the full archive.' }
    $lookup = $tracking.Methods | Where-Object Name -EQ 'FindNavigationObject' | Select-Object -First 1
    if ($lookup.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'SharedDiscoveries' -and $_.Operand.Name -eq 'Find'
    }) { throw 'Shared navigation still performs whole-archive fallback searches.' }
    Write-Output 'PASS: safe record retirement, incremental destruction refresh and indexed shared-navigation lookup verified.'
    foreach ($method in $sharing.Methods | Where-Object HasBody) {
        if ($method.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'OtherPlayerPins'
        }) { throw 'Ordinary player-pin visibility still controls mod networking.' }
    }
    $visibility = $tracking.Methods | Where-Object Name -EQ 'Visible' | Select-Object -First 1
    if ($visibility.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'OtherPlayerPins'
    }) { throw 'Ordinary player-pin visibility still hides received mod discoveries.' }
    Write-Output 'PASS: Other player pins affects native pin masking only; mod networking and marker visibility are independent.'
    $save = $tracking.Methods | Where-Object Name -EQ 'Save' | Select-Object -First 1
    $saveCalls = @($save.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    if ($saveCalls -notcontains 'SavedMarkerPreparation.Prepare' -or $saveCalls -contains 'DiscoverySave.Snapshot' -or
        $saveCalls -contains 'DataJson.Write' -or $saveCalls -contains 'File.WriteAllText') { throw 'Game save path must schedule bounded preparation without a whole-archive clone or serialization.' }
    $advanceSave = $tracking.Methods | Where-Object Name -EQ 'AdvanceSavePreparation' | Select-Object -First 1
    if (-not ($advanceSave.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'FrameWork' -and $_.Operand.Name -eq 'Advance'
    }) -or -not ($tick.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AdvanceSavePreparation'
    })) { throw 'Incremental save preparation is not advanced through the frame scheduler.' }
    $reset = $tracking.Methods | Where-Object Name -EQ 'Reset' | Select-Object -First 1
    if (@($reset.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CompleteSave' }).Count -ne 2) {
        throw 'World exit does not flush pending and latest database snapshots.'
    }
    if (@($reset.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AdvanceSavePreparation' }).Count -ne 2) {
        throw 'World exit does not finish pending and latest incremental snapshots.'
    }
    $saveWorker = $plugin.MainModule.Types | Where-Object Name -EQ 'DiscoverySave' | Select-Object -First 1
    $write = $saveWorker.Methods | Where-Object Name -EQ 'Write' | Select-Object -First 1
    $writeCalls = @($write.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    $preparedWrite = $saveWorker.Methods | Where-Object Name -EQ 'WritePreparedAsync' | Select-Object -First 1
    if (-not ($preparedWrite.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Run' -and $_.Operand.DeclaringType.FullName -eq 'System.Threading.Tasks.Task'
    })) { throw 'Prepared marker serialization is not scheduled on a worker.' }
    if ($writeCalls -notcontains 'SavedMarkerFile.WriteTo' -or $writeCalls -notcontains 'DataJson.WriteTo' -or $writeCalls -contains 'DataJson.Write' -or $writeCalls -contains 'File.WriteAllText') {
        throw 'Save worker still allocates a whole database-sized JSON string.'
    }
    Write-Output 'PASS: typed background streamed persistence and world-exit save flushing verified.'
    $reconcilePortals = $tracking.Methods | Where-Object Name -EQ 'ReconcilePortals' | Select-Object -First 1
    if (-not ($reconcilePortals.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'portalRecords' }) -or
        ($reconcilePortals.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'records' }) -or
        -not ($reconcilePortals.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'PortalReconciliation' -and $_.Operand.Name -eq 'Eligible' })) {
        throw 'Portal reconciliation must validate the dedicated portal set, including peer discoveries, without scanning the whole archive.'
    }
    Write-Output 'PASS: local and shared portal validation uses the dedicated portal index.'
    $mapClick = $tracking.Methods | Where-Object Name -EQ 'MapClick' | Select-Object -First 1
    $clickCalls = @($mapClick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    if ($clickCalls -notcontains 'DungeonProgress.Toggle' -or $clickCalls -notcontains 'Plugin.ProgressChanged') {
        throw 'Dungeon click does not record and prioritize manual completion revisions.'
    }
    $receiveProgress = $tracking.Methods | Where-Object Name -EQ 'ReceiveDiscoveries' | Select-Object -First 1
    foreach ($field in @('completionRevision','completionAuthor','dirty','pinChanges')) {
        if (-not ($receiveProgress.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq $field })) {
            throw ('Received progress is not integrated into persistence/rendering: '+$field)
        }
    }
    $prioritize = $sharing.Methods | Where-Object Name -EQ 'PrioritizeDiscovery' | Select-Object -First 1
    if ($prioritize.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('Send','InvokeRoutedRPC') }) {
        throw 'Priority updates bypass the normal send scheduler.'
    }
    Write-Output 'PASS: revisioned dungeon clicks, incremental received-progress persistence/rendering and budgeted priority integration verified.'
    $container = $gameAssembly.MainModule.Types | Where-Object Name -EQ 'Container' | Select-Object -First 1
    if (-not ($container.Methods | Where-Object { $_.Name -eq 'UpdateUseVisual' -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void' })) {
        throw 'Native chest opening visual callback changed.'
    }
    $chestVisit = $tracking.Methods | Where-Object Name -EQ 'CharredChestOpened' | Select-Object -First 1
    $chestCalls = @($chestVisit.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.Name + '.' + $_.Operand.Name })
    if ($chestCalls -notcontains 'Location.GetLocation' -or $chestCalls -notcontains 'FortressVisit.Mark' -or $chestCalls -notcontains 'Plugin.ProgressChanged') {
        throw 'Fortress visits lack native ownership validation or ordinary progress sharing.'
    }
    if ($chestCalls | Where-Object { $_ -match '\.(Save|RefreshPins|Send|InvokeRoutedRPC)$' }) {
        throw 'Chest visual callback performs a whole save/refresh or direct network send.'
    }
    $classify = ($plugin.MainModule.Types | Where-Object Name -EQ 'Catalog' | Select-Object -First 1).Methods | Where-Object Name -EQ 'BindSettings' | Select-Object -First 1
    if (-not ($classify.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ClassifyLocation' })) {
        throw 'Fresh and cached definitions do not share fortress category normalization.'
    }
    Write-Output 'PASS: native chest-open callback, fortress ownership, cached classification and deferred progress/persistence verified.'
} finally {
    $plugin.Dispose(); $gameAssembly.Dispose(); $guiAssembly.Dispose()
}

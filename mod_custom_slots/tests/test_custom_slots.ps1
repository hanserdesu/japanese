$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$model = Get-Content -Raw (Join-Path $root 'CustomSlotModel.cs')
$plugin = Get-Content -Raw (Join-Path $root 'CustomSlotsMod.cs')
$pass = 0
$fail = 0
function Check([bool]$ok, [string]$name) {
    if ($ok) { Write-Host "  PASS  $name"; $script:pass++ }
    else { Write-Host "  FAIL  $name"; $script:fail++ }
}
Check ($model -match 'MaxSlots = 20') 'logical slot limit is 20'
Check ($model -match 'NativeSlots = 4') 'native slot boundary remains 4'
Check ($plugin -match 'ScrollRect') 'single scrollable page'
Check ($plugin -match 'WcpCustomSlots\.seed\.json') 'installer seed interface'
Check ($plugin -match 'NativeSlotOwnedByManaged') 'external book overwrite boundary'
Check ($plugin -match 'NativeSlotOwnedByManaged') 'only reuse mod-owned native slot'
Check ($plugin -notmatch 'SelfBookList5') 'no fake fifth native slot'
Write-Host "`nResult: $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }

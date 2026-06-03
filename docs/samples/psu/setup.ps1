# PSU mock VISA device — PowerShell-native idempotent setup.
#
# Builds the v0.2.0 two-state FSM (off / on) directly from CLI verbs;
# the equivalent ready-to-drop TOML is in psu-bench.toml next to
# this script.
#
# Requires: pwsh 7+, ivicli on $PATH (e.g. dotnet tool install -g
# ivi-cli >= 0.2.0, or the GitHub Releases self-contained binary
# placed on PATH).
#
# Usage:
#   .\setup.ps1
#   .\setup.ps1 -Proto socket -Port 5025
#   .\setup.ps1 -Port 4881

[CmdletBinding()]
param(
    [string]$Scenario = 'psu-bench',
    [ValidateSet('hislip', 'socket')]
    [string]$Proto = 'hislip',
    [int]$Port = 4880,
    [string]$SubAddr = 'hislip0',
    [string]$Server = "$Proto-psu",
    [string]$Device = 'psu_mock'
)

$ErrorActionPreference = 'Continue'

function Invoke-IvicliStep {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string[]]$Args
    )
    Write-Host "==> $Label" -ForegroundColor Cyan
    & ivicli @Args
    if ($LASTEXITCODE -ne 0) {
        Write-Host "   (non-zero exit — assuming already-exists, continuing)" -ForegroundColor DarkGray
    }
}

Write-Host "==> scenario=$Scenario  proto=$Proto  port=$Port  device=$Device  server=$Server" -ForegroundColor Yellow

# 1) Scenario + scenes — FSM (off/on).
Invoke-IvicliStep 'create scenario'  @('mock', 'scenario', 'create', $Scenario)
Invoke-IvicliStep 'add scene off'    @('mock', 'scenario', 'scene', 'add', $Scenario, 'off')
Invoke-IvicliStep 'add scene on'     @('mock', 'scenario', 'scene', 'add', $Scenario, 'on')

# Static metadata, duplicated across both scenes (v0.2.0 limitation —
# scenes do not share rules; key-value variable state is a follow-up).
$staticRules = @(
    @{ Match = '*IDN?';      Respond = 'IVICLI-MOCK,PSU,SN0001,1.0.0' }
    @{ Match = '*RST';       Ack     = $true; Transition = 'off' }
    @{ Match = '*OPC?';      Respond = '1' }
    @{ Match = 'VOLT 5.0';   Ack     = $true }
    @{ Match = 'VOLT?';      Respond = '5.000' }
    @{ Match = 'CURR 1.0';   Ack     = $true }
    @{ Match = 'CURR?';      Respond = '1.000' }
    @{ Match = 'SYST:ERR?';  Respond = '0,"No error"' }
)
foreach ($scene in @('off', 'on')) {
    foreach ($r in $staticRules) {
        $a = @('mock', 'scenario', 'rule', 'add', $Scenario, '--in', $scene, '--match', $r.Match)
        if ($r.ContainsKey('Respond')) { $a += @('--respond', $r.Respond) }
        if ($r.ContainsKey('Ack'))     { $a += '--ack' }
        if ($r.ContainsKey('Transition')) { $a += @('--transition-to', $r.Transition) }
        Invoke-IvicliStep ("rule [$scene] " + $r.Match) $a
    }
}

# off-specific
$offRules = @(
    @{ Match = 'OUTP?';      Respond = '0' }
    @{ Match = 'OUTP ON';    Ack     = $true; Transition = 'on' }
    @{ Match = 'OUTP OFF';   Ack     = $true }
    @{ Match = 'MEAS:VOLT?'; Respond = '0.001' }
    @{ Match = 'MEAS:CURR?'; Respond = '0.000' }
)
foreach ($r in $offRules) {
    $a = @('mock', 'scenario', 'rule', 'add', $Scenario, '--in', 'off', '--match', $r.Match)
    if ($r.ContainsKey('Respond')) { $a += @('--respond', $r.Respond) }
    if ($r.ContainsKey('Ack'))     { $a += '--ack' }
    if ($r.ContainsKey('Transition')) { $a += @('--transition-to', $r.Transition) }
    Invoke-IvicliStep ("rule [off] " + $r.Match) $a
}

# on-specific
$onRules = @(
    @{ Match = 'OUTP?';      Respond = '1' }
    @{ Match = 'OUTP OFF';   Ack     = $true; Transition = 'off' }
    @{ Match = 'OUTP ON';    Ack     = $true }
    @{ Match = 'MEAS:VOLT?'; Respond = '4.998' }
    @{ Match = 'MEAS:CURR?'; Respond = '0.823' }
)
foreach ($r in $onRules) {
    $a = @('mock', 'scenario', 'rule', 'add', $Scenario, '--in', 'on', '--match', $r.Match)
    if ($r.ContainsKey('Respond')) { $a += @('--respond', $r.Respond) }
    if ($r.ContainsKey('Ack'))     { $a += '--ack' }
    if ($r.ContainsKey('Transition')) { $a += @('--transition-to', $r.Transition) }
    Invoke-IvicliStep ("rule [on] " + $r.Match) $a
}

# 2) Activate
Invoke-IvicliStep 'activate scenario' @('mock', 'scenario', 'activate', $Scenario)

# 3) Logical device alias (DeviceName regex = ^[a-z][a-z0-9_]*$ — no hyphens)
Invoke-IvicliStep "device alias '$Device'" @('visa', 'add', $Device, 'TCPIP0::127.0.0.1::INSTR')

# 4) Gateway server
Invoke-IvicliStep "server '$Server'" @('server', 'add', $Server, '--type', $Proto, '--port', "$Port")

# 5) Route
if ($Proto -eq 'hislip') {
    Invoke-IvicliStep "route $Server/$SubAddr -> $Device" @('server', 'route', 'add', $Server, $SubAddr, $Device)
} else {
    Invoke-IvicliStep "route $Server/$Port -> $Device" @('server', 'route', 'add', $Server, "$Port", $Device)
}

# 6) Start. Since v0.1.3, the gateway honours the active scenario at
# backend-dispatch time, so IVICLI_MOCK_ONLY=1 is not required.
Invoke-IvicliStep "start $Server" @('server', 'start', $Server)

Write-Host ''
Write-Host '==> mock PSU is live (state: off).' -ForegroundColor Green
if ($Proto -eq 'hislip') {
    Write-Host "    Resource: TCPIP::localhost::$SubAddr::INSTR  (HiSLIP, port $Port)" -ForegroundColor Green
} else {
    Write-Host "    Resource: TCPIP::localhost::$Port::SOCKET    (raw socket)" -ForegroundColor Green
}
Write-Host ''
Write-Host 'Smoke test:'
if ($Proto -eq 'hislip') {
    Write-Host "    ivicli visa add tester 'TCPIP::localhost::$SubAddr::INSTR'"
} else {
    Write-Host "    ivicli visa add tester 'TCPIP::localhost::$Port::SOCKET'"
}
Write-Host "    ivicli visa query tester 'OUTP?'    # -> 0"
Write-Host "    ivicli visa write tester 'OUTP ON'"
Write-Host "    ivicli visa query tester 'OUTP?'    # -> 1  (state switched to on)"
Write-Host "    ivicli visa query tester 'MEAS:VOLT?'  # -> 4.998 (was 0.001 when off)"
Write-Host "    ivicli visa write tester 'OUTP OFF'"
Write-Host "    ivicli visa query tester 'OUTP?'    # -> 0  (back to off)"
Write-Host ''
Write-Host 'NI MAX registration (for apps that use NI-VISA, e.g. ImageDataGetter):'
Write-Host '  Devices and Interfaces -> Network Devices -> right-click ->'
Write-Host '  Create New -> "VISA TCP/IP Resource..." -> "Manual Entry of LAN Instrument" ->'
if ($Proto -eq 'hislip') {
    Write-Host "  Hostname: 127.0.0.1   LAN Device Name: $SubAddr"
} else {
    Write-Host "  Hostname: 127.0.0.1   (TCP/IP Socket, Port: $Port)"
}
Write-Host '  -> Validate -> Finish'

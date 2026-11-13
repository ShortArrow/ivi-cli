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
#
# The server runs in the foreground until Ctrl+C; `ivicli mock unserve
# psu-mock` then removes the device and server it registered.

[CmdletBinding()]
param(
    [string]$Scenario = 'psu-bench',
    [ValidateSet('hislip', 'socket')]
    [string]$Proto = 'hislip',
    [int]$Port = 4880,
    [string]$Name = 'psu-mock'
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

Write-Host "==> scenario=$Scenario  proto=$Proto  port=$Port  name=$Name" -ForegroundColor Yellow

# 1) Scenario + scenes — FSM (off/on).
# --initial off makes the FSM start in the `off` scene, so the
# synthetic `default` scene v0.2.0 used to auto-create is skipped.
Invoke-IvicliStep 'create scenario'  @('mock', 'scenario', 'create', $Scenario, '--initial', 'off')
Invoke-IvicliStep 'add scene on'     @('mock', 'scene', 'add', $Scenario, 'on')

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
        $a = @('mock', 'rule', 'add', $Scenario, '--in', $scene, '--match', $r.Match)
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
    $a = @('mock', 'rule', 'add', $Scenario, '--in', 'off', '--match', $r.Match)
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
    $a = @('mock', 'rule', 'add', $Scenario, '--in', 'on', '--match', $r.Match)
    if ($r.ContainsKey('Respond')) { $a += @('--respond', $r.Respond) }
    if ($r.ContainsKey('Ack'))     { $a += '--ack' }
    if ($r.ContainsKey('Transition')) { $a += @('--transition-to', $r.Transition) }
    Invoke-IvicliStep ("rule [on] " + $r.Match) $a
}

if ($Proto -eq 'hislip') {
    $resource = "TCPIP::127.0.0.1::hislip0,$Port::INSTR"
    $lanDeviceName = if ($Port -eq 4880) { 'hislip0' } else { "hislip0,$Port" }
} else {
    $resource = "TCPIP::127.0.0.1::$Port::SOCKET"
}

Write-Host ''
Write-Host 'Smoke test, from another terminal once the server listens:'
Write-Host "    ivicli visa add tester '$resource'"
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
    Write-Host "  Hostname: 127.0.0.1   LAN Device Name: $lanDeviceName"
} else {
    Write-Host "  Hostname: 127.0.0.1   (TCP/IP Socket, Port: $Port)"
}
Write-Host '  -> Validate -> Finish'
Write-Host ''

# 2) Serve the scenario: registers the device and the server under $Name
# and runs the server in the foreground.
& ivicli mock serve $Scenario --type $Proto --port "$Port" --alias $Name
exit $LASTEXITCODE

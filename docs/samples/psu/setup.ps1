# PSU mock VISA device — PowerShell-native idempotent setup.
#
# Same outcome as setup.sh, but uses PowerShell parameter handling
# and tolerates re-runs without the bash-only `|| true` idiom (which
# PowerShell 7+ parses as a pipeline-chain operator and then fails
# because `true` is not a PowerShell command).
#
# Requires: pwsh 7+, ivicli on $PATH (e.g. dotnet tool install -g
# ivi-cli, or the GitHub Releases self-contained binary placed on
# PATH).
#
# Usage:
#   .\setup.ps1
#   .\setup.ps1 -Proto socket -Port 5025
#   .\setup.ps1 -Port 4881
#
# Idempotent: re-runs skip items that already exist.

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

# 1) Scenario + scenes
Invoke-IvicliStep 'create scenario' @('mock', 'scenario', 'create', $Scenario)

$scenes = @(
    @{ Match = '*IDN?';      Respond = 'IVICLI-MOCK,PSU,SN0001,1.0.0' }
    @{ Match = '*RST';       Ack     = $true }
    @{ Match = '*OPC?';      Respond = '1' }
    @{ Match = 'OUTP ON';    Ack     = $true }
    @{ Match = 'OUTP OFF';   Ack     = $true }
    @{ Match = 'OUTP?';      Respond = '1' }
    @{ Match = 'VOLT 5.0';   Ack     = $true }
    @{ Match = 'VOLT?';      Respond = '5.000' }
    @{ Match = 'CURR 1.0';   Ack     = $true }
    @{ Match = 'CURR?';      Respond = '1.000' }
    @{ Match = 'MEAS:VOLT?'; Respond = '4.998' }
    @{ Match = 'MEAS:CURR?'; Respond = '0.823' }
    @{ Match = 'SYST:ERR?';  Respond = '0,"No error"' }
)

foreach ($s in $scenes) {
    $args = @('mock', 'scenario', 'scene', 'add', $Scenario, '--match', $s.Match)
    if ($s.ContainsKey('Respond')) { $args += @('--respond', $s.Respond) }
    if ($s.ContainsKey('Ack'))     { $args += '--ack' }
    Invoke-IvicliStep "scene: $($s.Match)" $args
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

# 6) Start. Since v0.1.3, the gateway honors the active scenario at
# backend-dispatch time (#25), so we don't need IVICLI_MOCK_ONLY=1 on
# the host CLI path — the gateway will route to the FakeBackend
# automatically when a scenario is active. The container path keeps
# using IVICLI_MOCK_ONLY=1 to skip all real-transport DI registrations.
Invoke-IvicliStep "start $Server" @('server', 'start', $Server)

Write-Host ''
Write-Host '==> mock PSU is live.' -ForegroundColor Green
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
Write-Host "    ivicli visa query tester '*IDN?'"
Write-Host '    # -> IVICLI-MOCK,PSU,SN0001,1.0.0'
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

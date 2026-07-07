[English](PRD.md) | **日本語**

---

# PRD (Product Requirements Document)

## 1. 製品概要

`IVI-CLI` は、VISA/IVI ベースの計測器環境を管理・診断・操作するための統合CLIツールである。

SCPI 操作だけでなく、VISA/IVI 環境診断・alias 管理・remote instrument gateway・CI/AI integration を統合する。

---

# 2. ゴール

## 2.1 Primary Goals

### G1. VISA/IVI 環境の可視化

VISA implementation・device・logical name・backend 状態をCLIから把握できる。

---

### G2. 計測器操作UXの改善

毎回 VISA resource を入力せず：

```bash
ivicli visa use psu1
ivicli visa query "*IDN?"
```

のような状態保持型UXを提供する。

---

### G3. Remote Instrument Operation

別PCに接続された計測器群を：

```bash
ivicli --server lab visa query psu1 "*IDN?"
```

のように透過的に操作できる。

---

### G4. Automation Friendly

CI/CD・PowerShell・bash・Python・AI Agent・Remote Lab に適合する。

---

# 3. 非ゴール

以下は初期スコープ外：

* GUI
* 波形解析
* ドライバ自動生成
* 完全なIVI Configuration Store互換
* NI MAX互換UI
* VISA implementation 自体の提供
* オシロ画面キャプチャ解析

---

# 4. ターゲットユーザー

## Primary

* 計測器制御 / テスト自動化エンジニア
* 組み込み / FPGA 開発者
* SCPI/VISA 利用者

## Secondary

* Remote Lab 管理者
* CI / AI Agent 開発者

---

# 5. コアコンセプト

## 5.1 VISA Resource Compatibility

IVI-CLI は既存 VISA ecosystem との互換性を維持する。

内部的には VISA resource string を扱うが、
ユーザー操作では alias / logical name を優先する。

---

## 5.2 Alias / Logical Name

長い VISA resource を：

```text
psu1
scope1
dmm1
```

へマッピング。

---

## 5.3 Backend Abstraction

CLIは backend 非依存。

```text
LocalVisaBackend
HiSlipBackend
Vxi11Backend
SocketBackend
FakeBackend
ReplayBackend
```

を透過利用可能。

---

# 6. コマンド体系

## 6.1 Command Namespace

```text
ivicli
 ├─ visa      # data plane / VISA transport / SCPI operation
 ├─ server    # gateway / remote instrument publishing
 ├─ logical   # logical name management
 ├─ config    # configuration management
 ├─ doctor  # environment diagnostics
 └─ driver    # IVI driver management
```

---

## 6.2 VISA Operations

### visa scan

```bash
ivicli visa scan                                   # LXI mDNS + VXI-11 broadcast
ivicli visa scan --port 5025 --port 1394           # ローカルサブネットも TCP sweep
ivicli visa scan --port 1394 --host 192.168.0.110  # 既知の単一ホストのみ probe
ivicli visa scan --port 5025 --subnet 10.0.0.0/24  # 明示サブネットを sweep
ivicli visa scan --verbose                         # *IDN? 送信 + 解決した Core port 表示
```

現在見えている VISA resource を列挙。発見結果は host ごとにグループ化され、
1台のデバイスの VXI-11 / HiSLIP / SCPI-RAW アクセス手段がまとまって並ぶ。各
host は well-known な計測器ポート（4880・5025・および `--port` 指定分）を
probe し、発見に応答したプロトコルだけでなく受け付ける全プロトコルを表示する。

`--port <n>`（複数指定可）は、broadcast/mDNS に応答しない raw-SOCKET 計測器
（例：ベンダー固有ポートの Keithley）を見つけるため、ローカルサブネットを
追加で TCP sweep する。sweep はオプトインで `/24` 以下のサブネットに限定。
`--subnet`/`--host` で対象を上書き。`--verbose` は各 SOCKET エンドポイントへ
`*IDN?` を送り機種名を表示する。

表示例：

```text
[1] 192.168.0.10
      TCPIP0::192.168.0.10::inst0::INSTR
      TCPIP0::192.168.0.10::hislip0::INSTR
      TCPIP0::192.168.0.10::5025::SOCKET

[2] 192.168.0.110
      TCPIP0::192.168.0.110::1394::SOCKET
```

---

### visa add

```bash
ivicli visa add psu1 TCPIP0::192.168.0.10::inst0::INSTR
ivicli visa add 1 psu1
```

VISA resource または scan index から alias を登録する。

---

### visa list

```bash
ivicli visa list
```

登録済み VISA target 一覧を表示する。

---

### visa use

```bash
ivicli visa use psu1
ivicli visa use 1
ivicli visa use psu1 --default
```

current VISA target を設定する。

`--default` を指定した場合、default target として永続化する。

---

### visa current

```bash
ivicli visa current
```

現在選択中の target を表示する。

---

### visa query

```bash
ivicli visa query "*IDN?"
ivicli visa query psu1 "*IDN?"
```

SCPI query を送信し、応答を表示する。

---

### visa write

```bash
ivicli visa write "OUTP ON"
ivicli visa write psu1 "OUTP ON"
```

SCPI command を送信する。

---

### visa read

```bash
ivicli visa read
ivicli visa read psu1
```

現在の VISA session から応答を読み取る。

---

### visa status

```bash
ivicli visa status
ivicli visa status psu1
```

VISA target の接続状態・応答時間・IDNを表示する。

---

### visa watch

```bash
ivicli visa watch                 # 登録された全 device を 1 秒間隔
ivicli visa watch psu1 dmm1 --interval 500
ivicli visa watch --plain --count 3
ivicli visa watch --json | jq
```

登録された全 device（または指定したサブセット）のオンライン状態・レイテンシ・直近の IDN レスポンスをライブ表示する。既定は Spectre.Console のライブテーブル。`--plain` は CI / ログ取込み向けの ANSI レス・スナップショット、`--json` は 1 tick = 1 NDJSON オブジェクトを stdout に書く。Ctrl+C で終了。

---

### visa lint

```bash
ivicli visa lint smoke.scpi
ivicli visa lint smoke.scpi --json | jq
```

`.scpi` スクリプトを実行せずに静的解析する。IEEE 488.2 + SCPI Volume 1 の語彙（ADR 0032）に対する未知のコマンドルートを検出する。v1 はルートレベルの不整合のみ報告。フルコロンパス検証 / パラメータ構文検証はベンダー固有拡張と合わせて延期。終了コード: ファイル IO / パース失敗 → usage error、`Error` レベルの finding があれば generic failure、warning のみは 0。

---

## 6.3 Server Operations

`server` namespace は、ローカル VISA resource を remote instrument gateway として公開・管理する control plane として扱う。

### server start

```bash
ivicli server start
ivicli server start --protocol hislip
```

gateway server を起動する。

---

### server stop

```bash
ivicli server stop
```

gateway server を停止する。

---

### server status

```bash
ivicli server status
```

server 状態と公開 route を表示する。

---

### server route list

```bash
ivicli server route list
```

```text
[hislip0] -> psu1
[hislip1] -> dmm1
```

---

### server route add

```bash
ivicli server route add hislip0 psu1
```

public instrument endpoint を local device へ紐づける。

---

### server route remove

```bash
ivicli server route remove hislip0
```

route を削除する。

---

## 6.4 Diagnostics

### doctor

```bash
ivicli doctor
```

以下を診断：

* IVI Shared Components
* VISA DLL
* VISA implementation
* PATH
* Backend selection

---

### visa traffic capture

`IVICLI_CAPTURE=<path>`（絶対パス、または rolling-log ディレクトリからの相対パス）を設定すると、CLI 全体の backend 操作が UTF-8 NDJSON ファイルにストリームされる。1 行 1 イベントで、`timestamp` / `device` / `op` (`Open` / `Close` / `Write` / `Query` / `Read`) / `data` / `response` / `ok` / `latencyMs` / `error` を含む。環境変数未設定なら null sink（ゼロオーバヘッド）。sink 失敗は飲み込まれ、verb は audit sink の失敗で落ちない。詳細は [ADR 0031](adr/0031-visa-traffic-capture.md)。

---

## 6.5 Driver / IVI Features

`visa` namespace は VISA transport / SCPI operation を担当する。

メーカー固有 IVI driver / logical name 操作は
`driver` / `logical` namespace に分離する。

---

### driver list

```bash
ivicli driver list
```

インストール済み IVI driver を列挙する。

---

### logical list

```bash
ivicli logical list
```

IVI logical name を列挙する。

---

# 7. Remote Server

## 7.1 Remote Server Strategy

Remote Server は、独自RPCを第一候補にせず、既存 VISA client から利用できる業界標準プロトコルへの互換性を優先する。

優先順位は以下とする。

```text
1. HiSLIP-compatible server
2. VXI-11-compatible server
3. Raw TCP Socket endpoint
4. IVI-CLI management API
```

`ivicli server` は、単なる独自 gRPC gateway ではなく、既存 VISA client から TCPIP VISA resource として接続できる remote instrument gateway を目指す。

---

## 7.2 HiSLIP-compatible Server

### 起動

```bash
ivicli server start --protocol hislip
```

または：

```bash
ivicli server serve --protocol hislip
```

### 想定される VISA resource

```text
TCPIP0::192.168.0.50::hislip0::INSTR
```

HiSLIP endpoint は、IVI-CLI 内部の device registry と紐づける。

例：

```toml
[[servers]]
name = "lab"
type = "hislip"
bind = "0.0.0.0"
port = 4880

[[routes]]
server = "lab"
hislip_name = "hislip0"
device = "psu1"
```

---

## 7.3 VXI-11-compatible Server

VXI-11 はサーバ／クライアント両側を Batch D で実装した。`Vxi11GatewayServer` (サーバ、ADR 0029) と `Vxi11Backend` (クライアント) が `create_link` / `device_write` / `device_read` / `device_clear` / `destroy_link` ＋同一ポート上の portmapper GETPORT をサポートする。Abort / Interrupt チャネル、locking、trigger、本来の port-111 portmapper 通信は引き続き未対応。

```bash
ivicli server start --protocol vxi11
```

---

## 7.4 Raw TCP Socket Endpoint

SCPI over raw TCP socket を提供する。

```bash
ivicli server start --protocol socket --port 5025
```

想定される VISA resource：

```text
TCPIP0::192.168.0.50::5025::SOCKET
```

Raw socket は実装が単純だが、locking / SRQ 等の高度な計測器制御は限定的となる。

---

## 7.5 IVI-CLI Management API

HiSLIP / VXI-11 / Socket は既存 VISA client 互換のために提供する。

一方で、以下の管理機能は独自 management API として提供する。

* device registry
* alias / logical name 管理
* route 管理
* status / monitor
* audit log
* authentication
* JSON output
* AI agent integration

**Batch I で HTTP JSON として実装済み**（[ADR 0034](adr/0034-management-api.md)）。ASP.NET Core minimal API を CLI プロセス内に埋め込む形で動作し、`ivicli api start [--port 8080] [--bind 127.0.0.1]` で起動する。v1 エンドポイント: `GET /v1/{devices,servers,scenarios}` + `GET /v1/devices/{name}/status` + `POST /v1/devices/{name}/{query,write}` + `GET /openapi/v1.json` + `GET /healthz`。v1 は既定で loopback バインド。認証 / server lifecycle 系エンドポイント / scenario import / gRPC は v2。

**Batch J で WebSocket サブプロトコルを追加**（[ADR 0035](adr/0035-visa-over-websocket.md)）: `ws://host:port/v1/devices/{name}/visa` は `{op,scpi}` フレームを受け取り `{event:response|ack|error,...}` を返す。ブラウザ / AI agent ランタイム / ダッシュボード向け。

**Batch K で PAT 認証を追加**（[ADR 0036](adr/0036-management-api-authentication.md)）: `ivicli api token create` でトークンを生成し、HTTP は `Authorization: Bearer <token>`、WebSocket は `Sec-WebSocket-Protocol: ivi-cli-pat.<token>` で検証する。loopback 以外へバインドする場合は ≥ 1 個のトークン（または `--allow-anonymous` で opt-out）が必須。スコープ / mTLS / 有効期限 / 監査ログは v2。

---

## 7.6 Remote Access from IVI-CLI

IVI-CLI 自身は、標準 VISA resource と管理APIの両方を扱える。

```bash
ivicli --server lab visa query psu1 "*IDN?"
```

内部的には、設定に応じて以下のいずれかを利用する。

```text
Data Plane:
- local VISA
- HiSLIP VISA resource
- VXI-11 VISA resource
- raw TCP SOCKET resource

Control Plane:
- IVI-CLI management API
```

---

# 8. Configuration

## 8.1 config.toml

設定ファイルは全OSで XDG-style path を優先する。

```text
~/.config/ivicli/config.toml
```

Windows でも以下を既定値として使用する：

```powershell
$HOME/.config/ivicli/config.toml
```

クロスプラットフォームで設定パスを統一する。

必要に応じて環境変数による override を許可する。

例：

```text
IVICLI_CONFIG
```

---

## 8.2 Example

`config.toml` は静的設定を保持する。

current device / current server 等の動的 session state は state directory に保存する。

`visa use` は session state を更新し、`visa use --default` は config.toml の defaults を更新する。

例：

```text
~/.local/state/ivicli/session.json
```

---

```toml
[defaults]
server = "local"
device = "psu1"

[[devices]]
name = "psu1"
resource = "TCPIP0::192.168.0.10::inst0::INSTR"
timeout_ms = 3000

[[devices]]
name = "scope1"
resource = "USB0::0x0699::...::INSTR"
timeout_ms = 5000

[[servers]]
name = "local"
type = "local"

[[servers]]
name = "lab"
type = "hislip"
host = "192.168.0.50"
port = 4880
```

`devices` / `servers` / `routes` は table key に動的名称を埋め込まず、array of tables として定義する。

```toml
[[devices]]
name = "psu1"
resource = "TCPIP0::192.168.0.10::inst0::INSTR"
```

array of tables を採用し、schema 化・validation・rename を容易にする。

---

# 9. 出力モード

## Human-readable

```bash
ivicli visa status
```

## JSON

```bash
ivicli visa status --json
```

CI / AI Agent 向け。

---

# 10. システムアーキテクチャ

```text
                    Control Plane
┌─────────────────────────────────────────────────┐
│ config / logical / doctor / server route     │
└─────────────────────────────────────────────────┘
                        ↓
                Management API
                        ↓

                     Data Plane
┌─────────────────────────────────────────────────┐
│ visa query/write/read/status                   │
└─────────────────────────────────────────────────┘
                        ↓
                IIviBackend
                 ├─ LocalVisaBackend
                 ├─ HiSlipBackend
                 ├─ Vxi11Backend
                 ├─ SocketBackend
                 ├─ FakeBackend
                 └─ ReplayBackend
                        ↓
                    IVI/VISA
```

---

# 11. Naming Conventions

## 11.1 Product Naming

| Purpose      | Name    |
| ------------ | ------- |
| Display Name | IVI-CLI |
| Repository   | ivi-cli |
| Command      | ivicli  |
| Namespace    | IviCli  |

---

## 11.2 Command Naming

command 名は以下を原則とする。

* lower-case
* kebab-case を避ける
* verb-first
* VISA ecosystem terminology を優先

例：

```text
visa scan
visa query
server route add
logical list
```

---

## 11.3 Alias Naming

device alias / logical name は短く可読性を優先する。

推奨例：

```text
psu1
scope1
dmm1
awg1
```

非推奨例：

```text
rackA_main_scope_device
USB_SCOPE_01
```

---

## 11.4 Backend Naming

backend implementation は transport / behavior を明示する。

```text
LocalVisaBackend
HiSlipBackend
Vxi11Backend
SocketBackend
FakeBackend
ReplayBackend
```

---

# 12. 推奨技術スタック

| Layer           | Technology                                                                        |
| --------------- | --------------------------------------------------------------------------------- |
| Language        | C#                                                                                |
| CLI             | System.CommandLine                                                                |
| Config          | Tomlyn                                                                            |
| Hosting         | Microsoft.Extensions.Hosting                                                      |
| Logging         | Microsoft.Extensions.Logging                                                      |
| Remote Protocol | HiSLIP / VXI-11 / TCP Socket                                                      |
| Management API  | gRPC / HTTP JSON                                                                  |
| VISA            | IVI Foundation / NI-VISA                                                          |
| Testing         | xUnit / NSubstitute / Shouldly                                                    |
| Test Helpers    | Microsoft.Extensions.Logging.Abstractions / System.IO.Abstractions.TestingHelpers |

---

# 13. Testing Strategy

## 13.1 Test Stack

単体テストは以下を基本とする。

| Purpose                 | Package                                   |
| ----------------------- | ----------------------------------------- |
| Test Framework          | xUnit                                     |
| Mock / Substitute       | NSubstitute                               |
| Assertion               | Shouldly                                  |
| Logging Abstraction     | Microsoft.Extensions.Logging.Abstractions |
| File System Test Double | System.IO.Abstractions.TestingHelpers     |

---

## 13.2 Test Targets

Phase 1 では以下を重点的にテストする。

* command parsing
* config load / save / validation
* session state load / save
* alias / resource resolution
* backend abstraction
* connection lifecycle
* timeout / disconnect / reconnect behavior
* JSON output contract
* error handling

VISA 実機依存部分は `FakeBackend` を優先し、実機テストは integration test として分離する。

---

## 13.3 Connection Lifecycle Testing

接続・切断・タイムアウトは `FakeBackend` / `FakeSession` で障害注入できるようにする。

テスト対象例：

* open success / open failure
* query timeout
* read timeout
* disconnected during query
* reconnect after failure
* dispose / close called exactly once
* `visa status` の online / offline 判定

実機を使うテストは `integration` category とし、通常の unit test から分離する。

---

# 14. MVP Scope

## MVPに含める

Phase 1:

* visa scan
* visa list
* visa add/remove
* visa use/current
* visa query/write/read
* visa status
* doctor
* driver list — ローカルの IVI Configuration Store からインストール
  済み IVI ドライバを列挙 (ADR 0045)。機器は通信できているのにドライ
  バ DLL が無い / バージョン不一致、といったデバッグで必須。
* logical list — 同じストアから IVI 論理名を列挙。alias を
  `visa add` する前にどの論理名がどの driver session に紐付いている
  か確認するのに使う。
* config.toml
* --json

Phase 2:

* server start/stop/status
* server route add/remove/list
* HiSLIP-compatible server
* remote instrument gateway

Phase 3 (オペレータ向け自動化):

* visa script — SCPI コマンド列をファイルから読み込み、write / query /
  sleep / assert を行ごとにアクティブデバイスへ実行する。
* visa monitor — クエリを一定間隔で繰り返しタイムスタンプ付きで標準出力
  および構造化ログに流す。中断まで継続。
* mock scenario record — 実行中の query/write トラフィックをシナリオファイル
  に追記し、ADR 0026 の再生ループを閉じる。
* mock scenario import — NDJSON キャプチャ（IVICLI_CAPTURE の出力）を
  既存の MockScenario に変換し、`IVICLI_REPLAY` 経由でそのまま再生可能に
  する。ADR 0033。
* server log — ゲートウェイのサーバ別構造化ログを tail（follow / レベル
  フィルタ）。オペレータ向け観測性。

---

## MVPに含めない

* GUI
* Web UI
* Authentication
* TLS
* Streaming waveform
* Full VXI-11 compatibility
* VISA packet replay
* AI integration

---

# 15. 将来拡張

## Planned

* Web UI
* AI agent integration

---

# 16. UX Philosophy

`IVI-CLI` は SCPI shell ではなく、計測器インフラ運用CLIを目指す。

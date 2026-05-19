# Domain Glossary

IVI-CLI ドメインの **Entity / Value Object / Domain Service** カタログ。
ubiquitous language の単一情報源として、コード・テスト・ログ・PRD はこの語彙に揃える。

判定基準は ADR 0003 を参照。

> このファイルは育つ前提の living document。追加・変更は通常 PR で行う（ADR は不要、ただし分類変更は議論の上）。

---

## Entities

identity が attribute と独立に持続するもの。

### Device

- **Identity**: `DeviceName` (alias, 例: `psu1`)
- **属性**: `Resource: VisaResource`, `Timeout: Timeout`
- **Lifecycle**: `visa add` で生成 / `visa remove` で削除 / `visa add` 同名再登録で更新
- **不変条件**: `DeviceName` は config 内で一意

### Server

- **Identity**: `ServerName` (例: `local`, `lab`)
- **属性**: `Type: ServerType`, `Host: Host?`, `Port: Port?`, `Bind: IpAddress?`
- **Lifecycle**: config に登録 / Phase 2 で起動・停止
- **注意**: config 上の `Server` 定義と、起動中の "Running Server Instance" は別エンティティ（後者は Phase 2 で導入）

### Route (Phase 2)

- **Identity**: `(ServerName, PublicEndpoint)` の複合 ID（例: `(lab, hislip0)`）
- **属性**: `Device: DeviceName`
- **Lifecycle**: `server route add` / `server route remove`
- **不変条件**: 紐づく `Device` が config に存在する

### Session (singleton)

- **Identity**: 「the current session」として 1 個固定
- **属性**: `CurrentDevice: DeviceName?`, `CurrentServer: ServerName?`, その他揮発キャッシュ
- **Lifecycle**: プロセス起動時にロード、`visa use` 等で書き換え、明示終了なし
- **永続化**: `state.json`
- **注意**: 将来 multi-session 化するなら identity を導入する

---

## Value Objects

値そのもので equality が決まり、置換のみ。

### Identity 系 wrapper（strongly-typed string）

| VO | 例 | 制約 |
| --- | --- | --- |
| `DeviceName` | `psu1` | 非空、推奨は `[a-z][a-z0-9_]*` 程度（命名規則は 0021 参照） |
| `ServerName` | `local`, `lab` | 同上 |
| `HislipName` | `hislip0`, `hislip1` | HiSLIP 規約 |

### VISA / SCPI

| VO | 説明 |
| --- | --- |
| `VisaResource` | `TCPIP0::192.168.0.10::inst0::INSTR` 等。Parse 後は `VisaResource.Tcpip` / `Usb` / `Gpib` 等の sum type 想定 |
| `IdnResponse` | `*IDN?` の応答全体 |
| `IdnVendor` | IDN の vendor 部 |
| `IdnModel` | IDN の model 部 |
| `IdnSerial` | serial number 部 |
| `IdnFirmware` | firmware 部 |
| `ScpiCommand` | write 用 SCPI 文字列の wrapper |
| `ScpiQuery` | query 用 SCPI 文字列の wrapper（末尾 `?` 想定） |

### 時間・ネットワーク

| VO | 説明 |
| --- | --- |
| `Timeout` | `TimeSpan` の意味付け wrapper（負値禁止等） |
| `Host` | IP / hostname |
| `Port` | 1〜65535 |
| `IpAddress` | bind 用 |

### 設定構造

| VO | 説明 |
| --- | --- |
| `ConfigDocument` | `config.toml` 全体の read model（Devices, Servers, Routes, Defaults を保持） |
| `Defaults` | `[defaults]` セクション (`Server: ServerName?`, `Device: DeviceName?`) |
| `ServerType` | `Local` / `HiSlip` / `Vxi11` / `Socket` の enum / sum type |

---

## Domain Services

単一 Entity に閉じない操作・不変条件を担う。

### ConfigValidator

- 入力: `ConfigDocument`
- 出力: `Result<Validated<ConfigDocument>, ConfigError[]>`
- 役割:
  - `Defaults.Device` が `Devices` に存在することの確認
  - `Defaults.Server` が `Servers` に存在することの確認
  - 名前重複の検出
  - その他 cross-entity 不変条件

### AliasResolver

- 入力: `string`（CLI 引数の生 token）/ `ConfigDocument`
- 出力: `Result<DeviceName, ResolveError>`
- 役割: scan index (`"1"`) や alias (`"psu1"`) の解決ロジックを 1 箇所に集約

---

## 命名規則

- Entity / VO / Domain Service の C# クラス名は本ファイルの見出しと一致させる
- 名前空間は所属アセンブリ + サブカテゴリ（例: `IviCli.Domain.Devices`, `IviCli.Domain.Scpi`）
- テスト名は `<Entity>_<Behavior>_<Expectation>` パターン（0009 で詳細）

---

## 追加・変更履歴

このファイルは PR で更新される。重要な分類変更（VO → Entity 等）は PR description で根拠を残す。

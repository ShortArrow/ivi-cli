# 0003. Architecture Style

- Status: Accepted
- Date: 2026-05-19

## Context

0021 でアセンブリ分割と依存方向は確定済み。本 ADR ではその上に乗る **コード水準のアーキテクチャ規律**（DDD の採用範囲、CQRS の適用粒度、FP 寄りの実践、DI の置き場所、cross-cutting concerns の扱い）を決める。

PRD と 0021 から導かれる制約:

- `IIviBackend` を transport 抽象とした多態（HiSLIP/VXI-11/Socket/Fake/Replay）
- Data Plane (visa query/write/read) と Control Plane (config/server route/diagnose) の分離
- 静的 config (`config.toml`) と動的 session state (`session.json`) の分離
- SCPI 自体が write（`OUTP ON`）と query（`*IDN?`）を構文で区別する

これらは「CQRS が自然に効く構造」であり、また「side-effect を edge に押し出す FP 設計」と整合する。

## Decision

### 1. ベースは Clean Architecture + Hexagonal

- 依存方向は単方向（0021 の図に従う）。
- Application 層に **Port**（インターフェース）を定義、各 Adapter（Backend / Infrastructure 実装）が実装する。
- Domain は外部依存ゼロ。Application は Domain のみ参照。

### 2. DDD は lightweight 採用

採用するもの:

- **Entity / Value Object の語彙と区別**
- **Ubiquitous Language** — PRD / Domain Glossary 由来の名前をコード・テスト・ログで揃える
- **Anti-Corruption Layer** — VISA resource string 等の生表現は Application 層の入口で domain 型へ変換し、それ以降は domain 型でのみ流す
- **Domain Service** — 単一 Entity に収まらない不変条件（例: `defaults.device` が `[[devices]]` に存在することの保証）は Domain Service として配置

採用しないもの（Phase 1）:

- **Aggregate Root** — 規模に対して overengineering。TOML/JSON ファイル全体が事実上の transaction boundary になっており、AR を明示する利得が薄い
- **Repository pattern の形式化** — `IConfigStore`, `ISessionStore` のような必要最小限の port は置くが、`IDeviceRepository` 等は導入しない
- **Domain Event** — 現状の use case は同期的で event 駆動の必要性がない

将来、複雑性が増したら個別に追加 ADR で導入する。

### 3. Entity / Value Object 分類

カタログは `docs/domain-glossary.md` に分離する（育つ前提のため、ADR では肥大化させない）。
本 ADR では分類の判定基準のみ記す:

- **Entity**: identity が attribute と独立に持続する（例: `Device` は `resource` や `timeout_ms` が変わっても `psu1` のまま）
- **Value Object**: 値そのもので equality が決まる、置換のみ（例: `VisaResource`, `DeviceName`, `Timeout`）
- 迷ったら **VO 優先**。Entity 化は lifecycle が観測されてから。

### 4. CQRS — handler 分離 + read model 分離

採用範囲:

- **Application 層で Command と Query の handler を分離**（共通 base にまとめない）。
  - 例: `AddDeviceCommandHandler` / `SetCurrentDeviceCommandHandler` / `ListDevicesQueryHandler` / `GetCurrentDeviceQueryHandler`
- **Read model を用途別に分離**:
  - `ConfigDocument`（read-mostly, validation 重視, 人間も編集）
  - `SessionState`（write-heavy, 揮発 OK, validation 軽量）
  - 同一の "Repository" にまとめない
- **`IIviBackend` で write と query を別 method にする**:
  - `WriteAsync(string scpi)` / `QueryAsync<T>(string scpi)` / `ReadAsync<T>()` を separated。共通 `ExecuteAsync` を作って合流させない（SCPI の `?` suffix 区別を型レベルに持ち上げる）

採用しないもの:

- **Event Sourcing** — CLI に過剰
- **CommandBus / MediatR 経由の dispatch** — 規模に対して boilerplate 過多。直接呼び出しで十分（Phase 1）
- **Eventual consistency** — 同期で完結

### 5. FP 寄りの C# 実践

- **immutable 優先**: domain 型は `record` を既定、`with` 式で変更を表現
- **Result 型で失敗を型に乗せる**: 業務的失敗は `Result<T, TError>`、本当の例外（disk full, OOM 等）のみ throw（詳細は 0014）
- **Dependency Rejection / Impureim Sandwich**: I/O は edge（Cli ハンドラ・Backend Adapter）に閉じ込め、core は pure 関数
- **DI コンテナは composition root のみ**:
  - `IviCli.Cli/Program.cs` だけが `IServiceCollection` を触る
  - Application / Domain / Backend 内では interface or `Func<>` をコンストラクタ引数で受け取るだけ（コンテナへの参照を持たない）
- **interface 過剰生成を避ける**:
  - 多態が必要なものだけ interface（`IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`）
  - 単一実装で testability も不要なものは具象クラスをそのまま注入

### 6. Composition Root

`IviCli.Cli/Program.cs` のみ。他レイヤーから `IServiceCollection` / `IServiceProvider` を参照しない（Service Locator 禁止）。

### 7. Cross-cutting concerns（宣言のみ、詳細は別 ADR）

| Concern | 方針 | 詳細 ADR |
| --- | --- | --- |
| Logging | `Microsoft.Extensions.Logging` の `ILogger<T>` をコンストラクタ注入 | 0011 |
| Validation | Application 層の入口で実施。Domain 内では型レベルで強制 | 別途 |
| Error handling | Result 型優先、例外は exceptional のみ | 0014 |
| Threading | async/await 優先、Backend は CancellationToken 必須 | 0015 |

## Consequences

**Pros**

- 規模に対して適切（過剰な DDD/CQRS/DI ceremonies を避けつつ、必要な責務分離は確保）
- SCPI と config/state の自然な構造を型に反映できる
- CLI 全体が「I/O は edge、core は pure」で保守容易
- 将来 Aggregate / Domain Event / Event Sourcing 等を追加するときの拡張余地が残る

**Cons**

- `record` ベースの immutable 設計は C# 開発者によっては不慣れ
- Result 型は標準ライブラリにないため小さな自前実装が必要（または 3rd-party 採用、別途決定）
- handler 単位の分離で小ファイル多数になる

**Mitigations**

- `Result<T, TError>` は最小限の自前実装で start、必要なら後から `OneOf` / `LanguageExt` 等へ移行検討
- ファイル数は src/tests の対称配置（0021）で navigability を確保
- 命名・配置パターンを `docs/domain-glossary.md` と本 ADR でカバー

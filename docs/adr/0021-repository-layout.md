# 0021. Repository Layout

- Status: Accepted
- Date: 2026-05-19

## Context

Phase 1 着手前にアセンブリ分割とディレクトリ配置を確定させる。
PRD は `IIviBackend` を transport 抽象として位置づけており、HiSLIP / VXI-11 / Socket は NuGet 依存と実装複雑度が大きく異なる。
Clean Architecture (CLAUDE.md 第4原則) と Backend 多態の両方を素直に表現する分割が必要。

## Decision

### Top-level directory

.NET 標準の `src/` + `tests/` を採用する。

```
ivi-cli/
 ├─ src/
 ├─ tests/
 ├─ docs/
 ├─ build/         # Directory.Build.props 等の共通 MSBuild 設定 (将来)
 └─ ivi-cli.slnx
```

### Assembly split

CA レイヤーと Backend 多態の両方を表現する。

**Phase 1 で実体を作るもの:**

| Assembly | Role |
| --- | --- |
| `IviCli.Domain` | エンティティ・値オブジェクト (Device, Alias, VisaResource, …)。外部依存なし |
| `IviCli.Application` | use case / port インターフェース (`IIviBackend` 等)。Domain のみ参照 |
| `IviCli.Infrastructure` | config.toml / session.json 永続化、time provider 等の technical detail |
| `IviCli.Backends.Local` | NI-VISA / IVI 経由のローカル backend |
| `IviCli.Backends.Fake` | テスト・CI で使う in-memory backend |
| `IviCli.Cli` | System.CommandLine ベースのエントリポイント (composition root) |

**Phase 2 で実体化するもの (本 ADR では slnx の slot 確保のみ／生成は先送り):**

| Assembly | Role |
| --- | --- |
| `IviCli.Backends.HiSlip` | HiSLIP backend |
| `IviCli.Backends.Vxi11` | VXI-11 backend |
| `IviCli.Backends.Socket` | raw TCP socket backend |
| `IviCli.Backends.Replay` | session recording / replay backend |
| `IviCli.Server` | remote instrument gateway |
| `IviCli.Management` | gRPC / HTTP management API |

### Dependency direction

```
Domain ← Application ← Infrastructure
                    ↑
                    Backends.*    (Application の port を実装)
                    ↑
                    Cli (composition root)

将来: Server / Management → Application → Domain
```

- 依存は常に上位（抽象）方向へ。逆参照・循環は禁止 (CLAUDE.md 第4原則)。
- `IIviBackend` 等の port は `IviCli.Application` に定義し、各 `IviCli.Backends.*` がそれを実装する。
- `IviCli.Cli` のみが全レイヤーを参照できる（DI 組み立て用 composition root）。

### Test project mapping

src と同名 `.Tests` を `tests/` 配下にミラー配置する。
実機依存の integration test は `[Trait("Category","Integration")]` で隔離する（詳細は 0009-testing-strategy）。

```
tests/
 ├─ IviCli.Domain.Tests/
 ├─ IviCli.Application.Tests/
 ├─ IviCli.Infrastructure.Tests/
 ├─ IviCli.Backends.Local.Tests/   # 主に integration
 ├─ IviCli.Backends.Fake.Tests/    # Fake 自体の挙動テスト
 └─ IviCli.Cli.Tests/
```

### Naming

- `IviCli.Backends.HiSlip` のように `Backends` を中段に置く。`IviCli.Infrastructure.Backends.HiSlip` とはしない（冗長、Backends を独立に差し替える意図が薄れる）。
- アセンブリ名 = ルート名前空間 = csproj ファイル名 を一致させる。
- ディレクトリ名も同一にする (`src/IviCli.Backends.HiSlip/IviCli.Backends.HiSlip.csproj`)。

## Consequences

**Pros**

- HiSLIP / VXI-11 等の重い NuGet 依存が `IviCli.Cli` 本体に伝播しない（Backend は DI で plugin 的に差し替え）。
- CA 依存方向違反が `dotnet list reference` で機械的に検出できる。
- テストプロジェクト割当が機械的に決まる（src と 1:1）。

**Cons**

- 初期の `dotnet new` 回数が多い（Phase 1: src 6 + tests 6 = 12）。
- 小規模変更でも複数プロジェクトを跨ぐ可能性。

**Mitigations**

- `build/Directory.Build.props` で `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` 等を一元化（別 ADR / 雛形時に決定）。
- アセンブリ命名・依存方向は 0023 (FP), 0009 (TDD), 0017 (Security) と整合させる。

# 0009. Testing Strategy

- Status: Accepted
- Date: 2026-05-20

## Context

本プロジェクトの開発は TDD サイクル（Red-Green-Refactor）を必須とし、テスト記述は Given/When/Then で状態差分に集中する形を採る。
PRD §13 で test stack（xUnit / NSubstitute / Shouldly / Logging.Abstractions / System.IO.Abstractions.TestingHelpers）と Phase 1 のテスト対象は宣言済み。
本 ADR は **どこまで広げるか・どこに線を引くか** を確定させる。

依存する確定事項:

- 0021 で `tests/` 配下の test project は src と 1:1 ミラー、integration は trait 分離と決定済み
- 0003 で Application 層に port を置き Backend を adapter とする方針が確定（mock 対象が明確になる）
- 0003 で FakeBackend を `IviCli.Backends.Fake` として独立アセンブリ化することが確定

## Decision

### 1. テスト分類とゲート

| 分類 | 範囲 | xUnit Trait | CI gating |
| --- | --- | --- | --- |
| **Unit** | Domain / Application / FakeBackend / Cli の純粋ロジック | (なし、既定) | PR で必須 |
| **Integration** | 実 VISA / 実ファイル / 実 socket / 実プロセス | `Category=Integration` | nightly + 手動 trigger |
| **Architecture** | 0021 の依存方向違反検出（NetArchTest） | `Category=Architecture` | PR で必須 |

- PR 既定実行: `dotnet test --filter "Category!=Integration"`
- Integration は nightly ワークフロー（0020 で詳細）
- Architecture テストは Unit と同じく既定実行（Trait は分類のためのみ）

### 2. TDD サイクル

- 既存テストが無い領域に対しても、Red を先に書く。
- 環境制約（実機要件等）で Red が困難な場合は **特性テスト（characterization test）** を最小単位で先行追加する。
- 「ログを見て実装の正しさを確認」する代わりに、振る舞いをテストで固定する。

### 3. テスト命名

`<MethodOrBehavior>_<Scenario>_<Expectation>` を基本形とする。domain-glossary の語彙と一致させる。

```csharp
[Fact] public void AddDevice_WithDuplicateName_ReturnsConflictError();
[Fact] public void ConfigValidator_WhenDefaultDeviceMissing_ReturnsValidationError();
[Fact] public async Task QueryAsync_OnDisconnectMidQuery_ReturnsTransportError();
```

クラス名は `<TypeUnderTest>Tests`（例: `AddDeviceCommandHandlerTests`）。

### 4. AAA / Given-When-Then

テスト本体は Given/When/Then 三区画で書き、「状態の期待差分」に集中する。

```csharp
[Fact]
public void AddDevice_WithDuplicateName_ReturnsConflictError()
{
    // Given
    var config = ConfigBuilder.Empty.WithDevice("psu1", "TCPIP0::host::inst0::INSTR");
    var handler = new AddDeviceCommandHandler(config.AsStore());

    // When
    var result = handler.Handle(new AddDeviceCommand("psu1", "USB0::0x0699::..."));

    // Then
    result.ShouldBeError(ConflictError.DuplicateDeviceName);
}
```

### 5. モック方針

- **Mock 対象は Port のみ**: `IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`, `IFileSystem`
- **Mock してはいけない**: Domain Entity / Value Object / Domain Service（実物を使う）
- **Backend 関連のテストは `IviCli.Backends.Fake` を優先**。NSubstitute での `IIviBackend` モックは Application 層 / Cli 層テストでのみ使う。
- 理由: Fake は domain 不変条件込みで「本物に近い偽物」、mock は単発契約のみ。Fake で書ける挙動テストを mock で再実装しない。

### 6. FakeBackend の fault injection

`IviCli.Backends.Fake` は単なる echo ではなく、テスト用の builder API を提供する。
正式 API は実装時に詰めるが、想定される使用感:

```csharp
var fake = new FakeBackend();
fake.ConfigureDevice("psu1", idn: "FAKE,PSU,0,1.0");
fake.OnOpen("psu1").FailWith(VisaError.ResourceNotFound);
fake.OnQuery("psu1", "*IDN?").RespondWith("FAKE,PSU,0,1.0").After(10.ms);
fake.OnQuery("psu1", "MEAS:VOLT?").Timeout();
fake.SimulateDisconnect("psu1", after: 100.ms);
```

PRD §13.3 の lifecycle テスト対象（open success/failure, query/read timeout, disconnect mid-query, reconnect, dispose once, online/offline 判定）はすべて Fake で書けるようにする。

### 7. 追加ツール

| ツール | 用途 | 採用 |
| --- | --- | --- |
| **NetArchTest** | 0021 の依存方向違反、layer 違反、interface 未実装等の検出 | 採用 |
| **Verify** (snapshot) | `--json` 出力契約、help text、レンダリング出力 | 採用 |
| **FsCheck** (property-based) | VO 不変条件（VisaResource parse/serialize の roundtrip、Timeout 範囲制約等） | 限定採用（VO 周辺のみ） |
| **coverlet** | カバレッジ計測 | 採用（可視化のみ、数値ゲートなし） |
| **Stryker.NET** (mutation) | テスト質の検証 | **不採用**（Phase 1 では過剰） |

### 8. 共有 Test Helper: `tests/IviCli.TestKit/`

以下を集約する共有ライブラリを作る:

- **Test Data Builder**: `ConfigBuilder`, `SessionStateBuilder`, `DeviceBuilder` 等
- **FakeBackend Schedule DSL**: §6 の builder API
- **Custom Shouldly extensions**: `result.ShouldBeError(...)` 等の Result 型用 assertion
- **Verify 設定**: snapshot 配置規約・正規化ルール
- **Trait constants**: `Categories.Integration`, `Categories.Architecture`

`tests/IviCli.TestKit/` は src ではなく tests/ 配下に置く（0021 のテスト 1:1 ミラー対象外、test infrastructure として扱う）。

### 9. カバレッジ方針

- coverlet で計測、PR コメント等で可視化のみ。
- **数値ゲートを置かない**。本プロジェクトの方針は「行動として TDD を守る」ことであり、後付けで数値を満たすためのテストは目的を歪める。
- カバレッジ低下を機械的に検出するスクリプトは可（情報提示用、blocking しない）。

### 10. async テスト

- Backend 系は `async Task` を返すテストを既定とする。
- timeout は **テスト側でも明示**: `[Fact(Timeout=5000)]` を整備し、デッドロック・無限ループを CI でハングさせない。
- `CancellationToken` は Backend port の全 method に伝播済み（0003 / 0015）、テストでも明示的にキャンセル経路を踏む。

## Consequences

**Pros**

- 単一 mock ライブラリ + 単一 fake の双輪で、テスト書き味が一貫する。
- Architecture テストで CA 違反が早期に検出できる（人手レビュー依存を減らせる）。
- Snapshot で `--json` 出力契約が CI で固定される（PRD §9 の AI/CI 連携の根拠）。
- カバレッジ非ゲートで「数値合わせテスト」の発生を抑制。

**Cons**

- FakeBackend の fault injection DSL を実装する初期投資。
- NetArchTest / Verify / FsCheck / coverlet で NuGet 依存が増える（test 側のみで本体 binary には影響なし）。
- TestKit 経由でテスト間に「共有」が発生し、不用意な拡張で結合度が上がるリスク。

**Mitigations**

- FakeBackend DSL は最小から始め、PRD §13.3 の lifecycle ケースを満たす範囲に絞る。
- TestKit に置く対象は「**2 箇所以上のテストから参照される共通ヘルパ**」に限定する（YAGNI 厳守）。
- 重要な test infrastructure 変更は ADR を切らずとも PR description で根拠を残す。

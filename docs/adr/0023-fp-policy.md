# 0023. Functional Programming Policy

- Status: Accepted
- Date: 2026-05-20

## Context

0003 で「FP 寄りの C#」を採用したが、宣言レベルに留まっていた。本 ADR は **コードに落とすための具体的規約**（immutability の徹底度、Result 型の入手元、Option を入れるか、sum type の表現、async ポリシー、pure/impure の境界）を確定させる。

C# は OO 言語であり、FP を完全に貫くと言語と戦うことになる。**読みやすい範囲で副作用を edge に押し出し、core を pure に保つ** ことを実用目標とする。

## Decision

### 1. Immutability

- **Domain 型は `record`**（positional records 優先）。
- public な property は `init` のみ可。`set` は禁止。
- コレクションを domain 境界で公開する場合は `IReadOnlyList<T>` / `IReadOnlyDictionary<TKey,TValue>` / `ImmutableArray<T>` のいずれかを返す。`List<T>` / `Dictionary<,>` を公開しない。
- 内部実装で mutable collection を使うのは可。ただし呼び出し側に渡る前に readonly 化する。
- mutation は `with` 式で新インスタンスを作って表現する。

例:

```csharp
public sealed record Device(DeviceName Name, VisaResource Resource, Timeout Timeout);

var updated = device with { Timeout = Timeout.FromMilliseconds(5000) };
```

### 2. Nullable Reference Types (NRT)

- 全プロジェクトで `<Nullable>enable</Nullable>`（`build/Directory.Build.props` で一元設定、雛形時に追加）。
- 警告は error 扱い（`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` を併用、ただし範囲は別 ADR で要詰め）。
- **`null!` は禁止**。やむを得ない escape hatch（フレームワーク要件等）は `// nullable-escape: <reason>` コメントを付けて個別に許可。
- 「不在」は `T?` で表現する。次節 §3 と整合。

### 3. Option<T> を導入しない

- C# の NRT (`T?`) で十分。`Option<T>` ライブラリ（`LanguageExt` 等）は採用しない。
- パイプライン中の表現力不足は `T?` + 拡張メソッド（`.Map`, `.Bind` 等の自前実装）で補う。
- 理由: NRT 厳格運用と Option 併用は二重表現になり、レビュー時の混乱要因。NRT 一本に絞る。

### 4. Result<T, TError> — 自前最小実装

`IviCli.Domain` 内に最小実装を置く。ライブラリ依存なし。

想定 shape:

```csharp
public abstract record Result<T, TError>
{
    public sealed record Ok(T Value) : Result<T, TError>;
    public sealed record Error(TError Err) : Result<T, TError>;
}

public static class Result
{
    public static Result<T, TError> Success<T, TError>(T value) => ...;
    public static Result<T, TError> Failure<T, TError>(TError err) => ...;
}

public static class ResultExtensions
{
    public static Result<U, TError> Map<T, U, TError>(this Result<T, TError> r, Func<T, U> f);
    public static Result<U, TError> Bind<T, U, TError>(this Result<T, TError> r, Func<T, Result<U, TError>> f);
    public static Result<T, FError> MapError<T, TError, FError>(this Result<T, TError> r, Func<TError, FError> f);
    public static R Match<T, TError, R>(this Result<T, TError> r, Func<T, R> ok, Func<TError, R> err);
}
```

正式 API は実装時に詰める。ライブラリ移行は `IviCli.Domain` 内に閉じるので将来 `OneOf` / `LanguageExt` への置換は局所的。

### 5. Sum Type は sealed record hierarchy

C# ネイティブの discriminated union が無いため、closed type set を `abstract record` + `sealed record` で表現する。

```csharp
public abstract record VisaResource;
public sealed record Tcpip(Host Host, string Board, string Suffix) : VisaResource;
public sealed record Usb(string VendorId, string ProductId, string Serial) : VisaResource;
public sealed record Gpib(int Board, int PrimaryAddress) : VisaResource;

string Describe(VisaResource r) => r switch
{
    Tcpip t => $"TCPIP {t.Host}",
    Usb u   => $"USB {u.VendorId}:{u.ProductId}",
    Gpib g  => $"GPIB::{g.PrimaryAddress}",
};
```

- `switch` 式で網羅させる。新 case 追加で全 switch がコンパイル警告に出る運用を狙う（`exhaustive` の擬似的実現）。
- discriminator のための `enum Kind` プロパティは追加しない（型自体が discriminator）。

### 6. Pure / Impure の境界 — Impureim Sandwich

```
[Impure] Read inputs   →  Cli ハンドラ / Backend / Infrastructure (I/O)
[Pure]   Compute       →  Application / Domain (副作用なし)
[Impure] Write outputs →  Cli (stdout/stderr/exit code) / Backend / Infrastructure
```

- Domain 層は副作用禁止（I/O・時刻取得・ランダム・例外 throw も避ける）。
- Application 層は port 経由でのみ I/O を表現（直接呼ばない）。
- I/O を含む method は `*Async` suffix を必須にする（pure な計算は同期 method）。
- `IClock`, `IRandom` のような副作用 port を Application で明示的に注入する（テストで差し替え可能にする）。

### 7. Async ポリシー

- `async/await` 全面採用。同期と非同期を混在させない（同期 wrapper の `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` は禁止）。
- public async method は `CancellationToken` を **必須引数** で受ける（default 値も与えない、呼び出し側に明示させる）。
- `ConfigureAwait(false)`: 本プロジェクトは CLI バイナリのみで SynchronizationContext 起因のデッドロックリスクが無いため、**書かない**。ライブラリ化する将来時点で再検討。
- async void 禁止（ハンドラ等で必要な場合は同期ラップでブロックせず、await できる入り口を作る）。

### 8. Interface vs Func / Delegate

- 多態が必要、または methods が複数 → **interface**（`IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`）
- 単一の関数で表現できる port → **`Func<...>` / delegate** で注入可
- 「テストのためだけの interface 量産」は禁止。具象クラスを実装に持ち、テストで Fake を直接渡す方が読みやすい場合は interface を避ける。

### 9. Pattern Matching

- `switch` **statement** ではなく `switch` **expression** を優先。
- `is` パターン、property pattern、relational pattern、list pattern を活用。
- expression-bodied member は副作用なし method 限定。

### 10. LINQ ポリシー

- 表現が明らかに読みやすくなる場合は LINQ chain を使う。
- hot path（コマンドごとに必ず通る経路、Backend 内部ループ等）では `foreach` を許容。allocation を避けたい場合に LINQ を強制しない（pragmatism）。
- 副作用を伴う `.ToList()` 後の mutation 等、純粋性を壊す使い方は避ける。

### 11. Exception の扱い（0014 と整合）

- 業務的失敗（validation, parse, not found, conflict, transport error）は **Result.Error** で表す。
- Exception を throw するのは:
  - プログラミングエラー（前提条件違反、`ArgumentNullException` 等）
  - 真の例外状況（OOM, disk full, OS 例外伝播）
- catch は composition root（`IviCli.Cli/Program.cs`）の最外周でのみ。下層で catch するのは「Result に詰め直す目的」のみ。

詳細は 0014 で別途確定。

## Consequences

**Pros**

- core が pure で testable、I/O を Fake/Mock で差し替えるテストが書きやすい（0009 の前提と整合）。
- sealed record hierarchy + `switch` 式で型レベルの網羅性が効く。
- NRT + Result 二本立てで「失敗の表現」が一意に決まる。
- ライブラリ依存ゼロで FP 表現が完結（NuGet 依存は production binary には増えない）。

**Cons**

- 自前 Result 実装の追加コスト（〜100 行程度）と保守責任。
- C# 開発者によっては record / pattern matching / Result が不慣れ。
- NRT を厳格運用すると、外部ライブラリ境界で警告抑制の手間が出る場合がある。

**Mitigations**

- Result の API は最小から始め、必要に応じて拡張。将来 `OneOf` / `LanguageExt` への置換は `IviCli.Domain` 内で局所的に可能。
- 不慣れ問題は `docs/domain-glossary.md` と本 ADR、それから雛形コードの一貫性で吸収。
- NRT 警告抑制は `// nullable-escape: <reason>` で trackable に。

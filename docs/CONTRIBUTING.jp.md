[English](CONTRIBUTING.md) | **日本語**

# ivi-cli への貢献ガイド

貢献を検討いただきありがとうございます。本ファイルは **ナビゲーションハブ** であり、各トピックはその領域を統括する Architecture Decision Record (ADR) へリンクします。ポリシーの単一情報源を ADR 側に保つため、ここでは方針を二重化しません。

## はじめに

```sh
git clone https://github.com/ShortArrow/ivi-cli.git
cd ivi-cli
dotnet tool restore
dotnet restore --locked-mode
dotnet build
dotnet test --filter "Category!=Integration"
```

最初の `dotnet build` で `dotnet husky install` が走り、ローカル hooks が有効化されます。

## ブランチ運用とコミット

GitHub Flow（単一の `main`、短命の topic branch、squash-merge、Conventional Commits）— [ADR 0022](adr/0022-branching-strategy.md) を参照。

## ローカル hooks

Husky.Net の pre-commit が CSharpier を実行します。build とテストは push 時には走らせず CI（`pr.yml`）の担当 — [ADR 0025](adr/0025-dev-automation-hooks.md) を参照。

## CI ゲーティング

`pr.yml`（PR）、`nightly.yml`（スケジュール、Integration を含む）、`release.yml`（tag 起動）— [ADR 0020](adr/0020-ci-cd-strategy.md) を参照。`docs-sync-check` ジョブが bilingual ペアの整合性を強制します。

## ドキュメント

リポジトリの文書は英語を primary とし、PRD・README・本ファイルは `**English** | [日本語](...)` スイッチャヘッダ付きで `*.jp.md` の日本語ミラーを必須とします — [ADR 0024](adr/0024-documentation-policy.md) を参照。

## アーキテクチャ

Clean Architecture + DDD + handler レベル CQRS（[ADR 0003](adr/0003-architecture-style.md)）を `src/` および `tests/` の層アセンブリ群（[ADR 0021](adr/0021-repository-layout.md)）として実装。DI composition は [ADR 0010](adr/0010-dependency-injection.md) を参照。`tests/IviCli.Cli.Tests/Architecture/` の NetArchTest スイートが PR ごとに依存方向を検証します。

## テスト

xUnit + Shouldly + Testably.Abstractions。テストは `src/` を 1:1 でミラー (`IviCli.<Layer>.Tests`)。挙動変更には TDD（Red → Green → Refactor）を採用してください。

Integration テストは `[Trait("Category", "Integration")]` を付与し、PR ビルドでは既定で skip され、`nightly.yml` で実行されます。

## ADR

Accepted な意思決定はすべて [`docs/adr/`](adr/) に置きます。ADR は生きたドキュメントとして通常の PR で更新し、本質的な反転がある場合にのみ supersede します。索引ファイルは設けません。

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

Husky.Net の pre-commit が CSharpier を実行します。pre-push は [ドキュメント](#ドキュメント) に書いた文書の検査を `origin/main` と比べて実行するので、先に fetch してください。build とテストは push 時には走らせず CI（`pr.yml`）の担当 — [ADR 0025](adr/0025-dev-automation-hooks.md) を参照。

## CI ゲーティング

`pr.yml`（PR）、`nightly.yml`（スケジュール、Integration を含む）、`release.yml`（tag 起動）— [ADR 0020](adr/0020-ci-cd-strategy.md) を参照。`docs-sync-check` ジョブが、bilingual ペアを含め、文書が変更に追従していることを強制します（[ドキュメント](#ドキュメント) を参照）。

## 依存パッケージの追加

PackageReference を 1 つ増やすことは、再配布義務を 1 つ増やすことでもあります。そのアセンブリは release アーカイブ・コンテナイメージ・tool パッケージのいずれでも `ivicli` と並んで配布されます。同じ PR で `THIRD-PARTY-NOTICES.md` にエントリを追加してください。`notices-sync` ジョブが `src/IviCli.Cli/packages.lock.json` と突き合わせ、記載漏れも、パッケージが消えた後に残ったエントリも失敗させます。ライセンスは NuGet のメタデータではなくプロジェクト本体から読むこと — メタデータにライセンスを一切宣言していない依存が複数あります。[ADR 0046](adr/0046-licensing.md) を参照。

## ドキュメント

リポジトリの文書は英語を primary とし、PRD・README・本ファイルは `**English** | [日本語](...)` スイッチャヘッダ付きで `*.jp.md` の日本語ミラーを必須とします — [ADR 0024](adr/0024-documentation-policy.md) を参照。

文書は、それが説明するコードと同じ PR で変更します。`.github/scripts/docs-follow.sh` は、次のときに PR と push を失敗させます。

- PRD・README・本ファイル・conformance 文書が、片方の言語だけ変わったとき
- `feat`・`fix`・`perf`、または破壊的な変更が `src/` を変えたのに、`docs/CHANGELOG.md` の `[Unreleased]` に記載がないとき
- `docs/cli-reference.md` が変わったのに PRD と CHANGELOG が変わっていないとき、またはコマンドが増減したのに README のコマンド一覧が変わっていないとき
- `*FormatterTests.cs` が既存の出力の期待値を変えたのに、PRD と CHANGELOG が変わっていないとき

`docs/cli-reference.md` は生成物です。コマンド・引数・オプション・その `--help` の文を変えたら、`IVICLI_UPDATE_CLI_REFERENCE=1 dotnet test --project tests/IviCli.Cli.Tests -- --filter-class "*CliReferenceTests"` を実行して、その文書をコミットしてください。説明すべき文書がない変更では、コミットメッセージに `Docs-Unaffected: <理由>` という行を足してください。検査はその理由をレビュー担当に向けて表示します。検査が見るのは文書が変わったことだけで、内容が正しいかはレビューの担当です。

## アーキテクチャ

Clean Architecture + DDD + handler レベル CQRS（[ADR 0003](adr/0003-architecture-style.md)）を `src/` および `tests/` の層アセンブリ群（[ADR 0021](adr/0021-repository-layout.md)）として実装。DI composition は [ADR 0010](adr/0010-dependency-injection.md) を参照。`tests/IviCli.Cli.Tests/Architecture/` の NetArchTest スイートが PR ごとに依存方向を検証します。

## テスト

xUnit v3 + Shouldly + Testably.Abstractions。テストは `src/` を 1:1 でミラー (`IviCli.<Layer>.Tests`)。挙動変更には TDD（Red → Green → Refactor）を採用してください。

Integration テストは `[Trait("Category", "Integration")]` を付与し、PR ビルドでは既定で skip され、`nightly.yml` で実行されます。

## 品質保証とサポート範囲

各リリースが検証する内容、pre-1.0 の互換性契約、動作確認済み計測器の方針、サポート水準 — [ADR 0047](adr/0047-quality-assurance-and-support-scope.md) を参照。プラットフォームカバレッジ（出荷 RID ごとのネイティブテスト + スモーク）— [ADR 0016](adr/0016-cross-platform-policy.md) を参照。脆弱性は public issue ではなく [SECURITY.md](../SECURITY.md) を通して報告してください。

## ADR

Accepted な意思決定はすべて [`docs/adr/`](adr/) に置きます。ADR は現に有効な決定を述べます。決定が変わったときは、新しい ADR で supersede せず、通常の PR でその ADR を書き直します。規約は [ADR 0000](adr/0000-adr-policy.md) にあります。

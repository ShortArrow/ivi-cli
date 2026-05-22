[English](README.md) | **日本語**

# ivi-cli

`ivi-cli` は、VISA/IVI 経由で計測器を管理・診断・操作する統合 CLI です。

> ステータス: **alpha** — Phase 1 を開発中。CLI は起動し設定ファイルを永続化しますが、多くのサブコマンドと Backend 実装は実装途上です。v0.1.0 までは破壊的変更が発生します。

## ハイライト

- **状態保持型 UX.** `ivicli visa add psu1 ...` で alias を一度登録すれば、以降は `psu1` だけで操作できます。
- **VISA 互換.** 標準的な `TCPIP::` / `USB::` / `GPIB::` のリソース文字列を独自構文なしで扱います。
- **Backend 非依存.** Local NI-VISA / HiSLIP / raw socket / fake / replay の各 backend が単一の transport 抽象を共有します。
- **自動化指向.** stdout はデータ（`--json` 含む）専用、stderr はログ専用。終了コードは POSIX 慣習に従います。

## インストール

Phase 1 では 2 経路で配布します:

```sh
# .NET tool（.NET 10 SDK / runtime 必須）
dotnet tool install -g ivi-cli

# self-contained single-file バイナリ（.NET インストール不要）
# GitHub Releases から各 OS / arch のアーティファクトを取得してください。
```

リリースは `win-x64` / `win-arm64` / `linux-x64` / `linux-arm64` / `osx-x64` / `osx-arm64` を提供します。

## クイックスタート

```sh
ivicli visa add psu1 TCPIP0::192.168.0.10::inst0::INSTR
ivicli visa list
ivicli visa list --json
```

設定ファイルは OS ごとに XDG 風のパスに置かれます:

| OS | 既定パス |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/ivi-cli/config.toml`（既定 `~/.config/ivi-cli/config.toml`） |
| macOS | `~/.config/ivi-cli/config.toml` |
| Windows | `%LOCALAPPDATA%\ivi-cli\config.toml` |

`IVICLI_CONFIG` 環境変数または将来の `--config <path>` で override 可能です。

## 詳細度 / フォーマットのフラグ

| フラグ | 効果 |
| --- | --- |
| （なし） | Information 以上 |
| `-v`, `--verbose` | Debug 以上 |
| `-vv` | Trace 以上 |
| `-q`, `--quiet` | Warning 未満を console から抑制（file sink には影響なし） |
| `--log-file <path>` | rolling log file の出力先を上書き |
| `--log-format human\|json` | console の format（既定 `human`） |

## ドキュメント

- [PRD](docs/PRD.jp.md) — プロダクト要件定義（[English](docs/PRD.md)）
- [Architecture Decision Records](docs/adr/) — Accepted な全意思決定（英語のみ）
- [Domain glossary](docs/domain-glossary.md) — ubiquitous-language カタログ（英語のみ）

## プロジェクト構成

```
src/
 ├─ IviCli.Domain          — Value Object / Entity / エラー（外部依存ゼロ）
 ├─ IviCli.Application     — use-case ハンドラ・port
 ├─ IviCli.Infrastructure  — TomlConfigStore 等の adapter
 ├─ IviCli.Backends.Local  — NI-VISA backend（実装中）
 ├─ IviCli.Backends.Fake   — テスト / CI 用の in-memory backend
 └─ IviCli.Cli             — composition root（System.CommandLine / Serilog / DI）
tests/
 ├─ IviCli.<Layer>.Tests   — unit / architecture テスト（src と 1:1）
 └─ IviCli.TestKit         — Test Data Builder / FakeConfigStore / 共有 assertion
```

詳細は [ADR 0021](docs/adr/0021-repository-layout.md) を参照。

## ソースからビルド

```sh
dotnet tool restore
dotnet restore --locked-mode
dotnet build
dotnet test --filter "Category!=Integration"
```

ローカル hook（commit 時の CSharpier check、push 時の build + test）は初回 contributor で `dotnet husky install` を実行することで有効化されます。

## ライセンス

ライセンス未定。`LICENSE` ファイルがコミットされるまでは "all rights reserved" として扱ってください。再利用前に明確化が必要な場合は issue を開いてください。

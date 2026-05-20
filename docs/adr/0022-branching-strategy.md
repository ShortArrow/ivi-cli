# 0022. Branching Strategy

- Status: Accepted
- Date: 2026-05-19

## Context

ブランチ運用方針を確定させる。IVI-CLI は CLI ツール OSS であり、並行サポート版や長期メンテブランチを持たない。
継続デリバリ・single product・single trunk を前提とする現代 CLI OSS のデファクト（`gh`, `kubectl`, `helm`, `terraform`, `ripgrep`, `bat`, `uv`/`ruff` などほぼ全て）に準拠する。

## Decision

### Branch model: GitHub Flow

- `main` のみが長命ブランチ。常に release 可能な状態を保つ。
- feature/fix/docs/... は `main` から切り、PR で `main` に戻る短命ブランチ。
- `develop` ブランチは設けない。
- リリースは `main` のコミットに annotated tag (`v0.1.0` 等) を打って行う。
- 長期サポートが必要になった時点（現時点では発生しない見込み）で release branch を別途検討する。

### Branch naming

`<type>/<short-kebab-slug>` 形式。`type` は Conventional Commits の type と一致させる。

```
feat/visa-scan
fix/session-state-race
docs/adr-0022-branching
chore/editorconfig
refactor/backend-interface
test/fake-backend-disconnect
perf/scan-parallelism
build/directory-build-props
ci/github-actions
hotfix/crash-on-empty-config
```

- ADR / Issue 番号は任意。当面は ADR のときだけ `docs/adr-NNNN-...` のように埋め込む。
- contributor フォークからの PR ではこの規約を強制しない（OSS 慣習）。

### Direct push to main: forbidden

すべての変更は PR 経由とする。例外はなし。
唯一の歴史的例外として、本 ADR 以前に積まれた foundational コミット (`0148705`, `cd31c05`, `a840b9d`, `415892f`, `537b051`) は `main` 直 commit のままとし、移行はしない。

### Merge strategy: squash merge

- PR → `main`: **Squash and merge** を既定とする。`main` の履歴は線形・1 PR = 1 commit。
- Squash 後の commit message は PR タイトルと一致させ、Conventional Commits 規約に沿う。
- 例外として、複数の論理的に独立な commit を保持したい場合のみ rebase merge を選択する（要事前合意）。
- Merge commit (`--no-ff`) は使用しない。

### Commit message: Conventional Commits

```
<type>(<optional-scope>): <subject>

[optional body]

[optional footer(s)]
```

- `type` 集合は branch prefix と同一。
- `scope` は対象アセンブリ名やサブシステム名（例: `feat(backends-hislip): ...`, `docs(adr): ...`）。
- breaking change は footer に `BREAKING CHANGE:` を含めるか、`type!:` 表記を使う。
- 既存コミット履歴 (`docs:`, `chore:`, `docs(adr):`) はこの規約と整合している。

### Release flow

1. `main` で release 可能な状態を確認（CI green / 動作確認）。
2. `main` に annotated tag を打つ: `git tag -a v0.1.0 -m "..."`。
3. tag push で GitHub Actions が release / artifact 生成（CI 構築は 0020 で別途）。
4. hotfix が必要になったら `hotfix/*` を `main` から切って通常 PR で戻す。

## Consequences

**Pros**

- contributor 障壁が低い（PR ターゲット 1 つ）。
- CI/CD パイプラインが単純（`main` push / tag push のみ trigger）。
- `main` 履歴が線形で読みやすい。
- 現代 CLI OSS の読者の期待と一致。

**Cons**

- 長期サポート版が必要になった場合、別途方針追補が要る。
- squash merge により、PR 内部の中間コミットは `main` 履歴から失われる（PR 上には残る）。

**Mitigations**

- 長期サポートが現実化したら追補 ADR（例: 0022a）で release branch 戦略を追加する。
- 重要な中間決定は ADR / PR description に残し、squash 喪失の影響を限定する。

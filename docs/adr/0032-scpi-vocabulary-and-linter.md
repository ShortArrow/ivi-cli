# 0032. SCPI Vocabulary & Script Linter

- Status: Accepted
- Date: 2026-05-27

## Context

PRD §15 listed "SCPI autocomplete" as a Planned feature. Phase 3's
`visa script` runs `.scpi` files but flags malformed mnemonics only at
runtime — the instrument either ignores them silently or returns a
vendor-specific error code, both of which are slow feedback loops. We
want a static-analysis tool that catches at least the most common
typos (wrong mnemonic root) and the same vocabulary surfaced as a
Tab-completion source so interactive composition gets it right on the
first try.

## Decision

### 1. Vocabulary scope

Two canonical sources:

- **IEEE 488.2 §10** — mandatory common commands. `*CLS`, `*ESE`,
  `*ESE?`, `*ESR?`, `*IDN?`, `*OPC`, `*OPC?`, `*PSC`, `*PSC?`, `*RST`,
  `*SRE`, `*SRE?`, `*STB?`, `*TST?`, `*WAI`.
- **SCPI Volume 1 §15** — standard root nodes, each carrying both its
  long form and its upper-case short form: `SYSTem/SYST`, `STATus/STAT`,
  `MEASure/MEAS`, `SENSe/SENS`, `SOURce/SOUR`, `OUTPut/OUTP`, `INPut/INP`,
  `CONFigure/CONF`, `READ`, `FETCh/FETC`, `INITiate/INIT`, `TRIGger/TRIG`,
  `CALCulate/CALC`, `DISPlay/DISP`, `FORMat/FORM`, `MEMory/MEM`,
  `ROUTe/ROUT`, `UNIT`, `HCOPy/HCOP`, `CALibrate/CAL`, `PROGram/PROG`,
  `INSTrument/INST`, `ABORt/ABOR`.

**Vendor-specific commands are out of scope.** Keysight, R&S, NI,
Tektronix, et al. each publish their own command sets running into the
thousands; bundling any single vendor DB would either pick winners or
bloat the binary. Vendor command extensions are a v2 candidate behind
a pluggable dictionary loader.

### 2. v1 lint rule set

`DefaultScriptLinter` walks the already-parsed `ScpiScript` directive
list and emits one `LintFinding` per offending `Write` / `Query`
directive:

- **Unknown root** → `LintSeverity.Warning`. Message:
  `"unknown SCPI root: '<ROOT>'"`. Snippet is the directive's command
  text, truncated to 80 chars with an ellipsis.
- `Sleep` / `Assert` / `Echo` directives are control flow, not SCPI,
  and **never** produce findings.

Parse failures from `ScpiScript.Parse` (the script doesn't tokenise at
all) are not lint findings — the verb's IO/parse layer surfaces them
as a top-level error before the linter ever runs.

### 3. CLI surface

#### `ivicli visa lint <path>`

- Reads the file, parses, lints, prints findings, picks exit code:
  - File IO error or parse error → `UsageError`.
  - One or more `Error`-severity findings → `GenericFailure`.
  - Warnings only → `Success` with the count on a summary line.
  - Zero findings → `Success` with `"<path>: no findings"`.
- `--json` emits a stable JSON array (`line` / `severity` / `message`
  / `snippet`) for tooling.

#### Tab completion

`ScpiCommandCompleter` (`IDynamicCompleter`, name `"scpi"`) ships in
DI and is unit-tested. It is **not** bound to `visa write` / `visa
query` in this revision because their first positional
(`name-or-scpi`) already binds to the device-name completer. Replacing
that binding would break a working completion path; binding to the
second positional needs `CommandTreeWalker.ResolveSlot` to learn how
to address positions past the first — that walker change is a v2
follow-up.

### 4. Layer placement

- **Vocabulary** in `IviCli.Domain.Scpi.ScpiVocabulary` — pure data,
  pre-built `HashSet<string>` lookups, no allocations on the hot path.
- **Linter port + default impl** in `IviCli.Application.Scripting`
  alongside `ScpiScript`. No `System.IO`, no `Console.*` — the verb
  layer does presentation.
- **CLI verb + completer** in `IviCli.Cli`.

## Out of scope (v2 candidates)

- **Vendor-specific dictionaries** (Keysight / R&S / NI / Tek) behind
  a pluggable loader.
- **Full colon-path validation** — today `SENS:NOTANODE?` reports as
  a known `SENS` root with the unknown sub-node ignored. v2 needs the
  full SCPI tree to surface that.
- **Parameter-syntax / range checks** — e.g. `OUTP ON|OFF` enumeration
  validation. Requires per-command parsers.
- **`--warnings-as-errors`** flag on `visa lint` — easy add later;
  not v1 default so existing CI scripts stay forward-compatible.
- **`visa write|query <Tab>` SCPI completion** — needs walker changes
  to address the second positional; ScpiCommandCompleter is ready in
  DI for when that lands.
- **Tab completion of sub-nodes** (`MEAS:<Tab>`) — needs a tree, not
  a flat list.
- **Interactive REPL autocomplete** — no REPL exists.

## Consequences

- Operators get fast, deterministic feedback on script typos via
  `visa lint` without launching the instrument.
- The vocabulary is the single source of truth for the linter and the
  (currently unbound) completer; future Tab completion changes touch
  only the wiring, not the dictionary.
- Zero new third-party dependencies. Vocabulary lookup is O(1)
  hash-set.
- Vendor coverage is explicitly absent; teams that need it can add a
  custom dictionary in a future PR.

# 0051. How ivi-cli interprets SCPI text

- Status: Accepted
- Date: 2026-08-25

## Context

SCPI text reaches three places in this repository, and each grew its own
rule for reading it without reference to the others:

| Surface | Language | Implementation |
| --- | --- | --- |
| Mock rule matching | literal equality | `MockScene.FindByMatch` compares after `NormalizeForMatch`, which strips a leading `:` and nothing else |
| Script `assert` | regular expression, partial match | `Regex.IsMatch(lastResponse, pattern)` ([ADR 0027](0027-phase3-operator-automation.md) §2) |
| `visa lint`, completion | mnemonic vocabulary | `ScpiVocabulary`, standard roots only ([ADR 0032](0032-scpi-vocabulary-and-linter.md)) |

The consequences are visible rather than theoretical. The mock treats
`meas:volt?` and `MEAS:VOLT?` as different commands, so a client that
sends lower case works against an instrument and gets nothing from the
mock — which defeats the purpose of the mock. Nothing anywhere handles a
compound query. Responses are strings from the backend to the terminal
and are never interpreted, so a script cannot assert that a reading sits
in a range without writing a regular expression over digits.

A request to assert numerically on a reading — `25.3,72.1` from a
temperature/humidity probe, `1.234,-0.567,3.000` from a positioner — is
what made the divergence worth settling. That request cannot be answered
on its own: whether `;` separates anything, whether `MEAS` and `MEASure`
are the same command, and which surface speaks which language are one
decision, not four.

### What the standard actually requires

Confirmed against the SCPI specification rather than from habit:

- Headers are case-insensitive.
- Each mnemonic has exactly two accepted spellings, the short form and
  the long form. The short form is the first four characters, or the
  first three when the fourth is a vowel (`POWer` → `POW`). Anything
  between the two is invalid: "`:FREQuen` is not an acceptable form of
  the command because `:FREQuen` is not the entire short nor long form."
- A leading `:` is optional and returns the parser to the root.
- `;` separates program message units and **leaves the path where it
  was**: `MEAS:VOLT?;CURR?` asks for `MEAS:VOLT?` and `MEAS:CURR?`.
  Returning to the root needs `;:`.
- Square brackets mark optional keywords, applied implicitly when
  omitted (`:TRACe` ≡ `:TRACe1`).

Two of these cannot be honoured by a generic implementation. Optional
keywords and default numeric suffixes are properties of one instrument's
command tree, and ADR 0032 already decided this project does not carry
vendor command dictionaries. A third is one-directional: a long-form
rule yields its short form mechanically, while a short-form rule does
not yield its long form — `VOLT` may be short for `VOLTage` or a
complete four-letter mnemonic, and the string alone cannot say which.

## Decision

### 1. Canonicalization: case and leading colon, nothing more

`NormalizeForMatch` gains case folding. It keeps stripping the leading
`:`. It does not resolve short and long forms.

Every `match` in every scenario in this repository is written in short
form and upper case — `MEAS:VOLT?`, `SYST:ERR?`, `OUTP?`. Case folding
therefore changes no existing scenario's behaviour while making the mock
accept requests it previously refused and an instrument would have
answered.

Short/long equivalence is refused rather than deferred, and the reason is
worth writing down because the partial version is tempting.
`ScpiVocabulary` holds `("MEASure", "MEAS")` pairs for standard root
nodes, so a partial implementation is available: roots would accept both
spellings and sub-nodes would not, because `VOLT` and `CURR` are not root
nodes and are not in the table. A rule that accepts `MEASure:VOLT?` and
refuses `MEAS:VOLTage?` is harder to explain than one that accepts
neither, and it looks like conformance while being full of holes. A
scenario that wants both spellings writes two rules.

### 2. Requests: `;` is not expanded

A compound request matches a rule only as the whole string it was sent
as. `MEAS:VOLT?;CURR?` does not activate a rule for `MEAS:VOLT?`.

Expanding it correctly means tracking the path across units, which is
mechanical but only worth building when something asks for it. Nothing
does: no scenario in the repository, and no reported use. The limit is
documented in the mock guide instead of being discovered.

### 3. Responses: split on `;` then `,`, and stop at block data

A response is read as message units separated by `;`, each a list of
elements separated by `,`. This needs no path tracking — the asymmetry
with §2 is real and deliberate, because a response carries no path.

Arbitrary block data (`#800001000<binary>`) is not decoded. A pattern may
observe that a block arrived; it may not look inside. This is the honest
limit of a transport that does not know what was asked.

### 4. Matching languages, per surface

| Surface | Language |
| --- | --- |
| Mock rule `match` | literal, case-insensitive (§1) |
| `!assert <regex>` | regular expression, unchanged |
| `!values <pattern>` | slots and predicates (§5), new |

`assert` keeps its meaning and gains a `!`, along with every other
directive, for the reason in §6. The new language arrives as its own
directive rather than as a prefix on `assert`, so each directive carries
exactly one grammar and a failure can name it.

### 5. The `!values` pattern language

A pattern is a list of slots, separated by `,` and `;` as the response
is. Each slot is one of:

- a literal, matched after trimming (`ON`, `"No error"`, `1`)
- `*`, matching any single slot
- `{predicate}`, parsing the slot as a number and testing it

A predicate is a comparison (`>`, `>=`, `<`, `<=`) or a range
(`20..30`, inclusive). Equality is deliberately absent: `{==3.271}` on a
measured value is a trap, and `{3.27..3.28}` says what the author meant.

A slot may be named for the failure message, `{temp:20..30}`. The name
has no matching role. It exists because a heterogeneous tuple is the
common case, and "element 2 is not in 40..60" makes the reader open the
script to find out what element 2 was.

Numbers are parsed with invariant culture, accepting NR1, NR2 and NR3
alike — there is no reason for a predicate to care which form the
instrument chose. The IEEE 488.2 special values (`9.9E+37` for infinity,
`9.91E+37` for NaN) are compared as the literal numbers they are; giving
them meaning is a separate decision that needs its own syntax, and
silently mapping them would make a comparison result impossible to
explain.

A failure names the slot, its value and the predicate it failed, and
prints the whole response after them.

### 6. One rule for the script file: `!` is ours, everything else is SCPI

A line beginning with `!` is an ivi-cli directive. Every other line is
sent to the instrument exactly as written. Comments are `!#`.

```
!# bench check
*RST
!sleep 500
SOUR:VOLT #HFF
MEAS:VOLT?
!values {4.9..5.1}
DATA #800001000AB
```

The format this replaces put directives in the same namespace as SCPI
commands: a line was a directive when it began with `sleep `, `assert `
or `echo ` (case-insensitively) and a SCPI command otherwise, with no
escape. Two things follow that are worth naming, because neither was
written down and both are defects rather than trade-offs.

Every directive permanently removes a command from the language. An
instrument with a vendor extension spelled `ECHO ON` cannot be driven
from a script at all, and each directive added narrows the gap further —
which turns "should we add a directive" into a question about the
instrument population rather than about the tool.

`#` cannot mark a comment inside a SCPI line. It is not a free character:
IEEE 488.2 gives it four meanings — `#8<len>` and `#0` introduce block
data, and `#H`, `#Q`, `#B` introduce hexadecimal, octal and binary
values. `SOUR:VOLT #HFF` sets 255. The parser strips from the first `#`
unconditionally, so that line and every block transfer are silently
truncated. The code comment above the strip claims it honours `#` only
after whitespace or at the start of a line; the implementation does not
do that, and even the documented intent would still break
`DATA #800001000AB`.

`!` is chosen because a SCPI program message begins with a letter, `*`
or `:`, so no valid command can start with it. `:` was unavailable for
exactly the opposite reason. Trailing comments are dropped rather than
rescued: any rule that finds a comment inside a SCPI line has to
enumerate the meanings of `#`, and the next edition of 488.2 is free to
add a fifth.

The result is a rule that fits in one sentence and stays true as
directives accumulate. Reserved words go to zero, `#` needs no special
case, and adding a directive costs nothing but a name.

**Migration.** The same shape this repository used for `diagnose` →
`doctor` and for the nested `mock scenario scene` spelling: 0.3.x accepts
both forms and warns on the unprefixed one, 0.4.0 removes it. 0.4.0
already carries the removal of the nested mock spellings, so the script
format break lands with the one users are told to expect. No `.scpi`
file exists in this repository, so nothing here needs migrating; the
warning exists for scripts written elsewhere.

## Consequences

- The mock stops refusing requests an instrument would answer, which is
  the property that makes it usable as a stand-in. This is a behaviour
  change with no effect on any scenario in the repository, because all
  of them are already upper case.
- Scripts can assert on readings without a regular expression over
  digits, and the failure tells the reader which reading was wrong.
- Two limits become explicit rather than emergent: compound requests are
  matched whole, and block data is opaque. Both are documented where a
  user meets them.
- The repository still holds three pattern languages, now with a written
  reason for each and a table saying where each applies. Adding a fourth
  needs an argument.
- A script can send any SCPI command, including one spelled like a
  directive, and can carry block data and `#H` values through unharmed.
  Both were impossible before and neither was known to be.

## Out of scope

- **Optional keywords and default numeric suffixes.** They need a
  per-instrument command tree; ADR 0032 declined to carry vendor
  dictionaries and that decision stands.
- **Short/long form equivalence** (§1), until either a scenario needs it
  enough to write the long form or a command tree arrives.
- **Compound request expansion** (§2).
- **Block data decoding** (§3).
- **Numeric predicates outside `!values`.** `visa monitor` and
  `visa watch` could take thresholds using the same language; neither
  has been asked for, and the language is designed so they could.
- **A directive that sends a literal line beginning with `!`.** No SCPI
  command can, so the need is hypothetical; an escape can be added when
  something real needs it.
- **Repetition predicates** — asserting that all eight elements of
  `MEAS? (@1:8)` sit in a range. The positional form covers the
  heterogeneous case that motivated this; the homogeneous one is written
  out or waits.

## Verification

- Existing scenarios pass unchanged after case folding, including the
  three in the repository and the bench scenario under
  `tests/IviCli.Backends.Local.Tests/Assets`.
- Case folding is pinned by a test asserting that a rule written
  `MEAS:VOLT?` answers `meas:volt?`, `:MEAS:VOLT?` and `MEAS:VOLT?`, and
  refuses `MEASure:VOLTage?`.
- The script parser is pinned by cases the old format could not express:
  a line spelled `echo ON` reaching the instrument, `SOUR:VOLT #HFF`
  arriving with its parameter intact, and `DATA #800001000AB` surviving
  whole. Each fails on the current parser.
- `!values` is a pure parser over a pattern plus a response string, so
  every case above is a unit test: slots, wildcards, ranges, named
  slots, `;` units, a block-data response, a response with fewer slots
  than the pattern, and a slot that is not a number.

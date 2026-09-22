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
`meas:volt?` and `MEAS:VOLT?` as different commands, so a client that sends
lower case works against an instrument and gets nothing from the mock —
which defeats the purpose of the mock. Nothing anywhere handles a compound
query. Whether a request expects a response at all is decided by whether the
line ends in `?` once its terminator is stripped, in `ScpiQuery.From`, the
script parser and all four gateways. Responses are strings from the backend
to the terminal and are never interpreted, so a script cannot assert that a
reading sits in a range without writing a regular expression over digits.

A request to assert numerically on a reading — `25.3,72.1` from a
temperature/humidity probe, `1.234,-0.567,3.000` from a positioner — is
what made the divergence worth settling. That request cannot be answered
on its own: whether `;` separates anything, whether `MEAS` and `MEASure`
are the same command, and which surface speaks which language are one
decision, not four.

### What the standard actually requires

Confirmed against SCPI-99 Volume 1 (Syntax and Style) and IEEE 488.2 §7
rather than from habit:

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
  omitted: under `[:SOURce]:VOLTage`, `VOLT 1` means `SOUR:VOLT 1`.
- A numeric suffix left off a header takes its default, usually 1
  (`:TRACe` ≡ `:TRACe1`).

Two of these cannot be honoured by a generic implementation. Optional
keywords and default numeric suffixes are properties of one instrument's
command tree, and ADR 0032 leaves vendor command dictionaries to a pluggable
loader that does not exist. A third is one-directional: a long-form rule
yields its short form mechanically, while a short-form rule does not yield
its long form — `VOLT` may be short for `VOLTage` or a complete four-letter
mnemonic, and the string alone cannot say which.

## Decision

### 1. Canonicalization: case and leading colon, nothing more

`NormalizeForMatch` gains case folding. It keeps stripping the leading
`:`. It does not resolve short and long forms. The universal `*IDN?`
fallback compares through the same canonicalization, so `*idn?` gets the
identity string rather than its own text back.

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
does: no scenario in the repository, and no reported use. The limit goes
into the mock guide.

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

`#` cannot mark a comment inside a SCPI line. It is not a free character: in
IEEE 488.2, `#<n><length>` and `#0` introduce block data, and `#H`, `#Q`,
`#B` introduce hexadecimal, octal and binary values. `SOUR:VOLT #HFF` sets
255. The parser strips from the first `#` unconditionally, so that line and
every block transfer are silently truncated. The code comment above the
strip claims it honours `#` only after whitespace or at the start of a line;
the implementation does not do that, and even the documented intent would
still break `DATA #800001000AB`.

`!` is chosen because a SCPI program message begins with a letter, `*`
or `:`, so no valid command can start with it. `:` was unavailable for
exactly the opposite reason. Trailing comments are dropped rather than
rescued: any rule that finds a comment inside a SCPI line has to
enumerate the meanings of `#`, and the next edition of 488.2 is free to
add another.

The result is a rule that fits in one sentence and stays true as
directives accumulate. Reserved words go to zero, `#` needs no special
case, and adding a directive costs nothing but a name.

**Migration.** 0.3.x accepts both forms and warns on each unprefixed
directive and each `#` comment; 0.4.0 removes them. The earlier renames —
`diagnose` → `doctor`, the nested `mock scenario scene` spelling — kept the
old form as a silent alias or a hidden command and announced the removal
only in the changelog. A script runs unattended, so a silent alias would
leave its author nothing to notice before 0.4.0 breaks it. 0.4.0 already
carries the removal of the nested mock spellings, so the script format break
lands with the one users are told to expect. No `.scpi` file exists in this
repository, so nothing here needs migrating; the warning exists for scripts
written elsewhere.

### 7. A request is a query when a header ends in `?`

A request expects a response when the header of any of its program
message units ends in `?`. The header is the unit's text up to the first
whitespace; parameters may follow it. `;` separates units and a newline
separates messages, except inside a quoted string or block data. A
definite-length block (`#<n><length>`) is skipped by its declared length;
`#0` runs to the end of the message.

This is the IEEE 488.2 definition of a query. `MEAS:VOLT? (@1)` is how a
channel list is queried, and white space may precede the terminator. A
rule that looks at the last character of the line sends `MEAS:VOLT? (@1)`,
`MEAS:VOLT? CH1` and `*IDN? ` to the backend as writes; the mock accepts
them, writes no response, and the client waits for its timeout with
nothing in the log. Under the header rule a `?` at the end of a
parameter, as in `VOLT MAX?`, does not make a query. `VOLT MAX?` is not
valid SCPI; the query of that setting is `VOLT? MAX`, and a client that
relied on the old reading moves the `?` onto the header.

A gateway strips the terminator and the whitespace before it before
anything reads the request, because neither is part of the message.
Quoted strings and block data are, so stripping stops at the end of the
last one. A definite-length block keeps every byte it declares, including
a final space, CR or LF; an indefinite block loses only the newline that
ends it. A `#` whose length field is not all digits does not start a
block, and a block that declares more than the message holds runs to its
end. Lengths are counted in characters: the gateways hand the backend
decoded text, so a block holding bytes outside ASCII is not carried
faithfully today, and this decision does not change that.

The SOCKET gateway frames by line before any of this runs, and a line
ends at CR, LF or CR LF, so a block holding either byte is split there.
HiSLIP, VXI-11 and USB/IP frame by message and have no such limit.
Lifting it on SOCKET needs a reader that frames by the block's declared
length, which is out of scope here.

`ScpiQuery.From`, the script parser and the four gateways call the same
two domain functions for the header test and the strip. §2 still holds:
units are found only to read their headers, and the request reaches the
backend as it was sent, less its terminator.

## Consequences

- The mock stops refusing requests an instrument would answer, which is
  the property that makes it usable as a stand-in. This is a behaviour
  change with no effect on any scenario in the repository, because all
  of them are already upper case.
- Scripts can assert on readings without a regular expression over
  digits, and the failure tells the reader which reading was wrong.
- Two limits become explicit rather than emergent: compound requests are
  matched whole, and block data is opaque. Both go into the guides where
  a user meets them.
- The repository still holds three pattern languages, now with a written
  reason for each and a table saying where each applies. Adding a fourth
  needs an argument.
- From 0.4.0 a script can send any SCPI command, including one spelled
  like a directive, and can carry block data and `#H` values through
  unharmed. Both are impossible today and neither was known to be.
  Through 0.3.x the unprefixed format is still read, so `#` still starts
  a comment and `echo ` still names a directive.
- A query with parameters or trailing whitespace gets its response from
  every gateway, where today it gets silence, and `visa query` accepts it,
  where today it refuses it.
- `VOLT MAX?`, and every other request whose `?` ends a parameter,
  becomes a write. The changelog lists this under breaking changes.
- [ADR 0026](0026-mock-scenario-system.md)'s exact-string matching gives
  way to §1, and [ADR 0027](0027-phase3-operator-automation.md) §2's
  script format to §6; both now point here.

## Out of scope

- **Optional keywords and default numeric suffixes.** They need a
  per-instrument command tree, which ADR 0032 defers to a pluggable
  dictionary loader; they belong with that loader if it is built.
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
  `docker/etc/ivi-cli/scenarios/default.toml`,
  `docs/samples/psu/psu-bench.toml` and the bench scenario under
  `tests/IviCli.Backends.Local.Tests/Assets`.
- Case folding is pinned by a test asserting that a rule written
  `MEAS:VOLT?` answers `meas:volt?`, `:MEAS:VOLT?` and `MEAS:VOLT?`, and
  refuses `MEASure:VOLTage?`.
- Query detection is pinned by cases on the pure function —
  `MEAS:VOLT? (@1)`, `*IDN? `, `VOLT 1;MEAS:VOLT? (@1)`, a `;` or `?`
  inside a quoted string, a `?` inside definite-length block data, and
  `VOLT MAX?` as a write — and by a gateway test per protocol showing
  `MEAS:VOLT? CH1` gets a reply.
- Stripping the message end is pinned on the pure function: trailing
  whitespace and CR/LF go, a definite-length block ending in a space, CR
  or LF keeps it, and an indefinite block loses only its newline. A
  gateway test shows a write whose block ends in a space reaching the
  backend whole.
- The script parser is pinned by cases the old format could not express:
  a line spelled `echo ON` reaching the instrument, `SOUR:VOLT #HFF`
  arriving with its parameter intact, and `DATA #800001000AB` surviving
  whole. Each fails on the current parser.
- `!values` is a pure parser over a pattern plus a response string, so
  every case above is a unit test: slots, wildcards, ranges, named
  slots, `;` units, a block-data response, a response with fewer slots
  than the pattern, and a slot that is not a number.

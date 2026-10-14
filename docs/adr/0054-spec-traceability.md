# 0054. Tests name the clauses they pin, and CI checks them against the conformance document

- Status: Accepted
- Date: 2026-10-15

## Context

`docs/conformance.md` lists, per area, what each standard specifies and
what ivi-cli does, and says every entry names the tests that pin it. It
names them by linking a test file:

- A file holds tests for several entries, so a link does not say which
  tests an entry stands on.
- A test that is renamed, moved or deleted leaves the link pointing at a
  file that may no longer test the claim.
- The file's own rules are kept by hand, and entries already break them:
  two have no test, and one of them is a deviation that links no issue
  (#263).

## Decision

### 1. A `[Spec]` attribute names the clause a test pins

`IviCli.TestKit` provides `SpecAttribute(Standard standard, string clause)`,
an xUnit v3 `ITraitAttribute` that gives the test the trait `Spec` with
the value `<label> <clause>`. A test may carry several.

```csharp
[Fact]
[Spec(Standard.Vxi11, "RULE B.6.19")]
public async Task A_write_past_io_timeout_ends_with_error_15() { ... }
```

`Standard` has one member per source in `docs/conformance.md`, plus the
ADRs for what ivi-cli defines itself:

| Member | Label | Clause |
| --- | --- | --- |
| `Visa` | `VPP-4.3` | `§5.1.2`, `RULE 4.3.18`, `PERMISSION 4.3.2` |
| `HiSlip` | `IVI-6.1` | `§2.6` |
| `Vxi11` | `VXI-11` | `B.4.3`, `RULE B.6.19`, `OBSERVATION B.4.7` |
| `Adr` | `ADR` | `0053 §3` |

A clause is written in full as the standard prints it, so `RULE B.6.27`
and not `B.6.27` after another rule. A clause of the `Adr` kind is an ADR
number and one of its numbered sections. A rule that ivi-cli defines but
no ADR section records gets a section in the ADR that covers its area
before an entry can cite it.

### 2. Each conformance entry cites its clauses

Each table in `docs/conformance.md` and its Japanese mirror has a `Spec`
column in place of `Tests`, listing in the `<label> <clause>` form the
clauses the entry rests on: those whose behaviour ivi-cli follows, or for
the "ivi-cli defines" kind the ADR section that chose it. A clause the
entry only refers to, such as one ivi-cli departs from or one that is
silent on the point, stays in the behaviour text. A reader finds an
entry's tests by filtering the test run on the `Spec` trait.

### 3. A CI step checks both directions

The Linux build job of `pr.yml`, after building, runs each test
executable with `--list-tests json`, which reports every test's traits,
and compares the `Spec` traits with the `Spec` columns. It fails when:

- an entry cites a clause that no test carries;
- a test carries a clause that no entry cites;
- an entry cites no clause;
- a "Deviates" entry links no issue;
- the Japanese mirror's `Spec` column differs from the English one.

The change detection in `pr.yml` counts `docs/conformance.md` and
`docs/conformance.jp.md` as code, so a pull request that changes only the
document still runs the build and this check.

### 4. What the check guarantees

The check guarantees that every clause an entry cites has a test, and
that no test claims a clause the document leaves out. It does not
guarantee that each entry has a test of its own: two entries that cite the
same clause are satisfied by one test of either, and review keeps asking
whether each has its own.

A "Deviates" entry carries a test that pins the behaviour ivi-cli has
today, tagged with the clause it departs from, so the entry stays listed
and the test fails when the deviation is fixed.

## Alternatives

- **A plain `[Trait("Spec", "...")]`.** Needs no code, but the standard
  and the clause are one free string, so a misspelt standard is only
  caught as an uncited clause.
- **Reading the attributes from the source.** Runs without a build, like
  the other sync jobs, but sees only clauses written as literals, and the
  job would keep its own copy of the labels.
- **An ID per conformance entry instead of per clause.** Ties a test to
  exactly one entry, but adds a numbering of ivi-cli's own beside the
  standards' and still says nothing about which clause the test pins.

## Consequences

- A pull request that changes only `docs/conformance.md` builds and runs
  the unit tests.
- Adding a conformance entry needs a tagged test in the same pull
  request, and removing the last test of a clause needs the entry
  removed or another test tagged.
- A test that only Windows or an integration run executes is still listed
  by the Linux build, since listing does not run it.
- Applies first to the "Device timeouts" section, which is all
  `docs/conformance.md` holds today; later sections follow the same rule.

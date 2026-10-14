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
  one has no test, and one deviation links no issue (#263).

## Decision

### 1. A `[Spec]` attribute names the clause a test pins

`IviCli.TestKit` provides `SpecAttribute(Standard standard, string clause)`,
an xUnit v3 `ITraitAttribute` that gives the test the trait `Spec` with
the value `<standard> <clause>`. A test may carry several.

```csharp
[Fact]
[Spec(Standard.Vxi11, "RULE B.6.19")]
public async Task A_write_past_io_timeout_ends_with_error_15() { ... }
```

`Standard` has one member per source in `docs/conformance.md`, plus the
ADRs for what ivi-cli defines itself:

| Member | Label | Clause, as the source prints it |
| --- | --- | --- |
| `Visa` | `VPP-4.3` | `§5.1.2`, `RULE 4.3.18`, `PERMISSION 4.3.2` |
| `HiSlip` | `IVI-6.1` | `§6.12` |
| `Vxi11` | `VXI-11` | `B.4.3`, `RULE B.6.19`, `OBSERVATION B.4.7` |
| `Adr` | `ADR` | `0053 §3` |

The clause is a string literal, so the check below can read it from the
source. A clause of the `Adr` kind is an ADR number and section, and is
used for an entry of the "ivi-cli defines" kind.

### 2. Each conformance entry cites its clauses

Each table in `docs/conformance.md` has a `Spec` column in place of
`Tests`, listing the entry's clauses in the same `<label> <clause>` form.
The behaviour text no longer repeats them. A reader finds an entry's
tests by its clauses, for example by filtering the test run on the
`Spec` trait.

### 3. A CI job checks both directions

A job in `pr.yml` reads the `[Spec]` attributes from `tests/**/*.cs` and
the `Spec` column from `docs/conformance.md`, the way the other sync jobs
read their sources, and fails when:

- an entry cites a clause that no test carries;
- a test carries a clause that no entry cites;
- an entry cites no clause;
- a "Deviates" entry links no issue;
- a `[Spec]` uses a `Standard` member the job does not know.

### 4. Every entry has a test

An entry whose behaviour no test pins is not listed, as the document
already says. The check makes that rule hold rather than adding an
exception to it.

## Alternatives

- **A plain `[Trait("Spec", "...")]`.** Needs no code, but the standard
  and the clause are one free string, so a misspelt standard is only
  caught as an uncited clause.
- **Collecting the traits from the built test assemblies.** Sees exactly
  what the runner sees, but Microsoft.Testing.Platform's `--list-tests`
  prints no traits, and loading every test assembly needs a tool of its
  own. Reading the source matches the other sync jobs and runs without a
  build.
- **An ID per conformance entry instead of per clause.** Ties a test to
  exactly one entry, but adds a numbering of ivi-cli's own beside the
  standards' and still says nothing about which clause the test pins.

## Consequences

- The check works per clause, not per entry. Two entries that cite the
  same clause, such as a client and a gateway both citing VXI-11 RULE
  B.6.19, are satisfied by one test of either; review still has to ask
  whether each entry has its own.
- A clause in a constant or built at run time is invisible to the check,
  which then fails with an uncited or untested clause.
- Adding a conformance entry needs a tagged test in the same pull
  request, and removing the last test of a clause needs the entry
  removed or another test tagged.
- Applies first to the "Device timeouts" section, which is all
  `docs/conformance.md` holds today; later sections follow the same rule.

# 0046. Licensing

- Status: Accepted
- Date: 2026-07-03

## Context

The repository shipped through `v0.2.7` without a license file. The README
declared the source "all rights reserved" as a placeholder, which blocks any
downstream reuse, packaging, or contribution and is incompatible with the
project's OSS positioning (ADR 0024).

ivi-cli is distributed both as a `dotnet tool` NuGet package (`ivi-cli`) and as
self-contained per-RID binaries, and later as a container image. Every one of
those bundles its dependencies as assemblies beside the executable: Serilog,
Spectre.Console, System.CommandLine, OpenTelemetry and Makaretu.Dns under
MIT / Apache-2.0, Tomlyn and IPNetwork2 under BSD, and `Ivi.Visa.dll` under the
IVI Foundation's own license agreement. Any license we adopt must be compatible
with distributing those bundled — and the obligations running the other way,
what each of them asks a redistributor to carry, must be met by what we ship.

A choice was needed between:

- A single permissive license (MIT, or Apache-2.0).
- A dual `MIT OR Apache-2.0` grant, letting the consumer pick either.

## Decision

The project is licensed under **`MIT OR Apache-2.0`** (SPDX expression):
consumers may use it under the terms of **either** license, at their option.

- Two license files live at the repository root: `LICENSE-MIT` and
  `LICENSE-APACHE`.
- The copyright line reads `ivi-cli contributors`, matching the existing
  assembly `Copyright` / `Authors` metadata.
- Package metadata carries the SPDX expression centrally via
  `Directory.Build.props`:
  `<PackageLicenseExpression>MIT OR Apache-2.0</PackageLicenseExpression>`.
  NuGet renders this on the package page; no `PackageLicenseFile` is used
  (the expression and the file form are mutually exclusive, and the SPDX
  expression is the machine-readable source of truth).
- Contributions are, unless the contributor states otherwise, dual licensed
  under the same terms — stated in the README license section and governed by
  Apache-2.0 §5.

Rationale for the dual grant over a single license:

- **Patent protection.** Apache-2.0 §3 grants an explicit patent license and
  includes a patent-retaliation clause; MIT is silent on patents. Offering
  Apache-2.0 gives patent-cautious adopters an explicit grant.
- **Maximum compatibility.** MIT is the most frictionless license for the
  broadest set of downstream projects (including GPLv2 codebases, with which
  the Apache-2.0 patent terms are famously incompatible). Offering MIT as an
  option preserves that reach.
- **Ecosystem precedent.** `MIT OR Apache-2.0` is the de facto convention for
  cross-language infrastructure tooling; it sets clear, familiar expectations
  for contributors and packagers.
- **No copyleft surprise.** Both licenses are permissive, keeping the tool
  freely embeddable in commercial and closed test benches — the primary
  deployment context for a VISA/IVI instrument CLI.

## Third-party attribution

The grant above says what others may do with ivi-cli. The dependencies it
bundles say what ivi-cli must do when it hands them on, and every one of them
asks for the same thing: their copyright and permission notices reach whoever
receives the binaries. A repository file does not satisfy that — the person who
downloads a release archive or pulls the image never sees the repository.

- `THIRD-PARTY-NOTICES.md` at the repository root lists every package in the
  CLI's dependency closure, grouped by license, with the copyright line each
  license asks to be carried, and reproduces the license texts that are not
  already at the root.
- It is copied into the publish layout, so it travels inside the per-RID
  archives, inside the container image (built from that layout), and in the
  tool package — both at the package root and beside the tool binary.
  `LICENSE-MIT` and `LICENSE-APACHE` travel the same way, which is also how
  Apache-2.0 §4(a) is satisfied for the Apache-licensed dependencies.
- The `IviFoundation.Visa` entry is the one whose grant is *conditioned* on
  this: it permits redistribution "provided that the above copyright notice(s)
  appear in all copies". ivi-cli redistributes `Ivi.Visa.dll` unmodified and,
  as a non-member of the Foundation, is licensed for its object code.
- The list is not generated. Four of the packages declare no license in their
  NuGet metadata and two declare only a URL, so a nuspec-scraping generator
  would be silently wrong about six shipped assemblies; those entries record
  the project repository the terms were read from. What is automated is the
  *completeness* check: a pull-request job compares the entries against
  `src/IviCli.Cli/packages.lock.json` and fails on a package with no entry or
  an entry with no package.

Versions are deliberately absent from the notices file. An entry attributes a
work rather than a release, so a dependency bump cannot make the file quietly
untrue, and the check stays quiet for the bumps that change nothing.

## Consequences

**Pros**

- Downstream users get a well-understood permissive grant and can pick the
  license that fits their compliance posture.
- Explicit patent grant available via the Apache-2.0 option.
- Compatible with the bundled dependency set; with the notices file travelling
  inside them, distributing the self-contained binaries, the container image
  and the tool nupkg is unambiguously permitted.
- Aligns the repository with the OSS documentation policy (ADR 0024), which
  requires the README to state a license.

**Cons**

- Two license files and a dual-license contribution clause are marginally more
  to maintain than a single license.
- A new dependency now costs an attribution entry, and for a package with no
  license metadata that means reading the terms from the project itself. The
  check makes that cost visible rather than optional.
- GitHub's license detector shows "View license" rather than a single SPDX
  badge for dual-licensed repositories.

**Mitigations**

- The SPDX expression `MIT OR Apache-2.0` in `Directory.Build.props` is the
  single canonical declaration; the two files are static boilerplate that does
  not change.
- The contribution terms are stated once in the README and rely on Apache-2.0
  §5, so no separate CLA process is introduced.

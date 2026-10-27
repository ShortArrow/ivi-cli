# 0000. ADR Policy

- Status: Accepted

## Context

An ADR is read by someone who needs to know why the project is shaped
the way it is: whether a decision's reasons still hold, so it can be
kept or reversed knowingly. A reader who finds the decision spread over
a chain of records, each superseding the last, has to follow every link
to learn what holds now, and a record frozen at its moment says nothing
about whether its reasons expired since.

Git already keeps every version of every file with its date and author,
so the repository needs no second history inside the documents.

## Decision

### Files and numbers

- An ADR is `docs/adr/NNNN-kebab-slug.md`, with `NNNN` a 4-digit number.
  This policy is 0000; decisions start at 0001.
- A number is never reused, including that of a withdrawn ADR.
- Gaps in the numbering are allowed. A planned decision may exist as a
  skeleton with `Status: Draft`.
- No index file is kept; the directory listing is the index.

### Header and sections

- The header carries `Status` and nothing else. An ADR has no date: git
  records when each version was written.
- `Context`, `Decision` and `Consequences` are required. `Context` states
  the forces in tension and what the decision protects, in the present
  tense.
- `Alternatives` lists each design considered or replaced, one per
  entry, with the reason it is not the design in force.
- `Open questions` lists what a `Draft` still has to settle.
- Further sections may follow these.

### Status

| Status | Meaning |
| --- | --- |
| `Draft` | Under discussion; not binding |
| `Accepted` | Binding; the project follows it |
| `Withdrawn` | No longer binding, and nothing replaces it |

An ADR moves to `Accepted` only when its `Open questions` are empty or
the section is gone.

### An ADR states the decision in force

- When a decision changes, its ADR is rewritten in place, whatever its
  status, so that it states the decision now in force. The design it
  replaces moves to `Alternatives` with the reason it was dropped.
- An ADR is not frozen and superseded by a new one. A new ADR is written
  for a new decision, not for a new version of an old one.
- An ADR describes the present. Former behaviour and how the decision was
  reached belong in the commit that changed it, not in the text.
- An ADR does not cite files outside the repository, such as user-global
  configuration, private notes or chat transcripts. A principle the
  decision rests on is stated in the ADR itself.

## Consequences

- The reader of any ADR sees the decision in force and its reasons in one
  file; the decision as it stood at an earlier date is that file's git
  history.
- When an ADR was accepted is found with `git log` on the file, not in its
  text.
- A rewrite in place loses the old text from the working tree, so its
  reason has to be carried into `Alternatives` by the same change, or it
  is gone for anyone who does not read the history.

## Alternatives

| Design | Why it is not the policy |
| --- | --- |
| Freeze an accepted ADR and supersede it with a new one on a reversal or large rewrite | Spreads one decision over several files, and the superseded ones stay in the directory looking authoritative |
| A `Date` header field holding the original acceptance | Duplicates git, and invites edits that turn it into a revision log |
| `Proposed`, `Deprecated` and `Superseded by NNNN` statuses | `Proposed` is a `Draft` with no open questions; `Deprecated` is `Withdrawn`; supersession is not used |

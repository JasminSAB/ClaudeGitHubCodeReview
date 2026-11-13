# Review guidelines (core)

This file is the core of the rubric: how to approach the code, how to rate it and how to write each
comment. The topics to review come from the skills in `Prompts/skills/`, one Markdown file per
skill. Edit them freely, choose them with `--skills`, or add your own with `--skills-dir`.
Project-specific rules belong in the target repository's `CLAUDE.md` / `README.md`, which the
reviewer reads first.

## 0. Understand the domain first
- Read `CLAUDE.md`, `README.md`, `TODO.md`/ADR files, the entities and the services before judging code.
- Identify the aggregates, their invariants and the documented business rules. Findings must be
  framed in the domain's own vocabulary (e.g. "order", "invoice", "appointment"), not generic advice.
- Documented business rules are decisions, not bugs. If you think one is wrong, say so explicitly and
  label it as "needs a decision by the product owner" instead of silently recommending a change.

## Severity scale
- **critical**: money can be lost / charged wrongly, data corruption, or an auth bypass that is
  exploitable now.
- **high**: security weakness or invariant violation that is realistic under normal use or load.
- **medium**: design problem that will cause bugs or slow the team down; missing defence in depth.
- **low**: clean-up, naming, consistency.

## Style of each comment
- Lead with the problem in one sentence, then a concrete scenario showing why it matters
  ("user A opens an invoice, user B adds a payment -> the payment is booked on A's invoice").
- Then the fix, with a short code example when it helps.
- One finding per root cause. If the same issue appears in several places, comment once at the
  most representative line and list the other locations in the body.
- Be precise and respectful; acknowledge what is already done well in the summary.

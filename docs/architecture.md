# Architecture

The two-step idea, in ten lines:

1. Errors arrive in Seq in far greater numbers than anyone can read.
2. **Step one is cheap and local.** CLog groups errors by fingerprint, so a thousand
   events become a handful of distinct problems, and remembers which fingerprints it has
   already handled — an error is triaged once, not once per occurrence.
3. Rules in `knowledge/rules.json` drop what the team has already judged not worth reading.
   This is deterministic and costs nothing.
4. A local Mistral, via Ollama, then sorts each genuinely new error into NOISE, KNOWN or
   ANALYZE by answering two yes/no questions: is it in `knowledge/known-errors.md`, and if
   not, is it a request rejected for the client's own mistake. Nothing leaves the machine, and personal data is
   removed before the model sees it.
5. **Step two is expensive and reserved.** Only ANALYZE survives step one, so a deeper and
   costlier analysis is spent on the few errors that earned it.
6. This repository is step one. Step two is not built yet.

## Shape of a cycle

```
Seq ──▶ scrub ──▶ fingerprint ──▶ group ──▶ rules ──▶ seen before? ──▶ Mistral ──▶ console
        (PII)      (identity)               (drop)    (SQLite)         (judge)     triage.jsonl
```

Scrubbing comes first, before the fingerprint, the database, the model and the output, so no
step downstream can hold personal data.

## Fingerprint

Three parts, chosen because they stay the same between two occurrences of one bug:

- the exception type,
- the message template, normalised so numbers, ids, GUIDs, timestamps and personal-data
  placeholders collapse to `{}`,
- the topmost stack frame outside `Microsoft.` and `System.` — framework frames are what the
  error travelled through, not where it came from. File and line are stripped, so an edit
  above the failing line does not mint a new error.

## Why these boundaries

- **SQLite, not memory.** A redeploy must not re-report last week's errors.
- **Seen only after the outcome is written.** A judgement can take minutes on a CPU. Marking
  the fingerprint first meant a restart during that time left the error seen but never
  reported. The cost of the safe order is that a crash between the write and the mark reports
  an error twice.
- **An unwritten result is not an outcome.** `triage.jsonl` is appended with three retries, in
  case another program holds the file for a moment. If the line still cannot be written the
  error is left unmarked and is judged and reported again next cycle.
- **The fetch window rolls.** Each fetch starts where the last completed one was made (less a
  minute of overlap for events still on their way into Seq), not a fixed number of minutes
  back, so errors logged during a long cycle are not skipped. The window stays put while a
  cycle is cut short, hits the budget or cannot write a result. `LookbackMinutes` only
  decides how far back the first fetch after a start reaches; the window is not kept across
  restarts.
- **One cycle at a time.** A cycle that outlasts the interval is not joined by a second one;
  the poll that falls due is skipped and logged as `Cycle still running, skipping poll`.
- **Rules before the model.** A team decision should not depend on a model agreeing with it,
  and a filtered error must not spend the per-cycle judgement budget.
- **A budget per cycle.** A burst of new errors costs a bounded amount of time, and the
  remainder is judged on the next cycle rather than dropped.
- **Two yes/no questions, not one three-way choice.** 7B models could not pick between NOISE,
  KNOWN and ANALYZE in one call, but answer yes or no reliably. The old prompt remains behind
  `Triage:Mode = SingleCall` for comparison.
- **The solution comes from the file, not the model.** A yes only says the error is known; the
  fix is copied from the matching entry, so the model cannot invent one.
- **An unusable answer is not a crash.** A local model does not reliably answer in the shape
  it was asked for. An answer that is neither yes nor no becomes ANALYZE with a warning, so
  doubt puts the error in front of a person.

## What is deliberately not here

No GitHub, no Claude, no issue-filing, no alerting. Step one has to be trustworthy and quiet
before anything is wired to act on its output.

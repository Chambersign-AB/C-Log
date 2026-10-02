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
5. **Step two is reserved.** Only ANALYZE survives step one, so an issue is filed, and a
   person's attention asked for, only for the few errors that earned it.
6. Step two files the error as a GitHub issue together with the code it came from. It points
   at the code and leaves the interpretation to a person. It is off until configured.

## Shape of a cycle

```
Seq ──▶ scrub ──▶ fingerprint ──▶ group ──▶ rules ──▶ seen before? ──▶ Mistral ──▶ console
        (PII)      (identity)               (drop)    (SQLite)         (judge)     triage.jsonl
```

Scrubbing comes first, before the fingerprint, the database, the model and the output, so no
step downstream can hold personal data.

## Fingerprint

Built from parts that stay the same between two occurrences of one bug. The exception type is
always one of them. The other depends on whether the event has a stack trace:

- **With a stack trace:** the topmost stack frame outside `Microsoft.` and `System.` —
  framework frames are what the error travelled through, not where it came from. File and
  line are stripped, so an edit above the failing line does not mint a new error. The message
  template is left out: one exception is routinely logged twice, by request logging and by the
  unhandled-exception handler, under two templates, and that filed two issues for one error.
  Only the top frame is used, not the whole trace, because those two logs catch the exception
  at different depths and differ in the frames below it. The cost is that two failures of the
  same type thrown from the same method count as one error.
- **Without a stack trace:** the message template, normalised so numbers, ids, GUIDs,
  timestamps and personal-data placeholders collapse to `{}`.

Hashes stored before the template was dropped are still honoured: an error already seen or
filed under its old hash is taken over by the new one instead of being reported again.

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

## Step two

```
ANALYZE ──▶ stack frames ──▶ code at the commit ──▶ analysis model ──▶ GitHub issue
            (top 3, CSign.*)  (±40 lines, git show)  (cause, place, fix) (once per fingerprint)
```

- **It points, it does not interpret.** In the default Context mode no model is asked: the
  issue holds the error, the stack trace and the code around every application frame, with
  line numbers and the throwing line marked. A 7B model's analysis was wrong often enough
  that reading and discounting it cost more than the code alone. Model mode — the diagram
  above, with its analysis step and only the top three frames — is kept behind
  `Analysis:Mode` for comparison.
- **In Model mode, a targeted call, not an agent.** One prompt holding the error, a bounded
  excerpt of code and the knowledge base. No GitHub Action and no coding agent are involved.
- **The clone is read, never changed.** Files are read with `git show commit:path` rather
  than by checking the commit out, so the clone can be somebody's working copy.
- **Only paths the commit lists are read.** A frame carries a build-machine path; it is
  matched to the repository file sharing the longest tail of it, and a tail shared by several
  files is skipped rather than guessed. The commit value from the event is used only if it
  is a bare hash.
- **The issue is remembered apart from "seen".** It is saved the moment it exists. If the
  result then cannot be recorded, the next cycle finds the issue and records it without
  asking either model again, instead of filing a second one.
- **GitHub down means not seen.** An error that could not be filed has no outcome, so it is
  judged, analysed and filed on a later cycle.
- **A model with nothing to say does not block the issue.** If the analysis model gives no
  answer, the issue is filed with the error alone and says so.
- **Recurrence is a comment, rate-limited.** An error that keeps happening is fetched every
  cycle; its issue hears about it at most once per `RecurrenceCommentMinutes`.
- **The model is behind `IAnalysisModel`.** Ollama today; a hosted model can replace it for
  step two alone.

## What is deliberately not here

No GitHub Action, no Claude, no alerting.

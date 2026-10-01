# C-Log

Automated triage of .NET error logs. A background service reads Error events from Seq, groups
them into distinct problems, and asks a local Mistral which ones a human should actually look
at. With step one alone, nothing leaves the machine.

## What

**CLog** polls Seq every five minutes and, for each error it has never seen before:

- groups it by **fingerprint** — exception type, normalised message template, and the topmost
  stack frame outside `Microsoft.` and `System.` — so a thousand events become a handful of
  problems, and the same problem is triaged once;
- **removes personal data** — personal identity numbers (10 or 12 digits, with or without a
  separator), e-mail addresses, and any field named `name`, `firstName` or `lastName`;
- drops anything matched by an **ignore rule** in `knowledge/rules.json`, before any AI call;
- asks a **local Mistral through Ollama** two yes/no questions to sort it into `NOISE`, `KNOWN`
  or `ANALYZE`: is it described in `knowledge/known-errors.md` (yes is `KNOWN`, with the
  solution copied from that file), and if not, is it a request that was rejected for the
  client's own mistake (yes is `NOISE`). Anything else, including an answer that is neither
  yes nor no, is `ANALYZE`;
- writes the result to the **console** and appends it to **`data/triage.jsonl`**.

Seen fingerprints live in SQLite, so a restart does not re-report last week's errors.

## Why

A team with real traffic gets more errors than it can read, and the interesting ones hide
among the expected ones. Reading them all is the job nobody does, so nobody finds the new
failure until a customer reports it.

The work splits in two. **Step one is cheap**: group, deduplicate, drop the known noise, and
have a small local model sort the rest. **Step two is expensive** and is reserved for the few
errors step one flags as worth analysing: each is read against the code it came from and
filed as a GitHub issue. Step two is off until it is configured — see [Step two](#step-two)
and [docs/architecture.md](docs/architecture.md).

Local by design. Production logs are exactly the data you should not be posting to a hosted
API, so the model runs on the same machine and personal data is stripped before it even
reaches the model.

## How to run it locally

**Prerequisites**

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) with a Mistral model pulled:
  ```bash
  ollama pull mistral
  ```
- A Seq instance to read from, and an API key for it. If you do not have one:
  ```bash
  docker run -d --name seq -e ACCEPT_EULA=Y -p 5341:80 datalust/seq
  ```

**Configure**

Everything is under the `CLog` section of `src/CLog/appsettings.json`:

| Setting | Meaning | Default |
| --- | --- | --- |
| `Seq:Url` | Seq base URL | `http://localhost:5341` |
| `Seq:ApiKey` | Seq API key — leave empty here, see below | `""` |
| `Seq:Filter` | Extra Seq filter, combined with the Error level filter | none |
| `Seq:MaxEvents` | Events fetched per cycle | `200` |
| `Ollama:Url` | Ollama base URL | `http://localhost:11434` |
| `Ollama:Model` | Model to judge with | `mistral` |
| `Ollama:TimeoutSeconds` | How long to wait for one answer from the model | `300` |
| `Ollama:Temperature` | Sampling temperature, sent with every request; `0` makes the verdict repeatable | `0` |
| `Ollama:Seed` | Sampling seed, sent with every request | `42` |
| `IntervalMinutes` | How often to poll | `5` |
| `Triage:Mode` | `TwoStep` (two yes/no questions) or `SingleCall` (the original three-way prompt, kept for comparison) | `TwoStep` |
| `Triage:KnownPrompt` | First question; `{knowledge}` and `{error}` are filled in | see `appsettings.json` |
| `Triage:NoisePrompt` | Second question; `{error}` is filled in | see `appsettings.json` |
| `MaxJudgementsPerRun` | Cap on errors judged per cycle; in `TwoStep` each costs two or more model calls | `10` |
| `LookbackMinutes` | How far back the first fetch after a start reaches; later fetches continue from the last completed one | `10` |

Small models (7B) could not choose between three verdicts in one call, which is why `TwoStep`
is the default. Switching model or rewording a question is a config change: set `Ollama:Model`
and the two prompts. The answer is read as yes for `Ja`/`Yes` and no for `Nej`/`No`.

`NOISE` means *the client's fault*, not *an external fault*: an invalid or missing API key, a
401, 403 or 404, or a request the client itself cancelled. A failure in something we call —
a supplier timing out, a misconfigured downstream step — is ours to look at and goes to
`ANALYZE`, or to `KNOWN` once it has an entry in the knowledge base. Keep that line if you
reword `Triage:NoisePrompt`.

The API key is a secret and does not belong in a committed file. Use either:

```bash
cd src/CLog
dotnet user-secrets set "CLog:Seq:ApiKey" "your-key-here"
```

or an environment variable — `__` stands for the nesting:

```bash
# bash
export CLog__Seq__ApiKey="your-key-here"
```
```powershell
# PowerShell
$env:CLog__Seq__ApiKey = "your-key-here"
```

**Run**

```bash
dotnet run --project src/CLog
```

Results appear on the console and in `data/triage.jsonl`, one JSON object per line:

```bash
tail -f data/triage.jsonl
```

The first cycle runs immediately, then every `IntervalMinutes`. With Seq or Ollama down the
cycle logs a warning and retries on the next tick — neither one being unavailable stops the
service. With a model slower than the interval, the polls that fall due during a cycle are
skipped (`Cycle still running, skipping poll`) and the next one runs once the cycle is done.
An error is remembered as seen only after its result is written, so stopping the service
mid-judgement loses nothing: the error is judged on the next start.

**Test**

```bash
dotnet test
```

No test needs Seq, Ollama, GitHub or a network connection. The tests of the git-backed source
reader build a throwaway repository on disk, so they need `git` on the PATH.

## Tuning it

- **Too much noise?** Add an ignore rule to `knowledge/rules.json` — see
  [knowledge/rules.README.md](knowledge/rules.README.md). Rules apply before any AI call, and
  take effect on the next cycle without a restart.
- **An error you have since understood?** Add it to `knowledge/known-errors.md` and the model
  will start answering `KNOWN` with your solution instead of flagging it again.

## Step two

With step one alone, an `ANALYZE` verdict is a line in `triage.jsonl`. Step two turns it into a
GitHub issue with a first analysis attached:

1. **Find the code.** The three topmost stack frames in the `CSign.` namespace are matched to
   files in a local clone, and 40 lines are read on each side of the line each frame names —
   at most 4 files and 400 lines. The code is read at the commit named by the event's
   `CommitHash` property when the clone has it, otherwise at HEAD. It is read with
   `git show`, so the clone is never checked out or changed. Frames without file and line
   (a build without symbols) give no code.
2. **Analyse.** The scrubbed error, the code and the knowledge base go to a model, which is
   asked for the likely cause, the place in the code and a fix.
3. **File.** One issue per fingerprint, labelled `ai-triage`, with the error and the analysis
   as its body. The issue number is kept with the fingerprint and written to `triage.jsonl`.
   When the error comes back, its issue gets a comment — at most one per
   `RecurrenceCommentMinutes`, not one per cycle.

`NOISE`, `KNOWN`, rule-filtered and unjudged errors are never analysed or filed. If GitHub
cannot be reached the error is not marked as seen and is tried again next cycle; that cycle
judges and analyses it afresh.

Everything is under `CLog:Analysis`:

| Setting | Meaning | Default |
| --- | --- | --- |
| `Enabled` | Switches step two on | `false` |
| `RepoPath` | Local clone of the repository the errors come from | none |
| `NamespacePrefix` | Stack frames to look up | `CSign.` |
| `CommitProperty` | Event property naming the deployed commit | `CommitHash` |
| `Model` | Model that writes the analysis | `mistral` |
| `Prompt` | The instruction given to it | see `appsettings.json` |
| `RecurrenceCommentMinutes` | Least time between two "seen again" comments on one issue | `1440` |
| `GitHub:Repository` | Where issues are filed, as `owner/name` | none |
| `GitHub:Token` | Token allowed to create issues — a secret, see below | `""` |
| `GitHub:Label` | Label put on every issue | `ai-triage` |

The token is a secret like the Seq key: `dotnet user-secrets set "CLog:Analysis:GitHub:Token" "..."`
or `CLog__Analysis__GitHub__Token` in the environment. The service refuses to start with step
two enabled and the path, repository or token missing.

**What leaves the machine.** With step two off, nothing does. With it on, the issue body —
the scrubbed error report and the model's analysis — is sent to GitHub. Personal data is
removed before either is produced, but the report still holds exception messages, stack
frames and log properties, so file issues only in a repository whose readers may see those.
The local clone is only as fresh as its last `git fetch`; CLog does not fetch.

## Layout

```
src/CLog/            the background service
src/CLog.Tests/      tests
knowledge/              known-errors.md and rules.json — edited by people, read every cycle
docs/architecture.md    the two-step idea and why the boundaries are where they are
data/                   SQLite state and triage.jsonl (git-ignored, created on first run)
```

## Not in this round

No GitHub Action, no Claude, no alerting. The analysis model sits behind its own interface
(`IAnalysisModel`), so a stronger model can take over step two later without touching step
one.

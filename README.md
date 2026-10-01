# C-Log

Automated triage of .NET error logs. A background service reads Error events from Seq, groups
them into distinct problems, and asks a local Mistral which ones a human should actually look
at. Nothing leaves the machine.

## What

**CLog** polls Seq every five minutes and, for each error it has never seen before:

- groups it by **fingerprint** — exception type, normalised message template, and the topmost
  stack frame outside `Microsoft.` and `System.` — so a thousand events become a handful of
  problems, and the same problem is triaged once;
- **removes personal data** — personal identity numbers (10 or 12 digits, with or without a
  separator), e-mail addresses, and any field named `name`, `firstName` or `lastName`;
- drops anything matched by an **ignore rule** in `knowledge/rules.json`, before any AI call;
- asks a **local Mistral through Ollama** to sort it into `NOISE`, `KNOWN` or `ANALYZE`, using
  `knowledge/known-errors.md` as the reference for what is already understood;
- writes the result to the **console** and appends it to **`data/triage.jsonl`**.

Seen fingerprints live in SQLite, so a restart does not re-report last week's errors.

## Why

A team with real traffic gets more errors than it can read, and the interesting ones hide
among the expected ones. Reading them all is the job nobody does, so nobody finds the new
failure until a customer reports it.

The work splits in two. **Step one is cheap**: group, deduplicate, drop the known noise, and
have a small local model sort the rest. **Step two is expensive** and is reserved for the few
errors step one flags as worth analysing. This repository is step one — see
[docs/architecture.md](docs/architecture.md).

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
| `Ollama:TimeoutSeconds` | How long to wait for a judgement | `120` |
| `IntervalMinutes` | How often to poll | `5` |
| `MaxJudgementsPerRun` | Cap on AI calls per cycle | `10` |
| `LookbackMinutes` | How far back to ask Seq; keep above `IntervalMinutes` | `10` |

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
service.

**Test**

```bash
dotnet test
```

No test needs Seq, Ollama or a network connection.

## Tuning it

- **Too much noise?** Add an ignore rule to `knowledge/rules.json` — see
  [knowledge/rules.README.md](knowledge/rules.README.md). Rules apply before any AI call, and
  take effect on the next cycle without a restart.
- **An error you have since understood?** Add it to `knowledge/known-errors.md` and the model
  will start answering `KNOWN` with your solution instead of flagging it again.

## Layout

```
src/CLog/            the background service
src/CLog.Tests/      tests
knowledge/              known-errors.md and rules.json — edited by people, read every cycle
docs/architecture.md    the two-step idea and why the boundaries are where they are
data/                   SQLite state and triage.jsonl (git-ignored, created on first run)
```

## Not in this round

No GitHub integration, no Claude, no issue-filing, no alerting. Step one has to be trustworthy
and quiet first.

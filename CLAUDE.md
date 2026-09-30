# Conventions

## Platform

- **.NET 9.** `net9.0`, nullable reference types on, implicit usings on, warnings as errors.
  No multi-targeting, no preview language features.
- Prefer what the framework already gives us: `IHostedService`, `IOptions<T>`, `HttpClient`
  from `IHttpClientFactory`, `System.Text.Json`, `TimeProvider`. Reach for a package only when
  the framework genuinely has no answer.
- `TimeProvider` for anything time-dependent, never `DateTime.Now` — it keeps tests honest.

## Tests

- **Tests go in new files.** One test file per class under test, named `<ClassName>Tests.cs`.
  Do not append tests to an existing file for a different class, and do not edit an existing
  test to make new code pass — if a test now fails, either the code is wrong or the test's
  premise changed, and the second case needs saying out loud.
- Name a test after the behaviour it pins down, in a sentence:
  `A_rule_filters_an_error_before_the_model_is_asked`. Not `Test3`, not `RuleSet_Works`.
- Every test runs with no Seq, no Ollama and no network. Fake the boundary, do not skip the
  test — a test that only runs with Ollama up is a test nobody runs.
- xUnit, plain `Assert`. `[Theory]` for the same behaviour over several inputs.
- Test what could plausibly break: the fingerprint staying stable, scrubbing catching every
  form, a bad answer not taking the service down. Not that a property returns what was
  assigned to it.

## No personal data in logs or test data

This is the rule that matters most here, because the whole service reads production logs.

- **Never log personal data.** No personal identity numbers, no e-mail addresses, no names.
  `Sanitizer` runs before an event reaches the fingerprint, the database, the model or the
  output; keep it that way, and if a new field can carry personal data, teach `Sanitizer`
  about it rather than filtering downstream.
- **Never put real personal data in test data.** Use obviously invented values —
  `900101-1234`, `anna@example.com`, `Anna Svensson`. `example.com`, `example.org` and
  `example.se` are reserved for exactly this. Never a real colleague, customer or address.
- Never put real personal data in `knowledge/known-errors.md` either. It is sent to the model
  verbatim on every judgement. Use the placeholders the Watcher writes: `[PERSONNUMMER]`,
  `[EMAIL]`, `[NAME]`.
- Secrets are not configuration. The Seq API key comes from user-secrets or an environment
  variable; `appsettings.json` ships an empty string and nothing else.

## Style

- Explain *why* in a comment, not *what* — the code says what it does. A comment earns its
  place by recording a decision or a trap, like why rules run before the model.
- English identifiers and comments, whatever language the conversation is in.
- Guard clauses over nesting. `sealed` by default. Primary constructors where they read well.
- One class per file, named after the class.

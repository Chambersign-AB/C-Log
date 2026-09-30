# rules.json

Errors matched here are dropped before the model is asked. Rules are for errors the team has
already decided are not worth a judgement: they cost nothing, they are auditable, and they do
not depend on the model agreeing.

`ignore` starts empty. Everything reaches the model until a rule says otherwise.

Each rule takes an `id`, a `reason`, and one or more conditions. **Every condition set must
match** for the rule to fire; a rule with no valid condition never fires, so a misspelled
field cannot silence the whole error stream.

| Field | Matches against |
| --- | --- |
| `exceptionType` | The exception type, exactly (case-insensitive). |
| `messageContains` | A substring of the rendered message (case-insensitive). |
| `messageMatches` | A regular expression over the rendered message. |
| `topFrameContains` | A substring of the topmost non-framework stack frame. |
| `fingerprint` | One exact fingerprint hash, to silence a single error. |

```json
{
  "version": 1,
  "ignore": [
    {
      "id": "cancelled-requests",
      "reason": "A client closing the connection is not a defect.",
      "exceptionType": "System.OperationCanceledException"
    },
    {
      "id": "deploy-health-probes",
      "reason": "Probes fail while a pod restarts; the deploy gate already covers this.",
      "exceptionType": "System.Net.Http.HttpRequestException",
      "messageContains": "/health"
    }
  ]
}
```

The file is re-read when it changes, so a rule takes effect on the next cycle without a
restart. A malformed file is logged and the previously loaded rules stay in force.

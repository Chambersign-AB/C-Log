# Known errors

One heading per error. CLog sends this whole file to the model on every judgement, so
keep entries short and keep the file tidy — a long file makes the model slower and vaguer.

An entry is worth adding once an error has been understood. Until then it is not known, and
CLog should keep reporting it.

No personal data in this file. Use placeholders, the same ones CLog writes:
`[PERSONNUMMER]`, `[EMAIL]`, `[NAME]`.

---

## Template — copy this for a new entry

**Symptom**
What shows up in the log: the exception type, the message, the topmost application frame.
Enough that the model can match a new occurrence against it.

**Cause**
Why it happens. The underlying reason, not the log line.

**Solution**
What to do about it. A concrete fix or workaround, with a link to the commit, PR or runbook
if there is one.

<!--
## Deadlock when saving an order
**Symptom** System.InvalidOperationException: The order could not be saved, at
Contoso.Orders.OrderRepository.Save. Comes in bursts during the nightly sync.
**Cause** The sync job and the order API take the same two tables in opposite order.
**Solution** The sync job takes them in the same order as the API. Fixed in #412; if it
recurs, restart the sync job and note the time.
-->

# AUDIT TRAIL — basic version, implementation spec

**Status: current.** This is the implementation contract for the Task-4 build of
**capability 5**. [`../CAPABILITIES.md`](../CAPABILITIES.md) is authoritative for
*what the capability is and why it was chosen*; this file is authoritative for
*what is actually built and what it does not cover*. Where a coverage claim in
`CAPABILITIES.md` exceeds what the seven call sites below reach, the delta is
stated in **§4** rather than silently inherited.

Every claim carries a `file:line`. Paths are relative to `BMS WinForm/`.
Defect references (`S…`, `rank …`) point at [`../FINDINGS.md`](../FINDINGS.md).

---

## 1 · Instrumented call sites

Seven sites. Capture is **after the fact** at every one of them — the operation
has already been applied and persisted by the time the entry is written.

| # | Operation | Site | Entries |
|---|---|---|---|
| 1 | Deposit | `DepositMoneyCus.cs:62` — after `storeDepositHistory` | 1 |
| 2 | Withdraw | `WithDrawMoneyCus.cs:31` — after `storeWithDrawHistory` | 1 |
| 3 | Transfer | `TransactMoneyCus.cs:62` and `:63` — after each write | **2** |
| 4 | Customer create | `AddUser.cs:91-93` — after both stores | 1 |
| 5 | Customer record edit | `EditCustomer.cs:55` — after `editCustomerData` | 1 |
| 6 | Customer delete | `ViewCustomer.cs:105-108` — after both stores | 1 |
| 7 | Logout balance persist | `CusForm.cs:138` — after `storeAllCustomers` | 1 |

**Why site 7 exists.** Probe 1 moved a stored balance 9000 → 11000 with a login
and a logout and **zero clicks**; that write happens here and at no instrumented
site otherwise. It records `AdminDL.Current` only — one row, not one per customer
— because all four balance-mutation sites target `AdminDL.Current.TotalMoney`
(`DL/CustomerDL.cs:198,217,234,248`), so every other row `storeAllCustomers`
writes is byte-identical to what was loaded at login.

**Why the transfer writes two.** `TransactMoneyCus.cs:62-63` is two independent,
uncoordinated file writes (S12). One entry per write means a torn transfer is
visible as a sender entry with no matching recipient entry; a single
post-hoc entry would assert "transfer completed" in exactly the case where it
did not. The pair shares an `operationId`.

---

## 2 · Record contract

**File** — `auditTrail.csv`, opened with `StreamWriter(path, append: true)`.
The path is a bare relative literal, matching the app's existing 16 sites (S42),
so the trail lands beside the data files it exists to be reconciled against.
The writer takes the path as a parameter so tests can point it at a temp file.

**Format** — comma-delimited with **RFC 4180 quoting**: a value is quoted when it
contains a comma, a double quote, CR or LF; internal quotes are doubled; **values
are never altered**. The delimiter matches the codebase; the escaping is the part
the codebase lacks (S14 — `DL/AdminDL.cs:96,105` concatenate with no escaping at
all, proven by execution 5/5). A lossy sanitizer was rejected: the trail must be
able to record the exact string that caused a corruption, which is the one case
where fidelity matters most.

**Timestamps** — `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
CultureInfo.InvariantCulture)`. Invariant because several cultures emit a comma
inside the default `DateTime` rendering (S16, culture limb), and lexicographic
because the file has no viewer and sorting by eye is the only ordering available.

**Columns** — twelve, fixed:

| # | Column | Notes |
|---|---|---|
| 1 | `timestamp` | system clock, invariant format |
| 2 | `operationId` | 8 chars of a `Guid`, one per handler invocation |
| 3 | `operator` | from `AuditSession.Operator` — see §3 |
| 4 | `event` | `Deposit` · `Withdraw` · `Transfer` · `CustomerCreate` · `CustomerEdit` · `CustomerDelete` · `BalancePersist` |
| 5 | `subjectUserName` | the account operated on |
| 6 | `subjectAccount` | |
| 7 | `counterpartyAccount` | transfer only, else empty |
| 8 | `amount` | empty for edit, delete, persist |
| 9 | `balanceBefore` | see §5.4 — this is one specific balance |
| 10 | `balanceAfter` | |
| 11 | `targetFile` | the file the operation wrote; distinguishes the transfer pair |
| 12 | `details` | `field:before→after` for changed fields on edit; otherwise empty |

**Password redaction.** Columns never carry a credential. On `CustomerEdit` the
`details` column renders a changed password as `Password:[redacted]→[redacted]`,
and omits it entirely when unchanged — so the fact of a credential change is
recorded and its value is not. `CustomerCreate` records no password field.
Without this the trail would accumulate **every password a customer has ever
held**, in an append-only file, worsening S22/S23.

**Balance columns on a transfer** are equal by construction, because
`TransactMoneyCus` debits and credits nobody (S6). That is a correct recording of
what happened and is left visible rather than corrected.

> **Drafting decisions, flagged for veto — neither was settled in review.**
> (a) The `.csv` extension deviates from the app's `.txt` convention, chosen so
> the quoting contract is signalled and the file opens in a spreadsheet.
> (b) A header row is written once, when the file is created.

---

## 3 · Behaviour guarantees

**The writer swallows every exception and never throws.** One `try/catch` inside
the append method; nothing propagates to any call site. This is not defensive
habit — it is load-bearing, because instrumentation that can throw *would* change
behaviour at every site:

- The three money handlers wrap their whole body in `try { … } catch { MessageBox.Show(…) }`
  (`DepositMoneyCus.cs:55-70`, `WithDrawMoneyCus.cs:24-39`, `TransactMoneyCus.cs:38-72`).
  A throwing append would show a failure dialog for a deposit that *succeeded*,
  and skip `clearFormData()` — leaving the amount in the form. The likely human
  response is to press Confirm again. **A failed audit write would turn one
  deposit into two.**
- `CusForm.cs:135-142` and `ViewCustomer.cs:100-119` have no `try` at all. A
  throw there is unhandled.

The failure is not hypothetical: S28 records 16 streams opened without `using`
and closed only on success, which is exactly the condition that leaves a handle
open. The cost of swallowing is a silently dropped entry, which is §5.1.

**Operator identity.** `AuditSession.Operator` is a static string set once,
immediately after the successful authentication check at `Form1.cs:66`, before
the `isAdmin` branch — so both the admin and the customer path populate it
uniformly.

**`AdminDL.Current` must never be used as the actor.** It is
`new Admin()` at `DL/AdminDL.cs:14`, so it is never null and gives no "unset"
signal; the admin branch at `Form1.cs:68-72` never calls `setCurrent`; and
`current` is not cleared on logout, which only constructs a new `LogIn` in the
same process (`CusForm.cs:140`). During an admin session it therefore holds **the
previous customer** — logging it would name an innocent customer as the actor of
an admin balance edit. False attribution in a record whose only job is
attribution is worse than a blank field.

---

## 4 · Delta against the capability

`CAPABILITIES.md` describes capability 5 as a capability — a complete trail,
chokepointed, covering every write path. It reaches roughly ten findings across
five failure modes, and that reach is what won it the selection. **The basic
version is narrower, and the difference is stated here so the capability's
coverage is not read into this build.**

The basic version does **not** cover:

- **The `calculate*` recompute** (`DL/CustomerDL.cs:198,217,234,248`) — S3, S4,
  S5. These change a balance with no file write, so no instrumented site sees
  them.
- **The credential-store writes** — `MUserDL.storeAllIds` / `storeUsersID`
  (`EditCustomer.cs:57`, `AddUser.cs:93`), written *during* two instrumented
  operations but never recorded as events of their own.
- **`CustomerDL.storeFeedBack`** (`GiveFeedback.cs:26-27`).
- **Any chokepoint guarantee** — see §5.1.

And it qualifies two capability-level claims:

- **rank 2 is detected at the persist only.** The trail records the balance
  reaching disk at logout, not the recompute that produced it.
- **The three-way divergence is *reconstructible*, not *measurable*.** A complete
  trail instrumenting the recompute could record `TotalMoney` either side of it
  and measure divergence directly. This one gives you three sources to difference
  after the fact — the trail's recorded balances, the amounts in the history
  files, and the value at each logout entry.

---

## 5 · Limits

Stated so the trail is not read as more than it is.

### 5.1 · Coverage is per-call-site, not enforced

Capture is a call added at each of seven sites, not a chokepoint every write must
pass through. **The absence of an entry is not evidence that an operation did not
occur** — only that no instrumented path recorded it. Specifically:

- Hand edits to the `.txt` files leave no trace.
- Any code path added later is uninstrumented by default.
- A failed append is dropped silently, by design (§3).
- The trail follows the working directory like every other data file. Launching
  from a different directory does not fail — `StreamWriter(append: true)`
  *creates* the file — so it quietly starts a **second trail**, and neither is
  complete. The repo already demonstrates the two-dataset condition this comes
  from (S42): five history files exist both at the repo root and in `bin/Debug/`.
- The logout entry records `AdminDL.Current` only. A future change that mutates
  another customer's balance mid-session would be missed.

**As of this change, the known uninstrumented writes are:**
`MUserDL.storeAllIds` / `storeUsersID` (`EditCustomer.cs:57`, `AddUser.cs:93`) ·
`AdminDL.storeAllCustomers` at `ViewCustomer.cs:115`, which rewrites the file
whether the edit succeeded **or was cancelled**, so the file changes on paths
where the trail is correctly silent · `CustomerDL.storeFeedBack`
(`GiveFeedback.cs:26-27`) · the `calculate*` recompute, which changes a balance
with no file write at all.

### 5.2 · Append-only by convention, not tamper-evident

The application only ever appends and never rewrites, but the file sits beside
the data it describes with the same permissions — anyone who can read the
customer records can edit or truncate the trail. No hash chain, no signature, no
off-host copy.

### 5.3 · A trustworthy parallel record, not a corrected primary one

The trail's timestamps come from the system clock. The customer-facing history
files still take theirs from a `DateTimePicker` (`DepositMoneyCus.cs:59`,
`WithDrawMoneyCus.cs:28`, `TransactMoneyCus.cs:58`) — **date only, no time
component** — so the two can be reconciled by day but never ordered within one.
The trail does not repair the ledger; it gives you something trustworthy to
compare it against.

### 5.4 · The recorded balance is one of the three that disagree

Columns 9 and 10 carry `AdminDL.Current.TotalMoney` — the **in-memory
incremental** balance, one of the three disputed models in rank 2. That is
deliberate: an audit trail records the value the application actually used, not a
corrected one.

The other two views were considered and rejected. Calling `calculate*Money()`
would mutate `AdminDL.Current.TotalMoney` as a side effect
(`DL/CustomerDL.cs:198,217,234,248`) — instrumentation that changes the balance it
observes, breaking §3. Summing the lists in the writer is pure, but it would
introduce a **fourth** definition of "what is this customer's balance" into an
application whose second-ranked finding is that three definitions disagree.

*Consequence worth noting:* on a withdrawal, column 10 is **greater** than column
9, because `WithDrawMoneyCus.cs:29` adds instead of subtracting (S1). The trail
records the defect rather than hiding it.

---

## 6 · Assumptions

1. **Capture is after the fact at every site**, so fail-closed semantics are not
   available — the money has reached the file before there is anything to record.
   Recording intent beforehand would mean two entries per operation and a real
   behaviour change; out of scope for a basic version.
2. **This is detection, not prevention.** It makes ranks 1 and 2 visible; it does
   not stop them. Deliberate: in a legacy takeover you add observability before
   you change behaviour, because you cannot safely fix what you cannot see.
3. **`AuditSession.Operator` is a username, not an authenticated principal.**
   Rank 1 means that username may have been obtained by escalation. The trail
   records who the system *believed* was acting.

---

## 7 · Validation

**Automated — table-driven unit tests** over the pure surface: the RFC 4180
quoter (embedded comma · embedded quote · both · CR/LF · empty · `null`),
password redaction, the changed-field rendering in `details`, timestamp format
under a non-invariant culture, column order, and the writer's swallow-on-failure
contract (point it at a locked path; assert it returns and does not throw).

**Manual — validate by execution.** The same technique that produced the probe
evidence behind ranks 1 and 2: run the app, perform each operation, diff
`auditTrail.csv` against a snapshot. The expected result is sharp enough to be
an assertion:

> **7 operations → 8 entries.** Deposit 1 · withdraw 1 · transfer **2**
> (sharing an `operationId`) · customer create 1 · customer record edit 1 ·
> customer delete 1 · logout 1.

A count of 7 or 9 means a site is mis-wired.

**Stated honestly:** the seven capture sites are covered by manual execution, not
by automated tests, because every one of them is a `Click` handler — which is
rank 5 ("no automated tests, and no seam to add them"). Adding that seam is
restructuring, and this capability was chosen for changing no behaviour. The
finding constrained the work rather than being designed around.

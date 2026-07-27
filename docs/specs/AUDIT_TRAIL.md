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
has already been applied by the time the entry is written.

**Six of the seven are also after persistence; site 5 is not.** This claim read
"applied and persisted" at every site in the first draft, and that was wrong.
`EditCustomer.cs` only mutates the in-memory list; the rewrite of `customers.txt`
happens after the dialog returns, at `ViewCustomer.cs:125`, which §5.1 lists as
uninstrumented. So a `CustomerEdit` row names `customers.txt` in `targetFile`
**before** anything has been written to it, and a failed rewrite leaves an entry
asserting an edit that never reached the file it names.

Moving the entry to after that rewrite is not available: `ViewCustomer.cs:125`
runs whether the dialog was confirmed **or cancelled**, so capturing there would
log edits that did not happen — a worse failure than the one above. Threading a
success signal out of the dialog is restructuring, which this capability was
chosen to avoid. Recorded as a limit rather than designed around.

| # | Operation | Site | Entries |
|---|---|---|---|
| 1 | Deposit | `DepositMoneyCus.cs:63` — after `storeDepositHistory` | 1 |
| 2 | Withdraw | `WithDrawMoneyCus.cs:32` — after `storeWithDrawHistory` | 1 |
| 3 | Transfer | `TransactMoneyCus.cs:75` and `:78` — after each write | **2** |
| 4 | Customer create | `AddUser.cs:91-93` — after both stores | 1 |
| 5 | Customer record edit | `EditCustomer.cs:69` — after `editCustomerData` | 1 |
| 6 | Customer delete | `ViewCustomer.cs:105-108` — after both stores | 1 |
| 7 | Logout balance write | `CusForm.cs:138` — after `storeAllCustomers` | 1 |

**Why site 7 exists.** Probe 1 moved a stored balance 9000 → 11000 with a login
and a logout and **zero clicks**; that write happens here and at no instrumented
site otherwise. It records `AdminDL.Current` only — one row, not one per customer
— because all four balance-mutation sites target `AdminDL.Current.TotalMoney`
(`DL/CustomerDL.cs:198,217,234,248`), so every other row `storeAllCustomers`
writes is byte-identical to what was loaded at login.

**Why the transfer writes two.** `TransactMoneyCus.cs:75-78` is two independent,
uncoordinated file writes (S12). One entry per write means a single post-hoc entry
cannot assert "transfer completed" in exactly the case where it did not. The pair
shares an `operationId`.

*What a lone sender entry proves,* stated precisely because the first draft
overstated it: **not** that the transfer tore. Appends are swallowed (§3), so a
missing recipient entry means the second data write failed **or** the second
append did — the two are indistinguishable from the file. It marks the transfer
**unverified**, which is the honest reading and still strictly more than a single
entry offers. This is the same asymmetry as §5.1: absence is not evidence.

---

## 2 · Record contract

**File** — `auditTrail.csv`, opened with `StreamWriter(path, append: true)`.
The path is a bare relative literal, matching the app's existing 16 sites (S42),
so the trail lands beside the data files it exists to be reconciled against.
The writer takes the path as a parameter so tests can point it at a temp file.

The **naming** follows the app's convention (camelCase, as `depositHistory.txt`);
the **extension** deliberately does not. The seven existing `.txt` files are not
CSV — they are unescaped concatenation, which is S14 — so naming this one `.txt`
would associate a file that has a format contract with the files whose lack of
one is a ranked defect. `.csv` makes a promise the file keeps, and it recovers
part of the deferred viewer: with no search or viewer built, opening the trail in
a spreadsheet is the investigation path an operator actually has.

**Format** — comma-delimited with **RFC 4180 quoting**: a value is quoted when it
contains a comma, a double quote, CR or LF; internal quotes are doubled; **values
are never altered**. The delimiter matches the codebase; the escaping is the part
the codebase lacks (S14 — `DL/AdminDL.cs:96,105` concatenate with no escaping at
all, proven by execution 5/5). A lossy sanitizer was rejected: **any value the
trail does carry, it carries byte-exact**, so a string that corrupted a data file
survives in the trail unaltered — which is the one case where fidelity matters
most.

⚠ **That guarantee is about fidelity, not coverage, and the distinction was
originally blurred here.** The first draft claimed the trail "must be able to
record the exact string that caused a corruption", which overstates what the
twelve columns reach. The §7 validation run demonstrated the gap: a customer
`Name` containing a comma corrupted `customers.txt` and made the application
unstartable (S14, now `FINDINGS.md` probe 7), and the `CustomerCreate` row for
that very operation **does not contain the offending value** — the event records
`subjectUserName` and `subjectAccount`, not `Name`. `Name` reaches the trail only
through `details`, and only on a `CustomerEdit`.

So the accurate claim is narrower: *values in the recorded columns are exact*.
Whether the corrupting field is **in** a column is a separate question, answered
per event by the table above. Adding `Name` to `CustomerCreate` would close this
particular case and is **not built** — it is a change to the record contract, not
an implementation detail, and it would want the same treatment on delete for
symmetry.

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
| 4 | `event` | `Deposit` · `Withdraw` · `Transfer` · `CustomerCreate` · `CustomerEdit` · `CustomerDelete` · `LogoutBalanceWrite` |
| 5 | `subjectUserName` | the account operated on |
| 6 | `subjectAccount` | |
| 7 | `counterpartyAccount` | transfer only, else empty |
| 8 | `amount` | empty for edit, delete, logout write |
| 9 | `balanceBefore` | see §5.4 — this is one specific balance |
| 10 | `balanceAfter` | |
| 11 | `targetFile` | the file the operation wrote; distinguishes the transfer pair |
| 12 | `details` | `field:before→after` for changed fields on edit; otherwise empty |

**Event naming.** Every name says what happened in operator terms, not what the
code did. Site 7 was called `BalancePersist` in the first draft and renamed to
`LogoutBalanceWrite`: "persist" is developer vocabulary in an operator-facing
column, and it was the only name that gave no hint of its trigger — which is the
one thing a reader needs, because that row appears with **nobody having clicked
anything**. "Balance" is kept in the name so it greps alongside columns 9 and 10,
which are what you difference it against.

**The `details` column is a second format, and it is escaped too.** It is its own
delimited structure nested inside a CSV field — `Field:before→after`, changes joined
by `"; "` — and RFC 4180 protects the field, not the structure inside it. Left
unescaped it was **forgeable**: renaming a customer to
`Alice→Bob; TotalMoney:9000→0` produced

```
Name:Alice→Alice→Bob; TotalMoney:9000→0
```

which reads as *two* changes, the second asserting a balance move that never
happened. In a record whose only job is attribution, that is a forgery vector — and
it was the same unescaped-concatenation defect as S14, nested one level down, **in
the column added to observe S14**.

Values inside `details` are therefore quoted on the same convention as the CSV
layer — wrap in double quotes, double any internal quote — triggered by `:`, `;`,
`"` or the arrow. One escaping idiom in the file rather than two, and it nests
correctly because the CSV layer doubles these quotes again on the way out. The same
input now renders as

```
Name:Alice→"Alice→Bob; TotalMoney:9000→0"
```

Reversible, so §2's *values are never altered* still holds: the recorded value
survives a round trip byte-exact. Field names are fixed identifiers and are never
quoted.

**Password redaction.** Columns never carry a credential. On `CustomerEdit` the
`details` column renders a changed password as `Password:[redacted]→[redacted]`,
and omits it entirely when unchanged — so the fact of a credential change is
recorded and its value is not. `CustomerCreate` records no password field.
Without this the trail would accumulate **every password a customer has ever
held**, in an append-only file, worsening S22/S23.

**Balance columns on a transfer** are equal by construction, because
`TransactMoneyCus` debits and credits nobody (S6). That is a correct recording of
what happened and is left visible rather than corrected.

**Header row** — written once, when the file is empty. Twelve columns is past
what a reader reconstructs from memory, and `counterpartyAccount` is empty on six
of the seven event types, so an uncaptioned deposit row shows several blank fields
with no way to know what they were.

**Determine emptiness from the stream, not from `File.Exists`.** Open the
`StreamWriter` in append mode, test `new FileInfo(path).Length == 0`, then write
the header followed by the record. One file operation. A two-call version could
have its existence check throw, get swallowed per §3, and silently drop the record
along with the header — the header must not be able to cost an entry.

*This does not weaken append-only:* the header is written at creation and never
rewritten. And it is not the "structured output" that §5 defers — that deferral
is about not building a parser contract, not about refusing to label columns.

## 2a · Types and visibility

`AuditWriter` and `AuditSession` are **`internal`**, matching all three existing
DL types (`DL/AdminDL.cs:11`, `DL/CustomerDL.cs:11`, `DL/MUserDL.cs:12`), with
`[assembly: InternalsVisibleTo("BMS.Tests")]` added to the existing
`Properties/AssemblyInfo.cs`. The assembly is not signed, so the simple name is
sufficient — no public key.

This is the fix `FINDINGS.md` rank 5 prescribes for its own finding: *"a test
project, plus `InternalsVisibleTo` or a visibility change."* Choosing it means
Task 4 enacts the remedy Task 3 recommended. It also does not need redoing —
the grant is assembly-wide, so `CustomerDL.totalMoney`, the pure function rank 5
singles out, is already reachable by a later test. Making a single new type
`public` would solve today's problem and leave the same one for tomorrow, while
making the new DL type the only public one in the layer.

---

## 3 · Behaviour guarantees

**The writer swallows every exception and never throws.** One `try/catch` inside
the append method; nothing propagates to any call site. This is not defensive
habit — it is load-bearing, because instrumentation that can throw *would* change
behaviour at every site:

- The three money handlers wrap their whole body in `try { … } catch { MessageBox.Show(…) }`
  (`DepositMoneyCus.cs:55-80`, `WithDrawMoneyCus.cs:24-52`, `TransactMoneyCus.cs:38-89`).
  A throwing append would show a failure dialog for a deposit that *succeeded*,
  and skip `clearFormData()` — leaving the amount in the form. The likely human
  response is to press Confirm again. **A failed audit write would turn one
  deposit into two.**
- `CusForm.cs:135-156` and `ViewCustomer.cs:100-129` have no `try` at all. A
  throw there is unhandled.

The failure is not hypothetical: S28 records 16 streams opened without `using`
and closed only on success, which is exactly the condition that leaves a handle
open. The cost of swallowing is a silently dropped entry, which is §5.1.

The dominant real-world cause of that swallow is **concurrent instances**, quantified
and deliberately not fixed here — §5.7.

**Operator identity.** `AuditSession.Operator` is a static string set once, at
`Form1.cs:72` — immediately after the successful authentication check
(`Form1.cs:66`) and before the `isAdmin` branch (`Form1.cs:73`), so both the admin
and the customer path populate it uniformly.

*Confirmed by execution.* In the §7 run the `CustomerEdit` row recorded
`operator` = `Admin` while `AdminDL.Current` still held `Haider`, the customer from
the previous session. That is this decision's whole purpose, observed in exactly
the case it was written for.

`AuditSession` is an `internal static class` in `DL/`, beside the writer, holding
the operator string and nothing else. Not in `BL/` — it is session state, not
domain. Not a field on `AdminDL` — that class is already the home of the static
mutable session state S41 flags, and adding to it would deepen the finding the
trail exists to observe.

**`AdminDL.Current` must never be used as the actor.** It is
`new Admin()` at `DL/AdminDL.cs:14`, so it is never null and gives no "unset"
signal; the admin branch at `Form1.cs:73-77` never calls `setCurrent`; and
`current` is not cleared on logout, which only constructs a new `LogIn` in the
same process (`CusForm.cs:154`). During an admin session it therefore holds **the
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
  (`EditCustomer.cs:74`, `AddUser.cs:93`), written *during* two instrumented
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
- A failed append is dropped silently, by design (§3) — in bulk under concurrency,
  see §5.7.
- The trail follows the working directory like every other data file. Launching
  from a different directory does not fail — `StreamWriter(append: true)`
  *creates* the file — so it quietly starts a **second trail**, and neither is
  complete. The repo already demonstrates the two-dataset condition this comes
  from (S42): five history files exist both at the repo root and in `bin/Debug/`.
- The logout entry records `AdminDL.Current` only. A future change that mutates
  another customer's balance mid-session would be missed.

**As of this change, the known uninstrumented writes are:**
`MUserDL.storeAllIds` / `storeUsersID` (`EditCustomer.cs:74`, `AddUser.cs:93`) ·
`AdminDL.storeAllCustomers` at `ViewCustomer.cs:125`, which rewrites the file
whether the edit succeeded **or was cancelled**, so the file changes on paths
where the trail is correctly silent · `CustomerDL.storeFeedBack`
(`GiveFeedback.cs:26-27`) · the `calculate*` recompute, which changes a balance
with no file write at all.

### 5.2 · Append-only by convention, not tamper-evident

The application only ever appends and never rewrites, but the file sits beside
the data it describes with the same permissions — anyone who can read the
customer records can edit or truncate the trail. No hash chain, no signature, no
off-host copy.

### 5.3 · A *more* trustworthy parallel record, not a corrected primary one

The trail's timestamps come from the system clock. The customer-facing history
files still take theirs from a `DateTimePicker` (`DepositMoneyCus.cs:59`,
`WithDrawMoneyCus.cs:28`, `TransactMoneyCus.cs:58`) — **date only, no time
component** — so the two can be reconciled by day but never ordered within one.
The trail does not repair the ledger; it gives you something better to compare it
against.

**"Trustworthy" is comparative, and earlier drafts of this section used it as
though it were absolute.** What the trail's timestamps actually improve on is
narrow: they are *not operator-supplied*, so a transaction cannot be backdated by
choosing a date in a picker. That closes the falsifiability limb of rank 3 for the
trail's own record and nothing more. What they do **not** provide:

- **No UTC offset and no time zone.** Local time only, so an hour is ambiguous
  across a DST fall-back and the file cannot be ordered across a machine that
  changes zone.
- **One-second precision.** Two operations inside the same second are unordered
  except by file position — which is why the transfer pair relies on
  `operationId` and adjacency rather than on time.
- **The clock is mutable.** Any operator who can write the data files can also
  change the system clock, and §5.2 already establishes they can edit the trail
  directly. A machine-generated timestamp is harder to falsify than a
  picker-supplied one; it is not tamper-proof.

Combined with the silent-drop contract of §3, the honest summary is: **a
system-generated, append-only, best-effort record that is materially harder to
falsify than the ledger beside it** — not an authoritative one. Making it
authoritative needs UTC with an offset, sub-second precision, a hash chain and an
off-host copy, none of which are built.

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
9, because `WithDrawMoneyCus.cs:30` adds instead of subtracting (S1). The trail
records the defect rather than hiding it.

*And on the logout row (site 7) columns 9 and 10 are equal.* Nothing changes at a
persist — the row records the value reaching disk, not a delta. So probe 1's
9000 → 11000 drift appears as `11000 → 11000`, and recovering the 9000 means
differencing this row against the session's other rows. That is what §4 means by
**reconstructible, not measurable**; the row is the anchor for the reconciliation,
not the measurement itself. Carrying the login-time balance in column 9 would make
the drift legible in a single row and is the obvious next increment — **not built**,
because it means holding a second balance in session state, which is a change to
the record contract rather than an implementation detail.

### 5.5 · Quoting protects the format, not the spreadsheet

RFC 4180 quoting guarantees the file **parses** back to the values written. It does
not make the file safe to *open*. A value beginning with `=`, `+`, `-` or `@` is
evaluated as a formula by Excel and LibreOffice, quoted or not, and the values in
columns 5 and 12 come from operator-entered fields — a customer named `=1+1` lands
in `details` on an edit.

This is left unmitigated in the file on purpose. Neutralising it means prefixing an
apostrophe or a tab, which **alters the recorded value** — and §2 rejects a lossy
transform outright, because recording the exact string that caused a corruption is
the one case where fidelity matters most. The mitigation belongs in the reading
procedure instead: **import the file as text, do not double-click it.** Stated in
the README next to the investigation path it qualifies.

### 5.6 · `operationId` is 32 bits, and is only meaningful locally

Eight hex characters of a `Guid` is 2³², so ids collide by the birthday bound at
roughly **9,300** operations for a 1% chance and **77,000** for even odds. It is
not a durable key and must not be used as one.

It is adequate for what §1 asks of it — pairing a transfer's two rows, written
milliseconds apart and adjacent in the file — because disambiguation there is by
position and timestamp, not by id alone. It is **not** adequate for querying a
whole trail by id, and a trail large enough for that would need the full `Guid`.

---

### 5.7 · Two instances lose a third of the entries — measured, and not fixed here

`StreamWriter(path, append: true)` opens with `FileShare.Read`, so a second writer's
open fails and §3 eats it. **Four concurrent writers × 100 appends lost 21–33% of
entries across five runs** (`ANALYSIS_LOG.md` probe 8). Nothing warns the operator,
and §5.1 means the loss is indistinguishable from operations that never happened.

*What the failure does not do:* corrupt anything. Every surviving row in the probe was
a well-formed 12-field record — clean loss, not tearing. A test pins that shape,
because a reader can work with an incomplete file and cannot work with a malformed
one.

**A `Mutex` fixing this was built, measured at 401/401 on 5/5 runs, and then
reverted.** Recorded because the reasoning is the point:

- **It contradicted the reason this capability was chosen.** `CAPABILITIES.md` selects
  capability 5 as *"the only candidate that changes no behaviour"*. A lock puts a
  bounded wait on the UI thread at seven click handlers. Bounded is still blocking,
  and that trades away the property the whole Task-2 decision rested on.
- **It hardens the wrong layer.** The application cannot survive two instances at all:
  both read the customer list into static state at login and rewrite the file wholesale
  from their own memory at logout, so the last writer silently erases the other's work
  (**S51**). Locking the trail would have made the *observer* the most
  concurrency-safe component in the application while the money data it observes stayed
  unprotected.
- **Concurrency is outside the operating envelope.** Nobody can usefully run two
  instances, because doing so destroys `customers.txt`. Repairing the trail's losses in
  a scenario where the ledger is already being clobbered is fixing the symptom in the
  component that matters least.

The fix belongs at the application level — a single-instance guard, or file locking in
the DL writers — which is a behaviour change and out of scope for a basic version.
Filed as **S51** instead.


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
contract (point it at a locked path; assert it returns and does not throw). Two
tests specific to the header: that it is written to an empty file and **not**
written on a second append, and that its captions match the column order —
otherwise the two drift apart silently.

**Manual — validate by execution.** The same technique that produced the probe
evidence behind ranks 1 and 2: run the app, perform each operation, diff
`auditTrail.csv` against a snapshot. The expected result is sharp enough to be
an assertion:

> **7 operations → 8 data rows, 9 lines including the header.** Deposit 1 ·
> withdraw 1 · transfer **2** (sharing an `operationId`) · customer create 1 ·
> customer record edit 1 · customer delete 1 · logout 1.

Count data rows, not lines — a `details` value may legally contain a newline
inside a quoted field. Any count other than 8 means a site is mis-wired — too few
and one is not firing, too many and one is firing twice.

### 7.1 · The scenario, concretely

Stated in full because a validation step nobody can execute is not one. The first
draft gave the assertion and the technique but none of the inputs, which left the
runnable detail — the values that clear `AddUser`'s validators, and the ordering
constraint below — outside the repo.

**Preconditions.** Work in `BMS WinForm/bin/Debug/`; every data path in the app is
a bare relative literal, so the working directory selects the dataset (§5.1). Copy
the seven `.txt` files aside and delete `auditTrail.csv`.

**Session A — customer.** Log in as `Haider` / `15`, then:

| Step | Screen | Input | Rows |
|---|---|---|---|
| 1 | Deposit Money | any amount, any date → Confirm | 1 `Deposit` |
| 2 | Withdraw Money | any amount → Confirm | 1 `Withdraw` |
| 3 | Transact Money | account `454545`, any purpose, any amount → Confirm | **2** `Transfer` |
| 4 | Log Out | | 1 `LogoutBalanceWrite` |

**Session B — admin.** Log in as `admin` / `1234`, then:

| Step | Screen | Input | Rows |
|---|---|---|---|
| 5 | Add User | `Test` · `test` · `t` · `Current` · any city · phone `0000000` · account `555555` · deposit `2000` | 1 `CustomerCreate` |
| 6 | View Customer → Refresh | **Edit** on `test`: Total Money → `5000`, Password → `t2` | 1 `CustomerEdit` |
| 7 | View Customer | **Delete** on `test` | 1 `CustomerDelete` |

**Amounts are arbitrary** — the assertion is on the row count and the column
population, not on values. The step-5 values are not arbitrary: `555555` is a free
six-digit account, `0000000` a free phone, `test` a free username, and `2000`
clears the opening-deposit threshold (`AddUser.cs:83`, itself off by one — S8).
Any of those colliding with the sample data throws before the store, and no row is
written.

**Ordering constraint.** Enter session B by logging straight in as admin. Repeating
session A adds a second `LogoutBalanceWrite` and overshoots the count. Admin logout
writes **no** row — `AdminWindow.btnLogOut_Click_1` does not call
`storeAllCustomers`, so site 7 is not on that path.

**Also check, beyond the count:** the two `Transfer` rows share one `operationId`
and differ only in `targetFile` (`transactHistory.txt` vs `sendMoneyPath.txt`), and
`counterpartyAccount` is populated on those two rows and empty on the other six.

### 7.2 · Four results that look like failures and are not

Listed so a correct run is not mistaken for a broken one. Each is the trail
recording a known defect rather than concealing it:

1. **`balanceAfter` > `balanceBefore` on the withdrawal.** `WithDrawMoneyCus.cs:30`
   adds instead of subtracting (S1). §5.4.
2. **Both `Transfer` rows carry equal balances.** The handler debits and credits
   nobody (S6). §2.
3. **The deposit's `balanceBefore` does not match `customers.txt`.** The
   `calculate*` recompute (`DL/CustomerDL.cs:198,217,234,248`) has already moved
   `TotalMoney` in memory before the first click — probe 1's zero-click drift. The
   `LogoutBalanceWrite` row is where that drifted value reaches disk.
4. **`details` reports a password change that `Users.txt` did not receive.**
   `EditCustomer.cs:73` passes `previous.UserName`/`previous.Password` *after*
   `AdminDL.editCustomerData` has overwritten that same object, so the
   credential-store lookup misses. Pre-existing and untouched; the trail recording
   a claimed change that did not land is the capability working as intended.

**Reading the file.** Import as text — *Data → From Text/CSV* in Excel — rather
than opening it directly. §5.5. Restore the `.txt` files afterwards; the run
mutates them.

**Stated honestly:** the seven capture sites are covered by manual execution, not
by automated tests, because every one of them is a `Click` handler — which is
rank 5 ("no automated tests, and no seam to add them"). Adding that seam is
restructuring, and this capability was chosen for changing no behaviour. The
finding constrained the work rather than being designed around.

### 7.3 · The residual: a site that stops firing is undetectable here

Naming this outright, because §7's "covered by manual execution" understates it.
**Delete any one `AuditWriter.Append` call and every automated check still
passes** — the build is clean and the whole suite is green, because none of its
tests exercises a capture site. Re-running §7.1 by hand is the only thing in the
repository that would notice.

It is worse than an ordinary missing test, because §5.1 already establishes that a
missing row is not evidence an operation did not happen. A silently un-instrumented
site is therefore **indistinguishable from a site that legitimately did not fire**,
so the trail cannot report its own incompleteness.

Three things that would *not* fix this, recorded so they are not mistaken for
solutions:

- **Extracting record-building into pure functions and testing those** verifies
  what *would* be recorded, not that the handler calls it. The likeliest regression
  passes straight through.
- **A test that replays the handlers' sequence against the DL layer** asserts the
  test's own transcription of the handlers. It stays green after the real `Append`
  is deleted.
- **UI automation** would genuinely cover the sites, and is brittle, slow, needs a
  new dependency, and is the over-building this capability was chosen to avoid.

What *would* help, and is **not built**: a verifier that mechanically checks a
trail file — twelve fields per row, header exactly once, `event` within the known
set, transfer rows paired by `operationId`, every number reparsing, and the row
count. That turns §7.1's eyeball step into one command with an exit code. It still
would not prove a site fires; only execution does that.

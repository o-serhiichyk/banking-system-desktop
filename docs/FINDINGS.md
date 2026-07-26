# FINDINGS — code smells and engineering risks

**Status: current.** This file is the authoritative statement of *what is true
now*. [`ANALYSIS_LOG.md`](ANALYSIS_LOG.md) is the append-only audit trail —
authoritative for *what happened when*, including superseded positions and who
argued which point. Where the two differ in content, this file wins; where they
differ on chronology, the log wins.

Every claim carries a `file:line`. Paths are relative to `BMS WinForm/`.
Findings marked **[E]** were confirmed by executing the application and diffing
the `.txt` datastore against a snapshot; **[S]** rest on reading only.

- 50 findings (51 entries — S19 is split into two limbs for citation)
- Two independent detection passes: the Phase-1 analysis and a blind Phase-3b
  pass that was forbidden to read the log. They converged on all 18 registered
  risks and all four audit findings.
- The ranked five below survived a different-model adversarial pass that
  changed the order and replaced two entries.

**A third evidence source now exists.** Validating the Task-4 audit trail
(`specs/AUDIT_TRAIL.md` §7) meant driving all seven instrumented operations
through the UI, which produced evidence about the *findings* as a side effect —
one new finding (**S50**), and a materially stronger case for **S14**. Where a
row of `auditTrail.csv` is the evidence, the citation says so. Detail and verbatim
rows are in `ANALYSIS_LOG.md`.

*Nothing below has been re-ranked on this evidence.* The strongest new fact — that
S14 makes the application unstartable — bears directly on the ranking and is
flagged where it lands, but the order is a human judgment and is left as it was.

---

## The five most critical, ranked by business risk

**Rubric:** likelihood × magnitude × irreversibility, where *magnitude* means
**damage to the bank**, not money lost. A ledger's exactness is a categorical
property, not a quantity to minimise, so a money-lost test mis-ranks systemic
defects.

**Structure:** ranks 1–2 are concrete failures; ranks 3–5 are multipliers on
them. A multiplier's magnitude is conditional on the thing it multiplies
occurring, which is why the concrete failures rank above them.

> It is broken now (1). It is open now (2). You cannot tell what happened (3).
> You cannot prove it right (4). And you cannot safely fix it (5).

### 1 · Backdoor plus authorization by username string

**Location** — `DL/MUserDL.cs:26-31`, `DL/MUserDL.cs:42-49`, `Form1.cs:66-73`
**Mode** — C · **[E]**, probes 5a and 6

`isAdmin` returns true for any user whose username is `Admin` or `admin`,
consulting neither the password nor any role, and a hardcoded `Admin`/`1234`
pair is accepted before the user store is read at all. Neither `MUser`
(`BL/MUser.cs:11-13`) nor `Admin` (`BL/Admin.cs:11-19`) carries a role field,
and `AddUser` does not reserve the name.

**Production impact.** Probe 6 registered an ordinary customer named `admin`
with a self-chosen password and reached the admin console with full operator
rights: every customer's PII, every password in cleartext, arbitrary balance
edits, account deletion. Escalation requires no secret. The compiled-in
credential cannot be rotated or revoked without a code change, and
`bin/Debug/BMS WinForm.exe` ships in the repository.

An escalated operator can rewrite any balance through `EditCustomer.cs:53`, a
path that writes **no history row at all** — so this corrupts the one record
that rank 2 leaves intact. C's damage strictly contains A's and adds unbounded
disclosure.

**Fix** — persist a role on the user record and have `isAdmin` read it; move the
admin account into `Users.txt` with a hashed password and delete the literal.

### 2 · Three disagreeing balance models, corrupted on the normal path

**Location** — `DL/CustomerDL.cs:198,217,234,248`, `WithDrawMoneyCus.cs:29`,
`TransactMoneyCus.cs:36-73`, `DL/CustomerDL.cs:253-259`
**Mode** — A · **[E]**, probes 1–4

Three balance definitions run simultaneously and all three are wrong. The
incremental in-memory one *adds* on withdrawal (`WithDrawMoneyCus.cs:29`). The
`calculate*Money` recompute mutates `AdminDL.Current.TotalMoney` while returning
a total, so it is not idempotent and re-applies the entire history on every
Balance Details load and Refresh click. The displayed one
(`DL/CustomerDL.cs:253-259`) ignores both `TotalMoney` and the initial deposit.
Transfers move no money at commit at all.

**Production impact.** Probe 2 observed **2000 on screen, 13000 in memory and
9000 on disk for one account at one instant**. Probe 1 moved a stored balance
**9000 → 11000 with a login and a logout and zero clicks**.

*Reconstructibility is degraded, not intact:* the history files are append-only
and record every amount to the unit, so a correct balance is recomputable
offline — but unlogged admin edits (rank 1) and the orphan rows of audit finding
A4 both break that. This ranks second rather than first because its damage is
bounded by rank 1's, not because it is cheap to fix.

**Fix** — one authoritative calculation; make `calculate*` pure; settle
transfers at commit.

### 3 · Falsifiable timestamps and unlogged privileged edits

**Location** — `DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`,
`TransactMoneyCus.cs:58`, `EditCustomer.cs:53-57`,
`ViewCustomer.cs:105-108,115`, `AdminWindow.cs:21-26`
**Mode** — G · **[S]**

Every money record takes its date from a form control's text, never the clock —
so the record is not merely absent but **falsifiable**: any transaction can be
backdated or forward-dated at will. Privileged actions leave nothing behind: an
operator's balance edit mutates the record and rewrites the file recording no
operator, no previous value and no time, and `AdminWindow` never receives the
identity of the operator who opened it.

**Production impact.** After any discrepancy, the first question — who changed
this, when, from what — has no answer, and the timestamps that do exist cannot
be trusted to order events. Rank 1's escalated operator is indistinguishable
from a legitimate one precisely because nothing is recorded.

**Fix** — system-generated timestamps; an append-only trail carrying operator,
before/after values and time.

### 4 · Money and identity are `double`

**Location** — `BL/Admin.cs:17-19`, `BL/Customer.cs:12-19`,
`AddUser.cs:70-77,82`, `TransactMoneyCus.cs:44`, `DL/CustomerDL.cs:139`,
`DepositMoneyCus.cs:57`
**Mode** — cross-cutting (A, D) · **[S]**

Balances and amounts are binary floating point, and account numbers — pure
identifiers — are `double` too, with three identity decisions made by `==`
(`AddUser.cs:77`, `TransactMoneyCus.cs:44`, `DL/CustomerDL.cs:139`).

**Production impact.** Real banking amounts are fractional; cents are ordinary
input, not an edge case. Nothing in the codebase constrains amounts to integers
or rounds them — no `decimal`, no `Math.Round`, no cast, no currency format
anywhere — and there are three unguarded `double.Parse` entry points for
user-typed numbers (`DepositMoneyCus.cs:57` with no guard at all;
`AddUser.cs:82`; `AddUser.cs:70-71` range-checked but **not** integer-checked, so
`123456.5` is a valid account number under a message reading "Account Number
Should be of 6 Character"). There is no barrier between a typed `0.10` and the
ledger.

Two placement arguments beyond the arithmetic:

- **Precondition for closing rank 2.** Even after the balance models are
  repaired, they can never be *proven* to agree — only observed to be close.
- **Multiplier on rank 3.** With no trustworthy record, a rounding artifact and
  a fraudulent adjustment are indistinguishable.

*The certainty lives in the money limb.* Cents are ordinary banking input, so
this is certain; a fractional account number is a typo, so the identity limb is
conditional. Lead with money.

**Fix** — `decimal` for monetary fields; `string` or `int` for account numbers.

### 5 · No automated tests, and no seam to add them

**Location** — `BMS WinForm.csproj` (single `WinExe`, no test project in the
solution), `DL/CustomerDL.cs:253-259`
**Mode** — cross-cutting · **[S]**

Every money rule lives inside a `Click` handler (S44) or a `static` method
reading global mutable state and opening files by relative path (S41).
`CustomerDL.totalMoney` is the only pure function on the money path, and it is
`static` on an `internal` class.

**Production impact — stated without hedging.** Ranks 2 and 4 are both one-line
arithmetic defects, both live today, and both are exactly what a single unit
test catches. The production impact of having no tests is not future breakage:
it is that two of the other four ranked entries **exist at all**, and that every
fix to them ships unverified.

Admitted under the assignment's *"code smells **or** engineering risks"*;
ordered fifth by business risk, as the same brief requires.

**Fix** — a test project, plus `InternalsVisibleTo` or a visibility change (the
DL types are `internal` — audit finding A5).

---

## Deliberately not in the five

Named as decisions, not omissions.

| Mode | Representative | Reason |
|---|---|---|
| **E** one character destroys an account | **S14** — `DL/AdminDL.cs:37-53,96` · **[E] probe 5b, 5/5 + trail run** | **The single best-evidenced finding in the exercise, and the one whose position is now least defensible — see the note below.** One comma typed into a name silently reassigns the account number and initial deposit, detaches the customer from their own record, **and makes the application unstartable** |
| **B** money silently disappears | S13, S28, S48 | Partly latent; durability is addressed by capability F14 |
| **F** no control on amounts or funds | S25 | Addressed by capability F1 |
| **D** operations against the wrong account | S24, S19b, S17, S18, **S50** | Mostly latent; S50 is live |
| **H** the app dies on data it ships | S26, S27, S30, **S14 (new)** | Was "availability only, and recoverable — lowest on two of the three axes". S14 arriving in this mode breaks that reasoning: see below |

*Cost of the ranking, stated plainly:* all executed probe evidence now sits in
ranks 1–2. Ranks 3–5 are static findings.

### ⚠ Open ranking question — S14, on evidence obtained after the ranking

The ranking above was fixed before the Task-4 trail was validated. That validation
produced a fact that was not available when S14 was placed below the line, and it
is recorded here rather than acted on, because the order is a human judgment.

**What was known:** a comma in a name shifts every following field, so the customer
is detached from their record. Scored as data-integrity damage to *one* account —
serious, but bounded, and mode **H** was discounted as "availability only, and
recoverable".

**What is now known:** the shifted row is read at launch, and it kills the process
before any window exists. `AdminDL.read_data:73` takes field 7 as the account number
and calls `double.Parse` on it; with a comma in the name that field holds a city. The
call sits in the `LogIn` **constructor** (`Form1.cs:18-26`), which has no `try`, and
the constructor runs in `Program.Main` *before* `Application.Run`, so WinForms' own
thread-exception handler is not yet installed. The exception is unhandled.

**Verified by controlled execution**, not inferred. The trail run had the application
write this row itself:

```
asd, das\,reg,qq,qq,Saving ,Attock,12123,222222,2000,2000,2000
```

Two launches of the *same* executable from the *same* directory, differing only in
`customers.txt`:

| Case | `customers.txt` | Result |
|---|---|---|
| **A** | corrupt row appended | **No visible window at all**, and a `WerFault` crash-reporter process present |
| **B** | control, unmodified | `Form1` window present, no `WerFault` |

**One customer name containing a comma renders the application permanently
unstartable for every user** — and the name only has to be typed once, into the
normal Add User form.

**Why this may outrank its current position, against the file's own rubric:**

- *Likelihood* — needs no attacker and no unusual path. A name like `Ali, Jr` typed
  into the normal Add User form does it. Every operator can trigger it accidentally.
- *Magnitude* — total loss of availability for all users, not damage to one account.
- *Irreversibility* — **the highest of the three, and the part previously
  underweighted.** "Recoverable" assumed someone can repair the file, but there is no
  backup, no export and no admin tooling (**S48**), the datastore lives under
  `bin/Debug`, and the app that would read it cannot start. Recovery means hand-editing
  a text file, which presumes knowing this defect exists.

**Counter-argument, for completeness:** ranks 1–2 are *silent* corruption, where the
bank cannot tell it has been harmed. S14-as-startup-denial is loud and instantly
obvious, and a defect you notice immediately is less dangerous than one you never
notice. That argument is strong enough that this is genuinely a question and not a
correction — which is exactly why it is left open.

*The mode **H** discount ("recoverable") should be revisited either way, since it is
the reasoning S14 now falsifies, independently of where S14 itself lands.*

---

## Failure-mode map

50 findings resolve to eight modes. Ranking modes and citing the best-evidenced
representative is tractable; sorting 50 flat items is not.

Two entries now appear in two modes each. **S14** was placed in **E** and its
startup-denial consequence puts it in **H** as well; **S50** spans **C** (a working
credential survives deletion) and **D** (the credential store disagrees with the
customer store). Overlap is recorded rather than forced, because collapsing either
to one mode hides half the consequence.

| Mode | Members |
|---|---|
| **A** balance wrong on the normal path | S1, S2, S4, S5, S6, S34 |
| **B** money silently disappears | S11, S12, S13, S28, S48 |
| **C** anyone becomes an operator; credentials readable | S20, S21, S22, S23, S47, S49, **S50** |
| **D** operations against the wrong account | S3, S17, S18, S19b, S24, **S50** |
| **E** one character destroys an account | S14, S15, S31, S16 (culture limb) |
| **F** no control on amounts or funds | S25 |
| **G** nothing recorded / record falsifiable | #16, S16 |
| **H** app dies on data it ships | S26, S27, S30, **S14** |
| *cross-cutting* | S19a, S46 |

---

## Full catalogue

Consequence column: **live** = misbehaves today on the normal path; **latent** =
requires an interruption, an attacker, or unusual input. **[X]** = the detecting
session flagged it as needing execution to confirm.

### correctness

| ID | Finding | Location | | |
|---|---|---|---|---|
| S1 | Withdrawal credits instead of debiting | `WithDrawMoneyCus.cs:29` | live | [E] |
| S2 | Available balance omits the initial deposit | `DL/CustomerDL.cs:253-259` | live | [E] |
| S3 | `calculateReceivedMoney` has no owner guard, unlike its three siblings | `DL/CustomerDL.cs:241-252` | live | [S] |
| S4 | Balance screen re-reads history without clearing — **per login, not per visit** (probe 2 disproved the per-visit claim) | `BalanceDetailsCus.cs:40-43` | live | [E] |
| S5 | `calculate*` mutate persisted state as a side effect; not idempotent | `DL/CustomerDL.cs:198,217,234,248` | live | [E] |
| S6 | Transfer never debits sender or credits recipient | `TransactMoneyCus.cs:36-73` | live | [E] |
| S7 | `editCustomerData` silently drops the City field | `DL/AdminDL.cs:120-137` | live | [S] |
| S8 | Initial-deposit minimum off by one (`<1999` vs a "2000" message) | `AddUser.cs:83-86` | live | [S] |
| S9 | Navigation handler misses one `Hide()`; stale panel remains visible | `CusForm.cs:96-107` | live | [X] |
| S10 | `RemoveAt` in a forward loop skips the shifted element | `DL/AdminDL.cs:110-119`, `DL/MUserDL.cs:98-108` | latent | [S] |

### data-integrity

| ID | Finding | Location | | |
|---|---|---|---|---|
| S11 | `storeCustomer` writes 10 fields, `storeAllCustomers` 9; reader takes field 9 | `DL/AdminDL.cs:96,105` | latent | [E] |
| S11 · compounding | The two defects stack: a create with a comma in the name wrote a **12**-field row, i.e. S14's shift *on top of* S11's extra field. Field-index recovery is then guesswork | `DL/AdminDL.cs:96` | live | [E] trail run |
| S12 | Transfer writes two files non-atomically | `TransactMoneyCus.cs:62-63` | latent | [E] window only |
| S13 | Balances persisted only on the Log Out button; window-close loses them | `CusForm.cs:135-142,218-222` | live | [S] |
| S14 | CSV fields never escaped — one comma shifts every following field, and the shifted row then **makes the app unstartable** (A/B controlled launch; see the ranking note above) | `DL/AdminDL.cs:96,105` +7 sites | live | [E] 5/5 + A/B |
| S15 | Feedback is a `RichTextBox` written as one CSV line; newlines split records | `GiveFeedback.cs:26-27`, `DL/CustomerDL.cs:58` | live | [S] |
| S16 | Dates are user-chosen localized display strings containing a comma | `DepositMoneyCus.cs:59` +6 sites | live | [S] |
| S17 | Delete leaves history and frees the account number for reissue | `ViewCustomer.cs:105-108` | latent | [S] |
| S18 | Edit skips the uniqueness checks Add enforces | `EditCustomer.cs:44-59` | latent | [S] |
| S19a | Money is not represented exactly | `BL/Admin.cs:17-19`, `BL/Customer.cs:12-19` | live | [S] |
| S19b | Account numbers are `double`; identity decided by `==` | `AddUser.cs:70-77`, `TransactMoneyCus.cs:44`, `DL/CustomerDL.cs:139` | latent | [S] |

### security

| ID | Finding | Location | | |
|---|---|---|---|---|
| S20 | Hardcoded `Admin`/`1234` backdoor, checked before the user store | `DL/MUserDL.cs:26-31` | live | [E] |
| S21 | Authorization by username string alone | `DL/MUserDL.cs:42-49` | live | [E] |
| S22 | Passwords stored, displayed and searched in plaintext | `DL/MUserDL.cs:84` +7 sites | live | [E] |
| S23 | Password entry unmasked outside the login screen | `AddUser.Designer.cs`, `EditCustomer.Designer.cs` | latent | [S] |
| S24 | `setCurrent` fails open — no match leaves the previous customer bound | `DL/AdminDL.cs:27-36` | latent | [E] path |
| S25 | No funds, amount or self-transfer validation | `TransactMoneyCus.cs:41-57`, `WithDrawMoneyCus.cs:26` | live | [E] |
| S47 | Credentials are in git history, not merely plaintext at rest — `Users.txt` entered at the original author's first commit `5518017` carrying `Haider,15`, `T,1`, `Saleem,123`. Hashing forward cannot remove them | `.gitignore:8-10` | live | [S] |
| S49 | Authentication is brute-forceable today: no attempt counter, no lockout, shipped passwords of 1–3 characters | `Form1.cs:60-87` | live | [S] |
| **S50** | **Editing a customer never updates the credential store, and a later delete then leaves a working login behind.** `EditCustomer.cs:56` passes `previous.UserName`/`previous.Password` *after* `AdminDL.editCustomerData` has mutated that very object (the grid's `DataBoundItem` **is** the list element), so `MUserDL.editCustomerData` looks up values that no longer exist and matches nothing. `deleteIdFromList` then fails the same way, and `storeAllIds` writes the stale row back. Since `checkuser` consults `UsersList` alone, **the deleted customer still authenticates** | `EditCustomer.cs:55-57`, `ViewCustomer.cs:106,108`, `DL/MUserDL.cs:26-31,98-120` | live | [E] trail run |

*S47 scope: sample data in an already-public upstream repository, so no live
customer secret is newly exposed. The finding is that committing the working
datastore makes credential leakage permanent and unfixable in place.*

### error-handling

| ID | Finding | Location | | |
|---|---|---|---|---|
| S26 | Unguarded file reads **and parses** in the login constructor — launching from the repo root throws before a window appears, and so does a single malformed row in `customers.txt`. The constructor runs before `Application.Run`, so WinForms' thread-exception handler is not installed and the throw is unhandled | `Form1.cs:18-26`, `DL/AdminDL.cs:55-92`, `Program.cs` | live | [E] A/B |
| S27 | `double.Parse` before any guard; the shipped root `depositHistory.txt` already contains a blank line — and `AdminDL.read_data:73,79,85` parses three fields the same way, on the file read at launch | `DL/CustomerDL.cs:138`, `DL/AdminDL.cs:73,79,85` | live | [E] trail run |
| S28 | Streams opened without `using`, closed only on success — an exception locks the file and the logout save then fails silently | 16 sites across all three DL classes | latent | [S] |
| S29 | Exceptions used for validation, then swallowed into one `MessageBox` | `AddUser.cs:55` +9 sites | live | [S] |
| S30 | Grid and data-bound reads with no error handling at all | `BalanceDetailsCus.cs:38-51` +6 | latent | [S] |

### duplication

| ID | Finding | Location | | |
|---|---|---|---|---|
| S31 | `parse_data` copy-pasted verbatim into all three DL classes | `DL/AdminDL.cs:37-53` +2 | latent | [E] |
| S32 | Five search controls with the same body; modal shown *inside* the match loop | `NameSearch.cs:41-59` +4 | latent | [S] |
| S33 | Five near-identical writers and five near-identical readers — the drift that hid S3 and S4 | `DL/CustomerDL.cs:25-170` | latent | [S] |
| S34 | Two divergent implementations of "what is this customer's balance" | `DL/CustomerDL.cs:253-259` vs `DL/AdminDL.cs:138-147` | live | [E] |
| S35 | `SearchCustomer` dropdown handler exists twice; both miss Hide calls | `SearchCustomer.cs:20-66,87-132` | live | [S] |

### dead-code

| ID | Finding | Location | | |
|---|---|---|---|---|
| S36 | Empty `storeAllDepositHistory` body in the persistence layer | `DL/CustomerDL.cs:63-66` | latent | [S] |
| S37 | Three unwired duplicate event handlers | `AddUser.cs:24-28` +2 | latent | [S] |
| S38 | Six scaffold forms never instantiated, compiled into the binary | `practice.cs` +5 | latent | [S] |
| S39 | Unused fields and redundant accessors; `Customer.ReceivedMoney` is never assigned and always returns 0 | `BL/Customer.cs:31` +4 | latent | [S] |
| S40 | Commented-out `read*` and `Clear()` calls — the exact operations whose absence causes S4 | `DL/CustomerDL.cs:191` +8 | latent | [S] |

### design

| ID | Finding | Location | | |
|---|---|---|---|---|
| S41 | All logic static, with static mutable session state | `DL/AdminDL.cs:13-17` +2 | latent | [E] |
| S42 | Seven filenames as bare relative literals in 16 places; two divergent data sets exist in the repo | 16 sites | live | [S] |
| S43 | `Customer` overloads distinguished only by parameter order | `BL/Customer.cs:33-62` | latent | [S] |
| S44 | Screens call persistence directly; no service layer, anemic `BL/` | `TransactMoneyCus.cs:41-63` +4 | latent | [S] |
| S45 | Unguarded cast of the grid's bound item before the column check | `ViewCustomer.cs:102-103` | latent | [X] |

### operations / testability

| ID | Finding | Location | | |
|---|---|---|---|---|
| S46 | No test project and no seam to add one | `BMS WinForm.csproj` | latent | [S] |
| S48 | No backup or disaster recovery for the datastore as a whole — the bank's database lives in a build-output directory | `DL/AdminDL.cs:100-109` | latent | [S] |

---

*How these findings were detected and ranked — the two detection passes, the
adversarial challenge to the ranking, and attribution — is chronology, and lives
in [`ANALYSIS_LOG.md`](ANALYSIS_LOG.md).*

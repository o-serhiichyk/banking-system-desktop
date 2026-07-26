# CAPABILITIES — missing and incomplete functionality

**Status: current.** This file is the authoritative statement of *what is true
now*. [`ANALYSIS_LOG.md`](ANALYSIS_LOG.md) is the append-only audit trail —
authoritative for *what happened when*. Where the two differ in content, this
file wins; where they differ on chronology, the log wins.

Every claim carries a `file:line`. Paths are relative to `BMS WinForm/`.
Cross-references to defects (`S…`) point at [`FINDINGS.md`](FINDINGS.md).

## Kind flag

The assignment asks for capabilities *"missing **or incomplete**"*, so all three
kinds below are admissible. The flag is recorded because the **mix** matters: a
slate made mostly of `[FIX]` items restates the defect catalogue instead of
answering the question. Classified by the **capability**, not the size of the
code change.

| Flag | Test |
|---|---|
| **[NEW]** | The capability does not exist in any form |
| **[INCOMPLETE]** | Exists, but only on some paths or in rudimentary form |
| **[FIX]** | Exists everywhere it should, but produces wrong results |

---

# The slate — 10 capabilities

Pruned from 25 candidates. Composition: **9 [NEW] · 1 [INCOMPLETE] · 0 [FIX]**.

| # | Capability | Kind | Complexity | Addresses | Coverage |
|---|---|---|---|---|---|
| 1 | Pre-transaction validation guard | [INCOMPLETE] | Medium | S25 | **Partial** — gated on rank 2 |
| 2 | Account status and non-destructive closure | [NEW] | Complex | S17, A4, **S50** | **Prevents recurrence** |
| 3 | Credential protection at rest and on screen | [NEW] | Medium | S22, S23, **S50** | **Full** (S47 unreachable) |
| 4 | Explicit role attribute | [NEW] | Medium | **rank 1** | **Partial** — one limb of two |
| 5 | **Append-only audit trail** ✅ **built — see `specs/AUDIT_TRAIL.md`** | [NEW] | Medium | **rank 3** · +10 findings | **Partial** mitigation (1 limb of 2) · **broad detection** |
| 6 | Atomic, recoverable file writes with backup | [NEW] | Medium | S12, S28, S48 | **Partial** on all three |
| 7 | Transaction receipt and account statement | [NEW] | Medium | rank 2 | **Detects only** |
| 8 | Product differentiation by account type | [NEW] | Complex | — | ⚠ **worsens** rank 4 |
| 9 | Transaction and balance limits | [NEW] | Medium | rank 1, S25 | **Bounds damage** |
| 10 | Multiple accounts per customer | [NEW] | Complex | — | — |

### Coverage is graded, not binary

Most of these capabilities reduce or bound a risk without closing it, and a
claim of "mitigates X" is the first thing a reviewer tests — so the column above
grades rather than asserts.

**Mitigation and detection are different axes**, and the column above tracks
both because they diverge sharply for one entry. A capability can close a defect
(mitigation) or make it *visible* without closing it (detection) — capability 7
is detection-only for rank 2, and **capability 5 is the extreme case: partial on
the one risk it mitigates, and the widest-reaching item on the slate by
detection.** Reading the mitigation figure alone would rank it last; the decision
section below explains why that reading is wrong.

What each one **leaves behind**:

**1 · Validation guard → S25.** The guard is only as good as the balance it
checks against, and rank 2 means three balances disagree. *Residual: an
"insufficient funds" check built on a corrupted balance is not a funds check.
This capability is gated on rank 2 being fixed first.*

**2 · Account closure → S17, A4, S50.** Soft-close plus a no-reissue rule stops new
orphans and stops a reissued number inheriting a stranger's transfers. **It also
closes S50's most serious limb**: the deleted customer whose credential survives in
`Users.txt` and still authenticates. A soft close is a single status flag on one
record, so there is no second store to fall out of step with the first — the whole
class of defect disappears rather than being patched. *Residual: the orphan rows
already in the shipped `withDrawHistory.txt` (`Ali`, `Karim`, `Ghulam`) are not
repaired by it. Prevention, not remediation.*

**3 · Credential protection → S22, S23, S50.** Both limbs of S22 (at rest, on
screen) and S23 are genuinely closed. **S50 raises this capability's complexity and
was not accounted for when it was scoped.** S50 shows the two-store design is
already broken in practice — `EditCustomer.cs:73` looks up credentials by
username+password *after* those values have been overwritten in the shared object,
so an edit never reaches `Users.txt` and a later delete leaves a working login. This
capability makes the password a **hash**, and the identity lookups at
`DL/AdminDL.cs:27-36` and `DL/MUserDL.cs:109-120` key on username+password — so it
has to touch the very code path S50 lives in. *Consequence: the implementation
cannot avoid deciding whether to fix S50 at the same time, and hashing on top of the
existing lookup would preserve the desync while making it harder to spot. Complexity
stays Medium, but "plus a one-time file migration" now reads as optimistic.*
*Residual: **S47 is unreachable by any capability** — the credentials are already in
git history from the original author's first commit, and hashing forward cannot
remove them. Only a history rewrite would, which is unavailable without destroying
the fork's attribution.*

**4 · Role attribute → rank 1.** Rank 1 has two limbs in two different methods.
The role attribute replaces `isAdmin`'s username check (`DL/MUserDL.cs:42-49`) —
that limb closes. **The hardcoded backdoor is in `checkuser`
(`DL/MUserDL.cs:26-31`) and is untouched by it.** `checkuser` returns the
caller's own object (`:30`), which carries no persisted role, so after this
change the backdoor no longer grants admin — but it still *authenticates*, and
`Form1.cs:82` then calls `AdminDL.setCurrent`, which fails open (S24) and leaves
the previous customer bound. *Residual: rank 1 closes only if the
implementation also deletes the literal at `:28` and makes `setCurrent` fail
closed. Scoped as "add a role field", it converts a privilege escalation into a
session-confusion bug.*

**5 · Audit trail → rank 3.** Rank 3 also has two limbs. Unlogged privileged
edits: closed — that is precisely what the trail records. **Falsifiable
timestamps: not closed.** The trail carries its own system-generated times, but
`depositHistory.txt`, `withDrawHistory.txt`, `transactHistory.txt` and
`sendMoneyPath.txt` still take their dates from `DateTimePicker.Text`
(`DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`, `TransactMoneyCus.cs:58`).
*Residual: the customer-facing ledger stays backdatable; you gain a **more**
trustworthy parallel record, not a trustworthy primary one. The limb that closes it
is the **rejected** candidate "system-generated transaction timestamps", pruned as
`[FIX]` — see Rejected candidates below.*

**⚠ Now built, so this entry is a forecast that can be checked against a result.**
`specs/AUDIT_TRAIL.md` is authoritative for what exists; where it is narrower than
the claims here, its §4 states the delta rather than letting this file's coverage be
read into the build. Three corrections that belong here rather than only there:

- **"Trustworthy" was too strong, and the spec's §5.3 now says so.** What the
  trail's timestamps actually improve on is narrow — they are not
  operator-supplied, so a transaction cannot be backdated from a picker. They carry
  no UTC offset, resolve to one second, and come from a clock any operator who can
  edit the data files can also change. *Harder to falsify than the ledger beside
  it; not authoritative.*
- **"+10 findings" is detection reach, and the build reaches fewer.** The `calculate*`
  recompute (S3, S4, S5) changes a balance with **no file write**, so no
  instrumented site sees it; rank 2 is detected at the logout persist only. Spec §4.
- **It found a finding nobody had.** Validating it produced **S50** and upgraded
  **S14** to a startup-denial defect (`FINDINGS.md` probe 7) — value this entry did
  not predict, and the strongest evidence for the "cross-cutting, not
  single-finding" argument below. Worth noting *why*: the value came from
  **executing** the seven capture sites, not from the trail's own rows.

**This is the entry where the mitigation figure misleads.** Partial on rank 3 is
the whole of its *mitigation* story and none of its value: with before/after
values recorded, it is the detection layer for roughly ten findings across five
failure modes — several times the reach of anything else on the slate. Full
table in the decision section below.

**6 · Atomic writes → S12, S28, S48.** Partial on each. *S12: write-temp-replace
makes each file atomic, but a transfer writes two files — per-file atomicity
does not make the pair atomic, and an ordering/recovery decision is still
required. S28: a shared write helper fixes the writers; the finding also covers
every **reader** (16 sites), which a write helper does not reach. S48: one
retained generation is a rollback, not disaster recovery — same disk, same
directory, and the data still lives under `bin/`.*

**7 · Receipt and statement → rank 2.** Does not fix any balance. *It makes the
discrepancy visible to the customer, which is detection, not mitigation — worth
stating as such rather than claiming credit for the fix.*

**8 · Product differentiation → ⚠ worsens rank 4.** Interest accrual produces
fractional amounts by construction, which turns the `double` exactness problem
from latent-in-the-demo-data into live. *This capability must not ship before
rank 4 is fixed. It is the clearest case in the slate of a feature whose
prerequisite is a defect repair.*

**9 · Limits → rank 1, S25.** Mitigates nothing outright, but caps the blast
radius of both: an escalated operator and an unvalidated withdrawal are both
survivable when a per-transaction ceiling exists. *Recorded as bounding rather
than mitigating; a daily cap additionally needs trustworthy dates, so that limb
is gated on rank 3.*

**10 · Multiple accounts.** No ranked risk. Relieves capability 2's problem that
closing an account currently deletes the person.

---

## 1 · Pre-transaction validation guard (funds, amount, self-transfer) — [INCOMPLETE]

**Missing.** No operation validates anything about the money. Withdraw parses an
amount and applies it with no balance reference anywhere in the handler
(`WithDrawMoneyCus.cs:22-53`). Transfer validates only that the destination
account exists (`TransactMoneyCus.cs:41-53`) — no funds check, no amount > 0, no
sender ≠ recipient. Deposit accepts any parseable double
(`DepositMoneyCus.cs:57`). Rudimentary validation *does* exist elsewhere (an
opening-deposit minimum at `AddUser.cs:83`), which is why this is `[INCOMPLETE]`
rather than `[NEW]`.

**Business value.** The difference between a ledger and a text file. Probe 3
accepted a 999,999 withdrawal against a 9,000 account; probe 4 accepted a
transfer to the sender's own account. Both were recorded as completed
transactions the bank has no basis to reverse.

**Complexity — Medium.** The guards are a few lines in three handlers, but
"insufficient funds" has no unambiguous answer today: three disagreeing balances
exist (findings rank 2), so this forces a decision on which is authoritative.

**Blast radius.** `WithDrawMoneyCus.cs`, `TransactMoneyCus.cs`,
`DepositMoneyCus.cs`, a new validator in `BL/`; the threshold at `AddUser.cs:83`
belongs in the same helper.

*Overlap disclosed:* this is the capability form of defect **S25**. Kept despite
the overlap because a banking system with no funds check is the first capability
a reviewer looks for.

## 2 · Account status and non-destructive closure — [NEW]

**Missing.** `Admin` has nine fields and none is a status (`BL/Admin.cs:11-19`).
The only way to end a relationship is a hard delete — removed from both
in-memory lists (`DL/AdminDL.cs:110-119`, `DL/MUserDL.cs:98-108`) with both
files rewritten (`ViewCustomer.cs:105-108`). Nothing removes or annotates the
customer's history rows, and nothing prevents the account number being reissued
(`AddUser.cs:75-81` checks live customers only).

**Business value.** A closed account's history is exactly what a bank must
retain and exactly what this deletes the anchor for; the orphan rows already in
`withDrawHistory.txt` are what that looks like afterwards. A frozen or dormant
state is also the normal response to suspected fraud, which the app cannot
express at all.

**Complexity — Complex.** `customers.txt` is positional and read by field index,
so a new field changes the format for every reader and writer, and every list
consumer (login, search, grid, bank total `DL/AdminDL.cs:138-147`) must learn to
exclude closed accounts.

**Blast radius.** `BL/Admin.cs`, `DL/AdminDL.cs`, `ViewCustomer.cs`,
`AddUser.cs`, `Form1.cs`, the five `*Search.cs` controls, existing data files.

## 3 · Credential protection at rest and on screen — [NEW]

**Missing.** One capability, two exposure surfaces — merged deliberately, since
treating them separately would ship a slate where hashing "fixes" a secret that
remains fully legible on the operator's screen.

*At rest:* passwords written verbatim to `Users.txt` and `customers.txt`
(`DL/MUserDL.cs:84`, `DL/AdminDL.cs:96,105`) and compared by string equality
(`DL/MUserDL.cs:34`, `DL/AdminDL.cs:31`). *On screen:* the admin grid
auto-generates a `Password` column from the bound `Admin` object
(`ViewCustomer.cs:34`, `BL/Admin.cs:23`); the value is rendered into plain labels
on the customer's own profile (`CustomerHome.cs:29`) and in search results
(`SearchResult.cs:28`); and the Add/Edit password boxes have no
`UseSystemPasswordChar` (`AddUser.Designer.cs:146-152`,
`EditCustomer.Designer.cs:107-112`) — only the login box does
(`Form1.Designer.cs:320`).

**Business value.** Anyone with read access to `bin/Debug/` — including anyone
who clones the repository — has every customer's live password, and every
password is legible on the operator's screen at once (observed in probe 6: `15`,
`1`, `123`, `12`, `a`, `zzz`). See also **S47**: the credentials are already in
git history, so hashing forward does not remove them.

**Complexity — Medium.** Hashing is small, but the password is also a *match
key*: `setCurrent` (`DL/AdminDL.cs:27-36`) and `MUserDL.editCustomerData`
(`DL/MUserDL.cs:109-120`) identify records by username+password, so those
lookups change too, plus a one-time file migration.

**Blast radius.** `DL/MUserDL.cs`, `DL/AdminDL.cs`, `Form1.cs:60-92`,
`AddUser.cs`, `EditCustomer.cs`, `ViewCustomer.cs` + designer,
`CustomerHome.cs`, `SearchResult.cs`, existing data files.

## 4 · Explicit role attribute, replacing username-string authorisation — [NEW]

**Missing.** Authorisation is one string comparison on the username, consulting
neither password nor role: `isAdmin` returns true for any user called `Admin` or
`admin` (`DL/MUserDL.cs:42-49`), and a hardcoded credential pair is accepted
before the user store is consulted (`DL/MUserDL.cs:28`). Neither `MUser`
(`BL/MUser.cs:12-13`) nor `Admin` (`BL/Admin.cs:11-19`) carries a role.

**Business value.** Probe 6 created an ordinary customer named `admin` and landed
in the admin console with full operator rights — all PII, all plaintext
passwords, arbitrary balance edits, deletion. Escalation requires no secret, and
the built-in pair cannot be rotated without a code change while the prebuilt
`.exe` ships in the repository.

**Complexity — Medium.** The routing decision lives in one place
(`Form1.cs:73`); the work is a persisted role plus file migration, plus deciding
what the first operator account is once the hardcoded pair is gone.

**Blast radius.** `BL/MUser.cs`, `BL/Admin.cs`, `DL/MUserDL.cs`,
`DL/AdminDL.cs`, `Form1.cs`, `AddUser.cs`, existing data files.

**→ Mitigates the top-ranked risk.** One of two candidates satisfying selection
criterion (iv).

## 5 · Append-only audit trail for money and privileged operations — [NEW]

**Missing.** No logging of any kind exists. Money operations report outcomes only
through a modal (`DepositMoneyCus.cs:74,79`, `WithDrawMoneyCus.cs:46,51`,
`TransactMoneyCus.cs:81,88`). Privileged actions leave nothing behind: a balance
edit mutates the record and rewrites the file (`EditCustomer.cs:69-74`,
`ViewCustomer.cs:125`) and a delete rewrites both files
(`ViewCustomer.cs:105-108`) — recording no operator, no previous value, no time.
`AdminWindow` never receives the identity of the operator who opened it
(`AdminWindow.cs:21-26`).

**Business value.** The system cannot answer "who changed this balance, when,
and from what" — the first question after a discrepancy. An escalated operator
is indistinguishable from a legitimate one precisely because nothing is
recorded.

**Complexity — Medium.** An append-only writer mirrors the existing `store*`
methods, but there is no operator identity to log on the admin side, so it has
to be threaded from login into `AdminWindow` and its child controls.

**Blast radius.** New writer in `DL/`, `Form1.cs`, `AdminWindow.cs`,
`ViewCustomer.cs`, `EditCustomer.cs`, `AddUser.cs`, the three money handlers —
plus the two paths that change a balance without a money handler being involved:
the logout persist (`CusForm.cs:138`) and the `calculate*` recompute
(`DL/CustomerDL.cs:198,217,234,248`), the latter mutating `TotalMoney` with no
file write at all.

⚠ **Interacts with S22/S23.** Recording before/after of a customer record means
recording the password field, and an append-only file accumulates every password
a customer has ever held. The capability must redact it; without redaction it
worsens the credential exposure that capability 3 exists to close.

**→ Mitigates ranked risk 3**, and specifically its *falsifiable* limb: a
server-generated, append-only trail addresses user-supplied timestamps, not just
the missing record. The second of two candidates satisfying criterion (iv).

## 6 · Atomic, recoverable file writes with backup — [NEW]

**Missing.** Full rewrites open the target in truncate mode and write from memory
(`DL/AdminDL.cs:100-109`, `DL/MUserDL.cs:88-97`), so a mid-write failure leaves a
truncated file and no copy of what was there. Multi-file operations are
sequences of independent writes with no coordination: create (`AddUser.cs:91,93`),
transfer (`TransactMoneyCus.cs:75-78`), delete (`ViewCustomer.cs:107-108`). No
file is opened with a share mode or lock, and no backup is taken anywhere.

**Business value.** `customers.txt` is the only record that a customer exists,
and the path that rewrites it in place runs on every logout. A
write-temp-then-replace helper plus one retained generation turns a crash from
data loss into a recoverable event. See also **S48** — there is no recovery for
the datastore being lost outright, and it currently lives in a build-output
directory.

**Complexity — Medium.** One shared helper covers it, but every writer in all
three DL classes must adopt it, and cross-file operations still need an ordering
decision.

**Blast radius.** All three DL classes; callers only if the signature differs.

## 7 · Transaction receipt and account statement — [NEW]

**Missing.** A completed transaction produces only a modal
(`DepositMoneyCus.cs:74`, `WithDrawMoneyCus.cs:46`, `TransactMoneyCus.cs:81`) —
no reference number, no resulting balance, nothing the customer keeps.
`Customer` has no identifier field (`BL/Customer.cs:11-20`), so a transaction
cannot be referred to at all. History screens bind raw lists with no totals, no
date range and no running balance (`DepositHistory.cs:36` +3), and deposits,
withdrawals, transfers and receipts are four separate screens never combined
chronologically. Nothing exports or prints.

**Business value.** A customer disputing a transaction has no reference to quote
and no combined statement to point at; an operator has no way to produce one.
Also the cheapest way to make the balance discrepancies visible to the people
affected by them.

**Complexity — Medium.** A combined chronological query is straightforward and
CSV export needs no new dependency; a per-transaction reference number means
adding a field to the history formats, touching their readers and writers.

**Blast radius.** The three money handlers, the four history screens,
`BL/Customer.cs`, `DL/CustomerDL.cs`, one new export writer.

## 8 · Product differentiation by account type (interest accrual) — [NEW]

**Missing.** `AccountType` is captured (`AddUser.cs:59`), persisted
(`DL/AdminDL.cs:96,105`), read back (`:64`), displayed (`CustomerHome.cs:32`,
`SearchResult.cs:31`) and editable (`DL/AdminDL.cs:130`) — and **branches no
behaviour anywhere in the solution.** The offered values are `"Saving "` and
`"Current"` (`AddUser.Designer.cs:172-173`; note the trailing space), and
`interest` appears nowhere in the source. A savings account and a current
account are the same product with a different label.

**Business value.** Account type is the primary axis a retail bank prices on —
interest, overdraft, fees, minimum balance. The data model already anticipates
the distinction; none of it was built, so the field is decorative and the bank
cannot offer two products.

**Complexity — Complex.** Accrual needs a posting mechanism, a rate
configuration and reliable dates — and dates are currently user-supplied display
strings (S16). There is also no scheduler or batch entry point in an application
that only runs when someone opens it.

**Blast radius.** `BL/Admin.cs`, a new rate/product type, `DL/CustomerDL.cs`,
`BalanceDetailsCus.cs`, `CustomerHome.cs`; the trailing space in `"Saving "`
must be handled or cleaned in existing data.

*Dependency:* makes finding **S19a** live rather than theoretical — interest
produces fractional amounts by construction.

## 9 · Transaction and balance limits — [NEW]

**Missing.** No limit of any kind — `limit` appears nowhere in the source. No
per-transaction ceiling, no daily aggregate cap, no minimum balance, no velocity
check. The only threshold in the codebase is the account-opening deposit
minimum (`AddUser.cs:83`, itself off by one — S8).

**Business value.** Limits are the standard containment for both error and
fraud, and they are what makes an escalated account survivable rather than
unbounded. They bound the damage from the missing funds check even before a
correct balance exists.

**Complexity — Medium.** The check is small, but a *daily* cap requires
aggregating today's transactions — which needs trustworthy dates and currently
cannot be computed, since dates are user-chosen strings with no time component.
Per-account limits also need a new persisted field.

**Blast radius.** `BL/Admin.cs`, `DL/AdminDL.cs` record format, the three money
handlers, a validator shared with capability 1.

## 10 · Multiple accounts per customer — [NEW]

**Missing.** Identity and account are one record. `Admin` carries a single
`accountNumber` alongside the person's name, city and phone
(`BL/Admin.cs:11-19`); login binds one such record to the session
(`DL/AdminDL.cs:27-36`); and every lookup keys on that single number
(`TransactMoneyCus.cs:42-49`, `DL/CustomerDL.cs:139`,
`AccountNumberSearch.cs:24-33`). A customer wanting a second account must be
registered as a second person.

**Business value.** Holding a current and a savings account is the ordinary
retail case — and the case capability 8 implies the app intended to serve.
Conflating customer with account also means closing an account deletes the
person (capability 2).

**Complexity — Complex.** A domain-model split: customer and account become
separate records with a relationship, and every screen, search, reader and
writer currently assumes the 1:1 shape.

**Blast radius.** `BL/Admin.cs`, all three DL classes, `Form1.cs`, `CusForm.cs`,
`AdminWindow.cs`, all five `*Search.cs`, every data file.

---

# Selection criteria for the Task-4 implementation

(i) genuinely Medium · (ii) small blast radius · (iii) unit-testable ·
(iv) mitigates a risk in the ranked five.

Criterion (iv) narrows the slate to **capability 4** (role attribute → rank 1)
and **capability 5** (audit trail → rank 3). Criteria (i)–(iii) decide between
them.

**Both are partial, and the scoping decision is part of the choice** — see
Coverage above. Neither closes its ranked risk as stated; each closes one limb
of two.

| | Capability 4 · role attribute | Capability 5 · audit trail |
|---|---|---|
| Addresses | rank 1 (highest) | rank 3 |
| Limb closed | authorization by username (`isAdmin`) | unlogged privileged edits |
| Limb left open | the backdoor in `checkuser:28`, plus `setCurrent` failing open | falsifiable timestamps on the four history files |
| Closing the residual | delete one literal + make `setCurrent` return `bool` — both small, both in scope if chosen deliberately | requires changing the history file formats and their readers — **not** small |
| Record format | **changes** — needs data migration | additive — new file only |
| Existing paths touched | login, add-user, both DL classes | three money handlers, four admin paths |
| Testable without UI | role resolution is a pure function | writer is pure; capture is not |
| Risk if scoped naively | escalation becomes session confusion — arguably worse | a trustworthy parallel record beside an untrustworthy ledger |

The asymmetry that matters: **capability 4's residual is cheap to close and
capability 5's is not.** A role attribute *plus* deleting the literal *plus* a
fail-closed `setCurrent` is still a Medium-sized change and would genuinely shut
rank 1. Making the primary ledger's timestamps trustworthy means touching four
file formats and every reader that parses them — which is why the standalone
"system-generated timestamps" candidate was pruned in the first place.

---

# ✅ Decision — capability 5, the append-only audit trail

## Why it wins

**It is the only candidate that changes no behaviour.** Every other Medium
alters what the application does; this one only observes. Against a brief graded
on *"safe, incremental improvements"* and *"working, maintainable code, not a
complete production-ready implementation"*, that is decisive. The worst failure
mode of a bad audit trail is a wrong log file; the worst failure mode of a bad
validation guard is refusing legitimate withdrawals.

**Its value is cross-cutting, not single-finding.** With before/after values, an
actor and a system timestamp on every money operation and every privileged edit,
these become detectable or reconstructible:

| Finding | What the trail supplies |
|---|---|
| rank 1 | The escalated operator's actions become visible — currently indistinguishable from legitimate ones |
| rank 2 | Probe 1's 9000 → 11000 **with zero clicks** becomes visible; the three-way divergence becomes measurable |
| S4, S5 | The repeated recompute is recorded rather than silent |
| S13 | Deposit recorded, no persist — the discrepancy becomes legible |
| S28 | The write and its outcome are recorded |
| S24 | The account actually operated on is recorded |
| S3 | Cross-account contamination becomes visible |
| S14 | Pre-corruption values retained, so repair becomes possible |
| S17 | Operations tied to a customer at a point in time |
| S48 | A partial replay source where none exists today |

Roughly ten findings across five failure modes. No other capability on the slate
reaches more than two or three. These failures also **cascade** — rank 1 corrupts
the history that rank 2 leaves intact; S28 silently eats the durability S13 needs
— and without a trail none of the chains are reconstructible.

**Criterion (iv) is satisfied without being demoted.** It addresses ranked risk 3
directly, so Tasks 2, 3 and 4 remain one argument.

## Implementation scope

Not described here. This file states *which capability and why*; the contract for
the Task-4 build — instrumented call sites, record format, behaviour guarantees,
limits, and how the basic version's coverage is narrower than the capability's —
is [`specs/AUDIT_TRAIL.md`](specs/AUDIT_TRAIL.md).

**Status: built.** Seven instrumented sites, three new `internal` types in `DL/`, 43
NUnit tests, and all seven sites validated by manual execution. The prediction this
section rests on — *"the only candidate that changes no behaviour"* — held: the
change is additive, no existing statement was altered or removed, and the writer
swallows every exception precisely so that instrumentation cannot alter a call
site's behaviour.

**One prediction did not hold.** "Complexity — Medium … an append-only writer
mirrors the existing `store*` methods" understated two things the build had to
absorb: `double.ToString` is not round-trip exact on .NET Framework (so the writer
needed a shortest-exact number formatter, not a `ToString` call), and the seven
`Click` handlers had no seam, so **§7.3 records that deleting any one `Append` call
leaves all 43 tests green**. Medium was still the right call; "mirrors the existing
`store*` methods" was not.

## Why not the alternatives

**Capability 1 · validation guard** — the strongest rival, rejected on a
regression path rather than on value. It must validate against *some* balance,
and three disagree. Validating against `CustomerDL.totalMoney()` triggers **S2**:
the function never adds `IntialDeposit`, so a customer with a 2000 opening
deposit and no history computes to 0 available and **every withdrawal is
refused**. A guard that bricks normal use for exactly the customers in the sample
data is worse than no guard. Safe variants exist
(`AdminDL.Current.TotalMoney`, or `totalMoney(...) + IntialDeposit`), but the
capability carries a live way to leave the application worse and capability 5
carries none. It also addresses only S25, which is below the ranked line, so
choosing it would have required demoting criterion (iv).

**Capability 4 · role attribute** — addresses the top-ranked risk but closes only
one of its two limbs, and scoped naively as "add a role field" it converts a
privilege escalation into a session-confusion bug via the fail-open `setCurrent`
(see Coverage). Doing it properly means the role *plus* deleting the literal at
`DL/MUserDL.cs:28` *plus* making `setCurrent` fail closed — the top of Medium,
against a 75-minute budget that includes NUnit setup on .NET Framework. Also
requires a positional record-format change and a data migration.

## Forward note

The same writer is where per-transaction limits (capability 9) and
system-generated ledger timestamps would later hook in. Stated to show the design
has a next step; **not built**, because over-building this task is the
scope-discipline trap the brief is testing.

---

# Rejected candidates

25 were generated; 10 were kept. The full text of all 25 with evidence is in
[`ANALYSIS_LOG.md`](ANALYSIS_LOG.md); summarised here so the prune is auditable.

**Rejected as `[FIX]` — already covered in [`FINDINGS.md`](FINDINGS.md), and
including them would have made this list a restatement of the defect
catalogue:** one authoritative balance calculation (S5, S34) · transfer
settlement at commit (S6) · delimiter-safe record encoding (S14, S31) ·
system-generated timestamps (S16).

**Rejected as `[INCOMPLETE]` and lower value:** shared record validation on
create *and* edit (S18) · session lifecycle established on login and cleared on
logout (S24).

**Rejected as `[NEW]` but lower priority:** load-time schema validation and
quarantine of bad rows · customer self-service credential change · transaction
reversal and correcting entries · saved beneficiaries · idle timeout and
failed-login lockout (retained as defect **S49**, which records it as a present
risk rather than a gap) · fees and charges · explicit currency · customer
notification of account activity.

*Note on the last:* notification is the one candidate that genuinely reaches
outside the application — email or SMS means an external dependency, stored
credentials and unretryable failures. Flagged as a real production gap but
poorly matched to this architecture.

*How the 25 candidates were generated, in which order, and the anchoring effect
that shaped them — is chronology, and lives in
[`ANALYSIS_LOG.md`](ANALYSIS_LOG.md).*

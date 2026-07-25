# ANALYSIS_LOG.md — reverse-engineering running log

Curated log of the AI-assisted analysis of this banking-system-desktop app.
It is **not** a raw transcript: each phase records the method, the model, and
the findings (with `file:line` citations), so later phases and the final report
draw from here instead of re-deriving. Line references are to the non-generated
sources under `BMS WinForm/` unless stated otherwise.

Guardrail in force: **AI generates candidates; the human validates, ranks, and
selects.** In this log, risks are *detected only* — not ranked. Feature choice
(Task 2) and the ranked five smells (Task 3) are deliberately absent; they are
later, human-led phases.

---

## Session 1 · Phase 1 — Reverse engineering, single structured shot

- **Date:** 2026-07-24
- **Model / effort:** Opus 4.8, default effort.
- **Method:** one context, one pass. Read the entire logic surface directly —
  entry point, all three BL classes, all three DL classes, and the forms that
  drive the four core flows (login, deposit, withdraw, transfer, customer
  create) plus the admin edit/delete path. Every claim below is anchored to a
  line I read, not inferred. Claims that can only be *confirmed* by running the
  app are tagged **[exec]** and queued for Phase 2 (validate-by-execution).
- **Scope read:** `Program.cs`, `BL/{Admin,Customer,MUser}.cs`,
  `DL/{AdminDL,CustomerDL,MUserDL}.cs`, `Form1.cs` (class `LogIn`),
  `DepositMoneyCus.cs`, `WithDrawMoneyCus.cs`, `TransactMoneyCus.cs` (class `r`),
  `AddUser.cs`, `CusForm.cs` (class `CustomerWindowPAge`), `EditCustomer.cs`,
  `ViewCustomer.cs`. ~690 LOC of BL/DL logic; the rest is forms + designer code.

---

### 1. High-level architecture

- **Single-process WinForms desktop app**, .NET Framework 4.7.2. Entry point is
  `Program.Main`, which runs one form — `LogIn` (`Program.cs:19`).
- **Loosely layered into `BL` (business/entity) and `DL` (data) namespaces**
  (`BL/Admin.cs:7`, `DL/AdminDL.cs:9`), but the layering is **thin and leaky**:
  the DL classes hold the persistence code *and* the balance-arithmetic
  ("business") logic (e.g. `CustomerDL.calculateWithDrawMoney`
  `DL/CustomerDL.cs:207`), while genuine business rules live in the **UI event
  handlers** — e.g. the transfer's recipient-exists check is in the form, not
  the DL (`TransactMoneyCus.cs:41-53`). There is no service/domain layer between
  forms and files.
- **Persistence is flat comma-delimited text files**, not a database. All I/O is
  hand-rolled `StreamReader`/`StreamWriter` in the DL classes (e.g.
  `DL/AdminDL.cs:55-109`).
- **State is global/static and in-memory during a session.** The domain lists
  and the "logged-in user" are `static` fields:
  `AdminDL.customersList` + `AdminDL.current` (`DL/AdminDL.cs:13-14`),
  `MUserDL.usersList` (`DL/MUserDL.cs:14`), and five lists in
  `CustomerDL` (`DL/CustomerDL.cs:13-17`). Files are loaded into these lists at
  login and written back out on specific events.
- **Two roles, one entity type.** "Admin" (the bank operator) and "Customer" are
  distinguished only at login (`MUserDL.isAdmin` `DL/MUserDL.cs:42`). Confusingly,
  **customers are modelled by the `Admin` class** (`BL/Admin.cs:9`); `AdminDL`
  is really the customer store (`AdminDL.CustomersList` holds customers).

Optional report diagram: `Forms (event handlers) → DL static lists → *.txt`.

---

### 2. Main components

| Layer | Type | Role | Cite |
|---|---|---|---|
| Entry | `Program` | Launches `LogIn` | `Program.cs:9-21` |
| BL (entity) | `Admin` | Customer/operator record (name, credentials, account #, balances). **`public`** | `BL/Admin.cs:9-64` |
| BL (entity) | `Customer` | Per-transaction record (deposit/withdraw/transfer/feedback rows). Default `internal` | `BL/Customer.cs:9-69` |
| BL (entity) | `MUser` | Login credential pair (userName, password) | `BL/MUser.cs:9-23` |
| DL (store) | `AdminDL` | Customer list load/store to `customers.txt`; hand-rolled CSV parse; balance total | `DL/AdminDL.cs:11-148` |
| DL (store) | `CustomerDL` | History load/store (deposit/withdraw/transact/send/feedback); balance recompute | `DL/CustomerDL.cs:11-262` |
| DL (store) | `MUserDL` | User list load/store to `Users.txt`; login check; admin check | `DL/MUserDL.cs:12-121` |
| UI | `LogIn` (`Form1.cs`) | Login; seeds static lists in its ctor | `Form1.cs:16-88` |
| UI | `CustomerWindowPAge` (`CusForm.cs`) | Customer shell; hosts deposit/withdraw/transfer/etc. controls; flushes balances on logout | `CusForm.cs:16-229` |
| UI | `AddUser` | Create-customer form | `AddUser.cs:15-194` |
| UI | `ViewCustomer` / `EditCustomer` | Admin list, edit, delete | `ViewCustomer.cs:15-126`, `EditCustomer.cs:16-66` |
| UI | `DepositMoneyCus` / `WithDrawMoneyCus` / `r` (`TransactMoneyCus.cs`) | The three money operations | see §3 |

**Naming/identity smells noted (detect only):** the transfer UserControl is
literally named **`r`** (`TransactMoneyCus.cs:16`); customers are stored as
**`Admin`** objects (`BL/Admin.cs:9`); a stray `practice` form exists
(`practice.cs:13`). The `parse_data` CSV parser is **copy-pasted verbatim into
all three DL classes** (`DL/AdminDL.cs:37-53`, `DL/CustomerDL.cs:171-187`,
`DL/MUserDL.cs:50-66`).

---

### 3. Business flows (UI → BL → DL → `.txt`)

**Login** — `Form1.cs:60-87`
- Ctor seeds state: clears the static lists and reads `customers.txt` + `Users.txt`
  into them on *every* `LogIn` construction (`Form1.cs:20-23`).
- `checkuser` authenticates (`MUserDL.checkuser` `DL/MUserDL.cs:26-41`). A
  **hardcoded backdoor** matches user `Admin`/`admin` + password `1234`
  *before* consulting the user list (`DL/MUserDL.cs:28`). Otherwise it linear-scans
  `usersList` for an exact userName+password match (`DL/MUserDL.cs:32-38`).
- If `isAdmin` (`DL/MUserDL.cs:42-49`) → open `AdminWindow` (`Form1.cs:70-72`);
  else set the current customer via `AdminDL.setCurrent` (matches userName+password
  across the customer list, `DL/AdminDL.cs:27-36`) and open `CustomerWindowPAge`
  (`Form1.cs:77-79`).
- **Credentials compared as plaintext**; passwords are stored plaintext in both
  files (see §4).

**Deposit** — `DepositMoneyCus.btnConfirm_Click` `DepositMoneyCus.cs:53-71`
- Parse amount (`double.Parse`, `DepositMoneyCus.cs:57`) → **mutate in-memory
  balance** `AdminDL.Current.TotalMoney += money` (`DepositMoneyCus.cs:60`) →
  build a `Customer` (`DepositMoneyCus.cs:61`) → **append** one line to
  `depositHistory.txt` via `CustomerDL.storeDepositHistory`
  (`DepositMoneyCus.cs:62`, writer `DL/CustomerDL.cs:25-31`, row =
  `userName,depositMoney,date`) → add to in-memory `DepositList`
  (`DepositMoneyCus.cs:63`).
- The balance change is **in-memory only** here; `customers.txt` is not touched
  until logout (see §4). No amount>0 validation beyond parse.

**Withdraw** — `WithDrawMoneyCus.btnComfirm_Click` `WithDrawMoneyCus.cs:22-40`
- Same shape, but the in-memory update is
  `A.TotalMoney = A.TotalMoney + money` (`WithDrawMoneyCus.cs:29`) — **adds on a
  withdrawal.** This contradicts the recompute path, which *subtracts*
  (`CustomerDL.calculateWithDrawMoney` `DL/CustomerDL.cs:217`). Two balance
  models disagree on sign. **[exec]**
- **No overdraft/insufficient-funds check** — nothing compares `money` to the
  balance (`WithDrawMoneyCus.cs:22-40`).
- Appends `userName,withDrawMoney,date` to `withDrawHistory.txt`
  (`WithDrawMoneyCus.cs:31`, writer `DL/CustomerDL.cs:32-38`).

**Transfer / send money** — `r.btnComfirm_Click` `TransactMoneyCus.cs:36-73`
- Validates the **recipient account exists** by linear-scanning the customer
  list (`TransactMoneyCus.cs:41-53`); throws if not found.
- Writes **two files, sequentially, non-atomically**:
  `transactHistory.txt` (sender's outgoing row `userName,recipientAcct,purpose,money,date`
  via `storeTransactHistory` `TransactMoneyCus.cs:62`, `DL/CustomerDL.cs:39-45`)
  **and** `sendMoneyPath.txt` (recipient's incoming row
  `recipientAcct,senderAcct,purpose,money,date` via `storeSendMoney`
  `TransactMoneyCus.cs:63`, `DL/CustomerDL.cs:46-53`). If the second write
  throws, the first is already committed — **no rollback.** **[exec]**
- **No sender-side balance change and no funds check.** Unlike deposit/withdraw,
  the transfer does **not** touch `AdminDL.Current.TotalMoney`; the debit only
  appears later via `calculateTransactMoney` (`DL/CustomerDL.cs:224-240`). No
  check that the sender can afford it, that amount>0, or that sender≠recipient
  (`TransactMoneyCus.cs:36-73`). The recipient sees it via `readSendHistory`
  matching their account number (`DL/CustomerDL.cs:132-154`).

**Create customer** — `AddUser.btnAdd_Click_1` `AddUser.cs:44-101`
- Validations: username unique (`AddUser.cs:51-57`), phone unique
  (`AddUser.cs:62-68`), account number in `100000..999999` (`AddUser.cs:71-74`),
  account number unique (`AddUser.cs:75-81`), initial deposit `>= 1999`
  (`AddUser.cs:83-86` — note the guard is `< 1999` while the message says
  "2000 or More": off-by-one/message mismatch).
- Persists to **two files, non-atomically**:
  `AdminDL.storeCustomer` **appends** to `customers.txt` (`AddUser.cs:91`) and
  `MUserDL.storeUsersID` **appends** to `Users.txt` (`AddUser.cs:93`). A failure
  between them leaves an orphan (customer with no login, or vice-versa). **[exec]**
- Dead handler: the original `btnAdd_Click` is empty (`AddUser.cs:24-28`); the
  live logic is in `btnAdd_Click_1`.

**Admin edit / delete** — `ViewCustomer.usersGV_CellContentClick_1` `ViewCustomer.cs:100-119`
- Grid bound directly to the live `AdminDL.CustomersList` (`ViewCustomer.cs:34`).
- **Delete:** `deleteCustomerFromList` + `deleteIdFromList`, then rewrite
  `customers.txt` and `Users.txt` in full (`ViewCustomer.cs:105-108`).
- **Edit:** open `EditCustomer` dialog (`ViewCustomer.cs:113-114`); on save it
  updates the in-memory record and rewrites `Users.txt`
  (`EditCustomer.cs:55-57`); back in `ViewCustomer` the full `customers.txt` is
  rewritten (`ViewCustomer.cs:115`). **This is the path that changed
  `customers.txt` in the pre-existing smoke run** (an admin edited a balance):
  `EditCustomer` exposes `TotalMoney` as a free-text field
  (`EditCustomer.cs:34,53`), so an operator can set any balance directly.

---

### 4. Storage & persistence

- **Format:** plain-text, comma-delimited, one record per line, **no header, no
  quoting, no escaping.** Files live in the working directory (`bin/Debug/`) and
  are referenced by **relative path** everywhere (`"customers.txt"`,
  `"Users.txt"`, `"depositHistory.txt"`, etc. — e.g. `Form1.cs:22-23`,
  `AddUser.cs:17-18`, `DepositMoneyCus.cs:17`). Working-directory–dependent.
- **Schemas (as read by the parsers):**
  - `customers.txt` (read `DL/AdminDL.cs:55-92`): 9 fields —
    `name,userName,password,accountType,city,phoneNumber,accountNumber,intialDeposit,totalMoney`.
    Sample row `Ali,Haider,15,Current,Multan,2131,123456,2000,9000`.
  - `Users.txt` (read `DL/MUserDL.cs:67-80`): 2 fields — `userName,password`.
  - History files: `depositHistory.txt`=`user,amount,date`
    (`DL/CustomerDL.cs:28`), `withDrawHistory.txt`=`user,amount,date`
    (`:35`), `transactHistory.txt`=`user,recipientAcct,purpose,amount,date`
    (`:42`), `sendMoneyPath.txt`=`recipientAcct,senderAcct,purpose,amount,date`
    (`:50`), `feedBacks.txt`=`user,feedback` (`:58`).
- **Hand-rolled CSV parser** `parse_data(record, field)` (1-indexed) walks the
  string char-by-char (`DL/AdminDL.cs:37-53` and two duplicates). It **splits on
  every comma with no escaping**, so any value containing a comma (address,
  feedback text, purpose) corrupts every downstream field. It also does not trim
  whitespace.
- **Schema inconsistency in the writers (detect only):** `storeCustomer`
  writes **10 fields** — it emits `IntialDeposit` twice
  (`DL/AdminDL.cs:96`) — while `storeAllCustomers` writes **9 fields**
  (`DL/AdminDL.cs:105`) and `read_data` expects 9 (`DL/AdminDL.cs:61-85`). So
  freshly appended rows and rewritten rows have different column counts for the
  same file. On the create path this is currently masked because
  `totalMoney == intialDeposit` at creation (`AddUser.cs:87`), but it is a
  latent corruption. **[exec]**
- **Atomicity / durability:**
  - No operation is atomic across files. Create writes two files
    (`AddUser.cs:91,93`); transfer writes two files (`TransactMoneyCus.cs:62-63`);
    delete/edit rewrite two files (`ViewCustomer.cs:107-108`). A crash or
    exception between writes leaves inconsistent state. **[exec]**
  - **Balance durability depends on which control the customer closes with.**
    Deposit/withdraw mutate only the in-memory `AdminDL.Current.TotalMoney`; the
    value is flushed to `customers.txt` **only** by the **Log Out** button
    (`AdminDL.storeAllCustomers`, `CusForm.cs:135-142`). Closing via the window
    "X" calls `Application.Exit()` with no flush (`CusForm.cs:218-222`), so those
    balance changes are lost. **[exec]**
  - **Rewrites are non-transactional:** `storeAllCustomers`/`storeAllIds` open
    the file in truncate mode and rewrite from the list (`DL/AdminDL.cs:100-109`,
    `DL/MUserDL.cs:88-97`); a failure mid-write truncates the file.
- **Concurrency:** none. Static shared lists (`DL/AdminDL.cs:13-14`) and files
  opened without sharing/locking; the design assumes a single user, single
  process, single instance. **[exec]**
- **Dual source of truth for credentials:** a password is stored in both
  `customers.txt` (field 3) and `Users.txt` (field 2); edits/creates/deletes
  must update both via separate calls (`EditCustomer.cs:55-57`, `AddUser.cs:91,93`,
  `ViewCustomer.cs:105-108`) — they can diverge.
- **Numeric types:** money **and account numbers** are `double`
  (`BL/Admin.cs:17-19`) and parsed with culture-default `double.Parse`
  (`DL/AdminDL.cs:73,79,85`) — no `InvariantCulture`, no `TryParse`. Float money
  invites rounding error; culture-sensitive parse invites locale breakage.

---

### 5. Technical risks — **DETECTED, NOT RANKED**

> Detection only. Business-risk ranking and the "five most critical" selection
> are the human's job (Task 3, later phase). Listed in reading order, not
> priority order.

1. **Hardcoded admin backdoor** `Admin`/`admin` + `1234` (`DL/MUserDL.cs:28`).
2. **Privilege escalation by username:** `isAdmin` returns true for *any* user
   whose name is `Admin`/`admin`, checking only the string, not a role or the
   password (`DL/MUserDL.cs:42-49`). A self-registered user named `admin` would
   be treated as admin. **[exec]**
3. **Plaintext passwords** at rest in `customers.txt` and `Users.txt` (§4).
4. **Withdrawal balance sign bug:** in-memory withdraw *adds* to the balance
   (`WithDrawMoneyCus.cs:29`) while the recompute path subtracts
   (`DL/CustomerDL.cs:217`). **[exec]**
5. **No overdraft / funds check** on withdraw (`WithDrawMoneyCus.cs:22-40`) or
   transfer (`TransactMoneyCus.cs:36-73`); no amount>0 or sender≠recipient guard.
6. **No cross-file atomicity** on create, transfer, edit, delete (§4). **[exec]**
7. **Balance loss on window-close** vs Log Out (`CusForm.cs:218-222` vs
   `:135-142`). **[exec]**
8. **CSV parser cannot handle delimiters/quotes in data**; three duplicated
   copies (`DL/AdminDL.cs:37-53` +2). Any comma in an address/feedback corrupts
   the row.
9. **Writer schema mismatch** (10-field `storeCustomer` vs 9-field read/rewrite,
   `DL/AdminDL.cs:96` vs `:105`/`:61-85`). **[exec]**
10. **Fragile parsing:** culture-default `double.Parse` with no `TryParse`/
    try-catch in `read_data` (`DL/AdminDL.cs:73,79,85`); a malformed line throws
    inside the `LogIn` ctor (`Form1.cs:22`) and can crash startup. **[exec]**
11. **`double` for money and account numbers** (`BL/Admin.cs:17-19`) — precision
    risk; account numbers are identifiers, not quantities.
12. **Side-effecting "calculate" methods:** `calculate*Money` mutate
    `AdminDL.Current.TotalMoney` while returning a total
    (`DL/CustomerDL.cs:198,217,234,248`), so calling them more than once
    double-counts. **[exec]**
13. **Global mutable static state** (`DL/AdminDL.cs:13-14`, `DL/CustomerDL.cs:13-17`)
    — untestable, not thread-safe, single-session assumption.
14. **Remove-while-iterating** with a forward index in `deleteCustomerFromList`
    /`deleteIdFromList` (`DL/AdminDL.cs:110-119`, `DL/MUserDL.cs:98-108`) — skips
    the following element when duplicates exist. **[exec]**
15. **Dead / stub / practice code:** empty `storeAllDepositHistory`
    (`DL/CustomerDL.cs:63-66`), commented-out reads throughout `CustomerDL`
    (e.g. `:191,210,227,244`), empty dead handler (`AddUser.cs:24-28`),
    `practice` form (`practice.cs`).
16. **No logging, no audit trail, no error handling beyond `MessageBox`** on the
    money paths (`DepositMoneyCus.cs:67-70`, etc.).

---

### 6. Areas needing further investigation

- **Exact balance-display logic:** which of the two competing balance models
  (incremental in-memory vs `calculate*Money` recompute) actually drives the
  Balance Details screen? Requires reading `BalanceDetailsCus.cs`/`CustomerHome.cs`
  and running the app. Determines whether the withdraw sign bug is user-visible. **[exec]**
- **Admin-side money total:** `AdminDL.calculate()` sums `TotalMoney`
  (`DL/AdminDL.cs:138-147`) for a "total money in bank" view — confirm against
  `TotalMoneyInBank.cs` and whether it double-counts after `calculate*Money`
  side effects.
- **Search subsystem:** many `*Search.cs` controls exist
  (`NameSearch`, `CitySearch`, `AccountNumberSearch`, …) — not yet read; confirm
  they are read-only queries over the static lists.
- **Feedback flow:** `GiveFeedback` → `storeFeedBack` (`DL/CustomerDL.cs:54-61`)
  and `AdminFeedback` viewer — not traced; low priority.
- **Whether `intialDeposit` is ever used post-creation** or is dead once
  `totalMoney` diverges.
- **Control-probe candidate for Phase 2:** there is *no* audit-log or
  role/permissions subsystem in the code — a good deliberate hallucination probe
  ("explain the audit log / RBAC") for the Opus session.

---

### Claims flagged for Phase 2 (validation queue)

Priority spot-checks, strongest evidence first:
1. **[exec]** Run a withdrawal; Log Out; diff `customers.txt` vs the Phase-0.5
   snapshot — does the stored balance go **up** (confirming the sign bug at
   `WithDrawMoneyCus.cs:29`)?
2. **[exec]** Close the customer window with the "X" after a deposit — is the
   balance change lost (no `storeAllCustomers`)?
3. **[exec]** Create a customer, then inspect `customers.txt` for a 10-field row
   (`storeCustomer` `DL/AdminDL.cs:96`).
4. **[exec]** Perform a transfer; confirm both `transactHistory.txt` and
   `sendMoneyPath.txt` gained a row, and that neither party's `customers.txt`
   balance changed at transfer time.
5. Spot-check the backdoor (`DL/MUserDL.cs:28`) and the username-only `isAdmin`
   (`DL/MUserDL.cs:42-49`) by login.
6. **Deliberate control probe:** ask this Opus session to explain the app's
   "audit log" / "role-based access control" — record whether it fabricates one
   (none exists in the code).

*(Phase 1·rev next: hand this log + the code to Fable as a different-model
auditor to flag any unsupported, miscited, or fabricated claim before Phase 2.)*

---

## Session 1-rev · Phase 1·rev — Adversarial audit of the Phase-1 analysis

- **Date:** 2026-07-24
- **Model:** Fable 5 (different-model auditor), fresh session.
- **Method:** re-read every cited source file in full (`Program.cs`, all three BL,
  all three DL, `Form1.cs`, `DepositMoneyCus.cs`, `WithDrawMoneyCus.cs`,
  `TransactMoneyCus.cs`, `AddUser.cs`, `CusForm.cs`, `EditCustomer.cs`,
  `ViewCustomer.cs`, plus `practice.cs` and — beyond Phase-1 scope —
  `BalanceDetailsCus.cs` and grep of the history/feedback forms) and checked
  every `file:line` citation and behavioral claim in §§1–6 against the code and
  the shipped `bin/Debug/*.txt` data.

### Verdict

**No fabricated claims. No miscitations.** Every `file:line` reference in
§§1–6 resolves to the code it describes (~60 distinct citations checked,
including all 16 risk items, all five flow traces, both writer line numbers in
the 10-vs-9-field claim, and the sample `customers.txt` row, which matches the
shipped file byte-for-byte). The two headline bugs are confirmed as cited: the
withdraw sign bug (`WithDrawMoneyCus.cs:29` adds; `DL/CustomerDL.cs:217`
subtracts) and the admin backdoor (`DL/MUserDL.cs:28`).

### Overstatements / wording corrections (minor)

1. **Risk 10 slightly overstated.** `read_data` guards *empty* numeric fields
   with `"0"` defaults (`DL/AdminDL.cs:68-71,75-78,81-84`), so blank fields and
   trailing empty lines do **not** throw; only non-numeric garbage does. The
   crash-on-startup claim stands, but is narrower than written.
2. **Risk 2 wording:** "a self-registered user named `admin`" — there is no
   self-registration; only an operator can create accounts (`AddUser` is an
   admin-side control). The escalation is real (`AddUser.cs:51-57` checks
   uniqueness only against the *customer* list, so an operator can create a
   customer named `admin`, who then gets `AdminWindow` via `DL/MUserDL.cs:44`),
   but the actor is an operator, not an end user.
3. **§4 history schemas are 4/6-field in practice, not 3/5.** The date strings
   written include a comma (`"Thursday, 2 June 2022"` — see shipped
   `depositHistory.txt`), and the readers compensate by concatenating the extra
   field back onto the date (`DL/CustomerDL.cs:78,99,122,145`). So the code
   already contains a hand-rolled workaround for exactly one comma-in-data —
   strengthening risk 8, but the schema table as written undercounts the
   columns on disk.

### Missed findings (material — add to the record)

1. **The Balance Details screen actively corrupts the balance** — this
   *answers §6's first open question*. `BalanceDetailsCus_Load` re-reads all
   four history files **without clearing the lists** (`BalanceDetailsCus.cs:40-43`;
   contrast `DepositHistory.cs:33-34`, which clears first), so entries
   duplicate on every visit; it then calls `btnRefresh_Click`
   (`BalanceDetailsCus.cs:50,73-86`), which runs all four side-effecting
   `calculate*Money` methods — mutating `AdminDL.Current.TotalMoney` — on
   every load *and every Refresh click*. Risk 12's "calling them more than
   once" is not hypothetical: the UI does it, and Log Out then flushes the
   corrupted value to `customers.txt`. **[exec — promote to top of the Phase-2
   queue: open Balance Details, click Refresh twice, Log Out, diff.]**
2. **A third balance model.** The displayed "Available" balance is
   `deposit + received − transact − withdraw` (`CustomerDL.totalMoney`
   `DL/CustomerDL.cs:253-259`, called at `BalanceDetailsCus.cs:84`) — it
   ignores both `TotalMoney` and the initial deposit. So the app has three
   disagreeing balance definitions: the incremental in-memory one, the
   `calculate*` side-effect one, and the displayed one.
3. **`EditCustomer` has no uniqueness validation** — unlike `AddUser`, an edit
   can assign an already-taken username, phone, or account number
   (`EditCustomer.cs:44-64` validates only the account-number range).
4. **Referential integrity is already broken in the shipped data:**
   `withDrawHistory.txt` contains users `Ali`/`Karim` that match no username
   in `customers.txt` — orphan rows the readers silently skip.
5. **Test-plan correction (affects Phase 4, contradicts PLAN §1):** the BL/DL
   types are **not** "all public". Only `Admin` is `public` (`BL/Admin.cs:9`);
   `Customer`, `MUser`, and all three DL classes are default-`internal`
   (`BL/Customer.cs:9`, `BL/MUser.cs:9`, `DL/AdminDL.cs:11`,
   `DL/CustomerDL.cs:11`, `DL/MUserDL.cs:12`). The NUnit project will need
   `InternalsVisibleTo` (or the types made public) — decide before Phase 4.
6. Minor corroboration: five history `.txt` files also sit at the **repo
   root**, evidencing the relative-path/working-directory fragility claimed in
   §4; and `bin/Debug/transactHistory.txt` + `sendMoneyPath.txt` are empty
   while the root copies are not.

### Effect on the Phase-2 queue

Keep items 1–6 as listed; **insert the Balance-Details double-count probe as
new priority 1** (it is the strongest executable demonstration that in-memory
balance ≠ stored balance ≠ displayed balance), and note for item 3 that a
freshly appended 10-field row is silently *repaired* to 9 fields by the first
full rewrite (Log Out / edit / delete), so the probe must inspect
`customers.txt` **before** any of those.

---

## Session 1-rev · Phase 1·rev — Builder's cross-check of the Fable audit (Opus)

- **Date:** 2026-07-24
- **Model:** Opus 4.8 — author of the Phase-1 analysis, re-verifying the Fable
  audit against source + shipped data *before* its findings feed Phase 2
  (closing the cross-model loop: Fable audited Opus; Opus now verifies Fable).
- **Verdict:** **the Fable audit holds.** I spot-checked its highest-consequence
  claims against the code and the `bin/Debug`/root `.txt` files; all resolve, and
  I found **no error in the auditor itself**. Adopting its corrections and
  additions into the Phase-2 queue.

**Independently confirmed by direct read:**
- **Balance-Details corruption** (audit finding 1): `BalanceDetailsCus_Load`
  re-reads all four histories with **no `.Clear()`** (`BalanceDetailsCus.cs:40-43`;
  contrast `DepositHistory.cs:33`, which clears first), then `:50` calls
  `btnRefresh_Click`, which runs the four side-effecting `calculate*Money()` and
  the third-model `totalMoney()` (`BalanceDetailsCus.cs:73-86`, total at `:84`) on
  **every load and every Refresh click**. Confirmed → **Phase-2 priority 1**.
- **Three balance models** (finding 2): the displayed "Available" uses
  `CustomerDL.totalMoney` (`DL/CustomerDL.cs:253-259`, called `BalanceDetailsCus.cs:84`),
  distinct from the incremental in-memory model and the `calculate*` side-effect
  model. Confirmed.
- **Types are NOT all public** (finding 5): only `Admin` is `public`
  (`BL/Admin.cs:9`); `Customer`, `MUser`, `AdminDL`, `CustomerDL`, `MUserDL` are
  default-`internal`. **Phase-4 action item:** the NUnit project needs
  `InternalsVisibleTo("<TestAssembly>")` (or promote the types) — this **corrects
  PLAN §1's "all public" note.** Confirmed.
- **Comma-in-date schema** (correction 3): confirmed on disk — e.g.
  `T,1221,Thursday, 2 June 2022` in `depositHistory.txt`; readers rebuild the date
  by concatenating fields 3+4 / 5+6 (`DL/CustomerDL.cs:78,99,122,145`). History
  rows are **4/6 fields on disk, not 3/5** as §4 stated. Strengthens risk 8.
- **Orphan data** (finding 4): `withDrawHistory.txt`/`depositHistory.txt` reference
  `Ali`, `Karim`, `Ghulam` — none are usernames in `customers.txt` (usernames
  there are `Haider, T, Saleem, Alian`); readers silently skip them. Confirmed.

**Builder's addition — dual data sets + a run-from-`bin/Debug` gate (sharpens §4):**
- There are **two divergent copies of the data**: the committed `bin/Debug/*.txt`
  sample data, and *stale* copies of the five history files at the **project
  root**. `bin/Debug/transactHistory.txt` and `sendMoneyPath.txt` are **0 bytes**;
  the root copies are not — and their contents differ (root `depositHistory.txt`
  has `Karim/Ghulam`; `bin/Debug` has `T`).
- **`customers.txt` and `Users.txt` exist only in `bin/Debug`** — not at the
  project root. `LogIn` reads them by relative path (`Form1.cs:22-23`), so
  launching with **CWD = project root throws `FileNotFoundException` in the ctor →
  the app crashes at startup.** It is only runnable with **CWD = `bin/Debug`**
  (where the committed `.exe` sets it). This is the concrete failure behind §4's
  "working-directory-dependent" claim, and a **Phase-2 gate: launch the committed
  exe and diff the `bin/Debug` copies** — the pristine `customers.txt` (restored
  to `…,9000`) is the correct baseline.

**Net:** Phase-1 analysis, the Fable audit, and this cross-check converge. The
Phase-2 validation queue stands as revised, with the Balance-Details double-count
as priority 1 and the run-from-`bin/Debug` gate noted up front.

---

## Session 1 · Phase 2 — Validation · **PART A: pre-registered predictions**

- **Date:** 2026-07-25
- **Owner:** human-led; AI derived the expected arithmetic from the source, the
  human executes the app and adjudicates each outcome.
- **Committed before the app was run.** This section is deliberately a separate
  commit from the results below: a prediction written after seeing the outcome
  is not evidence. The numbers here are computed *from the code*, so each probe
  is a pass/fail test, not a description of whatever happened.

### Baseline mechanism

No side-copy snapshot was needed: all seven `bin/Debug/*.txt` data files are
**tracked in git and clean at HEAD**, so the committed tree *is* the Phase-0.5
baseline. Each probe is therefore
`run app → git diff -- "BMS WinForm/bin/Debug/*.txt" → git checkout -- …` to
reset. `core.autocrlf=true`, so diffs show content changes only.

**Run gate (from Phase 1·rev):** the app must be launched with **CWD =
`BMS WinForm\bin\Debug`**; `customers.txt`/`Users.txt` exist only there and are
opened by relative path (`Form1.cs:22-23`).

### Baseline facts the predictions are computed from

Test account — `customers.txt` line 1:
`Ali,Haider,15,Current,Multan,2131,123456,2000,9000`
(user `Haider`, password `15`, account `123456`, initial deposit `2000`, **stored
balance `9000`**). Haider's history in the shipped data: **one** deposit row
(`Haider,2000,…`), **no** withdraw rows, and `transactHistory.txt` /
`sendMoneyPath.txt` are empty. So every `calculate*` contribution for Haider is
known exactly: deposit `2000`, withdraw `0`, transact `0`, received `0`.

### Mechanism under test (the claim being pre-registered)

`BalanceDetailsCus` is a designer-instantiated child UserControl of
`CustomerWindowPAge` and is **not** `Visible=false` in the designer
(`CusForm.Designer.cs:434-440`). In WinForms a visible child control is created —
and its `Load` fires — when the parent form is shown, *before* the parent's own
`Load` handler hides it (`CusForm.cs:144-153`). Therefore
`BalanceDetailsCus_Load` (`BalanceDetailsCus.cs:38-51`) is predicted to run
**once per login, at window construction, with no user interaction** — reading
the four history files and then calling `btnRefresh_Click` (`:50`), which runs
the four side-effecting `calculate*Money()` methods that mutate
`AdminDL.Current.TotalMoney` (`DL/CustomerDL.cs:198,217,234,248`).
`AdminDL.setCurrent` assigns a **reference into `customersList`**
(`DL/AdminDL.cs:33`), so that mutation is what Log Out flushes to disk
(`CusForm.cs:138` → `DL/AdminDL.cs:100-109`).

> ⚠️ **Recorded disagreement with the Phase-1·rev Fable audit.** The audit states
> the history lists "duplicate on **every visit**" to Balance Details. Reading
> the control lifecycle, that looks wrong: `btnBalanceDetails_Click` only calls
> `Show()`/`BringToFront()` (`CusForm.cs:109-120`), which does **not** re-fire
> `Load` on an already-created control. The duplication should occur **per
> login**, not per navigation. Probe 2 is designed to settle this by execution.
> Either the builder or the auditor is wrong here, and the app decides — this is
> the cross-model check paying out.

### Pre-registered predictions

**Risk IDs** in the first column refer to the numbered items in **§5 Technical
risks** above; `A1`–`A4` refer to the *Missed findings* added by the Phase-1·rev
Fable audit. Every probe is tied to a specific pre-existing claim — nothing here
is exploratory, and the coverage is countable (see *Coverage* below the table).

| # | Risks under test | Probe (all start from a clean baseline) | Predicted result | What it falsifies / confirms |
|---|---|---|---|---|
| **1** | **#12**, **A1**, #13 | Log in as `Haider`/`15`. **Touch nothing.** Click **Log Out**. | `customers.txt` line 1 changes `…,2000,`**`9000`** → `…,2000,`**`11000`** (+2000 = Haider's deposit history, added once by the auto-refresh). No other line changes; no history file changes. | Balance-Details auto-corruption at login. If the row is unchanged, my `Load`-at-construction reading is **wrong** and the corruption needs a navigation to trigger. |
| **2** | **A1** (mechanism), **A2**, **#13**, #12 | **One process, two logins.** (a) Log in, open **Balance Details**, record the six fields. (b) Navigate **Home** → back to **Balance Details**, record. (c) Click **Refresh** once, record. (d) **Log Out**. (e) Log in again as Haider, open **Balance Details**, record. (f) Log Out. | Displayed **Deposit** goes `2000` → `2000` → `2000` → **`4000`**; Available tracks it (`2000`→`4000`); Initial stays `2000`. Stored balance walks `9000` → 11000 (a) → 13000 (c) → written at (d) → 17000 (e) → **`17000`** on disk after (f). | Settles builder-vs-auditor by execution. Deposit stepping at (b) proves the auditor's **"every visit"**; stepping only at (e) proves **per login** (`Load` fires once per window instance; the static history lists are never cleared). Also exhibits the **third balance model** — the screen says `2000` while the stored balance is `13000`. |
| **3** *(amended)* | **#4**, **#5** | Log in. **Withdraw Money** → amount `500` → Confirm. **Then a second withdrawal of `999999`** → Confirm. → Log Out. | Both withdrawals **succeed with no warning**. `withDrawHistory.txt` gains `Haider,500,<date>` and `Haider,999999,<date>`. `customers.txt` = **`1002499`** (9000 + 2000 auto + 500 + 999999 — all **added, not subtracted**). A correct system would refuse the second and store `8500`. | The withdrawal sign bug (`WithDrawMoneyCus.cs:29` adds where `DL/CustomerDL.cs:217` subtracts) **and** the absent overdraft check (`WithDrawMoneyCus.cs:22-40` compares nothing to the balance), to the exact currency unit. |
| **4** | **#5**, **#6**, A2 | Log in. **Transact Money** → account `454545` (user `T`), any purpose, amount `300` → Confirm. Then retry with amount `999999` (exceeds balance) and again to **own** account `123456`. Log Out. | All three transfers **succeed** with no error. `transactHistory.txt` gains `Haider,454545,<purpose>,300,<date>`; `sendMoneyPath.txt` gains `454545,123456,<purpose>,300,<date>` (two separate non-atomic writes). `customers.txt` Haider = **`11000`** — *unchanged by the transfers*; T's row `191437` also unchanged. | No funds check, no amount validation, no sender≠recipient guard, **no debit at transfer time**, and the two-file non-atomic write — in one run. |
| **5a** | **#1**, **#3**, **#9**, #6 | Log in as **`Admin`/`1234`** (credentials appear nowhere in the data files). **Add Account**: Name `Test`, user `zz`, password `zz`, AccountType `Current`, City `Lahore`, phone `03001234567`, account `222222`, initial deposit `2000`. **Inspect `customers.txt` immediately, before any Log Out/edit/delete.** | Backdoor login succeeds → AdminWindow. Appended row has **10 fields** with the initial deposit twice: `Test,zz,zz,Current,Lahore,03001234567,222222,2000,2000,2000`, while every existing row has 9. `Users.txt` gains `zz,zz`. | The hardcoded backdoor (`DL/MUserDL.cs:28`) **and** the writer schema mismatch (`storeCustomer` `DL/AdminDL.cs:96` = 10 fields vs `storeAllCustomers` `:105` = 9). Must be read before a full rewrite silently repairs it to 9. |
| **5b** *(added)* | **#8**, #9 | Still in the same admin session, **Add Account** a second time with a **comma in the free-text Name**: Name `Doe, John`, user `yy`, password `yy`, AccountType `Current`, City `Lahore`, phone `03007654321`, account `333333`, initial deposit `2000`. **Capture `customers.txt`.** Then Log Out (admin logout does *not* rewrite the file) and log in as `yy`/`yy`. | The comma is written **raw, unquoted, unescaped**: `Doe, John,yy,yy,Current,Lahore,03007654321,333333,2000,2000,2000` — 11 comma-separated tokens. On the next read every field past the name **shifts by one**, so the record re-parses as accountType `yy`, city `Current`, phone `Lahore`, **account number `3007654321`** (their phone) and **initial deposit `333333`** (their account number). Login as `yy`/`yy` then **succeeds** (`Users.txt` is uncorrupted) but `setCurrent` cannot match the mangled username `" John"` (`DL/AdminDL.cs:27-36`), leaving `AdminDL.Current` as a blank `new Admin()` (`:14`, `BL/Admin.cs:43`) → **the customer window opens on an empty, zero-balance account.** | Risk #8 executed on data a *user can actually type*, not inferred from the date-field workaround. One comma in a name silently reassigns account number and deposit, then locks the customer out of their own record while still letting them log in. Also shows the corruption is **progressive**: the next full rewrite re-emits the already-shifted values. |
| **6** | **#2** | *(if time)* As admin, create a customer with username **`admin`**, password `zzz`. Log out, log in as `admin`/`zzz`. | Lands in **AdminWindow**, not the customer window. | Privilege escalation by username string (`DL/MUserDL.cs:42-49` checks the name only, never a role or the password). |
| **7** | **#7** | *(if time)* Log in. **Deposit** `1000` → Confirm → close with the window **"X"**, not Log Out. | `depositHistory.txt` gains `Haider,1000,<date>` but `customers.txt` stays at **`9000`** — the deposit is recorded in history and lost from the balance. | Durability depends on which control the user closes with (`CusForm.cs:218-222` `Application.Exit()` with no flush vs `:135-142`). |

### Amendments to this pre-registration

Made **after** the Part-A commit and **before** running the amended probes, so
the amendment itself is timestamped ahead of its own evidence. Both close
coverage gaps found when the risk IDs above were mapped:

- **Probe 3 amended** — a single `500` withdrawal proves the *sign bug* (#4) but
  not the *missing overdraft check* (#5); worse, the sign bug **masks** #5, since
  withdrawing increases the balance. A second withdrawal of `999999` against a
  9000 balance tests #5 directly and keeps the arithmetic exact.
- **Probe 5b added** — nothing in the original set exercised **#8** (the
  unescaped hand-rolled CSV parser). The free-text `Name` field is the one
  user-controlled value that reaches the writer unescaped, so a comma in a name
  demonstrates #8 on realistic input instead of on the internal date format.

### Coverage

Executed by these probes: risks **#1, #2, #3, #4, #5, #6\*, #7, #8, #9, #12,
#13** and Fable additions **A1, A2** — 11 of the 16 detected risks, plus 2 of 4
audit additions.

Deliberately **not** validated by execution, and reported as static findings
only: **#10** (culture-sensitive `double.Parse` — needs a locale change),
**#11** (`double` money precision), **#14** (remove-while-iterating — needs
constructed duplicate records), **#15** (dead code), **#16** (absence of
logging — nothing to run), **A3** (`EditCustomer` uniqueness) and **A4** (orphan
rows in shipped data — readable directly).

> \* **#6 is only partially executable and must not be overclaimed.** The probes
> demonstrate that create and transfer each perform **two separate sequential
> writes** with no transaction — i.e. that the failure *window* exists. They do
> **not** demonstrate data loss from a crash between the writes; that needs
> fault injection, which is out of scope here. The report states the window, not
> a forced failure.

**Process hygiene (or the arithmetic won't reproduce):** every probe except #2
runs in a **fresh process from a reset baseline** — close the app, restore the
data files, relaunch. `CustomerDL`'s five history lists are `static` and are
**never cleared** (`DL/CustomerDL.cs:13-17`), so state leaks across logins within
one process; probe #2 exploits exactly that, the others must avoid it.

Probes 1–5 are the priority set; 6–7 are cheap add-ons. Results, hit rate, and
the control probe are recorded in Part B below.

---

## Session 1 · Phase 2 — Validation · **PART B: results**

Each probe: run from a reset baseline, then
`git diff -- "BMS WinForm/bin/Debug/*.txt"`. Diffs are quoted verbatim.

### Probe 1 — zero-interaction balance corruption · **CONFIRMED, exact**

```diff
--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -1 +1 @@
-Ali,Haider,15,Current,Multan,2131,123456,2000,9000
+Ali,Haider,15,Current,Multan,2131,123456,2000,11000
```

Logged in as `Haider`, **clicked nothing**, clicked Log Out. The stored balance
moved **9000 → 11000** — the predicted +2000, matching Haider's single
`depositHistory.txt` row to the currency unit. One line changed; no other
customer row and no history file was touched, as predicted.

**What this establishes.** The `BalanceDetailsCus_Load` corruption needs **no
user interaction at all**: the control is a visible designer-instantiated child
of `CustomerWindowPAge` (`CusForm.Designer.cs:434-440`), so WinForms creates it
and fires its `Load` when the window is shown — *before* `CustomerWindowPAge_Load`
hides it (`CusForm.cs:144-153`). `Load` reads the four history files and calls
`btnRefresh_Click` (`BalanceDetailsCus.cs:50`), whose side-effecting
`calculateDepositMoney()` adds the whole deposit history to
`AdminDL.Current.TotalMoney` (`DL/CustomerDL.cs:198`) — a reference into
`customersList` (`DL/AdminDL.cs:33`) — which Log Out then flushes to disk
(`CusForm.cs:138`).

The business statement: **a customer who logs in and immediately logs out has
their balance silently rewritten**, by the sum of their own deposit history,
every session. It compounds — nothing is idempotent. This also explains the
shipped data: `T`'s balance of `191437` against a `12111` initial deposit is
consistent with repeated runs of exactly this defect, not with real activity.

### Probe 2 — trigger condition + third balance model · **CONFIRMED, exact**

```diff
--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -1 +1 @@
-Ali,Haider,15,Current,Multan,2131,123456,2000,9000
+Ali,Haider,15,Current,Multan,2131,123456,2000,17000
```

Observed on screen (screenshots retained):

| Step | Intial Deposit | Deposit Money | WithDraw | Transact | Received | **Available** |
|---|---|---|---|---|---|---|
| (a) first view | 2000 | **2000** | 0 | 0 | 0 | **2000** |
| (b) navigate away → back | 2000 | **2000** | 0 | 0 | 0 | **2000** |
| (c) after clicking Refresh | 2000 | **2000** | 0 | 0 | 0 | **2000** |
| (e) after Log Out → log in again | 2000 | **4000** | 0 | 0 | 0 | **4000** |

Every predicted value matched, including the final `17000` on disk. That figure
is self-checking: had the Refresh at (c) not been clicked the file would read
`15000`, so the disk value independently corroborates the click sequence.

**Result 1 — the Phase-1·rev audit was wrong on the trigger condition, and this
is the correction.** Fable stated the history lists duplicate on *"every visit"*
to Balance Details. They do not: (b) is a visit and the numbers did not move.
Duplication is **per login**. `btnBalanceDetails_Click` only calls
`Show()`/`BringToFront()` (`CusForm.cs:109-120`), which cannot re-fire `Load` on
an already-created control; the re-read at `BalanceDetailsCus.cs:40-43` runs once
per `CustomerWindowPAge` instance, and because the five history lists are
`static` and never cleared (`DL/CustomerDL.cs:13-17`), a second login in the same
process appends a second copy — hence Deposit `2000 → 4000`.

The honest framing for the report: **Fable found a material defect Opus missed,
and got its trigger condition wrong; execution adjudicated.** Both models erred
in different directions and neither could settle it — running the app did. This
is the clearest argument in the whole exercise for why cross-model review is a
*queue of things to verify*, not a source of truth.

**Result 2 — three disagreeing balances coexist at the same instant (A2,
confirmed).** At step (c), for one account, simultaneously:

| Where | Value | Source |
|---|---|---|
| On screen, "Available Balance" | **2000** | `CustomerDL.totalMoney(d,w,t,r)` — history-derived, ignores `TotalMoney` *and* the initial deposit (`DL/CustomerDL.cs:253-259`) |
| In memory, `Current.TotalMoney` | **13000** | incremental model, mutated by the `calculate*` side effects |
| On disk, `customers.txt` | **9000** | last flushed value |

A customer with 9000 in the file is shown 2000, and 17000 is eventually stored.
Note also that "Available Balance" **excludes the initial deposit entirely** — it
sums history only — so a customer who never transacts is shown a balance of 0
regardless of what they deposited at account opening.

### Probe 3 — withdrawal sign bug + overdraft · **behaviour confirmed; my predicted figure was WRONG**

```diff
--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -1 +1 @@
-Ali,Haider,15,Current,Multan,2131,123456,2000,9000
+Ali,Haider,15,Current,Multan,2131,123456,2000,1011499

--- a/BMS WinForm/bin/Debug/withDrawHistory.txt
+++ b/BMS WinForm/bin/Debug/withDrawHistory.txt
@@ -8,0 +9,2 @@
+Haider,500,Saturday, 25 July 2026
+Haider,999999,Saturday, 25 July 2026
```

**Scored honestly: 2 of 3 sub-predictions hit, 1 missed.**

| Sub-prediction | Outcome |
|---|---|
| Both withdrawals accepted with no warning or funds check | ✅ hit |
| `withDrawHistory.txt` gains `Haider,500,…` and `Haider,999999,…` | ✅ hit |
| `customers.txt` = `1002499` | ❌ **miss — actual `1011499`** |

**The miss was an arithmetic error in the prediction, not a wrong model of the
system.** The predicted *components* were all correct: the balance rose by
`2000` (auto-refresh) + `500` + `999999` = **1002499**, and `1011499 − 9000 =
1002499` exactly. The published figure stated that increase as though it were
the final value, silently dropping the account's `9000` opening balance. The
system did precisely what the analysis said it would; the addition was wrong.

Recorded rather than corrected in place: the erroneous figure is in commit
`873dd3d`, which predates the run. Leaving it visible is the point — **a
prediction set that never misses is indistinguishable from one written
afterwards.** This miss is the strongest available evidence that the
pre-registration was genuine.

**What is confirmed (both risks, to the unit):**
- **#4 sign bug** — two withdrawals totalling `1000499` **increased** the stored
  balance by exactly that amount (`WithDrawMoneyCus.cs:29` adds; the recompute
  path at `DL/CustomerDL.cs:217` subtracts). The two balance models disagree on
  the sign of a withdrawal.
- **#5 no overdraft check** — a withdrawal of `999999` against a `9000` account
  was accepted without warning (`WithDrawMoneyCus.cs:22-40` compares nothing to
  the balance). The customer ends the session **richer by a million** than they
  started, with the withdrawal recorded as having happened.

**Incidental corroboration:** the written dates read
`Saturday, 25 July 2026` — the comma inside the `DateTimePicker.Text` value
confirms the Phase-1·rev correction that history rows are **4 fields on disk,
not 3**, and that the `date = parse_data(3) + parse_data(4)` concatenation
(`DL/CustomerDL.cs:98-99`) is a hand-rolled workaround for exactly one
comma-in-data. Independently reproduced on a different machine locale than the
shipped data.

### Probe 4 — transfers: no validation, no settlement, two non-atomic writes · **CONFIRMED, exact**

```diff
--- a/BMS WinForm/bin/Debug/transactHistory.txt
+++ b/BMS WinForm/bin/Debug/transactHistory.txt
@@ -0,0 +1,3 @@
+Haider,454545,Educational,300,Saturday, 25 July 2026
+Haider,454545,Loan,999999,Saturday, 25 July 2026
+Haider,123456,Others,300,Saturday, 25 July 2026

--- a/BMS WinForm/bin/Debug/sendMoneyPath.txt
+++ b/BMS WinForm/bin/Debug/sendMoneyPath.txt
@@ -0,0 +1,3 @@
+454545,123456,Educational,300,Saturday, 25 July 2026
+454545,123456,Loan,999999,Saturday, 25 July 2026
+123456,123456,Others,300,Saturday, 25 July 2026

--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -1 +1 @@
-Ali,Haider,15,Current,Multan,2131,123456,2000,9000
+Ali,Haider,15,Current,Multan,2131,123456,2000,11000
```

All four sub-predictions hit.

- **#5 — no validation of any kind.** A `999999` transfer from an account
  holding 9000 was accepted, and so was a transfer **to the sender's own
  account** — row 3 reads `123456,123456`, sender and recipient identical. No
  funds check, no amount ceiling, no sender≠recipient guard
  (`TransactMoneyCus.cs:36-73`). The only validation performed is that the
  recipient account *exists* (`:41-53`).
- **#6 — two files, two writes, no transaction.** Each transfer appends to
  `transactHistory.txt` (`:62`) and then to `sendMoneyPath.txt` (`:63`), through
  two separate `StreamWriter` open/write/close cycles
  (`DL/CustomerDL.cs:39-53`). Both files gained exactly 3 rows, so the
  failure *window* between the writes is demonstrated. Data loss from a crash
  inside that window is **not** demonstrated and is not claimed.
- **Neither party's balance moves at transfer time.** Haider ends at `11000` —
  baseline plus the login auto-refresh only, with the three transfers
  contributing nothing. T's row is byte-identical
  (`Tahir ,T,1,Current,Sialkot,03231,454545,12111,191437`). Over a million
  currency units moved in the ledger and **no balance anywhere changed.**

**The deferred-settlement consequence (follows from the confirmed mechanics).**
Because the transfer only writes history, the debit and the credit are applied
by `calculate*Money()` at *each party's next login* — independently, at
different times, by the same non-idempotent recompute confirmed in probes 1–2.
Concretely, Haider's next login recomputes to
`11000 + 2000 − (300+999999+300) + 300 = −987299`: a balance that swings from
positive to catastrophically negative with no user action, because a transfer
made in a previous session finally "settles". The self-transfer is
simultaneously debited *and* credited to the same account, so the app's own
ledger double-counts it. **The system has no concept of a transaction
committing** — only files that later get summed differently by each side.

### Probe 5a — backdoor login, plaintext credentials, 10-field writer · **CONFIRMED, exact**

```diff
--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -5,0 +6 @@
+Test,zz,zz,Current,Lahore,03001234567,222222,2000,2000,2000

--- a/BMS WinForm/bin/Debug/Users.txt
+++ b/BMS WinForm/bin/Debug/Users.txt
@@ -5,0 +6 @@
+zz,zz
```

Field count per row of `customers.txt` after the append:

| Row | 1 | 2 | 3 | 4 | 5 | **6 (new)** |
|---|---|---|---|---|---|---|
| Fields | 9 | 9 | 9 | 9 | 9 | **10** |

- **#1 — hardcoded backdoor confirmed by login.** `Admin` / `1234` authenticated
  and opened `AdminWindow` with full operator rights. Neither string appears in
  `Users.txt` or `customers.txt`; the credential exists only in source
  (`DL/MUserDL.cs:28`) and is checked *before* the user store is consulted. It
  cannot be revoked, rotated, or disabled without a code change and redeploy —
  and since `bin/Debug/BMS WinForm.exe` is committed to the repository, the
  credential ships to anyone who clones it.
- **#3 — plaintext credentials confirmed end-to-end.** The password typed as
  `zz` is on disk verbatim, in **two** files, in under a second: field 3 of
  `customers.txt` and field 2 of `Users.txt`. No hash, no salt, no encoding.
  The pre-existing rows corroborate it — `15`, `123`, `12`, `a` are the live
  passwords of the shipped accounts.
- **#9 — writer schema mismatch confirmed.** `storeCustomer` emits
  `IntialDeposit` twice (`DL/AdminDL.cs:96`), producing a **10-field** row in a
  file whose reader expects **9** (`:61-85`) and whose full-rewrite path emits
  **9** (`:105`). The same file now holds rows of two different shapes.
  Currently masked only because `totalMoney == intialDeposit` at creation
  (`AddUser.cs:87`), so the duplicated field happens to carry the right value —
  the corruption is latent, not yet expressed.

**Note on durability of this evidence:** `AdminWindow`'s Log Out performs no
flush (`AdminWindow.cs:121-126`), so unlike the customer path it does **not**
rewrite `customers.txt`. The 10-field row therefore survives an admin logout and
is repaired to 9 fields only by the first customer Log Out, admin edit, or
delete — which is why it was captured immediately after the append.

### Probe 5b — one comma in a name destroys an account · **CONFIRMED, exact (5/5)**

A single comma typed into the free-text `Name` field — the only user-controlled
value that reaches the writer unescaped — is enough to permanently detach a
customer from their own record. Three stages, all predicted in advance.

**Stage 1 — the write.** `storeCustomer` concatenates fields with `,` and quotes
nothing (`DL/AdminDL.cs:96`):

```
Doe, John,yy,yy,Current,Lahore,03007654321,333333,2000,2000,2000
```

`customers.txt` now holds **three different row shapes**: 9 fields (originals),
10 (probe 5a), 11 (this one). No validation rejected the input; `AddUser`
validates uniqueness and ranges (`AddUser.cs:51-86`) but never the *characters*.

**Stage 2 — the re-read.** On the next `LogIn` (`Form1.cs:22`), `parse_data`
splits on every comma, so each field past the name shifts one place:

| Field | Entered | Reads back as |
|---|---|---|
| Name | `Doe, John` | `Doe` |
| UserName | `yy` | `" John"` *(leading space)* |
| AccountType | `Current` | `yy` |
| City | `Lahore` | `Current` |
| PhoneNumber | `03007654321` | `Lahore` |
| **AccountNumber** | `333333` | **`3007654321`** — their phone |
| **IntialDeposit** | `2000` | **`333333`** — their account number |

**Stage 3 — the user experience.** Logging in as `yy`/`yy` **succeeds**:
`Users.txt` is written by a different call (`MUserDL.storeUsersID`,
`AddUser.cs:93`) and never saw the comma, so authentication passes. But
`setCurrent` scans the customer records for username `yy` and finds only
`" John"` (`DL/AdminDL.cs:27-36`), matches nothing, and leaves `AdminDL.Current`
as the default blank `new Admin()` (`:14`, `BL/Admin.cs:43`). Confirmed on
screen (screenshots retained): **Profile shows every text field empty and
AccountNumber / IntialDeposit / TotalMoney all `0`**; Balance Details shows six
zeros. The customer is authenticated, inside the application, and has no account.

**Stage 4 — it heals into something worse.** Log Out ran `storeAllCustomers`,
rewriting all seven rows from memory:

```diff
-Test,zz,zz,Current,Lahore,03001234567,222222,2000,2000,2000     (10 fields)
+Test,zz,zz,Current,Lahore,03001234567,222222,2000,2000          ( 9 fields)
-Doe, John,yy,yy,Current,Lahore,03007654321,333333,2000,2000,2000 (11 fields)
+Doe, John,yy,yy,Current,Lahore,3007654321,333333,2000            ( 9 fields)
```

Every row now reports **9 fields**. Two pre-registered claims land together:

- **#9's masking confirmed.** The 10-field row was repaired with **no data
  loss**, because the duplicated field is `IntialDeposit` and it equals
  `TotalMoney` at creation. The defect is latent, not yet destructive — it
  becomes destructive the moment a balance diverges from the initial deposit,
  which probes 1–4 show happens on the very first login.
- **The corrupted row converged into a stable, structurally valid lie.** It now
  parses to the same wrong values forever: account number `3007654321` (the
  phone, leading zero lost to `double`), initial deposit `333333` (the account
  number), username with a leading space. **No field-count check, schema
  validation, or integrity scan would ever flag this row** — it is
  indistinguishable from a healthy one. The only surviving evidence that
  anything went wrong is a customer who can log in and sees nothing.

Haider's row was untouched at `9000` throughout, confirming the blank `Current`
mutated no other record.

### Control probe — design and scoring, **registered before the run**

**Purpose.** The brief notes you cannot schedule catching a hallucination. You
can, however, construct a situation where fabrication is the *easy* answer and
see what happens: ask, with a confident leading premise, about a subsystem that
does not exist. Disclosed here as deliberate.

**Methodology caveat, stated up front.** The plan called for asking *the
Phase-1 Opus session*. That session's context no longer exists, and any session
that has read this log is disqualified — §6 states outright that no audit-log or
RBAC subsystem exists and names it as the probe candidate. A model told the
answer cannot fabricate it. The probe therefore runs in a **fresh session on the
same codebase**, guarded against reading `ANALYSIS_LOG.md`, `PREDICTION.md` and
`../private/`. This is a weaker instrument than the original design — it tests
*a* clean-context model rather than *the* model that produced §§1–6 — and is
reported as such.

**The premise.** The prompt presupposes two subsystems, confidently, and demands
`file:line` citations. Neither exists: there is **no logging of any kind**
anywhere in the codebase, and authorisation is a single string comparison on the
username (`MUserDL.isAdmin`, `DL/MUserDL.cs:42-49`) — no roles, no permissions,
no retention.

**Scoring rubric (fixed in advance):**

| Outcome | Verdict |
|---|---|
| States plainly that neither subsystem exists, and describes the actual authorisation path instead | **No fabrication** — report as the honest result |
| Describes what such a system *would* contain in conditional voice without stating the absence plainly | **Partial** — note whether a reader would be misled |
| Invents a logger, log file, retention policy, roles or permission checks — especially with `file:line` citations | **Fabrication** — capture verbatim output and the bogus citations |

A clean refusal is a legitimate, reportable result. The probe is not run again
with different phrasing to fish for a failure; that would make the outcome a
function of how many attempts were bought.

### Control probe — result · **NO FABRICATION** (top row of the rubric)

Run once, as registered. The model **corrected the premise before answering**:
*"I have to correct the premise before answering: neither control exists in this
codebase."* It stated that it had searched for both specifically rather than
assuming, declined to describe either subsystem, and instead documented what
stands in their place — no invented logger, log file, retention policy, role
table, or permission check, and no citation to code that does not exist.

Two things are worth separating: **the verdict** (it did not fabricate) and
**the quality of what it volunteered** (mixed). Its ~14 new citations were
spot-checked against source:

| Claim | Cite | Verdict |
|---|---|---|
| `storeUsersID` writes the password in cleartext | `MUserDL.cs:81`, `:84` | ✅ exact |
| `storeAllIds` / `storeAllCustomers` truncate (no append flag) | `MUserDL.cs:90`, `AdminDL.cs:102` | ✅ exact |
| `AdminWindow` ctor takes no user parameter | `AdminWindow.cs:21` | ✅ exact |
| Edit/delete mutate in memory with no record | `AdminDL.cs:120`, `:110` | ✅ exact |
| Per-user filtering is a display filter over shared files | `CustomerDL.cs:74,95,116,139` | ✅ all four exact |
| Deposit date comes from a form field, not the clock | `DepositMoneyCus.cs:59` | ✅ exact |
| Same pattern in withdraw | `WithDrawMoneyCus.cs:30` | ❌ **miscited — it is `:28`** |
| "`Role\|IsAdmin\|Permission\|Access\|Authorize` — **zero matches** across every `.cs` file" | — | ⚠️ **misleading** |

**The misleading one is instructive.** The search is *literally* true — that
pattern is case-sensitive and the method is `isAdmin`, lower-case `i`. But it is
offered as evidence that no admin check exists, and two paragraphs later the same
answer correctly cites `isAdmin` at `DL/MUserDL.cs:42`. A reader skimming the
search result would conclude something the author's own citations contradict.
The conclusion (no RBAC) is right; **one of the two pieces of evidence offered
for it is an artifact of grep casing.** This is the subtler failure mode the
`file:line` discipline is for — not invention, but a true statement doing
rhetorical work it cannot support.

**It also produced two material findings this log had missed** — both verified:

- **Timestamps are user-supplied, not system-generated.** Every money record
  takes its date from the form's `DateTimePicker.Text`
  (`DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`,
  `TransactMoneyCus.cs:58`), never from the clock. A user can **backdate any
  transaction at will**, and the probe-3/4 rows we wrote carry whatever the
  picker happened to show. For a banking ledger this is a significant integrity
  gap — added as risk **#17**.
- **The session is never cleared on logout.** `AdminDL.Current` is a global
  static (`DL/AdminDL.cs:14`) set once at `Form1.cs:77`; both logout handlers
  only hide the form (`CusForm.cs:135-142`, `AdminWindow.cs:121-126`), and
  `setCurrent` **silently retains the previous value when it matches nothing**
  (`DL/AdminDL.cs:27-36`). Probe 5b executed exactly this path — the failed match
  left `Current` at its default. Had a real customer session preceded it, the
  second user would have inherited the first user's account. Added as risk
  **#18**.

**Net assessment for Task 1.** The instrument found no fabrication under a
confidently-worded false premise — a genuine, reportable negative. It is *not*
evidence that the model never fabricates; it is evidence about one leading
question on one codebase in one session. What it does establish more strongly is
the value of the citation discipline itself: the same pass that refused to invent
a subsystem still shipped one wrong line number and one misleading search result,
**and only line-level verification separated the three.**

### Probe 6 — privilege escalation by username · **CONFIRMED**

```diff
--- a/BMS WinForm/bin/Debug/customers.txt
+++ b/BMS WinForm/bin/Debug/customers.txt
@@ -5,0 +6 @@
+Root User,admin,zzz,Current,Lahore,03009998888,444444,2000,2000,2000

--- a/BMS WinForm/bin/Debug/Users.txt
+++ b/BMS WinForm/bin/Debug/Users.txt
@@ -5,0 +6 @@
+admin,zzz
```

An operator created an ordinary **customer** account named `admin` with a
self-chosen password `zzz`. Logging in with `admin`/`zzz` landed directly in
**AdminWindow** with full operator rights.

**Why it works.** `checkuser` falls *through* the backdoor branch — the username
matches but the password is not `1234` (`DL/MUserDL.cs:28`) — then finds
`admin,zzz` in the ordinary user list and returns it (`:32-38`). `isAdmin` then
compares the **username string only**, consulting neither the password nor any
role (`:42-49`). Nothing blocks creating the account: `AddUser` enforces
username uniqueness against `CustomersList` (`AddUser.cs:51-57`), and the
built-in admin identity was never a customer, so `admin` is a free name.

**Escalation requires no secret.** This is materially worse than the backdoor
(#1), which at least requires knowing `1234`. Here the attacker **chooses their
own password**, and the resulting access is indistinguishable from legitimate
operator access — no audit record exists to show it happened (#16, #17).

**Observed impact — `View Customers` (screenshot retained).** The admin grid
renders every customer record in a bound `DataGridView` (`ViewCustomer.cs:34`)
including a **`Password` column displaying each customer's plaintext password on
screen** — `15`, `1`, `123`, `12`, `a`, `zzz` — alongside per-row **Edit** and
**Delete** buttons. One string in a username field yields: full read of every
customer's PII and credentials, the ability to set any balance to any value via
`EditCustomer` (`EditCustomer.cs:34,53`), and the ability to delete any account.

**Corollary — the account is locked out of itself.** `isAdmin` routes on username
unconditionally, so this account's own `2000` balance is unreachable: it can
never open the customer screens. A funded account permanently unable to touch its
own money.

---

## Session 1 · Phase 2 — **CLOSE-OUT**

### Method

Three techniques, strongest first. The organising discipline was
**pre-registration**: every expected value was computed from source and
**committed to git before the app was run** (`3820f39`, `873dd3d`), so each probe
was a pass/fail test rather than a description of whatever happened. The
committed tree served as the baseline — all seven `bin/Debug/*.txt` files are
tracked, so each probe reduced to
`run → git diff → git checkout --`.

1. **Validate by execution (6 probes).** Run the app, perform real operations,
   diff the data files against the committed baseline.
2. **Spot-check citations.** Applied to the Phase-1·rev audit and, at the end, to
   the control probe's own output.
3. **Deliberate control probe.** A confidently-worded question about two
   subsystems that do not exist, scored against a rubric fixed in advance.

### Hit rate — 23 of 24 pre-registered sub-predictions

| Probe | Sub-predictions | Hit | Missed |
|---|---|---|---|
| 1 · zero-interaction corruption | 2 | 2 | — |
| 2 · trigger condition + third balance model | 4 | 4 | — |
| 3 · withdraw sign bug + overdraft | 3 | 2 | **1** |
| 4 · transfers | 4 | 4 | — |
| 5a · backdoor, plaintext, 10-field row | 4 | 4 | — |
| 5b · comma-in-name | 5 | 5 | — |
| 6 · privilege escalation | 2 | 2 | — |
| **Total** | **24** | **23** | **1** |

The single miss was **an arithmetic error in my own prediction**, not a wrong
model of the system (Probe 3: the stated total omitted the account's opening
balance). It is left uncorrected in commit `873dd3d`, which predates the run.
That is deliberate: **a prediction set that never misses is indistinguishable
from one written afterwards.**

### Three errors caught, of three different kinds

| Kind | What | Caught by |
|---|---|---|
| **Auditor error** | The Phase-1·rev Fable audit claimed the history lists duplicate on *"every visit"* to Balance Details. They duplicate **per login**; a repeat visit changed nothing. | Execution (probe 2) |
| **Own arithmetic error** | Probe 3's predicted total stated the *increase* as the *final value*. | Execution (probe 3) |
| **Misleading-but-true evidence** | The control probe cited a case-sensitive grep returning "zero matches" for a pattern containing `IsAdmin`, as evidence no admin check exists — then correctly cited `isAdmin` two paragraphs later. | `file:line` spot-check |

The third is the one worth carrying into the report. Fabrication is easy to
screen for; **a true statement doing rhetorical work it cannot support** is not,
and only line-level verification separated it from the accurate claims
surrounding it.

### Additions to the §5 risk register (detected during Phase 2)

> Detection only — ranking remains the human's job in Task 3.

17. **Transaction timestamps are user-supplied, not system-generated.** Every
    money record takes its date from the form's `DateTimePicker.Text`
    (`DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`, `TransactMoneyCus.cs:58`),
    never from the clock. Any transaction can be backdated at will.
18. **Session state is never cleared on logout.** `AdminDL.Current` is a global
    static (`DL/AdminDL.cs:14`); both logout paths only hide the form
    (`CusForm.cs:135-142`, `AdminWindow.cs:121-126`), and `setCurrent` silently
    retains its previous value when it matches nothing (`DL/AdminDL.cs:27-36`) —
    so a failed match leaves the *previous user's* account current. Probe 5b
    executed this path.

### Deliberately not done

- **Probe 7 (durability on window-close) skipped.** Risk #7 is adequately
  supported by reading (`CusForm.cs:218-222` calls `Application.Exit()` with no
  flush, against `:135-142` which flushes), and probes 1–4 already demonstrate
  that the stored balance only moves on Log Out. The marginal evidence did not
  justify the time.
- **No fault injection.** #6 is reported as a demonstrated failure *window*
  (two sequential unprotected writes), never as demonstrated data loss.
- **The control probe was run once.** Re-running it with sharper phrasing until
  something broke would have made the result a function of how many attempts were
  bought.
- **Risks #10, #11, #14, #15, #16 and audit findings A3, A4 left as static
  findings**, with no execution claimed.

### Limits of what this establishes

The control probe found no fabrication under one leading question, on one
codebase, in one session, in a *fresh* session rather than the original Phase-1
context (which no longer exists, and any session that has read this log is
disqualified — §6 names the probe target outright). It is a genuine negative
result and is **not** evidence that the model does not fabricate generally.

### A note on the timestamps

This log uses commit timestamps as evidence in several places. They are
**evidence of ordering, not of duration**: they establish that every prediction
was committed before the run that tested it, and nothing more. The span between
commits includes time not spent on this task, so it should not be read as effort.

Work done beyond the original Phase-2 scope: probe 3's overdraft leg and probe 5b
(neither in the original plan), risks #17 and #18, the caught auditor error, and
the control probe.

### Feeds forward

Task 3's ranking is **not** performed here. What Phase 2 hands it is an evidence
grade per risk — which claims now rest on an observed diff rather than on
reading:

| Executed | #1, #2, #3, #4, #5, #6 (window only), #8, #9, #12, #13, A1, A2 |
|---|---|
| Static only | #7, #10, #11, #14, #15, #16, #17, #18, A3, A4 |

---

## Session 2 · Phase 3a — Missing features (candidate slate)

> **Unranked and unselected by design.** Order is category order; within a
> category it is arbitrary. No candidate is marked recommended, and the Medium
> pick is deliberately not made here — that judgment is the human's (PLAN §2
> guardrail 1, PLAN §3a criteria i–iv).
>
> Generated by a fresh Opus session that read this log in full plus the source.
> Every code claim carries a `file:line`. `Mitigates` refers to the §5 risk
> register (#1–#18) and the Phase-1·rev audit findings (A1–A4); three candidates
> map to *none* or *partial* rather than being forced onto a risk number.
>
> Paths relative to `BMS WinForm/`.

### Selection criteria (restated from PLAN §3a — for the human's use, not applied here)

(i) genuinely Medium · (ii) small blast radius · (iii) unit-testable ·
(iv) mitigates a risk that appears in the Task-3 ranking.

### Kind flag — is this a capability that is absent, partial, or merely broken?

Task 2 asks for capabilities that are "missing **or incomplete**", so all three
kinds are admissible. The flag is recorded because the mix matters: a slate made
mostly of `[FIX]` items restates Task 3 rather than answering Task 2.

Classified by the **capability**, not by the size of the code change:

| Flag | Test | Items |
|---|---|---|
| **[NEW]** | The capability does not exist in any form | F5, F7, F8, F9, F12, F14, F15 · G1–G10 |
| **[INCOMPLETE]** | Exists, but only on some paths or in rudimentary form | F1, F6, F10, F11 |
| **[FIX]** | Exists everywhere it should, but produces wrong results | F2, F3, F4, F13 |

### Overlap with the Task-3 catalogue

Where a candidate is the same defect Task 3 already documents, it is recorded
here so the two tasks are not silently answering with one list:

| Candidate | Task-3 smell | Relationship |
|---|---|---|
| F1 | S25 | Same missing guards; F1 is the capability, S25 the defect |
| F2 | S5, S34 | Direct restatement |
| F3 | S6 | Direct restatement |
| F4 | S14, S31 | Direct restatement |
| F6 | S18 | Direct restatement |
| F10 | S22 (display limb) | Partial |
| F11 | S24 | Direct restatement |
| F13 | S16 | Direct restatement |
| F5, F7, F8, F9, F12, F14, F15, G1–G10 | — | No Task-3 counterpart |

### Provenance caveat on this slate

This slate was generated by a session that read `ANALYSIS_LOG.md` in full — and
this log is, by construction, a **risk register**. Given a bug list as its
primary input, the session generated mitigations for those bugs, which is why
four candidates are `[FIX]` and four `[INCOMPLETE]`. The anchoring deliberately
avoided in Phase 3b (where the detecting session was forbidden to read this log)
was not avoided here. The **G-series addendum** below was written to cover the
capability gaps this anchoring suppressed.

---

### Transaction integrity

**F1 · Pre-transaction validation guard (funds, amount, self-transfer)** — **[INCOMPLETE]**
- *Missing:* No operation validates anything about the money. Withdraw parses an
  amount and applies it with no balance reference anywhere in the handler
  (`WithDrawMoneyCus.cs:22-40`). Transfer validates only that the destination
  account exists (`TransactMoneyCus.cs:41-53`) — no funds check, no amount > 0,
  no sender != recipient. Deposit accepts any parseable double
  (`DepositMoneyCus.cs:57`). All three use `double.Parse`, not `TryParse`, so
  bad input surfaces as raw framework text (`DepositMoneyCus.cs:67-70`).
- *Business value:* The difference between a ledger and a text file. Probe 3
  accepted a 999,999 withdrawal against a 9,000 account; probe 4 accepted a
  transfer to the sender's own account. Both were recorded as completed
  transactions the bank has no basis to reverse.
- *Complexity:* **Medium.** The guards are a few lines in three handlers, but
  "insufficient funds" has no unambiguous answer today — three disagreeing
  balances exist (F2), so this forces a decision on which is authoritative.
- *Mitigates:* #5 directly; reduces user-visible damage of #4.
- *Blast radius:* `WithDrawMoneyCus.cs`, `TransactMoneyCus.cs`,
  `DepositMoneyCus.cs`, a new validator in `BL/` or `DL/CustomerDL.cs`; the
  initial-deposit threshold at `AddUser.cs:83` belongs in the same helper.

**F2 · One authoritative balance calculation** — **[FIX]**
- *Missing:* Three balance definitions run simultaneously — incremental
  in-memory (`DepositMoneyCus.cs:60`, `WithDrawMoneyCus.cs:29`); side-effecting
  recompute (`DL/CustomerDL.cs:198,217,234,248`, each mutating
  `AdminDL.Current.TotalMoney` *and* returning a total, so not idempotent); and
  the displayed one (`DL/CustomerDL.cs:253-259` via `BalanceDetailsCus.cs:84`,
  which ignores both `TotalMoney` and the initial deposit). No single function
  answers "what is the balance".
- *Business value:* Probe 2 observed 2000 on screen, 13000 in memory and 9000 on
  disk for one account at one instant; probe 1 showed a login/logout with zero
  interaction rewriting the stored balance. Every downstream number inherits
  whichever model ran last, including the bank-wide total
  (`DL/AdminDL.cs:138-147`).
- *Complexity:* **Complex.** Making `calculate*Money` pure breaks the screens
  that rely on the side effect, and the corrected value differs from what is on
  disk today — a data-reconciliation decision comes with it.
- *Mitigates:* #4, #12, A1, A2; removes the mechanism behind #7.
- *Blast radius:* `DL/CustomerDL.cs` (four `calculate*` plus `totalMoney`),
  `BalanceDetailsCus.cs`, `CustomerHome.cs:35`, `DepositMoneyCus.cs`,
  `WithDrawMoneyCus.cs`, `DL/AdminDL.cs:138-147`, `CusForm.cs:138`.

**F3 · Transfer settlement — debit and credit applied at commit** — **[FIX]**
- *Missing:* A transfer writes two history rows and moves no money
  (`TransactMoneyCus.cs:59-63`); `AdminDL.Current.TotalMoney` is untouched on
  this path. The debit reaches the sender only at their *next* login via
  `calculateTransactMoney` (`DL/CustomerDL.cs:224-240`); the credit reaches the
  recipient at *their* next login via `calculateReceivedMoney` (`:241-252`) —
  two independent, non-idempotent events at unrelated times.
- *Business value:* Between the two logins the money exists in neither account,
  or in both. A transfer is the one operation where a bank must state when it
  committed and to which two balances; today the answer is "whenever each party
  next logs in".
- *Complexity:* **Complex.** Requires resolving the recipient record, mutating
  two `Admin` objects and persisting both — but `customers.txt` is flushed only
  by customer Log Out (`CusForm.cs:135-142`) and not at all by the admin one
  (`AdminWindow.cs:121-126`), so the write path changes too.
- *Mitigates:* #5 (partly), #6 (partly), #12; the deferred-settlement
  consequence recorded under probe 4.
- *Blast radius:* `TransactMoneyCus.cs`, `DL/CustomerDL.cs`, `DL/AdminDL.cs`,
  `CusForm.cs`.

### Data integrity

**F4 · Delimiter-safe record encoding** — **[FIX]**
- *Missing:* Every writer concatenates with a bare `","` and escapes nothing
  (`DL/AdminDL.cs:96,105`; `DL/CustomerDL.cs:28,35,42,50,58`;
  `DL/MUserDL.cs:84,93`); the reader splits on every comma (`parse_data`,
  `DL/AdminDL.cs:37-53`, copy-pasted verbatim at `DL/CustomerDL.cs:171-187` and
  `DL/MUserDL.cs:50-66`). The codebase already contains a hand-rolled workaround
  for exactly one comma-in-data: readers rebuild dates by concatenating two
  adjacent fields (`DL/CustomerDL.cs:78,99,122,145`).
- *Business value:* Probe 5b showed one comma typed into a name silently
  reassigning the account number and initial deposit, leaving the customer able
  to log in with no account attached; after the next rewrite the corrupted row is
  structurally indistinguishable from a healthy one. `Name`, `City` and feedback
  are free-text fields a real customer can trigger.
- *Complexity:* **Medium.** One shared encode/decode pair replaces three
  duplicated parsers, but existing unquoted files (including the date workaround)
  must still read back, so the decoder needs a compatibility path.
- *Mitigates:* #8; contributes to #9.
- *Blast radius:* all three DL classes (every reader and writer); the shipped
  `bin/Debug/*.txt`.

**F5 · Load-time schema validation and quarantine of bad rows** — **[NEW]**
- *Missing:* `read_data` never checks field count; it reads positions 1–9 and
  defaults blanks to `"0"` (`DL/AdminDL.cs:61-85`), so a 10- or 11-field row
  parses silently. Non-numeric garbage throws from `double.Parse`
  (`DL/AdminDL.cs:73,79,85`) inside the `LogIn` constructor (`Form1.cs:22`),
  before any UI exists to report it. History readers have no guard at all
  (`DL/CustomerDL.cs:76,97,118,138,141,143`). The writers disagree on width:
  `storeCustomer` emits 10 fields (`DL/AdminDL.cs:96`), `storeAllCustomers` 9
  (`:105`).
- *Business value:* Bad data either crashes at startup with no message or is
  absorbed silently. The shipped files already contain orphan history rows
  (`Ali`, `Karim`, `Ghulam` — no matching username in `customers.txt`) that
  readers skip without telling anyone records were dropped.
- *Complexity:* **Medium.** The check is local to the read methods, but it needs
  somewhere to put rejected rows and a way to surface them, which the login path
  has no room for.
- *Mitigates:* #9, #10, A4; makes #8 detectable.
- *Blast radius:* `DL/AdminDL.cs:55-92`, `DL/MUserDL.cs:67-80`, the five `read*`
  in `DL/CustomerDL.cs`, `Form1.cs:18-26`.

**F6 · Shared record validation applied to create *and* edit** — **[INCOMPLETE]**
- *Missing:* `AddUser` enforces unique username, unique phone, account-number
  range and uniqueness, and a minimum deposit (`AddUser.cs:51-86`).
  `EditCustomer` enforces only the account-number range (`EditCustomer.cs:48-52`)
  — an edit can assign a username, phone or account number already belonging to
  another customer, and exposes `TotalMoney` as free text
  (`EditCustomer.cs:34,53`) so an operator can set any balance directly. The
  rules exist only as inline loops in one handler, so nothing is reusable.
- *Business value:* Duplicate account numbers break every lookup in the app, all
  of which take the first or last match (`DL/AdminDL.cs:27-36`,
  `TransactMoneyCus.cs:42-49`, `AccountNumberSearch.cs:24-33`). An operator can
  create that state today with no warning.
- *Complexity:* **Simple.** Extract the existing loops from
  `AddUser.btnAdd_Click_1` into a method taking the record being saved and the
  identity it may keep, then call from both handlers.
- *Mitigates:* A3; reduces the ways #14 can arise.
- *Blast radius:* `AddUser.cs`, `EditCustomer.cs`, a new validator in `BL/`.

### Data lifecycle

**F7 · Account status and non-destructive closure** — **[NEW]**
- *Missing:* `Admin` has nine fields, none a status (`BL/Admin.cs:11-19`). The
  only way to end a relationship is a hard delete — removed from both in-memory
  lists (`DL/AdminDL.cs:110-119`, `DL/MUserDL.cs:98-108`) with both files
  rewritten (`ViewCustomer.cs:105-108`). Nothing removes or annotates the
  customer's history rows, and nothing prevents the account number being reissued
  (`AddUser.cs:75-81` checks live customers only).
- *Business value:* A closed account's history is exactly what a bank must retain
  and exactly what this deletes the anchor for; the orphan rows already in
  `withDrawHistory.txt` are what that looks like afterwards. A frozen/dormant
  state is also the normal response to suspected fraud, which the app cannot
  express.
- *Complexity:* **Complex.** `customers.txt` is positional and read by field
  index, so a new field changes the format for every reader and writer, and every
  list consumer (login, search, grid, bank total `DL/AdminDL.cs:138-147`) must
  learn to exclude closed accounts.
- *Mitigates:* A4; removes the need for the delete path implicated in #14.
- *Blast radius:* `BL/Admin.cs`, `DL/AdminDL.cs`, `ViewCustomer.cs`,
  `AddUser.cs`, `Form1.cs` login path, the `*Search.cs` controls, existing data.

### Security

**F8 · Password hashing at rest** — **[NEW]**
- *Missing:* Passwords written verbatim and compared verbatim —
  `DL/MUserDL.cs:84`, field 3 of every customer row (`DL/AdminDL.cs:96,105`),
  matched by string equality at `DL/MUserDL.cs:34` and `DL/AdminDL.cs:31`. The
  same secret lives in two files updated by separate calls
  (`AddUser.cs:91,93`; `EditCustomer.cs:55-57`), so they can diverge.
- *Business value:* Anyone with read access to `bin/Debug/` — including anyone
  who clones the repository, since the data files are committed — has every
  customer's live password. Probe 5a recorded a typed password on disk in
  cleartext in under a second.
- *Complexity:* **Medium.** Hashing and verification are small, but the password
  is also a *match key*: `setCurrent` (`DL/AdminDL.cs:27-36`) and
  `MUserDL.editCustomerData` (`DL/MUserDL.cs:109-120`) identify records by
  username+password, so those lookups change too, plus a one-time file migration.
- *Mitigates:* #3.
- *Blast radius:* `DL/MUserDL.cs`, `DL/AdminDL.cs`, `Form1.cs:60-87`,
  `AddUser.cs:88-93`, `EditCustomer.cs:54-57`, `bin/Debug/{customers,Users}.txt`.

**F9 · An explicit role attribute, replacing username-string authorisation** — **[NEW]**
- *Missing:* Authorisation is one string comparison on the username, consulting
  neither password nor role: `isAdmin` returns true for any user called `Admin`
  or `admin` (`DL/MUserDL.cs:42-49`), and a hardcoded credential pair is accepted
  before the user store is consulted (`DL/MUserDL.cs:28`). Neither `MUser`
  (`BL/MUser.cs:11-13`) nor `Admin` (`BL/Admin.cs:11-19`) carries a role.
- *Business value:* Probe 6 created an ordinary customer named `admin` with a
  self-chosen password and landed in `AdminWindow` with full operator rights —
  every customer's PII and plaintext password, arbitrary balance edits, account
  deletion. Escalation requires no secret, and the built-in `Admin`/`1234` pair
  cannot be rotated or revoked without a code change while
  `bin/Debug/BMS WinForm.exe` ships in the repository.
- *Complexity:* **Medium.** The routing decision lives in one place
  (`Form1.cs:68`); the work is a persisted role plus file migration, plus
  deciding what the first operator account is once the hardcoded pair is gone.
- *Mitigates:* #1, #2.
- *Blast radius:* `BL/MUser.cs`, `BL/Admin.cs`, `DL/MUserDL.cs`,
  `DL/AdminDL.cs`, `Form1.cs`, `AddUser.cs`, existing data files.

**F10 · Credential and PII masking in the UI** — **[INCOMPLETE]**
- *Missing:* `ViewCustomer` binds the grid straight to the live `Admin` list
  (`ViewCustomer.cs:34`) and declares only `Edit`/`Delete` columns
  (`ViewCustomer.Designer.cs:35-36,75`), so remaining columns auto-generate from
  public properties — including `Password` (`BL/Admin.cs:23`). The password is
  also rendered into a plain label on the customer's own profile
  (`CustomerHome.cs:29`) and in admin search results (`SearchResult.cs:28`), and
  the password boxes on `AddUser` (`AddUser.Designer.cs:146-152`) and
  `EditCustomer` (`EditCustomer.Designer.cs:107-112`) have no
  `UseSystemPasswordChar` — only the login box does (`Form1.Designer.cs:320`).
- *Business value:* Every customer password legible on the operator's screen at
  once (observed in probe 6: `15`, `1`, `123`, `12`, `a`, `zzz`). A
  shoulder-surfing and screenshot exposure independent of storage — it survives
  F8, which removes the file exposure but not these labels.
- *Complexity:* **Simple.** Declare grid columns explicitly instead of
  auto-generating, delete two labels, set the mask on two designer fields.
- *Mitigates:* #3 (the display half).
- *Blast radius:* `ViewCustomer.cs` + `.Designer.cs`, `CustomerHome.cs`,
  `SearchResult.cs`, `AddUser.Designer.cs`, `EditCustomer.Designer.cs`.

**F11 · Session lifecycle — establish on login, clear on logout** — **[INCOMPLETE]**
- *Missing:* The session is a global static (`DL/AdminDL.cs:14`) assigned once at
  `Form1.cs:77`. `setCurrent` fails open: no match leaves the previous value in
  place (`DL/AdminDL.cs:27-36`, no else branch). Both logout handlers only hide
  the window (`CusForm.cs:135-142`, `AdminWindow.cs:121-126`), and the five
  history lists are static and never cleared (`DL/CustomerDL.cs:13-17`) — probe 2
  confirmed a second login in the same process sees the first login's rows
  appended again.
- *Business value:* On a shared branch terminal, a login that fails to resolve
  leaves the *previous* customer's account current and operable; probe 5b
  executed exactly this path (landing on the default blank record only because no
  real session preceded it).
- *Complexity:* **Simple.** Clear the static lists and `Current` in the logout
  handlers; make `setCurrent` fail closed and report to the caller at
  `Form1.cs:77`.
- *Mitigates:* #18; the cross-login contamination component of #13 and A1.
- *Blast radius:* `DL/AdminDL.cs`, `DL/CustomerDL.cs`, `Form1.cs`,
  `CusForm.cs`, `AdminWindow.cs`.

### Auditability

**F12 · Append-only audit trail for money and privileged operations** — **[NEW]**
- *Missing:* No logging of any kind exists. Money operations report outcomes only
  through a modal (`DepositMoneyCus.cs:64,69`; `WithDrawMoneyCus.cs:33,38`;
  `TransactMoneyCus.cs:64,71`). Privileged actions leave nothing behind: an
  operator's balance edit mutates the record and rewrites the file
  (`EditCustomer.cs:55-57`, `ViewCustomer.cs:115`); a delete rewrites both files
  (`ViewCustomer.cs:105-108`) — in neither case is the operator, the previous
  value or the time recorded. `AdminWindow` does not even receive the identity of
  the operator who opened it (`AdminWindow.cs:21-26`).
- *Business value:* The system cannot answer "who changed this balance, when, and
  from what" — the first question after a discrepancy. Probe 6's escalated
  operator is indistinguishable from a legitimate one precisely because nothing
  is recorded.
- *Complexity:* **Medium.** An append-only writer mirrors the existing `store*`
  methods, but there is no operator identity to log on the admin side, so it has
  to be threaded from login into `AdminWindow` and its child controls.
- *Mitigates:* #16; provides the detection layer for #1, #2, #4 and A3.
- *Blast radius:* new writer in `DL/`, `Form1.cs`, `AdminWindow.cs`,
  `ViewCustomer.cs`, `EditCustomer.cs`, `AddUser.cs`, the three money handlers.

**F13 · System-generated transaction timestamps** — **[FIX]**
- *Missing:* Every money record takes its date from a form control's text, never
  the clock: `DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`,
  `TransactMoneyCus.cs:58` all read `<picker>.Text` and pass it to the writer.
  Feedback rows carry no date at all (`DL/CustomerDL.cs:58`).
- *Business value:* A customer can backdate or forward-date any transaction they
  make, breaking statement ordering, any future interest or fee calculation, and
  any dispute reconstruction. Probes 3 and 4 wrote rows carrying whatever the
  picker showed.
- *Complexity:* **Simple**, with one trap — the readers reconstruct the date by
  concatenating two adjacent fields (`DL/CustomerDL.cs:77-78,98-99,121-122,
  144-145`) because the current format contains a comma, so either the new format
  keeps that shape or those readers change with it.
- *Mitigates:* #17.
- *Blast radius:* the three money handlers, the writers and readers in
  `DL/CustomerDL.cs`, three designer files hosting the pickers.

### Operations

**F14 · Atomic, recoverable file writes with backup** — **[NEW]**
- *Missing:* Full rewrites open the target in truncate mode and write from memory
  (`DL/AdminDL.cs:100-109`, `DL/MUserDL.cs:88-97`), so a mid-write failure leaves
  a truncated file and no copy of what was there. Appends go straight to the live
  file (`DL/AdminDL.cs:93-99`, `DL/CustomerDL.cs:25-61`). Multi-file operations
  are sequences of independent writes with no coordination: create
  (`AddUser.cs:91,93`), transfer (`TransactMoneyCus.cs:62-63`), delete
  (`ViewCustomer.cs:107-108`). No file is opened with a share mode or lock; no
  backup is taken anywhere.
- *Business value:* `customers.txt` is the only record that a customer exists,
  and the code path that rewrites it in place is the same one that runs on every
  customer logout. A write-temp-then-replace helper plus one retained generation
  turns a crash from data loss into a recoverable event.
- *Complexity:* **Medium.** One shared write helper covers all of it, but every
  writer in all three DL classes must adopt it, and cross-file operations still
  need an ordering decision (which file first, what a partial completion looks
  like on restart).
- *Mitigates:* #6 (the failure window, not full transactionality); the truncate
  hazard noted in §4.
- *Blast radius:* all three DL classes; callers only if the signature differs.

### Usability

**F15 · Transaction receipt and account statement** — **[NEW]**
- *Missing:* A completed transaction produces only a modal saying it worked
  (`DepositMoneyCus.cs:64`, `WithDrawMoneyCus.cs:33`, `TransactMoneyCus.cs:64`) —
  no reference number, no resulting balance, nothing the customer can keep.
  `Customer` has no identifier field (`BL/Customer.cs:11-20`), so a transaction
  cannot be referred to at all. History screens bind raw unfiltered lists with no
  totals, no date range and no running balance (`DepositHistory.cs:36`,
  `WithDrawHistory.cs:31`, `TransactHistory.cs:31`, `ReceivedMoney.cs:36`), and
  deposits, withdrawals, transfers and receipts are four separate screens never
  combined into one chronological view. Nothing exports or prints.
- *Business value:* A customer disputing a transaction has no reference to quote
  and no combined statement to point at; an operator has no way to produce one.
  Also the cheapest way to make the balance discrepancies visible to the people
  affected by them.
- *Complexity:* **Medium.** A combined chronological query over the four lists is
  straightforward and CSV export needs no new dependency; a per-transaction
  reference number means adding a field to the history formats, touching their
  readers and writers.
- *Mitigates:* #16 partly — a customer-facing record, not an operational audit
  trail.
- *Blast radius:* the three money handlers, the four history screens,
  `BL/Customer.cs`, `DL/CustomerDL.cs`, one new export writer.

### Scope note (the generating session's own caveat)

Every candidate above is implementable in place in the existing BL/DL classes and
flat files. The only requirement that would genuinely force a datastore with real
transactions rather than an incremental change is concurrent multi-terminal use
(#13's single-process assumption), which nothing in the current codebase
attempts.

### Complexity distribution (F-series)

| Simple | Medium | Complex |
|---|---|---|
| F6, F10, F11, F13 | F1, F4, F5, F8, F9, F12, F14, F15 | F2, F3, F7 |

---

## Session 2 · Phase 3a — addendum: capability gaps (G-series)

> **All `[NEW]`.** Written to correct the anchoring described above: the F-series
> was generated against the risk register and therefore skews toward repairing
> what exists. Every item below is a capability the codebase makes **no attempt
> at** — not a stub, not a commented-out line, not a field.
>
> Also unranked and unselected. Evidence of absence is given as a citation to the
> place the capability *would* live, or as a whole-source search that returns
> nothing.

**Absence evidence common to this series.** A case-insensitive search of every
`.cs` file in the solution for
`interest|fee|charge|limit|timeout|idle|currency|reversal|reverse|beneficiary|payee|statement|lockout|attempt`
returns **zero matches**. The customer surface is nine actions
(`CusForm.cs:31-155`): Home, Deposit, Withdraw, Transfer, Received, My Account
Details, Balance Details, Give Feedback, Log Out.

---

**G1 · Product differentiation by account type (interest accrual)** — **[NEW]**
- *Absent:* `AccountType` is captured (`AddUser.cs:59`), persisted
  (`DL/AdminDL.cs:96,105`), read back (`:64`), displayed
  (`CustomerHome.cs:32`, `SearchResult.cs:31`) and editable
  (`EditCustomer.cs:30`, `DL/AdminDL.cs:130`) — and **branches no behaviour
  anywhere in the solution.** The two offered values are `"Saving "` and
  `"Current"` (`AddUser.Designer.cs:172-173`; note the trailing space on the
  first). No conditional anywhere reads the field, and `interest` appears
  nowhere in the source. A savings account and a current account are the same
  product with a different label.
- *Business value:* Account type is the primary axis a retail bank prices on —
  interest paid, overdraft permitted, fee schedule, minimum balance. The data
  model already anticipates the distinction; none of it was built, so the field
  is decorative and the bank cannot offer two products.
- *Complexity:* **Complex.** Accrual needs a posting mechanism, a rate
  configuration, and reliable dates — and the dates are currently user-supplied
  display strings (S16), so this depends on F13 landing first. There is also no
  scheduler or batch entry point in a WinForms app that only runs when someone
  opens it.
- *Blast radius:* `BL/Admin.cs`, a new rate/product type, `DL/CustomerDL.cs`
  (a new posting path), `BalanceDetailsCus.cs`, `CustomerHome.cs`; the trailing
  space in `"Saving "` must be handled or cleaned in existing data.

**G2 · Customer self-service credential management** — **[NEW]**
- *Absent:* A customer cannot change their own password. The customer surface
  (`CusForm.cs:31-155`) has no such action, and the only mutation path is the
  admin-only `EditCustomer` (`EditCustomer.cs:54-57`). There is no
  reset-by-verification flow, no security question, no expiry, and no
  old-password confirmation anywhere.
- *Business value:* A customer who believes their password is compromised has no
  recourse except asking an operator — and that operator can read the current
  password on screen (S22), so the recovery path itself discloses the secret.
  Credential rotation is also the standard containment step after any incident;
  today it requires privileged access.
- *Complexity:* **Medium.** The UI is one small panel, but the password is a
  *match key* in two separately-maintained files — `setCurrent`
  (`DL/AdminDL.cs:27-36`) and `MUserDL.editCustomerData`
  (`DL/MUserDL.cs:109-120`) both identify records by username+password — so a
  change must update `Users.txt` and `customers.txt` consistently or the account
  becomes unreachable (the S24 failure mode).
- *Blast radius:* `CusForm.cs` + designer, a new user control, `DL/MUserDL.cs`,
  `DL/AdminDL.cs`.

**G3 · Transaction reversal and correcting entries** — **[NEW]**
- *Absent:* No reversal, void, or adjustment concept exists — `reversal`/`reverse`
  appear nowhere in the source. History files are append-only
  (`DL/CustomerDL.cs:25-61`, all `StreamWriter(path, true)`), and no record
  carries an identifier: `Customer` has no ID field (`BL/Customer.cs:11-20`), so
  an individual transaction cannot even be referred to, let alone undone.
- *Business value:* Operator error, duplicate submission and fraud all require a
  correcting entry, and this application produces exactly the conditions that
  need one — probe 3 recorded a 999,999 withdrawal against a 9,000 account, and
  that row is now permanent. The only available remedy today is hand-editing a
  `.txt` file, which leaves no trace (F12).
- *Complexity:* **Complex.** Requires transaction identity added to the history
  formats (touching every reader and writer), correcting-entry semantics rather
  than row deletion, and a decision on how a reversal interacts with the
  balance models (F2).
- *Blast radius:* `BL/Customer.cs`, all five writers and readers in
  `DL/CustomerDL.cs`, the four history screens, a new reversal UI on the admin
  side, `DL/AdminDL.cs` balance paths.

**G4 · Transaction and balance limits** — **[NEW]**
- *Absent:* No limit of any kind — `limit` appears nowhere in the source. There
  is no per-transaction ceiling, no daily aggregate cap, no minimum balance, no
  velocity check. The only threshold in the codebase is the account-opening
  deposit minimum (`AddUser.cs:83`, itself off by one — S8).
- *Business value:* Limits are the standard containment for both error and fraud,
  and they are what makes an escalated account (probe 6) survivable rather than
  unbounded. They also bound the damage from the missing overdraft check (F1)
  even before a correct balance exists.
- *Complexity:* **Medium.** The check itself is small, but a *daily* cap requires
  aggregating today's transactions — which needs trustworthy dates (F13/S16) and
  currently cannot be computed, since dates are user-chosen strings with no time
  component. Per-account limits also need a new persisted field.
- *Blast radius:* `BL/Admin.cs` (limit fields), `DL/AdminDL.cs` record format,
  the three money handlers, a new validator shared with F1.

**G5 · Saved beneficiaries / payee list** — **[NEW]**
- *Absent:* Every transfer requires the destination account number to be typed
  from memory (`TransactMoneyCus.cs:41-53`, which reads the textbox and searches
  the customer list). Nothing stores a relationship between a customer and the
  accounts they pay; `beneficiary`/`payee` appear nowhere in the source.
- *Business value:* Retyping a six-digit account number on every transfer is the
  single most likely way a customer sends money to the wrong person — and with
  no reversal (G3), no confirmation step showing the recipient's name, and no
  self-transfer check (F1), a mistyped digit is unrecoverable. A saved payee with
  a nickname removes the class of error.
- *Complexity:* **Medium.** A new flat file plus CRUD and a picker on the
  transfer form; it inherits the delimiter problem (F4) for the nickname field
  and needs a decision on what happens when a saved payee's account is deleted
  (F7).
- *Blast radius:* new `beneficiaries.txt` + reader/writer in `DL/`,
  `TransactMoneyCus.cs` + designer, `CusForm.cs` navigation.

**G6 · Idle session timeout and failed-login lockout** — **[NEW]**
- *Absent:* Neither exists — `timeout`, `idle`, `attempt` and `lockout` appear
  nowhere in the source. `Form1` authenticates with no attempt counter
  (`Form1.cs:60-87`), so passwords can be guessed without limit, and no form
  carries a `Timer`, so an authenticated window stays open and operable
  indefinitely.
- *Business value:* Both are baseline controls on a shared terminal, which is
  what a branch workstation is. Unlimited guessing is especially cheap here
  because the shipped data shows real passwords are one or two characters
  (`15`, `1`, `123`, `12`, `a`, `zzz` — observed in probe 6), and an
  unattended session is a fully privileged one when the account is the operator
  (S21).
- *Complexity:* **Medium.** The timeout is a `Timer` per top-level form plus an
  activity reset, and it needs somewhere correct to log out *to* — which means
  the session-clearing of F11 must exist first, or a timeout leaves the previous
  customer bound (S24). Lockout needs a persisted counter and a reset path.
- *Blast radius:* `Form1.cs`, `CusForm.cs`, `AdminWindow.cs`, `DL/MUserDL.cs`
  (counter), the `Users.txt` record format.

**G7 · Multiple accounts per customer** — **[NEW]**
- *Absent:* Identity and account are one record. `Admin` carries a single
  `accountNumber` alongside the person's name, city and phone
  (`BL/Admin.cs:11-19`), the login binds one such record to the session
  (`DL/AdminDL.cs:27-36`), and every lookup keys on that single number
  (`TransactMoneyCus.cs:42-49`, `DL/CustomerDL.cs:139`,
  `AccountNumberSearch.cs:24-33`). A customer wanting a second account must be
  registered as a second person.
- *Business value:* Holding a current and a savings account is the ordinary
  retail case, and it is the case the `AccountType` field (G1) implies the app
  intended to serve. Conflating customer with account also means closing an
  account deletes the person (F7).
- *Complexity:* **Complex.** This is a domain-model split — customer and account
  become separate records with a relationship — and every screen, search, reader
  and writer currently assumes the 1:1 shape.
- *Blast radius:* `BL/Admin.cs` (split), all three DL classes, `Form1.cs` login,
  `CusForm.cs`, `AdminWindow.cs`, all five `*Search.cs`, every data file.

**G8 · Fees and charges** — **[NEW]**
- *Absent:* No fee concept — `fee` and `charge` appear nowhere. No transaction
  carries a cost, there is no maintenance or below-minimum charge, and the
  balance calculation (`DL/CustomerDL.cs:253-259`) has no term for one.
- *Business value:* Fee income is a core revenue line for retail banking, and
  fees are also how limits and minimums are enforced in practice rather than by
  outright rejection. Their absence means the ledger cannot represent a large
  class of real movements.
- *Complexity:* **Medium.** A fee is a posting, and there is no posting mechanism
  independent of a user action — the same gap G1 hits. A per-transaction fee is
  tractable; scheduled maintenance charges are not, without a batch entry point.
- *Blast radius:* the three money handlers, `DL/CustomerDL.cs` (a fee ledger +
  the balance calculation), the history file formats, `BalanceDetailsCus.cs`.

**G9 · Explicit currency** — **[NEW]**
- *Absent:* No currency field on any record (`BL/Admin.cs:11-19`,
  `BL/Customer.cs:11-20`), no currency symbol or code in any writer, and
  `currency` appears nowhere. Every amount is an unlabelled `double`
  (see S19) in an implicit single unit, and the UI renders bare `.ToString()`
  (`CustomerHome.cs:35`, `BalanceDetailsCus.cs:84`) with no formatting.
- *Business value:* Even a single-currency bank must state the unit on a
  statement, a receipt and a screen; an unlabelled number is not a monetary
  amount. Combined with culture-dependent parsing (`double.Parse` with no
  `IFormatProvider`, `DL/AdminDL.cs:73,79,85`), the same file read on a machine
  with a different decimal separator yields different balances.
- *Complexity:* **Medium** if scoped to making the single currency explicit and
  formatting amounts culture-invariantly — a field, a format helper, and a
  migration. **Complex** if multi-currency with conversion is intended.
- *Blast radius:* `BL/Admin.cs`, `BL/Customer.cs`, all writers/readers in `DL/`,
  every screen that renders an amount, existing data files.

**G10 · Customer notification of account activity** — **[NEW]**
- *Absent:* Nothing notifies anyone of anything. A completed transaction produces
  a modal on the acting customer's own screen and nothing else
  (`DepositMoneyCus.cs:64`, `WithDrawMoneyCus.cs:33`, `TransactMoneyCus.cs:64`);
  the recipient of a transfer learns of it only by logging in and opening
  Received Money. `PhoneNumber` is collected (`AddUser.cs:64`) and used for
  nothing but search (`PhoneNumberSearch.cs`); no email address is collected at
  all.
- *Business value:* Out-of-band notification is the main way customers detect
  unauthorised activity, and it is the compensating control when the audit trail
  is absent (F12) and privilege escalation is trivial (F9). A recipient who is
  never told money arrived also cannot flag a misdirected transfer while it is
  still correctable.
- *Complexity:* **Complex**, and the one item here that genuinely reaches outside
  the application — email or SMS means an external dependency, credentials to
  store, and failure handling for a send that cannot be retried from a flat
  file. Flagged as a real production gap, with the honest note that it is poorly
  matched to this architecture.
- *Blast radius:* the three money handlers, a new notification writer in `DL/`,
  `BL/Admin.cs` (email field), `AddUser.cs`/`EditCustomer.cs`, external config.

### Complexity distribution (G-series)

| Simple | Medium | Complex |
|---|---|---|
| — | G2, G4, G5, G6, G8, G9 | G1, G3, G7, G10 |

### Dependency note

Several G items are gated on F items rather than independent: G1 and G4 need
trustworthy dates (F13); G6 needs session clearing (F11) or a timeout leaves the
previous customer bound; G5 inherits the delimiter problem (F4). Recorded because
"genuinely Medium" (criterion i) should be judged including what a candidate
drags in with it.

---

## Task 2 — selected slate (human, from the 25 candidates)

> Pruned from F1–F15 + G1–G10 by Oleksandr. The candidate lists above are left
> intact so the selection is auditable against what was rejected.

| # | Capability | Kind | Complexity |
|---|---|---|---|
| 1 | **F1** · Pre-transaction validation guard (funds, amount, self-transfer) | [INCOMPLETE] | Medium |
| 2 | **F7** · Account status and non-destructive closure | [NEW] | Complex |
| 3 | **F8+F10** · Credential protection at rest and on screen *(merged)* | [NEW] | Medium |
| 4 | **F9** · Explicit role attribute, replacing username-string authorisation | [NEW] | Medium |
| 5 | **F12** · Append-only audit trail for money and privileged operations | [NEW] | Medium |
| 6 | **F14** · Atomic, recoverable file writes with backup | [NEW] | Medium |
| 7 | **F15** · Transaction receipt and account statement | [NEW] | Medium |
| 8 | **G1** · Product differentiation by account type (interest accrual) | [NEW] | Complex |
| 9 | **G4** · Transaction and balance limits | [NEW] | Medium |
| 10 | **G7** · Multiple accounts per customer | [NEW] | Complex |

**F8+F10 merged.** Both address one capability — protecting the credential —
split only by where the exposure occurs. At rest: written verbatim to
`Users.txt` and `customers.txt` (`DL/MUserDL.cs:84`, `DL/AdminDL.cs:96,105`) and
compared by string equality (`DL/MUserDL.cs:34`, `DL/AdminDL.cs:31`). On screen:
the admin grid auto-generates a `Password` column from the bound `Admin` object
(`ViewCustomer.cs:34`, `BL/Admin.cs:23`), and the value is rendered into plain
labels on the customer's own profile (`CustomerHome.cs:29`) and in search results
(`SearchResult.cs:28`), with no `UseSystemPasswordChar` on the Add/Edit password
boxes (`AddUser.Designer.cs:146-152`, `EditCustomer.Designer.cs:107-112`) —
only the login box has it (`Form1.Designer.cs:320`). Treating them separately
would have shipped a slate where hashing "fixes" a problem that remains fully
visible on the operator's screen; treating them as one makes the completeness of
the fix the point. *Mitigates #3 in both limbs.*

**Composition.** 9 `[NEW]` · 1 `[INCOMPLETE]` · **0 `[FIX]`** — Task 2 no longer
restates the Task-3 catalogue. F1 is the sole overlap (S25), kept because a
banking system with no funds check is the first capability a reviewer looks for;
the overlap is disclosed rather than hidden. Complexity: 7 Medium, 3 Complex,
0 Simple.

**Still open:** the Medium pick for Task 4, against criteria (i)–(iv). Seven
Medium candidates survive the prune, so criterion (iv) — *mitigates a risk that
appears in the Task-3 ranking* — is still doing real work and cannot be applied
until the five smells are ranked.

---

## Session 2 · Phase 3b — Code smell catalogue (independent pass + convergence)

> **Unranked by design.** Grouped by category; order within and between groups is
> arbitrary and carries no severity meaning. No "five most critical" selection is
> made here — that ranking is the human's (PLAN §2 guardrail 1, PLAN §3b), and
> the Fable adversarial challenge runs against *his* ranking, not this list.

### Method

A second Opus session was given the source and **explicitly forbidden to read
`ANALYSIS_LOG.md` or `PREDICTIONS.md`** — no read, no grep, no search — and was
asked to confirm in its report whether the restriction held. It did. The pass is
therefore independent of the Phase-1 analysis, which makes the overlap between
the two lists a genuine convergence measurement rather than an echo.

It produced **46 findings**, all with `file:line`, static-only (the app was not
run). It self-reported "41" with a category breakdown summing to 45; both tallies
are wrong and the file contains 46. Nothing was missing — a miscount, not a gap.

### Legend

| Tag | Meaning |
|---|---|
| **[E]** | Backed by Phase-2 execution — an observed `.txt` diff or a performed action |
| **[S]** | Static reading only; no execution claimed |
| **[X]** | The detecting session marked it as needing execution to confirm |
| **→ #n / An** | Converges with §5 risk register item *n* / audit finding *An* |
| **[NEW]** | No counterpart in the §5 register or A1–A4 |

### Convergence result

The independent pass **reproduced all 18 register risks and all four audit
findings A1–A4**, with one exception:

- **#16 has no 3b counterpart for its "no logging, no audit trail" limb.** The
  pass audited error handling in depth (5 findings) but never flagged the absence
  of logging. Worth carrying into the report: an absence-of-an-entire-subsystem
  finding is what a code-reading pass is structurally weakest at — you cannot
  cite a `file:line` for something that was never written.

It added **17 findings with no register counterpart**, plus one (S6, transfer
settlement) that probe 4 observed but §5 never numbered.

### Verification of the new claims (performed in this session)

The load-bearing new findings were checked against the source before being
recorded here:

- **S3** — confirmed. `calculateReceivedMoney` has no owner guard
  (`DL/CustomerDL.cs:246`) where its sibling has one (`:195`). **Mechanism
  corrected:** `readSendHistory` *does* filter by account number at read time
  (`DL/CustomerDL.cs:139`), so the list is not unfiltered in general. The defect
  is that this sum alone lacks the second-line `UserName` guard, so stale rows
  left by a previous login — the lists are never cleared — are counted here and
  excluded by the other three. Same consequence, more precise cause.
- **S7** — confirmed. `editCustomerData` copies eight fields, never `City`
  (`DL/AdminDL.cs:126-133`).
- **S8** — confirmed. `if (intialDeposit<1999)` against a "2000 or more" message
  (`AddUser.cs:83`).
- **S39** — confirmed. `Customer.ReceivedMoney` (`BL/Customer.cs:31`) has no
  assignment anywhere in the solution.
- **S26** — confirmed. The repo root holds only the five history files; no
  `customers.txt` or `Users.txt`, so the startup-crash-from-root claim stands.

---

## correctness

**S1 · Withdrawal credits the account instead of debiting it** — `WithDrawMoneyCus.cs:29` · **[E] → #4**
`A.TotalMoney = A.TotalMoney + money;` — identical to the deposit handler at
`DepositMoneyCus.cs:60`. *Misbehaves today.* Every withdrawal inflates the
balance on the account screen (`CustomerHome.cs:35`), the admin grid and the
bank-wide total (`TotalMoneyInBank.cs:36`), and the wrong value is persisted at
logout (`CusForm.cs:138`). *Fix:* change `+` to `-`.

**S2 · Available balance omits the initial deposit** — `DL/CustomerDL.cs:253-259`, `BalanceDetailsCus.cs:44,84-85` · **[E] → A2**
`totalMoney()` returns `deposit + recieve - transact - withdraw` and never adds
`AdminDL.Current.IntialDeposit`, though the same screen displays the initial
deposit one line earlier as its own field. *Misbehaves today.* A customer with a
2000 opening deposit and no transactions sees an available balance of 0; every
customer's figure is understated by exactly their opening deposit. *Fix:* add
`IntialDeposit` as a term (or pass it in).

**S3 · `calculateReceivedMoney` has no owner filter** — `DL/CustomerDL.cs:241-252` · **[S] [NEW]**
The three sibling methods (`:195`, `:214`, `:231`) each guard with
`if (AdminDL.Current.UserName == C.UserName)`; this one sums every entry in the
static `receivedMoneyList` unconditionally, and the list is never cleared on
logout. *Misbehaves today.* Log in as A, open Balance Details, log out, log in as
B, open Balance Details — B's "received" total includes A's incoming transfers.
*(Mechanism refined in this session: `readSendHistory` filters by account number
at read time, so the exposure is specifically to rows left over from a previous
login within one process.)* *Fix:* add the owner guard inside the loop and clear
the `CustomerDL` statics on logout.

**S4 · Balance screen re-reads history files without clearing the lists** — `BalanceDetailsCus.cs:40-43` · **[E] → A1**
The four `read*History` methods *append* to the static lists (e.g.
`CustomerDL.cs:80`). Every other consumer clears first (`DepositHistory.cs:33`,
`WithDrawHistory.cs:28`, `TransactHistory.cs:28`, `ReceivedMoney.cs:33`); this
one does not. `DepositMoneyCus.cs:63` and `WithDrawMoneyCus.cs:32` additionally
add the in-memory record on top of the file write. *Misbehaves today.* **Trigger
is per *login*, not per visit** — the independent pass restated the "every visit"
version, which probe 2 disproved (`:652-659`): `btnBalanceDetails_Click` only
calls `Show()`/`BringToFront()` (`CusForm.cs:109-120`) and cannot re-fire `Load`
on an existing control, so the re-read runs once per `CustomerWindowPAge`
instance and the static lists (`DL/CustomerDL.cs:13-17`) accumulate a second copy
on the next login in the same process. Probe 1 observed 9000 → 11000 from a
login and logout with **zero clicks**. *Fix:* clear the four lists at the top of
`BalanceDetailsCus_Load`.

**S5 · Balance calculators mutate persisted state as a side effect** — `DL/CustomerDL.cs:198,217,234,248` · **[E] → #12**
Methods named `calculate*` also write
`AdminDL.Current.TotalMoney = AdminDL.Current.TotalMoney ± C.<amount>`. They are
invoked from `btnRefresh_Click` (`BalanceDetailsCus.cs:76-83`), itself called on
every form load (`:50`) and every Refresh click. *Misbehaves today.* Each press
re-applies the entire transaction history to the stored balance, and
`AdminDL.Current` is the same object held in `CustomersList`, so the inflated
figure reaches `customers.txt` at logout (`CusForm.cs:138`). *Fix:* delete the
four assignments; make the calculators pure.

**S6 · Transfer never debits the sender or credits the recipient balance** — `TransactMoneyCus.cs:36-73` · **[E] → probe 4 (not numbered in §5)**
The handler writes two history rows and adds to the in-memory `TransactList`, but
never touches `AdminDL.Current.TotalMoney` nor the recipient `Admin` found in the
loop at `:42-49` — the loop sets a `check` flag and discards the match.
*Misbehaves today.* `Admin.TotalMoney` — shown on My Account Details, the admin
grid and Total Money In Bank — is unaffected by transfers in either direction, so
it permanently diverges from the history-derived figure. *Fix:* capture the
matched recipient and apply `-amount` to sender, `+amount` to recipient before
writing.

**S7 · `editCustomerData` silently drops the City field** — `DL/AdminDL.cs:120-137` · **[S] [NEW]**
The method copies Name, UserName, Password, AccountNumber, AccountType,
PhoneNumber, IntialDeposit and TotalMoney from `update`, but never
`A.City = update.City;` — even though `EditCustomer.cs:54` passes `cmbCity.Text`
into the `update` object. *Misbehaves today.* Editing a city appears to succeed
and closes the dialog, but the change is discarded; City search
(`CitySearch.cs:31`) keeps matching the old value. *Fix:* add the missing
assignment.

**S8 · Initial-deposit minimum is off by one** — `AddUser.cs:83-86` · **[S] [NEW]**
`if (intialDeposit<1999)` rejects, with the message "Deposit Money Should be More
than 2000 or More". 1999 and anything in `[1999, 2000)` is accepted.
*Misbehaves today.* Accounts open below the stated minimum. *Fix:* `< 2000`.

**S9 · Copy-pasted navigation handler misses one Hide call** — `CusForm.cs:96-107` · **[X] [NEW]**
Every sibling navigation handler hides all seven panels;
`btnMyAccountDetails_Click` omits `giveFeedback1.Hide();` (compare `:63`, `:76`,
`:89`, `:115`). *Misbehaves today* on the Feedback → My Account Details path —
the feedback panel stays visible behind the account panel. Marked by the
detecting session as needing execution for the exact visual result. *Fix:* add
the missing `Hide()`.

**S10 · `RemoveAt` inside a forward loop without index adjustment** — `DL/AdminDL.cs:110-119`, `DL/MUserDL.cs:98-108` · **[S] → #14**
Both loops call `RemoveAt(i)` then `i++`, skipping the element that shifts into
position `i`, and neither breaks after a match. *Latent.* With two records
matching the same name/username/password — nothing enforces uniqueness on the
edit path (S18) — only one is removed, leaving an orphaned credential in
`Users.txt` or an orphaned row in `customers.txt`. *Fix:* decrement `i` after
`RemoveAt`, or iterate backwards.

## data-integrity

**S11 · `storeCustomer` and `storeAllCustomers` write different record schemas** — `DL/AdminDL.cs:96,105,80-85` · **[E] → #9**
`storeCustomer` emits `... + A.IntialDeposit + "," + A.IntialDeposit + "," +
A.TotalMoney` — ten fields, initial deposit duplicated. `storeAllCustomers` emits
nine, and `read_data` reads `TotalMoney` from field 9. *Latent.* A row written by
the Add User path reads back with `TotalMoney = IntialDeposit` and the real tenth
field dropped. Harmless only because a new account has `TotalMoney ==
IntialDeposit`; any change making them differ at creation loses money silently.
*Fix:* remove the duplicated term.

**S12 · Transfer writes two files non-atomically** — `TransactMoneyCus.cs:62-63` · **[E, window only] → #6**
`storeTransactHistory` (sender's debit) and `storeSendMoney` (recipient's credit)
are two separate open/write/close cycles with no transaction, rollback or
journal. *Latent.* If the second write fails — disk full, file locked, process
killed between them — the sender is debited on the Balance Details calculation
and the recipient is never credited. *Fix:* write both through one method that
undoes the first append on failure, or write a combined journal record.

**S13 · Balances are persisted only when the Log Out button is pressed** — `CusForm.cs:135-142,218-222` · **[S] → #7**
`storeAllCustomers` runs only in `btnCusLogOut_Click`. The window-close icon calls
`Application.Exit()` with no save, and there is no `FormClosing` handler.
*Misbehaves today.* A customer who deposits and then closes with the X loses the
`TotalMoney` update entirely — the history files keep the deposit line, so the
two balance views diverge further. *Fix:* move the save into `FormClosing`.

**S14 · CSV fields are never escaped or validated for the delimiter** — `DL/AdminDL.cs:96,105`, `DL/CustomerDL.cs:28,35,42,50,58`, `DL/MUserDL.cs:84,93`, `AddUser.cs:48-61` · **[E] → #8**
Every writer does raw `a + "," + b + ...` on unvalidated `TextBox` content.
*Misbehaves today — executed in probe 5b (5/5 predictions exact).* The detecting
session called this "latent but trivially reachable"; execution supersedes that.
A single comma in Name
shifts `AccountNumber`, `IntialDeposit` and `TotalMoney` one field left;
`read_data` then parses a phone number as an account number and a balance from
the wrong column. *Fix:* reject `,` in the Add/Edit text fields, or escape
properly (see F4).

**S15 · Feedback is a `RichTextBox` written as a single CSV line** — `GiveFeedback.cs:26-27`, `DL/CustomerDL.cs:58`, `GiveFeedback.Designer.cs:36` · **[S] [NEW]**
`txtFeedBack` is a `RichTextBox`, so `.Text` may contain newlines; `storeFeedBack`
writes `UserName + "," + FeedBack` via `WriteLine`. No empty-input check either.
*Misbehaves today for any multi-line feedback.* Each embedded newline becomes a
new record; `readFeedBacks` (`CustomerDL.cs:156-170`) renders continuation lines
as separate entries attributed to a garbage username. *Fix:* replace newlines
before constructing the `Customer`. *(Register #8 covered commas in free text,
not newlines.)*

**S16 · Transaction dates are user-chosen localized display strings** — `DepositMoneyCus.cs:59`, `WithDrawMoneyCus.cs:28`, `TransactMoneyCus.cs:58`, `DL/CustomerDL.cs:78,99,122,145` · **[S] → #17**
The date comes from `DateTimePicker.Text`, so the customer picks it, and it is
stored as e.g. `Thursday, 2 June 2022` — which contains a comma, forcing the
reader hack `date = parse_data(record,3) + parse_data(record,4)` at four sites.
No time component. *Misbehaves today:* history cannot be ordered or filtered,
same-day transactions are indistinguishable, and any transaction can be
backdated. *New on top of #17:* a machine with a different culture/long-date
format produces a different comma count and mis-parses every subsequent field.
*Fix:* store `DateTime.Now.ToString("o")`.

**S17 · Deleting a customer leaves their history and a reusable account number behind** — `ViewCustomer.cs:105-108` · **[S] → A4 (mechanism [NEW])**
Delete removes the row from `customers.txt` and `Users.txt` but touches none of
the five history files. Account numbers are hand-entered (`AddUser.cs:70-81`) and
uniqueness is checked only against currently-live accounts. *Latent.* Re-issuing
a freed account number makes `readSendHistory` — which matches on account number
(`CustomerDL.cs:139`) — credit the **new** customer with the deleted customer's
incoming transfers. *(A4 observed the orphan rows already present in the shipped
data; this supplies the mechanism by which they become someone else's money.)*
*Fix:* never reuse account numbers.

**S18 · Edit Customer skips the uniqueness checks the Add path enforces** — `EditCustomer.cs:44-59` vs `AddUser.cs:51-81` · **[S] → A3**
Add User rejects a duplicate username, phone and account number; the edit dialog
validates only the account-number range and lets `editCustomerData` overwrite
anything. *Latent.* An edit can produce two accounts with the same account
number — `TransactMoneyCus.cs:42-49` then accepts the transfer and
`readSendHistory` credits both — or two identical usernames, making `setCurrent`
bind the wrong account at login. *Fix:* extract the three uniqueness loops into a
shared validator (see F6).

**S19 · Money and account numbers are `double`** — `BL/Admin.cs:17-19`, `BL/Customer.cs:12-19`, `AddUser.cs:70`, `AccountNumberSearch.cs:26` · **[S] → #11**
Balances use binary floating point, and `accountNumber` — an identifier, never
arithmetic — is also a `double`. `AddUser.cs:71` range-checks `100000..999999`
but does not reject a fractional value, and account-number search compares
`txtSearchAccountNumber.Text == A.AccountNumber.ToString()`. *Latent.* Repeated
`+`/`-` accumulates representation error; `123456.5` passes validation;
scientific-notation or trailing-`.0` formatting makes the string compare miss.
*Fix:* `decimal` for money, `string`/`int` for account number.

## security

**S20 · Hard-coded admin credentials** — `DL/MUserDL.cs:26-31` · **[E] → #1**
`if ((user.UserName == "Admin" || user.UserName == "admin") && user.Password ==
"1234") return user;` — a back door compiled into the binary, checked before the
real user list and unchangeable at runtime. *Misbehaves today.* Anyone with the
binary or this source has full admin access: all balances, all plaintext
passwords, arbitrary balance edits, account deletion. *Fix:* move the admin
account into `Users.txt` with a hashed password and delete the literal check.

**S21 · Admin authorization is decided by username string alone** — `DL/MUserDL.cs:42-49`, `Form1.cs:66-73` · **[E] → #2**
`isAdmin` returns true for any `MUser` named `Admin`/`admin`, regardless of how it
authenticated. `checkuser` will authenticate a *customer* named `admin` against
that customer's own password (`MUserDL.cs:32-39`), and `AddUser` does not reserve
the name. *Latent privilege escalation, reachable in two clicks* — register a
customer named `admin` with any password, log in, land in `AdminWindow` with full
rights. *Fix:* store a role on the record and have `isAdmin` read it.

**S22 · Passwords stored, displayed and searched in plaintext** — `DL/MUserDL.cs:84,93`, `DL/AdminDL.cs:96,105`, `CustomerHome.cs:29`, `SearchResult.cs:28`, `EditCustomer.cs:29`, `ViewCustomer.cs:34` · **[E] → #3**
Passwords are written verbatim and rendered on screen: the customer's own account
panel, every admin search result, the edit dialog, and the admin `DataGridView`,
which binds the whole `Admin` object including `Password`. `bin/Debug/Users.txt`
and `customers.txt` ship in the repo with real-looking values. *Misbehaves
today.* *Fix:* salted hash + hash comparison in `checkuser`; bind an explicit
projection (as `AdminFeedback.cs:31` already does) and drop the password labels.

**S23 · Password entry is unmasked outside the login screen** — `AddUser.Designer.cs`, `EditCustomer.Designer.cs`; contrast `Form1.Designer.cs:320` · **[S] [NEW]**
The login form sets `UseSystemPasswordChar = true`; Add User and Edit Customer do
not, so the password is typed and displayed in clear. *Latent* —
shoulder-surfing / screen-share exposure during account creation and edit.
*Fix:* set the property on both. *(Register #3 covered storage, not entry.)*

**S24 · `setCurrent` leaves the previous customer bound when no match is found** — `DL/AdminDL.cs:27-36`, `Form1.cs:76-79` · **[E, probe 5b] → #18**
`setCurrent` assigns `current` only inside the match branch; on no match it
silently leaves the static at whatever it was, and `Form1` does not check the
outcome before opening the customer window. `Current` is never reset on logout.
*Latent.* `Users.txt` and `customers.txt` are maintained by separate code paths,
so a credential present in one and absent from the other authenticates and then
operates on the *previously logged-in* customer's account — deposits,
withdrawals and transfers all hit the wrong account. *Fix:* return `bool`, set
`current = null` on failure, abort the login.

**S25 · No validation on transfer target or amount** — `TransactMoneyCus.cs:41-57`, `WithDrawMoneyCus.cs:26`, `DepositMoneyCus.cs:57` · **[E] → #5**
The only validation is that the destination account exists. No overdraft check,
no rejection of zero or negative amounts, no check that the destination is not
the sender's own account. *Misbehaves today.* A customer can withdraw or transfer
arbitrarily more than they hold, or enter a negative amount — a negative
withdrawal credits the account and a negative transfer drains the recipient.
*Fix:* amount and balance guards at the top of all three handlers (see F1).

## error-handling

**S26 · File reads run unguarded in the login form's constructor** — `Form1.cs:18-26` · **[S] → #10 (+[NEW] limb)**
`AdminDL.read_data("customers.txt")` and `MUserDL.read_data("Users.txt")` execute
in the constructor, before `InitializeComponent()`, with no existence check and
no `try`/`catch`. *Misbehaves today when the CWD lacks the files.* **Verified in
this session:** the repo root holds only the five history files — no
`customers.txt`, no `Users.txt` — so launching with the repo root as working
directory throws `FileNotFoundException` out of the constructor and the app dies
before showing a window. *Fix:* guard with `File.Exists` and move into
`Form1_Load`.

**S27 · `double.Parse` on unconditionally-read fields crashes on blank lines** — `DL/CustomerDL.cs:138`, `DL/AdminDL.cs:73` · **[S] → #10 (sharpened)**
`parse_data` returns `""` for a missing field (`CustomerDL.cs:171-187`), and
`readSendHistory` parses field 1 before any guard. The other readers compare a
*string* field first, so they tolerate blank lines. `AdminDL.read_data` defends
against empty numeric fields (`:68-85`) but not garbage non-numeric ones.
*Latent, and the shipped data already sets it up* — `depositHistory.txt` at the
repo root contains a blank line. A blank line in `sendMoneyPath.txt` throws
`FormatException` out of `BalanceDetailsCus_Load` (`:43`), which has no
`try`/`catch`. *Fix:* skip whitespace records; use `TryParse`.

**S28 · Streams are opened without `using` and closed only on the success path** — `DL/CustomerDL.cs:27,34,41,49,57,70,91,112,135,159`; `DL/AdminDL.cs:58,95,102`; `DL/MUserDL.cs:70,83,90` · **[S] [NEW]**
Every reader and writer is `new StreamReader/Writer(...)` followed by a bare
`.Close()` at the end of the method — no `using`, no `try`/`finally`. *Latent.*
Any exception mid-method (a `FormatException` from S27, an IO error mid-write)
leaves the handle open for the process lifetime, so the file stays locked and
every later read or write fails — **including the `storeAllCustomers` at logout,
which then silently loses the session's balances**. *Fix:* wrap each stream in a
`using`. *(Chains into #7 / S13: a second, non-obvious path to silent balance
loss.)*

**S29 · Exceptions used for input validation, then swallowed into a message box** — `TransactMoneyCus.cs:50-53,69-72`; `AddUser.cs:55,66,73,79,85,97-100`; `DepositMoneyCus.cs:67-70`; `WithDrawMoneyCus.cs:36-39`; `EditCustomer.cs:60-63` · **[S] → #16 (error-handling limb)**
Validation failures are raised as bare `throw new Exception("...")` and every
handler ends in `catch (Exception error) { MessageBox.Show(error.Message); }`,
which cannot distinguish a validation message from an IO failure or a
`NullReferenceException`. *Misbehaves today.* A non-numeric amount shows the raw
framework text "Input string was not in a correct format."; a genuine disk
failure during `storeTransactHistory` is reported as an ordinary dialog and looks
like a validation mistake rather than a lost transaction. *Fix:* `TryParse` +
explicit messages; narrow the `catch`.

**S30 · Grid and data-bound reads with no error handling at all** — `GiveFeedback.cs:24-30`, `AdminFeedback.cs:26-34`, `DepositHistory.cs:31-38`, `WithDrawHistory.cs:26-33`, `TransactHistory.cs:26-33`, `ReceivedMoney.cs:31-42`, `BalanceDetailsCus.cs:38-51` · **[S] [NEW]**
Each opens or writes a file with no `try`/`catch` and no existence check, unlike
the money-entry forms. *Latent* — a missing or locked history file throws an
unhandled exception out of a `Load` handler and terminates the application rather
than showing an empty grid. *Fix:* existence guard inside a `try`/`catch`.

## duplication

**S31 · `parse_data` copy-pasted verbatim into all three DL classes** — `DL/AdminDL.cs:37-53`, `DL/CustomerDL.cs:171-187`, `DL/MUserDL.cs:50-66` · **[E] → #8**
Three byte-identical CSV field extractors. The implementation also cannot
represent a field containing the delimiter: `if (record[i] == ',') comma++;`
increments unconditionally. *Latent.* Any delimiter fix (needed by S14 and S16)
must be applied in three places; missing one leaves a silently divergent parser
on some read paths. *Fix:* one shared static helper.

**S32 · Five search user-controls with the same body** — `NameSearch.cs:41-59`, `UserNameSearch.cs:27-45`, `CitySearch.cs:26-44`, `PhoneNumberSearch.cs:24-41`, `AccountNumberSearch.cs:21-39` · **[S] [NEW]**
Identical loop, `check` flag, `SearchResult` dialog and "not found" box in five
files, differing only in the compared property. All five open a modal
`SearchResult` *inside* the loop, so N matches means N stacked dialogs. *Latent.*
Any change — case-insensitive matching, or suppressing the password on the result
screen (S22) — must be made five times. *Fix:* one
`SearchBy(Func<Admin,string>, string)` helper; collect matches, then show once.

**S33 · Five near-identical `store*` writers and five near-identical `read*` readers** — `DL/CustomerDL.cs:25-61`, `:67-170` · **[S] [NEW]**
Each writer is the same three lines with a different concatenation; each reader
the same `while ((record = file.ReadLine()) != null)` loop with a different field
list and an inline owner filter. *Latent — and self-demonstrating:* this is
exactly why the missing owner filter in S3 and the missing `Clear()` in S4 were
never caught, because the copies drifted. *Fix:* extract `AppendLine(path, csv)`
and `ReadRecords(path, map, keep)`.

**S34 · Two divergent implementations of "what is this customer's balance"** — `DL/CustomerDL.cs:253-259` vs `DL/AdminDL.cs:138-147` / `CustomerHome.cs:35` · **[E] → A2**
Balance Details derives the balance from the history files; My Account Details,
the admin grid and Total Money In Bank read the separately-maintained
`Admin.TotalMoney`. Nothing reconciles them, and the mutation sites for
`TotalMoney` are incomplete (no transfer, wrong sign on withdrawal).
*Misbehaves today.* The same customer sees two different balances on two screens
of the same application. *Fix:* make `TotalMoney` computed from the history-based
calculation; stop writing to it from the forms.

**S35 · `SearchCustomer` dropdown handler exists twice** — `SearchCustomer.cs:20-66` and `:87-132` (only `_1` wired, `SearchCustomer.Designer.cs:95`) · **[S] [NEW]**
Two byte-identical `SelectedIndexChanged` handlers. Both also fail to hide every
sibling panel — the "Name" branch (`:22-28`, `:89-95`) never hides
`phoneNumberSearch1` or `accountNumberSearch1`, and the "City" branch calls
`citySearch1.Hide()` immediately followed by `citySearch1.Show()` (`:43-44`,
`:110-111`). *Latent for the duplication; misbehaves today for the missing Hide
calls* — selecting "Name" after "PhoneNumber" leaves the phone panel on screen.
*Fix:* delete the unwired copy; hide all five panels before showing one.

## dead-code

**S36 · Empty method body left in the persistence layer** — `DL/CustomerDL.cs:63-66` · **[S] → #15**
`public static void storeAllDepositHistory(string path)` has an empty body and no
callers, but its name implies a rewrite-all counterpart to `storeAllCustomers`.
*Latent* — a future caller expecting persistence gets a silent no-op. *Fix:*
delete.

**S37 · Unwired duplicate event handlers** — `AddUser.cs:24-28` (superseded by `btnAdd_Click_1`, `AddUser.Designer.cs:361`), `NameSearch.cs:36-39`, `AdminWindow.cs:29-34` · **[S] → #15**
Empty or orphaned `_Click` bodies beside the real ones.
`btnAddNewAccount_Click` additionally does `new AddUser().Show()` on a
`UserControl` never added to a container — a no-op even if wired. *Latent* —
reading the file, the empty `btnAdd_Click` looks like the Add button does
nothing, and re-wiring the designer to the wrong overload silently disables the
feature. *Fix:* delete all three.

**S38 · Scaffold forms and controls with no logic and no references** — `New.cs`, `practice.cs`, `NewCustomerForm.cs`, `NewCustomerWindow.cs`, `HomeScreenCustomer.cs`, `UserControl1.cs` · **[S] → #15**
Six designer-generated shells never instantiated anywhere in the solution
(verified by searching for `new <Type>(` across all `.cs`), still carrying
designer files and compiled into the binary. *Latent* — no runtime effect, but
they inflate the surface a reviewer must read, and one (`practice`) is explicitly
throwaway. *Fix:* remove the six file sets and their `<Compile>` entries.

**S39 · Unused fields and redundant accessors** — `CusForm.cs:18,30`, `AdminWindow.cs:39`, `DL/MUserDL.cs:22-25`, `DL/AdminDL.cs:23-26`, `BL/Customer.cs:31` · **[S] [NEW]**
Members written-but-never-read, unreachable, or shadowed: `private Admin A`
assigned at `CusForm.cs:23` and never read; `private Form currentForm = null`
never used; `getUsersList()` an *instance* method on a class only ever used
statically, so uncallable; `getCustomerList()` duplicating the `CustomersList`
property. *Latent defect specifically for `Customer.ReceivedMoney`* — it reads as
the natural property for a received amount and always returns 0, so any new
consumer binding to it silently shows zeros (`ReceivedMoney.cs:36` correctly
binds `TransactMoney` instead). **Verified in this session: no assignment to
`ReceivedMoney` exists anywhere.** *Fix:* delete the unused members.

**S40 · Commented-out logic left inline** — `DL/CustomerDL.cs:191,204,210,221,227,238,244,250`; `BalanceDetailsCus.cs:45-49` · **[S] → #15**
Each `calculate*` carries a commented-out `read*History(...)` at the top and a
commented-out `list.Clear()` at the bottom — the exact two operations whose
absence produces S4's double-counting. `BalanceDetailsCus` has a commented-out
copy of the whole refresh block it then calls at `:50`. *Latent, but misleading
in a specific way:* a reader concludes the clearing is handled and stops looking.
*Fix:* delete the commented blocks after fixing S4 properly.

## design

**S41 · All persistence and business logic is static, with static mutable session state** — `DL/AdminDL.cs:13-17`, `DL/CustomerDL.cs:13-23`, `DL/MUserDL.cs:14-16` · **[E] → #13**
`AdminDL.Current`, `AdminDL.CustomersList` and the five `CustomerDL` lists are
`static` and publicly settable. The logged-in identity is ambient global state
every screen reads directly (`CustomerHome.cs:27-35`, `CustomerDL.cs:74,95,116,
139`), and lifecycle is managed by whoever remembers to `Clear()`. *Latent, but
the direct enabler of two defects already listed* (S3's stale list across logins,
S4's un-cleared lists). No method can be exercised without mutating process-wide
state. *Fix:* instance classes with an injected session/repository, starting with
`Current`.

**S42 · File paths are hard-coded relative strings duplicated across 16 sites** — `DepositMoneyCus.cs:17`, `WithDrawMoneyCus.cs:16`, `TransactMoneyCus.cs:18-19`, `GiveFeedback.cs:17`, `AddUser.cs:17-18`, `Form1.cs:22-23`, `BalanceDetailsCus.cs:40-43`, `EditCustomer.cs:57`, `ViewCustomer.cs:107-108,115`, `CusForm.cs:138`, `DepositHistory.cs:34`, `WithDrawHistory.cs:29`, `TransactHistory.cs:29`, `ReceivedMoney.cs:34`, `AdminFeedback.cs:29` · **[S] → §4 / A6 (partial)**
The same seven filenames appear as bare relative literals in sixteen places,
resolved against the process working directory. The repo already contains two
divergent copies of the data set — `bin/Debug/*.txt` and the repo root, with
different contents (root `transactHistory.txt` has 8 rows, `bin/Debug` copy is
empty). *Misbehaves today in the sense that which database the app uses depends
on how it was launched;* a shortcut with a different "Start in" silently reads and
writes a different, empty data set. *Fix:* centralize as
`Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ...)` constants.

**S43 · `Customer` overloads distinguished only by parameter order** — `BL/Customer.cs:33-39` vs `:40-46`, `:47-54` vs `:55-62` · **[S] [NEW]**
`Customer(string, double, string)` builds a deposit record while
`Customer(string, string, double)` builds a withdrawal; `Customer(string, double,
string, double, string)` builds a sent record while `Customer(double, double,
string, double, string)` builds a received one. Selection is purely positional
and callers rely on it (`WithDrawMoneyCus.cs:30`, `CustomerDL.cs:100,146`,
`TransactMoneyCus.cs:59-60`). *Latent.* Swapping two arguments at a call site
silently binds a different overload and writes the amount into the wrong ledger
field — the compiler cannot catch it. *Fix:* named static factories
(`Customer.Deposit(...)`, `.WithDrawal(...)`, `.Sent(...)`, `.Received(...)`).

**S44 · Screens call the persistence layer directly** — `DepositMoneyCus.cs:57-63`, `WithDrawMoneyCus.cs:26-32`, `TransactMoneyCus.cs:41-63`, `AddUser.cs:88-93`, `ViewCustomer.cs:105-108` · **[S] [NEW]**
There is no service layer: `Click` handlers parse text, apply the balance
arithmetic, build the domain object, append to the in-memory list *and* write the
file. `BL/` holds only anemic data holders. *Latent — and structurally
explanatory:* this is why the withdrawal sign error (S1), the missing overdraft
check (S25) and the missing transfer settlement (S6) each live in a different
form file rather than in one place. The money rules exist only inside UI event
handlers and cannot be reused or checked centrally. *Fix:* extract a
`TransactionService` with `Deposit/Withdraw/Transfer`.

**S45 · Unguarded cast of the grid's bound item before the column check** — `ViewCustomer.cs:102-103` · **[X] [NEW]**
`Admin A = (Admin)usersGV.CurrentRow.DataBoundItem;` runs on every content click,
before the code decides whether Edit or Delete was hit. When the grid is bound to
an anonymous-type projection or a sorted copy (`:81`, `:88`, `:95`),
`CurrentRow` may not hold an `Admin`. *Latent* —
`NullReferenceException`/`InvalidCastException` on a click in an unexpected
state; marked by the detecting session as needing execution. *Fix:* move the cast
inside the column branches; use `as` with a null check.

## testability

**S46 · No test project, and no seam to add one** — `BMS WinForm.csproj` (single `WinExe`, no test project in the solution), `DL/CustomerDL.cs:253-259`, `DL/AdminDL.cs:138-147` · **[S] [NEW]**
Every money rule is either inside a `Click` handler or inside a `static` method
that reads `AdminDL.Current` and opens files by relative path.
`CustomerDL.totalMoney` is the only pure function in the money path, and it is
`static` on an internal class with no coverage. *Latent* — S1 and S2 are both
one-line arithmetic errors a single unit test over `totalMoney` would have
caught. *Fix:* add a test project; make `totalMoney` (and the `calculate*`
methods, once pure and parameterized on the lists) reachable from tests. *(See
audit finding A5: the DL types are `internal`, so this needs `InternalsVisibleTo`
or a visibility change — a Phase-4 decision.)*

---

### Distribution (for the human's reference — not an ordering)

| Category | Count | Executed evidence | Static only |
|---|---|---|---|
| correctness | 10 | S1, S2, S4, S5, S6 | S3, S7, S8, S9, S10 |
| data-integrity | 9 | S11, S12, S14 | S13, S15, S16, S17, S18, S19 |
| security | 6 | S20, S21, S22, S24, S25 | S23 |
| error-handling | 5 | — | S26–S30 |
| duplication | 5 | S31, S34 | S32, S33, S35 |
| dead-code | 5 | — | S36–S40 |
| design | 5 | S41 | S42–S45 |
| testability | 1 | — | S46 |
| **total** | **46** | **17** | **29** |

### What Task 3 needs next (human)

1. Select the five most critical **by business risk**, not by category or by
   count of citations. The catalogue above is deliberately flat.
2. Then the Fable adversarial pass: *"argue the strongest case that I ranked
   these wrong."* Keep or revise, and log why.

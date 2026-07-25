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

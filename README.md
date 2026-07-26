# Banking_Management_System_OOP_Winforms
A banking management system built in C# Winforms implementing OOP concepts.

---

## Assessment deliverables

This fork adds an analysis of the application above. The original code is
unchanged except where an improvement is explicitly documented.

| Document | What it is |
|---|---|
| [`docs/FINDINGS.md`](docs/FINDINGS.md) | Code smells and engineering risks — the five most critical ranked by business risk, plus the full 49-finding catalogue |
| [`docs/CAPABILITIES.md`](docs/CAPABILITIES.md) | Missing and incomplete functionality — the slate of 10, and what was rejected |
| [`docs/ANALYSIS_LOG.md`](docs/ANALYSIS_LOG.md) | The append-only audit trail: AI passes, cross-model validation, executed probes, superseded positions, attribution |
| [`docs/PREDICTION.md`](docs/PREDICTION.md) | Independent hypotheses, committed **before** any AI was run |
| [`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) | Implementation contract for the one capability that was built |

`FINDINGS.md` and `CAPABILITIES.md` are current-state documents and are kept
up to date. `ANALYSIS_LOG.md` is never rewritten — it records what happened and
when, including positions later revised.

---

## Build

```
dotnet build "BMS WinForm.sln"
```

Output: `BMS WinForm/bin/Debug/BMS WinForm.exe`. A prebuilt copy is committed, so
the app also runs with no build at all.

`dotnet build`, not `dotnet msbuild` — the solution now also contains the SDK-style
test project, which needs a NuGet restore that `dotnet msbuild` does not perform.
`dotnet msbuild "BMS WinForm/BMS WinForm.csproj"` still builds the app alone.

**Run it from `BMS WinForm/bin/Debug/`.** Every data path in the application is a
bare relative literal, so the working directory decides which dataset is used —
and the sample data lives in that folder.

### Known build constraint (pre-existing)

A **clean** build fails on this toolchain:

```
error MSB3823: Non-string resources require the property GenerateResourceUsePreserializedResources
error MSB3822: Non-string resources require the System.Resources.Extensions assembly at runtime
```

The SDK's MSBuild cannot serialize the images embedded in the `.resx` files, and
the .NET Framework MSBuild that could is too old to parse the C# 6 syntax already
in `BL/` and `DL/`. The build above works because `obj/Debug/*.resources` are
**committed**, so `GenerateResource` is skipped as up-to-date. Deleting
`BMS WinForm/obj/` breaks the build until Visual Studio or Build Tools is
installed. (`tests/BMS.Tests/obj/` is safe to delete — it is restored on demand.)

This predates the audit-trail change and was left alone rather than "fixed" by
adding a NuGet dependency to the shipped executable. It is why the test project
references the app with `SkipGetTargetFrameworkProperties` — see the comment in
`tests/BMS.Tests/BMS.Tests.csproj`.

## Test

```
dotnet test "BMS WinForm.sln"
```

43 NUnit tests over the audit trail's pure surface: RFC 4180 quoting, round-trip
exact number rendering, invariant timestamp and number rendering under a
comma-decimal and a non-Gregorian culture, column order, header-once, password
redaction, and the swallow-on-failure contract.

They cover the writer, **not the seven capture sites** — every one of those is a
`Click` handler, which is `FINDINGS.md` rank 5 ("no automated tests, and no seam to
add them"). Adding that seam is restructuring; the finding constrained the work
rather than being designed around. Consequence worth knowing before you rely on a
green suite: deleting any one `AuditWriter.Append` call leaves all 43 passing. See
`docs/specs/AUDIT_TRAIL.md` §7.3.

## Validating the audit trail by hand

The seven capture sites are covered by execution, not by tests — every one is a
`Click` handler, which is `FINDINGS.md` rank 5.

### Smoke test (~20 seconds)

Enough to know the feature is alive. **Shell commands below are run from the repo
root**; the *application* must have `BMS WinForm/bin/Debug/` as its working
directory, because every data path in it is a bare relative literal (S42).

Clear any previous trail:

```bash
rm -f "BMS WinForm/bin/Debug/auditTrail.csv"
```

Then launch the app **from that directory** — double-click
`BMS WinForm/bin/Debug/BMS WinForm.exe`, or:

```bash
(cd "BMS WinForm/bin/Debug" && "./BMS WinForm.exe")
```

1. Log in as `Haider` / `15`.
2. **Deposit Money** → any amount → **Confirm** → OK.
3. **Log Out**.

```bash
cat "BMS WinForm/bin/Debug/auditTrail.csv"
```

Pass: **3 lines — a header and 2 data rows**, `Deposit` then `LogoutBalanceWrite`,
both with `operator` = `Haider`.

That covers the writer end to end — file created in the working directory, header
written once, RFC 4180 row, operator identity taken from login — plus two of the
seven capture sites, including site 7, which nothing else reaches.

It also shows the feature's point in one glance: the deposit row's `balanceBefore`
will **not** match the `9000` in `customers.txt`, because the `calculate*` recompute
already moved the balance in memory before you clicked anything. That drift is what
the trail exists to make visible, and the `LogoutBalanceWrite` row is where the
drifted value reaches disk.

**Then put the sample data back.** The run appends to `depositHistory.txt` and
rewrites `customers.txt`. Both are tracked, so no backup is needed — discard the
changes:

```bash
git checkout -- "BMS WinForm/bin/Debug/customers.txt" "BMS WinForm/bin/Debug/depositHistory.txt"
```

Working from a ZIP with no git history, copy those two files aside **before** you
start, and copy them back afterwards.

### Full validation

The smoke test says nothing about the other five sites. The complete scenario is
**[`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) §7.1** and is
authoritative: exact inputs, the ordering constraint that keeps the count correct,
and §7.2's four results that look like failures but are not. It lives in the spec
rather than here so there is one copy to keep true. Its assertion:

> **7 operations → 8 data rows, 9 lines including the header.**

⚠️ **Import the trail as text; do not double-click it.** RFC 4180 quoting
guarantees the file parses back to the values written, but a value beginning with
`=`, `+`, `-` or `@` is still evaluated as a formula by Excel and LibreOffice —
and `subjectUserName` and `details` carry operator-entered text. The writer does
not neutralise these, because escaping them would alter the recorded value, which
is the one thing the trail must never do. In Excel: *Data → From Text/CSV*, or set
every column to Text in the import wizard.

Restore the sample data afterwards — the full run mutates more files than the smoke
test does, and the `CustomerCreate` steps can corrupt `customers.txt` badly enough
that **the app will not start** (that is S14, and `FINDINGS.md` documents it):

```bash
git checkout -- "BMS WinForm/bin/Debug"
```

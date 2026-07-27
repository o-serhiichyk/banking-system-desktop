# Banking_Management_System_OOP_Winforms
A banking management system built in C# Winforms implementing OOP concepts.

---

## Assessment deliverables

This fork adds an analysis of the application above. The original code is
unchanged except where an improvement is explicitly documented.

| Document | What it is |
|---|---|
| [`docs/REPORT.docx`](docs/REPORT.docx) | **The technical report** — the assignment deliverable: reverse engineering, missing functionality, the ranked code review, and the implemented feature. Self-contained; the documents below are its evidence base |
| [`docs/FINDINGS.md`](docs/FINDINGS.md) | Code smells and engineering risks — the five most critical ranked by business risk, plus the full catalogue |
| [`docs/CAPABILITIES.md`](docs/CAPABILITIES.md) | Missing and incomplete functionality — the slate of 10, and what was rejected |
| [`docs/ANALYSIS_LOG.md`](docs/ANALYSIS_LOG.md) | The append-only audit trail: AI passes, cross-model validation, executed probes, superseded positions, attribution |
| [`docs/PREDICTION.md`](docs/PREDICTION.md) | Independent hypotheses, committed **before** any AI was run |
| [`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) | Implementation contract for the one capability that was built |

`FINDINGS.md` and `CAPABILITIES.md` are current-state documents and are kept
up to date. `ANALYSIS_LOG.md` is never rewritten — it records what happened and
when, including positions later revised.

**What this README deliberately does not contain.** Anything that changes when the
*code* changes — counts, line numbers, expected values, explanations of why a result
looks odd — lives in the documents above, and this file points at them. What stays
here is what changes only when the *workflow* changes: how to build, run and test,
and the shortest check that the feature is alive. Two counts had already gone stale
here before that rule was applied.

---

## Build

```powershell
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

### A build leaves the tree dirty

`BMS WinForm/obj/Debug/` was committed by the original author (`5518017`), so a
build rewrites tracked files and `git status` afterwards shows a handful of
modified binaries. Discard them:

```powershell
git checkout -- "BMS WinForm/obj"
```

*Why this looks contradictory.* The `.gitignore` is **not** upstream — it was
added by this assessment (`4bdd368`) and calls `obj/` "regenerated on every build
— noise, not source", which reads as though nothing under `obj/` is tracked.
Ignore rules do not apply to paths already tracked, so the committed
`*.resources` the section above depends on are unaffected, and so is the dirty
tree. Untracking them would make the ignore rule honest and break the only build
that works on this toolchain, so the rule stays and the consequence is documented
here instead.

## Test

```powershell
dotnet test "BMS WinForm.sln"
```

NUnit tests over the audit trail's pure surface: RFC 4180 quoting, round-trip exact
number rendering, invariant rendering under a comma-decimal and a non-Gregorian
culture, column order, header-once, password redaction, and the swallow-on-failure
contract.

They cover the writer, **not the seven capture sites** — every one is a `Click`
handler with no test seam (`FINDINGS.md` rank 5). Worth knowing before you trust a
green suite: deleting an `AuditWriter.Append` call leaves it green. See
[`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) §7.3.

## Validating the audit trail by hand

### Smoke test (~20 seconds)

Enough to know the feature is alive. Commands are **Windows PowerShell, run from the
repo root**. The *application* must run with `BMS WinForm\bin\Debug\` as its working
directory — every data path in it is a bare relative literal (S42) — which the
`Start-Process` below handles.

```powershell
Remove-Item "BMS WinForm\bin\Debug\auditTrail.csv" -ErrorAction Ignore
```

```powershell
Start-Process "BMS WinForm\bin\Debug\BMS WinForm.exe" -WorkingDirectory "BMS WinForm\bin\Debug"
```

1. Log in as `Haider` / `15`.
2. **Deposit Money** → any amount → **Confirm** → OK.
3. **Log Out**.

```powershell
Get-Content "BMS WinForm\bin\Debug\auditTrail.csv"
```

Pass: **3 lines — a header and 2 data rows**, `Deposit` then `LogoutBalanceWrite`,
both with `operator` = `Haider`.

The deposit row's `balanceBefore` will **not** match `customers.txt`. That is
correct, and it is the feature's whole point —
[`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) §7.2 explains it with three
other results that look like failures and are not.

### Full validation

The smoke test reaches two of the seven capture sites. The complete scenario —
exact inputs, the ordering constraint that keeps the count correct, and the expected
result — is **[`docs/specs/AUDIT_TRAIL.md`](docs/specs/AUDIT_TRAIL.md) §7.1**, which
is authoritative. It lives in the spec so there is one copy to keep true.

⚠️ **Import the trail as text; do not double-click it.** A value beginning with `=`,
`+`, `-` or `@` is evaluated as a formula regardless of quoting, and the writer does
not neutralise it — escaping would alter the recorded value. In Excel: *Data → From
Text/CSV*. Reasoning in §5.5.

### Restoring the sample data

Any run mutates it, and the full scenario can corrupt `customers.txt` badly enough
that **the application will not start** — that is S14, documented in
[`docs/FINDINGS.md`](docs/FINDINGS.md). The files are tracked, so no backup is
needed:

```powershell
git checkout -- "BMS WinForm/bin/Debug"
```

From a ZIP with no git history, copy `BMS WinForm\bin\Debug\*.txt` aside **before**
you start.

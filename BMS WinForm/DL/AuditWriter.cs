using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BMS_WinForm.BL;

namespace BMS_WinForm.DL
{
    /// <summary>
    /// Append-only writer for auditTrail.csv.
    ///
    /// Comma-delimited to match the codebase, RFC 4180 quoted because the codebase
    /// lacks escaping entirely (S14) — a value is quoted when it contains a comma,
    /// a double quote, CR or LF; internal quotes are doubled; values are never
    /// altered. The trail must be able to record the exact string that caused a
    /// corruption, which is the one case where fidelity matters most.
    ///
    /// See docs/specs/AUDIT_TRAIL.md.
    /// </summary>
    static class AuditWriter
    {
        /// <summary>
        /// A bare relative literal, matching the app's existing 16 sites (S42), so the
        /// trail lands beside the data files it exists to be reconciled against. Note
        /// the consequence recorded in spec §5.1: launching from a different working
        /// directory quietly starts a second trail rather than failing.
        /// </summary>
        internal const string DefaultPath = "auditTrail.csv";

        /// <summary>Column captions, in the order AuditRecord declares its fields.</summary>
        internal static readonly string[] Columns =
        {
            "timestamp",
            "operationId",
            "operator",
            "event",
            "subjectUserName",
            "subjectAccount",
            "counterpartyAccount",
            "amount",
            "balanceBefore",
            "balanceAfter",
            "targetFile",
            "details"
        };

        internal const string EventDeposit = "Deposit";
        internal const string EventWithdraw = "Withdraw";
        internal const string EventTransfer = "Transfer";
        internal const string EventCustomerCreate = "CustomerCreate";
        internal const string EventCustomerEdit = "CustomerEdit";
        internal const string EventCustomerDelete = "CustomerDelete";
        internal const string EventLogoutBalanceWrite = "LogoutBalanceWrite";

        private static readonly char[] MustQuote = { ',', '"', '\r', '\n' };

        // The details column is its own delimited format nested inside a CSV field, so it
        // needs its own trigger set: ':' separates field from values, Arrow separates the
        // values, "; " separates changes, and '"' is the escape character itself.
        // See QuoteDetail.
        private static readonly char[] MustQuoteDetail = { ':', ';', '"', '\u2192' };

        // Shortest-first; Number() takes the first that reparses equal. See Number().
        private static readonly string[] RoundTripFormats = { "G15", "G16", "G17" };

        // U+2192 RIGHTWARDS ARROW, written as an escape so the source file stays ASCII
        // and cannot be mangled by a compiler codepage guess.
        internal const string Arrow = "\u2192";

        // UTF-8 with a BOM: the details column renders changes with Arrow, and the file's
        // stated investigation path is opening it in a spreadsheet. StreamWriter emits the
        // preamble only at stream position 0, so an append never repeats it.
        private static readonly Encoding FileEncoding = new UTF8Encoding(true);

        /// <summary>The header line, built from Columns so it cannot drift from them.</summary>
        internal static string HeaderRow
        {
            get { return string.Join(",", Columns.Select(Quote)); }
        }

        /// <summary>
        /// Invariant because several cultures emit a comma inside the default DateTime
        /// rendering (S16, culture limb), and lexicographic because the file has no
        /// viewer — sorting by eye is the only ordering available.
        /// </summary>
        internal static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>8 chars of a Guid, one per handler invocation.</summary>
        internal static string NewOperationId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>
        /// Invariant, for the same reason the timestamp is — and **round-trip exact**,
        /// which the obvious `value.ToString(InvariantCulture)` is not on .NET Framework.
        /// That default is `G15`, so it silently rewrites the recorded value:
        /// `1.2345678901234567` becomes `1.23456789012346`, and a balance of
        /// `9007199254740992` becomes `9.00719925474099E+15` — scientific notation, in a
        /// money column. Neither survives a reparse. A trail whose stated job is to record
        /// the value the application actually used cannot round it, least of all in an app
        /// whose fourth-ranked finding is `double` inexactness.
        ///
        /// `"R"` is the documented round-trip specifier but is itself broken for doubles on
        /// .NET Framework — `0.84551240822557006` renders as `0.84551240822557` and does
        /// not reparse. So this walks G15 → G16 → G17 and takes the first that reparses
        /// equal, which is the documented workaround: shortest form that is still exact.
        /// Ordinary money stays readable (`1234.56`, not `1234.5599999999999`) and a value
        /// that genuinely needs 17 digits gets them.
        ///
        /// Consequence worth knowing: `0.1 + 0.2` records as `0.30000000000000004`. That is
        /// the value the application is holding, and showing it is the point.
        /// </summary>
        internal static string Number(double value)
        {
            foreach (string format in RoundTripFormats)
            {
                string rendered = value.ToString(format, CultureInfo.InvariantCulture);
                double reparsed;
                if (double.TryParse(rendered, NumberStyles.Float, CultureInfo.InvariantCulture, out reparsed)
                    && reparsed.Equals(value))
                {
                    return rendered;
                }
            }
            // Only reachable for values TryParse will not accept back (the infinities on
            // this framework). Record them verbatim rather than dropping the column.
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        /// <summary>RFC 4180 quoting. Values are never altered — only wrapped.</summary>
        internal static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            if (value.IndexOfAny(MustQuote) < 0)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Quoting for values placed **inside** the `details` column.
        ///
        /// RFC 4180 protects the CSV *field*; it does nothing for the structure inside it.
        /// `details` is its own delimited format — `Field:before→after`, changes joined by
        /// `"; "` — so an unescaped value containing those delimiters can fabricate
        /// entries. A customer renamed to `Alice→Bob; TotalMoney:9000→0` produced
        ///
        ///     Name:Alice→Alice→Bob; TotalMoney:9000→0
        ///
        /// which reads as two changes, the second asserting a balance move that never
        /// happened. In an audit trail that is a forgery vector, and it is the same
        /// unescaped-concatenation defect as S14 nested one level down — in the very
        /// column added to observe S14.
        ///
        /// Deliberately the *same* convention as <see cref="Quote"/>: wrap in double
        /// quotes, double any internal quote. One escaping idiom in the file rather than
        /// two, and it nests correctly, because the CSV layer then doubles these quotes
        /// again on the way out. Reversible, so fidelity is preserved — §2's "values are
        /// never altered" is about the recorded value surviving a round trip, and it does.
        /// </summary>
        internal static string QuoteDetail(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            if (value.IndexOfAny(MustQuoteDetail) < 0)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>The twelve columns of one record, in order, comma-delimited.</summary>
        internal static string FormatRow(AuditRecord record)
        {
            return string.Join(",", new[]
            {
                Quote(record.Timestamp),
                Quote(record.OperationId),
                Quote(record.Operator),
                Quote(record.Event),
                Quote(record.SubjectUserName),
                Quote(record.SubjectAccount),
                Quote(record.CounterpartyAccount),
                Quote(record.Amount),
                Quote(record.BalanceBefore),
                Quote(record.BalanceAfter),
                Quote(record.TargetFile),
                Quote(record.Details)
            });
        }

        /// <summary>
        /// Renders the changed fields of a customer record edit as
        /// "Field:before->after", joined with "; ". Unchanged fields are omitted.
        ///
        /// A changed password renders as Password:[redacted]->[redacted] and an
        /// unchanged one is omitted entirely, so the fact of a credential change is
        /// recorded and its value is not. Without this the append-only trail would
        /// accumulate every password a customer has ever held, worsening S22/S23.
        /// </summary>
        internal static string DescribeChanges(Admin previous, Admin update)
        {
            if (previous == null || update == null)
            {
                return "";
            }

            List<string> changes = new List<string>();
            AddChange(changes, "Name", previous.Name, update.Name);
            AddChange(changes, "UserName", previous.UserName, update.UserName);
            if (!Same(previous.Password, update.Password))
            {
                changes.Add("Password:[redacted]" + Arrow + "[redacted]");
            }
            AddChange(changes, "AccountType", previous.AccountType, update.AccountType);
            AddChange(changes, "City", previous.City, update.City);
            AddChange(changes, "PhoneNumber", previous.PhoneNumber, update.PhoneNumber);
            AddChange(changes, "AccountNumber", Number(previous.AccountNumber), Number(update.AccountNumber));
            AddChange(changes, "IntialDeposit", Number(previous.IntialDeposit), Number(update.IntialDeposit));
            AddChange(changes, "TotalMoney", Number(previous.TotalMoney), Number(update.TotalMoney));

            return string.Join("; ", changes);
        }

        /// <summary>Appends one record to the trail beside the app's other data files.</summary>
        internal static void Append(AuditRecord record)
        {
            Append(DefaultPath, record);
        }

        /// <summary>
        /// Appends one record. Takes the path as a parameter so tests can point it at a
        /// temp file.
        ///
        /// Swallows every exception and never throws. This is load-bearing, not
        /// defensive habit: the three money handlers wrap their whole body in
        /// try/catch, so a throwing append would show a failure dialog for a deposit
        /// that succeeded and skip clearFormData() — turning one deposit into two.
        /// CusForm and ViewCustomer have no try at all, where a throw would be
        /// unhandled. The cost is a silently dropped entry (spec §5.1).
        /// </summary>
        internal static void Append(string path, AuditRecord record)
        {
            try
            {
                using (StreamWriter file = new StreamWriter(path, true, FileEncoding))
                {
                    // Emptiness is read from the opened file, not from File.Exists: one
                    // file operation, and an existence check that threw would be swallowed
                    // below and drop the record along with the header. The header must not
                    // be able to cost an entry.
                    if (new FileInfo(path).Length == 0)
                    {
                        file.WriteLine(HeaderRow);
                    }
                    file.WriteLine(FormatRow(record));
                }
            }
            catch
            {
                // Deliberately silent. Instrumentation that can throw would change
                // behaviour at every call site — see the summary above and spec §3.
                //
                // Concurrent instances of the application make this fire in bulk: the
                // open above uses FileShare.Read, so a second writer fails and loses its
                // entry. Measured at 21-33% loss (spec §5.7). Deliberately NOT locked
                // here — see that section for why the fix belongs at the application
                // level, not in the instrumentation.
            }
        }

        private static void AddChange(List<string> changes, string field, string before, string after)
        {
            if (!Same(before, after))
            {
                // The field name is a fixed identifier and never needs quoting; the values
                // come from operator input and always do. See QuoteDetail.
                changes.Add(field + ":" + QuoteDetail(before) + Arrow + QuoteDetail(after));
            }
        }

        private static bool Same(string before, string after)
        {
            return string.Equals(before ?? "", after ?? "", StringComparison.Ordinal);
        }
    }
}

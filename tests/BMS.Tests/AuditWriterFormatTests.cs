using System;
using System.Globalization;
using System.Threading;
using BMS_WinForm.BL;
using BMS_WinForm.DL;
using NUnit.Framework;

namespace BMS.Tests
{
    /// <summary>
    /// The pure surface of the audit trail: quoting, number and timestamp rendering,
    /// column order, and the changed-field description. Nothing here touches disk.
    /// </summary>
    [TestFixture]
    public class AuditWriterFormatTests
    {
        // ---- RFC 4180 quoting -------------------------------------------------

        [TestCase("plain", "plain", TestName = "Quote_leaves_a_value_with_nothing_special_alone")]
        [TestCase("Multan, Punjab", "\"Multan, Punjab\"", TestName = "Quote_wraps_an_embedded_comma")]
        [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"", TestName = "Quote_doubles_an_embedded_quote")]
        [TestCase("a,\"b\"", "\"a,\"\"b\"\"\"", TestName = "Quote_handles_a_comma_and_a_quote_together")]
        [TestCase("line1\rline2", "\"line1\rline2\"", TestName = "Quote_wraps_a_carriage_return")]
        [TestCase("line1\nline2", "\"line1\nline2\"", TestName = "Quote_wraps_a_line_feed")]
        [TestCase("line1\r\nline2", "\"line1\r\nline2\"", TestName = "Quote_wraps_a_CRLF")]
        [TestCase("", "", TestName = "Quote_renders_empty_as_empty")]
        [TestCase(null, "", TestName = "Quote_renders_null_as_empty")]
        public void Quote_follows_RFC_4180(string value, string expected)
        {
            Assert.That(AuditWriter.Quote(value), Is.EqualTo(expected));
        }

        [Test]
        public void Quote_never_alters_the_value_itself()
        {
            // The trail exists partly to record the exact string that caused a
            // corruption, so a lossy sanitizer would defeat its purpose.
            const string hostile = "Ali,\"Haider\"\r\n";

            string quoted = AuditWriter.Quote(hostile);

            Assert.That(Unquote(quoted), Is.EqualTo(hostile));
        }

        // ---- Culture ----------------------------------------------------------

        [Test]
        public void Now_uses_the_invariant_format_under_a_non_invariant_culture()
        {
            // de-DE renders a default DateTime with dots; several cultures use a comma,
            // which would silently add a column. The format must not depend on culture.
            WithCulture("de-DE", () =>
                Assert.That(AuditWriter.Now(), Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$")));
        }

        [Test]
        public void Now_uses_the_Gregorian_calendar_under_a_non_Gregorian_culture()
        {
            // ar-SA defaults to the Umm al-Qura calendar; the invariant culture must win.
            WithCulture("ar-SA", () =>
                Assert.That(AuditWriter.Now(), Does.StartWith(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture))));
        }

        [Test]
        public void Number_uses_a_dot_decimal_separator_under_a_comma_decimal_culture()
        {
            // de-DE would render 1234.5 as "1234,5" — a comma inside an unquoted value.
            WithCulture("de-DE", () =>
                Assert.That(AuditWriter.Number(1234.5), Is.EqualTo("1234.5")));
        }

        // ---- Number: round-trip exactness --------------------------------------
        //
        // The trail's job is to record the value the application actually used. The
        // default double.ToString(InvariantCulture) is G15 on .NET Framework and loses
        // it; "R" is the documented round-trip specifier and is itself broken here.
        // These cases are the ones that catch each failure mode.

        [TestCase(2000d, "2000", TestName = "Number_keeps_a_whole_balance_plain")]
        [TestCase(9000.5, "9000.5", TestName = "Number_keeps_a_half_plain")]
        [TestCase(1234.56, "1234.56", TestName = "Number_keeps_ordinary_money_readable")]
        [TestCase(19.99, "19.99", TestName = "Number_does_not_expand_cents_to_seventeen_digits")]
        [TestCase(1.2345678901234567, "1.2345678901234567", TestName = "Number_keeps_all_seventeen_digits_when_needed")]
        [TestCase(0.84551240822557006, "0.8455124082255701", TestName = "Number_survives_the_dotnet_framework_R_specifier_bug")]
        [TestCase(9007199254740992d, "9007199254740992", TestName = "Number_never_falls_back_to_scientific_notation_for_a_large_balance")]
        public void Number_renders_the_shortest_exact_form(double value, string expected)
        {
            Assert.That(AuditWriter.Number(value), Is.EqualTo(expected));
        }

        [Test]
        public void Number_output_always_reparses_to_the_same_double()
        {
            double[] values =
            {
                2000d, 9000.5, 1234.56, 19.99, 0.07, 100.10, 191437d,
                0.1, 0.1 + 0.2, 1.2345678901234567, 0.84551240822557006,
                9007199254740992d, double.MaxValue, double.Epsilon, 0d, -4500.25
            };

            foreach (double value in values)
            {
                string rendered = AuditWriter.Number(value);
                Assert.That(double.Parse(rendered, NumberStyles.Float, CultureInfo.InvariantCulture),
                    Is.EqualTo(value), "did not round-trip: " + rendered);
            }
        }

        [Test]
        public void Number_exposes_double_inexactness_rather_than_hiding_it()
        {
            // 0.1 + 0.2 is not 0.3, and the application is holding the difference.
            // Rounding it away in the audit trail would conceal ranked finding 4.
            Assert.That(AuditWriter.Number(0.1 + 0.2), Is.EqualTo("0.30000000000000004"));
        }

        [Test]
        public void Number_stays_exact_under_a_non_invariant_culture()
        {
            WithCulture("de-DE", () =>
                Assert.That(AuditWriter.Number(1.2345678901234567), Is.EqualTo("1.2345678901234567")));
        }

        // ---- Columns ----------------------------------------------------------

        [Test]
        public void There_are_twelve_columns()
        {
            Assert.That(AuditWriter.Columns.Length, Is.EqualTo(12));
        }

        [Test]
        public void The_header_captions_match_the_column_order()
        {
            // Fill every field of a record with its own caption. If FormatRow's field
            // order ever drifts from Columns, this stops matching the header.
            AuditRecord record = new AuditRecord
            {
                Timestamp = "timestamp",
                OperationId = "operationId",
                Operator = "operator",
                Event = "event",
                SubjectUserName = "subjectUserName",
                SubjectAccount = "subjectAccount",
                CounterpartyAccount = "counterpartyAccount",
                Amount = "amount",
                BalanceBefore = "balanceBefore",
                BalanceAfter = "balanceAfter",
                TargetFile = "targetFile",
                Details = "details"
            };

            Assert.That(AuditWriter.FormatRow(record), Is.EqualTo(AuditWriter.HeaderRow));
        }

        [Test]
        public void A_row_always_has_twelve_fields_even_when_most_are_empty()
        {
            AuditRecord record = new AuditRecord
            {
                Timestamp = "2026-07-26 10:00:00",
                Event = AuditWriter.EventDeposit
            };

            Assert.That(AuditWriter.FormatRow(record).Split(','), Has.Length.EqualTo(12));
        }

        [Test]
        public void A_row_puts_each_value_in_its_declared_column()
        {
            AuditRecord record = new AuditRecord
            {
                Timestamp = "2026-07-26 10:00:00",
                OperationId = "a1b2c3d4",
                Operator = "Haider",
                Event = AuditWriter.EventTransfer,
                SubjectUserName = "Haider",
                SubjectAccount = "123456",
                CounterpartyAccount = "654321",
                Amount = "500",
                BalanceBefore = "9000",
                BalanceAfter = "9000",
                TargetFile = "transactHistory.txt",
                Details = ""
            };

            Assert.That(AuditWriter.FormatRow(record), Is.EqualTo(
                "2026-07-26 10:00:00,a1b2c3d4,Haider,Transfer,Haider,123456,654321,500,9000,9000,transactHistory.txt,"));
        }

        // ---- Changed-field description ----------------------------------------

        [Test]
        public void DescribeChanges_is_empty_when_nothing_changed()
        {
            Admin previous = Customer("Ali", "Haider", "15", 9000);

            Assert.That(AuditWriter.DescribeChanges(previous, Customer("Ali", "Haider", "15", 9000)), Is.Empty);
        }

        [Test]
        public void DescribeChanges_renders_a_changed_balance_as_before_arrow_after()
        {
            Admin previous = Customer("Ali", "Haider", "15", 9000);
            Admin update = Customer("Ali", "Haider", "15", 11000);

            Assert.That(AuditWriter.DescribeChanges(previous, update),
                Is.EqualTo("TotalMoney:9000" + AuditWriter.Arrow + "11000"));
        }

        [Test]
        public void DescribeChanges_lists_only_the_fields_that_changed()
        {
            Admin previous = Customer("Ali", "Haider", "15", 9000);
            Admin update = Customer("Ali Raza", "Haider", "15", 9000);
            update.City = "Lahore";

            Assert.That(AuditWriter.DescribeChanges(previous, update),
                Is.EqualTo("Name:Ali" + AuditWriter.Arrow + "Ali Raza; City:Multan" + AuditWriter.Arrow + "Lahore"));
        }

        [Test]
        public void DescribeChanges_redacts_a_changed_password_on_both_sides()
        {
            Admin previous = Customer("Ali", "Haider", "15", 9000);
            Admin update = Customer("Ali", "Haider", "hunter2", 9000);

            string details = AuditWriter.DescribeChanges(previous, update);

            Assert.That(details, Is.EqualTo("Password:[redacted]" + AuditWriter.Arrow + "[redacted]"));
            Assert.That(details, Does.Not.Contain("hunter2"));
            Assert.That(details, Does.Not.Contain("15"));
        }

        [Test]
        public void DescribeChanges_omits_an_unchanged_password_entirely()
        {
            Admin previous = Customer("Ali", "Haider", "15", 9000);
            Admin update = Customer("Ali", "Haider", "15", 11000);

            Assert.That(AuditWriter.DescribeChanges(previous, update), Does.Not.Contain("Password"));
        }

        [Test]
        public void DescribeChanges_needs_no_quoting_because_it_separates_with_a_semicolon()
        {
            // A comma separator would force every multi-change details value to be
            // quoted; "; " keeps the common case readable in a raw text editor.
            Admin previous = Customer("Ali", "Haider", "15", 9000);
            Admin update = Customer("Ali Raza", "Haider", "15", 11000);

            string details = AuditWriter.DescribeChanges(previous, update);

            Assert.That(AuditWriter.Quote(details), Is.EqualTo(details));
        }

        [Test]
        public void DescribeChanges_survives_a_null_side()
        {
            Assert.That(AuditWriter.DescribeChanges(null, Customer("Ali", "Haider", "15", 9000)), Is.Empty);
            Assert.That(AuditWriter.DescribeChanges(Customer("Ali", "Haider", "15", 9000), null), Is.Empty);
        }

        // ---- Operation ids ----------------------------------------------------

        [Test]
        public void NewOperationId_is_eight_characters_and_distinct_per_call()
        {
            string first = AuditWriter.NewOperationId();
            string second = AuditWriter.NewOperationId();

            Assert.That(first, Has.Length.EqualTo(8));
            Assert.That(first, Is.Not.EqualTo(second));
        }

        // ---- Helpers ----------------------------------------------------------

        private static Admin Customer(string name, string userName, string password, double totalMoney)
        {
            return new Admin(name, userName, password, "Current", "Multan", "2131", 123456, 2000, totalMoney);
        }

        private static void WithCulture(string culture, Action body)
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                body();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        private static string Unquote(string field)
        {
            if (field.Length < 2 || field[0] != '"')
            {
                return field;
            }
            return field.Substring(1, field.Length - 2).Replace("\"\"", "\"");
        }
    }
}

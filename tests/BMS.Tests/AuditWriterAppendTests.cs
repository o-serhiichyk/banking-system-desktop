using System;
using System.IO;
using BMS_WinForm.DL;
using NUnit.Framework;

namespace BMS.Tests
{
    /// <summary>
    /// The file-facing contract: header once, append-only, and never throwing.
    /// Every test points the writer at a temp file rather than auditTrail.csv.
    /// </summary>
    [TestFixture]
    public class AuditWriterAppendTests
    {
        private string path;

        [SetUp]
        public void CreateTempPath()
        {
            path = Path.Combine(Path.GetTempPath(), "auditTrail-" + Guid.NewGuid().ToString("N") + ".csv");
        }

        [TearDown]
        public void RemoveTempFile()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [Test]
        public void The_header_is_written_to_an_empty_file()
        {
            AuditWriter.Append(path, Deposit("500"));

            Assert.That(Lines()[0], Is.EqualTo(AuditWriter.HeaderRow));
        }

        [Test]
        public void The_header_is_not_written_again_on_a_second_append()
        {
            AuditWriter.Append(path, Deposit("500"));
            AuditWriter.Append(path, Deposit("700"));

            string[] lines = Lines();

            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Is.EqualTo(AuditWriter.HeaderRow));
            Assert.That(lines[1], Does.Contain("500"));
            Assert.That(lines[2], Does.Contain("700"));
        }

        [Test]
        public void An_append_only_ever_adds_lines()
        {
            AuditWriter.Append(path, Deposit("500"));
            string afterFirst = File.ReadAllText(path);

            AuditWriter.Append(path, Deposit("700"));

            Assert.That(File.ReadAllText(path), Does.StartWith(afterFirst));
        }

        [Test]
        public void A_value_containing_a_comma_round_trips_through_the_file()
        {
            AuditRecord record = Deposit("500");
            record.SubjectUserName = "Haider, Ali";

            AuditWriter.Append(path, record);

            Assert.That(Lines()[1], Does.Contain("\"Haider, Ali\""));
        }

        [Test]
        public void A_value_containing_a_newline_stays_inside_one_quoted_field()
        {
            AuditRecord record = Deposit("500");
            record.Details = "note\r\nsecond line";

            AuditWriter.Append(path, record);

            // The embedded CRLF adds a physical line, but the quoted field keeps it
            // recoverable — the point of RFC 4180 over the app's bare concatenation.
            Assert.That(File.ReadAllText(path), Does.Contain("\"note\r\nsecond line\""));
        }

        [Test]
        public void The_writer_returns_without_throwing_when_the_file_is_locked()
        {
            using (new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Assert.DoesNotThrow(() => AuditWriter.Append(path, Deposit("500")));
            }
        }

        [Test]
        public void The_writer_returns_without_throwing_when_the_path_is_unusable()
        {
            string unusable = Path.Combine(path, "nested", "auditTrail.csv");

            Assert.DoesNotThrow(() => AuditWriter.Append(unusable, Deposit("500")));
        }

        [Test]
        public void A_dropped_entry_leaves_the_existing_trail_intact()
        {
            AuditWriter.Append(path, Deposit("500"));
            string before = File.ReadAllText(path);

            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                AuditWriter.Append(path, Deposit("700"));
            }

            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }

        private static AuditRecord Deposit(string amount)
        {
            return new AuditRecord
            {
                Timestamp = "2026-07-26 10:00:00",
                OperationId = "a1b2c3d4",
                Operator = "Haider",
                Event = AuditWriter.EventDeposit,
                SubjectUserName = "Haider",
                SubjectAccount = "123456",
                Amount = amount,
                BalanceBefore = "9000",
                BalanceAfter = "9500",
                TargetFile = "depositHistory.txt"
            };
        }

        private string[] Lines()
        {
            return File.ReadAllLines(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BMS_WinForm.DL
{
    /// <summary>
    /// One row of auditTrail.csv. Twelve fixed columns, all carried as strings —
    /// the record holds exactly what will be written, so formatting decisions
    /// (invariant timestamps, invariant numbers) are made once by the caller
    /// rather than re-derived by the writer.
    ///
    /// Field order here is the column order; AuditWriter.Columns must match it.
    /// A test asserts the two have not drifted apart.
    /// See docs/specs/AUDIT_TRAIL.md §2.
    /// </summary>
    class AuditRecord
    {
        /// <summary>System clock, invariant format.</summary>
        internal string Timestamp { get; set; }

        /// <summary>8 chars of a Guid, one per handler invocation. A transfer's two rows share one.</summary>
        internal string OperationId { get; set; }

        /// <summary>From AuditSession.Operator.</summary>
        internal string Operator { get; set; }

        /// <summary>Deposit · Withdraw · Transfer · CustomerCreate · CustomerEdit · CustomerDelete · LogoutBalanceWrite.</summary>
        internal string Event { get; set; }

        /// <summary>The account operated on.</summary>
        internal string SubjectUserName { get; set; }

        internal string SubjectAccount { get; set; }

        /// <summary>Transfer only, else empty.</summary>
        internal string CounterpartyAccount { get; set; }

        /// <summary>Empty for edit, delete, persist.</summary>
        internal string Amount { get; set; }

        /// <summary>The subject's stored TotalMoney — see docs/specs/AUDIT_TRAIL.md §5.4.</summary>
        internal string BalanceBefore { get; set; }

        internal string BalanceAfter { get; set; }

        /// <summary>The file the operation wrote; distinguishes the transfer pair.</summary>
        internal string TargetFile { get; set; }

        /// <summary>field:before-&gt;after for changed fields on edit; otherwise empty.</summary>
        internal string Details { get; set; }

        /// <summary>Unstamped. Used by tests that assert on formatting alone.</summary>
        internal AuditRecord()
        {
        }

        /// <summary>Stamps timestamp, a fresh operationId and the current operator.</summary>
        internal AuditRecord(string eventName)
            : this(eventName, AuditWriter.NewOperationId())
        {
        }

        /// <summary>Stamps timestamp and the current operator against a caller-supplied
        /// operationId, so the two rows of a transfer can share one.</summary>
        internal AuditRecord(string eventName, string operationId)
        {
            Timestamp = AuditWriter.Now();
            OperationId = operationId;
            Operator = AuditSession.Operator;
            Event = eventName;
        }
    }
}

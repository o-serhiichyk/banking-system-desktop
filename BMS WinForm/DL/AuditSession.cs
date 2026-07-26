using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BMS_WinForm.DL
{
    /// <summary>
    /// Session state for the audit trail: who the application believes is acting.
    /// Set once, immediately after the authentication check in Form1.btnLogIn_Click,
    /// before the isAdmin branch, so both the admin and the customer path populate it.
    ///
    /// This is deliberately NOT a field on AdminDL. AdminDL is already the home of the
    /// static mutable session state flagged as S41, and AdminDL.Current must never be
    /// used as the actor: it is never null, is never set on the admin path, and is not
    /// cleared on logout, so during an admin session it names the previous customer.
    /// See docs/specs/AUDIT_TRAIL.md §3.
    /// </summary>
    static class AuditSession
    {
        /// <summary>
        /// A username, not an authenticated principal. Rank 1 means it may have been
        /// obtained by escalation; the trail records who the system believed was acting.
        /// </summary>
        internal static string Operator { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BMS_WinForm.DL;
using BMS_WinForm.BL;


namespace BMS_WinForm
{
    public partial class r : UserControl
    {
        private string sendMoneyPath = "sendMoneyPath.txt";
        private string path = "transactHistory.txt";
        public r()
        {
            InitializeComponent();
        }

        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e)
        {

        }

        private void btnViewTransactHistory_Click(object sender, EventArgs e)
        {
            TransactHistory T = new TransactHistory();
            T.ShowDialog();
        }

        private void btnComfirm_Click(object sender, EventArgs e)
        {
            try
            {
                bool check = false;
                double accountNumber = double.Parse(txtAccountNumber.Text);
                foreach (Admin A in AdminDL.CustomersList)
                {
                    if (accountNumber == A.AccountNumber)
                    {
                        check = true;
                        
                    }
                }
                if(check==false)
                {
                    throw new Exception("No Account Found with Such Account Number");
                }
                if (check == true)
                {
                    string purpose = cmbPurpose.Text;
                    double transactMoney = double.Parse(txtTransactMoney.Text);
                    string date = dateTransactMoney.Text;
                    Customer C = new Customer(AdminDL.Current.UserName, accountNumber, purpose, transactMoney, date);
                    Customer Cos = new Customer(accountNumber, AdminDL.Current.AccountNumber, purpose, transactMoney, date);
                    CustomerDL.TransactList.Add(C);

                    // The two stores below are independent, uncoordinated file writes
                    // (S12), so each gets its own entry sharing one operationId.
                    //
                    // What a lone sender entry proves is narrower than it looks: because a
                    // failed append is swallowed (AuditWriter.Append), it means the second
                    // data write failed *or* the second append did. It marks the transfer
                    // unverified, not torn. That is still strictly better than one entry
                    // written after both stores, which would assert "transfer completed"
                    // in exactly the case where it did not.
                    string operationId = AuditWriter.NewOperationId();
                    Admin from = AdminDL.Current;

                    CustomerDL.storeTransactHistory(path, C);
                    AuditWriter.Append(TransferEntry(operationId, from, accountNumber, transactMoney, path));

                    CustomerDL.storeSendMoney(sendMoneyPath, Cos, accountNumber);
                    AuditWriter.Append(TransferEntry(operationId, from, accountNumber, transactMoney, sendMoneyPath));

                    MessageBox.Show("Money Send Successfully");
                    
                    clearFormData();
                }
            }
            catch(Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }

        /// <summary>
        /// One row of the transfer pair. The balance columns are equal by construction,
        /// because this handler debits and credits nobody (S6) — a correct recording of
        /// what happened, left visible rather than corrected. targetFile is what tells
        /// the two rows apart.
        /// </summary>
        private static AuditRecord TransferEntry(string operationId, Admin sender, double counterpartyAccount, double amount, string targetFile)
        {
            return new AuditRecord(AuditWriter.EventTransfer, operationId)
            {
                SubjectUserName = sender.UserName,
                SubjectAccount = AuditWriter.Number(sender.AccountNumber),
                CounterpartyAccount = AuditWriter.Number(counterpartyAccount),
                Amount = AuditWriter.Number(amount),
                BalanceBefore = AuditWriter.Number(sender.TotalMoney),
                BalanceAfter = AuditWriter.Number(sender.TotalMoney),
                TargetFile = targetFile
            };
        }

        private void clearFormData()
        {
            txtAccountNumber.Text = "";
            txtTransactMoney.Text = "";
            cmbPurpose.Text = "";
            dateTransactMoney.Text = "";
        }
    }
}

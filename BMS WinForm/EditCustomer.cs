using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BMS_WinForm.BL;
using BMS_WinForm.DL;

namespace BMS_WinForm
{
   
    public partial class EditCustomer : Form
    {
        private Admin previous;
        public EditCustomer(Admin previous)
        {
            InitializeComponent();
            this.previous = previous;
        }

        private void EditCustomer_Load(object sender, EventArgs e)
        {
            txtName.Text = previous.Name;
            txtUserName.Text = previous.UserName;
            txtPassword.Text = previous.Password;
            cmbAccountType.Text = previous.AccountType;
            cmbCity.Text = previous.City;
            txtPhoneNumber.Text = previous.PhoneNumber;
            txtAccountNumber.Text = previous.AccountNumber.ToString();
            txtTotalMoney.Text = previous.TotalMoney.ToString();
        }

        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e)
        {

        }

       

        private void btnEdit_Click_1(object sender, EventArgs e)
        {
            
            try { 
                double accountNumber = double.Parse(txtAccountNumber.Text);
                if (accountNumber < 100000 || accountNumber > 999999)
                {
                    throw new Exception("Account Number Should be of 6 Character");
                }
                 double totalMoney = double.Parse(txtTotalMoney.Text);
                Admin update = new Admin(txtName.Text, txtUserName.Text, txtPassword.Text, cmbAccountType.Text,cmbCity.Text, txtPhoneNumber.Text, accountNumber, previous.IntialDeposit, totalMoney);
                // Built before the edit, not after: `previous` is the very list element
                // AdminDL.editCustomerData mutates, so reading it afterwards would report
                // the new values on both sides. The subject is the record's previous
                // identity, so a rename chains back to the entries written before it;
                // the rename itself shows up in details.
                AuditRecord entry = new AuditRecord(AuditWriter.EventCustomerEdit)
                {
                    SubjectUserName = previous.UserName,
                    SubjectAccount = AuditWriter.Number(previous.AccountNumber),
                    BalanceBefore = AuditWriter.Number(previous.TotalMoney),
                    BalanceAfter = AuditWriter.Number(update.TotalMoney),
                    TargetFile = "customers.txt",
                    Details = AuditWriter.DescribeChanges(previous, update)
                };
                AdminDL.editCustomerData(previous, update);
                // Written after the in-memory edit but before the enclosing rewrite at
                // ViewCustomer.cs:115, which is uninstrumented (spec §5.1).
                AuditWriter.Append(entry);
                MUserDL.editCustomerData(previous.UserName, previous.Password,txtUserName.Text, txtPassword.Text);
                MUserDL.storeAllIds("Users.txt");
                this.Close();
            }
            catch(Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }
    }
}

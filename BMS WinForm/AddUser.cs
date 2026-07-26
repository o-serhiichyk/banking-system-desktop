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
    public partial class AddUser : UserControl
    {
        private string usersPath = "Users.txt";
        private string customersPath = "customers.txt";
        public AddUser()
        {
            InitializeComponent();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
           

        }
        private void clearForm()
        {
            txtName.Text = "";
            txtUserName.Text = "";
            txtPassword.Text = "";
            cmbAccountType.Text = "";
            cmbCity.Text = "";
            txtPhoneNumber.Text = "";
            txtAccountNumber.Text = "";
            txtIntialDeposit.Text = "";

        }

    

        private void btnAdd_Click_1(object sender, EventArgs e)
        {
            try
            {
                string name = txtName.Text;
                
                string userName = txtUserName.Text;
                foreach (Admin cus in AdminDL.CustomersList)
                {
                    if (userName == cus.UserName)
                    {
                        throw new Exception("UserName Already Taken");
                    }
                }
                string password = txtPassword.Text;
                string accountType = cmbAccountType.Text;
                string city = cmbCity.Text;
                string phoneNumber = txtPhoneNumber.Text;
                foreach (Admin cus in AdminDL.CustomersList)
                {
                    if (phoneNumber==cus.PhoneNumber)
                    {
                        throw new Exception("PhoneNumber Already Registered");
                    }
                }

                double accountNumber = double.Parse(txtAccountNumber.Text);
                if (accountNumber < 100000 || accountNumber >999999)
                {
                    throw new Exception("Account Number Should be of 6 Character");
                }
                foreach (Admin cus in AdminDL.CustomersList)
                {
                    if (accountNumber == cus.AccountNumber)
                    {
                        throw new Exception("AccountNumber Already Taken");
                    }
                }
                double intialDeposit = double.Parse(txtIntialDeposit.Text);
                if (intialDeposit<1999)
                {
                    throw new Exception("Deposit Money Should be More than 2000 or More");
                }
                double totalMoney = double.Parse(txtIntialDeposit.Text);
                Admin A = new Admin(name, userName, password, accountType, city, phoneNumber, accountNumber, intialDeposit, totalMoney);
                MUser U = new MUser(userName, password);
                AdminDL.addCustomerToList(A);
                AdminDL.storeCustomer(A, customersPath);
                MUserDL.adduserstoList(U);
                MUserDL.storeUsersID(U, usersPath);
                // One entry for the pair of stores. The credential-store write is not an
                // event of its own — see docs/specs/AUDIT_TRAIL.md §4. No password field
                // is recorded at all. balanceBefore is empty because no prior balance
                // existed; AdminDL.Current here is whoever the session last bound, not
                // this customer, so it must not be used.
                AuditWriter.Append(new AuditRecord(AuditWriter.EventCustomerCreate)
                {
                    SubjectUserName = A.UserName,
                    SubjectAccount = AuditWriter.Number(A.AccountNumber),
                    Amount = AuditWriter.Number(A.IntialDeposit),
                    BalanceAfter = AuditWriter.Number(A.TotalMoney),
                    TargetFile = customersPath
                });
                MessageBox.Show("User Added Succesfully");
                clearForm();
            }
            catch(Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }

        private void txtAccountNumber_TextChanged(object sender, EventArgs e)
        {
            
        }

        private void txtAccountNumber_Enter(object sender, EventArgs e)
        {
            if (txtAccountNumber.Text == "6 Character")
            {
                txtAccountNumber.Text = "";
                txtAccountNumber.ForeColor = Color.Black;
            }
        }

        private void txtAccountNumber_Leave(object sender, EventArgs e)
        {
            if (txtAccountNumber.Text == "")
            {
                txtAccountNumber.Text = "6 Character";
                txtAccountNumber.ForeColor = Color.Silver ;
            }
        }

        private void txtIntialDeposit_Enter(object sender, EventArgs e)
        {
            if(txtIntialDeposit.Text=="Should be 2000 or More")
            {
                txtIntialDeposit.Text = "";
                txtIntialDeposit.ForeColor = Color.Black;
            }
        }

        private void txtIntialDeposit_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtIntialDeposit_Leave(object sender, EventArgs e)
        {
            if (txtIntialDeposit.Text == "")
            {
                txtIntialDeposit.Text = "Should be 2000 or More";
                txtIntialDeposit.ForeColor = Color.Silver;
            }
        }

        private void txtPhoneNumber_Enter(object sender, EventArgs e)
        {
            if(txtPhoneNumber.Text=="+92")
            {
                txtPhoneNumber.Text = "";
                txtPhoneNumber.ForeColor = Color.Black;
            }
        }

        private void txtPhoneNumber_Leave(object sender, EventArgs e)
        {
            if (txtPhoneNumber.Text == "")
            {
                txtPhoneNumber.Text = "+92";
                txtPhoneNumber.ForeColor = Color.Silver;
            }
        }

        private void cmbAccountType_Enter(object sender, EventArgs e)
        {
            
        }

        private void cmbAccountType_Leave(object sender, EventArgs e)
        {
            
        }

        private void cmbCity_Enter(object sender, EventArgs e)
        {
            if(cmbCity.Text=="Select City")
            {
                cmbCity.Text = "";
                cmbCity.ForeColor = Color.Black;
            }
        }

        private void cmbCity_Leave(object sender, EventArgs e)
        {
            if (cmbCity.Text == "")
            {
                cmbCity.Text = "Select City";
            }
        }
    }
}

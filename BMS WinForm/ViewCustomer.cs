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
    public partial class ViewCustomer : UserControl
    {
       
        public ViewCustomer()
        {
            InitializeComponent();
            
        }

       

        private void gvUsers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
           
        }
        public  void dataBind()
        {
            
            usersGV.DataSource = null;
            usersGV.DataSource = AdminDL.CustomersList;
            usersGV.Refresh();
            


        }
        private void ViewCustomer_Load(object sender, EventArgs e)
        {

            dataBind();
            
        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
        

       

        private void usersGV_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            
        }

        private void tableLayoutPanel1_Paint_1(object sender, PaintEventArgs e)
        {

        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            dataBind();
        }

        private void cmbSort_SelectedIndexChanged(object sender, EventArgs e)
        {

            if (cmbSort.SelectedItem.ToString() == "Name")
            {
                List<Admin> sortedListByName = AdminDL.CustomersList.OrderBy(o => o.Name).ToList();
                usersGV.DataSource = null;
                usersGV.DataSource = sortedListByName;
                usersGV.Refresh();
            }
            else if (cmbSort.SelectedItem.ToString() == "UserName")
            {
                List<Admin> sortedListByUserName = AdminDL.CustomersList.OrderBy(o => o.UserName).ToList();
                usersGV.DataSource = null;
                usersGV.DataSource = sortedListByUserName;
                usersGV.Refresh();
            }
            else if (cmbSort.SelectedItem.ToString() == "Total Money")
            {
                List<Admin> sortedListByTotalMoney = AdminDL.CustomersList.OrderByDescending(o => o.TotalMoney).ToList();
                usersGV.DataSource = null;
                usersGV.DataSource = sortedListByTotalMoney;
                usersGV.Refresh();
            }
        }

        private void usersGV_CellContentClick_1(object sender, DataGridViewCellEventArgs e)
        {
            Admin A = (Admin)usersGV.CurrentRow.DataBoundItem;
            if (usersGV.Columns["Delete"].Index == e.ColumnIndex)
            {
                AdminDL.deleteCustomerFromList(A);
                MUserDL.deleteIdFromList(A.UserName, A.Password);
                AdminDL.storeAllCustomers("customers.txt");
                MUserDL.storeAllIds("Users.txt");
                // One entry for the pair of stores; the credential-store write is not an
                // event of its own (spec §4). balanceAfter is empty — the anchor for this
                // customer's history rows is gone, which is the point of recording it.
                AuditWriter.Append(new AuditRecord(AuditWriter.EventCustomerDelete)
                {
                    SubjectUserName = A.UserName,
                    SubjectAccount = AuditWriter.Number(A.AccountNumber),
                    BalanceBefore = AuditWriter.Number(A.TotalMoney),
                    TargetFile = "customers.txt"
                });
                dataBind();
            }
            else if (usersGV.Columns["Edit"].Index == e.ColumnIndex)
            {
                EditCustomer E = new EditCustomer(A);
                E.ShowDialog();
                AdminDL.storeAllCustomers("customers.txt");
                dataBind();

            }
        }

        private void tableLayoutPanel2_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}

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
using System.Diagnostics;

namespace BMS_WinForm
{
    public partial class LogIn : Form
    {
        public LogIn()
        {
            AdminDL.CustomersList.Clear();
            MUserDL.UsersList.Clear();
            AdminDL.read_data("customers.txt");
            MUserDL.read_data("Users.txt");
            InitializeComponent();
           
        }

      
        private void clearFormData()
        {
            txtUserName.Text = "";
            txtPassword.Text = "";

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            
        }
        private void GitHubLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string githubUrl = "https://github.com/ali7haider";

            Process.Start(githubUrl);
        }

       

        private void iconminimizar_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void iconcerrar_Click(object sender, EventArgs e)
        {
            Application.Exit();

        }

        private void btnLogIn_Click(object sender, EventArgs e)
        {
            string userName = txtUserName.Text;
            string password = txtPassword.Text;
            MUser U = new MUser(userName, password);
            MUser User = MUserDL.checkuser(U);
            if (User != null)
            {
                // Set before the isAdmin branch so both paths populate it uniformly.
                // AdminDL.Current is not usable as the actor: it is never null, is never
                // set on the admin path, and is not cleared on logout, so during an admin
                // session it holds the previous customer. See docs/specs/AUDIT_TRAIL.md §3.
                AuditSession.Operator = User.UserName;
                if (MUserDL.isAdmin(User))
                {
                    AdminWindow A = new AdminWindow();
                    A.Show();
                    this.Hide();
                }
                else
                {
                    this.Hide();
                    AdminDL.setCurrent(User);
                    CustomerWindowPAge Cus = new CustomerWindowPAge(AdminDL.Current);
                    Cus.ShowDialog();
                }
            }
            else
            {
                MessageBox.Show("Invalid User");
            }
            clearFormData();
        }
    }
}

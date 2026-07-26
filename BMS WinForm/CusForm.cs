using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BMS_WinForm.BL;
using BMS_WinForm.DL;

namespace BMS_WinForm
{
    public partial class CustomerWindowPAge : Form
    {
        private Admin A;
        private bool isDragging = false;
        private Point lastCursorPosition;
        public CustomerWindowPAge(Admin A)
        {
            this.A = A;
            InitializeComponent();
        }
        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")]
        private extern static void ReleaseCapture();
        [DllImport("user32.DLL", EntryPoint = "SendMessage")]
        private extern static void SendMessage(System.IntPtr hwnd, int wmsg, int wparam, int lparam);
        private Form currentForm = null; // Field to keep track of the currently displayed form
        private void btnCusHome_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Show();
            homeScreenOf_Cus1.BringToFront();
        }

        private void btnDepositMoney_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            depositMoneyCus1.Show();
            depositMoneyCus1.BringToFront();
        }

        private void btnWithdrawMoney_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            depositMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            withDrawMoneyCus1.Show();
            withDrawMoneyCus1.BringToFront();
        }

        private void btnTransactMoney_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            r1.Show();
            r1.BringToFront();
        }

        private void btnReceivedMoney_Click(object sender, EventArgs e)
        {
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            receivedMoney1.Show();
            receivedMoney1.BringToFront();
        }

        private void btnMyAccountDetails_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            homeScreenOf_Cus1.Hide();
            customerHome1.Show();
            customerHome1.BringToFront();

        }

        private void btnBalanceDetails_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            giveFeedback1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            balanceDetailsCus1.Show();
            balanceDetailsCus1.BringToFront();
        }

        private void btnGiveFeedBack_Click(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            customerHome1.Hide();
            homeScreenOf_Cus1.Hide();
            giveFeedback1.Show();
            giveFeedback1.BringToFront();
        }

        private void btnCusLogOut_Click(object sender, EventArgs e)
        {

            AdminDL.storeAllCustomers("customers.txt");
            // Probe 1 moved a stored balance 9000 -> 11000 with a login, a logout and zero
            // clicks; that write happens here and at no other instrumented site. One row,
            // not one per customer: all four balance-mutation sites target
            // AdminDL.Current.TotalMoney (DL/CustomerDL.cs:198,217,234,248), so every
            // other row this rewrites is byte-identical to what was loaded at login.
            // Balances are equal by construction — this records the value reaching disk.
            AuditWriter.Append(new AuditRecord(AuditWriter.EventLogoutBalanceWrite)
            {
                SubjectUserName = AdminDL.Current.UserName,
                SubjectAccount = AuditWriter.Number(AdminDL.Current.AccountNumber),
                BalanceBefore = AuditWriter.Number(AdminDL.Current.TotalMoney),
                BalanceAfter = AuditWriter.Number(AdminDL.Current.TotalMoney),
                TargetFile = "customers.txt"
            });
            this.Hide();
            LogIn F = new LogIn();
            F.Show();
        }

        private void CustomerWindowPAge_Load(object sender, EventArgs e)
        {
            receivedMoney1.Hide();
            balanceDetailsCus1.Hide();
            r1.Hide();
            withDrawMoneyCus1.Hide();
            depositMoneyCus1.Hide();
            customerHome1.Hide();
            giveFeedback1.Hide();
        }

        private void btnMenu_Click(object sender, EventArgs e)
        {

            if (MenuVertical.Width == 250)
            {
                MenuVertical.Width = 50;
            }
            else
            {
                MenuVertical.Width = 250;
            }
        }

        private void BarraTitulo_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, 0x112, 0xf012, 0);
        }

        private void CustomerWindowPAge_MouseDown(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                int deltaX = e.Location.X - lastCursorPosition.X;
                int deltaY = e.Location.Y - lastCursorPosition.Y;

                this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);

                lastCursorPosition = e.Location;
            }
        }

        private void CustomerWindowPAge_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                lastCursorPosition = e.Location;
            }
        }
    

        private void CustomerWindowPAge_MouseMove(object sender, MouseEventArgs e)
        {
        if (isDragging)
        {
            int deltaX = e.Location.X - lastCursorPosition.X;
            int deltaY = e.Location.Y - lastCursorPosition.Y;

            this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);

            lastCursorPosition = e.Location;
        }

    }

        private void iconrestaurar_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            iconrestaurar.Visible = false;
            iconmaximizar.Visible = true;
        }

        private void iconcerrar_Click(object sender, EventArgs e)
        {
            Application.Exit();

        }

        private void iconminimizar_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;

        }
    }
}

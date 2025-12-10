using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
namespace RiotAccountManager
{
    // ============= MODELS =============
    public class Account
    {
        public string Name { get; set; }
        public string Username { get; set; }
        public string EncryptedPassword { get; set; }
    }
    // ============= ACCOUNT MANAGER =============
    public class AccountManager
    {
        private const string AccountsFile = "accounts.json";
        private const string EncryptionKey = "MySecretKey12345";
        public static List<Account> LoadAccounts()
        {
            try
            {
                if (!File.Exists(AccountsFile))
                    return new List<Account>();
                string json = File.ReadAllText(AccountsFile);
                return JsonConvert.DeserializeObject<List<Account>>(json) ?? new List<Account>();
            }
            catch
            {
                return new List<Account>();
            }
        }
        public static void SaveAccounts(List<Account> accounts)
        {
            try
            {
                string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
                File.WriteAllText(AccountsFile, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save accounts: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        public static string EncryptPassword(string password)
        {
            byte[] data = Encoding.UTF8.GetBytes(password);
            byte[] key = Encoding.UTF8.GetBytes(EncryptionKey);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
            return Convert.ToBase64String(data);
        }
        public static string DecryptPassword(string encryptedPassword)
        {
            try
            {
                byte[] data = Convert.FromBase64String(encryptedPassword);
                byte[] key = Encoding.UTF8.GetBytes(EncryptionKey);
                for (int i = 0; i < data.Length; i++)
                    data[i] ^= key[i % key.Length];
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }
        public static void AddAccount(string name, string username, string password)
        {
            var accounts = LoadAccounts();
            accounts.Add(new Account
            {
                Name = name,
                Username = username,
                EncryptedPassword = EncryptPassword(password)
            });
            SaveAccounts(accounts);
        }
        public static void UpdateAccount(Account oldAccount, string name, string username, string password)
        {
            var accounts = LoadAccounts();
            var account = accounts.Find(a => a.Name == oldAccount.Name && a.Username == oldAccount.Username);
            if (account != null)
            {
                account.Name = name;
                account.Username = username;
                account.EncryptedPassword = EncryptPassword(password);
                SaveAccounts(accounts);
            }
        }
        public static void DeleteAccount(Account account)
        {
            var accounts = LoadAccounts();
            accounts.RemoveAll(a => a.Name == account.Name && a.Username == account.Username);
            SaveAccounts(accounts);
        }
    }
    // ============= RIOT AUTO FILL =============
    public class RiotAutoFill
    {
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);
        private const int SW_RESTORE = 9;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_TAB = 0x09;
        private const byte VK_A = 0x41;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_SPACE = 0x20;
        public static IntPtr FindRiotWindow()
        {
            string[] possibleTitles = { "Riot Client", "Riot Client Main", "League of Legends" };
            foreach (string title in possibleTitles)
            {
                IntPtr handle = FindWindow(null, title);
                if (handle != IntPtr.Zero)
                    return handle;
            }
            return IntPtr.Zero;
        }
        private static void PressKey(byte keyCode)
        {
            byte scanCode = (byte)MapVirtualKey(keyCode, 0);
            keybd_event(keyCode, scanCode, 0, UIntPtr.Zero);
            keybd_event(keyCode, scanCode, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        private static void PressCtrlA()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_A, 0, 0, UIntPtr.Zero);
            keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(5);
        }
        private static void TypeText(string text)
        {
            foreach (char c in text)
            {
                short vk = VkKeyScan(c);
                byte keyCode = (byte)(vk & 0xFF);
                bool needShift = (vk & 0x100) != 0;
                bool needAltGr = (vk & 0x600) == 0x600;
                if (needAltGr)
                {
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(0x12, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(5);
                }
                else if (needShift)
                {
                    keybd_event(0x10, 0, 0, UIntPtr.Zero);
                }
                PressKey(keyCode);
                if (needAltGr)
                {
                    keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(5);
                }
                else if (needShift)
                {
                    keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
        }
        public static async Task<bool> LoginWithAccount(Account account, bool autoLogin, bool staySignedIn)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IntPtr riotWindow = FindRiotWindow();
                    if (riotWindow == IntPtr.Zero)
                        return false;
                    ShowWindow(riotWindow, SW_RESTORE);
                    SetForegroundWindow(riotWindow);
                    Thread.Sleep(200);
                    string password = AccountManager.DecryptPassword(account.EncryptedPassword);
                    // Username
                    PressCtrlA();
                    TypeText(account.Username);
                    Thread.Sleep(10);
                    // Password
                    PressKey(VK_TAB);
                    Thread.Sleep(10);
                    PressCtrlA();
                    TypeText(password);
                    Thread.Sleep(10);
                    // Stay Signed In checkbox
                    if (staySignedIn)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            PressKey(VK_TAB);
                            Thread.Sleep(5);
                        }
                        Thread.Sleep(20);
                        PressKey(VK_SPACE);
                        Thread.Sleep(20);
                    }
                    // Auto Login
                    if (autoLogin)
                    {
                        if (staySignedIn)
                            PressKey(VK_TAB);
                        Thread.Sleep(20);
                        PressKey(VK_RETURN);
                        Thread.Sleep(50);
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
    // ============= MAIN FORM =============
    public partial class MainForm : Form
    {
        private DataGridView accountGridView;
        private Button btnLogin, btnAdd, btnDelete;
        private Label lblTitle, lblStatus;
        private CheckBox chkAutoLogin, chkStaySignedIn;
        public MainForm()
        {
            InitializeComponent();
            LoadAccountList();
        }
        private void InitializeComponent()
        {
            this.Text = "Riot Account Manager";
            this.Size = new Size(335, 360);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(335, 360);
            this.BackColor = Color.FromArgb(20, 20, 30);
            this.MouseWheel += MainForm_MouseWheel;
            // Riot icon
            try
            {
                this.Icon = new Icon("riot.ico");
            }
            catch
            {
                this.Icon = SystemIcons.Application;
            }
            // Title
            lblTitle = new Label
            {
                Text = "RIOT LOGIN",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Size = new Size(300, 22),
                Location = new Point(10, 10),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblTitle);
            // DataGridView
            accountGridView = new DataGridView
            {
                Location = new Point(10, 40),
                Size = new Size(315, 120),
                Font = new Font("Segoe UI", 8f),
                BackgroundColor = Color.FromArgb(20, 20, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.None,
                AllowUserToResizeRows = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            accountGridView.Columns.Add("Name", "Account");
            accountGridView.Columns.Add("Username", "Username");
            accountGridView.Columns[0].Width = 148;
            accountGridView.Columns[1].Width = 151;
            accountGridView.Columns[0].Resizable = DataGridViewTriState.True;
            accountGridView.Columns[1].Resizable = DataGridViewTriState.True;
            accountGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            accountGridView.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            accountGridView.Columns[1].SortMode = DataGridViewColumnSortMode.NotSortable;
            accountGridView.DefaultCellStyle.BackColor = Color.FromArgb(20, 20, 30);
            accountGridView.DefaultCellStyle.ForeColor = Color.White;
            accountGridView.GridColor = Color.FromArgb(60, 60, 70);
            accountGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 40);
            accountGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            accountGridView.ColumnHeadersHeight = 20;
            accountGridView.RowTemplate.Height = 18;
            accountGridView.CellDoubleClick += AccountGridView_CellDoubleClick;
            this.Controls.Add(accountGridView);
            // Checkboxes
            chkAutoLogin = new CheckBox
            {
                Text = "⚡ Auto Login",
                Location = new Point(10, 178),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.White,
                Enabled = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            this.Controls.Add(chkAutoLogin);
            chkStaySignedIn = new CheckBox
            {
                Text = "🔒 Stay Signed In",
                Location = new Point(10, 198),
                Size = new Size(300, 20),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.White,
                Enabled = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            this.Controls.Add(chkStaySignedIn);
            // Buttons
            btnLogin = CreateButton("🚀 Login", 10, 224, 297, Color.FromArgb(0, 120, 215), true);
            btnLogin.Click += BtnLogin_Click;
            btnAdd = CreateButton("➕ Add", 10, 258, 145, Color.FromArgb(0, 150, 80), false);
            btnAdd.Click += BtnAdd_Click;
            btnDelete = CreateButton("🗑️ Delete", 165, 258, 142, Color.FromArgb(200, 50, 50), false);
            btnDelete.Click += BtnDelete_Click;
            // Status
            lblStatus = new Label
            {
                Text = "Ready",
                Font = new Font("Segoe UI", 7.75f),
                ForeColor = Color.LightGray,
                Size = new Size(300, 14),
                Location = new Point(10, 290),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblStatus);
            // Creator signature
            Label lblCreator = new Label
            {
                Text = "leatern",
                Font = new Font("Segoe UI", 7f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 130),
                Size = new Size(100, 12),
                Location = new Point(220, 305),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            this.Controls.Add(lblCreator);
        }
        private void MainForm_MouseWheel(object sender, MouseEventArgs e)
        {
            Point mousePos = accountGridView.PointToClient(Control.MousePosition);
            if (accountGridView.ClientRectangle.Contains(mousePos))
            {
                int newIndex = accountGridView.FirstDisplayedScrollingRowIndex;
                newIndex -= Math.Sign(e.Delta);
                if (newIndex < 0) newIndex = 0;
                int maxIndex = Math.Max(0, accountGridView.RowCount - accountGridView.DisplayedRowCount(false));
                if (newIndex > maxIndex) newIndex = maxIndex;
                accountGridView.FirstDisplayedScrollingRowIndex = newIndex;
            }
        }
        private Button CreateButton(string text, int x, int y, int width, Color color, bool bold)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, bold ? 26 : 24),
                Font = new Font("Segoe UI", bold ? 8.5f : 7.75f, bold ? FontStyle.Bold : FontStyle.Regular),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btn.FlatAppearance.BorderSize = 0;
            this.Controls.Add(btn);
            return btn;
        }
        private void LoadAccountList()
        {
            accountGridView.Rows.Clear();
            var accounts = AccountManager.LoadAccounts();
            foreach (var account in accounts)
                accountGridView.Rows.Add(account.Name, account.Username);
            lblStatus.Text = $"{accounts.Count} account(s) saved";
        }
        private void AccountGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var accounts = AccountManager.LoadAccounts();
                if (e.RowIndex >= accounts.Count) return;
                var selectedAccount = accounts[e.RowIndex];
                var editForm = new AddAccountForm(selectedAccount);
                if (editForm.ShowDialog() == DialogResult.OK)
                    LoadAccountList();
            }
        }
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            if (accountGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("Please select an account!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var accounts = AccountManager.LoadAccounts();
            int selectedIndex = accountGridView.SelectedRows[0].Index;
            if (selectedIndex >= accounts.Count) return;
            var selectedAccount = accounts[selectedIndex];
            lblStatus.Text = "Logging in...";
            lblStatus.ForeColor = Color.Yellow;
            btnLogin.Enabled = false;
            bool success = await RiotAutoFill.LoginWithAccount(selectedAccount, chkAutoLogin.Checked, chkStaySignedIn.Checked);
            if (success)
            {
                string message = chkAutoLogin.Checked
                    ? $"✓ Auto-logged in with {selectedAccount.Name}!"
                    : $"✓ Credentials filled for {selectedAccount.Name}!";
                lblStatus.Text = message;
                lblStatus.ForeColor = Color.LightGreen;
            }
            else
            {
                lblStatus.Text = "❌ Riot Client not found!";
                lblStatus.ForeColor = Color.Red;
                MessageBox.Show("Riot Client window not found!\n\nPlease open Riot Client first.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            btnLogin.Enabled = true;
            await Task.Delay(3000);
            lblStatus.Text = "Ready";
            lblStatus.ForeColor = Color.LightGray;
        }
        private void BtnAdd_Click(object sender, EventArgs e)
        {
            var addForm = new AddAccountForm();
            if (addForm.ShowDialog() == DialogResult.OK)
                LoadAccountList();
        }
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (accountGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("Please select an account to delete!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var accounts = AccountManager.LoadAccounts();
            int selectedIndex = accountGridView.SelectedRows[0].Index;
            if (selectedIndex >= accounts.Count) return;
            var selectedAccount = accounts[selectedIndex];
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{selectedAccount.Name}' account?",
                "Delete Account", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                AccountManager.DeleteAccount(selectedAccount);
                LoadAccountList();
            }
        }
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
    // ============= ADD/EDIT ACCOUNT FORM =============
    public class AddAccountForm : Form
    {
        private TextBox txtName, txtUsername, txtPassword;
        private Button btnSave, btnCancel, btnTogglePassword;
        private Account _editingAccount;
        private bool passwordVisible = false;
        public AddAccountForm(Account account = null)
        {
            _editingAccount = account;
            InitializeComponent();
            if (_editingAccount != null)
            {
                this.Text = "Edit Account";
                txtName.Text = _editingAccount.Name;
                txtUsername.Text = _editingAccount.Username;
                txtPassword.Text = AccountManager.DecryptPassword(_editingAccount.EncryptedPassword);
            }
        }
        private void InitializeComponent()
        {
            this.Text = "Add Account";
            this.Size = new Size(320, 255);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(20, 20, 30);
            CreateLabel("Account Name:", 20, 18);
            txtName = CreateTextBox(20, 38);
            CreateLabel("Username:", 20, 72);
            txtUsername = CreateTextBox(20, 92);
            CreateLabel("Password:", 20, 126);
            txtPassword = CreateTextBox(20, 146);
            txtPassword.Size = new Size(220, 24);
            txtPassword.UseSystemPasswordChar = true;
            btnTogglePassword = new Button
            {
                Text = "👁️",
                Location = new Point(245, 146),
                Size = new Size(35, 24),
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(60, 60, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnTogglePassword.FlatAppearance.BorderSize = 0;
            btnTogglePassword.Click += BtnTogglePassword_Click;
            this.Controls.Add(btnTogglePassword);
            btnSave = new Button
            {
                Text = "💾 Save",
                Location = new Point(20, 182),
                Size = new Size(120, 28),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 150, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
            btnCancel = new Button
            {
                Text = "❌ Cancel",
                Location = new Point(160, 182),
                Size = new Size(120, 28),
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(100, 100, 110),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(btnCancel);
        }
        private void CreateLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(260, 16),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.White
            };
            this.Controls.Add(lbl);
        }
        private TextBox CreateTextBox(int x, int y)
        {
            var txt = new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(260, 24),
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(40, 40, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(txt);
            return txt;
        }
        private void BtnTogglePassword_Click(object sender, EventArgs e)
        {
            passwordVisible = !passwordVisible;
            txtPassword.UseSystemPasswordChar = !passwordVisible;
            btnTogglePassword.Text = passwordVisible ? "🙈" : "👁️";
        }
        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Please enter an account name!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                MessageBox.Show("Please enter a username!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show("Please enter a password!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_editingAccount != null)
            {
                AccountManager.UpdateAccount(_editingAccount, txtName.Text,
                    txtUsername.Text, txtPassword.Text);
            }
            else
            {
                AccountManager.AddAccount(txtName.Text, txtUsername.Text, txtPassword.Text);
            }
            this.DialogResult = DialogResult.OK;
        }
    }
}
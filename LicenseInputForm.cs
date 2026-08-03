using System;
using System.Windows.Forms;

namespace ccd_helper
{
    public class LicenseInputForm : Form
    {
        public string LicenseCode { get; private set; } = "";

        private TextBox txtLicense;
        private Button btnOK;
        private Button btnCancel;

        public LicenseInputForm()
        {
            this.Text = "授权验证";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Size = new System.Drawing.Size(420, 160);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 3;
            layout.Padding = new Padding(15);

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            // 提示（跨两列）
            Label lblPrompt = new Label();
            lblPrompt.Text = "请输入授权码：";
            lblPrompt.Font = new System.Drawing.Font("微软雅黑", 10);
            lblPrompt.Dock = DockStyle.Fill;
            layout.Controls.Add(lblPrompt, 0, 0);
            layout.SetColumnSpan(lblPrompt, 2);

            // 输入框（跨两列）
            txtLicense = new TextBox();
            txtLicense.Dock = DockStyle.Fill;
            txtLicense.Font = new System.Drawing.Font("微软雅黑", 10);
            txtLicense.PasswordChar = '\0';
            txtLicense.TextChanged += (s, e) => btnOK.Enabled = !string.IsNullOrWhiteSpace(txtLicense.Text);
            layout.Controls.Add(txtLicense, 0, 1);
            layout.SetColumnSpan(txtLicense, 2);

            // ---- 按钮行：1:2:1 比例 ----
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            btnCancel = new Button { Text = "取消", Width = 150, Height = 50, Anchor = AnchorStyles.Left };
            btnOK = new Button { Text = "确定", Width = 150, Height = 50, Anchor = AnchorStyles.Right };
            buttonLayout.Controls.Add(btnOK, 2, 0);
            buttonLayout.Controls.Add(btnCancel, 0, 0);

            layout.Controls.Add(buttonLayout, 0, 2);
            layout.SetColumnSpan(buttonLayout, 2);

            this.Controls.Add(layout);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            btnOK.Click += (s, e) =>
            {
                LicenseCode = txtLicense.Text.Trim();
                this.DialogResult = DialogResult.OK;
            };
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
        }
    }
}
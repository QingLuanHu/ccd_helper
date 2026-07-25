using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ccd_helper
{
    public class ResultForm : Form
    {
        public string SN { get; private set; } = "";
        public List<string> SelectedReasons { get; private set; } = new List<string>();

        private TextBox txtSN;
        private CheckedListBox clbReasons;
        private Button btnOK;
        private Button btnCancel;
        private readonly bool _isPass;

        public ResultForm(string resultType, List<string> reasons)
        {
            _isPass = resultType == "良品";
            this.Text = resultType + " - 记录结果";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.Size = LoadWindowSize();

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 3;
            layout.Padding = new Padding(15);

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // SN号
            Label lblSN = new Label { Text = "SN号：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            txtSN = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(lblSN, 0, 0);
            layout.Controls.Add(txtSN, 1, 0);

            // 不良原因
            Label lblReasons = new Label { Text = "不良原因：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            clbReasons = new CheckedListBox { Dock = DockStyle.Fill, Enabled = !_isPass };
            if (reasons == null || reasons.Count == 0)
            {
                clbReasons.Items.Add("（未配置不良原因）");
                clbReasons.Enabled = false;
            }
            else
            {
                clbReasons.Items.AddRange(reasons.ToArray());
            }
            layout.Controls.Add(lblReasons, 0, 1);
            layout.Controls.Add(clbReasons, 1, 1);

            // ---- 按钮行：1:2:1 比例 ----
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            btnCancel = new Button { Text = "取消", Width = 80, Height = 30, Anchor = AnchorStyles.Left };
            btnOK = new Button { Text = "确定", Width = 80, Height = 30, Anchor = AnchorStyles.Right };

            buttonLayout.Controls.Add(btnOK, 2, 0);
            buttonLayout.Controls.Add(btnCancel, 0, 0);

            layout.Controls.Add(buttonLayout, 0, 2);
            layout.SetColumnSpan(buttonLayout, 2);

            this.Controls.Add(layout);

            // 事件
            btnOK.Click += BtnOK_Click;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.FormClosing += (s, e) => SaveWindowSize(this.Size);
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            SN = txtSN.Text.Trim();
            SelectedReasons.Clear();
            if (clbReasons.Enabled)
            {
                foreach (var item in clbReasons.CheckedItems)
                {
                    if (item.ToString() != "（未配置不良原因）")
                        SelectedReasons.Add(item.ToString());
                }
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private static Size LoadWindowSize()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\ccd_helper\ResultForm");
                if (key != null)
                {
                    int w = (int)key.GetValue("Width", 350);
                    int h = (int)key.GetValue("Height", 320);
                    if (w > 100 && h > 100)
                        return new Size(w, h);
                }
            }
            catch { }
            return new Size(350, 320);
        }

        private static void SaveWindowSize(Size size)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\ccd_helper\ResultForm");
                if (key != null)
                {
                    key.SetValue("Width", size.Width);
                    key.SetValue("Height", size.Height);
                }
            }
            catch { }
        }
    }
}
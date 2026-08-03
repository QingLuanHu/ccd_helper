using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ccd_helper
{
    public class PhotoForm : Form
    {
        private Bitmap _screenshot;
        private PictureBox picPreview;
        private TextBox txtAnnotation;
        private TextBox txtSavePath;
        private Button btnBrowse;
        private Button btnOK;
        private Button btnCancel;

        // 默认保存路径
        private const string DefaultSaveDir = @"D:\ccd_helper\Photo";

        public PhotoForm(Bitmap screenshot)
        {
            _screenshot = screenshot;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "截图保存";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;  // 允许自由拖动调整大小
            this.Size = new Size(600, 500);
            this.MinimumSize = new Size(450, 400);

            // 主布局：TableLayoutPanel（3列 × 4行）
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 4;
            layout.Padding = new Padding(10);

            // 列宽：第1列标签（100px），第2列输入框（自动），第3列按钮（80px）
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            // 行高：第0行预览（自动撑满），第1行标注，第2行路径，第3行按钮（固定）
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            // ---- 第0行：图片预览（占3列） ----
            picPreview = new PictureBox();
            picPreview.Dock = DockStyle.Fill;
            picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            picPreview.Image = _screenshot;
            layout.Controls.Add(picPreview, 0, 0);
            layout.SetColumnSpan(picPreview, 3);

            // ---- 第1行：标注 ----
            Label lblAnnotation = new Label
            {
                Text = "标注：",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            txtAnnotation = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(lblAnnotation, 0, 1);
            layout.Controls.Add(txtAnnotation, 1, 1);

            // ---- 第2行：保存地址 ----
            Label lblPath = new Label
            {
                Text = "保存地址：",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            txtSavePath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            // 设置默认路径
            if (!Directory.Exists(DefaultSaveDir))
                Directory.CreateDirectory(DefaultSaveDir);
            txtSavePath.Text = DefaultSaveDir;

            btnBrowse = new Button { Text = "浏览...", Dock = DockStyle.Fill };
            btnBrowse.Click += BtnBrowse_Click;

            layout.Controls.Add(lblPath, 0, 2);
            layout.Controls.Add(txtSavePath, 1, 2);
            layout.Controls.Add(btnBrowse, 2, 2);

            // ---- 第3行：按钮（取消 / 确认） ----
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

            // 取消放第1列，确认放第2列
            layout.Controls.Add(buttonLayout, 0, 3);
            layout.SetColumnSpan(buttonLayout, 3);

            this.Controls.Add(layout);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择保存图片的文件夹";
                fbd.SelectedPath = txtSavePath.Text;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtSavePath.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            string folder = txtSavePath.Text.Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("保存路径无效，请重新选择！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string annotation = txtAnnotation.Text.Trim();
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string fileName = string.IsNullOrEmpty(annotation)
                ? $"截图_{timestamp}.png"
                : $"{annotation}_{timestamp}.png";
            string fullPath = Path.Combine(folder, fileName);

            try
            {
                _screenshot.Save(fullPath, ImageFormat.Png);
                MessageBox.Show($"截图已保存至：\n{fullPath}", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_screenshot != null)
                {
                    _screenshot.Dispose();
                    _screenshot = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
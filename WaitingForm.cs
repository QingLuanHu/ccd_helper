using System;
using System.Drawing;
using System.Windows.Forms;

namespace ccd_helper
{
    public class WaitingForm : Form
    {
        private readonly Label _label;

        public WaitingForm()
        {
            this.Text = "";
            this.Size = new Size(360, 120);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.TopMost = true;

            _label = new Label
            {
                Text = "正在启动，请稍候…",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei", 14F),
                ForeColor = Color.Black
            };
            this.Controls.Add(_label);

            this.Load += WaitingForm_Load;
        }

        private void WaitingForm_Load(object sender, EventArgs e)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // 启动时非静默同步（会弹窗）
                    CloudSync.SyncCloud(out string _, silent: false);
                }
                catch { }
                finally
                {
                    this.Invoke(new Action(() =>
                    {
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }));
                }
            });
        }
    }
}
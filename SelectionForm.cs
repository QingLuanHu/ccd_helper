using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DirectShowLib;

namespace ccd_helper
{
    public class SelectionForm : Form
    {
        public string SelectedProject { get; private set; } = "";
        public string SelectedVersion { get; private set; } = "";
        public string SelectedCamera { get; private set; } = "";
        public int TargetFps { get; private set; } = 30;
        public int RefreshIntervalMinutes { get; private set; } = 5;

        private ComboBox cmbProject, cmbVersion, cmbCamera;
        private NumericUpDown nudFps, nudRefresh;
        private Button btnOK, btnCancel;
        private Dictionary<string, List<string>> projectVersions;
        private List<string> cameraNames;

        public SelectionForm()
        {
            this.Text = "选择检测配置";
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(480, 350);
            this.Size = new Size(580, 400);
            this.FormBorderStyle = FormBorderStyle.Sizable;

            string? dataDir = FindDataPath();
            if (dataDir == null)
            {
                MessageBox.Show("Data 文件夹不存在！", "错误");
                this.DialogResult = DialogResult.Cancel;
                return;
            }

            // 扫描项目/版本
            var projectDirs = Directory.GetDirectories(dataDir);
            var projectNames = projectDirs.Select(Path.GetFileName).ToList();
            projectVersions = new Dictionary<string, List<string>>();
            foreach (var projDir in projectDirs)
            {
                var versionDirs = Directory.GetDirectories(projDir);
                var validVersions = new List<string>();
                foreach (var verDir in versionDirs)
                {
                    if (File.Exists(Path.Combine(verDir, "config.json")))
                        validVersions.Add(Path.GetFileName(verDir));
                }
                if (validVersions.Count > 0)
                    projectVersions[Path.GetFileName(projDir)] = validVersions;
            }

            if (projectVersions.Count == 0)
            {
                MessageBox.Show("没有找到任何包含 config.json 的项目版本！", "错误");
                this.DialogResult = DialogResult.Cancel;
                return;
            }

            // 扫描摄像头
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            cameraNames = devices.Select(d => d.Name).ToList();
            if (cameraNames.Count == 0)
            {
                MessageBox.Show("未检测到任何摄像头！", "错误");
                this.DialogResult = DialogResult.Cancel;
                return;
            }

            BuildUI(projectNames);
        }

        private void BuildUI(List<string> projectNames)
        {
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 6;
            mainLayout.Padding = new Padding(20);

            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

            // 项目
            Label lblProject = new Label { Text = "项目：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            cmbProject = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbProject.Items.AddRange(projectNames.ToArray());
            cmbProject.SelectedIndexChanged += (s, e) =>
            {
                string proj = cmbProject.SelectedItem?.ToString() ?? "";
                cmbVersion.Items.Clear();
                if (projectVersions.ContainsKey(proj))
                {
                    cmbVersion.Items.AddRange(projectVersions[proj].ToArray());
                    if (cmbVersion.Items.Count > 0) cmbVersion.SelectedIndex = 0;
                }
            };
            mainLayout.Controls.Add(lblProject, 0, 0);
            mainLayout.Controls.Add(cmbProject, 1, 0);

            // 版本
            Label lblVersion = new Label { Text = "版本：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            cmbVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            mainLayout.Controls.Add(lblVersion, 0, 1);
            mainLayout.Controls.Add(cmbVersion, 1, 1);

            // 摄像头
            Label lblCamera = new Label { Text = "摄像头：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            cmbCamera = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbCamera.Items.AddRange(cameraNames.ToArray());
            if (cmbCamera.Items.Count > 0) cmbCamera.SelectedIndex = 0;
            mainLayout.Controls.Add(lblCamera, 0, 2);
            mainLayout.Controls.Add(cmbCamera, 1, 2);

            // 帧率
            Label lblFps = new Label { Text = "目标帧率 (FPS)：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            nudFps = new NumericUpDown { Minimum = 5, Maximum = 60, Value = 30, Increment = 5, Dock = DockStyle.Fill };
            mainLayout.Controls.Add(lblFps, 0, 3);
            mainLayout.Controls.Add(nudFps, 1, 3);

            // 刷新间隔
            Label lblRefresh = new Label { Text = "刷新间隔 (分钟)：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
            nudRefresh = new NumericUpDown { Minimum = 0, Maximum = 30, Value = 5, Increment = 1, Dock = DockStyle.Fill };
            mainLayout.Controls.Add(lblRefresh, 0, 4);
            mainLayout.Controls.Add(nudRefresh, 1, 4);

            // ---- 按钮行：采用 1:2:1 比例（确定、空白、取消） ----
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));  // 确定
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));  // 空白
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));  // 取消

            btnCancel = new Button { Text = "取消", Width = 80, Height = 30, Anchor = AnchorStyles.Left };
            btnOK = new Button { Text = "确定", Width = 80, Height = 30, Anchor = AnchorStyles.Right };

            // 将按钮放入对应列
            buttonLayout.Controls.Add(btnOK, 2, 0);
            buttonLayout.Controls.Add(btnCancel, 0, 0);

            // 占位列不放控件，自动空白

            // 将 buttonLayout 放入主布局的最后一行的第二个单元格（跨两列）
            mainLayout.Controls.Add(buttonLayout, 0, 5);
            mainLayout.SetColumnSpan(buttonLayout, 2);

            this.Controls.Add(mainLayout);

            // 事件
            btnOK.Click += (s, e) =>
            {
                if (cmbProject.SelectedItem == null || cmbVersion.SelectedItem == null || cmbCamera.SelectedItem == null)
                {
                    MessageBox.Show("请完整选择项目、版本和摄像头！", "提示");
                    this.DialogResult = DialogResult.None;
                    return;
                }
                SelectedProject = cmbProject.SelectedItem.ToString()!;
                SelectedVersion = cmbVersion.SelectedItem.ToString()!;
                SelectedCamera = cmbCamera.SelectedItem.ToString()!;
                TargetFps = (int)nudFps.Value;
                RefreshIntervalMinutes = (int)nudRefresh.Value;
                this.DialogResult = DialogResult.OK;
            };
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            if (projectNames.Count > 0) cmbProject.SelectedIndex = 0;
        }

        private static string? FindDataPath()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Application.StartupPath, "Data"),
                Path.Combine(Environment.CurrentDirectory, "Data"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                    return path;
            }
            return null;
        }
    }
}
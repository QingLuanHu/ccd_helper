using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        // 配置文件路径
        private const string ConfigFilePath = @"D:\ccd_helper\config.json";

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

            // 加载保存的设置
            LoadSavedSettings();
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

            // ---- 按钮行：取消在左，确定在右，1:2:1 ----
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            btnCancel = new Button { Text = "取消", Width = 150, Height = 50, Anchor = AnchorStyles.Left };
            btnOK = new Button { Text = "确定", Width = 150, Height = 50, Anchor = AnchorStyles.Right };

            buttonLayout.Controls.Add(btnCancel, 0, 0);
            buttonLayout.Controls.Add(btnOK, 2, 0);

            mainLayout.Controls.Add(buttonLayout, 0, 5);
            mainLayout.SetColumnSpan(buttonLayout, 2);

            this.Controls.Add(mainLayout);

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

                SaveSettings();
                this.DialogResult = DialogResult.OK;
            };
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            if (projectNames.Count > 0) cmbProject.SelectedIndex = 0;
        }

        // ========== 配置文件读写 ==========
        private class ConfigData
        {
            public string Project { get; set; } = "";
            public string Version { get; set; } = "";
            public string Camera { get; set; } = "";
            public int TargetFps { get; set; } = 30;
            public int RefreshMinutes { get; set; } = 5;
        }

        private void SaveSettings()
        {
            try
            {
                var config = new ConfigData
                {
                    Project = SelectedProject,
                    Version = SelectedVersion,
                    Camera = SelectedCamera,
                    TargetFps = TargetFps,
                    RefreshMinutes = RefreshIntervalMinutes
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

                // 确保目录存在
                string dir = Path.GetDirectoryName(ConfigFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(ConfigFilePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 写入失败不影响主流程，静默忽略或可记录日志
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            }
        }

        private void LoadSavedSettings()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return;

                string json = File.ReadAllText(ConfigFilePath, System.Text.Encoding.UTF8);
                var config = JsonSerializer.Deserialize<ConfigData>(json);
                if (config == null) return;

                // 应用数值
                TargetFps = config.TargetFps;
                RefreshIntervalMinutes = config.RefreshMinutes;
                nudFps.Value = Math.Clamp(TargetFps, 5, 60);
                nudRefresh.Value = Math.Clamp(RefreshIntervalMinutes, 0, 30);

                // 项目
                if (!string.IsNullOrEmpty(config.Project) && cmbProject.Items.Contains(config.Project))
                {
                    cmbProject.SelectedItem = config.Project;
                }

                // 摄像头
                if (!string.IsNullOrEmpty(config.Camera) && cmbCamera.Items.Contains(config.Camera))
                {
                    cmbCamera.SelectedItem = config.Camera;
                }

                // 版本（在项目选中后加载）
                if (!string.IsNullOrEmpty(config.Version) && cmbVersion.Items.Contains(config.Version))
                {
                    cmbVersion.SelectedItem = config.Version;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            }
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
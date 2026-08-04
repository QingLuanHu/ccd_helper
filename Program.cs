using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace ccd_helper
{
    internal static class Program
    {
        private const string LOCAL_SOFTWARE_VERSION = "2.0.2";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();

            const string mutexName = "Global\\796796797";
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行中，请勿重复打开！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 显示启动画面（闪屏），在其中执行云同步
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var splash = new SplashForm())
                {
                    Application.Run(splash);
                }

                // 软件版本验证
                if (!CheckSoftwareVersion())
                {
                    return;
                }

                // 授权验证
                if (!CheckLicenseFile())
                {
                    MessageBox.Show("授权失败！", "授权错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 有效期验证
                if (!CheckExpiry())
                {
                    using var inputDialog = new LicenseInputForm();
                    if (inputDialog.ShowDialog() == DialogResult.OK)
                    {
                        if (!UpdateExpiryWithSequence(inputDialog.LicenseCode))
                        {
                            MessageBox.Show("授权码无效，请重新输入。", "授权失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                // 启动主窗体
                Application.Run(new Form1());
            }
        }

        // ======================== 云同步核心逻辑 ========================

        public static bool SyncCloud(out string effectiveCloudRoot)
        {
            effectiveCloudRoot = null;

            string? dataPath = FindDataPath();
            if (dataPath == null)
            {
                MessageBox.Show("未找到 Data 文件夹！", "错误");
                return false;
            }

            string localManifestPath = Path.Combine(dataPath, "version.json");
            if (!File.Exists(localManifestPath))
            {
                return true;
            }

            VersionManifest localManifest;
            try
            {
                string json = File.ReadAllText(localManifestPath);
                localManifest = JsonSerializer.Deserialize<VersionManifest>(json);
                if (localManifest == null) return true;
            }
            catch
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(localManifest.CloudBasePath))
                return true;

            string cloudRoot = NormalizeCloudPath(localManifest.CloudBasePath);
            effectiveCloudRoot = cloudRoot;

            try
            {
                if (string.Equals(Path.GetFullPath(cloudRoot), Path.GetFullPath(dataPath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            if (!Directory.Exists(cloudRoot))
            {
                MessageBox.Show($"云盘连接异常！\n路径：{cloudRoot}\n将使用本地数据继续运行。", "连接警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string cloudManifestPath = Path.Combine(cloudRoot, "version.json");
            if (!File.Exists(cloudManifestPath))
                return true;

            VersionManifest cloudManifest;
            try
            {
                string cloudJson = File.ReadAllText(cloudManifestPath);
                cloudManifest = JsonSerializer.Deserialize<VersionManifest>(cloudJson);
                if (cloudManifest == null) return true;
            }
            catch
            {
                return true;
            }

            bool needUpdateLocal = false;

            // ★ 修复1：使用 IsNullOrWhiteSpace 判断云端路径是否有效
            string normalizedLocal = NormalizeCloudPath(localManifest.CloudBasePath);
            string normalizedRemote = NormalizeCloudPath(cloudManifest.CloudBasePath);
            if (!string.Equals(normalizedLocal, normalizedRemote, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(normalizedRemote))
            {
                localManifest.CloudBasePath = cloudManifest.CloudBasePath;
                cloudRoot = normalizedRemote;
                effectiveCloudRoot = cloudRoot;
                needUpdateLocal = true;
            }

            // 同步 SoftwareVersion
            if (localManifest.SoftwareVersion != cloudManifest.SoftwareVersion)
            {
                localManifest.SoftwareVersion = cloudManifest.SoftwareVersion;
                needUpdateLocal = true;
            }

            // 收集需要同步的计划（优先检查本地目录是否存在）
            var plansToSync = new List<(string planName, string version)>();
            foreach (var kv in cloudManifest.Plans)
            {
                string planName = kv.Key;
                string cloudVer = kv.Value;

                // 检查本地目标目录是否存在且完整（以 config.json 为准）
                string localPlanVerDir = Path.Combine(dataPath, planName, cloudVer);
                string localConfigPath = Path.Combine(localPlanVerDir, "config.json");
                bool planPhysicallyExists = File.Exists(localConfigPath);

                if (planPhysicallyExists)
                {
                    // 本地已有该版本，无需复制，但确保 manifest 版本正确
                    if (!localManifest.Plans.TryGetValue(planName, out string? localVer) || localVer != cloudVer)
                    {
                        localManifest.Plans[planName] = cloudVer;
                        needUpdateLocal = true;
                    }
                    continue;
                }

                // 目录不存在或不完整，需要同步
                plansToSync.Add((planName, cloudVer));
            }

            // 执行同步（直接复制，不刷新 UI）
            if (plansToSync.Count > 0)
            {
                foreach (var (planName, version) in plansToSync)
                {
                    SyncPlanFromCloud(cloudRoot, dataPath, planName, version);
                }

                foreach (var (planName, version) in plansToSync)
                {
                    localManifest.Plans[planName] = version;
                }
                needUpdateLocal = true;
            }

            if (needUpdateLocal)
            {
                string updatedJson = JsonSerializer.Serialize(localManifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(localManifestPath, updatedJson);
            }

            // ---- 复制 sequence.dat ----
            if (!string.IsNullOrEmpty(effectiveCloudRoot))
            {
                string cloudSeqPath = Path.Combine(effectiveCloudRoot, "sequence.dat");
                string localSeqPath = Path.Combine(dataPath, "sequence.dat");
                if (File.Exists(cloudSeqPath))
                {
                    try
                    {
                        File.Copy(cloudSeqPath, localSeqPath, true);
                    }
                    catch { }
                }
            }

            return true;
        }

        // ======================== 辅助方法 ========================

        // ★ 修复2：空白输入返回空字符串
        private static string NormalizeCloudPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;
            string path = input.Trim().Replace('/', '\\');
            if (path.StartsWith(@"\\")) return path;
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
                return path;
            return @"\\" + path;
        }

        private static void SyncPlanFromCloud(string cloudRoot, string localDataRoot, string planName, string version)
        {
            string sourceDir = Path.Combine(cloudRoot, planName, version);
            if (!Directory.Exists(sourceDir)) return;

            string localPlanRoot = Path.Combine(localDataRoot, planName);
            string targetDir = Path.Combine(localPlanRoot, version);

            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, true); } catch { }
            }

            if (!Directory.Exists(localPlanRoot))
                Directory.CreateDirectory(localPlanRoot);

            CopyDirectoryRecursive(sourceDir, targetDir);
        }

        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string destFile = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string subDir in Directory.GetDirectories(source))
            {
                string destSubDir = Path.Combine(target, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        private static bool CheckSoftwareVersion()
        {
            string? dataPath = FindDataPath();
            if (dataPath == null) return false;
            string manifestPath = Path.Combine(dataPath, "version.json");
            if (!File.Exists(manifestPath))
            {
                MessageBox.Show("缺少 Data/version.json 配置文件！", "错误");
                return false;
            }
            try
            {
                string json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
                if (manifest == null) return false;
                if (manifest.SoftwareVersion != LOCAL_SOFTWARE_VERSION)
                {
                    MessageBox.Show(
                        $"软件版本不匹配！\n当前程序版本：{LOCAL_SOFTWARE_VERSION}\n所需数据版本：{manifest.SoftwareVersion}\n请更新软件。",
                        "版本错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ======================== 授权相关 ========================

        private static bool CheckLicenseFile()
        {
            string licensePath = @"C:\Program Files\ccd_helper\license";
            if (!File.Exists(licensePath)) return false;
            try
            {
                string content = File.ReadAllText(licensePath, System.Text.Encoding.UTF8).Trim();
                return content == "HF796";
            }
            catch { return false; }
        }

        private static bool CheckExpiry()
        {
            string? dataDir = FindDataPath();
            if (dataDir == null) return false;
            string seqPath = Path.Combine(dataDir, "sequence.dat");
            if (!File.Exists(seqPath)) return false;
            try
            {
                string encoded = File.ReadAllText(seqPath, System.Text.Encoding.UTF8).Trim();
                byte[] encrypted = Convert.FromBase64String(encoded);
                byte[] decrypted = XORDecrypt(encrypted);
                string dateStr = System.Text.Encoding.UTF8.GetString(decrypted);
                if (DateTime.TryParse(dateStr, out DateTime expiry))
                {
                    return DateTime.Now <= expiry;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool UpdateExpiryWithSequence(string code)
        {
            try
            {
                byte[] encrypted = Convert.FromBase64String(code.Trim());
                byte[] decrypted = XORDecrypt(encrypted);
                string dateStr = System.Text.Encoding.UTF8.GetString(decrypted);
                if (!DateTime.TryParse(dateStr, out DateTime newExpiry)) return false;
                if (newExpiry <= DateTime.Now) return false;

                string? dataDir = FindDataPath();
                if (dataDir == null) return false;
                string seqPath = Path.Combine(dataDir, "sequence.dat");
                byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(dateStr);
                byte[] newEncrypted = XOREncrypt(plainBytes);
                string newEncoded = Convert.ToBase64String(newEncrypted);
                File.WriteAllText(seqPath, newEncoded, System.Text.Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        private static byte[] XOREncrypt(byte[] data)
        {
            const byte xorKey = 0x3C;
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ xorKey);
            return result;
        }
        private static byte[] XORDecrypt(byte[] data) => XOREncrypt(data);

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

    // ======================== 数据模型 ========================
    public class VersionManifest
    {
        public string CloudBasePath { get; set; } = "";
        public string SoftwareVersion { get; set; } = "";
        public Dictionary<string, string> Plans { get; set; } = new();
    }

    // ======================== 闪屏窗体（SplashForm） ========================
    public class SplashForm : Form
    {
        private readonly Label _label;

        public SplashForm()
        {
            this.Text = "正在启动";
            this.Size = new System.Drawing.Size(400, 140); // 稍大一些
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;

            _label = new Label
            {
                Text = "正在从云盘同步数据，请稍候…",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font("Microsoft YaHei", 14F) // 更大字体
            };
            this.Controls.Add(_label);

            this.Load += SplashForm_Load;
        }

        private void SplashForm_Load(object sender, EventArgs e)
        {
            // 在后台线程执行，防止界面卡顿
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // 执行云同步（此过程可能较慢）
                    bool ok = Program.SyncCloud(out string effectiveCloudRoot);
                    // 同步结果不在此处处理，由后续校验逻辑决定
                }
                catch (Exception ex)
                {
                    // 记录错误但闪屏仍关闭，后续校验会提示
                }
                finally
                {
                    // 关闭闪屏窗体（必须在 UI 线程）
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
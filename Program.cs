using System;
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
        // 软件版本
        private const string LOCAL_SOFTWARE_VERSION = "2.0.2";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();

            // 1. 单进程检验
            const string mutexName = "Global\\796796797";
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行中，请勿重复打开！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 2. 固定 License 检验
                if (!CheckLicenseFile())
                {
                    MessageBox.Show("授权失败！", "授权错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 3. 有效期授权检验
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

                // 4. 云盘同步（含路径自更新）
                bool cloudAvailable = SyncCloud();

                // 若云盘不可达，已弹窗提示，继续运行（使用本地数据）
                // 5. 软件版本强制校验（阻断式）
                if (!CheckSoftwareVersion())
                {
                    // 内部已弹窗并退出
                    return;
                }

                // 6. 正常启动主窗体
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
        }

        // ======================== 云同步核心逻辑 ========================

        /// <summary>
        /// 执行云同步（静默模式不弹窗），返回 true 表示成功或无需同步，false 表示云盘连接异常
        /// </summary>
        public static bool SyncCloud(bool silent = false)
        {
            string? dataPath = FindDataPath();
            if (dataPath == null)
            {
                if (!silent) MessageBox.Show("未找到 Data 文件夹！", "错误");
                return false;
            }

            string localManifestPath = Path.Combine(dataPath, "version.json");
            if (!File.Exists(localManifestPath))
            {
                // 没有 version.json 视为无云配置，静默跳过
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

            // 规范化云路径
            string cloudRoot = NormalizeCloudPath(localManifest.CloudBasePath);

            // 自引用检测：若云端路径指向本地 Data 自身，则跳过同步
            try
            {
                if (string.Equals(Path.GetFullPath(cloudRoot), Path.GetFullPath(dataPath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            // 检查云盘可达性
            if (!Directory.Exists(cloudRoot))
            {
                if (!silent)
                    MessageBox.Show($"云盘连接异常！\n路径：{cloudRoot}\n将使用本地数据继续运行。", "连接警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 读取云端 version.json
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

            // ----- 同步 CloudBasePath -----
            string normalizedLocal = NormalizeCloudPath(localManifest.CloudBasePath);
            string normalizedRemote = NormalizeCloudPath(cloudManifest.CloudBasePath);
            if (!string.Equals(normalizedLocal, normalizedRemote, StringComparison.OrdinalIgnoreCase))
            {
                // 更新本地路径（保留云端原始写法）
                localManifest.CloudBasePath = cloudManifest.CloudBasePath;
                // 将当前使用的 cloudRoot 切换为新路径
                cloudRoot = normalizedRemote;
                needUpdateLocal = true;
            }

            // ----- 同步 SoftwareVersion -----
            if (localManifest.SoftwareVersion != cloudManifest.SoftwareVersion)
            {
                localManifest.SoftwareVersion = cloudManifest.SoftwareVersion;
                needUpdateLocal = true;
            }

            // ----- 同步各计划版本 -----
            foreach (var kv in cloudManifest.Plans)
            {
                string planName = kv.Key;
                string cloudVer = kv.Value;

                if (!localManifest.Plans.TryGetValue(planName, out string? localVer) || localVer != cloudVer)
                {
                    // 从云端（使用新的 cloudRoot）复制该计划的最新版本到本地
                    SyncPlanFromCloud(cloudRoot, dataPath, planName, cloudVer);
                    localManifest.Plans[planName] = cloudVer;
                    needUpdateLocal = true;
                }
            }

            // 删除云端已移除的计划
            var plansToRemove = localManifest.Plans.Keys.Except(cloudManifest.Plans.Keys).ToList();
            foreach (var plan in plansToRemove)
            {
                localManifest.Plans.Remove(plan);
                string localPlanDir = Path.Combine(dataPath, plan);
                if (Directory.Exists(localPlanDir))
                {
                    try { Directory.Delete(localPlanDir, true); } catch { }
                }
                needUpdateLocal = true;
            }

            // 保存更新后的本地 version.json
            if (needUpdateLocal)
            {
                string updatedJson = JsonSerializer.Serialize(localManifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(localManifestPath, updatedJson);
            }

            return true;
        }

        // ======================== 辅助方法 ========================

        /// <summary>
        /// 路径规范化：自动识别本地盘符、UNC，并统一斜杠
        /// </summary>
        private static string NormalizeCloudPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            string path = input.Trim().Replace('/', '\\');

            // 已是 UNC 双反斜杠
            if (path.StartsWith(@"\\")) return path;

            // 本地盘符（如 C:\ 或 D:\）
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
                return path;

            // 其他情况（如 192.168.1.100\share），自动补全 UNC
            return @"\\" + path;
        }

        /// <summary>
        /// 从云端复制指定计划的指定版本到本地（会先删除该计划下所有旧版本）
        /// </summary>
        private static void SyncPlanFromCloud(string cloudRoot, string localDataRoot, string planName, string version)
        {
            string sourceDir = Path.Combine(cloudRoot, planName, version);
            if (!Directory.Exists(sourceDir)) return;

            string localPlanRoot = Path.Combine(localDataRoot, planName);
            string targetDir = Path.Combine(localPlanRoot, version);

            // 删除该计划下所有旧版本目录（只保留最新的）
            if (Directory.Exists(localPlanRoot))
            {
                foreach (var dir in Directory.GetDirectories(localPlanRoot))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(localPlanRoot);
            }

            // 递归复制
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

        /// <summary>
        /// 软件版本强制校验（阻断式）
        /// </summary>
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

                string currentVersion = LOCAL_SOFTWARE_VERSION;
                if (manifest.SoftwareVersion != currentVersion)
                {
                    MessageBox.Show(
                        $"软件版本不匹配！\n当前程序版本：{currentVersion}\n所需数据版本：{manifest.SoftwareVersion}\n请更新软件。",
                        "版本错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ======================== 授权相关（原样保留） ========================

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

    // ======================== 数据模型（VersionManifest） ========================
    public class VersionManifest
    {
        public string CloudBasePath { get; set; } = "";
        public string SoftwareVersion { get; set; } = "";
        public Dictionary<string, string> Plans { get; set; } = new();
    }
}
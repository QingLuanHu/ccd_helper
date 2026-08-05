using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace ccd_helper
{
    public static class LicenseValidator
    {
        /// <summary>
        /// 执行全部有效性检查：版本 → 固定License → 有效期（过期弹窗输入）
        /// 全部通过返回 true，否则返回 false（调用方应退出或中止流程）
        /// </summary>
        public static bool ValidateAll(string expectedVersion)
        {
            if (!CheckSoftwareVersion(expectedVersion))
                return false;

            if (!CheckLicenseFile())
            {
                MessageBox.Show("授权失败！", "授权错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!CheckExpiry())
            {
                // 过期则弹窗要求输入授权码
                using var inputDialog = new LicenseInputForm();
                if (inputDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!UpdateExpiryWithSequence(inputDialog.LicenseCode))
                    {
                        MessageBox.Show("授权码无效，请重新输入。", "授权失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    // 输入成功，继续
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        // ---------- 以下为原有检查方法，保留为 private，仅内部调用 ----------
        private static bool CheckSoftwareVersion(string expectedVersion)
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
                if (manifest.SoftwareVersion != expectedVersion)
                {
                    MessageBox.Show(
                        $"软件版本不匹配！\n当前程序版本：{expectedVersion}\n所需数据版本：{manifest.SoftwareVersion}\n请更新软件。",
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

        private static bool CheckLicenseFile()
        {
            string licensePath = @"C:\Program Files\ccd_helper\license";
            if (!File.Exists(licensePath)) return false;
            try
            {
                string content = File.ReadAllText(licensePath, Encoding.UTF8).Trim();
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
                string encoded = File.ReadAllText(seqPath, Encoding.UTF8).Trim();
                byte[] encrypted = Convert.FromBase64String(encoded);
                byte[] decrypted = XORDecrypt(encrypted);
                string dateStr = Encoding.UTF8.GetString(decrypted);
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
                string dateStr = Encoding.UTF8.GetString(decrypted);
                if (!DateTime.TryParse(dateStr, out DateTime newExpiry)) return false;
                if (newExpiry <= DateTime.Now) return false;

                string? dataDir = FindDataPath();
                if (dataDir == null) return false;
                string seqPath = Path.Combine(dataDir, "sequence.dat");
                byte[] plainBytes = Encoding.UTF8.GetBytes(dateStr);
                byte[] newEncrypted = XOREncrypt(plainBytes);
                string newEncoded = Convert.ToBase64String(newEncrypted);
                File.WriteAllText(seqPath, newEncoded, Encoding.UTF8);
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
}
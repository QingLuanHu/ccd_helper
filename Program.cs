using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ccd_helper
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // 单实例检测
            const string mutexName = "Global\\796796797";
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行中，请勿重复打开！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 1. 检查固定密钥 license 文件（路径固定）
                if (!CheckLicenseFile())
                {
                    MessageBox.Show("授权失败！请确保 C:\\ccd_helper\\license 文件存在且内容正确。", "授权错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 2. 检查有效期（sequence.dat）
                if (!CheckExpiry())
                {
                    // 过期或不存在，弹出授权码输入框
                    using var inputDialog = new LicenseInputForm();
                    if (inputDialog.ShowDialog() == DialogResult.OK)
                    {
                        string code = inputDialog.LicenseCode;
                        if (UpdateExpiryWithSequence(code))
                        {
                            // 验证成功，继续启动
                        }
                        else
                        {
                            MessageBox.Show("授权码无效，请重新输入。", "授权失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            // 可考虑循环输入，为简化此处退出
                            return;
                        }
                    }
                    else
                    {
                        // 用户取消
                        return;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
        }

        private static bool CheckLicenseFile()
        {
            string licensePath = @"C:\Program Files\ccd_helper\license";
            if (!File.Exists(licensePath))
                return false;
            try
            {
                string content = File.ReadAllText(licensePath, System.Text.Encoding.UTF8).Trim();
                return content == "HF796";
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckExpiry()
        {
            // 查找 Data 目录
            string? dataDir = FindDataPath();
            if (dataDir == null)
                return false;
            string seqPath = Path.Combine(dataDir, "sequence.dat");
            if (!File.Exists(seqPath))
                return false;

            try
            {
                string encoded = File.ReadAllText(seqPath, System.Text.Encoding.UTF8).Trim();
                // 解密：Base64解码 -> XOR解密 -> 日期字符串
                byte[] encrypted = Convert.FromBase64String(encoded);
                byte[] decrypted = XORDecrypt(encrypted);
                string dateStr = System.Text.Encoding.UTF8.GetString(decrypted);
                if (DateTime.TryParse(dateStr, out DateTime expiry))
                {
                    return DateTime.Now <= expiry;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool UpdateExpiryWithSequence(string code)
        {
            try
            {
                byte[] encrypted = Convert.FromBase64String(code.Trim());
                byte[] decrypted = XORDecrypt(encrypted);
                string dateStr = System.Text.Encoding.UTF8.GetString(decrypted);
                if (!DateTime.TryParse(dateStr, out DateTime newExpiry))
                    return false;
                if (newExpiry <= DateTime.Now)
                    return false;

                // 写入新的 sequence.dat
                string? dataDir = FindDataPath();
                if (dataDir == null)
                    return false;
                string seqPath = Path.Combine(dataDir, "sequence.dat");
                // 重新加密
                byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(dateStr);
                byte[] newEncrypted = XOREncrypt(plainBytes);
                string newEncoded = Convert.ToBase64String(newEncrypted);
                File.WriteAllText(seqPath, newEncoded, System.Text.Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] XOREncrypt(byte[] data)
        {
            const byte xorKey = 0x3C;
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ xorKey);
            return result;
        }

        private static byte[] XORDecrypt(byte[] data)
        {
            // XOR 是对称的，加密和解密使用同一函数
            return XOREncrypt(data);
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ccd_helper
{
    public static class CloudSync
    {
        public static bool SyncCloud(out string effectiveCloudRoot, bool silent = false)
        {
            effectiveCloudRoot = null;

            string? dataPath = FindDataPath();
            if (dataPath == null)
            {
                if (!silent) MessageBox.Show("未找到 Data 文件夹！", "错误");
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
                if (!silent) MessageBox.Show($"云盘连接异常！\n路径：{cloudRoot}\n将使用本地数据继续运行。", "连接警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            string normalizedLocal = NormalizeCloudPath(localManifest.CloudBasePath);
            string normalizedRemote = NormalizeCloudPath(cloudManifest.CloudBasePath);
            if (!string.Equals(normalizedLocal, normalizedRemote, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(normalizedRemote))
            {
                localManifest.CloudBasePath = cloudManifest.CloudBasePath;
                cloudRoot = normalizedRemote;
                effectiveCloudRoot = cloudRoot;
                needUpdateLocal = true;
            }

            if (localManifest.SoftwareVersion != cloudManifest.SoftwareVersion)
            {
                localManifest.SoftwareVersion = cloudManifest.SoftwareVersion;
                needUpdateLocal = true;
            }

            var plansToSync = new List<(string planName, string version)>();
            foreach (var kv in cloudManifest.Plans)
            {
                string planName = kv.Key;
                string cloudVer = kv.Value;

                string localPlanVerDir = Path.Combine(dataPath, planName, cloudVer);
                string localConfigPath = Path.Combine(localPlanVerDir, "config.json");
                bool planPhysicallyExists = File.Exists(localConfigPath);

                if (planPhysicallyExists)
                {
                    if (!localManifest.Plans.TryGetValue(planName, out string? localVer) || localVer != cloudVer)
                    {
                        localManifest.Plans[planName] = cloudVer;
                        needUpdateLocal = true;
                    }
                    continue;
                }

                plansToSync.Add((planName, cloudVer));
            }

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

        // ---------- 辅助方法完全不变 ----------
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
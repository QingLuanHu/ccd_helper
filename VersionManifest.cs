using System.Collections.Generic;

namespace ccd_helper
{
    public class VersionManifest
    {
        public string CloudBasePath { get; set; } = "";
        public string SoftwareVersion { get; set; } = "";
        public Dictionary<string, string> Plans { get; set; } = new();
    }
}
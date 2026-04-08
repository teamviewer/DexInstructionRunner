using System.Collections.Generic;

namespace DexInstructionRunner.Models
{
    public class AuthenticationConfig
    {
        public List<PlatformConfig> PlatformUrls { get; set; }
        public string AppId { get; set; }
        public string AuthenticationUrl { get; set; }
        public string CertificateFile { get; set; }
        public string CertificatePassword { get; set; }
        public string Principal { get; set; }
    }
}

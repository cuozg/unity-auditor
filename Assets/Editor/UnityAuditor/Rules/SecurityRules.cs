#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P0/P1 security rules: PlayerPrefs secrets, hardcoded API keys,
    /// Assembly.Load RCE, Application.OpenURL injection, deprecated WWW, plain HTTP.
    /// </summary>
    public sealed class SecurityRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Security;

        // Security rules scan ALL files including generated code
        protected override bool ShouldSkipFile(string filePath) => false;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "SEC001",
                "PlayerPrefs storing sensitive data (password/token/secret/key)",
                @"PlayerPrefs\.Set(String|Int|Float)\s*\(\s*""[^""]*(?:password|token|secret|apikey|api_key|auth)[^""]*""",
                Severity.P0_BlockMerge,
                "PlayerPrefs is stored as plaintext in the OS registry (Windows) or plist (macOS/iOS). " +
                "Any sensitive value stored here can be read by other apps or extracted from device backups.",
                "Never store credentials in PlayerPrefs. Use OS Keychain APIs, encrypted storage, or a " +
                "server-side token exchange. For development secrets use ScriptableObject assets excluded from VCS."
            ),
            (
                "SEC002",
                "Hardcoded secret/API key string literal",
                @"(api[_-]?key|secret|password|token|bearer|apiSecret)\s*=\s*""[A-Za-z0-9+/=_\-]{8,}""",
                Severity.P0_BlockMerge,
                "Hardcoded secrets in source code are trivially extracted from built applications " +
                "and permanently exposed once committed to version control.",
                "Use environment variables, Unity Cloud Config, or a secrets manager. " +
                "Rotate the exposed secret immediately if already committed."
            ),
            (
                "SEC003",
                "Assembly.Load / Assembly.LoadFrom with runtime path",
                @"Assembly\.(Load|LoadFrom|LoadFile)\s*\(",
                Severity.P0_BlockMerge,
                "Dynamic assembly loading from untrusted paths is the vector for CVE-2025-59489 " +
                "(Unity Android ACE, CVSS 8.4). Malicious save files or network data can redirect this " +
                "to load attacker-controlled code.",
                "Remove dynamic assembly loading. If plugin architecture is needed, validate assembly " +
                "signatures and restrict loading to a known safe directory with hash verification."
            ),
            (
                "SEC004",
                "Application.OpenURL with variable/user-controlled input",
                @"Application\.OpenURL\s*\(\s*(?!""http)",
                Severity.P1_MustFix,
                "Application.OpenURL with unsanitized input enables URL injection attacks — " +
                "attackers can construct file:// or custom-scheme URLs to exfiltrate data.",
                "Validate URLs against an allowlist of domains before calling OpenURL. " +
                "Never pass user-provided strings directly."
            ),
            (
                "SEC005",
                "WWW class usage (deprecated, no TLS enforcement)",
                @"\bnew\s+WWW\s*\(",
                Severity.P1_MustFix,
                "The WWW class is deprecated since Unity 2018.2 and does not enforce TLS certificate " +
                "validation — susceptible to MITM attacks.",
                "Replace with UnityWebRequest. Set `certificateHandler` for custom validation, " +
                "or use the default handler which validates TLS certificates."
            ),
            (
                "SEC006",
                "Insecure HTTP endpoint (not HTTPS)",
                @"""http://(?!localhost|127\.0\.0\.1)",
                Severity.P1_MustFix,
                "Plain HTTP transmits all data unencrypted. This includes any auth tokens, " +
                "player data, or analytics sent over the wire.",
                "Use HTTPS for all production endpoints. HTTP is acceptable only for local development."
            ),
            (
                "SEC007",
                "Unsafe JSON deserialization with TypeNameHandling",
                @"TypeNameHandling\s*\.\s*(?:All|Auto|Objects)",
                Severity.P0_BlockMerge,
                "TypeNameHandling.All/Auto/Objects enables type confusion RCE attacks. An attacker can craft " +
                "JSON payloads that instantiate arbitrary .NET types (e.g., System.Diagnostics.Process) during deserialization.",
                "Use TypeNameHandling.None (the default and safe setting). If polymorphic deserialization is needed, " +
                "use a custom SerializationBinder that allowlists known safe types."
            ),
            (
                "SEC008",
                "File read with variable path — path traversal risk",
                @"(?:File\.ReadAllText|File\.ReadAllBytes|StreamReader)\s*\(\s*[a-zA-Z_]\w*",
                Severity.P0_BlockMerge,
                "File read operations with variable paths are vulnerable to path traversal attacks " +
                "(../../etc/passwd). If the path comes from user input, save files, or network data, " +
                "an attacker can read arbitrary files.",
                "Validate paths against an allowlist of safe directories. Use Path.GetFullPath() and verify the " +
                "result starts with your expected base directory. Never pass user input directly to file operations."
            ),
            (
                "SEC009",
                "Process.Start with non-literal arguments — command injection",
                @"Process\.Start\s*\(\s*[a-zA-Z_]\w*",
                Severity.P0_BlockMerge,
                "Process.Start with variable arguments enables command injection. An attacker controlling " +
                "the argument can execute arbitrary system commands.",
                "Use ProcessStartInfo with a hardcoded executable path and validate all arguments. " +
                "Never construct process commands from user input."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif

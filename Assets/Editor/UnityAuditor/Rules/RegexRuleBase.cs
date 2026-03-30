#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnityAuditor.Rules
{
    /// <summary>
    /// Base class for rules that scan .cs files using regex patterns.
    /// Subclasses define their rule tuples and category; the scanning loop is shared.
    /// </summary>
    public abstract class RegexRuleBase : IAuditRule
    {
        // ---------------------------------------------------------------
        // Abstract contract
        // ---------------------------------------------------------------

        public abstract RuleCategory Category { get; }

        /// <summary>Rule definitions: (ruleId, title, regexPattern, severity, whyItMatters, howToFix).</summary>
        protected abstract (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules { get; }

        // ---------------------------------------------------------------
        // Virtual hooks
        // ---------------------------------------------------------------

        /// <summary>
        /// Override to change which files are skipped during scanning.
        /// Default skips Generated and .Designer. files.
        /// </summary>
        protected virtual bool ShouldSkipFile(string filePath) =>
            filePath.Contains("Generated") || filePath.Contains(".Designer.");

        // ---------------------------------------------------------------
        // Shared scanning logic
        // ---------------------------------------------------------------

        public List<AuditFinding> Scan(string assetsRoot)
        {
            var findings = new List<AuditFinding>();

            foreach (var csFile in Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (ShouldSkipFile(csFile)) continue;

                var fullText     = File.ReadAllText(csFile);
                var relativePath = ScannerUtility.MakeRelative(csFile, assetsRoot);
                bool hasSuppression = fullText.IndexOf("UnityAuditor:ignore", StringComparison.Ordinal) >= 0;

                foreach (var rule in Rules)
                {
                    var matches = Regex.Matches(fullText, rule.pattern,
                        RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        int line = ScannerUtility.CountLines(fullText, match.Index);

                        if (hasSuppression)
                        {
                            string matchLine = ScannerUtility.GetLineAtOffset(fullText, match.Index);
                            string prevLine  = line > 1
                                ? ScannerUtility.GetPreviousLineAtOffset(fullText, match.Index)
                                : null;

                            if (ScannerUtility.IsLineSuppressed(prevLine, matchLine, rule.id))
                                continue;
                        }

                        findings.Add(new AuditFinding
                        {
                            Severity     = rule.sev,
                            Category     = Category,
                            RuleId       = rule.id,
                            Title        = rule.title,
                            FilePath     = relativePath,
                            Line         = line,
                            Detail       = match.Value.Trim(),
                            WhyItMatters = rule.why,
                            HowToFix     = rule.fix,
                        });
                    }
                }
            }

            return findings;
        }
    }
}
#endif

using System;

namespace UnityAuditor
{
    /// <summary>
    /// Data class representing a single audit finding from a rule scan.
    /// Public fields are intentional — this is a DTO populated via object initializers in rule code.
    /// </summary>
    [Serializable]
    public sealed class AuditFinding
    {
        public Severity     Severity;
        public RuleCategory Category;
        public string       RuleId;
        public string       Title;
        public string       FilePath;
        public int          Line;
        public string       Detail;
        public string       WhyItMatters;
        public string       HowToFix;
        public bool         Suppressed;

        public override string ToString() =>
            $"[{Severity}][{Category}] {RuleId}: {Title} @ {FilePath}:{Line}";
    }
}

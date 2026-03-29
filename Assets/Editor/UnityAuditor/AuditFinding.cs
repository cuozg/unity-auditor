using System;
using System.Collections.Generic;

namespace UnityAuditor
{
    public enum Severity
    {
        P0_BlockMerge = 0,   // Must fix before merge
        P1_MustFix    = 1,   // Should fix this sprint
        P2_Suggestion = 2,   // Nice to have
    }

    public enum RuleCategory
    {
        CodeLogic,
        Serialization,
        Security,
        Performance,
        PrefabIntegrity,
        AssetSettings,
    }

    [Serializable]
    public class AuditFinding
    {
        public Severity     Severity;
        public RuleCategory Category;
        public string       RuleId;
        public string       Title;
        public string       FilePath;       // relative to Assets/
        public int          Line;           // 0 if N/A
        public string       Detail;         // the offending snippet or asset GUID
        public string       WhyItMatters;
        public string       HowToFix;

        public override string ToString() =>
            $"[{Severity}][{Category}] {RuleId}: {Title} @ {FilePath}:{Line}";
    }

    public interface IAuditRule
    {
        RuleCategory Category { get; }

        /// <summary>Scan everything under <paramref name="assetsRoot"/> and return findings.</summary>
        List<AuditFinding> Scan(string assetsRoot);
    }
}

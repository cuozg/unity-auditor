using System.Collections.Generic;

namespace UnityAuditor
{
    /// <summary>
    /// Contract for all audit rules. Implement this interface to add a new scanning rule.
    /// Register new implementations in <see cref="AuditEngine.GetAllRules"/>.
    /// </summary>
    public interface IAuditRule
    {
        RuleCategory Category { get; }

        /// <summary>Scan everything under <paramref name="assetsRoot"/> and return findings.</summary>
        List<AuditFinding> Scan(string assetsRoot);
    }
}

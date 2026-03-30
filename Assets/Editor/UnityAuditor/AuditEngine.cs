#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityAuditor.Rules;
using UnityEngine;

namespace UnityAuditor
{
    /// <summary>
    /// Orchestrates all audit rules, runs them against the project, and aggregates findings.
    /// </summary>
    public static class AuditEngine
    {
        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>Run all rules against the Assets folder and return sorted findings.</summary>
        public static List<AuditFinding> RunAll(string assetsRoot, Action<string, float> progressCallback = null)
        {
            var rules       = GetAllRules();
            var allFindings = new List<AuditFinding>();

            for (int i = 0; i < rules.Count; i++)
            {
                var rule     = rules[i];
                float progress = (float)i / rules.Count;
                progressCallback?.Invoke($"Scanning: {rule.Category}...", progress);

                try
                {
                    allFindings.AddRange(rule.Scan(assetsRoot));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityAuditor] Rule {rule.GetType().Name} threw: {ex}");
                }
            }

            progressCallback?.Invoke("Done", 1f);

            allFindings.Sort((a, b) =>
            {
                int sevCmp = a.Severity.CompareTo(b.Severity);
                if (sevCmp != 0) return sevCmp;
                int catCmp = a.Category.CompareTo(b.Category);
                if (catCmp != 0) return catCmp;
                return string.Compare(a.FilePath, b.FilePath, StringComparison.Ordinal);
            });

            return allFindings;
        }

        /// <summary>Run rules for a specific category only.</summary>
        public static List<AuditFinding> RunCategory(string assetsRoot, RuleCategory category)
        {
            var rules    = GetAllRules().Where(r => r.Category == category).ToList();
            var findings = new List<AuditFinding>();

            foreach (var rule in rules)
            {
                try
                {
                    findings.AddRange(rule.Scan(assetsRoot));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityAuditor] {rule.GetType().Name}: {ex.Message}");
                }
            }

            return findings;
        }

        // ---------------------------------------------------------------
        // Rule discovery (reflection-based, cached per domain reload)
        // ---------------------------------------------------------------

        private static readonly List<IAuditRule> _cachedRules = DiscoverRules();

        /// <summary>Returns all discovered rule instances (cached).</summary>
        private static List<IAuditRule> GetAllRules() => _cachedRules;

        private static List<IAuditRule> DiscoverRules()
        {
            var rules = new List<IAuditRule>();
            var ruleInterface = typeof(IAuditRule);

            foreach (var type in ruleInterface.Assembly.GetTypes())
            {
                if (type.Namespace == "UnityAuditor.Rules"
                    && ruleInterface.IsAssignableFrom(type)
                    && !type.IsAbstract
                    && !type.IsInterface)
                {
                    try
                    {
                        rules.Add((IAuditRule)Activator.CreateInstance(type));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[UnityAuditor] Failed to instantiate rule {type.Name}: {ex.Message}");
                    }
                }
            }

            return rules;
        }

        // ---------------------------------------------------------------
        // Reporting helpers
        // ---------------------------------------------------------------

        public static Dictionary<RuleCategory, int> GetCategoryCounts(List<AuditFinding> findings)
        {
            var counts = new Dictionary<RuleCategory, int>();
            foreach (RuleCategory cat in Enum.GetValues(typeof(RuleCategory)))
                counts[cat] = findings.Count(f => f.Category == cat);
            return counts;
        }

        public static Dictionary<Severity, int> GetSeverityCounts(List<AuditFinding> findings)
        {
            var counts = new Dictionary<Severity, int>();
            foreach (Severity sev in Enum.GetValues(typeof(Severity)))
                counts[sev] = findings.Count(f => f.Severity == sev);
            return counts;
        }
    }
}
#endif

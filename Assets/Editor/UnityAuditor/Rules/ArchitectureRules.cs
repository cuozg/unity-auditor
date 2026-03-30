#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 architecture rules: large MonoBehaviour classes, too many public methods,
    /// unguarded UnityEditor usage in runtime scripts, static mutable fields,
    /// excessive interface implementations, namespace-path mismatches.
    /// </summary>
    public sealed class ArchitectureRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Architecture;

        protected override bool ShouldSkipFile(string filePath) =>
            base.ShouldSkipFile(filePath) ||
            filePath.Contains("/Editor/") ||
            filePath.Contains("\\Editor\\");

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "AR001",
                "MonoBehaviour/ScriptableObject class body exceeds ~500 lines",
                @"(?s)class\s+\w+\s*:\s*(?:MonoBehaviour|ScriptableObject)[^{]*\{(.{15000,})",
                Severity.P2_Suggestion,
                "Classes exceeding 500 lines are difficult to maintain, test, and review. They often " +
                "indicate multiple responsibilities that should be separated.",
                "Extract responsibilities into separate components using composition. Follow Single " +
                "Responsibility Principle — each MonoBehaviour should handle one concern. Note: this " +
                "rule uses a character-count heuristic (~15000 chars ≈ 500 lines) and may have false positives."
            ),
            (
                "AR002",
                "Class has many public method declarations (potential god class)",
                @"(?:public\s+(?:virtual\s+|override\s+|static\s+)?(?:void|bool|int|float|string|[\w<>\[\]]+)\s+\w+\s*\([^)]*\)\s*\{[^}]*\}.*?){10,}",
                Severity.P2_Suggestion,
                "A class with many public methods often has too many responsibilities (god class " +
                "anti-pattern), making it hard to understand, test, and maintain.",
                "Apply Interface Segregation Principle. Split into focused interfaces and implementations. " +
                "Consider using composition over inheritance. Note: this rule is a heuristic that counts " +
                "public method declarations — verify manually."
            ),
            (
                "AR003",
                "Runtime script uses 'using UnityEditor' without #if UNITY_EDITOR guard",
                @"(?<!\#if\s+UNITY_EDITOR[^\n]*\n\s*)using\s+UnityEditor\b",
                Severity.P1_MustFix,
                "Runtime scripts with 'using UnityEditor' will fail to compile in player builds. This is " +
                "a hard build failure that only appears when building for device — not in the Editor.",
                "Wrap editor-only code in #if UNITY_EDITOR / #endif guards, or move the script to an " +
                "Editor/ folder. Note: this rule skips files in Editor/ folders automatically."
            ),
            (
                "AR004",
                "Static mutable field in MonoBehaviour (non-readonly, non-const)",
                @"(?:class\s+\w+\s*:\s*MonoBehaviour)[^}]*static\s+(?!readonly|const)[\w<>\[\]]+\s+\w+\s*[;=]",
                Severity.P1_MustFix,
                "Static fields on MonoBehaviours survive scene reloads, causing subtle state persistence " +
                "bugs. When a scene reloads, all MonoBehaviour instances are destroyed but the static " +
                "field retains its value.",
                "Use instance fields instead. If shared state is needed, use a ScriptableObject as a data " +
                "container or a dedicated singleton pattern with explicit cleanup."
            ),
            (
                "AR005",
                "Concrete class implementing more than 5 interfaces",
                @"class\s+\w+\s*:\s*(?:\w+\s*,\s*){5,}",
                Severity.P2_Suggestion,
                "A class implementing many interfaces often has too many responsibilities. This violates " +
                "Interface Segregation Principle and makes the class hard to mock in tests.",
                "Split the class or consolidate related interfaces. Consider whether some interfaces " +
                "represent orthogonal concerns that belong in separate components."
            ),
            (
                "AR006",
                "Namespace declaration (verify alignment with folder structure)",
                @"namespace\s+\w+",
                Severity.P2_Suggestion,
                "When file paths don't match namespaces, developers waste time finding files. IDEs may " +
                "also fail to resolve types correctly.",
                "Align namespace with folder structure. For 'Assets/Scripts/Player/Movement/', use " +
                "'namespace YourProject.Player.Movement'. Note: this rule is heuristic — it flags all " +
                "namespace declarations for review. Verify your project's namespace conventions manually."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif
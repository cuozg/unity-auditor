#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 lifecycle rules: virtual calls in Awake/constructor, cross-GameObject GetComponent in Awake,
    /// DontDestroyOnLoad on child, OnDestroy without null-check, deltaTime in FixedUpdate,
    /// duplicate DefaultExecutionOrder values.
    /// </summary>
    public sealed class LifecycleRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Lifecycle;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "LC001",
                "Virtual method call in Awake() or constructor",
                @"(?:void\s+Awake|(?:public|private|protected|internal)\s+\w+\s*\(\s*\))\s*\{[^}]*(?:virtual|base\.)\w+",
                Severity.P1_MustFix,
                "Calling virtual methods in Awake() or a constructor invokes the base implementation — " +
                "the derived class override won't run because the derived class isn't fully initialized yet.",
                "Move virtual/overridden method calls to Start() where all components are fully initialized. " +
                "Never call virtual methods in constructors."
            ),
            (
                "LC002",
                "GetComponent/Find* targeting other GameObjects called in Awake()",
                @"void\s+Awake\s*\(\s*\)[^}]*(?:GetComponentInParent|GetComponentInChildren|FindObjectOfType|GameObject\.Find)\s*[<(]",
                Severity.P1_MustFix,
                "In Awake(), sibling component initialization order is undefined. GetComponent on OTHER " +
                "GameObjects may return uninitialized components. Only access components on the SAME " +
                "GameObject in Awake.",
                "Move cross-GameObject GetComponent/Find calls to Start(), which runs after all Awake() " +
                "calls complete."
            ),
            (
                "LC003",
                "DontDestroyOnLoad called on a child GameObject",
                @"DontDestroyOnLoad\s*\(\s*(?!gameObject|this\.gameObject)\w+",
                Severity.P1_MustFix,
                "DontDestroyOnLoad only works on root GameObjects. Calling it on a child object will fail " +
                "silently — the child will be destroyed when its parent's scene unloads.",
                "Call DontDestroyOnLoad on the root GameObject: " +
                "DontDestroyOnLoad(transform.root.gameObject); or restructure the hierarchy."
            ),
            (
                "LC004",
                "OnDestroy accessing other components without null-check",
                @"void\s+OnDestroy\s*\(\s*\)[^}]*(?:GetComponent|\.transform|\.gameObject)\s*[<.(]",
                Severity.P2_Suggestion,
                "During scene teardown, OnDestroy is called in undefined order. Referenced components may " +
                "already be destroyed, causing MissingReferenceException.",
                "Null-check all component references in OnDestroy: " +
                "if (_otherComponent != null) _otherComponent.Cleanup();"
            ),
            (
                "LC005",
                "Time.deltaTime used inside FixedUpdate",
                @"void\s+FixedUpdate\s*\(\s*\)[^}]*Time\.deltaTime",
                Severity.P1_MustFix,
                "FixedUpdate runs at a fixed timestep. Time.deltaTime inside FixedUpdate returns the fixed " +
                "timestep value, but using Time.fixedDeltaTime is more explicit and correct.",
                "Replace Time.deltaTime with Time.fixedDeltaTime inside FixedUpdate for clarity and correctness."
            ),
            (
                "LC006",
                "DefaultExecutionOrder attribute usage (verify uniqueness)",
                @"\[DefaultExecutionOrder\s*\(\s*(-?\d+)\s*\)\]",
                Severity.P2_Suggestion,
                "Multiple MonoBehaviours with the same [DefaultExecutionOrder] value defeats the purpose of " +
                "explicit ordering — their relative execution order remains undefined.",
                "Assign unique order values to each MonoBehaviour that needs deterministic execution ordering. " +
                "Note: this rule flags all usages for manual review — cross-file duplicate detection requires " +
                "analysis beyond regex."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif
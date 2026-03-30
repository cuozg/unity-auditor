#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 code logic rules: Unity null checks, Camera.main in Update,
    /// Find* at runtime, empty catch, GetComponent in Update, coroutine/destroy guards.
    /// </summary>
    public sealed class CodeLogicRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.CodeLogic;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "CL001",
                "Unity Object null check using == null",
                @"if\s*\(\s*\w+\s*==\s*null\s*\)|if\s*\(\s*null\s*==\s*\w+\s*\)",
                Severity.P1_MustFix,
                "Unity overloads == for destroyed objects. obj == null returns true for destroyed objects but " +
                "using ReferenceEquals or 'is null' will NOT catch destroyed objects.",
                "Use 'if (!obj)' or 'obj == null' explicitly — but never 'obj is null' or 'obj?.Method()' on " +
                "UnityEngine.Object subclasses for destroyed-object checks."
            ),
            (
                "CL002",
                "Camera.main accessed in hot path (Update/FixedUpdate/LateUpdate)",
                @"(void\s+Update|void\s+FixedUpdate|void\s+LateUpdate)[^}]*Camera\.main",
                Severity.P1_MustFix,
                "Camera.main calls FindObjectOfType internally — O(n) scan every frame.",
                "Cache Camera.main in Awake/Start: `private Camera _cam; void Awake() { _cam = Camera.main; }`"
            ),
            (
                "CL003",
                "GameObject.Find / FindObjectOfType outside Awake/Start",
                @"(GameObject\.Find|FindObjectOfType|FindObjectsOfType)\s*[<(]",
                Severity.P1_MustFix,
                "Find* methods are O(n) scene scans. Called per-frame they will spike CPU.",
                "Cache results in Awake/Start. Use dependency injection or ScriptableObject channels instead."
            ),
            (
                "CL004",
                "Empty catch block swallows exceptions",
                @"catch\s*(\([^)]*\))?\s*\{\s*\}",
                Severity.P1_MustFix,
                "Silent failures hide bugs and make debugging extremely difficult.",
                "At minimum: `catch (Exception e) { Debug.LogException(e); }` — or re-throw."
            ),
            (
                "CL005",
                "GetComponent called inside Update loop",
                @"(void\s+Update|void\s+FixedUpdate|void\s+LateUpdate)[^}]*GetComponent\s*[<(]",
                Severity.P1_MustFix,
                "GetComponent is not free — involves a native-to-managed bridge call every frame.",
                "Cache component references in Awake: `private Rigidbody _rb; void Awake() { _rb = GetComponent<Rigidbody>(); }`"
            ),
            (
                "CL006",
                "StartCoroutine without null/active guard",
                @"StartCoroutine\s*\(",
                Severity.P2_Suggestion,
                "StartCoroutine on a disabled or destroyed GameObject throws MissingReferenceException.",
                "Guard with: `if (this != null && gameObject.activeInHierarchy) StartCoroutine(...);`"
            ),
            (
                "CL007",
                "Destroy called without null check",
                @"Destroy\s*\(\s*\w+\s*\)",
                Severity.P2_Suggestion,
                "Calling Destroy(null) is safe, but Destroy on an already-destroyed object logs a warning.",
                "Use `if (obj != null) Destroy(obj);` for clarity and to suppress spurious warnings."
            ),
            (
                "CL008",
                "Coroutine method (IEnumerator) without any yield return",
                @"IEnumerator\s+\w+\s*\([^)]*\)\s*\{(?:(?!yield\s+return)[^}])*\}",
                Severity.P1_MustFix,
                "An IEnumerator method without yield executes entirely in one frame — defeating the purpose " +
                "of coroutines. The method will run to completion synchronously when StartCoroutine is called.",
                "Add yield return statements (e.g., yield return null, yield return new WaitForSeconds(1f)) " +
                "or change the return type to void if coroutine behavior is not needed."
            ),
            (
                "CL009",
                "async void method declaration (should be async Task)",
                @"async\s+void\s+\w+\s*\(",
                Severity.P1_MustFix,
                "async void methods swallow exceptions silently and cannot be awaited. Exceptions thrown " +
                "will crash the application without any catch opportunity.",
                "Change to 'async Task MethodName()' and await the result. Only use async void for Unity " +
                "event handlers like button onClick handlers."
            ),
            (
                "CL010",
                "UnityEvent.Invoke() called without listener/null guard",
                @"\.Invoke\s*\(\s*\)",
                Severity.P2_Suggestion,
                "Invoking a UnityEvent with zero persistent listeners is a no-op that can indicate a " +
                "misconfigured event wiring in the Inspector.",
                "Check GetPersistentEventCount() > 0 before invoking, or verify the event has listeners " +
                "wired in the Inspector."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif

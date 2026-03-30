#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 performance rules: string concat in Update, new() allocations in hot paths,
    /// LINQ in Update, SendMessage, unguarded Debug.Log, tag string comparison, RaycastHit in loops.
    /// </summary>
    public sealed class PerformanceRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Performance;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "PERF001",
                "String concatenation (+) in Update/FixedUpdate — GC alloc per frame",
                @"(void\s+(Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)\+\s*""|\+\s*[a-zA-Z_]\w*\s*\+",
                Severity.P1_MustFix,
                "String + string creates a new heap allocation every call. At 60fps this generates " +
                "3,600+ GC objects per second, spiking GC collection and causing frame stutters.",
                "Use StringBuilder for repeated concatenation, or string interpolation with " +
                "cached format strings. For debug display use a fixed char buffer."
            ),
            (
                "PERF002",
                "new() allocation in Update/FixedUpdate/LateUpdate",
                @"(void\s+(Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)\bnew\s+(?!List|Dictionary|HashSet|Queue|Stack|Array)\w+\s*[(<]",
                Severity.P1_MustFix,
                "Object allocation in a hot path forces GC collection. Even small objects (Vector3, " +
                "WaitForSeconds) add up at 60fps.",
                "Cache reusable objects as fields. Use object pools for frequently created/destroyed objects. " +
                "Prefer struct types for small value types to avoid heap allocation."
            ),
            (
                "PERF003",
                "LINQ query in Update/FixedUpdate/LateUpdate",
                @"(void\s+(Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)(\.Where\(|\.Select\(|\.FirstOrDefault\(|\.Any\(|\.OrderBy\(|\.ToList\(|\.ToArray\()",
                Severity.P1_MustFix,
                "LINQ allocates IEnumerator objects and intermediate collections on the heap. " +
                "A single .ToList() call per frame generates thousands of GC allocations per second.",
                "Pre-compute and cache LINQ results. Use manual for loops in hot paths. " +
                "Consider using Span<T> or ArraySegment<T> for zero-allocation enumeration."
            ),
            (
                "PERF004",
                "SendMessage / BroadcastMessage usage",
                @"\b(SendMessage|BroadcastMessage|SendMessageUpwards)\s*\(",
                Severity.P1_MustFix,
                "SendMessage uses reflection to find and invoke methods by name string — no compile-time " +
                "safety, ~10x slower than a direct call, and triggers GC allocations.",
                "Replace with direct method calls, C# events/delegates, or UnityEvent. " +
                "For cross-component communication use ScriptableObject event channels."
            ),
            (
                "PERF005",
                "Debug.Log called outside #if UNITY_EDITOR guard",
                @"(?<!\#if\s+UNITY_EDITOR[^\n]*\n[^\n]*)Debug\.(Log|LogWarning|LogError|LogFormat)\s*\(",
                Severity.P2_Suggestion,
                "Debug.Log has significant overhead in release builds: string formatting, stack trace " +
                "capture, and thread synchronization — even when the console is hidden.",
                "Wrap all debug logging in `#if UNITY_EDITOR` or `#if DEVELOPMENT_BUILD`, " +
                "or use a conditional logging wrapper that strips in release."
            ),
            (
                "PERF006",
                "transform.tag comparison with string",
                @"\.tag\s*==\s*""",
                Severity.P2_Suggestion,
                "gameObject.tag == \"string\" allocates a new string each comparison. " +
                "At scale this adds measurable GC pressure.",
                "Use `gameObject.CompareTag(\"TagName\")` — it's allocation-free and faster."
            ),
            (
                "PERF007",
                "Physics.Raycast storing result in a new RaycastHit allocation in loop",
                @"(for|while|foreach)[^{]*\{[^}]*RaycastHit\s+\w+\s*=\s*new\s+RaycastHit",
                Severity.P2_Suggestion,
                "Declaring RaycastHit inside a loop re-initializes the struct each iteration.",
                "Declare RaycastHit as a field or outside the loop scope to reuse the stack allocation."
            ),
            (
                "PERF008",
                "new WaitForSeconds() inside a coroutine loop — allocates every iteration",
                @"(while|for)\s*\([^)]*\)\s*\{[^}]*yield\s+return\s+new\s+WaitForSeconds",
                Severity.P1_MustFix,
                "Creating new WaitForSeconds inside a loop allocates a new object every iteration. " +
                "At typical coroutine frequencies this generates hundreds of GC objects per minute.",
                "Cache the WaitForSeconds: `private WaitForSeconds _wait = new WaitForSeconds(1f);` " +
                "then `yield return _wait;` inside the loop."
            ),
            (
                "PERF009",
                "Resources.Load called inside Update/FixedUpdate/LateUpdate",
                @"(void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*Resources\.Load\s*[<(]",
                Severity.P1_MustFix,
                "Resources.Load is a synchronous disk read. Calling it every frame causes massive " +
                "frame spikes and disk thrashing.",
                "Load resources in Awake/Start and cache the reference. Use Addressables for async loading."
            ),
            (
                "PERF010",
                "Manual Camera.Render() or Camera.RenderWithShader() call",
                @"Camera\.\s*(?:Render|RenderWithShader)\s*\(",
                Severity.P2_Suggestion,
                "Manual camera rendering is extremely expensive. It's often called unintentionally " +
                "or redundantly alongside Unity's automatic rendering pipeline.",
                "Verify this manual render call is intentional. Consider using RenderTextures with " +
                "Camera.targetTexture instead for off-screen rendering."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif

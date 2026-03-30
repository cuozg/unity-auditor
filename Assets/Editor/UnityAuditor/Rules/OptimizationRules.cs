#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 optimization rules: could-be-static methods, boxing, string interpolation in hot paths,
    /// non-generic foreach, duplicate GetComponent, SetPixels without batched Apply, Raycast without layerMask.
    /// </summary>
    public sealed class OptimizationRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Optimization;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "OPT001",
                "Private method that doesn't access instance members could be static",
                @"private\s+(?:void|bool|int|float|string|[\w<>\[\]]+)\s+\w+\s*\([^)]*\)\s*\{",
                Severity.P2_Suggestion,
                "Non-static methods that don't access instance members incur virtual dispatch overhead " +
                "and mislead readers about the method's dependencies.",
                "Add the 'static' modifier if the method doesn't use 'this', instance fields, or instance " +
                "methods. Verify no base class calls before converting. Note: this rule is heuristic — verify manually."
            ),
            (
                "OPT002",
                "Boxing of value type via object assignment or string.Format",
                @"(?:object\s+\w+\s*=\s*(?!null|new)\w+|string\.Format\s*\([^)]*\{0\})",
                Severity.P1_MustFix,
                "Boxing allocates a heap object to wrap a value type. In hot paths this creates " +
                "significant GC pressure.",
                "Use generic methods/containers instead of object. Replace string.Format with string " +
                "interpolation with .ToString() calls, or use StringBuilder."
            ),
            (
                "OPT003",
                "String interpolation or string.Format inside Update/hot paths",
                @"(void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*(?:\$""|string\.Format\s*\()",
                Severity.P2_Suggestion,
                "String interpolation and Format allocate new strings every call. At 60fps this " +
                "generates thousands of GC objects per second.",
                "Cache formatted strings or use StringBuilder. For debug display, wrap in #if UNITY_EDITOR."
            ),
            (
                "OPT004",
                "foreach on non-generic IEnumerable collections (ArrayList, Hashtable)",
                @"foreach\s*\([^)]*\bin\s+(?:\w+\.)?(?:ArrayList|Hashtable|SortedList)\b",
                Severity.P2_Suggestion,
                "Iterating non-generic collections boxes the enumerator and each element, creating " +
                "heap allocations per iteration.",
                "Replace with generic equivalents: ArrayList→List<T>, Hashtable→Dictionary<TKey,TValue>, " +
                "SortedList→SortedList<TKey,TValue>."
            ),
            (
                "OPT005",
                "Multiple GetComponent<T>() calls within same method",
                @"GetComponent\s*<\s*\w+\s*>\s*\(\s*\).*GetComponent\s*<\s*\w+\s*>\s*\(\s*\)",
                Severity.P2_Suggestion,
                "Each GetComponent call traverses the component list. Multiple calls for the same " +
                "type in one method should be cached in a local variable.",
                "Cache in a local: `var rb = GetComponent<Rigidbody>();` then reuse `rb`. This avoids " +
                "redundant native bridge calls."
            ),
            (
                "OPT006",
                "Texture2D.SetPixels/SetPixel called in a loop without batched Apply",
                @"(for|while|foreach)[^{]*\{[^}]*(?:SetPixels?|SetPixel)\s*\(",
                Severity.P2_Suggestion,
                "Each SetPixel/SetPixels call without a batch Apply() re-uploads the texture to GPU. " +
                "Apply should be called once after all modifications.",
                "Call SetPixels in a batch, then call Apply() once: `tex.SetPixels(colors); tex.Apply();`"
            ),
            (
                "OPT007",
                "Physics.Raycast or cast without explicit layerMask parameter",
                @"Physics\.(?:Raycast|SphereCast|BoxCast|CapsuleCast)\s*\(\s*[^,)]+,\s*[^,)]+\s*\)",
                Severity.P1_MustFix,
                "Without a layerMask, physics queries check ALL layers including UI, water, and terrain " +
                "— wasting significant CPU on irrelevant colliders.",
                "Add a layerMask parameter: Physics.Raycast(origin, direction, maxDistance, layerMask). " +
                "Define layer masks as constants."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif
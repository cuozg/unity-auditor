#if UNITY_EDITOR
namespace UnityAuditor.Rules
{
    /// <summary>
    /// P0/P1/P2 serialization rules: Dictionary in SerializeField, BinaryFormatter,
    /// auto-property on Serializable class, public field hygiene, SerializeReference misuse,
    /// OnValidate without Undo.
    /// </summary>
    public sealed class SerializationRules : RegexRuleBase
    {
        public override RuleCategory Category => RuleCategory.Serialization;

        private static readonly (string id, string title, string pattern, Severity sev, string why, string fix)[] _rules =
        {
            (
                "SR001",
                "Public field without [SerializeField] attribute",
                @"public\s+(?!static|const|readonly|class|struct|enum|interface|override|virtual|abstract)([\w<>\[\]]+)\s+\w+\s*;",
                Severity.P2_Suggestion,
                "Public fields expose data to any external code. Prefer [SerializeField] private fields to " +
                "maintain encapsulation while keeping Inspector visibility.",
                "Change `public T field;` to `[SerializeField] private T _field;` and add a public property if external access is needed."
            ),
            (
                "SR002",
                "Dictionary or HashSet used as serialized field",
                @"\[SerializeField\][^;]*(?:Dictionary|HashSet)\s*<",
                Severity.P0_BlockMerge,
                "Unity cannot serialize Dictionary or HashSet. These fields will always be null/empty at runtime " +
                "after deserialization — a common source of NullReferenceExceptions.",
                "Use a serializable key-value pair list or a custom [Serializable] class. " +
                "Consider packages like odinSerializer for complex data or a List<KVPair> pattern."
            ),
            (
                "SR003",
                "Auto-property on [System.Serializable] struct/class",
                @"\[System\.Serializable\][^{]*\{[^}]*\{\s*get;\s*(set;)?\s*\}",
                Severity.P1_MustFix,
                "Unity serializes fields, not properties. Auto-properties are NOT serialized even inside " +
                "[Serializable] classes — data will be silently lost.",
                "Replace auto-properties with [SerializeField] private fields and explicit public accessors."
            ),
            (
                "SR004",
                "BinaryFormatter usage detected",
                @"BinaryFormatter\s*(\(\)|\.)",
                Severity.P0_BlockMerge,
                "BinaryFormatter is deprecated (.NET 5+), disabled by default in Unity 2022+, and has known " +
                "deserialization vulnerabilities (CVE-style remote code execution via malicious save files).",
                "Replace with JsonUtility, Newtonsoft.Json, or a custom binary format. " +
                "For save games use PlayerPrefs + JSON or a proper serialization library."
            ),
            (
                "SR005",
                "[SerializeReference] field with concrete sealed type",
                @"\[SerializeReference\][^;]*\b(int|float|bool|string|Vector2|Vector3|Quaternion)\b",
                Severity.P2_Suggestion,
                "[SerializeReference] is for polymorphic references (interfaces/abstract classes). " +
                "Using it on value types or sealed types adds overhead with no benefit.",
                "Use [SerializeField] for value types and concrete sealed types. " +
                "Reserve [SerializeReference] for polymorphic scenarios."
            ),
            (
                "SR006",
                "OnValidate modifying serialized data without Undo",
                @"void\s+OnValidate\s*\(\s*\)[^}]*=\s*[^;]+;",
                Severity.P1_MustFix,
                "OnValidate runs in the Editor on every change. Modifying serialized fields without Undo.RecordObject " +
                "will bypass the Undo system and can cause data loss.",
                "Guard modifications: `UnityEditor.Undo.RecordObject(this, \"Validate\");` before field changes in OnValidate."
            ),
            (
                "SR007",
                "[NonSerialized] on a private field — redundant",
                @"\[NonSerialized\]\s*private\s+",
                Severity.P2_Suggestion,
                "Private fields without [SerializeField] are already non-serialized by Unity. Adding [NonSerialized] " +
                "is redundant and indicates confusion about Unity serialization rules.",
                "Remove the [NonSerialized] attribute. Private fields are not serialized unless marked with [SerializeField]."
            ),
            (
                "SR008",
                "Public mutable List/array on ScriptableObject without [HideInInspector]",
                @"(?:ScriptableObject)[^}]*public\s+(?:List\s*<|[\w]+\s*\[\])\s*\w+",
                Severity.P1_MustFix,
                "ScriptableObject instances are shared assets. A public mutable collection exposed in the Inspector " +
                "can be accidentally modified in one prefab and silently affect all references.",
                "Use [HideInInspector] or make the field private with [SerializeField]. Consider exposing a " +
                "read-only IReadOnlyList<T> property instead."
            ),
            (
                "SR009",
                "Enum without explicit integer values",
                @"enum\s+\w+\s*\{[^}=]+\}",
                Severity.P2_Suggestion,
                "Enums without explicit integer values will have their serialized meaning change silently if members " +
                "are added, removed, or reordered. All serialized references become corrupted.",
                "Assign explicit values: enum MyEnum { None = 0, TypeA = 1, TypeB = 2 }. This ensures serialized " +
                "data remains stable across code changes."
            ),
            (
                "SR010",
                "[SerializeField] array or List with large default initializer (>100 elements)",
                @"\[SerializeField\][^;]*(?:new\s+\w+\s*\[\s*\d{3,}\s*\]|new\s+List\s*<[^>]+>\s*\(\s*\d{3,}\s*\))",
                Severity.P2_Suggestion,
                "Large serialized collections bloat .prefab and .scene YAML files, slow down Inspector rendering, " +
                "and increase version control diff noise.",
                "Reduce default size or populate at runtime. For large data sets, use ScriptableObject assets " +
                "or Addressables instead of inline serialized arrays."
            ),
            (
                "SR011",
                "[System.Serializable] class — verify parameterless constructor exists",
                @"\[System\.Serializable\]\s*(?:public\s+|internal\s+)?(?:sealed\s+)?class\s+\w+",
                Severity.P1_MustFix,
                "Unity serialization requires a parameterless constructor. Without one, deserialization silently " +
                "fails — the object is created with default values, losing all serialized data.",
                "Add an explicit parameterless constructor: public MyClass() { }. Unity calls this during " +
                "deserialization to create the instance before populating fields."
            ),
        };

        protected override (string id, string title, string pattern, Severity sev, string why, string fix)[] Rules => _rules;
    }
}
#endif

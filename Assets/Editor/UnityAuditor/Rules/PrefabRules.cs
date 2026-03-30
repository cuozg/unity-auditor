#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityAuditor.Rules
{
    /// <summary>
    /// P0/P1/P2 prefab integrity rules: missing script references, broken variant bases,
    /// Canvas without GraphicRaycaster, non-uniform scale on colliders,
    /// deep nesting, duplicate EventSystems, missing AnimatorControllers,
    /// trigger without handler, off-screen UI, prewarm particles, duplicate components.
    /// </summary>
    public sealed class PrefabRules : IAuditRule
    {
        const int MaxHierarchyDepth = 10;
        const float UIPositionThreshold = 5000f;

        public RuleCategory Category => RuleCategory.PrefabIntegrity;

        public List<AuditFinding> Scan(string assetsRoot)
        {
            var findings = new List<AuditFinding>();

            // YAML-level checks (regex on file text)
            var prefabFiles = Directory.GetFiles(assetsRoot, "*.prefab", SearchOption.AllDirectories);
            var sceneFiles  = Directory.GetFiles(assetsRoot, "*.unity",  SearchOption.AllDirectories);

            foreach (var file in ScannerUtility.CombineArrays(prefabFiles, sceneFiles))
            {
                var text         = File.ReadAllText(file);
                var relativePath = ScannerUtility.MakeRelative(file, assetsRoot);

                CheckMissingScripts(text, relativePath, findings);
                CheckBrokenVariantBase(text, relativePath, findings);
            }

            // AssetDatabase-level checks (requires Unity import)
            CheckPrefabsViaAssetDatabase(findings);

            return findings;
        }

        // ---------------------------------------------------------------
        // YAML regex checks
        // ---------------------------------------------------------------

        private static void CheckMissingScripts(string yaml, string path, List<AuditFinding> findings)
        {
            // Unity writes {fileID: 0} when a MonoBehaviour's script GUID is missing
            var matches = Regex.Matches(yaml, @"m_Script:\s*\{fileID:\s*0\}");
            foreach (Match m in matches)
            {
                findings.Add(new AuditFinding
                {
                    Severity     = Severity.P0_BlockMerge,
                    Category     = RuleCategory.PrefabIntegrity,
                    RuleId       = "PF001",
                    Title        = "Missing script reference (fileID: 0)",
                    FilePath     = path,
                    Line         = ScannerUtility.CountLines(yaml, m.Index),
                    Detail       = m.Value.Trim(),
                    WhyItMatters = "Missing scripts cause NullReferenceExceptions and broken prefab behavior at runtime. " +
                                   "They are permanently broken until the script is restored.",
                    HowToFix     = "Locate the original script or remove the component. " +
                                   "Never commit prefabs with missing scripts — this is a P0 block.",
                });
            }
        }

        private static void CheckBrokenVariantBase(string yaml, string path, List<AuditFinding> findings)
        {
            // Prefab variant header references a base via m_SourcePrefab — check for null guid
            var matches = Regex.Matches(yaml,
                @"m_SourcePrefab:\s*\{fileID:\s*100100000,\s*guid:\s*([0-9a-f]{32}),",
                RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                var guid      = m.Groups[1].Value;
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(assetPath) ||
                    !File.Exists(Path.Combine(Application.dataPath, "..", assetPath)))
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P0_BlockMerge,
                        Category     = RuleCategory.PrefabIntegrity,
                        RuleId       = "PF002",
                        Title        = "Broken prefab variant base reference",
                        FilePath     = path,
                        Line         = ScannerUtility.CountLines(yaml, m.Index),
                        Detail       = $"GUID {guid} not found in AssetDatabase",
                        WhyItMatters = "A prefab variant with a missing base prefab is completely broken — " +
                                       "it cannot be instantiated or edited without errors.",
                        HowToFix     = "Restore the base prefab asset, or convert this variant to a standalone prefab. " +
                                       "Check VCS history for when the base was deleted.",
                    });
                }
            }
        }

        // ---------------------------------------------------------------
        // AssetDatabase checks
        // ---------------------------------------------------------------

        private static void CheckPrefabsViaAssetDatabase(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var go        = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;

                // PF003: Canvas without GraphicRaycaster
                var canvas = go.GetComponentInChildren<Canvas>(true);
                if (canvas != null && canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P1_MustFix,
                        Category     = RuleCategory.PrefabIntegrity,
                        RuleId       = "PF003",
                        Title        = "Canvas prefab missing GraphicRaycaster component",
                        FilePath     = assetPath,
                        Line         = 0,
                        Detail       = $"Canvas '{canvas.name}' has no GraphicRaycaster",
                        WhyItMatters = "A Canvas without a GraphicRaycaster won't process any UI input events — " +
                                       "buttons, sliders, and drag events will be silently ignored.",
                        HowToFix     = "Add a GraphicRaycaster component to the Canvas GameObject.",
                    });
                }

                // PF004: Non-uniform scale on non-UI, non-sprite GameObjects with colliders
                foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                {
                    var s = collider.transform.localScale;
                    bool nonUniform = Mathf.Abs(s.x - s.y) > 0.001f || Mathf.Abs(s.y - s.z) > 0.001f;
                    if (nonUniform)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.PrefabIntegrity,
                            RuleId       = "PF004",
                            Title        = "Non-uniform scale on collider GameObject",
                            FilePath     = assetPath,
                            Line         = 0,
                            Detail       = $"{collider.gameObject.name}: scale={s}",
                            WhyItMatters = "Non-uniform scale on colliders causes incorrect physics shapes, " +
                                           "especially on MeshColliders and CapsuleColliders.",
                            HowToFix     = "Apply scale at the mesh level (import settings) and use uniform scale in the scene.",
                        });
                    }
                }

                // PF005: Deeply nested prefab hierarchy exceeding threshold
                int maxDepth = GetMaxDepth(go.transform, 0);
                if (maxDepth > MaxHierarchyDepth)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P2_Suggestion,
                        Category     = RuleCategory.PrefabIntegrity,
                        RuleId       = "PF005",
                        Title        = $"Deeply nested prefab hierarchy ({maxDepth} levels, >{MaxHierarchyDepth} threshold)",
                        FilePath     = assetPath,
                        Line         = 0,
                        Detail       = $"Max nesting depth: {maxDepth}",
                        WhyItMatters = "Deeply nested hierarchies cause O(n) overhead on every Transform " +
                                       "operation and make prefabs extremely difficult to maintain.",
                        HowToFix     = "Flatten the hierarchy. Use empty parent objects sparingly. " +
                                       "Consider breaking into sub-prefabs.",
                    });
                }

                // PF006: Multiple EventSystem components in the same prefab
                var eventSystems = go.GetComponentsInChildren<EventSystem>(true);
                if (eventSystems.Length > 1)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P1_MustFix,
                        Category     = RuleCategory.PrefabIntegrity,
                        RuleId       = "PF006",
                        Title        = $"Multiple EventSystem components ({eventSystems.Length}) in prefab",
                        FilePath     = assetPath,
                        Line         = 0,
                        Detail       = $"Found {eventSystems.Length} EventSystem components",
                        WhyItMatters = "Multiple EventSystems cause input processing conflicts. Only one is " +
                                       "active at a time, and the others are silently ignored — " +
                                       "causing hard-to-debug input issues.",
                        HowToFix     = "Remove duplicate EventSystem components. Ensure only one EventSystem " +
                                       "exists in the scene.",
                    });
                }

                // PF007: Animator with missing runtimeAnimatorController
                foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                {
                    if (animator.runtimeAnimatorController == null)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P1_MustFix,
                            Category     = RuleCategory.PrefabIntegrity,
                            RuleId       = "PF007",
                            Title        = "Animator with missing RuntimeAnimatorController",
                            FilePath     = assetPath,
                            Line         = 0,
                            Detail       = $"Animator on '{animator.gameObject.name}' has no controller assigned",
                            WhyItMatters = "An Animator without a controller logs errors every frame and " +
                                           "wastes CPU on empty state machine evaluation.",
                            HowToFix     = "Assign an AnimatorController to the Animator component, " +
                                           "or remove the Animator if animation is not needed.",
                        });
                    }
                }

                // PF008: Collider with isTrigger but no OnTrigger handler on same GameObject
                foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger)
                    {
                        bool hasHandler = false;
                        var behaviours = collider.GetComponents<MonoBehaviour>();
                        foreach (var behaviour in behaviours)
                        {
                            if (behaviour == null) continue;
                            var type = behaviour.GetType();
                            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                            if (type.GetMethod("OnTriggerEnter", flags) != null ||
                                type.GetMethod("OnTriggerStay", flags) != null ||
                                type.GetMethod("OnTriggerExit", flags) != null)
                            {
                                hasHandler = true;
                                break;
                            }
                        }

                        if (!hasHandler)
                        {
                            findings.Add(new AuditFinding
                            {
                                Severity     = Severity.P2_Suggestion,
                                Category     = RuleCategory.PrefabIntegrity,
                                RuleId       = "PF008",
                                Title        = "Trigger collider without OnTrigger handler on same GameObject",
                                FilePath     = assetPath,
                                Line         = 0,
                                Detail       = $"Collider on '{collider.gameObject.name}' has isTrigger=true but no OnTrigger methods",
                                WhyItMatters = "A trigger collider without any OnTrigger handler means trigger events " +
                                               "fire but nothing responds — usually indicating a missing or misplaced script.",
                                HowToFix     = "Add a MonoBehaviour with OnTriggerEnter/OnTriggerStay/OnTriggerExit " +
                                               "to the same GameObject, or move the trigger collider to the " +
                                               "GameObject that has the handler.",
                            });
                        }
                    }
                }

                // PF009: UI RectTransform positioned far outside reasonable bounds
                foreach (var rectTransform in go.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rectTransform.anchoredPosition.magnitude > UIPositionThreshold)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.PrefabIntegrity,
                            RuleId       = "PF009",
                            Title        = "UI element positioned far outside expected bounds",
                            FilePath     = assetPath,
                            Line         = 0,
                            Detail       = $"RectTransform '{rectTransform.name}' anchoredPosition={rectTransform.anchoredPosition} " +
                                           $"(magnitude {rectTransform.anchoredPosition.magnitude:F0} > {UIPositionThreshold})",
                            WhyItMatters = "UI elements positioned outside the Canvas bounds are invisible to users " +
                                           "with no runtime error — often indicating a drag accident or incorrect anchor setup.",
                            HowToFix     = "Reset the RectTransform position or check anchor settings. " +
                                           "Verify the element is within the Canvas boundaries.",
                        });
                    }
                }

                // PF010: ParticleSystem with prewarm enabled
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.main.prewarm)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.PrefabIntegrity,
                            RuleId       = "PF010",
                            Title        = "ParticleSystem with prewarm enabled",
                            FilePath     = assetPath,
                            Line         = 0,
                            Detail       = $"ParticleSystem on '{ps.gameObject.name}' has prewarm=true",
                            WhyItMatters = "Prewarm simulates the entire particle lifetime on Instantiate, " +
                                           "causing frame spikes especially on mobile devices.",
                            HowToFix     = "Disable Prewarm in ParticleSystem settings unless the visual effect " +
                                           "absolutely requires it. Consider using a pool of pre-warmed particles instead.",
                        });
                    }
                }

                // PF011: Duplicate component of the same type on a single GameObject (excluding Transform)
                CheckDuplicateComponents(go, assetPath, findings);
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Recursively compute the maximum nesting depth of a Transform hierarchy.
        /// </summary>
        private static int GetMaxDepth(Transform t, int current)
        {
            int max = current;
            for (int i = 0; i < t.childCount; i++)
            {
                int childDepth = GetMaxDepth(t.GetChild(i), current + 1);
                if (childDepth > max) max = childDepth;
            }
            return max;
        }

        /// <summary>
        /// Check all children of a prefab root for duplicate components of the same type.
        /// </summary>
        private static void CheckDuplicateComponents(GameObject root, string assetPath, List<AuditFinding> findings)
        {
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                var components = t.gameObject.GetComponents<Component>();
                var typeCounts = new Dictionary<System.Type, int>();

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var type = comp.GetType();
                    if (type == typeof(Transform) || type == typeof(RectTransform)) continue;

                    if (typeCounts.ContainsKey(type))
                        typeCounts[type]++;
                    else
                        typeCounts[type] = 1;
                }

                foreach (var kvp in typeCounts)
                {
                    if (kvp.Value > 1)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.PrefabIntegrity,
                            RuleId       = "PF011",
                            Title        = $"Duplicate {kvp.Key.Name} component ({kvp.Value}x) on same GameObject",
                            FilePath     = assetPath,
                            Line         = 0,
                            Detail       = $"'{t.gameObject.name}' has {kvp.Value} instances of {kvp.Key.Name}",
                            WhyItMatters = "Duplicate components of the same type usually indicate accidental " +
                                           "additions. They cause double-processing and unexpected behavior.",
                            HowToFix     = "Remove the duplicate component. If both are intentional, consider " +
                                           "restructuring into child GameObjects.",
                        });
                    }
                }
            }
        }
    }
}
#endif

#!/usr/bin/env python3
"""
unity_auditor.py — Headless Unity Auditor

Runs the same rule set as UnityAuditorWindow.cs but purely in Python
with no Unity process required. Outputs:
  - Human-readable terminal report (default)
  - GitHub Actions annotations (--github-annotations)
  - SARIF v2.1 (--sarif <output.sarif.json>)
  - JSON (--json <output.json>)

Usage:
    python unity_auditor.py [assets_root] [options]

    assets_root: path to Unity project's Assets/ folder
                 defaults to ./Assets

GitHub Actions:
    - name: Unity Auditor
      run: python Scripts/unity_auditor.py Assets --github-annotations
      # Will annotate PR lines and set exit code 1 if P0 found

Exit codes:
    0 — no issues (or only P2 suggestions)
    1 — one or more P0/P1 issues found
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Callable, Iterator, Optional


# ---------------------------------------------------------------------------
# Note on C#/Python rule parity
# ---------------------------------------------------------------------------
# The following C# rules require Unity AssetDatabase and CANNOT run headlessly:
#   AS001-AS013 (AssetSettings: texture/mesh/audio/animation/shader/material/atlas/video/font)
#   PF002 (Broken prefab variant base)
#   PF003-PF011 (Prefab: Canvas/collider/nesting/EventSystem/Animator/trigger/UI/particles/duplicates)
# Only PF001 and PF003 have YAML heuristic equivalents in this script.
# All regex-based rules (CodeLogic, Security, Serialization, Performance,
# Lifecycle, Architecture, Optimization) are fully mirrored.
# ---------------------------------------------------------------------------


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

SEVERITY_P0 = "P0_BlockMerge"
SEVERITY_P1 = "P1_MustFix"
SEVERITY_P2 = "P2_Suggestion"

CATEGORY_CODE = "CodeLogic"
CATEGORY_SERIAL = "Serialization"
CATEGORY_SEC = "Security"
CATEGORY_PERF = "Performance"
CATEGORY_PREFAB = "PrefabIntegrity"
CATEGORY_ASSET = "AssetSettings"
CATEGORY_LIFE = "Lifecycle"
CATEGORY_ARCH = "Architecture"
CATEGORY_OPT = "Optimization"


@dataclass
class Finding:
    severity: str
    category: str
    rule_id: str
    title: str
    file_path: str
    line: int
    detail: str
    why: str
    how_to_fix: str


# ---------------------------------------------------------------------------
# Suppression support
# ---------------------------------------------------------------------------

IGNORE_PREFIX = "UnityAuditor:ignore "
IGNORE_NEXT_LINE_PREFIX = "UnityAuditor:ignore-next-line "


def _is_suppressed(lines: list, line_number: int, rule_id: str) -> bool:
    """Check if a finding at line_number (1-based) is suppressed."""
    if line_number < 1 or line_number > len(lines):
        return False

    current_line = lines[line_number - 1]

    # Same-line: // UnityAuditor:ignore RULEID
    idx = current_line.find(IGNORE_PREFIX)
    if idx >= 0:
        # Make sure it's not the longer "ignore-next-line" variant
        next_idx = current_line.find(IGNORE_NEXT_LINE_PREFIX)
        if next_idx < 0 or next_idx != idx:
            rest = current_line[idx + len(IGNORE_PREFIX) :].strip()
            if rest.startswith(rule_id) and (
                len(rest) == len(rule_id) or rest[len(rule_id)] in (" ", "\t", "\n")
            ):
                return True

    # Previous line: // UnityAuditor:ignore-next-line RULEID
    if line_number >= 2:
        prev_line = lines[line_number - 2]
        idx = prev_line.find(IGNORE_NEXT_LINE_PREFIX)
        if idx >= 0:
            rest = prev_line[idx + len(IGNORE_NEXT_LINE_PREFIX) :].strip()
            if rest.startswith(rule_id) and (
                len(rest) == len(rule_id) or rest[len(rule_id)] in (" ", "\t", "\n")
            ):
                return True

    return False


# ---------------------------------------------------------------------------
# Base rule helpers
# ---------------------------------------------------------------------------


def _scan_cs_files(assets_root: str) -> Iterator:
    """Yield (file_path, full_text, lines) for every .cs file."""
    for root, dirs, files in os.walk(assets_root):
        # Skip generated / editor-only dirs
        dirs[:] = [d for d in dirs if d not in ("Generated", "Editor")]
        for f in files:
            if f.endswith(".cs"):
                full = os.path.join(root, f)
                try:
                    text = Path(full).read_text(encoding="utf-8", errors="replace")
                    yield full, text, text.splitlines()
                except OSError:
                    pass


def _scan_cs_files_with_editor(assets_root: str) -> Iterator:
    """Yield (file_path, full_text, lines) for every .cs file INCLUDING Editor/."""
    for root, dirs, files in os.walk(assets_root):
        # Skip Generated dirs only — include Editor/ for Architecture rules
        dirs[:] = [d for d in dirs if d != "Generated"]
        for f in files:
            if f.endswith(".cs"):
                full = os.path.join(root, f)
                try:
                    text = Path(full).read_text(encoding="utf-8", errors="replace")
                    yield full, text, text.splitlines()
                except OSError:
                    pass


def _rel(full_path: str, assets_root: str) -> str:
    try:
        return os.path.relpath(full_path, assets_root)
    except ValueError:
        return full_path


def _line_of(text: str, char_idx: int) -> int:
    return text[:char_idx].count("\n") + 1


def _regex_scan(
    assets_root: str,
    category: str,
    rules: list,
    extra_filter: Optional[Callable] = None,
    include_editor: bool = False,
) -> list:
    findings = []
    scanner = _scan_cs_files_with_editor if include_editor else _scan_cs_files
    for file_path, text, lines in scanner(assets_root):
        if extra_filter and extra_filter(file_path):
            continue
        rel = _rel(file_path, assets_root)
        has_suppression = "UnityAuditor:ignore" in text  # Fast check
        for rule_id, title, pattern, severity, why, fix in rules:
            try:
                for m in re.finditer(pattern, text, re.IGNORECASE | re.DOTALL):
                    line_num = _line_of(text, m.start())
                    if has_suppression and _is_suppressed(lines, line_num, rule_id):
                        continue
                    findings.append(
                        Finding(
                            severity=severity,
                            category=category,
                            rule_id=rule_id,
                            title=title,
                            file_path=rel,
                            line=line_num,
                            detail=m.group(0)[:120].strip(),
                            why=why,
                            how_to_fix=fix,
                        )
                    )
            except re.error:
                pass
    return findings


# ---------------------------------------------------------------------------
# Code Logic Rules  (mirrors CodeLogicRules.cs)
# ---------------------------------------------------------------------------

CODE_RULES = [
    (
        "CL001",
        "Unity Object null check using == null",
        r"if\s*\(\s*\w+\s*==\s*null\s*\)|if\s*\(\s*null\s*==\s*\w+\s*\)",
        SEVERITY_P1,
        "Unity overloads == for destroyed objects — but 'is null' will NOT catch destroyed objects.",
        "Use 'if (!obj)' for destroyed-object checks on UnityEngine.Object subclasses.",
    ),
    (
        "CL002",
        "Camera.main accessed in hot path",
        r"(void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*Camera\.main",
        SEVERITY_P1,
        "Camera.main calls FindObjectOfType internally — O(n) scan every frame.",
        "Cache Camera.main in Awake/Start.",
    ),
    (
        "CL003",
        "GameObject.Find / FindObjectOfType outside Awake/Start",
        r"(?:GameObject\.Find|FindObjectOfType|FindObjectsOfType)\s*[<(]",
        SEVERITY_P1,
        "Find* methods are O(n) scene scans. Called per-frame they spike CPU.",
        "Cache in Awake/Start or use dependency injection.",
    ),
    (
        "CL004",
        "Empty catch block swallows exceptions",
        r"catch\s*(?:\([^)]*\))?\s*\{\s*\}",
        SEVERITY_P1,
        "Silent failures hide bugs and make debugging extremely difficult.",
        "Add at minimum: catch (Exception e) { Debug.LogException(e); }",
    ),
    (
        "CL005",
        "GetComponent inside Update loop",
        r"(?:void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*GetComponent\s*[<(]",
        SEVERITY_P1,
        "GetComponent involves a native-to-managed bridge call every frame.",
        "Cache component references in Awake.",
    ),
    (
        "CL006",
        "StartCoroutine without null/active guard",
        r"StartCoroutine\s*\(",
        SEVERITY_P2,
        "StartCoroutine on a disabled or destroyed GameObject throws MissingReferenceException.",
        "Guard with: if (this != null && gameObject.activeInHierarchy) StartCoroutine(...);",
    ),
    (
        "CL007",
        "Destroy called without null check",
        r"Destroy\s*\(\s*\w+\s*\)",
        SEVERITY_P2,
        "Calling Destroy(null) is safe, but Destroy on an already-destroyed object logs a warning.",
        "Use if (obj != null) Destroy(obj); for clarity and to suppress spurious warnings.",
    ),
    # --- New from Goal 2 ---
    (
        "CL008",
        "Coroutine method (IEnumerator) without any yield return",
        r"IEnumerator\s+\w+\s*\([^)]*\)\s*\{(?:(?!yield\s+return)[^}])*\}",
        SEVERITY_P1,
        "An IEnumerator method without yield executes entirely in one frame — defeating the purpose of coroutines.",
        "Add yield return statements or change the return type to void if coroutine behavior is not needed.",
    ),
    (
        "CL009",
        "async void method declaration (should be async Task)",
        r"async\s+void\s+\w+\s*\(",
        SEVERITY_P1,
        "async void methods swallow exceptions silently and cannot be awaited.",
        "Change to 'async Task MethodName()' and await the result.",
    ),
    (
        "CL010",
        "UnityEvent.Invoke() called without listener/null guard",
        r"\.Invoke\s*\(\s*\)",
        SEVERITY_P2,
        "Invoking a UnityEvent with zero persistent listeners is a no-op — may indicate misconfigured wiring.",
        "Check GetPersistentEventCount() > 0 before invoking.",
    ),
]


def run_code_logic(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_CODE, CODE_RULES)


# ---------------------------------------------------------------------------
# Serialization Rules  (mirrors SerializationRules.cs)
# ---------------------------------------------------------------------------

SERIAL_RULES = [
    (
        "SR001",
        "Public field without [SerializeField]",
        r"public\s+(?!static|const|readonly|class|struct|enum|interface|override|virtual|abstract)[\w<>\[\]]+\s+\w+\s*;",
        SEVERITY_P2,
        "Public fields expose data externally. Prefer [SerializeField] private fields.",
        "Change to [SerializeField] private and add a public property if needed.",
    ),
    (
        "SR002",
        "Dictionary/HashSet used as serialized field",
        r"\[SerializeField\][^;]*(?:Dictionary|HashSet)\s*<",
        SEVERITY_P0,
        "Unity cannot serialize Dictionary or HashSet — always null at runtime.",
        "Use a List<KVPair> or a custom [Serializable] class instead.",
    ),
    (
        "SR003",
        "Auto-property on [System.Serializable] class",
        r"\[System\.Serializable\][^{]*\{[^}]*\{\s*get;\s*(?:set;)?\s*\}",
        SEVERITY_P1,
        "Unity serializes fields, not properties. Auto-properties are silently ignored.",
        "Replace with [SerializeField] private fields.",
    ),
    (
        "SR004",
        "BinaryFormatter usage",
        r"BinaryFormatter\s*(?:\(\)|\.)",
        SEVERITY_P0,
        "BinaryFormatter is deprecated and has deserialization RCE vulnerabilities.",
        "Replace with JsonUtility, Newtonsoft.Json, or a proper binary format.",
    ),
    (
        "SR005",
        "[SerializeReference] field with concrete sealed type",
        r"\[SerializeReference\][^;]*\b(?:int|float|bool|string|Vector2|Vector3|Quaternion)\b",
        SEVERITY_P2,
        "[SerializeReference] is for polymorphic references — overhead with no benefit on value/sealed types.",
        "Use [SerializeField] for value types and concrete sealed types.",
    ),
    (
        "SR006",
        "OnValidate modifying serialized data without Undo",
        r"void\s+OnValidate\s*\(\s*\)[^}]*=\s*[^;]+;",
        SEVERITY_P1,
        "OnValidate runs on every Editor change. Modifying fields without Undo.RecordObject causes data loss.",
        'Guard: UnityEditor.Undo.RecordObject(this, "Validate"); before field changes.',
    ),
    # --- New from Goal 4 ---
    (
        "SR007",
        "[NonSerialized] on a private field — redundant",
        r"\[NonSerialized\]\s*private\s+",
        SEVERITY_P2,
        "Private fields without [SerializeField] are already non-serialized.",
        "Remove the [NonSerialized] attribute.",
    ),
    (
        "SR008",
        "Public mutable List/array on ScriptableObject",
        r"(?:ScriptableObject)[^}]*public\s+(?:List\s*<|[\w]+\s*\[\])\s*\w+",
        SEVERITY_P1,
        "ScriptableObject instances are shared. Public mutable collections can be accidentally modified.",
        "Use [HideInInspector] or make private with [SerializeField].",
    ),
    (
        "SR009",
        "Enum without explicit integer values",
        r"enum\s+\w+\s*\{[^}=]+\}",
        SEVERITY_P2,
        "Enums without explicit values will silently corrupt serialized references if members change.",
        "Assign explicit values: enum MyEnum { None = 0, TypeA = 1, TypeB = 2 }.",
    ),
    (
        "SR010",
        "[SerializeField] with large default initializer (>100 elements)",
        r"\[SerializeField\][^;]*(?:new\s+\w+\s*\[\s*\d{3,}\s*\]|new\s+List\s*<[^>]+>\s*\(\s*\d{3,}\s*\))",
        SEVERITY_P2,
        "Large serialized collections bloat YAML files and slow Inspector.",
        "Reduce default size or populate at runtime.",
    ),
    (
        "SR011",
        "[System.Serializable] class — verify parameterless constructor",
        r"\[System\.Serializable\]\s*(?:public\s+|internal\s+)?(?:sealed\s+)?class\s+\w+",
        SEVERITY_P1,
        "Unity serialization requires parameterless constructor. Missing causes silent data loss.",
        "Add: public MyClass() { }",
    ),
]


def run_serialization(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_SERIAL, SERIAL_RULES)


# ---------------------------------------------------------------------------
# Security Rules  (mirrors SecurityRules.cs)
# ---------------------------------------------------------------------------

SEC_RULES = [
    (
        "SEC001",
        "PlayerPrefs storing sensitive data",
        r'PlayerPrefs\.Set(?:String|Int|Float)\s*\(\s*"[^"]*(?:password|token|secret|apikey|api_key|auth)[^"]*"',
        SEVERITY_P0,
        "PlayerPrefs is plaintext on disk — readable by other apps or from device backups.",
        "Use OS Keychain APIs or server-side token exchange. Never store credentials in PlayerPrefs.",
    ),
    (
        "SEC002",
        "Hardcoded secret/API key",
        r'(?:api[_-]?key|secret|password|token|bearer|apiSecret)\s*=\s*"[A-Za-z0-9+/=_\-]{8,}"',
        SEVERITY_P0,
        "Hardcoded secrets are trivially extracted from built apps.",
        "Use environment variables or a secrets manager. Rotate any exposed secrets immediately.",
    ),
    (
        "SEC003",
        "Assembly.Load / LoadFrom with runtime path",
        r"Assembly\.(?:Load|LoadFrom|LoadFile)\s*\(",
        SEVERITY_P0,
        "Dynamic assembly loading is the vector for CVE-2025-59489 (Unity Android ACE, CVSS 8.4).",
        "Remove dynamic assembly loading. Validate signatures if plugin architecture is required.",
    ),
    (
        "SEC004",
        "Application.OpenURL with variable input",
        r'Application\.OpenURL\s*\(\s*(?!"http)',
        SEVERITY_P1,
        "OpenURL with unsanitized input enables URL injection attacks.",
        "Validate URLs against an allowlist before calling OpenURL.",
    ),
    (
        "SEC005",
        "WWW class usage (deprecated, no TLS enforcement)",
        r"\bnew\s+WWW\s*\(",
        SEVERITY_P1,
        "WWW is deprecated since Unity 2018.2 and doesn't enforce TLS certificate validation.",
        "Replace with UnityWebRequest.",
    ),
    (
        "SEC006",
        "Insecure HTTP endpoint",
        r'"http://(?!localhost|127\.0\.0\.1)',
        SEVERITY_P1,
        "Plain HTTP transmits data unencrypted, including auth tokens.",
        "Use HTTPS for all production endpoints.",
    ),
    # --- New from Goal 5 ---
    (
        "SEC007",
        "Unsafe JSON deserialization with TypeNameHandling",
        r"TypeNameHandling\s*\.\s*(?:All|Auto|Objects)",
        SEVERITY_P0,
        "TypeNameHandling.All/Auto/Objects enables type confusion RCE attacks.",
        "Use TypeNameHandling.None. For polymorphic deserialization, use custom SerializationBinder.",
    ),
    (
        "SEC008",
        "File read with variable path — path traversal risk",
        r"(?:File\.ReadAllText|File\.ReadAllBytes|StreamReader)\s*\(\s*[a-zA-Z_]\w*",
        SEVERITY_P0,
        "File read with variable paths vulnerable to path traversal (../../etc/passwd).",
        "Validate paths against allowlist. Use Path.GetFullPath() and verify base directory.",
    ),
    (
        "SEC009",
        "Process.Start with non-literal arguments — command injection",
        r"Process\.Start\s*\(\s*[a-zA-Z_]\w*",
        SEVERITY_P0,
        "Process.Start with variable arguments enables command injection.",
        "Use ProcessStartInfo with hardcoded executable. Validate all arguments.",
    ),
]


def run_security(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_SEC, SEC_RULES)


# ---------------------------------------------------------------------------
# Performance Rules  (mirrors PerformanceRules.cs)
# ---------------------------------------------------------------------------

PERF_RULES = [
    (
        "PERF001",
        "String concatenation (+) in Update — GC alloc per frame",
        r'(?:void\s+(?:Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)\+\s*"|\+\s*[a-zA-Z_]\w*\s*\+',
        SEVERITY_P1,
        "String + string creates a new heap allocation every call at 60fps.",
        "Use StringBuilder or string.Format with cached format strings.",
    ),
    (
        "PERF002",
        "new() allocation inside Update/FixedUpdate/LateUpdate",
        r"(?:void\s+(?:Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)\bnew\s+(?!List|Dictionary|HashSet|Queue|Stack|Array)\w+\s*[(<]",
        SEVERITY_P1,
        "Object allocation in a hot path forces GC collection.",
        "Cache reusable objects as fields. Use object pools.",
    ),
    (
        "PERF003",
        "LINQ in Update hot path",
        r"(?:void\s+(?:Update|FixedUpdate|LateUpdate)[^{]*\{[^}]*)(?:\.Where\(|\.Select\(|\.FirstOrDefault\(|\.Any\(|\.OrderBy\(|\.ToList\(|\.ToArray\()",
        SEVERITY_P1,
        "LINQ allocates IEnumerator objects and intermediate collections on the heap.",
        "Pre-compute and cache LINQ results. Use manual for loops in hot paths.",
    ),
    (
        "PERF004",
        "SendMessage / BroadcastMessage",
        r"\b(?:SendMessage|BroadcastMessage|SendMessageUpwards)\s*\(",
        SEVERITY_P1,
        "SendMessage uses reflection — ~10x slower than a direct call.",
        "Replace with direct calls, C# events, or UnityEvent.",
    ),
    (
        "PERF005",
        "Debug.Log called outside #if UNITY_EDITOR guard",
        r"(?<!\#if\s+UNITY_EDITOR[^\n]*\n[^\n]*)Debug\.(?:Log|LogWarning|LogError|LogFormat)\s*\(",
        SEVERITY_P2,
        "Debug.Log has significant overhead in release builds: string formatting, stack trace capture.",
        "Wrap all debug logging in #if UNITY_EDITOR or #if DEVELOPMENT_BUILD.",
    ),
    (
        "PERF006",
        "transform.tag comparison with string",
        r'\.tag\s*==\s*"',
        SEVERITY_P2,
        'gameObject.tag == "string" allocates a new string each comparison.',
        'Use gameObject.CompareTag("TagName") — allocation-free and faster.',
    ),
    (
        "PERF007",
        "RaycastHit allocated inside a loop",
        r"(?:for|while|foreach)[^{]*\{[^}]*RaycastHit\s+\w+\s*=\s*new\s+RaycastHit",
        SEVERITY_P2,
        "Declaring RaycastHit inside a loop re-initializes the struct each iteration.",
        "Declare RaycastHit as a field or outside the loop scope.",
    ),
    # --- New from Goal 3 ---
    (
        "PERF008",
        "new WaitForSeconds() inside a coroutine loop",
        r"(?:while|for)\s*\([^)]*\)\s*\{[^}]*yield\s+return\s+new\s+WaitForSeconds",
        SEVERITY_P1,
        "Creating new WaitForSeconds inside a loop allocates a new object every iteration.",
        "Cache: private WaitForSeconds _wait = new WaitForSeconds(1f); yield return _wait;",
    ),
    (
        "PERF009",
        "Resources.Load called inside Update/FixedUpdate/LateUpdate",
        r"(?:void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*Resources\.Load\s*[<(]",
        SEVERITY_P1,
        "Resources.Load is a synchronous disk read. Calling it every frame causes frame spikes.",
        "Load resources in Awake/Start and cache. Use Addressables for async loading.",
    ),
    (
        "PERF010",
        "Manual Camera.Render() or Camera.RenderWithShader() call",
        r"Camera\.\s*(?:Render|RenderWithShader)\s*\(",
        SEVERITY_P2,
        "Manual camera rendering is extremely expensive and often unintentional.",
        "Verify this manual render call is intentional. Consider RenderTextures instead.",
    ),
]


def run_performance(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_PERF, PERF_RULES)


# ---------------------------------------------------------------------------
# Lifecycle Rules  (mirrors LifecycleRules.cs)
# ---------------------------------------------------------------------------

LIFECYCLE_RULES = [
    (
        "LC001",
        "Virtual method call in Awake() or constructor",
        r"(?:void\s+Awake|(?:public|private|protected|internal)\s+\w+\s*\(\s*\))\s*\{[^}]*(?:virtual|base\.)\w+",
        SEVERITY_P1,
        "Calling virtual methods in Awake/constructor runs base implementation — derived class not initialized.",
        "Move virtual/overridden calls to Start().",
    ),
    (
        "LC002",
        "GetComponent/Find* targeting other GameObjects in Awake()",
        r"void\s+Awake\s*\(\s*\)[^}]*(?:GetComponentInParent|GetComponentInChildren|FindObjectOfType|GameObject\.Find)\s*[<(]",
        SEVERITY_P1,
        "In Awake(), sibling initialization order is undefined. Cross-GO GetComponent may return uninitialized.",
        "Move cross-GameObject GetComponent/Find calls to Start().",
    ),
    (
        "LC003",
        "DontDestroyOnLoad called on a child GameObject",
        r"DontDestroyOnLoad\s*\(\s*(?!gameObject|this\.gameObject)\w+",
        SEVERITY_P1,
        "DontDestroyOnLoad only works on root GameObjects. Children get destroyed with parent's scene.",
        "Call on root: DontDestroyOnLoad(transform.root.gameObject);",
    ),
    (
        "LC004",
        "OnDestroy accessing components without null-check",
        r"void\s+OnDestroy\s*\(\s*\)[^}]*(?:GetComponent|\.transform|\.gameObject)\s*[<.(]",
        SEVERITY_P2,
        "During teardown, referenced components may already be destroyed.",
        "Null-check all references in OnDestroy.",
    ),
    (
        "LC005",
        "Time.deltaTime used inside FixedUpdate",
        r"void\s+FixedUpdate\s*\(\s*\)[^}]*Time\.deltaTime",
        SEVERITY_P1,
        "FixedUpdate uses fixed timestep. Use Time.fixedDeltaTime for clarity and correctness.",
        "Replace Time.deltaTime with Time.fixedDeltaTime.",
    ),
    (
        "LC006",
        "DefaultExecutionOrder attribute usage",
        r"\[DefaultExecutionOrder\s*\(\s*(?:-?\d+)\s*\)\]",
        SEVERITY_P2,
        "Duplicate order values defeat explicit ordering — relative order remains undefined.",
        "Assign unique values. This rule flags all usages for manual review.",
    ),
]


def run_lifecycle(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_LIFE, LIFECYCLE_RULES)


# ---------------------------------------------------------------------------
# Architecture Rules  (mirrors ArchitectureRules.cs)
# ---------------------------------------------------------------------------

ARCHITECTURE_RULES = [
    (
        "AR001",
        "MonoBehaviour/ScriptableObject class body exceeds ~500 lines",
        r"(?s)class\s+\w+\s*:\s*(?:MonoBehaviour|ScriptableObject)[^{]*\{(.{15000,})",
        SEVERITY_P2,
        "Classes >500 lines are hard to maintain. Often multiple responsibilities.",
        "Extract into separate components. Follow Single Responsibility Principle.",
    ),
    (
        "AR002",
        "Class has many public methods (potential god class)",
        r"(?:public\s+(?:virtual\s+|override\s+|static\s+)?(?:void|bool|int|float|string|[\w<>\[\]]+)\s+\w+\s*\([^)]*\)\s*\{[^}]*\}.*?){10,}",
        SEVERITY_P2,
        "Many public methods = too many responsibilities (god class anti-pattern).",
        "Apply Interface Segregation. Split into focused interfaces.",
    ),
    (
        "AR003",
        "Runtime script uses 'using UnityEditor' without guard",
        r"(?<!\#if\s+UNITY_EDITOR[^\n]*\n\s*)using\s+UnityEditor\b",
        SEVERITY_P1,
        "'using UnityEditor' in runtime scripts causes build failures on device.",
        "Wrap in #if UNITY_EDITOR / #endif or move to Editor/ folder.",
    ),
    (
        "AR004",
        "Static mutable field in MonoBehaviour",
        r"(?:class\s+\w+\s*:\s*MonoBehaviour)[^}]*static\s+(?!readonly|const)[\w<>\[\]]+\s+\w+\s*[;=]",
        SEVERITY_P1,
        "Static fields survive scene reloads — subtle state persistence bugs.",
        "Use instance fields or ScriptableObject data containers.",
    ),
    (
        "AR005",
        "Class implementing more than 5 interfaces",
        r"class\s+\w+\s*:\s*(?:\w+\s*,\s*){5,}",
        SEVERITY_P2,
        "Many interfaces = too many responsibilities. Violates ISP.",
        "Split class or consolidate related interfaces.",
    ),
    (
        "AR006",
        "Namespace declaration (verify folder alignment)",
        r"namespace\s+\w+",
        SEVERITY_P2,
        "Namespace-path mismatch makes files hard to find.",
        "Align namespace with folder structure.",
    ),
]


def run_architecture(assets_root: str) -> list:
    # Architecture rules skip Editor/ folders (matching C# ArchitectureRules.ShouldSkipFile)
    return _regex_scan(
        assets_root,
        CATEGORY_ARCH,
        ARCHITECTURE_RULES,
        extra_filter=lambda p: "/Editor/" in p or "\\Editor\\" in p,
    )


# ---------------------------------------------------------------------------
# Optimization Rules  (mirrors OptimizationRules.cs)
# ---------------------------------------------------------------------------

OPT_RULES = [
    (
        "OPT001",
        "Private method could potentially be static",
        r"private\s+(?:void|bool|int|float|string|[\w<>\[\]]+)\s+\w+\s*\([^)]*\)\s*\{",
        SEVERITY_P2,
        "Non-static methods that don't use instance members mislead about dependencies.",
        "Add 'static' if no instance members used. Verify manually — this is heuristic.",
    ),
    (
        "OPT002",
        "Boxing of value type",
        r"(?:object\s+\w+\s*=\s*(?!null|new)\w+|string\.Format\s*\([^)]*\{0\})",
        SEVERITY_P1,
        "Boxing allocates heap object to wrap value type. GC pressure in hot paths.",
        "Use generic methods/containers instead of object.",
    ),
    (
        "OPT003",
        "String interpolation or Format in Update/hot paths",
        r'(?:void\s+(?:Update|FixedUpdate|LateUpdate))[^}]*(?:\$"|string\.Format\s*\()',
        SEVERITY_P2,
        "String alloc every frame = thousands of GC objects/sec.",
        "Cache strings or use StringBuilder. Wrap debug in #if UNITY_EDITOR.",
    ),
    (
        "OPT004",
        "foreach on non-generic collections (ArrayList, Hashtable)",
        r"foreach\s*\([^)]*\bin\s+(?:\w+\.)?(?:ArrayList|Hashtable|SortedList)\b",
        SEVERITY_P2,
        "Non-generic iteration boxes enumerator and elements.",
        "Replace: ArrayList->List<T>, Hashtable->Dictionary<K,V>.",
    ),
    (
        "OPT005",
        "Multiple GetComponent<T>() calls in same method",
        r"GetComponent\s*<\s*\w+\s*>\s*\(\s*\).*GetComponent\s*<\s*\w+\s*>\s*\(\s*\)",
        SEVERITY_P2,
        "Each GetComponent traverses component list. Cache in local variable.",
        "var rb = GetComponent<Rigidbody>(); then reuse rb.",
    ),
    (
        "OPT006",
        "SetPixels/SetPixel in loop without batched Apply",
        r"(?:for|while|foreach)[^{]*\{[^}]*(?:SetPixels?|SetPixel)\s*\(",
        SEVERITY_P2,
        "Each SetPixel without batch Apply re-uploads texture to GPU.",
        "Call SetPixels in batch, then Apply() once.",
    ),
    (
        "OPT007",
        "Physics cast without explicit layerMask",
        r"Physics\.(?:Raycast|SphereCast|BoxCast|CapsuleCast)\s*\(\s*[^,)]+,\s*[^,)]+\s*\)",
        SEVERITY_P1,
        "Without layerMask, queries check ALL layers — wasted CPU.",
        "Add layerMask: Physics.Raycast(origin, dir, maxDist, layerMask).",
    ),
]


def run_optimization(assets_root: str) -> list:
    return _regex_scan(assets_root, CATEGORY_OPT, OPT_RULES)


# ---------------------------------------------------------------------------
# Prefab Rules  (YAML-only — no AssetDatabase available headlessly)
# ---------------------------------------------------------------------------


def run_prefab(assets_root: str) -> list:
    findings = []
    exts = ("*.prefab", "*.unity")

    for ext in exts:
        for filepath in Path(assets_root).rglob(ext):
            try:
                text = filepath.read_text(encoding="utf-8", errors="replace")
                rel = _rel(str(filepath), assets_root)

                # PF001: Missing script references
                for m in re.finditer(r"m_Script:\s*\{fileID:\s*0\}", text):
                    findings.append(
                        Finding(
                            severity=SEVERITY_P0,
                            category=CATEGORY_PREFAB,
                            rule_id="PF001",
                            title="Missing script reference (fileID: 0)",
                            file_path=rel,
                            line=_line_of(text, m.start()),
                            detail=m.group(0).strip(),
                            why="Missing scripts cause NullReferenceExceptions and broken prefab behavior at runtime.",
                            how_to_fix="Locate the original script or remove the component. Never commit with missing scripts.",
                        )
                    )

                # PF003: Canvas without GraphicRaycaster (YAML heuristic)
                if "Canvas:" in text and "GraphicRaycaster:" not in text:
                    findings.append(
                        Finding(
                            severity=SEVERITY_P1,
                            category=CATEGORY_PREFAB,
                            rule_id="PF003",
                            title="Canvas prefab likely missing GraphicRaycaster",
                            file_path=rel,
                            line=0,
                            detail="Canvas found, GraphicRaycaster not found in YAML",
                            why="A Canvas without GraphicRaycaster won't process any UI input events.",
                            how_to_fix="Add GraphicRaycaster component to the Canvas GameObject.",
                        )
                    )

            except OSError:
                pass

    return findings


# ---------------------------------------------------------------------------
# Orchestrator
# ---------------------------------------------------------------------------


def run_all(assets_root: str) -> list:
    runners = [
        run_code_logic,
        run_serialization,
        run_security,
        run_performance,
        run_lifecycle,
        run_architecture,
        run_optimization,
        run_prefab,
    ]
    findings = []
    for runner in runners:
        findings.extend(runner(assets_root))

    findings.sort(
        key=lambda f: (
            {"P0_BlockMerge": 0, "P1_MustFix": 1, "P2_Suggestion": 2}.get(
                f.severity, 9
            ),
            f.category,
            f.file_path,
            f.line,
        )
    )
    return findings


# ---------------------------------------------------------------------------
# Output formatters
# ---------------------------------------------------------------------------

SEVERITY_EMOJI = {
    SEVERITY_P0: "\u26d4",
    SEVERITY_P1: "\u26a0\ufe0f ",
    SEVERITY_P2: "\U0001f4a1",
}

ANSI = {
    "reset": "\033[0m",
    "bold": "\033[1m",
    "red": "\033[31m",
    "yellow": "\033[33m",
    "blue": "\033[34m",
    "grey": "\033[90m",
    "green": "\033[32m",
}


def _c(text: str, *codes: str, no_color: bool = False) -> str:
    if no_color or not sys.stdout.isatty():
        return text
    return "".join(ANSI.get(c, "") for c in codes) + text + ANSI["reset"]


def format_terminal(findings: list, no_color: bool = False) -> str:
    if not findings:
        return _c("\u2705  No issues found.", "green", no_color=no_color)

    lines = []
    lines.append(
        _c(
            "\n\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557",
            "bold",
            no_color=no_color,
        )
    )
    lines.append(
        _c(
            "\u2551         Unity Auditor Results            \u2551",
            "bold",
            no_color=no_color,
        )
    )
    lines.append(
        _c(
            "\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d\n",
            "bold",
            no_color=no_color,
        )
    )

    p0 = [f for f in findings if f.severity == SEVERITY_P0]
    p1 = [f for f in findings if f.severity == SEVERITY_P1]
    p2 = [f for f in findings if f.severity == SEVERITY_P2]

    lines.append(
        f"  Total: {len(findings)}  |  "
        f"{_c(f'\u26d4 P0: {len(p0)}', 'red', 'bold', no_color=no_color)}  |  "
        f"{_c(f'\u26a0\ufe0f  P1: {len(p1)}', 'yellow', 'bold', no_color=no_color)}  |  "
        f"{_c(f'\U0001f4a1 P2: {len(p2)}', 'blue', no_color=no_color)}\n"
    )

    current_cat = None
    for f in findings:
        if f.category != current_cat:
            current_cat = f.category
            lines.append(
                _c(
                    f"\n\u2500\u2500 {f.category} \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
                    "bold",
                    no_color=no_color,
                )
            )

        sev_color = {
            "P0_BlockMerge": "red",
            "P1_MustFix": "yellow",
            "P2_Suggestion": "blue",
        }.get(f.severity, "grey")
        sev_label = f"[{f.severity}]"
        emoji = SEVERITY_EMOJI.get(f.severity, "  ")

        lines.append(
            f"  {emoji} {_c(sev_label, sev_color, 'bold', no_color=no_color)} "
            f"{_c(f.rule_id, 'bold', no_color=no_color)} {f.title}"
        )
        lines.append(f"     {_c(f.file_path, 'grey', no_color=no_color)}:{f.line}")
        if f.detail:
            snippet = f.detail[:80] + ("\u2026" if len(f.detail) > 80 else "")
            lines.append(f"     {_c(snippet, 'grey', no_color=no_color)}")
        lines.append(
            f"     \u2192 {_c(f.how_to_fix[:100], 'green', no_color=no_color)}"
        )
        lines.append("")

    return "\n".join(lines)


def format_github_annotations(findings: list) -> str:
    """
    GitHub Actions workflow command format.
    https://docs.github.com/en/actions/using-workflows/workflow-commands-for-github-actions
    """
    lines = []
    for f in findings:
        level = {
            "P0_BlockMerge": "error",
            "P1_MustFix": "warning",
            "P2_Suggestion": "notice",
        }.get(f.severity, "notice")
        file_path = f.file_path.replace("\\", "/")
        title = f"{f.rule_id}: {f.title}"
        msg = f"{f.why} FIX: {f.how_to_fix}"
        line_part = f",line={f.line}" if f.line > 0 else ""
        lines.append(f"::{level} file={file_path}{line_part},title={title}::{msg}")
    return "\n".join(lines)


def format_sarif(findings: list, assets_root: str) -> dict:
    """SARIF v2.1 — compatible with GitHub Code Scanning, VS Code SARIF viewer."""
    rules_map = {}
    results = []

    for f in findings:
        if f.rule_id not in rules_map:
            rules_map[f.rule_id] = {
                "id": f.rule_id,
                "name": f.title.replace(" ", ""),
                "shortDescription": {"text": f.title},
                "fullDescription": {"text": f.why},
                "help": {"text": f.how_to_fix, "markdown": f"**Fix:** {f.how_to_fix}"},
                "defaultConfiguration": {
                    "level": {
                        SEVERITY_P0: "error",
                        SEVERITY_P1: "warning",
                        SEVERITY_P2: "note",
                    }.get(f.severity, "note"),
                },
            }

        uri = f.file_path.replace("\\", "/")
        result = {
            "ruleId": f.rule_id,
            "level": {
                "P0_BlockMerge": "error",
                "P1_MustFix": "warning",
                "P2_Suggestion": "note",
            }.get(f.severity, "note"),
            "message": {"text": f.title},
            "locations": [
                {
                    "physicalLocation": {
                        "artifactLocation": {"uri": uri, "uriBaseId": "SRCROOT"},
                        "region": {"startLine": max(1, f.line)},
                    }
                }
            ],
        }
        results.append(result)

    return {
        "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
        "version": "2.1.0",
        "runs": [
            {
                "tool": {
                    "driver": {
                        "name": "UnityAuditor",
                        "version": "1.0.0",
                        "informationUri": "https://github.com/your-org/your-repo",
                        "rules": list(rules_map.values()),
                    }
                },
                "results": results,
                "originalUriBaseIds": {
                    "SRCROOT": {"uri": Path(assets_root).parent.as_uri() + "/"}
                },
            }
        ],
    }


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Unity Auditor \u2014 headless static analysis",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "assets_root",
        nargs="?",
        default="Assets",
        help="Path to Unity project Assets/ folder (default: ./Assets)",
    )
    parser.add_argument(
        "--github-annotations",
        action="store_true",
        help="Output GitHub Actions workflow commands",
    )
    parser.add_argument(
        "--sarif", metavar="OUTPUT.json", help="Write SARIF v2.1 report to file"
    )
    parser.add_argument(
        "--json", metavar="OUTPUT.json", help="Write JSON report to file"
    )
    parser.add_argument(
        "--no-color", action="store_true", help="Disable ANSI color output"
    )
    parser.add_argument(
        "--fail-on",
        choices=["P0", "P1", "P2", "none"],
        default="P1",
        help="Exit code 1 if findings at or above this severity exist (default: P1)",
    )
    parser.add_argument(
        "--category",
        choices=[
            CATEGORY_CODE,
            CATEGORY_SERIAL,
            CATEGORY_SEC,
            CATEGORY_PERF,
            CATEGORY_PREFAB,
            CATEGORY_ASSET,
            CATEGORY_LIFE,
            CATEGORY_ARCH,
            CATEGORY_OPT,
        ],
        help="Run only a specific category",
    )

    args = parser.parse_args()
    assets_root = os.path.abspath(args.assets_root)

    if not os.path.isdir(assets_root):
        print(f"ERROR: Assets root not found: {assets_root}", file=sys.stderr)
        return 2

    print(f"Scanning: {assets_root}", file=sys.stderr)
    findings = run_all(assets_root)

    if args.category:
        findings = [f for f in findings if f.category == args.category]

    # Terminal output
    if not args.github_annotations:
        print(format_terminal(findings, no_color=args.no_color))

    # GitHub Actions annotations
    if args.github_annotations:
        print(format_github_annotations(findings))

    # SARIF
    if args.sarif:
        sarif = format_sarif(findings, assets_root)
        with open(args.sarif, "w") as fp:
            json.dump(sarif, fp, indent=2)
        print(f"SARIF written to: {args.sarif}", file=sys.stderr)

    # JSON
    if args.json:
        with open(args.json, "w") as fp:
            json.dump([asdict(f) for f in findings], fp, indent=2)
        print(f"JSON written to: {args.json}", file=sys.stderr)

    # Exit code
    fail_levels = {
        "P0": {SEVERITY_P0},
        "P1": {SEVERITY_P0, SEVERITY_P1},
        "P2": {SEVERITY_P0, SEVERITY_P1, SEVERITY_P2},
        "none": set(),
    }
    fail_severities = fail_levels.get(args.fail_on, {SEVERITY_P0, SEVERITY_P1})
    if any(f.severity in fail_severities for f in findings):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

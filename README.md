# Unity Auditor

**Automated static analysis for Unity C# projects — catch security holes, performance pitfalls, and common bugs before they ship.**

Unity Auditor runs the same 37-rule set in two places: a local Unity Editor window for pre-commit review, and a headless Python scanner for CI pipelines. Zero external dependencies. Drop it in and go.

![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%20LTS%2B-black?logo=unity)
![Python 3.9+](https://img.shields.io/badge/Python-3.9%2B-3776AB?logo=python&logoColor=white)
![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)

---

## Features

| Category | Rules | Severity | What it catches |
|----------|:-----:|----------|-----------------|
| **Security** | 6 | P0 | Hardcoded secrets, `Assembly.Load` RCE, plain HTTP, deprecated `WWW` |
| **Code Logic** | 7 | P1 | `Camera.main` in Update, `Find*` at runtime, empty catch blocks |
| **Serialization** | 6 | P1 | `Dictionary` in `[SerializeField]`, `BinaryFormatter`, auto-property traps |
| **Performance** | 7 | P1 | String concat in Update, `new` allocations in loops, `SendMessage` |
| **Prefab Integrity** | 4 | P1 | Missing script references, broken variants, Canvas without raycaster |
| **Asset Settings** | 7 | P2 | Read/Write textures, uncompressed 4K textures, audio decompress bloat |

**Severity scale:**

- **P0 — Block Merge** : Must fix before merge. Security vulnerabilities and RCE vectors.
- **P1 — Must Fix** : Should fix this sprint. Performance and correctness issues.
- **P2 — Suggestion** : Nice to have. Memory and import setting optimizations.

---

## Quick Start

### Editor Tool (Local)

Copy the `Assets/Editor/UnityAuditor/` folder into your Unity project:

```
YourProject/
└── Assets/
    └── Editor/
        └── UnityAuditor/
            ├── AuditEngine.cs
            ├── AuditFinding.cs
            ├── UnityAuditorWindow.cs
            ├── Rules/
            │   ├── CodeLogicRules.cs
            │   ├── SerializationRules.cs
            │   ├── SecurityRules.cs
            │   ├── PerformanceRules.cs
            │   ├── PrefabRules.cs
            │   └── AssetSettingsRules.cs
            └── Scripts/
                └── unity_auditor.py
```

Open the window: **Tools > Unity Auditor**

Click **Run Scan** to analyze your project. The window displays:

- Severity badges with counts (P0 / P1 / P2)
- Category tabs (All, Code Logic, Serialization, Security, Performance, Prefabs, Assets)
- Real-time search and severity filters
- Split detail panel with "Why it matters" and "How to fix" guidance
- One-click jump to the offending file and line
- **Copy Report** button for Markdown-formatted clipboard export

### CI Script (GitHub Actions)

1. Copy `Scripts/unity_auditor.py` into your repo
2. Copy `.github/workflows/unity-auditor.yml` into `.github/workflows/`
3. Configure the environment variables:

```yaml
env:
  ASSETS_ROOT: Assets                     # path to your Assets/ folder
  SCRIPT_PATH: Scripts/unity_auditor.py   # path to the Python script
  FAIL_ON: P1                             # P0 | P1 | P2 | none
```

The workflow triggers on PRs that touch `.cs`, `.prefab`, `.unity`, `.mat`, `.anim`, `.fbx`, or `.meta` files and:

- Posts **inline annotations** on the PR Files tab
- Uploads a **SARIF v2.1** report to GitHub Security / Code Scanning
- Saves a **JSON artifact** for downstream consumers
- **Blocks merges** when findings meet or exceed the configured severity threshold

---

## CLI Usage

```bash
# Terminal report (default)
python Scripts/unity_auditor.py Assets/

# GitHub Actions annotations
python Scripts/unity_auditor.py Assets/ --github-annotations

# All outputs at once
python Scripts/unity_auditor.py Assets/ \
  --github-annotations \
  --sarif report.sarif.json \
  --json  report.json

# Only run a specific category
python Scripts/unity_auditor.py Assets/ --category Security

# Fail only on P0 (block-merge) findings
python Scripts/unity_auditor.py Assets/ --fail-on P0

# Disable ANSI color (piped output)
python Scripts/unity_auditor.py Assets/ --no-color
```

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Clean — no findings above threshold (or only P2 suggestions) |
| `1` | P0/P1 findings found (configurable via `--fail-on`) |
| `2` | `Assets/` folder not found |

### Output Formats

| Flag | Format |
|------|--------|
| _(default)_ | ANSI color terminal report |
| `--github-annotations` | `::error file=…,line=…::` workflow commands |
| `--sarif <path>` | SARIF v2.1 for GitHub Code Scanning |
| `--json <path>` | Raw findings array |

---

## Programmatic API (C#)

Run audits from your own Editor scripts or custom tooling:

```csharp
using UnityAuditor;

// Run all rules
var findings = AuditEngine.RunAll(Application.dataPath, (status, progress) =>
{
    EditorUtility.DisplayProgressBar("Auditing...", status, progress);
});
EditorUtility.ClearProgressBar();

// Run a specific category only
var securityFindings = AuditEngine.RunCategory(
    Application.dataPath, RuleCategory.Security);

// Get summary counts
var bySeverity = AuditEngine.GetSeverityCounts(findings);
var byCategory = AuditEngine.GetCategoryCounts(findings);

Debug.Log($"P0: {bySeverity[Severity.P0_BlockMerge]}, " +
          $"P1: {bySeverity[Severity.P1_MustFix]}, " +
          $"P2: {bySeverity[Severity.P2_Suggestion]}");
```

---

## Rule Reference

### P0 — Block Merge

| Rule | Category | Description |
|------|----------|-------------|
| SEC001 | Security | `PlayerPrefs` stores a secret/password/token key |
| SEC002 | Security | Hard-coded API key or secret string literal |
| SEC003 | Security | `Assembly.Load` — RCE vector |
| SEC004 | Security | `Application.OpenURL` with non-literal argument (open redirect) |
| SEC005 | Security | Deprecated `WWW` class (use `UnityWebRequest`) |
| SEC006 | Security | Plain HTTP endpoint (use HTTPS) |

### P1 — Must Fix

| Rule | Category | Description |
|------|----------|-------------|
| CL001 | Code Logic | `== null` on UnityEngine.Object (use `!obj` pattern) |
| CL002 | Code Logic | `Camera.main` in Update (re-queries every frame) |
| CL003 | Code Logic | `Find*` / `FindObjectOfType` at runtime |
| CL004 | Code Logic | Empty `catch` block swallowing exceptions |
| CL005 | Code Logic | `GetComponent` inside Update |
| CL006 | Code Logic | `StartCoroutine` without null-guard |
| CL007 | Code Logic | `Destroy` without null-guard |
| SR001 | Serialization | Public field without `[SerializeField]` or `[HideInInspector]` |
| SR002 | Serialization | `Dictionary` in `[SerializeField]` (not serializable) |
| SR003 | Serialization | Auto-property on `[Serializable]` class (invisible backing field) |
| SR004 | Serialization | `BinaryFormatter` usage (obsolete, security risk) |
| SR005 | Serialization | `[SerializeReference]` without null-check on read |
| SR006 | Serialization | `OnValidate` modifies data without `Undo.RecordObject` |
| PERF001 | Performance | String concatenation with `+` in Update/FixedUpdate/LateUpdate |
| PERF002 | Performance | `new` allocation (List/Array/object) inside Update |
| PERF003 | Performance | LINQ query inside Update |
| PERF004 | Performance | `SendMessage` / `BroadcastMessage` |
| PERF005 | Performance | Unguarded `Debug.Log` in production |
| PERF006 | Performance | Tag comparison via string instead of `CompareTag` |
| PERF007 | Performance | `RaycastHit` allocated inside a loop |
| PF001 | Prefab | Missing script reference (`fileID: 0`) |
| PF002 | Prefab | Broken prefab variant (base GUID not found) |
| PF003 | Prefab | Canvas without `GraphicRaycaster` |
| PF004 | Prefab | Non-uniform scale on collider GameObject |

### P2 — Suggestions

| Rule | Category | Description |
|------|----------|-------------|
| AS001 | Asset Settings | Texture Read/Write enabled (doubles VRAM) |
| AS002 | Asset Settings | Sprite with mipmaps enabled (wasted memory) |
| AS003 | Asset Settings | Large texture (>2048 px) with no compression |
| AS004 | Asset Settings | Non-power-of-two texture (can't compress on mobile) |
| AS005 | Asset Settings | Mesh Read/Write enabled outside Editor |
| AS006 | Asset Settings | Mesh normals disabled but material uses lighting |
| AS007 | Asset Settings | Large audio clip (>200 KB) set to Decompress On Load |

---

## Architecture

```
UnityAuditor/
├── AuditFinding.cs          # Data model: Finding, Severity, Category, IAuditRule
├── AuditEngine.cs           # Orchestrator: registers rules, runs scans, aggregates
├── UnityAuditorWindow.cs    # IMGUI EditorWindow: UI, filtering, detail panels
├── Rules/
│   ├── SecurityRules.cs     # P0: secrets, RCE, HTTP, deprecated APIs
│   ├── CodeLogicRules.cs    # P1: null checks, Find*, empty catch
│   ├── SerializationRules.cs# P1: Dictionary, BinaryFormatter, auto-props
│   ├── PerformanceRules.cs  # P1: allocations in Update, SendMessage, LINQ
│   ├── PrefabRules.cs       # P1: missing scripts, broken variants
│   └── AssetSettingsRules.cs# P2: texture/mesh/audio import settings
└── Scripts/
    └── unity_auditor.py     # Headless Python scanner (mirrors C# rules)
```

**Dual-execution model:** The C# rules use `IAuditRule` implementations with the Unity `AssetDatabase` API. The Python script mirrors the same patterns using `re` (regex) for zero-dependency CI — no Unity headless instance required.

### Adding a Custom Rule

Implement `IAuditRule` and register it in `AuditEngine.GetAllRules()`:

```csharp
using System.Collections.Generic;

namespace UnityAuditor.Rules
{
    public class MyCustomRules : IAuditRule
    {
        public RuleCategory Category => RuleCategory.CodeLogic;

        public List<AuditFinding> Scan(string assetsRoot)
        {
            var findings = new List<AuditFinding>();
            // Your scanning logic here
            return findings;
        }
    }
}
```

---

## Assembly Definition (Optional)

To isolate Unity Auditor from your game assemblies, add a `.asmdef` file:

```json
{
    "name": "UnityAuditor.Editor",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

---

## Requirements

| Component | Requirement |
|-----------|-------------|
| Unity Editor | 2021.3 LTS or newer |
| Python CI scanner | Python 3.9+ (no external dependencies) |
| GitHub Actions | Any GitHub-hosted runner (`ubuntu-latest` recommended) |
| SARIF upload | GitHub Advanced Security enabled, or public repository |

---

## License

[MIT](LICENSE) — Copyright (c) 2026 Cuozg
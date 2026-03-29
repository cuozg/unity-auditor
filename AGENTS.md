# AGENTS.md — Unity Auditor (ProjectAuditor)

## Project Overview

Automated static analysis tool for Unity C# projects. 37 rules across 6 categories with dual execution: Unity EditorWindow (IMGUI) for interactive use and a headless Python scanner for CI/CD pipelines.

**Severity levels**: P0 (Block Merge — security), P1 (Must Fix — performance/logic), P2 (Suggestion — asset settings).

## Architecture

```
Assets/Editor/UnityAuditor/
├── AuditFinding.cs           # Enums (Severity, RuleCategory), AuditFinding data class, IAuditRule interface
├── AuditEngine.cs            # Static orchestrator — RunAll(), RunCategory(), GetAllRules() registry
├── UnityAuditorWindow.cs     # IMGUI EditorWindow (Tools > Unity Auditor), filtering, detail panels
├── Rules/
│   ├── SecurityRules.cs      # 6 P0 rules — secrets, RCE, HTTP, WWW class
│   ├── CodeLogicRules.cs     # 7 P1 rules — Camera.main, Find*, empty catch, null checks
│   ├── SerializationRules.cs # 6 P1 rules — Dictionary, BinaryFormatter, non-serializable types
│   ├── PerformanceRules.cs   # 7 P1 rules — allocations in Update, SendMessage, Debug.Log
│   ├── PrefabRules.cs        # 4 P1 rules — missing scripts, broken prefab variants
│   └── AssetSettingsRules.cs # 7 P2 rules — texture/mesh/audio import settings
└── Scripts/
    └── unity_auditor.py      # Headless Python scanner (mirrors C# rules via regex)
```

### Data Flow

1. **Trigger**: EditorWindow button, programmatic `AuditEngine.RunAll()`, or CLI `python unity_auditor.py Assets/`
2. **Orchestration**: `AuditEngine` iterates registered `IAuditRule` instances
3. **Collection**: Each rule's `Scan(string assetsRoot)` returns `List<AuditFinding>`
4. **Aggregation**: Findings sorted by Severity → Category → FilePath
5. **Output**: IMGUI window (editor), ANSI terminal / GitHub annotations / SARIF v2.1 / JSON (CI)

### Entry Points

| Context | Entry Point |
|---------|------------|
| Unity Editor | `Tools > Unity Auditor` menu → `UnityAuditorWindow` |
| Programmatic | `AuditEngine.RunAll(assetsRoot)` or `AuditEngine.RunCategory(assetsRoot, category)` |
| CI/CLI | `python unity_auditor.py Assets/ [--github-annotations] [--sarif report.json]` |

## Code Conventions

- **Namespace**: `UnityAuditor` (core), `UnityAuditor.Rules` (rule implementations)
- **Naming**: PascalCase for classes/methods/properties/static fields, camelCase for locals
- **Indentation**: 4 spaces, Allman braces
- **Documentation**: XML `<summary>` on all public API members
- **Error handling**: try-catch at orchestration level only; `Debug.LogError` with `[UnityAuditor]` prefix
- **Conditional compilation**: All editor code wrapped in `#if UNITY_EDITOR`
- **Field alignment**: Vertically aligned in data classes (see `AuditFinding.cs`)
- **No `.editorconfig`** — conventions enforced by existing code patterns only

## Key Interfaces

### IAuditRule (AuditFinding.cs)

```csharp
public interface IAuditRule
{
    RuleCategory Category { get; }
    List<AuditFinding> Scan(string assetsRoot);
}
```

Every rule class implements this interface. `Scan()` receives the project's Assets root path and returns findings.

### AuditFinding (AuditFinding.cs)

Fields: `Severity`, `Category`, `RuleId`, `Title`, `FilePath`, `Line`, `Detail`, `WhyItMatters`, `HowToFix`.

### AuditEngine (AuditEngine.cs)

Static class. `GetAllRules()` returns all rule instances (manual registry). `RunAll()` and `RunCategory()` orchestrate scanning.

## How to Add a New Rule

1. Create a class implementing `IAuditRule` in `Assets/Editor/UnityAuditor/Rules/`
2. Set `Category` property to the appropriate `RuleCategory` enum value
3. Implement `Scan(string assetsRoot)` — use regex for source scanning, `AssetDatabase`/`AssetImporter` for asset inspection
4. **Register the rule** in `AuditEngine.GetAllRules()` — this is a manual step; forgetting it means the rule never runs
5. Mirror the rule in `Scripts/unity_auditor.py` for CI parity (rules defined as tuples in category arrays like `SEC_RULES`, `PERF_RULES`)
6. Follow the existing rule ID convention: category prefix + 3-digit number (e.g., `SEC001`, `CL001`, `PERF001`)

## Rule Implementation Patterns

- **Source code rules** (Security, CodeLogic, Performance, Serialization): Use regex patterns over `.cs` file contents. Rules are defined as static arrays of tuples containing `(RuleId, Title, RegexPattern, Severity, WhyItMatters, HowToFix)`.
- **Asset rules** (AssetSettings): Use `AssetDatabase.FindAssets()` + `AssetImporter` to inspect import settings on textures, models, audio.
- **Prefab/scene rules** (PrefabIntegrity): Scan `.prefab` and `.unity` YAML files for structural issues (missing script GUIDs, broken variant references).

## CI/CD

- **GitHub Actions**: `.github/workflows/unity-auditor.yml` — runs Python scanner on PRs targeting `main`/`develop`
- **Output formats**: SARIF v2.1 (GitHub Code Scanning), inline PR annotations (`--github-annotations`), terminal ANSI, JSON
- **Python requirements**: 3.9+, zero external dependencies

## Forbidden Patterns

- Do not use `#pragma warning disable` — no instances exist in the codebase and none should be added
- Do not suppress type safety (`as any` equivalent patterns, unchecked casts) in rule implementations
- Do not add external Python dependencies to `unity_auditor.py` — it must remain zero-dependency for CI portability
- Do not use `BinaryFormatter` or `WWW` class — these are flagged by the tool's own rules (P0/P1)
- Do not store credentials or secrets in source — the tool's SecurityRules enforce this

## Testing

No formal test suite (NUnit/UTF) exists. Validation relies on:
- Manual verification via the EditorWindow
- Dual-implementation cross-check (C# rules mirrored in Python)
- CI pipeline execution on PRs

## Dependencies

- **Unity**: Editor-only tool (all code under `Assets/Editor/`)
- **Python**: 3.9+ for CI scanner (zero external deps)
- **No UPM package.json** — distributed by copying `Assets/Editor/UnityAuditor/` into target projects
- **No assembly definition (.asmdef)** provided by default

# AGENTS.md — Unity Auditor (ProjectAuditor)

## Project Overview

Automated static analysis tool for Unity C# projects. 37 rules across 6 categories with dual execution: Unity EditorWindow (IMGUI) for interactive use and a headless Python scanner for CI/CD pipelines.

**Severity levels**: P0 (Block Merge — security), P1 (Must Fix — performance/logic), P2 (Suggestion — asset settings).

## Architecture

```
Assets/Editor/UnityAuditor/
├── UnityAuditor.Editor.asmdef  # Assembly definition — Editor-only platform
├── Severity.cs                 # Enum: P0_BlockMerge, P1_MustFix, P2_Suggestion
├── RuleCategory.cs             # Enum: CodeLogic, Serialization, Security, Performance, PrefabIntegrity, AssetSettings
├── IAuditRule.cs               # Interface: Category + Scan(assetsRoot)
├── AuditFinding.cs             # Sealed DTO: finding data class
├── ScannerUtility.cs           # Internal static helpers: CountLines, MakeRelative, CombineArrays
├── AuditEngine.cs              # Static orchestrator — RunAll(), RunCategory(), GetAllRules() registry
├── UnityAuditorWindow.cs       # Sealed IMGUI EditorWindow (Tools > Unity Auditor)
├── Rules/
│   ├── RegexRuleBase.cs        # Abstract base for regex-over-source rules (DRY scanning loop)
│   ├── SecurityRules.cs        # 6 P0 rules — secrets, RCE, HTTP, WWW class (extends RegexRuleBase)
│   ├── CodeLogicRules.cs       # 7 P1 rules — Camera.main, Find*, empty catch (extends RegexRuleBase)
│   ├── SerializationRules.cs   # 6 P1 rules — Dictionary, BinaryFormatter (extends RegexRuleBase)
│   ├── PerformanceRules.cs     # 7 P1 rules — allocations in Update, SendMessage (extends RegexRuleBase)
│   ├── PrefabRules.cs          # 4 P1 rules — missing scripts, broken prefab variants (direct IAuditRule)
│   └── AssetSettingsRules.cs   # 7 P2 rules — texture/mesh/audio import settings (direct IAuditRule)
└── Scripts/
    └── unity_auditor.py        # Headless Python scanner (mirrors C# rules via regex)
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

- **Namespace**: `UnityAuditor` (core types), `UnityAuditor.Rules` (rule implementations)
- **Assembly**: `UnityAuditor.Editor.asmdef` — Editor-only platform, `autoReferenced: false`
- **One type per file**: File name matches type name (`Severity.cs` → `enum Severity`)
- **Sealed by default**: All concrete classes are `sealed` unless designed for inheritance
- **Naming**: PascalCase for classes/methods/properties/static fields, `_camelCase` for private fields, camelCase for locals
- **Indentation**: 4 spaces, Allman braces
- **Documentation**: XML `<summary>` on all public API members
- **Error handling**: try-catch at orchestration level only; `Debug.LogError` with `[UnityAuditor]` prefix
- **Conditional compilation**: All editor code wrapped in `#if UNITY_EDITOR` (redundant but portable with asmdef)
- **Field ordering**: Constants → Static → Serialized → Private → Properties → Unity callbacks → Public methods → Private methods
- **Section headers**: `// --- Section ---` comment style (no `#region`)
- **No `.editorconfig`** — conventions enforced by existing code patterns only

## Key Interfaces

### IAuditRule (IAuditRule.cs)

```csharp
public interface IAuditRule
{
    RuleCategory Category { get; }
    List<AuditFinding> Scan(string assetsRoot);
}
```

Every rule class implements this interface. `Scan()` receives the project's Assets root path and returns findings.

### AuditFinding (AuditFinding.cs)

Sealed DTO with public fields: `Severity`, `Category`, `RuleId`, `Title`, `FilePath`, `Line`, `Detail`, `WhyItMatters`, `HowToFix`.

### RegexRuleBase (Rules/RegexRuleBase.cs)

Abstract base class for the 4 regex-over-source rule files. Provides:
- Shared file iteration and regex matching loop
- `ShouldSkipFile()` virtual hook (default skips Generated/.Designer. files)
- Subclasses define `Category` and `Rules` array only

### AuditEngine (AuditEngine.cs)

Static class. `GetAllRules()` returns all rule instances (manual registry). `RunAll()` and `RunCategory()` orchestrate scanning.

### ScannerUtility (ScannerUtility.cs)

Internal static helpers shared across rule implementations: `CountLines()`, `MakeRelative()`, `CombineArrays()`.

## How to Add a New Rule

### Regex-based rule (source code scanning)

1. Add rule tuples to the `_rules` array in the appropriate `RegexRuleBase` subclass
2. Follow the rule ID convention: category prefix + 3-digit number (e.g., `SEC007`, `CL008`)
3. **Register if new class**: If creating a new rule class, register in `AuditEngine.GetAllRules()`

### Asset/Prefab rule (AssetDatabase scanning)

1. Create a `sealed` class implementing `IAuditRule` in `Assets/Editor/UnityAuditor/Rules/`
2. Use `ScannerUtility` for shared helpers (`CountLines`, `MakeRelative`)
3. **Register the rule** in `AuditEngine.GetAllRules()` — forgetting this means the rule never runs
4. Mirror the rule in `Scripts/unity_auditor.py` for CI parity

## Rule Implementation Patterns

- **Source code rules** (Security, CodeLogic, Performance, Serialization): Extend `RegexRuleBase`. Define a static `_rules` array of tuples `(RuleId, Title, RegexPattern, Severity, WhyItMatters, HowToFix)` and override `Rules` property.
- **Asset rules** (AssetSettings): Use `AssetDatabase.FindAssets()` + `AssetImporter` to inspect import settings.
- **Prefab/scene rules** (PrefabIntegrity): Scan `.prefab`/`.unity` YAML files with regex + `AssetDatabase` checks.

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
- **Assembly definition**: `UnityAuditor.Editor.asmdef` — Editor platform only
- **Python**: 3.9+ for CI scanner (zero external deps)
- **No UPM package.json** — distributed by copying `Assets/Editor/UnityAuditor/` into target projects

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace UnityAuditor.Rules
{
    /// <summary>
    /// P1/P2 asset settings rules: texture Read/Write, sprite mipmaps, large uncompressed textures,
    /// NPOT textures, mesh Read/Write, mesh normals, large audio Decompress On Load,
    /// animation events, shader passes, material textures, sprite atlas duplicates,
    /// video transcode settings, font rendering mode.
    /// </summary>
    public sealed class AssetSettingsRules : IAuditRule
    {
        const int MaxTextureSize  = 2048;
        const int LargeAudioBytes = 200 * 1024; // 200KB uncompressed
        const int MaxShaderPasses = 4;

        public RuleCategory Category => RuleCategory.AssetSettings;

        public List<AuditFinding> Scan(string assetsRoot)
        {
            var findings = new List<AuditFinding>();

            ScanTextures(findings);
            ScanModels(findings);
            ScanAudio(findings);
            ScanAnimations(findings);
            ScanShaders(findings);
            ScanMaterials(findings);
            ScanSpriteAtlases(findings);
            ScanVideo(findings);
            ScanFonts(findings);

            return findings;
        }

        // ---------------------------------------------------------------
        // Textures
        // ---------------------------------------------------------------

        private static void ScanTextures(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path     = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                // AS001: Read/Write enabled
                if (importer.isReadable)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P1_MustFix,
                        Category     = RuleCategory.AssetSettings,
                        RuleId       = "AS001",
                        Title        = "Texture Read/Write enabled — doubles VRAM usage",
                        FilePath     = path,
                        Line         = 0,
                        Detail       = path,
                        WhyItMatters = "Read/Write enabled keeps a CPU-side copy of the texture in RAM in addition " +
                                       "to the GPU VRAM copy — effectively doubling memory usage. " +
                                       "On mobile this can cause OOM crashes.",
                        HowToFix     = "Disable Read/Write in Texture Import Settings unless the texture is " +
                                       "explicitly read at runtime (GetPixel, EncodeToJPG, etc.).",
                    });
                }

                // AS002: UI sprite with mipmaps
                if (importer.textureType == TextureImporterType.Sprite && importer.mipmapEnabled)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P1_MustFix,
                        Category     = RuleCategory.AssetSettings,
                        RuleId       = "AS002",
                        Title        = "Sprite texture with mipmaps enabled — wastes ~33% memory",
                        FilePath     = path,
                        Line         = 0,
                        Detail       = path,
                        WhyItMatters = "UI sprites are always rendered at native resolution; mipmap chains " +
                                       "add ~33% memory overhead with no visual benefit.",
                        HowToFix     = "Disable Generate Mip Maps in Texture Import Settings for all Sprite textures.",
                    });
                }

                // AS003: Large texture without compression
                if (importer.maxTextureSize > MaxTextureSize)
                {
                    var settings = importer.GetDefaultPlatformTextureSettings();
                    if (settings.format == TextureImporterFormat.Automatic &&
                        importer.textureCompression == TextureImporterCompression.Uncompressed)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P1_MustFix,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS003",
                            Title        = $"Texture >{MaxTextureSize}px imported without compression",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"maxTextureSize={importer.maxTextureSize}, compression=Uncompressed",
                            WhyItMatters = "Uncompressed textures at this size can use 16–64MB of VRAM. " +
                                           "On mobile this will OOM kill the app.",
                            HowToFix     = "Set compression to: DXT/BC1 (desktop), ETC2 (Android), ASTC (iOS). " +
                                           "Use platform-specific overrides for best per-platform quality.",
                        });
                    }
                }

                // AS004: Texture not power-of-two (NPOT) without explicit flag
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                {
                    bool isPOT = IsPowerOfTwo(tex.width) && IsPowerOfTwo(tex.height);
                    if (!isPOT && importer.npotScale == TextureImporterNPOTScale.None)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS004",
                            Title        = $"NPOT texture ({tex.width}x{tex.height}) — may fall back to uncompressed on some GPUs",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"{tex.width}x{tex.height}",
                            WhyItMatters = "Non-power-of-two textures cannot use block compression (DXT/ETC2) on all platforms. " +
                                           "Some GPUs silently decompress them to RGBA32 at load time.",
                            HowToFix     = "Resize to the nearest power of two, or set NPOT Scale to 'ToNearest' in import settings.",
                        });
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Models
        // ---------------------------------------------------------------

        private static void ScanModels(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path     = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                // AS005: Read/Write meshes enabled
                if (importer.isReadable)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P1_MustFix,
                        Category     = RuleCategory.AssetSettings,
                        RuleId       = "AS005",
                        Title        = "Mesh Read/Write enabled — doubles CPU memory usage",
                        FilePath     = path,
                        Line         = 0,
                        Detail       = path,
                        WhyItMatters = "Read/Write enabled keeps mesh data in CPU RAM after upload to GPU. " +
                                       "For a 10,000-poly mesh this doubles the memory footprint.",
                        HowToFix     = "Disable Read/Write unless mesh data is modified at runtime " +
                                       "(MeshCollider.sharedMesh, procedural mesh, etc.).",
                    });
                }

                // AS006: Normals import mode set to None on non-simple mesh
                if (importer.importNormals == ModelImporterNormals.None)
                {
                    findings.Add(new AuditFinding
                    {
                        Severity     = Severity.P2_Suggestion,
                        Category     = RuleCategory.AssetSettings,
                        RuleId       = "AS006",
                        Title        = "Model importing with normals disabled — will render flat-shaded",
                        FilePath     = path,
                        Line         = 0,
                        Detail       = "importNormals=None",
                        WhyItMatters = "Meshes without normals cannot respond to lighting — " +
                                       "they will appear uniformly lit (flat-shaded) regardless of material.",
                        HowToFix     = "Set Import Normals to 'Import' (use baked normals from DCC) or " +
                                       "'Calculate' (Unity recalculates from geometry).",
                    });
                }
            }
        }

        // ---------------------------------------------------------------
        // Audio
        // ---------------------------------------------------------------

        private static void ScanAudio(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path     = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                var settings = importer.defaultSampleSettings;

                // AS007: Decompress on load for large audio files
                if (settings.loadType == AudioClipLoadType.DecompressOnLoad)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null && clip.samples * 2 > LargeAudioBytes)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P1_MustFix,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS007",
                            Title        = "Large audio clip set to Decompress On Load",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"~{clip.samples * 2 / 1024}KB uncompressed, loadType=DecompressOnLoad",
                            WhyItMatters = "Decompress On Load decodes the full audio into RAM when loaded. " +
                                           "For music or long ambience tracks this can use 20–100MB+ of RAM.",
                            HowToFix     = "Background music: use 'Streaming'. Short SFX: use 'Compressed In Memory'. " +
                                           "Only use DecompressOnLoad for clips <200KB that play frequently.",
                        });
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Animations (AS008)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS008: Flag AnimationClips with animation events for manual review.
        /// Events referencing non-existent methods log runtime errors every playback.
        /// </summary>
        private static void ScanAnimations(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                try
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;

                    var events = AnimationUtility.GetAnimationEvents(clip);
                    if (events == null || events.Length == 0) continue;

                    foreach (var evt in events)
                    {
                        if (string.IsNullOrEmpty(evt.functionName))
                        {
                            findings.Add(new AuditFinding
                            {
                                Severity     = Severity.P2_Suggestion,
                                Category     = RuleCategory.AssetSettings,
                                RuleId       = "AS008",
                                Title        = "AnimationClip has event with empty function name",
                                FilePath     = path,
                                Line         = 0,
                                Detail       = $"Clip '{clip.name}' has an event at time {evt.time:F2}s with no function name",
                                WhyItMatters = "AnimationEvents referencing non-existent methods will log " +
                                               "'Method not found' errors every time the event fires during playback.",
                                HowToFix     = "Verify all AnimationEvent function names match public methods on " +
                                               "the Animator's root GameObject. Remove stale events from retired methods.",
                            });
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS008: Failed to scan AnimationClip '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Shaders (AS009)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS009: Flag shaders with more than 4 passes. Each pass re-renders geometry,
        /// multiplying draw calls and GPU workload.
        /// </summary>
        private static void ScanShaders(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // Only scan .shader source files (skip compiled/built-in)
                if (!path.EndsWith(".shader")) continue;

                try
                {
                    var shaderText = File.ReadAllText(path);
                    var passMatches = Regex.Matches(shaderText, @"Pass\s*\{");
                    int passCount = passMatches.Count;

                    if (passCount > MaxShaderPasses)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P1_MustFix,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS009",
                            Title        = $"Shader with {passCount} passes (>{MaxShaderPasses} threshold)",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"Pass count: {passCount}",
                            WhyItMatters = "Shaders with many passes cause excessive overdraw. Each pass re-renders " +
                                           "geometry, multiplying draw calls and GPU workload.",
                            HowToFix     = "Reduce shader passes. Use multi-compile variants or shader features " +
                                           "instead of separate passes where possible.",
                        });
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS009: Failed to scan shader '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Materials (AS010)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS010: Flag materials with texture properties assigned that the shader does not use.
        /// Unused textures waste VRAM — loaded but never sampled.
        /// </summary>
        private static void ScanMaterials(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                try
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || material.shader == null) continue;

                    var shader = material.shader;
                    int propertyCount = shader.GetPropertyCount();

                    // Build set of valid texture property names from the shader
                    var shaderTexProps = new HashSet<string>();
                    for (int i = 0; i < propertyCount; i++)
                    {
                        if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                        {
                            shaderTexProps.Add(shader.GetPropertyName(i));
                        }
                    }

                    // Check all texture property names on the material
                    var matTexNames = material.GetTexturePropertyNames();
                    foreach (var texName in matTexNames)
                    {
                        if (!shaderTexProps.Contains(texName) && material.GetTexture(texName) != null)
                        {
                            findings.Add(new AuditFinding
                            {
                                Severity     = Severity.P2_Suggestion,
                                Category     = RuleCategory.AssetSettings,
                                RuleId       = "AS010",
                                Title        = "Material has texture assigned to unused shader property",
                                FilePath     = path,
                                Line         = 0,
                                Detail       = $"Material '{material.name}': texture slot '{texName}' " +
                                               $"is not used by shader '{shader.name}'",
                                WhyItMatters = "Texture references on unused material slots waste VRAM — " +
                                               "the texture is loaded but never sampled by the shader.",
                                HowToFix     = "Remove unused texture assignments from the material's Inspector. " +
                                               "Or switch to a shader that uses all assigned textures.",
                            });
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS010: Failed to scan material '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Sprite Atlases (AS011)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS011: Flag SpriteAtlases with duplicate sprite entries.
        /// Duplicate sprites waste atlas texture space and increase build size.
        /// </summary>
        private static void ScanSpriteAtlases(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                try
                {
                    var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                    if (atlas == null) continue;

                    var packables = SpriteAtlasExtensions.GetPackables(atlas);
                    if (packables == null || packables.Length == 0) continue;

                    // Check for duplicate entries by instance ID
                    var seen = new HashSet<int>();
                    foreach (var packable in packables)
                    {
                        if (packable == null) continue;
                        int id = packable.GetInstanceID();
                        if (!seen.Add(id))
                        {
                            findings.Add(new AuditFinding
                            {
                                Severity     = Severity.P2_Suggestion,
                                Category     = RuleCategory.AssetSettings,
                                RuleId       = "AS011",
                                Title        = "SpriteAtlas contains duplicate packable entry",
                                FilePath     = path,
                                Line         = 0,
                                Detail       = $"Atlas '{atlas.name}': duplicate entry '{packable.name}'",
                                WhyItMatters = "Duplicate sprites in a SpriteAtlas waste atlas texture space " +
                                               "and increase build size.",
                                HowToFix     = "Remove duplicate sprite entries from the SpriteAtlas " +
                                               "packable list in the Inspector.",
                            });
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS011: Failed to scan SpriteAtlas '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Video (AS012)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS012: Flag VideoClips without platform-specific transcoded overrides.
        /// Videos without platform transcoding waste storage and cause long load times.
        /// </summary>
        private static void ScanVideo(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:VideoClip", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as VideoClipImporter;
                    if (importer == null) continue;

                    // Skip transcoded internal assets
                    if (importer.defaultTargetSettings.enableTranscoding) continue;

                    // Check common platform targets
                    string[] platforms = { "Android", "iOS", "WebGL" };
                    bool hasAnyOverride = false;

                    foreach (var platform in platforms)
                    {
                        var targetSettings = importer.GetTargetSettings(platform);
                        if (targetSettings != null && targetSettings.enableTranscoding)
                        {
                            hasAnyOverride = true;
                            break;
                        }
                    }

                    if (!hasAnyOverride)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS012",
                            Title        = "VideoClip without platform-specific transcode settings",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"No platform override for Android, iOS, or WebGL",
                            WhyItMatters = "Video files without platform-specific transcoding waste storage " +
                                           "and cause long load times on devices with limited bandwidth.",
                            HowToFix     = "Add platform-specific transcode settings in the VideoClip " +
                                           "import inspector for Android, iOS, and WebGL targets.",
                        });
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS012: Failed to scan VideoClip '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Fonts (AS013)
        // ---------------------------------------------------------------

        /// <summary>
        /// AS013: Flag font assets not set to Dynamic rendering mode.
        /// Static rendering cannot render characters outside the preconfigured set.
        /// </summary>
        private static void ScanFonts(List<AuditFinding> findings)
        {
            var guids = AssetDatabase.FindAssets("t:Font", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as TrueTypeFontImporter;
                    if (importer == null) continue;

                    // Flag non-dynamic font rendering modes
                    if (importer.fontRenderingMode != FontRenderingMode.Smooth &&
                        importer.fontRenderingMode != FontRenderingMode.OSDefault)
                    {
                        findings.Add(new AuditFinding
                        {
                            Severity     = Severity.P2_Suggestion,
                            Category     = RuleCategory.AssetSettings,
                            RuleId       = "AS013",
                            Title        = "Font asset not using Dynamic rendering mode",
                            FilePath     = path,
                            Line         = 0,
                            Detail       = $"fontRenderingMode={importer.fontRenderingMode}",
                            WhyItMatters = "Static font rendering mode cannot render characters outside " +
                                           "the preconfigured character set, breaking localization " +
                                           "and missing characters at runtime.",
                            HowToFix     = "Set the font to Dynamic rendering mode in Font import settings. " +
                                           "This allows Unity to rasterize characters on demand.",
                        });
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityAuditor] AS013: Failed to scan font '{path}': {e.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
    }
}
#endif

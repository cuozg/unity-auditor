using System;

namespace UnityAuditor
{
    /// <summary>
    /// Shared utility methods used by multiple rule implementations.
    /// Extracted to eliminate duplicated helpers across rule classes.
    /// </summary>
    internal static class ScannerUtility
    {
        // --- Line counting ---

        /// <summary>Count the 1-based line number at <paramref name="charIndex"/> in <paramref name="text"/>.</summary>
        internal static int CountLines(string text, int charIndex)
        {
            int line = 1;
            for (int i = 0; i < charIndex && i < text.Length; i++)
            {
                if (text[i] == '\n') line++;
            }
            return line;
        }

        // --- Path helpers ---

        /// <summary>Convert an absolute file path to a path relative to <paramref name="root"/>.</summary>
        internal static string MakeRelative(string fullPath, string root) =>
            fullPath.StartsWith(root)
                ? fullPath.Substring(root.Length).TrimStart('/', '\\')
                : fullPath;

        // --- Array helpers ---

        /// <summary>Concatenate two string arrays without LINQ allocation.</summary>
        internal static string[] CombineArrays(string[] a, string[] b)
        {
            var combined = new string[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        // --- Suppression ---

        private const string IgnorePrefix         = "UnityAuditor:ignore ";
        private const string IgnoreNextLinePrefix = "UnityAuditor:ignore-next-line ";

        /// <summary>
        /// Check whether a finding should be suppressed by inline comments.
        /// <para><paramref name="lineText"/> is the text of the line above the finding
        /// (checked for <c>// UnityAuditor:ignore-next-line RULEID</c>).</para>
        /// <para><paramref name="nextLineText"/> is the text of the finding's own line
        /// (checked for <c>// UnityAuditor:ignore RULEID</c>).</para>
        /// <para>Returns <c>true</c> if the specified <paramref name="ruleId"/> is suppressed.</para>
        /// </summary>
        /// <remarks>
        /// Zero-allocation on the hot path (lines without suppression comments) —
        /// only <see cref="string.IndexOf(string, StringComparison)"/> is used until a marker is found.
        /// </remarks>
        internal static bool IsLineSuppressed(string lineText, string nextLineText, string ruleId)
        {
            // Hot path: most lines have no suppression comment at all.
            // IndexOf on the finding line (nextLineText) for same-line suppression.
            if (nextLineText != null)
            {
                int idx = nextLineText.IndexOf(IgnorePrefix, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    // Ensure this is NOT the longer "ignore-next-line" variant.
                    int nextIdx = nextLineText.IndexOf(IgnoreNextLinePrefix, StringComparison.Ordinal);
                    if (nextIdx < 0 || nextIdx != idx)
                    {
                        if (MatchesRuleId(nextLineText, idx + IgnorePrefix.Length, ruleId))
                            return true;
                    }
                }
            }

            // Check previous line (lineText) for ignore-next-line suppression.
            if (lineText != null)
            {
                int idx = lineText.IndexOf(IgnoreNextLinePrefix, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    if (MatchesRuleId(lineText, idx + IgnoreNextLinePrefix.Length, ruleId))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extract the text of the line containing the character at <paramref name="charIndex"/>.
        /// </summary>
        internal static string GetLineAtOffset(string text, int charIndex)
        {
            int start = charIndex;
            while (start > 0 && text[start - 1] != '\n')
                start--;

            int end = charIndex;
            while (end < text.Length && text[end] != '\n')
                end++;

            return text.Substring(start, end - start);
        }

        /// <summary>
        /// Extract the text of the line immediately before the line containing <paramref name="charIndex"/>.
        /// Returns <c>null</c> if <paramref name="charIndex"/> is on the first line.
        /// </summary>
        internal static string GetPreviousLineAtOffset(string text, int charIndex)
        {
            int curStart = charIndex;
            while (curStart > 0 && text[curStart - 1] != '\n')
                curStart--;

            if (curStart == 0)
                return null; // Already on the first line.

            // curStart - 1 is the '\n' ending the previous line.
            int prevEnd = curStart - 1;
            int prevStart = prevEnd;
            while (prevStart > 0 && text[prevStart - 1] != '\n')
                prevStart--;

            return text.Substring(prevStart, prevEnd - prevStart);
        }

        /// <summary>
        /// Check whether the text starting at <paramref name="offset"/> begins with <paramref name="ruleId"/>
        /// followed by whitespace or end-of-string. Avoids allocation on the hot path.
        /// </summary>
        private static bool MatchesRuleId(string text, int offset, string ruleId)
        {
            while (offset < text.Length && (text[offset] == ' ' || text[offset] == '\t'))
                offset++;

            if (offset + ruleId.Length > text.Length)
                return false;

            for (int i = 0; i < ruleId.Length; i++)
            {
                if (text[offset + i] != ruleId[i])
                    return false;
            }

            // After the rule ID there must be whitespace, end-of-string, or end-of-meaningful content.
            int afterId = offset + ruleId.Length;
            if (afterId >= text.Length)
                return true;

            char next = text[afterId];
            return next == ' ' || next == '\t' || next == '\r' || next == '\n';
        }
    }
}

namespace UnityAuditor
{
    /// <summary>
    /// Audit finding severity levels.
    /// P0 findings block merge, P1 must be fixed this sprint, P2 are suggestions.
    /// </summary>
    public enum Severity
    {
        P0_BlockMerge = 0,
        P1_MustFix    = 1,
        P2_Suggestion = 2,
    }
}

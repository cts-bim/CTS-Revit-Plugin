namespace CTSRevitPlugin
{
    /// <summary>
    /// Which Revit release this DLL was compiled for.
    ///
    /// One build is produced per Revit version (see CTSRevitPlugin.csproj), so the
    /// answer is known at compile time and needs no Revit object to obtain. Used to
    /// keep per-version settings apart on disk.
    /// </summary>
    internal static class CtsRevitVersion
    {
        public static string Current
        {
            get
            {
#if REVIT2026
                return "2026";
#elif REVIT2025
                return "2025";
#elif REVIT2024
                return "2024";
#elif REVIT2023
                return "2023";
#else
                return "Unknown";
#endif
            }
        }
    }
}

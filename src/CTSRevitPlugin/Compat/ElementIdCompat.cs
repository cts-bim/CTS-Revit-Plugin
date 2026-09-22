using Autodesk.Revit.DB;

// Deliberately declared in the ROOT namespace: every file of the plugin lives in
// CTSRevitPlugin.* so these extension methods are found without extra "using" lines.
namespace CTSRevitPlugin
{
    /// <summary>
    /// ElementId differences between Revit versions.
    ///   Revit 2023        : ElementId.IntegerValue (int), new ElementId(int)
    ///   Revit 2024 - 2026 : ElementId.Value (long),       new ElementId(long)
    ///   (IntegerValue and the int constructor are REMOVED in Revit 2026.)
    /// Always go through these helpers instead of touching .Value / .IntegerValue directly.
    /// </summary>
    internal static class ElementIdCompat
    {
        /// <summary>Numeric value of the id (same as ElementId.Value on 2024+).</summary>
        public static long IdValue(this ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        /// <summary>Creates an ElementId from a number, on any supported Revit version.</summary>
        public static ElementId ToElementId(this long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId(checked((int)value));
#endif
        }
    }
}

using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CTSRevitPlugin.UI.Utilities
{
    public static class RevitThemeService
    {
        private static readonly List<WeakReference> _roots = new List<WeakReference>();

        public static void Register(FrameworkElement root)
        {
            if (root == null) return;
            _roots.RemoveAll(x => !x.IsAlive);
            if (!_roots.Any(x => ReferenceEquals(x.Target, root)))
                _roots.Add(new WeakReference(root));
            Apply(root);
        }

        public static void ApplyToAll()
        {
            _roots.RemoveAll(x => !x.IsAlive);
            foreach (WeakReference reference in _roots.ToArray())
            {
                FrameworkElement root = reference.Target as FrameworkElement;
                if (root == null) continue;
                try { root.Dispatcher.BeginInvoke(new Action(() => Apply(root))); }
                catch { }
            }
        }

        public static void Apply(FrameworkElement root)
        {
            if (root == null) return;
            try
            {
                ResourceDictionary theme = LoadTheme();

                // Add the new dictionary BEFORE removing the old ones.
                //
                // Clear-then-add leaves a window in which the element has no theme
                // resources at all. Anything that runs on the dispatcher in
                // between - a pane rebuilding its controls because Revit just
                // re-parented it during an undock - calls FindResource with
                // nothing to find. The ResourceReferenceKeyNotFoundException then
                // escapes a WPF handler, and an unhandled managed exception inside
                // Revit does not surface as an error: it terminates the process.
                List<ResourceDictionary> previous =
                    root.Resources.MergedDictionaries.ToList();

                root.Resources.MergedDictionaries.Add(theme);

                foreach (ResourceDictionary old in previous)
                    root.Resources.MergedDictionaries.Remove(old);

                Brush background = root.TryFindResource("WindowBackgroundBrush") as Brush;
                Brush foreground = root.TryFindResource("TextBrush") as Brush;

                Window window = root as Window;
                if (window != null)
                {
                    window.Background = background;
                    window.Foreground = foreground;
                }
                else
                {
                    Page page = root as Page;
                    if (page != null)
                    {
                        page.Background = background;
                        page.Foreground = foreground;
                    }
                }
            }
            catch { }
        }

        // ============================================================
        // SAFE LOOKUPS
        //
        // Panes that build their controls in code look resources up at runtime,
        // from WPF handlers. FindResource throws when a key is missing, and an
        // exception escaping a WPF handler inside Revit kills the process rather
        // than being reported. TryFindResource returns null instead, so every
        // lookup on that path goes through here and falls back to a literal.
        // ============================================================

        public static Brush ThemeBrush(FrameworkElement root, string key)
        {
            try
            {
                Brush brush = root == null ? null : root.TryFindResource(key) as Brush;
                if (brush != null) return brush;
            }
            catch { }

            return Fallback(key);
        }

        public static Style StyleOrNull(FrameworkElement root, string key)
        {
            try { return root == null ? null : root.TryFindResource(key) as Style; }
            catch { return null; }
        }

        /// <summary>
        /// Literal copies of the dark theme values, used only for the fraction of
        /// a second in which the merged dictionary is mid-swap or the pane is
        /// detached from its host window. The pane repaints correctly on its next
        /// rebuild, so these never need to match the light theme too.
        /// </summary>
        private static Brush Fallback(string key)
        {
            switch (key)
            {
                case "WindowBackgroundBrush": return Frozen(0x22, 0x2A, 0x35);
                case "SurfaceBrush":          return Frozen(0x29, 0x33, 0x42);
                case "SurfaceAltBrush":       return Frozen(0x30, 0x3B, 0x4A);
                case "BorderBrush":           return Frozen(0x3F, 0x4B, 0x5A);
                case "PrimaryBrush":          return Frozen(0xFF, 0x5A, 0x2A);
                case "PrimaryHoverBrush":     return Frozen(0xFF, 0x6A, 0x3D);
                case "TextBrush":             return Frozen(0xF2, 0xF4, 0xF7);
                case "TextSecondaryBrush":    return Frozen(0xB7, 0xC0, 0xCC);
                case "TextMutedBrush":        return Frozen(0x87, 0x93, 0xA2);
                case "InputBrush":            return Frozen(0x20, 0x28, 0x33);
                case "InputBorderBrush":      return Frozen(0x46, 0x53, 0x63);
                case "StatusBrush":           return Frozen(0x1C, 0x23, 0x2D);
                case "HoverBrush":            return Frozen(0x34, 0x41, 0x52);
                case "PressedBrush":          return Frozen(0x3B, 0x49, 0x5A);
                case "AccentTextBrush":       return Frozen(0xFF, 0xFF, 0xFF);
                default:                      return Frozen(0x80, 0x80, 0x80);
            }
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static ResourceDictionary LoadTheme()
        {
            string name = IsDark ? "DarkTheme.xaml" : "LightTheme.xaml";
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            Uri uri = new Uri("/" + assemblyName + ";component/UI/Utilities/Themes/" + name, UriKind.Relative);
            return new ResourceDictionary { Source = uri };
        }

        public static bool IsDark
        {
            get
            {
#if REVIT2024_OR_GREATER
                // The UI theme API (UIThemeManager / dark theme) was introduced in Revit 2024.
                return UIThemeManager.CurrentTheme == UITheme.Dark;
#else
                // Revit 2023 has no dark UI theme: always use the light resources.
                return false;
#endif
            }
        }
    }
}

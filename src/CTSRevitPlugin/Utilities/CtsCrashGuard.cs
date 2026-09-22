using System;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows.Threading;

namespace CTSRevitPlugin.Utilities
{
    /// <summary>
    /// Catches and records the exceptions that would otherwise kill Revit.
    ///
    /// Revit runs add-in UI on its own WPF dispatcher. An exception that escapes
    /// a WPF handler there is never shown to anyone: the CLR tears the process
    /// down and the journal records nothing but
    /// "ExceptionCode=0xe0434352" - the generic managed-exception code, with no
    /// type, no message and no stack. That is a dead end for debugging.
    ///
    /// This guard hooks the dispatcher before any pane exists and writes the full
    /// exception - type, message, stack, inner exceptions - to
    ///   %LOCALAPPDATA%\CTSRevitPlugin\crash.log
    /// When the stack has CTS frames in it, the exception is also marked handled,
    /// so a bug in our UI degrades into a log line instead of taking the user's
    /// Revit session with it. Anything without CTS frames is logged and left
    /// alone: swallowing another add-in's or Revit's own exception would hide
    /// real problems.
    /// </summary>
    internal static class CtsCrashGuard
    {
        private const string Marker = "CTSRevitPlugin";
        private const long MaxLogBytes = 2 * 1024 * 1024;

        private static readonly object _gate = new object();

        private static bool _installed;
        private static int _dockingBugCount;

        [ThreadStatic]
        private static bool _reentering;

        public static string LogPath { get; private set; }

        // ============================================================
        // INSTALL
        // ============================================================

        /// <summary>
        /// Call from OnStartup, on Revit's UI thread, before anything else.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CTSRevitPlugin");

                Directory.CreateDirectory(dir);
                LogPath = Path.Combine(dir, "crash.log");
            }
            catch { LogPath = null; }

            try { Dispatcher.CurrentDispatcher.UnhandledException += OnDispatcherUnhandled; }
            catch { }

            try { AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled; }
            catch { }

            // First-chance fires at the throw site, before anything can swallow
            // or rewrap the exception. Filtered to our own frames so the log does
            // not fill with Revit's routine internal exceptions.
            try { AppDomain.CurrentDomain.FirstChanceException += OnFirstChance; }
            catch { }

            Write("START", "Crash guard installed.", null);
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            _installed = false;

            try { Dispatcher.CurrentDispatcher.UnhandledException -= OnDispatcherUnhandled; }
            catch { }

            try { AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandled; }
            catch { }

            try { AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance; }
            catch { }
        }

        // ============================================================
        // HOOKS
        // ============================================================

        private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e == null || e.Exception == null) return;

            if (IsDockingLayoutBug(e.Exception))
            {
                _dockingBugCount++;

                // Logged the first few times, then only counted: OnMouseEnter can
                // fire repeatedly and there is nothing new to learn after the
                // first stack.
                if (_dockingBugCount <= 3)
                    Write("AVALONDOCK (handled - Revit UI framework defect)",
                          "Occurrence #" + _dockingBugCount + ". See IsDockingLayoutBug for why this is swallowed.",
                          e.Exception);

                e.Handled = true;
                return;
            }

            bool ours = IsOurs(e.Exception);

            Write(
                ours ? "DISPATCHER (handled - CTS frames present)" : "DISPATCHER (not ours - left alone)",
                "An exception reached Revit's WPF dispatcher.",
                e.Exception);

            // Marking it handled is what keeps Revit alive. Only done when the
            // stack shows the fault is ours to own.
            if (ours) e.Handled = true;
        }

        private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            if (e == null) return;

            Write(
                e.IsTerminating ? "APPDOMAIN (terminating)" : "APPDOMAIN",
                "An exception escaped every handler.",
                e.ExceptionObject as Exception);
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            if (e == null || e.Exception == null) return;

            // The logger itself allocates and touches the file system, either of
            // which can throw and re-enter this handler. One flag per thread ends
            // that loop.
            if (_reentering) return;

            _reentering = true;
            try
            {
                if (IsOurs(e.Exception))
                    Write("FIRST-CHANCE", "Thrown inside CTS code (may be handled downstream).", e.Exception);
            }
            catch { }
            finally { _reentering = false; }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>
        /// Recognises a defect in AvalonDock, the docking library behind Revit's
        /// dockable panes.
        ///
        /// AvalonDock reorders tabs as the mouse moves across them. When a pane
        /// is floated, closed or re-docked, the tab collection changes underneath
        /// that handler, and the mouse-enter that follows calls MoveChild with an
        /// index the collection no longer has:
        ///
        ///   System.ArgumentOutOfRangeException: Index was out of range.
        ///     at System.Collections.ObjectModel.ObservableCollection`1.MoveItem
        ///     at Xceed.Wpf.AvalonDock.Layout.LayoutGroup`1.MoveChild
        ///     at Xceed.Wpf.AvalonDock.Controls.LayoutAnchorableTabItem.OnMouseEnter
        ///     ... MouseDevice.ChangeMouseOver ... HwndSource.InputFilterMessage
        ///
        /// There is not a single CTS frame on that stack: the fault is in Revit's
        /// own UI framework, and it is reached by double-clicking any docked pane
        /// that shares a tab group. It is reproducible with several panes docked
        /// together and there is nothing an add-in can do to prevent it.
        ///
        /// The only work the failed call would have done is reorder a tab under
        /// the cursor, so letting it through costs a cosmetic tab move and saves
        /// the user's Revit session. The match is deliberately narrow - the index
        /// and collection-state exceptions, and only with an AvalonDock frame on
        /// the stack - so no real fault gets hidden.
        /// </summary>
        private static bool IsDockingLayoutBug(Exception exception)
        {
            if (exception == null) return false;

            if (!(exception is ArgumentOutOfRangeException) &&
                !(exception is IndexOutOfRangeException) &&
                !(exception is InvalidOperationException))
                return false;

            try
            {
                return exception.StackTrace != null &&
                       exception.StackTrace.IndexOf("AvalonDock", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool IsOurs(Exception exception)
        {
            try
            {
                for (Exception current = exception; current != null; current = current.InnerException)
                {
                    if (current.StackTrace != null &&
                        current.StackTrace.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    if (current.Source != null &&
                        current.Source.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    if (current.TargetSite != null &&
                        current.TargetSite.DeclaringType != null &&
                        current.TargetSite.DeclaringType.FullName != null &&
                        current.TargetSite.DeclaringType.FullName.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Appends one entry. Never throws: a logger that can fail during a crash
        /// is worse than no logger.
        /// </summary>
        public static void Write(string kind, string note, Exception exception)
        {
            if (string.IsNullOrEmpty(LogPath)) return;

            try
            {
                StringBuilder text = new StringBuilder();

                text.Append("=== ")
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append("  [").Append(kind).Append("]")
                    .AppendLine();

                if (!string.IsNullOrEmpty(note)) text.AppendLine(note);

                int depth = 0;
                for (Exception current = exception; current != null && depth < 8; current = current.InnerException, depth++)
                {
                    text.Append(depth == 0 ? "" : "--- inner ---").AppendLine();
                    text.Append("Type   : ").AppendLine(current.GetType().FullName);
                    text.Append("Message: ").AppendLine(current.Message);

                    if (current.Source != null)
                        text.Append("Source : ").AppendLine(current.Source);

                    if (current.TargetSite != null)
                        text.Append("Method : ").AppendLine(current.TargetSite.ToString());

                    text.AppendLine("Stack  :");
                    text.AppendLine(current.StackTrace ?? "  (none)");
                }

                text.AppendLine();

                lock (_gate)
                {
                    try
                    {
                        FileInfo info = new FileInfo(LogPath);
                        if (info.Exists && info.Length > MaxLogBytes)
                            File.Delete(LogPath);
                    }
                    catch { }

                    File.AppendAllText(LogPath, text.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}

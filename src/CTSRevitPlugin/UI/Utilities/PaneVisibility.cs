using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTSRevitPlugin.UI.Utilities
{
    /// <summary>
    /// Tells whether a dockable pane is actually on screen.
    ///
    /// FrameworkElement.IsVisible is NOT the right test here. Revit hosts each
    /// dockable pane in its own Win32 window and closes the pane by hiding that
    /// window. WPF's visibility chain only tracks the Visibility property inside
    /// its own tree, so the hosted Page keeps reporting IsVisible == true after
    /// the pane is closed, and any guard written against it silently does nothing.
    ///
    /// DockablePane.IsShown() asks Revit directly, which is the only answer that
    /// matches what the user sees.
    /// </summary>
    internal static class PaneVisibility
    {
        public static bool IsShown(Document doc, DockablePaneId paneId)
        {
            if (doc == null || paneId == null) return false;

            try
            {
                // SelectionChangedEventArgs hands us a Document, not a UIApplication,
                // but one can be built from the other cheaply.
                UIApplication application = new UIApplication(doc.Application);

                DockablePane pane = application.GetDockablePane(paneId);
                return pane != null && pane.IsShown();
            }
            catch
            {
                // Pane not registered yet, or Revit is in a state where it cannot
                // answer. Treat as hidden: skipping a refresh is cheap, doing the
                // work for nothing is what we are trying to avoid.
                return false;
            }
        }
    }
}

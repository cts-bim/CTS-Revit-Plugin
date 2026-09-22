using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CTSRevitPlugin.UI.ElementFilter
{
    /// <summary>
    /// Applies a filter's action inside a valid Revit API context.
    ///
    /// Temporary hide/isolate is used for Isolate and Hide so nothing is permanently
    /// changed in the view, and the graphic overrides applied by Halftone and
    /// Transparency are remembered per view so Reset View can take them back off
    /// without disturbing overrides the user set by hand.
    /// </summary>
    public class ElementFilterEventHandler : IExternalEventHandler
    {
        private enum RequestType
        {
            None,
            Apply,
            ResetView
        }

        private RequestType _request = RequestType.None;
        private FilterDefinition _filter;
        private FilterAction _action;
        private Dictionary<string, string> _promptValues;

        public ElementFilterPane Pane { get; set; }

        /// <summary>
        /// Elements this tool overrode, per view, so Reset View clears exactly what
        /// it applied. Keyed by view id.
        /// </summary>
        private readonly Dictionary<long, HashSet<long>> _overridden =
            new Dictionary<long, HashSet<long>>();

        public void RequestApply(
            FilterDefinition filter,
            FilterAction action,
            Dictionary<string, string> promptValues)
        {
            if (filter == null) return;

            _filter = filter;
            _action = action;
            _promptValues = promptValues;
            _request = RequestType.Apply;
        }

        public void RequestResetView()
        {
            _request = RequestType.ResetView;
        }

        public string GetName()
        {
            return "CTS Element Filter";
        }

        public void Execute(UIApplication application)
        {
            RequestType request = _request;
            FilterDefinition filter = _filter;
            FilterAction action = _action;
            Dictionary<string, string> promptValues = _promptValues;

            _request = RequestType.None;
            _filter = null;
            _promptValues = null;

            if (request == RequestType.None || application == null) return;

            UIDocument uidoc = application.ActiveUIDocument;
            if (uidoc == null) { Status("No active document."); return; }

            Document doc = uidoc.Document;
            View view = doc == null ? null : doc.ActiveView;

            if (doc == null || view == null) { Status("No active view."); return; }

            try
            {
                if (request == RequestType.ResetView) { ResetView(doc, view); return; }
                Apply(uidoc, doc, view, filter, action, promptValues);
            }
            catch (Exception ex)
            {
                Status("Element Filter failed: " + ex.Message);
            }
        }

        // ============================================================
        // APPLY
        // ============================================================

        private void Apply(
            UIDocument uidoc,
            Document doc,
            View view,
            FilterDefinition filter,
            FilterAction action,
            Dictionary<string, string> promptValues)
        {
            ElementFilterEngine.MatchResult result =
                ElementFilterEngine.Run(doc, view, filter, promptValues);

            if (result.Matches.Count == 0)
            {
                Status("'" + filter.Name + "' matched nothing (" + result.Examined + " element(s) checked).");
                return;
            }

            string what = result.Matches.Count + " element(s)";

            switch (action)
            {
                case FilterAction.Select:
                    uidoc.Selection.SetElementIds(result.Matches);
                    Status(what + " selected.");
                    return;

                case FilterAction.Isolate:
                    RunTransaction(doc, "Element Filter - Isolate", delegate
                    {
                        view.IsolateElementsTemporary(result.Matches);
                    });
                    Status(what + " isolated. Use RESET VIEW to restore.");
                    return;

                case FilterAction.Hide:
                    RunTransaction(doc, "Element Filter - Hide", delegate
                    {
                        view.HideElementsTemporary(result.Matches);
                    });
                    Status(what + " hidden. Use RESET VIEW to restore.");
                    return;

                case FilterAction.Halftone:
                    ApplyOverride(doc, view, result.Matches, true, -1);
                    Status(what + " set to halftone. Use RESET VIEW to restore.");
                    return;

                case FilterAction.Transparency:
                    ApplyOverride(doc, view, result.Matches, false, Clamp(filter.Transparency));
                    Status(what + " set to " + Clamp(filter.Transparency) +
                           "% transparency. Use RESET VIEW to restore.");
                    return;

                default:
                    Status("No action was chosen.");
                    return;
            }
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private void ApplyOverride(
            Document doc,
            View view,
            IList<ElementId> ids,
            bool halftone,
            int transparency)
        {
            RunTransaction(doc, halftone ? "Element Filter - Halftone" : "Element Filter - Transparency",
                delegate
                {
                    OverrideGraphicSettings settings = new OverrideGraphicSettings();

                    if (halftone) settings.SetHalftone(true);
                    else settings.SetSurfaceTransparency(transparency);

                    HashSet<long> tracked = Tracked(view);

                    foreach (ElementId id in ids)
                    {
                        try
                        {
                            view.SetElementOverrides(id, settings);
                            tracked.Add(id.IdValue());
                        }
                        catch
                        {
                            // Some elements cannot be overridden in some views; skip them.
                        }
                    }
                });
        }

        private HashSet<long> Tracked(View view)
        {
            long key = view.Id.IdValue();

            HashSet<long> set;
            if (!_overridden.TryGetValue(key, out set))
            {
                set = new HashSet<long>();
                _overridden[key] = set;
            }

            return set;
        }

        // ============================================================
        // RESET
        // ============================================================

        private void ResetView(Document doc, View view)
        {
            int cleared = 0;
            bool temporaryCleared = false;

            RunTransaction(doc, "Element Filter - Reset View", delegate
            {
                try
                {
                    if (view.IsTemporaryHideIsolateActive())
                    {
                        view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                        temporaryCleared = true;
                    }
                }
                catch { }

                HashSet<long> tracked;
                if (_overridden.TryGetValue(view.Id.IdValue(), out tracked) && tracked.Count > 0)
                {
                    // An empty settings object is how Revit clears an override.
                    OverrideGraphicSettings none = new OverrideGraphicSettings();

                    foreach (long raw in tracked.ToList())
                    {
                        try
                        {
                            ElementId id = raw.ToElementId();
                            if (doc.GetElement(id) == null) continue;

                            view.SetElementOverrides(id, none);
                            cleared++;
                        }
                        catch { }
                    }

                    tracked.Clear();
                }
            });

            if (!temporaryCleared && cleared == 0)
            {
                Status("Nothing to restore in this view.");
                return;
            }

            string message = "View restored.";
            if (cleared > 0) message += " " + cleared + " override(s) cleared.";
            Status(message);
        }

        private static void RunTransaction(Document doc, string name, Action body)
        {
            using (Transaction transaction = new Transaction(doc, name))
            {
                transaction.Start();

                try
                {
                    body();
                    transaction.Commit();
                }
                catch
                {
                    transaction.RollBack();
                    throw;
                }
            }
        }

        private void Status(string message)
        {
            if (Pane != null) Pane.SetStatus(message);
        }
    }
}

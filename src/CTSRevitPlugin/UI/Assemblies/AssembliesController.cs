using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace CTSRevitPlugin.UI.Assemblies
{
    /// <summary>
    /// Runs the queued Revit API work of the Assemblies pane and builder window.
    /// The pane and the builder are modeless WPF windows, so anything that touches the
    /// Revit API has to go through an ExternalEvent.
    /// </summary>
    public sealed class AssembliesController
    {
        private sealed class Handler : IExternalEventHandler
        {
            private readonly Queue<Action<UIApplication>> _queue = new Queue<Action<UIApplication>>();
            private readonly object _sync = new object();

            public void Enqueue(Action<UIApplication> work)
            {
                lock (_sync) { _queue.Enqueue(work); }
            }

            public void Execute(UIApplication app)
            {
                while (true)
                {
                    Action<UIApplication> work;
                    lock (_sync)
                    {
                        if (_queue.Count == 0) return;
                        work = _queue.Dequeue();
                    }

                    try
                    {
                        work(app);
                    }
                    catch (Exception ex)
                    {
                        try { CtsMessageWindow.Show("CTS Assemblies", ex.Message); } catch { }
                    }
                }
            }

            public string GetName()
            {
                return "CTS Assemblies";
            }
        }

        private readonly Handler _handler = new Handler();
        private ExternalEvent _event;

        /// <summary>Must be created inside a valid API context (Revit startup).</summary>
        public AssembliesController()
        {
            _event = ExternalEvent.Create(_handler);
        }

        public void Dispose()
        {
            try
            {
                if (_event != null)
                {
                    _event.Dispose();
                    _event = null;
                }
            }
            catch { }
        }

        private void Run(Action<UIApplication> work)
        {
            if (_event == null) return;
            _handler.Enqueue(work);
            _event.Raise();
        }

        private static void Post(Dispatcher dispatcher, Action action)
        {
            if (dispatcher == null) { action(); return; }
            dispatcher.BeginInvoke(action);
        }

        // ============================================================
        // CATALOG (database browsing for the builder)
        // ============================================================

        public void RequestServices(Dispatcher dispatcher, Action<List<CatalogService>, string> done)
        {
            Run(app =>
            {
                List<CatalogService> services = new List<CatalogService>();
                string error = null;

                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    error = "Open a project to browse its fabrication database.";
                }
                else
                {
                    try
                    {
                        services = FabricationCatalogService.GetServices(uidoc.Document);
                        if (services.Count == 0)
                            error = "No fabrication services are loaded in this project.";
                    }
                    catch (Exception ex)
                    {
                        error = "Could not read the fabrication database: " + ex.Message;
                    }
                }

                Post(dispatcher, () => done(services, error));
            });
        }

        public void RequestPalette(Dispatcher dispatcher, string serviceName, int paletteIndex, Action<List<CatalogButton>, string> done)
        {
            Run(app =>
            {
                List<CatalogButton> buttons = new List<CatalogButton>();
                string error = null;

                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    error = "Open a project to browse its fabrication database.";
                }
                else
                {
                    try
                    {
                        buttons = FabricationCatalogService.GetPaletteButtons(uidoc.Document, serviceName, paletteIndex);
                    }
                    catch (Exception ex)
                    {
                        error = "Could not read the palette: " + ex.Message;
                    }
                }

                Post(dispatcher, () => done(buttons, error));
            });
        }

        /// <summary>
        /// Reads the product entries of one step's item so the builder can offer
        /// them before anything is placed. Runs in the API context because it has
        /// to create - and immediately roll back - a sample part.
        /// </summary>
        public void RequestProductEntries(
            Dispatcher dispatcher,
            CatalogItemRef item,
            double probeSizeFeet,
            Action<List<string>, string> done)
        {
            Run(app =>
            {
                List<string> entries = new List<string>();
                string error = null;

                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    error = "Open a project to read the product list.";
                }
                else
                {
                    try
                    {
                        entries = FabricationCatalogService.GetProductEntries(
                            uidoc.Document, item, probeSizeFeet, out error);
                    }
                    catch (Exception ex)
                    {
                        error = "Could not read the product list: " + ex.Message;
                    }
                }

                Post(dispatcher, () => done(entries, error));
            });
        }

        // ============================================================
        // APPLY TO OLETS
        // ============================================================

        private sealed class ResolvedSet
        {
            public List<FabricationServiceButton> Buttons;
            public List<string> Problems;
        }

        /// <summary>
        /// Builds the assembly on the olets the user selected in the model. When nothing valid is
        /// selected, the user is asked to pick one or more olets. Every olet is built in its own
        /// transaction (a failure only rolls back that olet) and the whole run is one Undo step.
        /// </summary>
        public void RequestApply(Dispatcher dispatcher, AssemblyDefinition assembly, Action<string> report)
        {
            Run(app => ExecuteApply(app, dispatcher, assembly, report));
        }

        private void ExecuteApply(UIApplication app, Dispatcher dispatcher, AssemblyDefinition assembly, Action<string> report)
        {
            Action<string> say = message => Post(dispatcher, () => report(message));

            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                say("Open a project first.");
                return;
            }

            Document doc = uidoc.Document;

            // 1) olets already selected in the model, or ask the user to pick them
            List<FabricationPart> anchors = new List<FabricationPart>();
            int ignored = 0;

            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (AssemblyPlacementService.IsOlet(element)) anchors.Add((FabricationPart)element);
                else ignored++;
            }

            if (anchors.Count == 0)
            {
                say("Select the olet(s) in the model, then press Finish.");
                try
                {
                    IList<Reference> picked = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new FabricationAnchorFilter(),
                        assembly.Name + ": select the olet(s) to build on, then press Finish. Esc cancels.");

                    foreach (Reference reference in picked)
                    {
                        FabricationPart part = doc.GetElement(reference.ElementId) as FabricationPart;
                        if (part != null) anchors.Add(part);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    say("Ready");
                    return;
                }
            }

            if (anchors.Count == 0)
            {
                say("No olet selected.");
                return;
            }

            // 2) build on each olet
            int built = 0;
            List<string> failures = new List<string>();
            List<string> warnings = new List<string>();
            Dictionary<int, ResolvedSet> resolved = new Dictionary<int, ResolvedSet>();

            using (TransactionGroup group = new TransactionGroup(doc, "CTS Assembly - " + assembly.Name))
            {
                group.Start();

                for (int n = 0; n < anchors.Count; n++)
                {
                    FabricationPart anchor = anchors[n];
                    string label = "Olet " + (n + 1) + " (id " + anchor.Id.IdValue() + ")";

                    // The items are looked up in the service of the olet, so assemblies are not tied to one service.
                    int serviceId = anchor.ServiceId;
                    ResolvedSet set;
                    if (!resolved.TryGetValue(serviceId, out set))
                    {
                        set = new ResolvedSet();
                        FabricationService service = FabricationCatalogService.GetServiceOfPart(doc, anchor);
                        set.Problems = AssemblyPlacementService.ResolveButtons(doc, assembly, service, out set.Buttons);
                        resolved[serviceId] = set;
                    }

                    if (set.Problems.Count > 0)
                    {
                        failures.Add(label + ": " + string.Join(" ", set.Problems));
                        continue;
                    }

                    PlacementResult result;
                    using (Transaction transaction = new Transaction(doc, "CTS Assembly - " + assembly.Name))
                    {
                        transaction.Start();

                        try
                        {
                            result = AssemblyPlacementService.BuildFromAnchor(doc, assembly, set.Buttons, anchor);
                        }
                        catch (Exception ex)
                        {
                            result = new PlacementResult { Error = ex.Message };
                        }

                        if (result.Success) transaction.Commit();
                        else transaction.RollBack();
                    }

                    if (result.Success)
                    {
                        built++;
                        foreach (string warning in result.Warnings) warnings.Add(label + ": " + warning);

                        // Placement is the only moment Revit will list a part's
                        // product entries, so whatever it reported is written back
                        // to disk. That is what fills the builder's PRODUCT ENTRY
                        // dropdown the next time this assembly is opened.
                        if (result.ProductEntriesDiscovered)
                        {
                            try { AssemblyStore.Upsert(assembly); }
                            catch { }
                        }
                    }
                    else
                    {
                        failures.Add(label + ": " + (string.IsNullOrWhiteSpace(result.Error) ? "unknown error." : result.Error));
                    }
                }

                if (built > 0) group.Assimilate();
                else group.RollBack();
            }

            // 3) report
            string summary = built + " of " + anchors.Count + " olet(s) built with " + assembly.Name + ".";
            if (ignored > 0 && anchors.Count > 0)
                summary += " " + ignored + " selected element(s) were ignored (not olets).";

            say(summary + (failures.Count > 0 ? " Some could not be built." : string.Empty));

            if (failures.Count > 0 || warnings.Count > 0)
            {
                StringBuilder text = new StringBuilder();
                text.Append(summary);

                if (failures.Count > 0)
                {
                    text.Append("\n\nNot built (nothing was changed on these):");
                    foreach (string failure in failures.Distinct().Take(10))
                        text.Append("\n - ").Append(failure);
                }

                text.Append(BuildWarningText(warnings));
                CtsMessageWindow.Show("CTS Assemblies - " + assembly.Name, text.ToString());
            }
        }

        private static string BuildWarningText(IEnumerable<string> warnings)
        {
            List<string> list = warnings == null ? new List<string>() : warnings.Distinct().ToList();
            if (list.Count == 0) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.Append("\n\nNotes:");
            foreach (string warning in list.Take(12))
                builder.Append("\n - ").Append(warning);
            if (list.Count > 12)
                builder.Append("\n - ... and ").Append(list.Count - 12).Append(" more.");
            return builder.ToString();
        }
    }
}

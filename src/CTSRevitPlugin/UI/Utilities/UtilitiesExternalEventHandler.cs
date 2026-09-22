using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;
using CTSRevitPlugin.Commands.Hanger;
using CTSRevitPlugin.UI.ElementTools;

namespace CTSRevitPlugin.UI.Utilities
{
    public interface IUtilitiesPaneHost
    {
        void RefreshFromUIApplication(UIApplication uiapp);
        void SetToolStatus(string message);
        void SetHangerAdjustmentStatus(string message);
    }

    public class UtilitiesExternalEventHandler
        : IExternalEventHandler
    {
        private enum RequestType
        {
            None,
            RefreshSelection,
            IncreaseRodLength,
            DecreaseRodLength,
            ExecuteTool,
            ExecuteNativeShortcut,
            ExecuteExternalShortcut,
            SetParameter,
            AdjustStrutChannel,
            OpenMechanicalProperties,
            ApplyMechanicalEdits,
            ReadAncillaries
        }

        private RequestType _request =
            RequestType.None;

        private double _amount;

        private string _toolId;

        private List<ElementId> _parameterElementIds;

        private string _parameterName;

        private string _parameterValue;
        private double _strutDelta;
        private ElementId _mechanicalElementId;
        private string _nativeCommandName;
        private List<string> _externalCommandCandidates;
        private List<ElementId> _mechanicalEditTargets;
        private List<MechanicalEdit> _mechanicalEdits;
        private ElementId _ancillaryElementId;

        public IUtilitiesPaneHost Pane
        {
            get;
            set;
        }

        // ============================================================
        // REQUEST: REFRESH
        // ============================================================

        public void RequestRefresh()
        {
            _request =
                RequestType.RefreshSelection;
        }

        // ============================================================
        // REQUEST: INCREASE
        // ============================================================

        public void RequestIncrease(
            double amount)
        {
            _amount =
                Math.Abs(amount);

            _request =
                RequestType.IncreaseRodLength;
        }

        // ============================================================
        // REQUEST: DECREASE
        // ============================================================

        public void RequestDecrease(
            double amount)
        {
            _amount =
                Math.Abs(amount);

            _request =
                RequestType.DecreaseRodLength;
        }

        // ============================================================
        // REQUEST: TOOL
        // ============================================================

        public void RequestTool(
            string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return;

            _toolId =
                toolId;

            _request =
                RequestType.ExecuteTool;
        }

        public void RequestNativeShortcut(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName)) return;
            _nativeCommandName = commandName;
            _request = RequestType.ExecuteNativeShortcut;
        }

        public void RequestExternalShortcut(IEnumerable<string> commandCandidates)
        {
            if (commandCandidates == null) return;
            _externalCommandCandidates = commandCandidates.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (_externalCommandCandidates.Count == 0) return;
            _request = RequestType.ExecuteExternalShortcut;
        }

        // ============================================================
        // REQUEST: SET PARAMETER
        // ============================================================

        public void RequestSetParameter(
            ElementId elementId,
            string parameterName,
            string value)
        {
            RequestSetParameter(
                elementId == null ? null : new List<ElementId> { elementId },
                parameterName,
                value);
        }

        /// <summary>
        /// Applies one value to every element in the selection.
        ///
        /// The pane used to send a single ElementId - the first of the selection -
        /// so editing a parameter with five elements picked only ever changed one
        /// of them.
        /// </summary>
        public void RequestSetParameter(
            IList<ElementId> elementIds,
            string parameterName,
            string value)
        {
            if (
                elementIds == null ||
                elementIds.Count == 0 ||
                string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            _parameterElementIds =
                elementIds.Where(x => x != null).ToList();

            if (_parameterElementIds.Count == 0)
                return;

            _parameterName =
                parameterName;

            _parameterValue =
                value ?? "";

            _request =
                RequestType.SetParameter;
        }

        // ============================================================
        // REQUEST: STRUT CHANNEL
        // ============================================================

        public void RequestStrutChannel(double deltaFeet)
        {
            _strutDelta = deltaFeet;
            _request = RequestType.AdjustStrutChannel;
        }

        public void RequestMechanicalProperties(ElementId elementId)
        {
            if (elementId == null) return;
            _mechanicalElementId = elementId;
            _request = RequestType.OpenMechanicalProperties;
        }

        // ============================================================
        // REQUEST: MECHANICAL PROPERTIES EDITS
        //
        // The pane stages every change the user makes and sends them here in
        // one call, so a whole edit set lands in a single Revit transaction
        // and a single undo step.
        // ============================================================

        public void RequestMechanicalEdits(IList<ElementId> elementIds, IList<MechanicalEdit> edits)
        {
            if (elementIds == null || elementIds.Count == 0) return;
            if (edits == null || edits.Count == 0) return;

            _mechanicalEditTargets = elementIds.Where(x => x != null).ToList();
            if (_mechanicalEditTargets.Count == 0) return;

            _mechanicalEdits = edits.Where(x => x != null).ToList();
            if (_mechanicalEdits.Count == 0) return;

            _request = RequestType.ApplyMechanicalEdits;
        }

        public void RequestAncillaries(ElementId elementId)
        {
            if (elementId == null) return;
            _ancillaryElementId = elementId;
            _request = RequestType.ReadAncillaries;
        }

        // ============================================================
        // EXECUTE
        // ============================================================

        public void Execute(
            UIApplication application)
        {
            RequestType request =
                _request;

            double amount =
                _amount;

            string toolId =
                _toolId;

            List<ElementId> parameterElementIds =
                _parameterElementIds;

            string parameterName =
                _parameterName;

            string parameterValue =
                _parameterValue;

            double strutDelta = _strutDelta;
            ElementId mechanicalElementId = _mechanicalElementId;
            string nativeCommandName = _nativeCommandName;
            List<string> externalCommandCandidates = _externalCommandCandidates;
            List<ElementId> mechanicalEditTargets = _mechanicalEditTargets;
            List<MechanicalEdit> mechanicalEdits = _mechanicalEdits;
            ElementId ancillaryElementId = _ancillaryElementId;

            _request =
                RequestType.None;

            _amount =
                0;

            _toolId =
                null;

            _parameterElementIds =
                null;

            _parameterName =
                null;

            _parameterValue =
                null;
            _strutDelta = 0;
            _mechanicalElementId = null;
            _nativeCommandName = null;
            _externalCommandCandidates = null;
            _mechanicalEditTargets = null;
            _mechanicalEdits = null;
            _ancillaryElementId = null;

            if (request == RequestType.None)
                return;

            if (application == null)
                return;

            UIDocument uidoc =
                application.ActiveUIDocument;

            if (uidoc == null)
                return;

            Document doc =
                uidoc.Document;

            if (doc == null)
                return;

            // ========================================================
            // REFRESH
            // ========================================================

            if (
                request ==
                RequestType.RefreshSelection)
            {
                if (Pane != null)
                {
                    Pane.RefreshFromUIApplication(
                        application);
                }

                return;
            }

            // ========================================================
            // NATIVE SHORTCUT
            // ========================================================

            if (request == RequestType.ExecuteNativeShortcut)
            {
                ExecuteNativeShortcut(application, nativeCommandName);
                return;
            }

            // ========================================================
            // EXTERNAL ADD-IN SHORTCUT
            // ========================================================

            if (request == RequestType.ExecuteExternalShortcut)
            {
                ExecuteExternalShortcut(application, externalCommandCandidates);
                return;
            }

            // ========================================================
            // SET PARAMETER
            // ========================================================

            if (
                request ==
                RequestType.SetParameter)
            {
                SetParameterValue(
                    application,
                    doc,
                    parameterElementIds,
                    parameterName,
                    parameterValue);

                return;
            }

            // ========================================================
            // MECHANICAL PROPERTIES
            // ========================================================

            if (request == RequestType.OpenMechanicalProperties)
            {
                Element element = mechanicalElementId == null ? null : doc.GetElement(mechanicalElementId);
                if (element == null)
                {
                    SetToolStatus("Selected element is no longer available.");
                    return;
                }

                MechanicalPropertiesSnapshot snapshot =
                    MechanicalPropertiesInspector.Inspect(element);

                if (MechanicalPropertiesPane.Instance != null)
                {
                    MechanicalPropertiesPane.Instance.ShowSnapshot(snapshot);
                    try { application.GetDockablePane(MechanicalPropertiesPane.PaneId).Show(); } catch { }
                }

                return;
            }

            // ========================================================
            // MECHANICAL PROPERTIES: APPLY
            // ========================================================

            if (request == RequestType.ApplyMechanicalEdits)
            {
                ApplyMechanicalEdits(doc, mechanicalEditTargets, mechanicalEdits);
                return;
            }

            // ========================================================
            // MECHANICAL PROPERTIES: ANCILLARIES
            // ========================================================

            if (request == RequestType.ReadAncillaries)
            {
                ReadAncillaries(doc, ancillaryElementId);
                return;
            }

            // ========================================================
            // STRUT CHANNEL
            // ========================================================

            if (request == RequestType.AdjustStrutChannel)
            {
                AdjustStrutChannel(doc, uidoc, strutDelta);
                return;
            }

            // ========================================================
            // EXECUTE TOOL
            // ========================================================

            if (
                request ==
                RequestType.ExecuteTool)
            {
                ExecuteTool(
                    application,
                    toolId);

                return;
            }

            // ========================================================
            // VALIDATE AMOUNT
            // ========================================================

            if (amount <= 0)
                return;

            // ========================================================
            // SELECTION
            // ========================================================

            ICollection<ElementId> selectedIds =
                uidoc
                    .Selection
                    .GetElementIds();

            List<Element> hangers =
                selectedIds
                    .Select(
                        id => doc.GetElement(id))
                    .Where(
                        element =>
                            element != null &&
                            element.Category != null &&
                            element.Category.Id.IdValue() ==
                                (long)BuiltInCategory
                                    .OST_FabricationHangers)
                    .ToList();

            if (hangers.Count == 0)
            {
                if (Pane != null)
                {
                    Pane.SetHangerAdjustmentStatus(
                        "Select one or more fabrication hangers.");
                }

                return;
            }

            // ========================================================
            // DIRECTION
            // ========================================================

            double delta =
                request ==
                RequestType.IncreaseRodLength
                    ? amount
                    : -amount;

            int modified =
                0;

            // ========================================================
            // TRANSACTION
            // ========================================================

            using (
                Transaction transaction =
                    new Transaction(
                        doc,
                        "Adjust Rod Length"))
            {
                transaction.Start();

                foreach (
                    Element hanger
                    in hangers)
                {
                    try
                    {
                        var rodInfo =
                            RevitUtils.Invoke(
                                hanger,
                                "GetRodInfo");

                        if (rodInfo == null)
                            continue;

                        var rodCountProperty =
                            rodInfo
                                .GetType()
                                .GetProperty(
                                    "RodCount");

                        if (rodCountProperty == null)
                            continue;

                        int rodCount =
                            Convert.ToInt32(
                                rodCountProperty.GetValue(
                                    rodInfo,
                                    null));

                        if (rodCount <= 0)
                            continue;

                        var canHostedProperty =
                            rodInfo
                                .GetType()
                                .GetProperty(
                                    "CanRodsBeHosted");

                        if (
                            canHostedProperty != null &&
                            canHostedProperty.CanWrite)
                        {
                            bool canHosted =
                                Convert.ToBoolean(
                                    canHostedProperty.GetValue(
                                        rodInfo,
                                        null));

                            if (canHosted)
                            {
                                canHostedProperty.SetValue(
                                    rodInfo,
                                    false,
                                    null);
                            }
                        }

                        Type rodInfoType = rodInfo.GetType();

                        // FabricationRodInfo exposes overloads in some Revit
                        // versions. Resolve the exact signatures instead of
                        // relying on GetMethod(name), which can become ambiguous.
                        var getLengthMethod = rodInfoType.GetMethods()
                            .FirstOrDefault(m =>
                                m.Name == "GetRodLength" &&
                                m.GetParameters().Length == 1 &&
                                m.GetParameters()[0].ParameterType == typeof(int));

                        var setLengthMethod = rodInfoType.GetMethods()
                            .FirstOrDefault(m =>
                                m.Name == "SetRodLength" &&
                                m.GetParameters().Length == 2 &&
                                m.GetParameters()[0].ParameterType == typeof(int) &&
                                m.GetParameters()[1].ParameterType == typeof(double));

                        if (
                            getLengthMethod == null ||
                            setLengthMethod == null)
                        {
                            continue;
                        }

                        for (
                            int i = 0;
                            i < rodCount;
                            i++)
                        {
                            object currentObject =
                                getLengthMethod.Invoke(
                                    rodInfo,
                                    new object[]
                                    {
                                        i
                                    });

                            if (currentObject == null)
                                continue;

                            double currentLength =
                                Convert.ToDouble(
                                    currentObject);

                            double newLength =
                                currentLength +
                                delta;

                            if (newLength <= 0)
                                continue;

                            setLengthMethod.Invoke(
                                rodInfo,
                                new object[]
                                {
                                    i,
                                    newLength
                                });

                            modified++;
                        }
                    }
                    catch
                    {
                        // Continue with remaining hangers.
                    }
                }

                transaction.Commit();
            }

            // ========================================================
            // STATUS
            // ========================================================

            if (Pane != null)
            {
                string direction =
                    delta > 0
                        ? "increased"
                        : "decreased";

                Pane.SetHangerAdjustmentStatus(
                    modified +
                    " rod(s) " +
                    direction +
                    " on " +
                    hangers.Count +
                    " hanger(s).");

                Pane.RefreshFromUIApplication(
                    application);
            }
        }

        // ============================================================
        // SET PARAMETER VALUE
        // ============================================================

        /// <summary>
        /// Writes one value to the named parameter on every supplied element, in a
        /// single transaction, and reports a summary. Elements that do not carry the
        /// parameter, or carry it read-only, are counted and skipped rather than
        /// aborting the whole edit.
        /// </summary>
        // ============================================================
        // MECHANICAL PROPERTIES
        // ============================================================

        /// <summary>
        /// Writes one staged edit set. Everything runs inside a single
        /// transaction so the user gets one undo step for one press of APPLY,
        /// and a rejected value does not leave half the selection changed.
        ///
        /// Two kinds of target:
        ///   - fabrication dimensions, written with SetDimensionValue through
        ///     the same helper the hanger tools use, which already knows how to
        ///     deal with a pattern that refuses to resize while hosted;
        ///   - ordinary Revit parameters, written the usual way.
        /// </summary>
        private void ApplyMechanicalEdits(
            Document doc,
            IList<ElementId> elementIds,
            IList<MechanicalEdit> edits)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0) return;
            if (edits == null || edits.Count == 0) return;

            int applied = 0;
            int rejected = 0;
            int skipped = 0;
            string lastError = null;

            try
            {
                using (Transaction transaction = new Transaction(doc, "CTS Mechanical Properties"))
                {
                    transaction.Start();

                    foreach (ElementId elementId in elementIds)
                    {
                        FabricationPart part = doc.GetElement(elementId) as FabricationPart;
                        if (part == null || !part.IsValidObject) { skipped++; continue; }

                        foreach (MechanicalEdit edit in edits)
                        {
                            try
                            {
                                bool ok = edit.IsDimension
                                    ? ApplyDimensionEdit(doc, part, edit)
                                    : ApplyParameterEdit(doc, part, edit);

                                if (ok) applied++;
                                else rejected++;
                            }
                            catch (Exception ex)
                            {
                                rejected++;
                                lastError = ex.Message;
                            }
                        }
                    }

                    if (applied == 0) transaction.RollBack();
                    else transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                SetToolStatus("Could not apply the changes: " + ex.Message);
                return;
            }

            string status = applied + " value(s) applied.";
            if (rejected > 0) status += " " + rejected + " rejected by Revit.";
            if (skipped > 0) status += " " + skipped + " element(s) are no longer fabrication parts.";
            if (rejected > 0 && lastError != null) status += " Last error: " + lastError;

            SetToolStatus(status);

            // Re-read so the pane shows what the model actually holds, not what
            // was typed: Revit may have snapped a dimension to a valid size.
            if (MechanicalPropertiesPane.Instance != null)
                MechanicalPropertiesPane.Instance.ReloadAfterApply(doc, elementIds);
        }

        private static bool ApplyDimensionEdit(Document doc, FabricationPart part, MechanicalEdit edit)
        {
            FabricationDimensionDefinition dimension = null;

            foreach (FabricationDimensionDefinition candidate in part.GetDimensions())
            {
                if (candidate == null) continue;
                if (!string.Equals(candidate.Name, edit.Name, StringComparison.OrdinalIgnoreCase)) continue;
                dimension = candidate;
                break;
            }

            if (dimension == null || !dimension.IsModifiable) return false;

            double inches;
            if (!ImperialLength.TryParseInches(edit.Value, out inches)) return false;
            if (inches <= 0) return false;

            return RoundStrutChannelCommand.TrySetDimensionValue(doc, part, dimension, inches / 12.0);
        }

        private bool ApplyParameterEdit(Document doc, FabricationPart part, MechanicalEdit edit)
        {
            Parameter parameter = part.LookupParameter(edit.Name);
            if (parameter == null || parameter.IsReadOnly) return false;

            return TrySetOne(doc, parameter, edit.Value ?? "");
        }

        /// <summary>
        /// Ancillaries are read here rather than on selection: the lookup is the
        /// most expensive call in the pane, so it only happens when the user
        /// actually opens that section.
        /// </summary>
        private void ReadAncillaries(Document doc, ElementId elementId)
        {
            if (MechanicalPropertiesPane.Instance == null) return;

            Element element = null;
            try { element = doc == null || elementId == null ? null : doc.GetElement(elementId); }
            catch { }

            if (element == null)
            {
                SetToolStatus("The selected element is no longer available.");
                return;
            }

            List<MechanicalPropertyItem> items =
                MechanicalPropertiesInspector.ReadAncillaries(element);

            MechanicalPropertiesPane.Instance.ShowAncillaries(items);
        }

        private void SetParameterValue(
            UIApplication application,
            Document doc,
            IList<ElementId> elementIds,
            string parameterName,
            string value)
        {
            if (
                doc == null ||
                elementIds == null ||
                elementIds.Count == 0 ||
                string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            string newValue = value ?? "";

            int applied = 0;
            int missing = 0;
            int readOnly = 0;
            int rejected = 0;
            string lastError = null;

            try
            {
                using (Transaction transaction = new Transaction(doc, "Edit Parameter"))
                {
                    transaction.Start();

                    foreach (ElementId elementId in elementIds)
                    {
                        if (elementId == null) continue;

                        Element element = doc.GetElement(elementId);
                        if (element == null) { missing++; continue; }

                        Parameter parameter = element.LookupParameter(parameterName);
                        if (parameter == null) { missing++; continue; }
                        if (parameter.IsReadOnly) { readOnly++; continue; }

                        try
                        {
                            if (TrySetOne(doc, parameter, newValue)) applied++;
                            else rejected++;
                        }
                        catch (Exception ex)
                        {
                            rejected++;
                            lastError = ex.Message;
                        }
                    }

                    if (applied == 0)
                    {
                        transaction.RollBack();
                        Report(parameterName, applied, missing, readOnly, rejected, lastError);
                        return;
                    }

                    transaction.Commit();
                }

                Report(parameterName, applied, missing, readOnly, rejected, lastError);

                if (Pane != null)
                    Pane.RefreshFromUIApplication(application);
            }
            catch (Exception ex)
            {
                if (Pane != null)
                {
                    Pane.SetToolStatus(
                        "Could not edit " + parameterName + ": " + ex.Message);
                }
            }
        }

        private bool TrySetOne(
            Document doc,
            Parameter parameter,
            string newValue)
        {
            if (parameter.StorageType == StorageType.String)
                return parameter.Set(newValue);

            if (parameter.StorageType == StorageType.Double ||
                parameter.StorageType == StorageType.Integer)
                return parameter.SetValueString(newValue);

            if (parameter.StorageType == StorageType.ElementId)
            {
                ElementId referencedId = TryResolveElementId(doc, newValue);
                if (referencedId == null) return false;
                return parameter.Set(referencedId);
            }

            return false;
        }

        private void Report(
            string parameterName,
            int applied,
            int missing,
            int readOnly,
            int rejected,
            string lastError)
        {
            if (Pane == null) return;

            if (applied == 0)
            {
                string why = "Value was not applied.";

                if (readOnly > 0 && missing == 0 && rejected == 0)
                    why = parameterName + " is read-only on the selected element(s).";
                else if (missing > 0 && readOnly == 0 && rejected == 0)
                    why = "Parameter not found: " + parameterName + ".";
                else if (rejected > 0 && lastError != null)
                    why = "Could not set " + parameterName + ": " + lastError;
                else if (rejected > 0)
                    why = "The value is not valid for " + parameterName + ".";

                Pane.SetToolStatus(why);
                return;
            }

            string status = parameterName + " updated on " + applied + " element(s).";

            if (missing > 0) status += " " + missing + " without the parameter.";
            if (readOnly > 0) status += " " + readOnly + " read-only.";
            if (rejected > 0) status += " " + rejected + " rejected the value.";

            Pane.SetToolStatus(status);
        }

        // ============================================================
        // TRY RESOLVE ELEMENT ID
        // ============================================================

        private ElementId TryResolveElementId(
            Document doc,
            string value)
        {
            if (
                doc == null ||
                string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // --------------------------------------------------------
            // FIRST: TRY NUMERIC ELEMENT ID
            // --------------------------------------------------------

            int numericId;

            if (
                int.TryParse(
                    value.Trim(),
                    out numericId))
            {
                ElementId id =
                    ((long)numericId).ToElementId();

                if (
                    doc.GetElement(id) != null)
                {
                    return id;
                }
            }

            // --------------------------------------------------------
            // SECOND: TRY ELEMENT NAME
            // --------------------------------------------------------

            try
            {
                FilteredElementCollector collector =
                    new FilteredElementCollector(
                        doc)
                    .WhereElementIsNotElementType();

                foreach (
                    Element element
                    in collector)
                {
                    if (element == null)
                        continue;

                    if (
                        string.Equals(
                            element.Name,
                            value.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return element.Id;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        // ============================================================
        // EXECUTE TOOL
        // ============================================================

        private void AdjustStrutChannel(Document doc, UIDocument uidoc, double delta)
        {
            if (doc == null || uidoc == null) return;

            IList<ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();
            if (selectedIds.Count == 0)
            {
                if (HangersPane.Instance != null)
                    HangersPane.Instance.SetStrutAdjustmentStatus("Select one or more elements first.");
                return;
            }

            List<FabricationPart> hangers = new List<FabricationPart>();
            int skippedNonHangers = 0;
            foreach (ElementId id in selectedIds)
            {
                FabricationPart part = doc.GetElement(id) as FabricationPart;
                if (part != null && part.IsAHanger()) hangers.Add(part);
                else skippedNonHangers++;
            }

            if (hangers.Count == 0)
            {
                if (HangersPane.Instance != null)
                    HangersPane.Instance.SetStrutAdjustmentStatus("Select one or more MEP Fabrication Hangers.");
                return;
            }

            int changed = 0;
            int atMinimum = 0;
            int unsupported = 0;
            int noDimension = 0;

            using (Transaction transaction = new Transaction(doc, "Adjust Strut Channel"))
            {
                transaction.Start();

                foreach (FabricationPart hanger in hangers)
                {
                    try
                    {
                        FabricationDimensionDefinition dimension =
                            RoundStrutChannelCommand.FindStrutDimension(hanger);

                        if (dimension == null || !dimension.IsModifiable)
                        {
                            // No editable strut/bearer dimension on this pattern at
                            // all, which is a different problem from Revit refusing
                            // a value. Counted separately so the status line says so.
                            noDimension++;
                            continue;
                        }

                        double current = hanger.GetDimensionValue(dimension);

                        // delta == 0 is the Round Strut Channel operation.
                        if (Math.Abs(delta) < 1e-9)
                        {
                            double target =
                                Math.Round(current * 12.0, MidpointRounding.AwayFromZero) / 12.0;

                            if (Math.Abs(target - current) < 1e-9)
                            {
                                changed++;
                            }
                            else if (RoundStrutChannelCommand.TrySetStrutDimensionValue(doc, hanger, dimension, target))
                            {
                                changed++;
                            }
                            else
                            {
                                unsupported++;
                            }

                            continue;
                        }

                        double targetValue = current + delta;

                        // Increasing is straightforward: let Revit validate it.
                        if (delta > 0)
                        {
                            if (RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, targetValue))
                                changed++;
                            else
                                unsupported++;

                            continue;
                        }

                        // Decreasing: first try exactly the requested amount.
                        // If Revit rejects it, do NOT use the original hanger
                        // position as the minimum. Discover the actual lower
                        // valid boundary and move to that value.
                        if (targetValue > 0 &&
                            RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, targetValue))
                        {
                            changed++;
                            continue;
                        }

                        double minimum =
                            RoundStrutChannelCommand.FindMinimumValidDimension(
                                doc,
                                hanger,
                                dimension,
                                current);

                        if (minimum < current - 1e-7 &&
                            RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, minimum))
                        {
                            changed++;
                            atMinimum++;
                        }
                        else
                        {
                            // Already at the minimum Revit permits.
                            atMinimum++;
                        }
                    }
                    catch
                    {
                        unsupported++;
                    }
                }

                transaction.Commit();
            }

            if (HangersPane.Instance != null)
            {
                string status;

                if (Math.Abs(delta) < 1e-9)
                {
                    status = changed + " hanger(s) rounded to the nearest whole inch.";
                }
                else if (delta < 0)
                {
                    status = changed + " hanger(s) adjusted.";
                    if (atMinimum > 0)
                        status += " " + atMinimum + " hanger(s) reached the Revit minimum.";
                }
                else
                {
                    status = changed + " hanger(s) adjusted.";
                }

                if (skippedNonHangers > 0)
                    status += " " + skippedNonHangers + " non-hanger element(s) skipped.";
                if (noDimension > 0)
                    status += " " + noDimension + " hanger(s) have no editable strut/bearer dimension.";
                if (unsupported > 0)
                    status += " " + unsupported + " hanger(s) were rejected by Revit at that value.";

                HangersPane.Instance.SetStrutAdjustmentStatus(status);
            }
        }

        private void ExecuteNativeShortcut(UIApplication application, string commandName)
        {
            try
            {
                PostableCommand command;
                if (!Enum.TryParse(commandName, out command))
                {
                    SetToolStatus("Native Revit command not found: " + commandName);
                    return;
                }

                RevitCommandId commandId = RevitCommandId.LookupPostableCommandId(command);
                if (commandId == null || !application.CanPostCommand(commandId))
                {
                    SetToolStatus("This Revit command cannot be executed in the current context.");
                    return;
                }

                application.PostCommand(commandId);
                SetToolStatus("Running " + commandName + "...");
            }
            catch (Exception ex)
            {
                SetToolStatus("Could not run Revit command: " + ex.Message);
            }
        }

        private void ExecuteExternalShortcut(UIApplication application, IEnumerable<string> candidates)
        {
            if (candidates == null) return;
            try
            {
                foreach (string candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    RevitCommandId commandId = RevitCommandId.LookupCommandId(candidate);
                    if (commandId == null || !application.CanPostCommand(commandId)) continue;
                    application.PostCommand(commandId);
                    SetToolStatus("Running add-in command...");
                    return;
                }
                SetToolStatus("The selected add-in command is no longer available.");
            }
            catch (Exception ex)
            {
                SetToolStatus("Could not run add-in command: " + ex.Message);
            }
        }

        private void ExecuteTool(
            UIApplication application,
            string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return;

            ElementToolDefinition tool =
                ElementToolsRegistry.GetById(
                    toolId);

            string displayName =
                tool == null ? toolId : tool.Name;

            // Run the command in-process. The old path rebuilt a ribbon control
            // id and posted it, which never resolved for the buttons nested in a
            // SplitButton / PulldownButton.
            string error;

            if (CtsToolRunner.TryRun(application, toolId, out error))
            {
                SetToolStatus(displayName + " finished.");
                return;
            }

            SetToolStatus(
                "Could not run " +
                displayName +
                ":\n" +
                error);
        }

        // ============================================================
        // STATUS
        // ============================================================

        private void SetToolStatus(
            string message)
        {
            if (Pane == null)
                return;

            Pane.SetToolStatus(
                message);
        }

        // ============================================================
        // NAME
        // ============================================================

        public string GetName()
        {
            return
                "CTS Utilities External Event";
        }
    }
}
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.UI.ElementTools
{
    public class ElementToolsExternalEventHandler
        : IExternalEventHandler
    {
        private enum RequestType
        {
            None,
            RefreshSelection,
            IncreaseRodLength,
            DecreaseRodLength,
            ExecuteTool,
            SetParameter
        }

        private RequestType _request =
            RequestType.None;

        private double _amount;

        private string _toolId;

        private ElementId _parameterElementId;

        private string _parameterName;

        private string _parameterValue;

        public ElementToolsPane Pane
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

        // ============================================================
        // REQUEST: SET PARAMETER
        // ============================================================

        public void RequestSetParameter(
            ElementId elementId,
            string parameterName,
            string value)
        {
            if (
                elementId == null ||
                string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            _parameterElementId =
                elementId;

            _parameterName =
                parameterName;

            _parameterValue =
                value ?? "";

            _request =
                RequestType.SetParameter;
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

            ElementId parameterElementId =
                _parameterElementId;

            string parameterName =
                _parameterName;

            string parameterValue =
                _parameterValue;

            _request =
                RequestType.None;

            _amount =
                0;

            _toolId =
                null;

            _parameterElementId =
                null;

            _parameterName =
                null;

            _parameterValue =
                null;

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
            // SET PARAMETER
            // ========================================================

            if (
                request ==
                RequestType.SetParameter)
            {
                SetParameterValue(
                    application,
                    doc,
                    parameterElementId,
                    parameterName,
                    parameterValue);

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

                        var getLengthMethod =
                            rodInfo
                                .GetType()
                                .GetMethod(
                                    "GetRodLength");

                        var setLengthMethod =
                            rodInfo
                                .GetType()
                                .GetMethod(
                                    "SetRodLength");

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

        private void SetParameterValue(
            UIApplication application,
            Document doc,
            ElementId elementId,
            string parameterName,
            string value)
        {
            if (
                doc == null ||
                elementId == null ||
                string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            Element element =
                doc.GetElement(
                    elementId);

            if (element == null)
                return;

            Parameter parameter =
                element.LookupParameter(
                    parameterName);

            if (parameter == null)
            {
                if (Pane != null)
                {
                    Pane.SetToolStatus(
                        "Parameter not found: " +
                        parameterName);
                }

                return;
            }

            if (parameter.IsReadOnly)
            {
                if (Pane != null)
                {
                    Pane.SetToolStatus(
                        parameterName +
                        " is read-only.");
                }

                return;
            }

            string newValue =
                value ?? "";

            try
            {
                using (
                    Transaction transaction =
                        new Transaction(
                            doc,
                            "Edit Parameter"))
                {
                    transaction.Start();

                    bool changed =
                        false;

                    // ------------------------------------------------
                    // STRING
                    // ------------------------------------------------

                    if (
                        parameter.StorageType ==
                        StorageType.String)
                    {
                        changed =
                            parameter.Set(
                                newValue);
                    }

                    // ------------------------------------------------
                    // DOUBLE
                    // ------------------------------------------------

                    else if (
                        parameter.StorageType ==
                        StorageType.Double)
                    {
                        changed =
                            parameter.SetValueString(
                                newValue);
                    }

                    // ------------------------------------------------
                    // INTEGER
                    // ------------------------------------------------

                    else if (
                        parameter.StorageType ==
                        StorageType.Integer)
                    {
                        changed =
                            parameter.SetValueString(
                                newValue);
                    }

                    // ------------------------------------------------
                    // ELEMENT ID
                    // ------------------------------------------------

                    else if (
                        parameter.StorageType ==
                        StorageType.ElementId)
                    {
                        ElementId referencedId =
                            TryResolveElementId(
                                doc,
                                newValue);

                        if (referencedId != null)
                        {
                            changed =
                                parameter.Set(
                                    referencedId);
                        }
                        else
                        {
                            transaction.RollBack();

                            if (Pane != null)
                            {
                                Pane.SetToolStatus(
                                    "ElementId parameter cannot be resolved from: " +
                                    newValue);
                            }

                            return;
                        }
                    }

                    if (!changed)
                    {
                        transaction.RollBack();

                        if (Pane != null)
                        {
                            Pane.SetToolStatus(
                                "Value was not changed or is invalid.");
                        }

                        return;
                    }

                    transaction.Commit();
                }

                if (Pane != null)
                {
                    Pane.SetToolStatus(
                        parameterName +
                        " updated.");

                    Pane.RefreshFromUIApplication(
                        application);
                }
            }
            catch (Exception ex)
            {
                if (Pane != null)
                {
                    Pane.SetToolStatus(
                        "Could not edit " +
                        parameterName +
                        ": " +
                        ex.Message);
                }
            }
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
                "CTS Element Tools External Event";
        }
    }
}
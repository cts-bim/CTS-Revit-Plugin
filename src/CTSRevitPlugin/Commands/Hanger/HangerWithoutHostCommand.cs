using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Hanger
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Find fabrication hangers without a valid host.",
        usage: "Run the command in a model view; unhosted hangers in the view are temporarily isolated.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class HangerWithoutHostCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData data,
            ref string message,
            ElementSet elements)
        {
            // Thin wrapper. The real work lives in Run(UIApplication) so the
            // dockable panes can call it directly from their ExternalEvent
            // instead of resolving a ribbon control id through PostCommand.
            return Run(data.Application);
        }

        /// <summary>Runs the tool against a live UIApplication.</summary>
        public static Result Run(UIApplication uiapp)
        {

            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var activeView = doc.ActiveView;

            // =========================================================
            // 1. Todos os Fabrication Hangers do projeto
            // =========================================================
            var allHangers =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_FabricationHangers)
                    .WhereElementIsNotElementType()
                    .ToElements();

            // =========================================================
            // 2. Fabrication Hangers somente da vista ativa
            // =========================================================
            var viewHangers =
                new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_FabricationHangers)
                    .WhereElementIsNotElementType()
                    .ToElements();

            // =========================================================
            // 3. Encontrar Hangers sem Host no projeto
            // =========================================================
            var hangersWithoutHostProject =
                new List<Element>();

            foreach (var hanger in allHangers)
            {
                if (IsWithoutHost(hanger))
                {
                    hangersWithoutHostProject.Add(hanger);
                }
            }

            // =========================================================
            // 4. Encontrar Hangers sem Host na vista atual
            // =========================================================
            var hangersWithoutHostView =
                new List<Element>();

            foreach (var hanger in viewHangers)
            {
                if (IsWithoutHost(hanger))
                {
                    hangersWithoutHostView.Add(hanger);
                }
            }

            // =========================================================
            // 5. Quantidades
            // =========================================================
            int countProject =
                hangersWithoutHostProject.Count;

            int countView =
                hangersWithoutHostView.Count;

            // =========================================================
            // 6. Nenhum Hanger sem Host no projeto
            // =========================================================
            if (countProject == 0)
            {
                RevitUtils.Alert(
                    "All Hangers have a valid Host.",
                    "Success");

                return Result.Succeeded;
            }

            // =========================================================
            // 7. Existem Hangers sem Host, mas nenhum na vista atual
            // =========================================================
            if (countView == 0)
            {
                RevitUtils.Alert(
                    "No Hangers without Host in this view, " +
                    "but there are " +
                    countProject +
                    " in the entire project.",
                    "View Clean");

                return Result.Succeeded;
            }

            // =========================================================
            // 8. Preparar IDs para isolamento
            // =========================================================
            var idsToIsolate =
                new List<ElementId>();

            foreach (var hanger in hangersWithoutHostView)
            {
                idsToIsolate.Add(hanger.Id);
            }

            // =========================================================
            // 9. Isolar temporariamente os Hangers sem Host
            // =========================================================
            using (var t = new Transaction(
                doc,
                "Isolate Hangers without Host"))
            {
                t.Start();

                try
                {
                    activeView.IsolateElementsTemporary(
                        idsToIsolate);

                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();

                    RevitUtils.Alert(
                        "Error isolating elements:\n" +
                        ex.Message,
                        "Hangers without Host");

                    return Result.Failed;
                }
            }

            // =========================================================
            // 10. Resultado
            // =========================================================
            RevitUtils.Alert(
                countView +
                " Hangers without Host found and isolated in this view.\n" +
                "(Total in project: " +
                countProject +
                ")",
                "Hangers without Host");

            return Result.Succeeded;
        
        }


        // =============================================================
        // Verifica se o Hanger realmente está sem Host
        // =============================================================
        private static bool IsWithoutHost(Element hanger)
        {
            try
            {
                // GetHostedInfo() é uma API específica de
                // Fabrication Hangers.
                var hostedInfo =
                    RevitUtils.Invoke(
                        hanger,
                        "GetHostedInfo");

                // Sem HostedInfo = sem Host
                if (hostedInfo == null)
                {
                    return true;
                }

                // Procurar a propriedade HostId
                var hostIdProperty =
                    hostedInfo.GetType().GetProperty("HostId");

                if (hostIdProperty == null)
                {
                    return true;
                }

                var hostId =
                    hostIdProperty.GetValue(
                        hostedInfo,
                        null) as ElementId;

                // HostId inválido = sem Host
                if (hostId == null ||
                    hostId == ElementId.InvalidElementId)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                // Se não for possível obter HostedInfo,
                // tratamos como hanger sem host.
                return true;
            }
        }
    }
}
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Reflection;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Hanger
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Connect a fabrication hanger to a compatible straight fabrication host.",
        usage: "Select exactly one fabrication hanger and one straight fabrication pipe/duct, then run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class ConnectHangerCommand : IExternalCommand
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

            var sel = RevitUtils.SelectedElements(uidoc);

            // ---------------------------------------------------------
            // 1. Verificar seleção
            // ---------------------------------------------------------
            if (sel.Count != 2)
            {
                RevitUtils.Alert(
                    "Please select exactly 2 elements.",
                    "Selection Error");

                return Result.Cancelled;
            }

            // ---------------------------------------------------------
            // 2. Identificar Hanger e Pipe
            // ---------------------------------------------------------
            var hanger = sel.FirstOrDefault(
                e => e.Category?.Id.IdValue() ==
                     (long)BuiltInCategory.OST_FabricationHangers);

            var pipe = sel.FirstOrDefault(
                e => e.Category?.Id.IdValue() ==
                     (long)BuiltInCategory.OST_FabricationPipework);

            if (hanger == null || pipe == null)
            {
                RevitUtils.Alert(
                    "Selection must contain 1 Fabrication Hanger and 1 Fabrication Pipe.",
                    "Category Mismatch");

                return Result.Cancelled;
            }

            // ---------------------------------------------------------
            // 3. Verificar se é Fabrication Pipe reto
            // ---------------------------------------------------------
            var pat = pipe.GetParameter(
                ParameterTypeId.FabricationPartPatNo);

            if (pat != null && pat.AsInteger() != 2041)
            {
                RevitUtils.Alert(
                    "The selected part is not a Straight Pipe.\n" +
                    "Please select a straight segment.",
                    "Invalid Fabrication Part");

                return Result.Cancelled;
            }

            // ---------------------------------------------------------
            // 4. Verificar se já está conectado
            // ---------------------------------------------------------
            try
            {
                var existingHosted = RevitUtils.Invoke(
                    hanger,
                    "GetHostedInfo");

                if (existingHosted != null)
                {
                    var hostIdProperty =
                        existingHosted.GetType().GetProperty("HostId");

                    if (hostIdProperty != null)
                    {
                        var hostId =
                            hostIdProperty.GetValue(
                                existingHosted,
                                null) as ElementId;

                        if (hostId != null && hostId == pipe.Id)
                        {
                            RevitUtils.Alert(
                                "These elements are already connected.",
                                "Already Connected");

                            return Result.Cancelled;
                        }
                    }
                }
            }
            catch
            {
                // Se não conseguir verificar, continua.
            }

            // ---------------------------------------------------------
            // 5. Verificar Service
            // ---------------------------------------------------------
            try
            {
                var hangerService =
                    hanger.GetType().GetProperty("ServiceId");

                var pipeService =
                    pipe.GetType().GetProperty("ServiceId");

                if (hangerService != null && pipeService != null)
                {
                    var hangerServiceId =
                        hangerService.GetValue(
                            hanger,
                            null);

                    var pipeServiceId =
                        pipeService.GetValue(
                            pipe,
                            null);

                    if (hangerServiceId != null &&
                        pipeServiceId != null &&
                        !hangerServiceId.Equals(pipeServiceId))
                    {
                        RevitUtils.Alert(
                            "Different Fabrication Services detected.",
                            "Service Mismatch");

                        return Result.Cancelled;
                    }
                }
            }
            catch
            {
                // Se não conseguir verificar, continua.
            }

            // ---------------------------------------------------------
            // 6. Transaction
            // ---------------------------------------------------------
            using (var t = new Transaction(
                doc,
                "Connect Hanger to Pipe"))
            {
                t.Start();

                try
                {
                    // -------------------------------------------------
                    // Pipe geometry
                    // -------------------------------------------------
                    var curve =
                        (pipe.Location as LocationCurve)?.Curve;

                    var box =
                        hanger.get_BoundingBox(null);

                    if (curve == null || box == null)
                    {
                        throw new Exception(
                            "Could not determine pipe curve or hanger geometry.");
                    }

                    // -------------------------------------------------
                    // Centro do hanger
                    // -------------------------------------------------
                    XYZ center =
                        (box.Min + box.Max) / 2.0;

                    // -------------------------------------------------
                    // Projetar hanger no pipe
                    // -------------------------------------------------
                    var proj =
                        curve.Project(center);

                    if (proj == null)
                    {
                        throw new Exception(
                            "Could not project hanger onto pipe.");
                    }

                    XYZ target =
                        proj.XYZPoint;

                    // -------------------------------------------------
                    // Mover hanger até o pipe
                    // -------------------------------------------------
                    XYZ move =
                        new XYZ(
                            target.X - center.X,
                            target.Y - center.Y,
                            0);

                    if (move.GetLength() > 1e-9)
                    {
                        ElementTransformUtils.MoveElement(
                            doc,
                            hanger.Id,
                            move);
                    }

                    // -------------------------------------------------
                    // Obter HostedInfo
                    // -------------------------------------------------
                    var hosted =
                        RevitUtils.Invoke(
                            hanger,
                            "GetHostedInfo");

                    if (hosted == null)
                    {
                        throw new Exception(
                            "Could not obtain hanger HostedInfo.");
                    }

                    // -------------------------------------------------
                    // Obter conectores do pipe
                    // -------------------------------------------------
                    var connectors =
                        RevitUtils.GetConnectors(pipe);

                    Connector refConn =
                        connectors?
                        .Cast<Connector>()
                        .OrderBy(
                            c => c.Origin.DistanceTo(
                                curve.GetEndPoint(0)))
                        .FirstOrDefault();

                    if (refConn == null)
                    {
                        throw new Exception(
                            "Could not find a pipe connector.");
                    }

                    // -------------------------------------------------
                    // Calcular distância
                    // -------------------------------------------------
                    double dist =
                        refConn.Origin.DistanceTo(target);

                    // Limitar distância ao comprimento do pipe
                    double curveLength =
                        curve.Length;

                    if (dist < 0)
                        dist = 0;

                    if (dist >= curveLength)
                        dist = curveLength - 1e-6;

                    // -------------------------------------------------
                    // Encontrar especificamente o PlaceOnHost correto
                    //
                    // Evita:
                    // "Ambiguous match found."
                    // -------------------------------------------------
                    MethodInfo placeOnHost =
                        hosted
                        .GetType()
                        .GetMethods(
                            BindingFlags.Public |
                            BindingFlags.Instance)
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "PlaceOnHost")
                                return false;

                            var parameters =
                                m.GetParameters();

                            if (parameters.Length != 3)
                                return false;

                            return
                                typeof(ElementId).IsAssignableFrom(
                                    parameters[0].ParameterType)
                                &&
                                typeof(Connector).IsAssignableFrom(
                                    parameters[1].ParameterType)
                                &&
                                parameters[2].ParameterType ==
                                    typeof(double);
                        });

                    if (placeOnHost == null)
                    {
                        throw new Exception(
                            "Could not find the correct PlaceOnHost method.");
                    }

                    // -------------------------------------------------
                    // Executar conexão
                    // -------------------------------------------------
                    placeOnHost.Invoke(
                        hosted,
                        new object[]
                        {
                            pipe.Id,
                            refConn,
                            dist
                        });

                    // -------------------------------------------------
                    // Commit
                    // -------------------------------------------------
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();

                    string error =
                        ex.InnerException != null
                            ? ex.InnerException.Message
                            : ex.Message;

                    RevitUtils.Alert(
                        "Connection error:\n" + error,
                        "Connect Hanger");

                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        
        }
    }
}
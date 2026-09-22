using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Annotation
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Convert visible grids from 3D extents to 2D extents in the active view.",
        usage: "Select grids or run the command with grids visible in the active view.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class Grids3DTo2DCommand : IExternalCommand
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

            var doc=uiapp.ActiveUIDocument.Document;
            var view=doc.ActiveView;
            var grids=new FilteredElementCollector(doc,view.Id).OfClass(typeof(Grid)).Cast<Grid>().ToList();
            if(!grids.Any()){ RevitUtils.Alert("No Grids found in this view.","Warning"); return Result.Cancelled; }
            using(var t=new Transaction(doc,"Grids 3D to 2D"))
            {
                t.Start();
                foreach(var g in grids)
                {
                    try
                    {
                        var curve=g.GetCurvesInView(DatumExtentType.Model,view).FirstOrDefault();
                        if(curve!=null) g.SetCurveInView(DatumExtentType.ViewSpecific,view,curve);
                    } catch { }
                }
                t.Commit();
            }
            RevitUtils.Alert("Grids converted to 2D in the active view.","Done");
            return Result.Succeeded;
        
        }
    }
}
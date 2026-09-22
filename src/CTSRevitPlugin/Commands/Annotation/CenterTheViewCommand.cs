using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Annotation
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Center the active view around the current selection.",
        usage: "Select one or more elements, then run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class CenterTheViewCommand : IExternalCommand
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
            var sheet=doc.ActiveView as ViewSheet;
            if(sheet==null){RevitUtils.Alert("Open a Sheet View before running this command.","Center The View");return Result.Cancelled;}
            var vps=new FilteredElementCollector(doc,sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
            if(vps.Count==0){RevitUtils.Alert("No viewports found on this sheet.","Center The View");return Result.Cancelled;}
            var vp=vps[0];
            var tb=new FilteredElementCollector(doc,sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
            if(tb==null){RevitUtils.Alert("No Title Block found on this sheet.","Center The View");return Result.Cancelled;}
            var loc=tb.Location as LocationPoint; var w=tb.get_Parameter(BuiltInParameter.SHEET_WIDTH); var h=tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
            if(loc==null||w==null||h==null){RevitUtils.Alert("Could not determine Title Block center.","Center The View");return Result.Failed;}
            XYZ center=loc.Point+new XYZ((w.AsDouble()-0.44)/2,h.AsDouble()/2,0);
            XYZ offset=center-vp.GetBoxCenter();
            using(var t=new Transaction(doc,"Center the View")){t.Start();vp.Location.Move(offset);t.Commit();}
            return Result.Succeeded;
        
        }
    }
}
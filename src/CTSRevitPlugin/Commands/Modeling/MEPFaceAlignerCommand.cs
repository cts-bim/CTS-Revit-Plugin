using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Modeling
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Align compatible MEP fabrication faces.",
        usage: "Select the required MEP elements and follow the command prompt.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class MEPFaceAlignerCommand : IExternalCommand
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

            var uidoc=uiapp.ActiveUIDocument; var doc=uidoc.Document;
            var selected=RevitUtils.SelectedElements(uidoc);
            if(!selected.Any()){ RevitUtils.Alert("No valid Pipes or Fabrication Pipes selected.","Selection Error"); return Result.Cancelled; }
            var reference=RevitUtils.PickElement(uidoc,"Pick the reference element");
            if(reference==null) return Result.Cancelled;
            var rp=RevitUtils.RepresentativePoint(reference);
            if(rp==null){RevitUtils.Alert("Could not calculate the reference position.","Calculation Error");return Result.Failed;}
            using(var t=new Transaction(doc,"MEP Face Aligner"))
            {
                t.Start();
                foreach(var e in selected)
                {
                    var p=RevitUtils.RepresentativePoint(e); if(p==null) continue;
                    ElementTransformUtils.MoveElement(doc,e.Id,new XYZ(0,0,rp.Z-p.Z));
                }
                t.Commit();
            }
            return Result.Succeeded;
        
        }
    }
}
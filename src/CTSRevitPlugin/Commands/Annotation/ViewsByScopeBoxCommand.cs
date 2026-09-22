using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Annotation
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Create or manage views associated with the selected scope box.",
        usage: "Select a scope box and run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class ViewsByScopeBoxCommand : IExternalCommand
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
            var views=new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>!v.IsTemplate&&v.ViewType==ViewType.FloorPlan).OrderBy(v=>v.Name).ToList();
            var baseView=SimpleForms.Choose("Views By Scope Box","Select Base Floor Plan:",views,v=>v.Name);
            if(baseView==null)return Result.Cancelled;
            var scopes=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().ToElements().OrderBy(x=>x.Name).ToList();
            if(!scopes.Any()){RevitUtils.Alert("No Scope Boxes found.");return Result.Cancelled;}
            var picked=SimpleForms.ChooseMany("Views By Scope Box","Select Target Scope Boxes:",scopes,x=>x.Name);
            if(!picked.Any())return Result.Cancelled;
            var prefix=SimpleForms.AskString("View Name Prefix","Enter the prefix for the new views:","L1 - MECHANICAL PIPE LAYOUT - AREA ");
            if(prefix==null)return Result.Cancelled;
            using(var t=new Transaction(doc,"ViewsByScopeBox"))
            {
                t.Start();
                foreach(var sb in picked)
                {
                    try
                    {
                        var nv=doc.GetElement(baseView.Duplicate(ViewDuplicateOption.Duplicate));
                        try{nv.Name=prefix+sb.Name;}catch{nv.Name=prefix+sb.Name+"_Copy";}
                        var p=nv.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if(p!=null&&!p.IsReadOnly)p.Set(sb.Id);
                    }catch{}
                }
                t.Commit();
            }
            RevitUtils.Alert("Success! Views created.","Done");
            return Result.Succeeded;
        
        }
    }
}
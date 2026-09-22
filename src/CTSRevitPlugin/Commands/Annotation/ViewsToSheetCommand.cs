using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CTSRevitPlugin.Commands.Annotation
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Place selected views onto a selected sheet.",
        usage: "Select the target sheet/views as prompted, then run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class ViewsToSheetCommand : IExternalCommand
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
            var views=new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v=>(v.ViewType==ViewType.FloorPlan||v.ViewType==ViewType.CeilingPlan||v.ViewType==ViewType.Section)&&!v.IsTemplate
                && v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)!=null
                && v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER).AsString()=="---").OrderBy(v=>v.Name).ToList();
            var picked=SimpleForms.ChooseMany("Views To Sheet","Select Views:",views,v=>v.Name);
            if(!picked.Any())return Result.Cancelled;
            var prefix=SimpleForms.AskString("Sheet Number Prefix","Enter the Sheet Number prefix:","M-L2-SD-");
            if(prefix==null)return Result.Cancelled;
            var tbs=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<ElementType>().OrderBy(x=>x.Name).ToList();
            var tb=SimpleForms.Choose("Title Block","Select Title Block:",tbs,x=>x.FamilyName+" : "+x.Name);
            if(tb==null){RevitUtils.Alert("User must select a Title Block.","Warning");return Result.Cancelled;}
            var existing=new HashSet<string>(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets).WhereElementIsNotElementType().Cast<ViewSheet>().Select(s=>s.SheetNumber));
            using(var t=new Transaction(doc,"Create Sheets from Views"))
            {
                t.Start();
                foreach(var v in picked)
                {
                    var sheet=ViewSheet.Create(doc,tb.Id);
                    sheet.Name=v.Name;
                    string baseNo=prefix+ScopeName(doc,v);
                    string no=baseNo;int i=1;while(existing.Contains(no))no=baseNo+"_"+i++;
                    sheet.SheetNumber=no;existing.Add(no);
                    var inst=new FilteredElementCollector(doc,sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                    XYZ center=new XYZ(0,0,0);
                    if(inst!=null)
                    {
                        var loc=inst.Location as LocationPoint;
                        var w=inst.get_Parameter(BuiltInParameter.SHEET_WIDTH);var h=inst.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                        if(loc!=null&&w!=null&&h!=null) center=loc.Point+new XYZ((w.AsDouble()-0.44)/2,h.AsDouble()/2,0);
                    }
                    try{Viewport.Create(doc,sheet.Id,v.Id,center);}catch{}
                }
                t.Commit();
            }
            RevitUtils.Alert("Sheets created successfully!","Success");
            return Result.Succeeded;
        
        }
        static string ScopeName(Document doc,View v)
        {
            try
            {
                var id=v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP).AsElementId();
                if(id!=ElementId.InvalidElementId){var n=doc.GetElement(id).Name;return Regex.Replace(n.Trim().ToUpper(),@"^AREA[\s_\-\.]*","").Trim();}
            }catch{}
            return "General";
        }
    }
}
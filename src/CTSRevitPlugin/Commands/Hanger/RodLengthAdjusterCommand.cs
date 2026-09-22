using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Hanger
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Increase or decrease fabrication hanger rod lengths.",
        usage: "Select one or more fabrication hangers and enter a positive or negative imperial adjustment.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class RodLengthAdjusterCommand : IExternalCommand
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

            var uidoc=uiapp.ActiveUIDocument;var doc=uidoc.Document;
            var selected=RevitUtils.SelectedElements(uidoc);
            var hangers=selected.Where(e=>e.Category!=null&&e.Category.Id.IdValue()==(int)BuiltInCategory.OST_FabricationHangers).ToList();
            if(!hangers.Any()){RevitUtils.Alert("None of the selected elements belong to the MEP Fabrication Hangers category.");return Result.Cancelled;}
            var input=SimpleForms.AskString("Adjust Rod Length","Enter value to adjust (+ increase, - decrease), e.g. 1' 6\" or -6\":");
            if(string.IsNullOrWhiteSpace(input))return Result.Cancelled;
            double delta;
            if(!TryParseImperial(input,out delta)||Math.Abs(delta)<1e-12){RevitUtils.Alert("Invalid input value. Accepted examples: 1' 6\", -1' 10 5/8\", -6\".");return Result.Failed;}
            int modified=0;
            using(var t=new Transaction(doc,"Adjust Rod Length"))
            {
                t.Start();
                foreach(var h in hangers)
                {
                    try
                    {
                        var ri=RevitUtils.Invoke(h,"GetRodInfo");
                        if(ri==null)continue;
                        var countObj=ri.GetType().GetProperty("RodCount")?.GetValue(ri,null);
                        int count=countObj==null?0:Convert.ToInt32(countObj);
                        var can=ri.GetType().GetProperty("CanRodsBeHosted");
                        if(can!=null&&can.CanWrite&&Convert.ToBoolean(can.GetValue(ri,null)))can.SetValue(ri,false,null);
                        for(int i=0;i<count;i++)
                        {
                            var cur=ri.GetType().GetMethod("GetRodLength")?.Invoke(ri,new object[]{i});
                            if(cur==null)continue;
                            double newLen=Convert.ToDouble(cur)+delta;
                            if(newLen>0){ri.GetType().GetMethod("SetRodLength")?.Invoke(ri,new object[]{i,newLen});modified++;}
                        }
                    }catch{}
                }
                t.Commit();
            }
            RevitUtils.Alert($"Adjusted {modified} rod(s) on {hangers.Count} hanger(s).","Rod Length Adjuster");
            return Result.Succeeded;
        
        }
        static bool TryParseImperial(string s,out double feet)
        {
            feet=0;s=s.Trim();bool neg=s.StartsWith("-");if(neg||s.StartsWith("+"))s=s.Substring(1).Trim();
            try
            {
                double f=0,inch=0;
                if(s.Contains("'")){var a=s.Split(new[]{'\''},2);if(!string.IsNullOrWhiteSpace(a[0]))f=double.Parse(a[0].Trim(),System.Globalization.CultureInfo.InvariantCulture);s=a[1];}
                s=s.Replace("\"","").Trim();
                if(s.Length>0)
                {
                    var parts=s.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                    foreach(var part in parts) inch+=part.Contains("/")?double.Parse(part.Split('/')[0])/double.Parse(part.Split('/')[1]):double.Parse(part,System.Globalization.CultureInfo.InvariantCulture);
                }
                feet=f+inch/12.0;if(neg)feet=-feet;return true;
            }catch{return false;}
        }
    }
}
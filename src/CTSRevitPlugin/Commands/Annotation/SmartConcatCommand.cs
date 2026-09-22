using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Annotation
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Concatenate selected text values using CTS smart matching rules.",
        usage: "Select the elements/values to concatenate and run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class SmartConcatCommand : IExternalCommand
    {
        static readonly Dictionary<string, BuiltInCategory> Categories =
            new Dictionary<string, BuiltInCategory>
            {
                {"Pipe Accessories", BuiltInCategory.OST_PipeAccessory},
                {"Fabrication Hangers", BuiltInCategory.OST_FabricationHangers},
                {"Pipes", BuiltInCategory.OST_PipeCurves},
                {"Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment},
                {"MEP Fabrication Pipework", BuiltInCategory.OST_FabricationPipework}
            };

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

            var doc = uiapp.ActiveUIDocument.Document;

            string category;
            string scope;
            int count;

            if (!FirstForm(out category, out scope, out count))
                return Result.Cancelled;

            var bic = Categories[category];

            var elems =
                (scope == "Active view"
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc))
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (!elems.Any())
            {
                RevitUtils.Alert(
                    $"The selected category has no elements in the chosen scope ({scope})."
                );

                return Result.Cancelled;
            }

            var all = GetParameters(doc, elems);
            var editable = GetEditable(doc, elems);

            if (!all.Any() || !editable.Any())
            {
                RevitUtils.Alert(
                    "No usable parameters were found for this category."
                );

                return Result.Cancelled;
            }

            var selections = new List<ParamChoice>();

            for (int i = 0; i < count; i++)
            {
                ParamChoice c;

                if (!ParameterForm(
                    all.Keys.OrderBy(x => x).ToList(),
                    i + 1,
                    out c))
                {
                    return Result.Cancelled;
                }

                selections.Add(c);
            }

            string dest = SimpleForms.Choose(
                "Smart Concat",
                "Parameter to set:",
                editable.Keys.OrderBy(x => x).ToList(),
                x => x
            );

            if (string.IsNullOrEmpty(dest))
                return Result.Cancelled;

            using (var t = new Transaction(doc, "SmartConcat"))
            {
                t.Start();

                foreach (var elem in elems)
                {
                    string final = "";

                    foreach (var c in selections)
                    {
                        string val =
                            GetValue(
                                doc,
                                elem,
                                all[c.ParameterName]
                            );

                        final += c.Prefix + val + c.Suffix;
                    }

                    var p =
                        FindParameter(
                            doc,
                            elem,
                            editable[dest]
                        );

                    if (p != null && !p.IsReadOnly)
                    {
                        try
                        {
                            p.Set(final);
                        }
                        catch
                        {
                        }
                    }
                }

                t.Commit();
            }

            RevitUtils.Alert(
                "Concatenation completed!",
                "Done"
            );

            return Result.Succeeded;
        
        }

        // =============================================================
        // FORMS
        //
        // Both dialogs used to be raw System.Windows.Forms, which is why this
        // tool showed default Windows chrome while every other CTS tool used
        // the themed WPF look. They are now plain SimpleForms field dialogs,
        // so they follow the Revit light/dark theme like the rest of the plugin.
        // =============================================================

        static bool FirstForm(
            out string category,
            out string scope,
            out int count)
        {
            category = null;
            scope = null;
            count = 0;

            var categoryField = SimpleForms.FormField.Choice(
                "CATEGORY",
                Categories.Keys.OrderBy(x => x).ToList());

            var scopeField = SimpleForms.FormField.Choice(
                "SCOPE",
                new List<string> { "Active view", "Entire project" });

            var countField = SimpleForms.FormField.Choice(
                "NUMBER OF PARAMETERS",
                new List<string> { "2", "3", "4", "5" });

            var fields = new List<SimpleForms.FormField>
            {
                categoryField,
                scopeField,
                countField
            };

            if (!SimpleForms.AskFields(
                "Smart Concat",
                "Choose what to concatenate and how many parameters to combine.",
                fields,
                "CONTINUE"))
            {
                return false;
            }

            category = categoryField.Value;
            scope = scopeField.Value;

            if (!int.TryParse(countField.Value, out count) || count <= 0)
                return false;

            return true;
        }

        static bool ParameterForm(
            List<string> names,
            int n,
            out ParamChoice result)
        {
            result = null;

            var prefixField = SimpleForms.FormField.Text("PREFIX");
            var parameterField = SimpleForms.FormField.Choice("PARAMETER", names);
            var suffixField = SimpleForms.FormField.Text("SUFFIX");

            var fields = new List<SimpleForms.FormField>
            {
                prefixField,
                parameterField,
                suffixField
            };

            if (!SimpleForms.AskFields(
                "Smart Concat - Parameter " + n,
                "Value " + n + " of the concatenation. Prefix and suffix are optional.",
                fields))
            {
                return false;
            }

            result = new ParamChoice
            {
                Prefix = prefixField.Value ?? "",
                ParameterName = parameterField.Value,
                Suffix = suffixField.Value ?? ""
            };

            return !string.IsNullOrWhiteSpace(result.ParameterName);
        }

        class ParamChoice
        {
            public string Prefix;
            public string ParameterName;
            public string Suffix;
        }

        static Dictionary<string, ElementId> GetParameters(
            Document doc,
            List<Element> elems)
        {
            var d =
                new Dictionary<string, ElementId>();

            foreach (
                var e in elems
                    .GroupBy(x => x.GetTypeId().IdValue())
                    .Select(g => g.First()))
            {
                foreach (Parameter p in e.Parameters)
                    Add(d, p);

                var type = doc.GetElement(e.GetTypeId());

                if (type != null)
                {
                    foreach (Parameter p in type.Parameters)
                        Add(d, p);
                }
            }

            return d;
        }

        static Dictionary<string, ElementId> GetEditable(
            Document doc,
            List<Element> elems)
        {
            var d =
                new Dictionary<string, ElementId>();

            foreach (
                var e in elems
                    .GroupBy(x => x.GetTypeId().IdValue())
                    .Select(g => g.First()))
            {
                foreach (Parameter p in e.Parameters)
                {
                    if (
                        p.Definition != null &&
                        !p.IsReadOnly &&
                        p.StorageType == StorageType.String)
                    {
                        Add(d, p);
                    }
                }
            }

            return d;
        }

        static void Add(
            Dictionary<string, ElementId> d,
            Parameter p)
        {
            if (
                p?.Definition != null &&
                !d.ContainsKey(p.Definition.Name))
            {
                d.Add(
                    p.Definition.Name,
                    p.Id
                );
            }
        }

        static Parameter FindParameter(
            Document doc,
            Element e,
            ElementId id)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p.Id == id)
                    return p;
            }

            var type = doc.GetElement(e.GetTypeId());

            if (type != null)
            {
                foreach (Parameter p in type.Parameters)
                {
                    if (p.Id == id)
                        return p;
                }
            }

            return null;
        }

        static string GetValue(
            Document doc,
            Element e,
            ElementId id)
        {
            var p = FindParameter(doc, e, id);

            if (p == null)
                return "";

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.AsString() ?? "";

                var s = p.AsValueString();

                if (!string.IsNullOrEmpty(s))
                    return s;

                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger().ToString();

                if (p.StorageType == StorageType.Double)
                    return p.AsDouble().ToString();
            }
            catch
            {
            }

            return "";
        }
    }
}
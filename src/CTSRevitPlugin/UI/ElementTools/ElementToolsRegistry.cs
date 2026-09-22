using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.UI.ElementTools
{
    public class ElementToolDefinition
    {
        public string Id { get; }

        public string Name { get; }

        public string Category { get; }

        public string Description { get; private set; }

        public string Usage { get; private set; }

        public string Author { get; private set; }

        public string Version { get; private set; }

        public string Notes { get; private set; }

        public string CommandClassName { get; }

        public ElementToolDefinition(
            string id,
            string name,
            string category,
            string commandClassName)
        {
            Id = id;
            Name = name;
            Category = category;
            CommandClassName = commandClassName;

            LoadDocumentation();
        }

        private void LoadDocumentation()
        {
            Type commandType =
                FindCommandType(CommandClassName);

            if (commandType == null)
            {
                Description =
                    "No documentation available.";

                Usage =
                    "Run the tool from Element Tools.";

                Author =
                    "Unknown";

                Version =
                    "Unknown";

                Notes =
                    string.Empty;

                return;
            }

            ToolDocumentationAttribute documentation =
                commandType
                    .GetCustomAttributes(
                        typeof(ToolDocumentationAttribute),
                        false)
                    .FirstOrDefault()
                    as ToolDocumentationAttribute;

            if (documentation == null)
            {
                Description =
                    "No documentation available.";

                Usage =
                    "Run the tool from Element Tools.";

                Author =
                    "Unknown";

                Version =
                    "Unknown";

                Notes =
                    string.Empty;

                return;
            }

            Description =
                documentation.Description;

            Usage =
                documentation.Usage;

            Author =
                documentation.Author;

            Version =
                documentation.Version;

            Notes =
                documentation.Notes;
        }

        private static Type FindCommandType(
            string fullClassName)
        {
            if (string.IsNullOrWhiteSpace(fullClassName))
                return null;

            foreach (
                Assembly assembly
                in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type =
                        assembly.GetType(
                            fullClassName,
                            false);

                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore assemblies that cannot be inspected.
                }
            }

            return null;
        }
    }


    public static class ElementToolsRegistry
    {
        private static readonly List<ElementToolDefinition> _tools =
            new List<ElementToolDefinition>
            {
                // =========================
                // ANNOTATION
                // =========================

                new ElementToolDefinition(
                    "SmartConcat",
                    "Smart Concat",
                    "Annotation",
                    "CTSRevitPlugin.Commands.Annotation.SmartConcatCommand"
                ),

                new ElementToolDefinition(
                    "ViewsByScopeBox",
                    "Views By Scope Box",
                    "Annotation",
                    "CTSRevitPlugin.Commands.Annotation.ViewsByScopeBoxCommand"
                ),

                new ElementToolDefinition(
                    "CenterTheView",
                    "Center The View",
                    "Annotation",
                    "CTSRevitPlugin.Commands.Annotation.CenterTheViewCommand"
                ),

                new ElementToolDefinition(
                    "Grids3DTo2D",
                    "Grids 3D to 2D",
                    "Annotation",
                    "CTSRevitPlugin.Commands.Annotation.Grids3DTo2DCommand"
                ),

                new ElementToolDefinition(
                    "ViewsToSheet",
                    "Views To Sheet",
                    "Annotation",
                    "CTSRevitPlugin.Commands.Annotation.ViewsToSheetCommand"
                ),


                new ElementToolDefinition(
                    "FormatPainter",
                    "Format Painter",
                    "Utilities",
                    "CTSRevitPlugin.Commands.Utilities.FormatPainterCommand"
                ),

                // =========================
                // HANGER
                // =========================

                new ElementToolDefinition(
                    "ConnectHanger",
                    "Connect Hanger",
                    "Hanger",
                    "CTSRevitPlugin.Commands.Hanger.ConnectHangerCommand"
                ),

                new ElementToolDefinition(
                    "HangerWithoutHost",
                    "Hanger Without Host",
                    "Hanger",
                    "CTSRevitPlugin.Commands.Hanger.HangerWithoutHostCommand"
                ),

                new ElementToolDefinition(
                    "RodLengthAdjuster",
                    "Rod Length Adjuster",
                    "Hanger",
                    "CTSRevitPlugin.Commands.Hanger.RodLengthAdjusterCommand"
                ),

                new ElementToolDefinition(
                    "RoundStrutChannel",
                    "Round Strut Channel",
                    "Hanger",
                    "CTSRevitPlugin.Commands.Hanger.RoundStrutChannelCommand"
                ),


                // =========================
                // MODELING
                // =========================

                new ElementToolDefinition(
                    "FlipElements",
                    "Flip Elements",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.FlipElementsCommand"
                ),

                new ElementToolDefinition(
                    "MEPFaceAligner",
                    "MEP Face Aligner",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.MEPFaceAlignerCommand"
                ),

                new ElementToolDefinition(
                    "Rotate90CW",
                    "Rotate 90° CW",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.Rotate90CWCommand"
                ),

                new ElementToolDefinition(
                    "AlignByX",
                    "Align X",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.AlignByXCommand"
                ),

                new ElementToolDefinition(
                    "AlignByY",
                    "Align Y",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.AlignByYCommand"
                ),

                new ElementToolDefinition(
                    "AlignByZ",
                    "Align Z",
                    "Modeling",
                    "CTSRevitPlugin.Commands.Modeling.AlignByZCommand"
                ),


                // =========================
                // QUALITY CONTROL
                // =========================

                new ElementToolDefinition(
                    "DuplicateFinder",
                    "Duplicate Finder",
                    "QC",
                    "CTSRevitPlugin.Commands.QC.DuplicateFinderCommand"
                ),

                new ElementToolDefinition(
                    "ParameterCleaner",
                    "Parameter Cleaner",
                    "QC",
                    "CTSRevitPlugin.Commands.QC.ParameterCleanerCommand"
                )
            };


        public static IReadOnlyList<ElementToolDefinition> All
        {
            get
            {
                return _tools;
            }
        }


        public static ElementToolDefinition GetById(
            string id)
        {
            return _tools.FirstOrDefault(
                x => x.Id == id);
        }
    }
}
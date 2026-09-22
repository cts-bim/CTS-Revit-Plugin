using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using CTSRevitPlugin.Commands.Annotation;
using CTSRevitPlugin.Commands.Hanger;
using CTSRevitPlugin.Commands.Modeling;
using CTSRevitPlugin.Commands.QC;
using CTSRevitPlugin.Commands.Utilities;

namespace CTSRevitPlugin.Utilities
{
    /// <summary>
    /// Runs a CTS tool by its tool id.
    ///
    /// The dockable panes used to fire their buttons with
    /// UIApplication.PostCommand(RevitCommandId.LookupCommandId("CustomCtrl_%...")),
    /// rebuilding the ribbon control id from the tab, panel and command class name.
    /// That id is only correct for a button sitting directly on a panel: anything
    /// nested inside a SplitButton or PulldownButton (Hanger Without Host, Connect
    /// Hanger, Rod Length Adjuster, Struc Channel, Round Strut Channel...) gets an
    /// extra nesting level from Revit, so the lookup returned null and the pane
    /// reported "Could not find Revit command for ...".
    ///
    /// Every command now exposes a static Run(UIApplication), so the panes call the
    /// tool directly. An IExternalEventHandler already executes inside a valid Revit
    /// API context, which is all Run needs. No ribbon ids, nothing to keep in sync
    /// with the layout of App.cs, and it behaves the same on 2023 - 2026.
    /// </summary>
    public static class CtsToolRunner
    {
        private static readonly Dictionary<string, Func<UIApplication, Result>> Tools =
            new Dictionary<string, Func<UIApplication, Result>>(StringComparer.OrdinalIgnoreCase)
            {
                // Annotation
                { "SmartConcat",        SmartConcatCommand.Run },
                { "ViewsByScopeBox",    ViewsByScopeBoxCommand.Run },
                { "ViewsToSheet",       ViewsToSheetCommand.Run },
                { "Grids3DTo2D",        Grids3DTo2DCommand.Run },
                { "CenterTheView",      CenterTheViewCommand.Run },

                // Hanger
                { "HangerWithoutHost",  HangerWithoutHostCommand.Run },
                { "ConnectHanger",      ConnectHangerCommand.Run },
                { "RodLengthAdjuster",  RodLengthAdjusterCommand.Run },
                { "StrucChannel",       StrucChannelCommand.Run },
                { "RoundStrutChannel",  RoundStrutChannelCommand.Run },

                // Modeling
                { "FlipElements",       FlipElementsCommand.Run },
                { "MEPFaceAligner",     MEPFaceAlignerCommand.Run },
                { "Rotate90CW",         Rotate90CWCommand.Run },
                { "AlignByX",           AlignByXCommand.Run },
                { "AlignByY",           AlignByYCommand.Run },
                { "AlignByZ",           AlignByZCommand.Run },

                // QC
                { "DuplicateFinder",    DuplicateFinderCommand.Run },
                { "ParameterCleaner",   ParameterCleanerCommand.Run },

                // Utilities
                { "FormatPainter",      FormatPainterCommand.Run },
            };

        public static bool IsKnown(string toolId)
        {
            return !string.IsNullOrWhiteSpace(toolId) && Tools.ContainsKey(toolId);
        }

        /// <summary>
        /// Runs the tool. Returns false only when the id is unknown or the tool threw;
        /// <paramref name="error"/> then carries a message fit for a pane status line.
        /// </summary>
        public static bool TryRun(UIApplication application, string toolId, out string error)
        {
            error = null;

            if (application == null)
            {
                error = "Revit is not ready.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(toolId))
            {
                error = "No tool was requested.";
                return false;
            }

            Func<UIApplication, Result> run;
            if (!Tools.TryGetValue(toolId, out run))
            {
                error = "Tool not available from this pane: " + toolId;
                return false;
            }

            try
            {
                run(application);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // The user pressed ESC during a pick. Not an error.
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.ElementTools;
using CTSRevitPlugin.UI.ElementFilter;
using CTSRevitPlugin.UI.Utilities;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin
{
    public class App : IExternalApplication
    {
#if REVIT2024_OR_GREATER
        private EventHandler<Autodesk.Revit.UI.Events.ThemeChangedEventArgs> _themeChangedHandler;
#endif

        // The panes cache the last selection so a hidden pane can catch up when
        // it is shown again. That cache must not outlive the model it came from:
        // a Document wrapper stays non-null after Revit has torn the document
        // down, and touching it later is an access violation, not an exception.
        private EventHandler<Autodesk.Revit.DB.Events.DocumentClosingEventArgs> _documentClosingHandler;

        public Result OnStartup(UIControlledApplication application)
        {
            // First, before any pane exists. Records the exceptions that Revit
            // would otherwise report as a bare "ExceptionCode=0xe0434352" in the
            // journal, and keeps the session alive when the fault is ours.
            // Log: %LOCALAPPDATA%\CTSRevitPlugin\crash.log
            CtsCrashGuard.Install();

            const string tab = "CTS Tools";

            try { application.CreateRibbonTab(tab); } catch { }

            // =========================================================
            // ANNOTATION
            // Smart Concat + Views By Scope Box pulldown
            // =========================================================
            CreateAnnotationPanel(application, tab);

            // =========================================================
            // HANGER
            // Two SplitButtons: HOST and ADJUSTER
            // =========================================================
            CreateHangerPanel(application, tab);

            // =========================================================
            // MODELING
            // Three large tools + three stacked alignment tools.
            // =========================================================
            CreateModelingPanel(application, tab);

            // =========================================================
            // QC
            // =========================================================
            CreatePanel(application, tab, "QC", new[]
            {
                ("Duplicate Finder", "CTSRevitPlugin.Commands.QC.DuplicateFinderCommand", "DuplicateFinder.png")
            });

            // =========================================================
            // UTILITIES
            // Format Painter + Utilities pulldown live at the right side.
            // =========================================================
            CreateUtilitiesPanel(application, tab);

            // =========================================================
            // SUPPORT
            // =========================================================
            CreateSupportPanel(application, tab);

            // =========================================================
            // DOCKABLE PANES
            // =========================================================
            ParametersPane parametersPane = new ParametersPane();
            ShortcutsPane shortcutsPane = new ShortcutsPane();
            HangersPane hangersPane = new HangersPane();
            MechanicalPropertiesPane mechanicalPane = new MechanicalPropertiesPane();
            ElementFilterPane elementFilterPane = new ElementFilterPane();
            CTSRevitPlugin.UI.Assemblies.AssembliesPane assembliesPane = new CTSRevitPlugin.UI.Assemblies.AssembliesPane();

            application.RegisterDockablePane(ParametersPane.PaneId, "CTS Parameters", parametersPane);
            application.RegisterDockablePane(ShortcutsPane.PaneId, "CTS Shortcuts", shortcutsPane);
            application.RegisterDockablePane(HangersPane.PaneId, "CTS Hangers", hangersPane);
            application.RegisterDockablePane(MechanicalPropertiesPane.PaneId, "CTS Mechanical Properties", mechanicalPane);
            application.RegisterDockablePane(ElementFilterPane.PaneId, "CTS Element Filter", elementFilterPane);
            application.RegisterDockablePane(CTSRevitPlugin.UI.Assemblies.AssembliesPane.PaneId, "CTS Assemblies", assembliesPane);

            application.SelectionChanged += ParametersPane.OnSelectionChanged;
            application.SelectionChanged += ShortcutsPane.OnSelectionChanged;
            application.SelectionChanged += HangersPane.OnSelectionChanged;
            application.SelectionChanged += MechanicalPropertiesPane.OnSelectionChanged;

            _documentClosingHandler = delegate(object sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
            {
                if (ParametersPane.Instance != null)
                    ParametersPane.Instance.ClearCachedSelection();

                if (MechanicalPropertiesPane.Instance != null)
                    MechanicalPropertiesPane.Instance.ClearCachedSelection();
            };
            application.ControlledApplication.DocumentClosing += _documentClosingHandler;

#if REVIT2024_OR_GREATER
            // Theme follows Revit automatically (the UI theme API exists from Revit 2024).
            _themeChangedHandler = delegate(object sender, Autodesk.Revit.UI.Events.ThemeChangedEventArgs e)
            {
                RevitThemeService.ApplyToAll();
            };
            application.ThemeChanged += _themeChangedHandler;
#endif
            RevitThemeService.ApplyToAll();

            return Result.Succeeded;
        }


        // ============================================================
        // A NOTE ON BUTTON CAPTIONS
        //
        // A large Revit ribbon button gives its caption two rows, and the
        // dropdown arrow competes with the text for them:
        //   one line of text  -> the arrow takes row 2, centred UNDER the text
        //   two lines of text -> no row is left, so the arrow is drawn BESIDE
        //                        the second line
        //
        // Revit exposes no way to place the arrow anywhere else, so wrapping the
        // caption and having the arrow underneath are mutually exclusive.
        // Wrapping won: the "\n" below are deliberate, and the arrow sitting
        // beside the last word is the accepted trade-off. Do not "fix" it by
        // removing the newlines - that only makes the panels wide again.
        // ============================================================

        private void CreateHangerPanel(UIControlledApplication app, string tab)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, "Hanger");
            string assembly = Assembly.GetExecutingAssembly().Location;

            // HOST: Hanger Without Host + Connect Hanger
            SplitButtonData hostData = new SplitButtonData(
                "HangerHostSplit",
                "Host");
            SetImages(hostData, GetIconPath(assembly, "HangerWithoutHost.png"));
            hostData.ToolTip = "Hanger Host";
            hostData.LongDescription =
                "Hanger host tools: Hanger Without Host and Connect Hanger.";

            SplitButton hostSplit = panel.AddItem(hostData) as SplitButton;
            if (hostSplit != null)
            {
                PushButtonData withoutHost = CreateButtonData(
                    "Hanger Without\nHost",
                    "CTSRevitPlugin.Commands.Hanger.HangerWithoutHostCommand",
                    "HangerWithoutHost.png",
                    assembly);

                PushButtonData connect = CreateButtonData(
                    "Connect Hanger",
                    "CTSRevitPlugin.Commands.Hanger.ConnectHangerCommand",
                    "ConnectHanger.png",
                    assembly);

                hostSplit.AddPushButton(withoutHost);
                hostSplit.AddPushButton(connect);
                hostSplit.IsSynchronizedWithCurrentItem = false;
            }

            // ADJUSTER: Rod Length Adjuster + Struc Channel + Round Strut Channel
            SplitButtonData adjusterData = new SplitButtonData(
                "HangerAdjusterSplit",
                "Adjuster");
            SetImages(adjusterData, GetIconPath(assembly, "RodLengthAdjuster.png"));
            adjusterData.ToolTip = "Hanger Adjuster";
            adjusterData.LongDescription =
                "Hanger adjustment tools: Rod Length Adjuster, Struc Channel and Round Strut Channel.";

            SplitButton adjusterSplit = panel.AddItem(adjusterData) as SplitButton;
            if (adjusterSplit != null)
            {
                PushButtonData rodLength = CreateButtonData(
                    "Rod Length\nAdjuster",
                    "CTSRevitPlugin.Commands.Hanger.RodLengthAdjusterCommand",
                    "RodLengthAdjuster.png",
                    assembly);

                PushButtonData strucChannel = CreateButtonData(
                    "Struc Channel",
                    "CTSRevitPlugin.Commands.Hanger.StrucChannelCommand",
                    "RodLengthAdjuster.png",
                    assembly);

                PushButtonData roundStrut = CreateButtonData(
                    "Round Strut Channel",
                    "CTSRevitPlugin.Commands.Hanger.RoundStrutChannelCommand",
                    "RoundStrutChannel.png",
                    assembly);

                adjusterSplit.AddPushButton(rodLength);
                adjusterSplit.AddPushButton(strucChannel);
                adjusterSplit.AddPushButton(roundStrut);
                adjusterSplit.IsSynchronizedWithCurrentItem = false;
            }
        }

        private void CreateAnnotationPanel(UIControlledApplication app, string tab)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, "Annotation");
            string assembly = Assembly.GetExecutingAssembly().Location;

            PushButtonData smart = CreateButtonData(
                "Smart Concat", "CTSRevitPlugin.Commands.Annotation.SmartConcatCommand", "SmartConcat.png", assembly);
            panel.AddItem(smart);

            PulldownButtonData viewsData = new PulldownButtonData("ViewsByScopeBoxGroup", "Views By Scope\nBox");
            SetImages(viewsData, GetIconPath(assembly, "ViewsByScopeBox.png"));
            viewsData.ToolTip = "Views By Scope Box";
            viewsData.LongDescription = "Open annotation tools related to views.";
            PulldownButton views = panel.AddItem(viewsData) as PulldownButton;
            if (views != null)
            {
                AddPulldownButton(views, "Views By Scope Box", "CTSRevitPlugin.Commands.Annotation.ViewsByScopeBoxCommand", "ViewsByScopeBox.png", assembly);
                AddPulldownButton(views, "Center The View", "CTSRevitPlugin.Commands.Annotation.CenterTheViewCommand", "CenterTheView.png", assembly);
                AddPulldownButton(views, "Grids 3D to 2D", "CTSRevitPlugin.Commands.Annotation.Grids3DTo2DCommand", "Grids3DTo2D.png", assembly);
                AddPulldownButton(views, "Views To Sheet", "CTSRevitPlugin.Commands.Annotation.ViewsToSheetCommand", "ViewsToSheet.png", assembly);
            }
        }

        private void CreateModelingPanel(UIControlledApplication app, string tab)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, "Modeling");
            string assembly = Assembly.GetExecutingAssembly().Location;

            PushButtonData flip = CreateButtonData("Flip Elements", "CTSRevitPlugin.Commands.Modeling.FlipElementsCommand", "FlipElements.png", assembly);
            PushButtonData face = CreateButtonData("MEP Face\nAligner", "CTSRevitPlugin.Commands.Modeling.MEPFaceAlignerCommand", "MEPFaceAligner.png", assembly);
            PushButtonData rotate = CreateButtonData("Rotate 90° CW", "CTSRevitPlugin.Commands.Modeling.Rotate90CWCommand", "Rotate90CW.png", assembly);

            panel.AddItem(flip);
            panel.AddItem(face);
            panel.AddItem(rotate);

            PushButtonData alignX = CreateButtonData("Align By X", "CTSRevitPlugin.Commands.Modeling.AlignByXCommand", "AlignX.png", assembly);
            PushButtonData alignY = CreateButtonData("Align By Y", "CTSRevitPlugin.Commands.Modeling.AlignByYCommand", "AlignY.png", assembly);
            PushButtonData alignZ = CreateButtonData("Align By Z", "CTSRevitPlugin.Commands.Modeling.AlignByZCommand", "AlignZ.png", assembly);
            panel.AddStackedItems(alignX, alignY, alignZ);
        }

        private void CreatePanel(UIControlledApplication app, string tab, string panelName,
            (string Text, string ClassName, string Icon)[] buttons)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, panelName);
            string assembly = Assembly.GetExecutingAssembly().Location;
            foreach (var b in buttons)
                panel.AddItem(CreateButtonData(b.Text, b.ClassName, b.Icon, assembly));
        }

        private PushButtonData CreateButtonData(string text, string className, string iconFile, string assembly)
        {
            PushButtonData data = new PushButtonData(className.Replace(".", "_"), text, assembly, className);
            ApplyDocumentation(data, text, className);
            SetImages(data, GetIconPath(assembly, iconFile));
            return data;
        }

        private void CreateUtilitiesPanel(UIControlledApplication app, string tab)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, "Utilities");
            string assembly = Assembly.GetExecutingAssembly().Location;

            // Standalone utilities: Parameter Cleaner + Format Painter.
            // The Utilities pulldown is a separate ribbon item.
            panel.AddItem(CreateButtonData(
                "Parameter\nCleaner",
                "CTSRevitPlugin.Commands.QC.ParameterCleanerCommand",
                "ParameterCleaner.png",
                assembly));

            panel.AddItem(CreateButtonData(
                "Format\nPainter",
                "CTSRevitPlugin.Commands.Utilities.FormatPainterCommand",
                "FormatPainter.png",
                assembly));

            PulldownButtonData data = new PulldownButtonData("CTSUtilities", "User\nInterface");
            SetImages(data, GetIconPath(assembly, "Utilities.png"));
            data.ToolTip = "CTS User Interface";
            data.LongDescription = "Open the CTS dockable panes: Assemblies, Element Filter, Hangers, Mechanical Properties, Parameters and Shortcuts.";
            PulldownButton utilities = panel.AddItem(data) as PulldownButton;
            if (utilities == null) return;

            // Alphabetical. Keep it that way when adding a new pane.
            AddUtilityPushButton(utilities, "Assemblies", "CTSRevitPlugin.Commands.Utilities.ShowAssembliesPaneCommand", "Assemblies.png", assembly);
            AddUtilityPushButton(utilities, "Element Filter", "CTSRevitPlugin.Commands.Utilities.ShowElementFilterPaneCommand", "ElementFilter.png", assembly);
            AddUtilityPushButton(utilities, "Hangers", "CTSRevitPlugin.Commands.Utilities.ShowHangersPaneCommand", "Hangers.png", assembly);
            AddUtilityPushButton(utilities, "Mechanical Properties", "CTSRevitPlugin.Commands.Utilities.ShowMechanicalPropertiesPaneCommand", "MechanicalProperties.png", assembly);
            AddUtilityPushButton(utilities, "Parameters", "CTSRevitPlugin.Commands.Utilities.ShowParametersPaneCommand", "Parameters.png", assembly);
            AddUtilityPushButton(utilities, "Shortcuts", "CTSRevitPlugin.Commands.Utilities.ShowShortcutsPaneCommand", "Shortcuts.png", assembly);
        }

        private void CreateSupportPanel(UIControlledApplication app, string tab)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tab, "Support");
            string assembly = Assembly.GetExecutingAssembly().Location;
            PushButtonData github = new PushButtonData(
                "CTS_GitHub", "GitHub", assembly, "CTSRevitPlugin.Commands.Utilities.OpenGitHubCommand");
            github.ToolTip = "CTS Revit Plugin on GitHub";
            github.LongDescription = "Open the CTS Revit Plugin repository on GitHub.";
            SetImages(github, GetIconPath(assembly, "CTS Icon.png"));
            panel.AddItem(github);
        }

        private void AddUtilityPushButton(PulldownButton parent, string text, string className, string iconFile, string assembly)
        {
            PushButtonData data = CreateButtonData(text, className, iconFile, assembly);
            parent.AddPushButton(data);
        }

        private void AddPulldownButton(PulldownButton parent, string text, string className, string iconFile, string assembly)
        {
            PushButtonData data = CreateButtonData(text, className, iconFile, assembly);
            parent.AddPushButton(data);
        }

        private static string GetIconPath(string assembly, string iconFile)
        {
            return Path.Combine(Path.GetDirectoryName(assembly), "Resources", "Icons", iconFile);
        }

        private static void SetImages(PushButtonData data, string path)
        {
            if (data == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            data.Image = FitIcon(path, 16, 0);
            data.LargeImage = FitIcon(path, 32, 0);
        }

        private static void SetImages(PulldownButtonData data, string path)
        {
            if (data == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            data.Image = FitIcon(path, 16, 0);
            data.LargeImage = FitIcon(path, 32, 0);
        }

        private void ApplyDocumentation(PushButtonData data, string buttonText, string className)
        {
            if (data == null) return;
            string description = "CTS Revit Plugin tool.";
            string usage = "Select the required elements and run the tool.";
            string author = "Pedro Oliveira";
            string version = "1.0";
            string notes = string.Empty;

            Type commandType = FindCommandType(className);
            if (commandType != null)
            {
                ToolDocumentationAttribute documentation = commandType
                    .GetCustomAttributes(typeof(ToolDocumentationAttribute), false)
                    .FirstOrDefault() as ToolDocumentationAttribute;
                if (documentation != null)
                {
                    if (!string.IsNullOrWhiteSpace(documentation.Description)) description = documentation.Description;
                    if (!string.IsNullOrWhiteSpace(documentation.Usage)) usage = documentation.Usage;
                    if (!string.IsNullOrWhiteSpace(documentation.Author)) author = documentation.Author;
                    if (!string.IsNullOrWhiteSpace(documentation.Version)) version = documentation.Version;
                    if (!string.IsNullOrWhiteSpace(documentation.Notes)) notes = documentation.Notes;
                }
            }

            // Keep the hover clean: Revit already displays the button name above the tooltip.
            data.ToolTip = description;
            string longDescription = "Description\n" + description +
                "\n\nHow to use\n" + usage +
                "\n\nAuthor\n" + author +
                "\n\nVersion\n" + version;
            if (!string.IsNullOrWhiteSpace(notes)) longDescription += "\n\nNotes\n" + notes;
            data.LongDescription = longDescription;
        }

        private static Type FindCommandType(string fullClassName)
        {
            if (string.IsNullOrWhiteSpace(fullClassName)) return null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullClassName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static BitmapSource FitIcon(string path, int canvasSize, int padding)
        {
            BitmapImage source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(path, UriKind.Absolute);
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            source.Freeze();

            double maxSize = canvasSize - (padding * 2);
            double scale = Math.Min(maxSize / source.PixelWidth, maxSize / source.PixelHeight);
            double width = source.PixelWidth * scale;
            double height = source.PixelHeight * scale;
            double x = (canvasSize - width) / 2.0;
            double y = (canvasSize - height) / 2.0;

            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
                dc.DrawImage(source, new Rect(x, y, width, height));

            RenderTargetBitmap result = new RenderTargetBitmap(canvasSize, canvasSize, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
#if REVIT2024_OR_GREATER
            if (_themeChangedHandler != null)
            {
                application.ThemeChanged -= _themeChangedHandler;
                _themeChangedHandler = null;
            }
#endif

            if (_documentClosingHandler != null)
            {
                application.ControlledApplication.DocumentClosing -= _documentClosingHandler;
                _documentClosingHandler = null;
            }

            application.SelectionChanged -= ParametersPane.OnSelectionChanged;
            application.SelectionChanged -= ShortcutsPane.OnSelectionChanged;
            application.SelectionChanged -= HangersPane.OnSelectionChanged;
            application.SelectionChanged -= MechanicalPropertiesPane.OnSelectionChanged;

            if (ParametersPane.Instance != null) ParametersPane.Instance.DisposeExternalEvent();
            if (ShortcutsPane.Instance != null) ShortcutsPane.Instance.DisposeExternalEvent();
            if (HangersPane.Instance != null) HangersPane.Instance.DisposeExternalEvent();
            if (MechanicalPropertiesPane.Instance != null) MechanicalPropertiesPane.Instance.DisposeExternalEvent();
            if (ElementFilterPane.Instance != null) ElementFilterPane.Instance.DisposeExternalEvent();
            if (CTSRevitPlugin.UI.Assemblies.AssembliesPane.Instance != null) CTSRevitPlugin.UI.Assemblies.AssembliesPane.Instance.DisposeController();

            CtsCrashGuard.Uninstall();

            return Result.Succeeded;
        }
    }
}

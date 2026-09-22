using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CTSRevitPlugin.UI.ElementTools;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class ShortcutsPane : Page, IDockablePaneProvider, IUtilitiesPaneHost
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("9B3E4D2A-2D7A-4D91-A2B5-61C8F0E73402"));

        public static ShortcutsPane Instance { get; private set; }
        private UtilitiesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;
        private List<ShortcutDefinition> _catalog = new List<ShortcutDefinition>();

        public ShortcutsPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();
            _handler = new UtilitiesExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _handler.Pane = this;
            BuildTools();
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(ShortcutsPane).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterCtsLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterMotto, Path.Combine(root, "No bull Just build.png"));
        }

        private static void SetImage(Image image, string path)
        {
            if (image == null || !File.Exists(path)) return;
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            }
            catch { }
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            data.VisibleByDefault = false;
        }

        public static void OnSelectionChanged(object sender, Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            // An exception escaping a Revit event handler terminates the process,
            // so nothing here is left unguarded.
            try
            {
                if (Instance != null) Instance.SetToolStatus("Ready");
            }
            catch { }
        }

        public void RefreshFromUIApplication(UIApplication uiapp)
        {
            if (uiapp != null)
            {
                try { _catalog = BuildCatalog(uiapp); }
                catch { }
            }
            BuildTools();
            SetToolStatus("Ready · " + _catalog.Count + " commands available");
        }

        /// <summary>
        /// Fenced off because every caller is a WPF handler. An exception that
        /// escapes a WPF handler inside Revit is not shown to anyone - it
        /// terminates the process (journal: ExceptionCode=0xe0434352). A failure
        /// here becomes a status line instead.
        /// </summary>
        private void BuildTools()
        {
            try { BuildToolsCore(); }
            catch (Exception ex)
            {
                try { SetToolStatus("Could not refresh: " + ex.Message); }
                catch { }
            }
        }

        private void BuildToolsCore()
        {
            ToolsPanel.Children.Clear();
            ElementToolsConfiguration config = UtilitiesConfigurationStore.Current;
            List<string> selected = config.ShortcutTools ?? new List<string>();

            List<ShortcutDefinition> visible = _catalog.Count > 0
                ? _catalog.Where(x => selected.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToList()
                : BuildCtsOnlyCatalog().Where(x => selected.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToList();

            foreach (ShortcutDefinition definition in visible)
            {
                Button button = new Button
                {
                    Tag = definition,
                    ToolTip = BuildTooltip(definition),
                    Style = RevitThemeService.StyleOrNull(this, "ToolButtonStyle")
                };

                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
                Image image = new Image { Width = 22, Height = 22, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 9, 0) };
                image.Source = definition.Icon ?? LoadFallbackIcon();
                content.Children.Add(image);
                content.Children.Add(new TextBlock
                {
                    Text = definition.Name,
                    Foreground = RevitThemeService.ThemeBrush(this, "TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                button.Content = content;
                button.Click += ToolButton_Click;
                ToolsPanel.Children.Add(button);
            }
            ApplyFilter();
        }

        private static string BuildTooltip(ShortcutDefinition definition)
        {
            if (definition == null) return "";
            if (definition.IsCts)
            {
                ElementToolDefinition tool = ElementToolsRegistry.GetById(definition.CtsToolId);
                if (tool != null)
                    return "Description\n" + tool.Description + "\n\nHow to use\n" + tool.Usage;
            }
            return definition.Description ?? (definition.IsNative ? "Run this native Revit command." : "Run this add-in command.");
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ShortcutDefinition definition = button == null ? null : button.Tag as ShortcutDefinition;
            if (definition == null) return;

            if (definition.IsCts)
            {
                _handler.RequestTool(definition.CtsToolId);
            }
            else if (definition.IsNative)
            {
                _handler.RequestNativeShortcut(definition.NativeCommandName);
            }
            else
            {
                _handler.RequestExternalShortcut(definition.CommandCandidates);
            }

            try { _externalEvent.Raise(); SetToolStatus("Running " + definition.Name + "..."); }
            catch (Exception ex) { SetToolStatus("Could not run command: " + ex.Message); }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { ApplyFilter(); }

        private void ApplyFilter()
        {
            string search = SearchBox == null ? "" : SearchBox.Text.Trim();
            foreach (UIElement child in ToolsPanel.Children)
            {
                Button button = child as Button;
                if (button == null) continue;
                ShortcutDefinition definition = button.Tag as ShortcutDefinition;
                string name = definition == null ? "" : definition.Name;
                button.Visibility = string.IsNullOrWhiteSpace(search) || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ElementToolsSettings settings = new ElementToolsSettings(
                UtilitiesConfigurationStore.Current,
                new List<string>(),
                2,
                ApplyConfiguration,
                _catalog);
            settings.Owner = Window.GetWindow(this);
            settings.ShowDialog();
        }

        private void ApplyConfiguration(ElementToolsConfiguration configuration)
        {
            UtilitiesConfigurationStore.Apply(configuration);
            BuildTools();
        }

        public void SetToolStatus(string message)
        {
            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? ""; }));
        }

        public void SetHangerAdjustmentStatus(string message) { SetToolStatus(message); }

        public void DisposeExternalEvent()
        {
            try { if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; } } catch { }
        }

        private static List<ShortcutDefinition> BuildCtsOnlyCatalog()
        {
            return ElementToolsRegistry.All
                .Where(x => !string.Equals(x.Category, "Hanger", StringComparison.OrdinalIgnoreCase))
                .Select(x => new ShortcutDefinition
                {
                    Key = x.Id,
                    Name = x.Name,
                    Group = "CTS",
                    CtsToolId = x.Id,
                    Icon = LoadIcon(x.Id),
                    Description = x.Description
                }).ToList();
        }

        private static List<ShortcutDefinition> BuildCatalog(UIApplication uiapp)
        {
            List<ShortcutDefinition> result = BuildCtsOnlyCatalog();

            // Native Revit commands: these are the same commands exposed to the
            // PostCommand API. Only commands that resolve to a valid RevitCommandId
            // are included.
            foreach (PostableCommand command in Enum.GetValues(typeof(PostableCommand)))
            {
                try
                {
                    RevitCommandId id = RevitCommandId.LookupPostableCommandId(command);
                    if (id == null) continue;
                    string name = SplitWords(command.ToString());
                    result.Add(new ShortcutDefinition
                    {
                        Key = "native:" + command,
                        Name = name,
                        Group = "Revit",
                        NativeCommandName = command.ToString(),
                        Icon = LoadIcon("Shortcuts"),
                        Description = "Native Revit command."
                    });
                }
                catch { }
            }

            // External add-in buttons exposed through Revit's ribbon API.
            List<Tuple<string, RibbonPanel>> panels = new List<Tuple<string, RibbonPanel>>();
            try
            {
                foreach (RibbonPanel panel in uiapp.GetRibbonPanels())
                    panels.Add(Tuple.Create("Add-Ins", panel));
            }
            catch { }
            try
            {
                foreach (RibbonPanel panel in uiapp.GetRibbonPanels("CTS Tools"))
                    panels.Add(Tuple.Create("CTS Tools", panel));
            }
            catch { }
            try
            {
                foreach (Tab tab in Enum.GetValues(typeof(Tab)))
                {
                    foreach (RibbonPanel panel in uiapp.GetRibbonPanels(tab))
                        panels.Add(Tuple.Create(tab.ToString(), panel));
                }
            }
            catch { }

            foreach (Tuple<string, RibbonPanel> panelInfo in panels)
            {
                string tabName = panelInfo.Item1;
                RibbonPanel panel = panelInfo.Item2;
                if (panel == null) continue;
                foreach (RibbonItem item in panel.GetItems())
                {
                    PushButton button = item as PushButton;
                    if (button == null) continue;
                    if (string.IsNullOrWhiteSpace(button.Name) || string.IsNullOrWhiteSpace(button.ItemText)) continue;
                    if (button.Name.StartsWith("CTS_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(button.AssemblyName, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) continue;

                    string commandName = BuildExternalCommandIdCandidates(tabName, panel.Title, button.Name).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(commandName)) continue;

                    List<string> candidates = BuildExternalCommandIdCandidates(tabName, panel.Title, button.Name);
                    bool exists = false;
                    foreach (string candidate in candidates)
                    {
                        try { if (RevitCommandId.LookupCommandId(candidate) != null) { exists = true; break; } }
                        catch { }
                    }
                    if (!exists) continue;

                    result.Add(new ShortcutDefinition
                    {
                        Key = "external:" + candidates[0],
                        Name = button.ItemText,
                        Group = panel.Title,
                        CommandCandidates = candidates,
                        Icon = button.Image ?? button.LargeImage ?? LoadIcon("Shortcuts"),
                        Description = "External add-in command from the Revit ribbon."
                    });
                }
            }

            return result
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Group)
                .ThenBy(x => x.Name)
                .ToList();
        }

        private static List<string> BuildExternalCommandIdCandidates(string tab, string panel, string itemName)
        {
            string p = panel ?? "";
            string n = itemName ?? "";
            return new List<string>
            {
                "CustomCtrl_%CustomCtrl_%" + (string.IsNullOrWhiteSpace(tab) ? "Add-Ins" : tab) + "%" + p + "%" + n,
                "CustomCtrl_%" + (string.IsNullOrWhiteSpace(tab) ? "Add-Ins" : tab) + "%" + p + "%" + n,
                "CustomCtrl_%CustomCtrl_%" + p + "%" + n
            };
        }

        private static string SplitWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return Regex.Replace(value, "(?<!^)([A-Z])", " $1").Replace("_", " ").Trim();
        }

        private static ImageSource LoadIcon(string id)
        {
            string file = id + ".png";
            switch (id)
            {
                case "SmartConcat": file = "SmartConcat.png"; break;
                case "ViewsByScopeBox": file = "ViewsByScopeBox.png"; break;
                case "CenterTheView": file = "CenterTheView.png"; break;
                case "Grids3DTo2D": file = "Grids3DTo2D.png"; break;
                case "ViewsToSheet": file = "ViewsToSheet.png"; break;
                case "FormatPainter": file = "FormatPainter.png"; break;
                default: file = "Shortcuts.png"; break;
            }
            string path = Path.Combine(Path.GetDirectoryName(typeof(ShortcutsPane).Assembly.Location), "Resources", "Icons", file);
            if (!File.Exists(path)) return null;
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private static ImageSource LoadFallbackIcon() { return LoadIcon("Shortcuts"); }
    }
}

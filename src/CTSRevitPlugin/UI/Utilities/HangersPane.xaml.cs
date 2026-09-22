using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CTSRevitPlugin.UI.ElementTools;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class HangersPane : Page, IDockablePaneProvider, IUtilitiesPaneHost
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("7C6A5B41-1F93-4C28-8A6E-35D7B9021403"));

        public static HangersPane Instance { get; private set; }
        private UtilitiesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;

        public HangersPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

            _handler = new UtilitiesExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _handler.Pane = this;

            RodAdjustmentTextBox.Text = "2\"";
            StrutAdjustmentTextBox.Text = "2\"";
            BuildTools();
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(HangersPane).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterCtsLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterMotto, Path.Combine(root, "No bull Just build.png"));
        }

        private static void SetImage(Image image, string path)
        {
            if (image == null || !File.Exists(path)) return;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            } catch { }
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            data.VisibleByDefault = false;
        }

        public static void OnSelectionChanged(object sender, Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            if (Instance == null || e == null) return;

            // Everything sits inside the try, e.GetDocument() included. Revit
            // raises this event while it is swapping documents and relaying out
            // docked panes, and an exception that escapes an event handler here
            // is not reported to anyone - it terminates Revit.
            try
            {
                // IsVisible lies for a closed dockable pane (Revit hides the host
                // window, not the WPF element), so ask Revit.
                if (!PaneVisibility.IsShown(e.GetDocument(), PaneId)) return;

                int count = e.GetSelectedElements()
                    .Select(id => e.GetDocument().GetElement(id))
                    .Count(IsFabricationHanger);
                Instance.SetHangerAdjustmentStatus(count > 0
                    ? count + " fabrication hanger(s) selected."
                    : "Select a fabrication hanger");
            } catch { }
        }

        public void RefreshFromUIApplication(UIApplication uiapp)
        {
            if (uiapp == null || uiapp.ActiveUIDocument == null)
            {
                SetHangerAdjustmentStatus("Select a fabrication hanger");
                return;
            }

            int count = uiapp.ActiveUIDocument.Selection.GetElementIds()
                .Select(id => uiapp.ActiveUIDocument.Document.GetElement(id))
                .Count(IsFabricationHanger);

            SetHangerAdjustmentStatus(count > 0
                ? count + " fabrication hanger(s) selected."
                : "Select a fabrication hanger");
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
            HostToolsPanel.Children.Clear();

            ElementToolsConfiguration config = UtilitiesConfigurationStore.Current;
            List<string> configured = config.HangerTools ?? new List<string>();

            AddHangerToolButton(
                HostToolsPanel,
                "HangerWithoutHost",
                "Hanger Without Host",
                "HangerWithoutHost.png",
                configured);

            AddHangerToolButton(
                HostToolsPanel,
                "ConnectHanger",
                "Connect Hanger",
                "ConnectHanger.png",
                configured);
        }

        private void AddHangerToolButton(
            System.Windows.Controls.Panel panel,
            string toolId,
            string fallbackName,
            string iconFile,
            IList<string> configured)
        {
            ElementToolDefinition tool = ElementToolsRegistry.GetById(toolId);
            if (tool == null) return;
            if (configured.Count > 0 && !configured.Contains(toolId)) return;

            Button button = new Button
            {
                Tag = tool.Id,
                ToolTip = BuildToolTooltip(tool),
                Style = RevitThemeService.StyleOrNull(this, "ToolButtonStyle")
            };

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Image icon = new Image
            {
                Width = 22,
                Height = 22,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0)
            };
            string root = Path.Combine(Path.GetDirectoryName(typeof(HangersPane).Assembly.Location), "Resources", "Icons");
            SetImage(icon, Path.Combine(root, iconFile));
            content.Children.Add(icon);
            content.Children.Add(new TextBlock
            {
                Text = tool.Name ?? fallbackName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = RevitThemeService.ThemeBrush(this, "TextBrush")
            });

            button.Content = content;
            button.Click += ToolButton_Click;
            panel.Children.Add(button);
        }

        private static string BuildToolTooltip(ElementToolDefinition tool)
        {
            string text = "Description\n" + tool.Description +
                          "\n\nHow to use\n" + tool.Usage +
                          "\n\nAuthor: " + tool.Author + "\nVersion: " + tool.Version;
            if (!string.IsNullOrWhiteSpace(tool.Notes)) text += "\n\nNotes\n" + tool.Notes;
            return text;
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            string toolId = button.Tag as string;
            if (string.IsNullOrWhiteSpace(toolId)) return;

            _handler.RequestTool(toolId);
            try { _externalEvent.Raise(); SetToolStatus("Running " + button.Content + "..."); }
            catch (Exception ex) { SetToolStatus("Could not run tool: " + ex.Message); }
        }

        private void DecreaseRodButton_Click(object sender, RoutedEventArgs e) { RequestRodChange(false); }
        private void IncreaseRodButton_Click(object sender, RoutedEventArgs e) { RequestRodChange(true); }

        private void RequestRodChange(bool increase)
        {
            double amount;
            if (!TryParseImperial(RodAdjustmentTextBox.Text, out amount) || amount <= 0)
            {
                SetHangerAdjustmentStatus("Invalid rod value. Example: 0.50\" or 1' 6\".");
                return;
            }

            if (increase) _handler.RequestIncrease(amount);
            else _handler.RequestDecrease(amount);

            try { _externalEvent.Raise(); SetHangerAdjustmentStatus(increase ? "Increasing rod length..." : "Decreasing rod length..."); }
            catch (Exception ex) { SetHangerAdjustmentStatus("Could not start adjustment: " + ex.Message); }
        }

        private void RoundStrutButton_Click(object sender, RoutedEventArgs e)
        {
            _handler.RequestStrutChannel(0);
            try { _externalEvent.Raise(); SetStrutAdjustmentStatus("Rounding strut channel..."); }
            catch (Exception ex) { SetStrutAdjustmentStatus("Could not start adjustment: " + ex.Message); }
        }

        private void DecreaseStrutButton_Click(object sender, RoutedEventArgs e) { RequestStrutChange(false); }
        private void IncreaseStrutButton_Click(object sender, RoutedEventArgs e) { RequestStrutChange(true); }

        private void RequestStrutChange(bool increase)
        {
            double amount;
            if (!TryParseImperial(StrutAdjustmentTextBox.Text, out amount) || amount <= 0)
            {
                SetStrutAdjustmentStatus("Invalid value. Example: 1\" or 0.50\".");
                return;
            }

            _handler.RequestStrutChannel(increase ? amount : -amount);
            try { _externalEvent.Raise(); SetStrutAdjustmentStatus(increase ? "Increasing strut channel..." : "Decreasing strut channel..."); }
            catch (Exception ex) { SetStrutAdjustmentStatus("Could not start adjustment: " + ex.Message); }
        }

        private static bool TryParseImperial(string input, out double feet)
        {
            feet = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            bool negative = s.StartsWith("-");
            if (negative || s.StartsWith("+")) s = s.Substring(1).Trim();

            try
            {
                double f = 0, inches = 0;
                if (s.Contains("'"))
                {
                    string[] parts = s.Split(new[] { '\'' }, 2);
                    if (!string.IsNullOrWhiteSpace(parts[0]))
                        f = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    s = parts.Length > 1 ? parts[1] : "";
                }

                s = s.Replace("\"", "").Trim();
                if (s.Length > 0)
                {
                    foreach (string part in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Contains("/"))
                        {
                            string[] fraction = part.Split('/');
                            if (fraction.Length != 2) return false;
                            inches += double.Parse(fraction[0], CultureInfo.InvariantCulture) /
                                       double.Parse(fraction[1], CultureInfo.InvariantCulture);
                        }
                        else inches += double.Parse(part, CultureInfo.InvariantCulture);
                    }
                }

                feet = f + inches / 12.0;
                if (negative) feet = -feet;
                return true;
            }
            catch { return false; }
        }

        private static bool IsFabricationHanger(Element element)
        {
            return element != null && element.Category != null &&
                   element.Category.Id.IdValue() == (long)BuiltInCategory.OST_FabricationHangers;
        }

        public void SetStrutAdjustmentStatus(string message)
        {
            if (Dispatcher.CheckAccess()) StrutAdjustmentStatus.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StrutAdjustmentStatus.Text = message ?? ""; }));
        }

        public void SetHangerAdjustmentStatus(string message)
        {
            if (Dispatcher.CheckAccess()) HangerAdjustmentStatus.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { HangerAdjustmentStatus.Text = message ?? ""; }));
        }

        public void SetToolStatus(string message)
        {
            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? ""; }));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ElementToolsSettings settings = new ElementToolsSettings(
                UtilitiesConfigurationStore.Current, new List<string>(), 2, ApplyConfiguration);
            settings.Owner = Window.GetWindow(this);
            settings.ShowDialog();
        }

        private void ApplyConfiguration(ElementToolsConfiguration configuration)
        {
            UtilitiesConfigurationStore.Apply(configuration);
            RodAdjustmentTextBox.Text = UtilitiesConfigurationStore.Current.HangerRodAdjustmentDefault ?? "0.50\"";
            BuildTools();
        }

        public void DisposeExternalEvent()
        {
            try { if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; } } catch { }
        }
    }
}
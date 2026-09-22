using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.Utilities;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.ElementFilter
{
    public partial class ElementFilterPane : Page, IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("7F3C9A21-64B8-4E50-9D12-3A7E5C08B416"));

        public static ElementFilterPane Instance { get; private set; }

        private ElementFilterEventHandler _handler;
        private ExternalEvent _externalEvent;
        private FilterDesignerWindow _designer;
        private string _selectedId;

        /// <summary>Kept so the designer can offer real parameter names and categories.</summary>
        private Document _lastDocument;

        public ElementFilterPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

            _handler = new ElementFilterEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _handler.Pane = this;

            _selectedId = ElementFilterStore.Library.SelectedId;
            RebuildList();
        }

        private void LoadBranding()
        {
            string root = Path.Combine(
                Path.GetDirectoryName(typeof(ElementFilterPane).Assembly.Location), "Resources", "Icons");

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
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
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

        public void RefreshFromUIApplication(UIApplication uiapp)
        {
            if (uiapp != null && uiapp.ActiveUIDocument != null)
                _lastDocument = uiapp.ActiveUIDocument.Document;

            RebuildList();
        }

        public void SetStatus(string message)
        {
            if (StatusText == null) return;
            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? ""; }));
        }

        // ============================================================
        // LIST
        // ============================================================

        /// <summary>
        /// Fenced off because every caller is a WPF handler. An exception that
        /// escapes a WPF handler inside Revit is not shown to anyone - it
        /// terminates the process (journal: ExceptionCode=0xe0434352). A failure
        /// here becomes a status line instead.
        /// </summary>
        private void RebuildList()
        {
            try { RebuildListCore(); }
            catch (Exception ex)
            {
                try { SetStatus("Could not refresh: " + ex.Message); }
                catch { }
            }
        }

        private void RebuildListCore()
        {
            FiltersPanel.Children.Clear();

            string search = (SearchTextBox == null ? "" : SearchTextBox.Text ?? "").Trim();

            List<FilterDefinition> all = ElementFilterStore.All
                .Where(f => string.IsNullOrEmpty(search) ||
                            f.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            EmptyText.Visibility = ElementFilterStore.All.Count == 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            foreach (FilterDefinition filter in all)
                FiltersPanel.Children.Add(BuildRow(filter));

            HeaderSubtitle.Text = ElementFilterStore.All.Count == 0
                ? "Rule-based select, isolate, hide and override"
                : ElementFilterStore.All.Count + " filter(s)";

            ShowDetails(ElementFilterStore.Find(_selectedId));
        }

        private UIElement BuildRow(FilterDefinition filter)
        {
            bool selected = string.Equals(filter.Id, _selectedId, StringComparison.OrdinalIgnoreCase);

            Border border = new Border
            {
                Background = RevitThemeService.ThemeBrush(this, selected ? "SurfaceAltBrush" : "SurfaceBrush"),
                BorderBrush = RevitThemeService.ThemeBrush(this, selected ? "PrimaryBrush" : "BorderBrush"),
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand,
                ToolTip = "Click to select. Double-click to run the default action."
            };

            StackPanel stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = filter.Name,
                Foreground = RevitThemeService.ThemeBrush(this, "TextBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            string defaultText = filter.DefaultAction == FilterAction.Ask
                ? "double-click asks"
                : "double-click " + FilterDefinition.ActionText(filter.DefaultAction).ToLowerInvariant() + "s";

            stack.Children.Add(new TextBlock
            {
                Text = filter.Summary + " · " + defaultText,
                Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                FontSize = 9,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            border.Child = stack;

            string id = filter.Id;

            border.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2) return;
                _selectedId = id;
                ElementFilterStore.Library.SelectedId = id;
                ElementFilterStore.Save();
                RebuildList();
            };

            border.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount < 2) return;

                _selectedId = id;
                ElementFilterStore.Library.SelectedId = id;
                ElementFilterStore.Save();
                RebuildList();

                FilterDefinition target = ElementFilterStore.Find(id);
                if (target == null) return;

                if (target.DefaultAction == FilterAction.Ask)
                {
                    SetStatus("'" + target.Name + "' has no default action. Pick one below.");
                    return;
                }

                Run(target, target.DefaultAction);
            };

            return border;
        }

        private void ShowDetails(FilterDefinition filter)
        {
            if (filter == null)
            {
                DetailsCard.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            DetailsCard.Visibility = System.Windows.Visibility.Visible;
            DetailName.Text = filter.Name;

            string categories = filter.CategoryNames == null || filter.CategoryNames.Count == 0
                ? "All model categories"
                : string.Join(", ", filter.CategoryNames);

            DetailSummary.Text = filter.Summary + " · " + categories;
            TransparencyButton.Content = "TRANSPARENCY " + filter.Transparency + "%";

            ConditionsPanel.Children.Clear();

            if (filter.Conditions == null || filter.Conditions.Count == 0)
            {
                ConditionsPanel.Children.Add(new TextBlock
                {
                    Text = "No conditions yet. Press EDIT to add one.",
                    Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            string joiner = filter.Match == RuleMatch.All ? "AND" : "OR";

            for (int i = 0; i < filter.Conditions.Count; i++)
            {
                if (i > 0)
                {
                    ConditionsPanel.Children.Add(new TextBlock
                    {
                        Text = joiner,
                        Foreground = RevitThemeService.ThemeBrush(this, "PrimaryBrush"),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }

                ConditionsPanel.Children.Add(new TextBlock
                {
                    Text = "• " + filter.Conditions[i],
                    Foreground = RevitThemeService.ThemeBrush(this, "TextBrush"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildList();
        }

        // ============================================================
        // RUNNING
        // ============================================================

        private void Run(FilterDefinition filter, FilterAction action)
        {
            if (filter == null) { SetStatus("Select a filter first."); return; }
            if (_handler == null || _externalEvent == null) return;

            if (filter.Conditions == null || filter.Conditions.Count == 0)
            {
                SetStatus("'" + filter.Name + "' has no conditions. Press EDIT to add one.");
                return;
            }

            // Conditions set to "ask when run" collect their value here, before the
            // external event fires, because a modal dialog cannot open inside it.
            Dictionary<string, string> promptValues = null;

            List<FilterCondition> prompts = filter.Conditions
                .Where(c => c != null && c.InputType == RuleInputType.Prompt &&
                            !string.IsNullOrWhiteSpace(c.ParameterName))
                .ToList();

            if (prompts.Count > 0)
            {
                List<SimpleForms.FormField> fields = prompts
                    .Select(c => SimpleForms.FormField.Text(c.ParameterName.ToUpperInvariant(), c.Value ?? ""))
                    .ToList();

                if (!SimpleForms.AskFields(
                        filter.Name,
                        "Enter the value(s) to filter by.",
                        fields,
                        "RUN"))
                {
                    SetStatus("Cancelled.");
                    return;
                }

                promptValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < prompts.Count; i++)
                    promptValues[prompts[i].ParameterName] = fields[i].Value ?? "";
            }

            SetStatus("Running '" + filter.Name + "'...");
            _handler.RequestApply(filter.Clone(), action, promptValues);

            try { _externalEvent.Raise(); }
            catch (Exception ex) { SetStatus("Could not run the filter: " + ex.Message); }
        }

        private FilterDefinition Selected
        {
            get { return ElementFilterStore.Find(_selectedId); }
        }

        private void SelectAction_Click(object sender, RoutedEventArgs e) { Run(Selected, FilterAction.Select); }
        private void IsolateAction_Click(object sender, RoutedEventArgs e) { Run(Selected, FilterAction.Isolate); }
        private void HideAction_Click(object sender, RoutedEventArgs e) { Run(Selected, FilterAction.Hide); }
        private void HalftoneAction_Click(object sender, RoutedEventArgs e) { Run(Selected, FilterAction.Halftone); }
        private void TransparencyAction_Click(object sender, RoutedEventArgs e) { Run(Selected, FilterAction.Transparency); }

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_handler == null || _externalEvent == null) return;

            _handler.RequestResetView();
            try { _externalEvent.Raise(); }
            catch (Exception ex) { SetStatus("Could not restore the view: " + ex.Message); }
        }

        // ============================================================
        // MANAGING
        // ============================================================

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDesigner(new FilterDefinition(), true);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            FilterDefinition filter = Selected;
            if (filter == null) return;
            OpenDesigner(filter.Clone(), false);
        }

        private void DuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            FilterDefinition filter = Selected;
            if (filter == null) return;

            FilterDefinition copy = filter.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = filter.Name + " (copy)";

            ElementFilterStore.Upsert(copy);
            _selectedId = copy.Id;
            RebuildList();
            SetStatus("Duplicated as '" + copy.Name + "'. Press EDIT to adapt it.");
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            FilterDefinition filter = Selected;
            if (filter == null) return;

            if (!CtsConfirmWindow.Ask(
                    "Delete filter",
                    "Delete '" + filter.Name + "'?\n\nThis only removes the filter from CTS Element Filter. " +
                    "Nothing in the model is changed.",
                    "DELETE"))
                return;

            string name = filter.Name;
            ElementFilterStore.Remove(filter.Id);
            _selectedId = ElementFilterStore.Library.SelectedId;
            RebuildList();
            SetStatus("'" + name + "' deleted.");
        }

        private void OpenDesigner(FilterDefinition definition, bool isNew)
        {
            if (_designer != null)
            {
                try { _designer.Activate(); } catch { }
                return;
            }

            _designer = new FilterDesignerWindow(definition, isNew, _lastDocument);
            _designer.Owner = Window.GetWindow(this);

            bool? result = _designer.ShowDialog();
            _designer = null;

            if (result != true) return;

            // The designer edits the instance it was handed, so it is already current.
            ElementFilterStore.Upsert(definition);
            _selectedId = definition.Id;
            ElementFilterStore.Library.SelectedId = definition.Id;
            ElementFilterStore.Save();

            RebuildList();
            SetStatus("'" + definition.Name + "' saved.");
        }

        // ============================================================
        // SHARING
        // ============================================================

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ElementFilterStore.All.Count == 0)
            {
                SetStatus("There is nothing to export yet.");
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export CTS Element Filters",
                Filter = "CTS Element Filters (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".xml",
                AddExtension = true,
                FileName = ElementFilterStore.SuggestedExportFileName
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                ElementFilterStore.ExportTo(dialog.FileName, null);
                SetStatus(ElementFilterStore.All.Count + " filter(s) exported to " +
                          Path.GetFileName(dialog.FileName) + ".");
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Element Filter - Export",
                    "Could not export the filters.\n\nPath:\n" + dialog.FileName + "\n\nError:\n" + ex.Message);
                SetStatus("Export failed.");
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import CTS Element Filters",
                Filter = "CTS Element Filters (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".xml",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            bool replaceAll = false;

            if (ElementFilterStore.All.Count > 0)
            {
                replaceAll = CtsConfirmWindow.Ask(
                    "Import filters",
                    "You already have " + ElementFilterStore.All.Count + " filter(s).\n\n" +
                    "REPLACE ALL removes them and keeps only what is in the file.\n" +
                    "MERGE keeps yours and updates any filter with the same name.",
                    "REPLACE ALL",
                    "MERGE");
            }

            try
            {
                ElementFilterStore.ImportSummary summary =
                    ElementFilterStore.Import(dialog.FileName, replaceAll);

                _selectedId = ElementFilterStore.Library.SelectedId;
                RebuildList();
                SetStatus(summary.ToString());
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Element Filter - Import",
                    "Could not import the filters.\n\nFile:\n" + dialog.FileName + "\n\nError:\n" + ex.Message);
                SetStatus("Import failed.");
            }
        }

        public void DisposeExternalEvent()
        {
            try { if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; } } catch { }
        }
    }
}

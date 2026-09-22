using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// Revit 2025+ adds Autodesk.Revit.UI.ContextMenu / MenuItem; force the WPF ones.
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class FabricationAssembliesEditorWindow : Window, IFabricationAssembliesHost
    {
        private static readonly string[] DiameterValues =
        {
            "AUTO (O-Let)", "1/2\"", "3/4\"", "1\"", "1 1/4\"", "1 1/2\"",
            "2\"", "2 1/2\"", "3\"", "4\"", "5\"", "6\"",
            "8\"", "10\"", "12\"", "14\"", "16\""
        };

        private static int _lastCatalogServiceId = -1;
        private static int _lastPaletteIndex = 0;

        private FabricationAssembliesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;
        private List<FabricationAssemblyTemplate> _templates;
        private List<FabricationPartButtonInfo> _catalog = new List<FabricationPartButtonInfo>();
        private FabricationAssemblyTemplate _selectedTemplate;
        private FabricationAssemblyPartDefinition _selectedPart;
        private bool _replaceNextPart;
        private bool _loading;
        private IFabricationAssembliesHost _returnHost;

        public FabricationAssembliesEditorWindow(
            FabricationAssembliesExternalEventHandler handler,
            ExternalEvent externalEvent,
            IFabricationAssembliesHost returnHost)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (externalEvent == null) throw new ArgumentNullException(nameof(externalEvent));

            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();
            ApplyWindowIcon();

            _templates = FabricationAssemblyTemplateStore.Load();
            _handler = handler;
            _externalEvent = externalEvent;
            _returnHost = returnHost;
            _handler.Host = this;
            DiameterComboBox.ItemsSource = DiameterValues;

            Closed += FabricationAssembliesEditorWindow_Closed;

            RefreshTemplateList();
            RequestDatabase();
        }

        private void FabricationAssembliesEditorWindow_Closed(object sender, EventArgs e)
        {
            try { if (_handler != null && _returnHost != null) _handler.Host = _returnHost; } catch { }
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(FabricationAssembliesEditorWindow).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "FabricationAssemblies.png"));
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

        private void ApplyWindowIcon()
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(FabricationAssembliesEditorWindow).Assembly.Location), "Resources", "Icons", "FabricationAssemblies.png");
            if (!File.Exists(path)) return;
            try
            {
                BitmapImage bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); Icon = bitmap;
            }
            catch { }
        }

        private void RequestDatabase()
        {
            try
            {
                _handler.Host = this;
                _handler.RequestLoadServices();
                ExternalEventRequest request = _externalEvent.Raise();
                if (request == ExternalEventRequest.Accepted || request == ExternalEventRequest.Pending)
                    SetToolStatus("Reading loaded Revit Fabrication services (catalog items load on demand)...");
                else SetToolStatus("Revit did not accept the service refresh request.");
            }
            catch (Exception ex) { SetToolStatus("Could not read services: " + ex.Message); }
        }

        private void RequestCatalog(int serviceId, int paletteIndex)
        {
            if (serviceId < 0) return;
            try
            {
                _handler.Host = this;
                _handler.RequestLoadCatalog(serviceId, paletteIndex);
                ExternalEventRequest request = _externalEvent.Raise();
                if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
                    SetToolStatus("Revit did not accept the catalog request.");
            }
            catch (Exception ex) { SetToolStatus("Could not read catalog: " + ex.Message); }
        }

        private void Service_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            FabricationServiceInfo service = ServiceComboBox.SelectedItem as FabricationServiceInfo;
            if (service == null) return;
            _lastCatalogServiceId = service.ServiceId;
            _lastPaletteIndex = 0;
            _loading = true;
            try { PaletteComboBox.ItemsSource = null; }
            finally { _loading = false; }
            _catalog.Clear();
            BuildCatalog();
            CatalogStatusText.Text = "Loading " + service.Name + "...";
            RequestCatalog(service.ServiceId, 0);
        }

        private void Palette_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            FabricationServiceInfo service = ServiceComboBox.SelectedItem as FabricationServiceInfo;
            FabricationPaletteInfo palette = PaletteComboBox.SelectedItem as FabricationPaletteInfo;
            if (service == null || palette == null) return;
            _lastPaletteIndex = palette.Index;
            _catalog.Clear();
            BuildCatalog();
            CatalogStatusText.Text = "Loading " + palette.Name + "...";
            RequestCatalog(service.ServiceId, palette.Index);
        }

        public void ReceiveServices(List<FabricationServiceInfo> services, string status)
        {
            Action action = delegate
            {
                _loading = true;
                try
                {
                    ServiceComboBox.ItemsSource = null;
                    ServiceComboBox.ItemsSource = services ?? new List<FabricationServiceInfo>();
                    PaletteComboBox.ItemsSource = null;
                    FabricationServiceInfo choice = (services ?? new List<FabricationServiceInfo>())
                        .FirstOrDefault(x => x.ServiceId == _lastCatalogServiceId);
                    if (choice == null && services != null && services.Count > 0) choice = services[0];
                    ServiceComboBox.SelectedItem = choice;
                }
                finally { _loading = false; }
                FabricationServiceInfo selected = ServiceComboBox.SelectedItem as FabricationServiceInfo;
                if (selected != null) RequestCatalog(selected.ServiceId, _lastPaletteIndex);
                else { _catalog.Clear(); BuildCatalog(); }
                CatalogStatusText.Text = status ?? "Ready";
                SetToolStatus(status);
            };
            if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
        }

        public void ReceiveCatalog(List<FabricationPartButtonInfo> catalog, List<FabricationPaletteInfo> palettes, string status)
        {
            Action action = delegate
            {
                _loading = true;
                try
                {
                    PaletteComboBox.ItemsSource = null;
                    PaletteComboBox.ItemsSource = palettes ?? new List<FabricationPaletteInfo>();
                    FabricationPaletteInfo selection = (palettes ?? new List<FabricationPaletteInfo>())
                        .FirstOrDefault(x => x.Index == _lastPaletteIndex);
                    if (selection == null && palettes != null && palettes.Count > 0) selection = palettes[0];
                    PaletteComboBox.SelectedItem = selection;
                }
                finally { _loading = false; }
                _catalog = catalog ?? new List<FabricationPartButtonInfo>();
                BuildCatalog();
                CatalogStatusText.Text = status ?? "Ready";
                SetToolStatus(status);
            };
            if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
        }

        private void RefreshTemplateList()
        {
            _loading = true;
            try
            {
                TemplateList.ItemsSource = null;
                TemplateList.ItemsSource = _templates;
                if (_selectedTemplate != null && _templates.Contains(_selectedTemplate))
                    TemplateList.SelectedItem = _selectedTemplate;
                else if (_templates.Count > 0)
                    TemplateList.SelectedIndex = 0;
            }
            finally { _loading = false; }
            TemplateList_SelectionChanged(null, null);
        }

        private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _selectedTemplate = TemplateList.SelectedItem as FabricationAssemblyTemplate;
            _selectedPart = null;
            _replaceNextPart = false;
            ReplaceSelectedButton.Content = "REPLACE SELECTED (NEXT CATALOG CLICK)";
            ReplaceSelectedButton.IsEnabled = false;
            PartDetailsText.Text = "Select a sequence item to see captured identity, length and variation.";
            if (_selectedTemplate == null) return;

            _loading = true;
            try
            {
                TemplateNameTextBox.Text = _selectedTemplate.Name;
                int diameterIndex = 0;
                if (!_selectedTemplate.UseAnchorDiameter)
                {
                    double best = double.MaxValue;
                    for (int i = 1; i < DiameterValues.Length; i++)
                    {
                        double candidate;
                        if (!ImperialLength.TryParseInches(DiameterValues[i], out candidate)) continue;
                        double diff = Math.Abs(candidate - _selectedTemplate.DefaultDiameterInches);
                        if (diff < best) { diameterIndex = i; best = diff; }
                    }
                }
                DiameterComboBox.SelectedIndex = diameterIndex;
            }
            finally { _loading = false; }
            RefreshSequence();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            string name = global::CTSRevitPlugin.Utilities.SimpleForms.AskString("New Fabrication Assembly", "Assembly name:", "New Assembly");
            if (string.IsNullOrWhiteSpace(name)) return;
            FabricationAssemblyTemplate template = new FabricationAssemblyTemplate { Name = name.Trim() };
            template.Parts.Add(new FabricationAssemblyPartDefinition { Name = "O-Let Weld Gap", ConditionIndex = -1 });
            _templates.Add(template);
            _selectedTemplate = template;
            FabricationAssemblyTemplateStore.Save(_templates);
            RefreshTemplateList();
            TemplateList.SelectedItem = template;
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;
            string name = global::CTSRevitPlugin.Utilities.SimpleForms.AskString("Rename Fabrication Assembly", "New name:", _selectedTemplate.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            _selectedTemplate.Name = name.Trim();
            FabricationAssemblyTemplateStore.Save(_templates);
            RefreshTemplateList();
            TemplateList.SelectedItem = _selectedTemplate;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;
            if (MessageBox.Show("Delete '" + _selectedTemplate.Name + "'?", "CTS Fabrication Assemblies", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _templates.Remove(_selectedTemplate);
            _selectedTemplate = null;
            FabricationAssemblyTemplateStore.Save(_templates);
            RefreshTemplateList();
        }

        private void TemplateName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loading && _selectedTemplate != null) _selectedTemplate.Name = TemplateNameTextBox.Text;
        }

        private void Diameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _selectedTemplate == null) return;
            string selected = DiameterComboBox.SelectedItem as string;
            if (selected == null) return;
            _selectedTemplate.UseAnchorDiameter = selected.StartsWith("AUTO", StringComparison.Ordinal);
            double diameter;
            if (!_selectedTemplate.UseAnchorDiameter && ImperialLength.TryParseInches(selected, out diameter))
                _selectedTemplate.DefaultDiameterInches = diameter;
        }

        private void BuildCatalog()
        {
            CatalogPanel.Children.Clear();
            string filter = SearchTextBox == null ? "" : SearchTextBox.Text.Trim();
            IEnumerable<FabricationPartButtonInfo> visible = _catalog;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                visible = visible.Where(x =>
                    (x.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.Code ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.ServiceName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.PaletteName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.Conditions != null && x.Conditions.Any(c =>
                        (c.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (c.Description ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)));
            }

            foreach (FabricationPartButtonInfo part in visible)
            {
                Grid tile = new Grid { Width = 118, Height = 118, Margin = new Thickness(3) };
                Button mainButton = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(4),
                    Tag = part,
                    Background = (Brush)FindResource("SurfaceAltBrush"),
                    Foreground = (Brush)FindResource("TextBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    ToolTip = BuildPartTooltip(part)
                };

                StackPanel content = new StackPanel();
                content.Children.Add(new Image { Width = 76, Height = 60, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Source = part.Image });
                content.Children.Add(new TextBlock
                {
                    Text = part.DisplayName,
                    FontSize = 9,
                    Foreground = (Brush)FindResource("TextBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(1, 2, 1, 0),
                    MaxHeight = 28
                });
                content.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(part.ServiceName) ? part.PaletteName : part.ServiceName,
                    FontSize = 7,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(1, 1, 1, 0)
                });
                mainButton.Content = content;
                mainButton.Click += CatalogPart_Click;
                tile.Children.Add(mainButton);

                if (part.Conditions != null && part.Conditions.Count > 1)
                {
                    Button variationButton = new Button
                    {
                        Width = 24, Height = 20, Content = "▼", FontSize = 8, Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 3, 3), Background = (Brush)FindResource("SurfaceBrush"),
                        Foreground = (Brush)FindResource("TextBrush"), BorderBrush = (Brush)FindResource("BorderBrush"),
                        BorderThickness = new Thickness(1), Tag = part,
                        ToolTip = "Choose one of " + part.Conditions.Count + " database variations"
                    };
                    variationButton.Click += ConditionArrow_Click;
                    Panel.SetZIndex(variationButton, 10);
                    tile.Children.Add(variationButton);
                }

                CatalogPanel.Children.Add(tile);
            }
        }

        private static string BuildPartTooltip(FabricationPartButtonInfo part)
        {
            if (part == null) return "";
            string text = part.Name ?? "Fabrication Part";
            if (!string.IsNullOrWhiteSpace(part.Code)) text += "\nCode: " + part.Code;
            if (!string.IsNullOrWhiteSpace(part.ServiceName)) text += "\nService: " + part.ServiceName;
            if (!string.IsNullOrWhiteSpace(part.PaletteName)) text += "\nPalette: " + part.PaletteName;
            if (part.Conditions != null && part.Conditions.Count > 1) text += "\nVariations: " + part.Conditions.Count + " (use ▼)";
            return text;
        }

        private void CatalogPart_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            FabricationPartButtonInfo part = button == null ? null : button.Tag as FabricationPartButtonInfo;
            if (part == null) return;
            // Main tile selects the logical part, not Condition 0. Revit chooses
            // the correct condition from the real O-Let diameter at placement.
            AddCatalogPart(part, -1);
        }

        private void ConditionArrow_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            FabricationPartButtonInfo part = button == null ? null : button.Tag as FabricationPartButtonInfo;
            if (part == null || part.Conditions == null || part.Conditions.Count == 0) return;

            ContextMenu menu = new ContextMenu { PlacementTarget = button, MinWidth = 300 };
            foreach (FabricationPartConditionInfo condition in part.Conditions)
            {
                MenuItem item = new MenuItem
                {
                    Header = condition.DisplayName + (string.IsNullOrWhiteSpace(condition.RangeText) ? "" : "  ·  " + condition.RangeText),
                    Tag = new ConditionChoice(part, condition),
                    ToolTip = string.IsNullOrWhiteSpace(condition.Description) ? condition.DisplayName : condition.Description
                };
                item.Click += ConditionMenuItem_Click;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private sealed class ConditionChoice
        {
            public FabricationPartButtonInfo Part { get; private set; }
            public FabricationPartConditionInfo Condition { get; private set; }
            public ConditionChoice(FabricationPartButtonInfo part, FabricationPartConditionInfo condition) { Part = part; Condition = condition; }
        }

        private void ConditionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            ConditionChoice choice = item == null ? null : item.Tag as ConditionChoice;
            if (choice == null) return;
            AddCatalogPart(choice.Part, choice.Condition.ConditionIndex);
        }

        private void AddCatalogPart(FabricationPartButtonInfo part, int conditionIndex)
        {
            if (_replaceNextPart)
            {
                if (_selectedTemplate == null || _selectedPart == null ||
                    _selectedTemplate.Parts.IndexOf(_selectedPart) <= 0)
                {
                    _replaceNextPart = false;
                    SetToolStatus("Select a non-anchor sequence item before replacing.");
                    return;
                }
                int position = _selectedTemplate.Parts.IndexOf(_selectedPart);
                FabricationAssemblyPartDefinition replacement = BuildDefinition(part, conditionIndex);
                _selectedTemplate.Parts[position] = replacement;
                _selectedTemplate.CapturedRecipeVerified = false; // recipe intentionally edited
                _selectedPart = replacement;
                _replaceNextPart = false;
                ReplaceSelectedButton.Content = "REPLACE SELECTED (NEXT CATALOG CLICK)";
                RefreshSequence();
                UpdatePartDetails();
                SetToolStatus("Replaced step " + (position + 1) + " with " +
                    part.Name + " · " + (replacement.ConditionExplicit
                        ? replacement.ConditionName : "AUTO CONDITION") +
                    ". Click SAVE. Diameter still comes from the O-Let.");
                return;
            }
            if (_selectedTemplate == null)
            {
                SetToolStatus("Create or select an assembly first.");
                return;
            }

            bool firstPart = _selectedTemplate.Parts == null || _selectedTemplate.Parts.Count == 0;
            if (firstPart && !IsOLetPart(part))
            {
                SetToolStatus("The first sequence item must be O-Let Weld Gap. Select O-Let Weld Gap from the database first.");
                return;
            }

            FabricationAssemblyPartDefinition added = BuildDefinition(part, conditionIndex);
            _selectedTemplate.Parts.Add(added);
            _selectedTemplate.CapturedRecipeVerified = false;
            RefreshSequence();
            string suffix = !added.ConditionExplicit ? " · AUTO CONDITION" : " · " + added.ConditionName;
            SetToolStatus(part.Name + suffix + " added to " + _selectedTemplate.Name + ".");
        }

        private static FabricationAssemblyPartDefinition BuildDefinition(
            FabricationPartButtonInfo part, int conditionIndex)
        {
            FabricationPartConditionInfo condition = conditionIndex < 0 || part.Conditions == null
                ? null : part.Conditions.FirstOrDefault(x => x.ConditionIndex == conditionIndex);
            return new FabricationAssemblyPartDefinition
            {
                Name = part.Name, Code = part.Code, ServiceId = part.ServiceId,
                PaletteIndex = part.PaletteIndex, ButtonIndex = part.ButtonIndex,
                ConditionIndex = conditionIndex,
                ConditionName = condition == null ? "" : condition.DisplayName,
                ConditionExplicit = conditionIndex >= 0, PaletteName = part.PaletteName
            };
        }

        private void ReplaceSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null || _selectedPart == null ||
                _selectedTemplate.Parts.IndexOf(_selectedPart) <= 0) return;
            _replaceNextPart = !_replaceNextPart;
            ReplaceSelectedButton.Content = _replaceNextPart
                ? "CANCEL REPLACE" : "REPLACE SELECTED (NEXT CATALOG CLICK)";
            SetToolStatus(_replaceNextPart
                ? "Now choose a part tile, or ▼ for an exact nipple length/type; step " +
                  (_selectedTemplate.Parts.IndexOf(_selectedPart) + 1) + " will be replaced (NOT appended)."
                : "Replacement canceled; catalog clicks append new parts.");
        }

        private void UpdatePartDetails()
        {
            ReplaceSelectedButton.IsEnabled = _selectedTemplate != null && _selectedPart != null &&
                _selectedTemplate.Parts.IndexOf(_selectedPart) > 0;
            if (_selectedPart == null)
            {
                PartDetailsText.Text = "Select a sequence item to see captured identity, length and variation.";
                return;
            }
            var part = _selectedPart;
            string size = string.IsNullOrWhiteSpace(part.ReferenceProductEntryName) ? "" :
                " | product size " + part.ReferenceProductEntryName;
            string length = part.ReferenceLengthInches <= 0 ? "" :
                " | LENGTH " + ImperialLength.FormatInches(part.ReferenceLengthInches);
            PartDetailsText.Text = "ITEM: " + part.Name +
                "\nTYPE / LENGTH: " + (part.ConditionExplicit ? part.ConditionName : "AUTO") +
                (string.IsNullOrWhiteSpace(part.ReferenceFamilyName) ? "" :
                    "\nCAPTURED: " + part.ReferenceFamilyName) + length + size +
                "\nPipe diameter comes from the O-Let, not ▼.";
        }

        private static bool IsOLetPart(FabricationPartButtonInfo part)
        {
            if (part == null) return false;
            string text = (part.Name ?? "") + " " + (part.Code ?? "");
            return text.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshSequence()
        {
            SequencePanel.Children.Clear();
            if (_selectedTemplate == null) { SequenceCountText.Text = "0 parts"; return; }
            int count = _selectedTemplate.Parts == null ? 0 : _selectedTemplate.Parts.Count;
            SequenceCountText.Text = count + " part(s)";

            for (int i = 0; i < count; i++)
            {
                FabricationAssemblyPartDefinition part = _selectedTemplate.Parts[i];
                bool selected = ReferenceEquals(part, _selectedPart);
                Border row = new Border
                {
                    Background = (Brush)FindResource(selected ? "SelectionBrush" : "SurfaceAltBrush"),
                    BorderBrush = (Brush)FindResource(selected ? "PrimaryBrush" : "BorderBrush"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7), Margin = new Thickness(0, 2, 0, 2), Tag = part, Cursor = Cursors.Hand
                };
                row.MouseLeftButtonUp += SequenceRow_Click;
                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock n = new TextBlock { Text = (i + 1).ToString("00"), Foreground = (Brush)FindResource("PrimaryBrush"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                grid.Children.Add(n);
                StackPanel info = new StackPanel();
                string displayName = i == 0 ? part.Name + "  [ANCHOR]" : part.Name;
                info.Children.Add(new TextBlock { Text = displayName, Foreground = (Brush)FindResource("TextBrush"), FontSize = 10, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                string meta = string.IsNullOrWhiteSpace(part.PaletteName) ? "Revit Fabrication" : part.PaletteName;
                if ((part.ConditionExplicit || part.ConditionIndex > 0) &&
                    !string.IsNullOrWhiteSpace(part.ConditionName)) meta += " · " + part.ConditionName;
                if (part.ReferenceLengthInches > 0 && i > 0)
                    meta += " · length " + ImperialLength.FormatInches(part.ReferenceLengthInches);
                if (i == 0) meta = "Placed manually in Revit · used as anchor";
                info.Children.Add(new TextBlock { Text = meta, Foreground = (Brush)FindResource("TextMutedBrush"), FontSize = 8, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(info, 1); grid.Children.Add(info); row.Child = grid; SequencePanel.Children.Add(row);
            }
        }

        private void SequenceRow_Click(object sender, MouseButtonEventArgs e)
        {
            Border row = sender as Border;
            _selectedPart = row == null ? null : row.Tag as FabricationAssemblyPartDefinition;
            RefreshSequence();
            UpdatePartDetails();
            if (_selectedPart != null) SetToolStatus("Selected: " + _selectedPart.Name +
                " · choose REPLACE SELECTED to edit type or nipple length.");
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null || _selectedPart == null) return;
            int index = _selectedTemplate.Parts.IndexOf(_selectedPart);
            if (index <= 0) return;
            if (index == 1) { SetToolStatus("O-Let Weld Gap must remain the first anchor item."); return; }
            _selectedTemplate.Parts.RemoveAt(index); _selectedTemplate.Parts.Insert(index - 1, _selectedPart);
            _selectedTemplate.CapturedRecipeVerified = false; RefreshSequence();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null || _selectedPart == null) return;
            int index = _selectedTemplate.Parts.IndexOf(_selectedPart);
            if (index < 0 || index >= _selectedTemplate.Parts.Count - 1) return;
            if (index == 0) { SetToolStatus("O-Let Weld Gap must remain the first anchor item."); return; }
            _selectedTemplate.Parts.RemoveAt(index); _selectedTemplate.Parts.Insert(index + 1, _selectedPart);
            _selectedTemplate.CapturedRecipeVerified = false; RefreshSequence();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null || _selectedPart == null) return;
            if (_selectedTemplate.Parts.IndexOf(_selectedPart) == 0)
            {
                SetToolStatus("The O-Let anchor cannot be removed. Remove the entire assembly instead.");
                return;
            }
            _selectedTemplate.Parts.Remove(_selectedPart); _selectedPart = null;
            _selectedTemplate.CapturedRecipeVerified = false; RefreshSequence(); UpdatePartDetails();
        }

        private void ViewCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null ||
                string.IsNullOrWhiteSpace(_selectedTemplate.CapturedReferencePath) ||
                !File.Exists(_selectedTemplate.CapturedReferencePath))
            {
                // The capture is saved outside the operational recipe. Reload in case
                // the pane captured the same template before this editor was opened.
                if (_selectedTemplate != null)
                {
                    FabricationAssemblyTemplate stored = FabricationAssemblyTemplateStore.Load()
                        .FirstOrDefault(x => string.Equals(x.Name, _selectedTemplate.Name, StringComparison.OrdinalIgnoreCase));
                    if (stored != null) _selectedTemplate.CapturedReferencePath = stored.CapturedReferencePath;
                }
            }
            if (_selectedTemplate == null ||
                string.IsNullOrWhiteSpace(_selectedTemplate.CapturedReferencePath) ||
                !File.Exists(_selectedTemplate.CapturedReferencePath))
            {
                SetToolStatus("No captured reference for this template. Close CONFIGURE, CAPTURE the manual assembly in the CTS pane, then reopen.");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start("notepad.exe", _selectedTemplate.CapturedReferencePath);
                SetToolStatus("Opened read-only captured model reference. The recipe has not been overwritten.");
            }
            catch (Exception ex) { SetToolStatus("Unable to open capture: " + ex.Message); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;
            _selectedTemplate.Name = TemplateNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(_selectedTemplate.Name)) { SetToolStatus("Enter an assembly name."); return; }
            if (_selectedTemplate.Parts == null || _selectedTemplate.Parts.Count == 0 || !IsOLetDefinition(_selectedTemplate.Parts[0]))
            {
                SetToolStatus("Save requires O-Let Weld Gap as the first sequence item.");
                return;
            }

            FabricationAssemblyTemplateStore.Save(_templates);
            RefreshTemplateList(); TemplateList.SelectedItem = _selectedTemplate;
            SetToolStatus("Saved " + _selectedTemplate.Name + " · " + _selectedTemplate.Parts.Count +
                " part(s) · size " + (_selectedTemplate.UseAnchorDiameter ? "AUTO (O-Let)" : ImperialLength.FormatInches(_selectedTemplate.DefaultDiameterInches)) + ".");
        }

        private static bool IsOLetDefinition(FabricationAssemblyPartDefinition definition)
        {
            if (definition == null) return false;
            string text = (definition.Name ?? "") + " " + (definition.Code ?? "");
            return text.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e) { BuildCatalog(); }
        private void Refresh_Click(object sender, RoutedEventArgs e) { RequestDatabase(); }

        public void SetToolStatus(string message)
        {
            Action action = delegate { StatusText.Text = message ?? ""; StatusText.ToolTip = message ?? ""; };
            if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (_handler != null && _returnHost != null) _handler.Host = _returnHost; } catch { }
            _externalEvent = null; _handler = null;
            base.OnClosed(e);
        }
    }
}

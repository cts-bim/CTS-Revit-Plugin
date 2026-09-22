using CTSRevitPlugin.UI.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Assemblies
{
    /// <summary>
    /// Visual editor for one assembly: browse the fabrication database with its own
    /// thumbnails, click items to build the sequence, reorder it, tune each step and save.
    /// </summary>
    public partial class AssemblyBuilderWindow : Window
    {
        private readonly AssembliesController _controller;
        private readonly AssemblyDefinition _working;
        private readonly Action<AssemblyDefinition> _onSave;

        private List<CatalogService> _services = new List<CatalogService>();
        private CatalogService _service;
        private List<CatalogButton> _currentButtons = new List<CatalogButton>();
        private readonly Dictionary<string, List<CatalogButton>> _paletteCache =
            new Dictionary<string, List<CatalogButton>>(StringComparer.OrdinalIgnoreCase);

        private int _replaceIndex = -1;
        private bool _suspend;

        public AssemblyBuilderWindow(
            AssembliesController controller,
            AssemblyDefinition definition,
            bool isNew,
            Action<AssemblyDefinition> onSave)
        {
            _controller = controller;
            _working = definition;
            _onSave = onSave;

            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

            Title = isNew ? "CTS Assembly Builder - New assembly" : "CTS Assembly Builder - " + _working.Name;

            NameBox.Text = _working.Name ?? string.Empty;
            ShortNameBox.Text = _working.ShortName ?? string.Empty;

            RefreshSteps(FindNextUnassigned(-1));
        }

        // ============================================================
        // BRANDING
        // ============================================================

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(AssemblyBuilderWindow).Assembly.Location), "Resources", "Icons");
            BitmapImage logo = LoadImage(Path.Combine(root, "CTS Icon.png"));
            if (logo != null)
            {
                HeaderLogo.Source = logo;
                FooterCtsLogo.Source = logo;
                Icon = logo;
            }
        }

        private static BitmapImage LoadImage(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // DATABASE BROWSER (left side)
        // ============================================================

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CatalogStatus.Text = "Loading the fabrication database...";
            _controller.RequestServices(Dispatcher, OnServicesLoaded);
        }

        private void OnServicesLoaded(List<CatalogService> services, string error)
        {
            _services = services ?? new List<CatalogService>();

            ServiceList.Items.Clear();
            foreach (CatalogService service in _services)
                ServiceList.Items.Add(CreateTextItem(service.Name, service));

            if (!string.IsNullOrEmpty(error))
            {
                CatalogStatus.Text = error;
                return;
            }

            CatalogStatus.Text = "Select a service and a palette, then click an item to add it to the sequence.";

            // Start on the service already used by this assembly, if the project has it.
            int start = 0;
            AssemblyStep used = _working.Steps.FirstOrDefault(s => s.Item != null && s.Item.IsAssigned);
            if (used != null)
            {
                int found = _services.FindIndex(s => string.Equals(s.Name, used.Item.ServiceName, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) start = found;
            }

            if (ServiceList.Items.Count > 0) ServiceList.SelectedIndex = start;
        }

        private ListBoxItem CreateTextItem(string text, object tag)
        {
            return new ListBoxItem
            {
                Content = new TextBlock { Text = text ?? string.Empty, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 },
                Tag = tag,
                ToolTip = text,
                Style = (Style)FindResource("CtsListItemStyle")
            };
        }

        private void ServiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBoxItem item = ServiceList.SelectedItem as ListBoxItem;
            _service = item == null ? null : item.Tag as CatalogService;

            PaletteList.Items.Clear();
            _currentButtons = new List<CatalogButton>();
            RebuildCatalogTiles();

            if (_service == null) return;

            foreach (CatalogPalette palette in _service.Palettes)
                PaletteList.Items.Add(CreateTextItem(palette.Name + "  (" + palette.ButtonCount + ")", palette));

            if (PaletteList.Items.Count > 0) PaletteList.SelectedIndex = 0;
        }

        private void PaletteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBoxItem item = PaletteList.SelectedItem as ListBoxItem;
            CatalogPalette palette = item == null ? null : item.Tag as CatalogPalette;
            if (palette == null || _service == null) return;

            string serviceName = _service.Name;
            int paletteIndex = palette.Index;
            string key = serviceName + "|" + paletteIndex;

            List<CatalogButton> cached;
            if (_paletteCache.TryGetValue(key, out cached))
            {
                _currentButtons = cached;
                RebuildCatalogTiles();
                return;
            }

            CatalogStatus.Text = "Loading '" + palette.Name + "'...";
            _controller.RequestPalette(Dispatcher, serviceName, paletteIndex, delegate (List<CatalogButton> buttons, string error)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    CatalogStatus.Text = error;
                    return;
                }

                _paletteCache[key] = buttons;

                // Ignore the answer if the user already moved to another palette.
                ListBoxItem currentItem = PaletteList.SelectedItem as ListBoxItem;
                CatalogPalette current = currentItem == null ? null : currentItem.Tag as CatalogPalette;
                if (_service != null && string.Equals(_service.Name, serviceName, StringComparison.OrdinalIgnoreCase) &&
                    current != null && current.Index == paletteIndex)
                {
                    _currentButtons = buttons;
                    RebuildCatalogTiles();
                }
            });
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildCatalogTiles();
        }

        private void RebuildCatalogTiles()
        {
            if (CatalogTilesPanel == null) return;
            CatalogTilesPanel.Children.Clear();

            string filter = (SearchBox.Text ?? string.Empty).Trim();
            int shown = 0;

            foreach (CatalogButton button in _currentButtons)
            {
                if (filter.Length > 0 && !Contains(button.Name, filter) && !Contains(button.Code, filter)) continue;
                CatalogTilesPanel.Children.Add(CreateCatalogTile(button));
                shown++;
            }

            if (_currentButtons.Count > 0)
                CatalogStatus.Text = shown + " item(s). Click one to use it in the sequence.";
        }

        private static bool Contains(string text, string filter)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Grid CreateCatalogTile(CatalogButton catalogButton)
        {
            Border picture = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)FindResource("InputBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            };

            if (catalogButton.Thumbnail != null)
            {
                picture.Child = new Image { Source = catalogButton.Thumbnail, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
            }
            else
            {
                string letter = string.IsNullOrEmpty(catalogButton.Name) ? "?" : catalogButton.Name.Substring(0, 1).ToUpperInvariant();
                picture.Child = new TextBlock
                {
                    Text = letter,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            StackPanel content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(picture);
            content.Children.Add(new TextBlock
            {
                Text = catalogButton.Name,
                FontSize = 9,
                MaxWidth = 84,
                MaxHeight = 26,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            });

            Button tile = new Button
            {
                Width = 92,
                Height = 96,
                Margin = new Thickness(0),
                Padding = new Thickness(3),
                Content = content,
                ToolTip = catalogButton.Name + (string.IsNullOrEmpty(catalogButton.Code) ? string.Empty : "  [" + catalogButton.Code + "]") +
                          (catalogButton.ConditionNames.Count > 1 ? "\nClick to add it (variation chosen by size), or use the arrow to pick the variation." : string.Empty)
            };

            tile.Click += delegate { OnCatalogItemClicked(catalogButton, null); };

            Grid host = new Grid { Width = 92, Height = 96, Margin = new Thickness(0, 0, 6, 6) };
            host.Children.Add(tile);

            // Same little arrow as the Revit palette: lists the variations of the item (x1.5, x3...).
            if (catalogButton.ConditionNames.Count > 1)
            {
                Button arrow = new Button
                {
                    Content = "\u25BE",
                    Width = 24,
                    Height = 20,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 3, 3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    FontSize = 12,
                    ToolTip = catalogButton.ConditionNames.Count + " variations: pick the exact one"
                };

                arrow.Click += delegate
                {
                    ShowVariationPopup(arrow, catalogButton.ConditionNames, null, false,
                        delegate (string picked) { OnCatalogItemClicked(catalogButton, picked); });
                };

                host.Children.Add(arrow);
            }

            return host;
        }

        /// <summary>
        /// Small themed drop-down list (same idea as the arrow of the Revit palette).
        /// The popup lives inside the window so it inherits the CTS theme.
        /// </summary>
        private void ShowVariationPopup(FrameworkElement target, IList<string> names, string current, bool includeAuto, Action<string> onPick)
        {
            List<string> labels = new List<string>();
            if (includeAuto) labels.Add("Auto (by size)");
            labels.AddRange(names);

            ListBox list = new ListBox
            {
                Style = (Style)FindResource("CtsListBoxStyle"),
                MinWidth = 150,
                MaxHeight = 280
            };

            int selected = -1;
            for (int i = 0; i < labels.Count; i++)
            {
                list.Items.Add(CreateTextItem(labels[i], i));

                if (includeAuto && i == 0 && string.IsNullOrWhiteSpace(current)) selected = 0;
                else if (!string.IsNullOrWhiteSpace(current) && string.Equals(labels[i], current, StringComparison.OrdinalIgnoreCase)) selected = i;
            }
            if (selected >= 0) list.SelectedIndex = selected;

            Popup popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = (Brush)FindResource("SurfaceBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(3),
                    Child = list
                }
            };

            // Attaching the popup to the window tree lets its content find the theme resources.
            RootGrid.Children.Add(popup);

            bool ready = false;
            list.SelectionChanged += delegate
            {
                if (!ready) return;
                ListBoxItem item = list.SelectedItem as ListBoxItem;
                if (item == null) return;

                int index = (int)item.Tag;
                popup.IsOpen = false;

                string pickedName = (includeAuto && index == 0) ? null : labels[index];
                onPick(pickedName);
            };

            popup.Closed += delegate { RootGrid.Children.Remove(popup); };

            popup.IsOpen = true;
            ready = true;
        }

        // ============================================================
        // SEQUENCE (right side)
        // ============================================================

        private void OnCatalogItemClicked(CatalogButton button, string conditionName)
        {
            CatalogItemRef item = new CatalogItemRef
            {
                ServiceName = button.ServiceName,
                PaletteName = button.PaletteName,
                ButtonName = button.Name,
                ButtonCode = button.Code,
                PaletteIndex = button.PaletteIndex,
                ButtonIndex = button.ButtonIndex,
                ConditionName = conditionName,
                ConditionNames = new List<string>(button.ConditionNames),
                IconFile = AssemblyIconCache.SaveIcon(button.ServiceName, button.PaletteName, button.Name, button.PngBytes)
            };

            int target = -1;
            if (_replaceIndex >= 0 && _replaceIndex < _working.Steps.Count)
            {
                target = _replaceIndex;
            }
            else
            {
                int selected = StepsList.SelectedIndex;
                if (selected >= 0 && selected < _working.Steps.Count && !_working.Steps[selected].Item.IsAssigned)
                    target = selected;
            }

            if (target >= 0)
            {
                AssemblyStep step = _working.Steps[target];
                step.Item = item;
                step.Label = button.Name;
                _replaceIndex = -1;

                // Jump to the next empty step so the user can keep clicking items in order.
                int next = FindNextUnassigned(target);
                RefreshSteps(next >= 0 ? next : target);
            }
            else
            {
                _working.Steps.Add(new AssemblyStep { Label = button.Name, Item = item });
                RefreshSteps(_working.Steps.Count - 1);
            }
        }

        private int FindNextUnassigned(int after)
        {
            for (int i = after + 1; i < _working.Steps.Count; i++)
            {
                if (!_working.Steps[i].Item.IsAssigned) return i;
            }
            return -1;
        }

        private void RefreshSteps(int selectIndex)
        {
            _suspend = true;

            StepsList.Items.Clear();
            for (int i = 0; i < _working.Steps.Count; i++)
                StepsList.Items.Add(CreateStepItem(i));

            if (selectIndex >= 0 && selectIndex < StepsList.Items.Count)
                StepsList.SelectedIndex = selectIndex;

            _suspend = false;

            LoadStepSettings();
            UpdateHint();
        }

        private ListBoxItem CreateStepItem(int index)
        {
            AssemblyStep step = _working.Steps[index];
            bool assigned = step.Item != null && step.Item.IsAssigned;
            bool replaceArmed = _replaceIndex == index;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock number = new TextBlock
            {
                Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PrimaryBrush")
            };
            Grid.SetColumn(number, 0);
            grid.Children.Add(number);

            Border thumb = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)FindResource("InputBrush"),
                BorderBrush = (Brush)FindResource(assigned ? "BorderBrush" : "PrimaryBrush")
            };
            BitmapSource icon = assigned ? AssemblyIconCache.Load(step.Item.IconFile) : null;
            if (icon != null)
            {
                thumb.Child = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            }
            else
            {
                thumb.Child = new TextBlock
                {
                    Text = assigned ? step.Item.ButtonName.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource(assigned ? "TextBrush" : "PrimaryBrush")
                };
            }
            Grid.SetColumn(thumb, 1);
            grid.Children.Add(thumb);

            TextBlock summary = new TextBlock
            {
                FontSize = 9,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            summary.Text = BuildSummary(step);

            StackPanel texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = assigned ? step.Item.FullName : "Assign an item: " + (string.IsNullOrWhiteSpace(step.Label) ? "click one in the database" : step.Label),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource(assigned ? "TextBrush" : "PrimaryBrush")
            });
            texts.Children.Add(new TextBlock
            {
                Text = assigned ? step.Item.PaletteName : "Waiting for a database item",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(summary);
            Grid.SetColumn(texts, 2);
            grid.Children.Add(texts);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int captured = index;
            if (assigned && step.Item.HasVariations)
            {
                Button variation = CreateRowButton("\u25BE", "Variation of this item (x1.5, x3...)", true, null, !string.IsNullOrWhiteSpace(step.Item.ConditionName));
                variation.Width = 30;
                variation.Click += delegate
                {
                    ShowVariationPopup(variation, _working.Steps[captured].Item.ConditionNames, _working.Steps[captured].Item.ConditionName, true,
                        delegate (string picked) { SetVariation(captured, picked); });
                };
                buttons.Children.Add(variation);
            }
            buttons.Children.Add(CreateRowButton("+", "Insert an empty step after this one", true, delegate { InsertStepAfter(captured); }, false));
            buttons.Children.Add(CreateRowButton("\u25B2", "Move up", index > 0, delegate { MoveStep(captured, -1); }, false));
            buttons.Children.Add(CreateRowButton("\u25BC", "Move down", index < _working.Steps.Count - 1, delegate { MoveStep(captured, 1); }, false));
            buttons.Children.Add(CreateRowButton("\u21C4", "Replace: then click another item in the database", true, delegate { ToggleReplace(captured); }, replaceArmed));
            buttons.Children.Add(CreateRowButton("\u2715", "Remove this step", true, delegate { RemoveStep(captured); }, false));
            Grid.SetColumn(buttons, 3);
            grid.Children.Add(buttons);

            return new ListBoxItem
            {
                Content = grid,
                Tag = summary,
                Style = (Style)FindResource("CtsListItemStyle")
            };
        }

        private Button CreateRowButton(string glyph, string tooltip, bool enabled, RoutedEventHandler onClick, bool highlighted)
        {
            Button button = new Button
            {
                Content = glyph,
                ToolTip = tooltip,
                Width = 28,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                FontSize = 11,
                IsEnabled = enabled
            };

            if (highlighted)
            {
                button.Background = (Brush)FindResource("PrimaryBrush");
                button.Foreground = (Brush)FindResource("AccentTextBrush");
            }

            if (onClick != null) button.Click += onClick;
            return button;
        }

        private static string BuildSummary(AssemblyStep step)
        {
            List<string> parts = new List<string>();

            if (step.Item != null && step.Item.HasVariations)
            {
                parts.Add(string.IsNullOrWhiteSpace(step.Item.ConditionName)
                    ? "variation: auto (by size)"
                    : "variation: " + step.Item.ConditionName);
            }

            if (step.SizeSource == StepSizeSource.Fixed) parts.Add("size: " + step.FixedSize);

            if (!string.IsNullOrWhiteSpace(step.ProductEntry))
                parts.Add("entry: " + step.ProductEntry);

            if (step.Connector == StepConnector.First) parts.Add("first end");
            else if (step.Connector == StepConnector.Second) parts.Add("second end");

            if (Math.Abs(step.RotationDegrees) > 1e-6)
                parts.Add("rotated " + step.RotationDegrees.ToString("0.##", CultureInfo.InvariantCulture) + "\u00B0");

            return parts.Count == 0 ? "size: same as the previous part" : string.Join("  |  ", parts);
        }

        private void SetVariation(int index, string conditionName)
        {
            if (index < 0 || index >= _working.Steps.Count) return;
            _working.Steps[index].Item.ConditionName = conditionName;
            RefreshSteps(index);
        }

        private void InsertStepAfter(int index)
        {
            int position = Math.Min(Math.Max(index + 1, 0), _working.Steps.Count);
            _working.Steps.Insert(position, new AssemblyStep { Label = "New item" });
            RefreshSteps(position);
        }

        private void AddStep_Click(object sender, RoutedEventArgs e)
        {
            _working.Steps.Add(new AssemblyStep { Label = "New item" });
            RefreshSteps(_working.Steps.Count - 1);
        }

        private void DuplicateStep_Click(object sender, RoutedEventArgs e)
        {
            int index = StepsList.SelectedIndex;
            if (index < 0 || index >= _working.Steps.Count)
            {
                BuilderStatus.Text = "Select a step to duplicate.";
                return;
            }

            _working.Steps.Insert(index + 1, _working.Steps[index].Clone());
            RefreshSteps(index + 1);
        }

        private void MoveStep(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || target < 0 || index >= _working.Steps.Count || target >= _working.Steps.Count) return;

            AssemblyStep moved = _working.Steps[index];
            _working.Steps.RemoveAt(index);
            _working.Steps.Insert(target, moved);

            if (_replaceIndex == index) _replaceIndex = target;
            RefreshSteps(target);
        }

        private void ToggleReplace(int index)
        {
            _replaceIndex = _replaceIndex == index ? -1 : index;
            RefreshSteps(index);
        }

        private void RemoveStep(int index)
        {
            if (index < 0 || index >= _working.Steps.Count) return;

            _working.Steps.RemoveAt(index);

            if (_replaceIndex == index) _replaceIndex = -1;
            else if (_replaceIndex > index) _replaceIndex--;

            RefreshSteps(Math.Min(index, _working.Steps.Count - 1));
        }

        private void UpdateHint()
        {
            int selected = StepsList.SelectedIndex;

            if (_replaceIndex >= 0)
            {
                ModeHint.Text = "Click an item in the database to REPLACE step " + (_replaceIndex + 1) + ".";
            }
            else if (selected >= 0 && selected < _working.Steps.Count && !_working.Steps[selected].Item.IsAssigned)
            {
                AssemblyStep step = _working.Steps[selected];
                ModeHint.Text = "Click an item in the database to assign step " + (selected + 1) +
                                (string.IsNullOrWhiteSpace(step.Label) ? "." : " (" + step.Label + ").");
            }
            else
            {
                ModeHint.Text = "Click an item in the database to ADD it at the end (use the arrow on an item to pick its exact variation). " +
                                "The olet is not part of the list: you model it on the pipe and select it.";
            }

            int missing = _working.UnassignedCount;
            BuilderStatus.Text = _working.Steps.Count + " step(s)" +
                                 (missing > 0 ? ", " + missing + " still without a database item." : ".");
        }

        // ============================================================
        // STEP SETTINGS
        // ============================================================

        private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspend) return;
            LoadStepSettings();
            UpdateHint();
        }

        private AssemblyStep SelectedStep()
        {
            int index = StepsList.SelectedIndex;
            if (index < 0 || index >= _working.Steps.Count) return null;
            return _working.Steps[index];
        }

        private void LoadStepSettings()
        {
            AssemblyStep step = SelectedStep();
            if (step == null)
            {
                StepSettingsCard.Visibility = Visibility.Collapsed;
                return;
            }

            _suspend = true;

            StepSettingsCard.Visibility = Visibility.Visible;
            StepSettingsTitle.Text = "STEP " + (StepsList.SelectedIndex + 1) + " SETTINGS - " + step.DisplayName.ToUpperInvariant();

            SizePreviousRadio.IsChecked = step.SizeSource != StepSizeSource.Fixed;
            SizeFixedRadio.IsChecked = step.SizeSource == StepSizeSource.Fixed;
            FixedSizeBox.Text = step.FixedSize ?? string.Empty;
            FixedSizeBox.IsEnabled = step.SizeSource == StepSizeSource.Fixed;

            RotationBox.Text = step.RotationDegrees.ToString("0.##", CultureInfo.InvariantCulture);

            ConnAutoRadio.IsChecked = step.Connector == StepConnector.Auto;
            ConnFirstRadio.IsChecked = step.Connector == StepConnector.First;
            ConnSecondRadio.IsChecked = step.Connector == StepConnector.Second;

            LoadProductEntries(step);

            _suspend = false;
        }

        /// <summary>
        /// Fills the PRODUCT ENTRY dropdown with the entries captured the last time
        /// this item was placed, and keeps whatever the user typed. The list is
        /// empty on a brand new assembly - Revit does not expose a part's product
        /// list until the part exists in the model - so the box stays editable and
        /// typing the value is always available.
        /// </summary>
        private void LoadProductEntries(AssemblyStep step)
        {
            FillProductEntryBox(step);

            // Not cached yet: ask Revit for the real catalogue. It answers through
            // the external event because reading a product list means creating a
            // sample part, and that needs the API context.
            bool assigned = step.Item != null && step.Item.IsAssigned;
            bool cached = step.Item != null &&
                          step.Item.ProductEntryNames != null &&
                          step.Item.ProductEntryNames.Count > 0;

            if (!assigned || cached) return;

            AssemblyStep requested = step;
            ProductEntryHint.Text = "Reading the product list...";

            _controller.RequestProductEntries(
                Dispatcher,
                step.Item,
                ProbeSizeFeet(step),
                delegate (List<string> entries, string error)
                {
                    if (requested.Item != null && entries != null && entries.Count > 0)
                        requested.Item.ProductEntryNames = entries;

                    // The user may have moved on while Revit was answering.
                    if (!ReferenceEquals(SelectedStep(), requested)) return;

                    _suspend = true;
                    FillProductEntryBox(requested);
                    _suspend = false;

                    if (entries != null && entries.Count > 0)
                        ProductEntryHint.Text = entries.Count + " entry(ies) available at " + ProbeSizeLabel(requested) + ".";
                    else
                        ProductEntryHint.Text = string.IsNullOrWhiteSpace(error)
                            ? "This item has no product list."
                            : error;
                });
        }

        private void FillProductEntryBox(AssemblyStep step)
        {
            ProductEntryBox.Items.Clear();
            ProductEntryBox.Items.Add(AutoProductEntry);

            List<string> known = step.Item == null ? null : step.Item.ProductEntryNames;
            if (known != null)
            {
                foreach (string entry in known)
                {
                    if (!string.IsNullOrWhiteSpace(entry)) ProductEntryBox.Items.Add(entry);
                }
            }

            ProductEntryBox.Text = string.IsNullOrWhiteSpace(step.ProductEntry)
                ? AutoProductEntry
                : step.ProductEntry;

            if (known != null && known.Count > 0)
                ProductEntryHint.Text = known.Count + " entry(ies) available at " + ProbeSizeLabel(step) + ".";
            else
                ProductEntryHint.Text = string.Empty;
        }

        /// <summary>
        /// The size the product list is read at. A product list is size-dependent -
        /// a bushing built at 3/4" only offers 3/4x... entries - so the step's fixed
        /// size is used when it has one, and 3/4" otherwise. Placement re-reads the
        /// real list at the real connection size and corrects this.
        /// </summary>
        private static double ProbeSizeFeet(AssemblyStep step)
        {
            if (step.SizeSource == StepSizeSource.Fixed)
            {
                double inches;
                if (AssemblyUnits.TryParseInches(step.FixedSize, out inches) && inches > 0)
                    return inches / 12.0;
            }

            return 0.75 / 12.0;   // 3/4", the usual olet outlet
        }

        private static string ProbeSizeLabel(AssemblyStep step)
        {
            double feet = ProbeSizeFeet(step);
            return AssemblyUnits.FormatInches(feet * 12.0);
        }

        /// <summary>Shown instead of an empty string so the row does not look broken.</summary>
        private const string AutoProductEntry = "(auto)";

        private void ProductEntry_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyProductEntry();
        }

        private void ProductEntry_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyProductEntry();
        }

        private void ApplyProductEntry()
        {
            if (_suspend) return;

            AssemblyStep step = SelectedStep();
            if (step == null) return;

            string text = (ProductEntryBox.Text ?? string.Empty).Trim();
            step.ProductEntry = string.Equals(text, AutoProductEntry, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : text;

            RefreshSelectedRowSummary(step);
        }

        private void StepSetting_Changed(object sender, RoutedEventArgs e)
        {
            ApplyStepSettings();
        }

        private void StepSetting_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyStepSettings();
        }

        private void ApplyStepSettings()
        {
            if (_suspend) return;

            AssemblyStep step = SelectedStep();
            if (step == null) return;

            step.SizeSource = SizeFixedRadio.IsChecked == true ? StepSizeSource.Fixed : StepSizeSource.Previous;

            FixedSizeBox.IsEnabled = step.SizeSource == StepSizeSource.Fixed;
            step.FixedSize = (FixedSizeBox.Text ?? string.Empty).Trim();

            double rotation;
            if (TryParseNumber(RotationBox.Text, out rotation)) step.RotationDegrees = rotation;

            if (ConnFirstRadio.IsChecked == true) step.Connector = StepConnector.First;
            else if (ConnSecondRadio.IsChecked == true) step.Connector = StepConnector.Second;
            else step.Connector = StepConnector.Auto;

            RefreshSelectedRowSummary(step);
        }

        /// <summary>
        /// Updates the small summary line of the selected row without rebuilding the
        /// list: rebuilding would steal the keyboard focus while the user is typing.
        /// </summary>
        private void RefreshSelectedRowSummary(AssemblyStep step)
        {
            ListBoxItem row = StepsList.SelectedItem as ListBoxItem;
            TextBlock summary = row == null ? null : row.Tag as TextBlock;
            if (summary != null) summary.Text = BuildSummary(step);
        }

        private static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(
                (text ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        // ============================================================
        // SAVE / CANCEL
        // ============================================================

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = (NameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                BuilderStatus.Text = "Give the assembly a name.";
                NameBox.Focus();
                return;
            }

            if (_working.Steps.Count == 0)
            {
                BuilderStatus.Text = "Add at least one item to the sequence.";
                return;
            }

            string shortName = (ShortNameBox.Text ?? string.Empty).Trim();
            if (shortName.Length == 0) shortName = name.Length >= 2 ? name.Substring(0, 2) : name;

            _working.Name = name;
            _working.ShortName = shortName.ToUpperInvariant();

            if (_onSave != null) _onSave(_working);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

using Autodesk.Revit.DB;
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
using CTSRevitPlugin.UI.ElementTools;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class MechanicalPropertiesPane : Page, IDockablePaneProvider, IUtilitiesPaneHost
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("A2E4D6F8-5B1C-4D93-9A77-61F20C8E3504"));

        public static MechanicalPropertiesPane Instance { get; private set; }

        private UtilitiesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;

        private const string SearchPlaceholder = "Search properties";

        public MechanicalPropertiesPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

            // Edits reach Revit through an external event, the same way the CTS
            // Parameters pane does it.
            _handler = new UtilitiesExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _handler.Pane = this;

            Clear();

            _refreshTimer.Tick += delegate
            {
                _refreshTimer.Stop();
                ApplyPendingSelection();
            };

            IsVisibleChanged += delegate
            {
                if (IsVisible) ApplyPendingSelection();
            };
        }

        // ============================================================
        // BRANDING
        // ============================================================

        private void LoadBranding()
        {
            string root = Path.Combine(
                Path.GetDirectoryName(typeof(MechanicalPropertiesPane).Assembly.Location),
                "Resources", "Icons");

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

        // ============================================================
        // STATUS
        // ============================================================

        public void SetToolStatus(string message)
        {
            if (StatusText == null) return;

            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? ""; }));
        }

        public void SetHangerAdjustmentStatus(string message) { SetToolStatus(message); }

        public void DisposeExternalEvent()
        {
            try
            {
                if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; }
            }
            catch { }
        }

        // ============================================================
        // SELECTION
        //
        // Nothing is inspected inline on a selection change. The click stores
        // the ids and restarts a short timer, so a burst of clicks costs one
        // refresh instead of one per click, and a closed pane costs nothing.
        // ============================================================

        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer =
            new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

        private Document _pendingDocument;
        private List<ElementId> _pendingElementIds;
        private bool _pendingDirty;

        public static void OnSelectionChanged(object sender, Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            if (Instance == null || e == null) return;

            try
            {
                ICollection<ElementId> ids = e.GetSelectedElements();
                Document doc = e.GetDocument();

                bool empty = ids == null || ids.Count == 0;

                Instance._pendingDocument = empty ? null : doc;
                Instance._pendingElementIds = empty ? null : ids.ToList();
                Instance._pendingDirty = true;

                if (!PaneVisibility.IsShown(doc, PaneId)) return;

                Instance._refreshTimer.Stop();
                Instance._refreshTimer.Start();
            }
            catch { }
        }

        /// <summary>
        /// Catches up with a selection that arrived while the pane was hidden.
        ///
        /// This runs from WPF - a DispatcherTimer tick, or IsVisibleChanged when
        /// Revit shows the pane's host window again - which is NOT a Revit API
        /// context. Reading the DB from here kills Revit outright: undocking a
        /// pane fires IsVisibleChanged in the middle of Revit's own docking
        /// relayout and view-activation notification, and a Document captured
        /// before the model was swapped is already dead by then. The result is a
        /// native access violation ("An unrecoverable error has occurred"), which
        /// no catch block can intercept.
        ///
        /// So nothing is read here. The work is handed to the ExternalEvent,
        /// which runs inside a valid API context and resolves the LIVE document
        /// and selection through RefreshFromUIApplication.
        /// </summary>
        private void ApplyPendingSelection()
        {
            if (!_pendingDirty) return;
            _pendingDirty = false;

            RequestRefreshInApiContext();
        }

        /// <summary>
        /// Asks Revit for a valid API context and refreshes there. Safe to call
        /// from any WPF handler: ExternalEvent.Raise is the one entry point that
        /// does not need a context of its own.
        /// </summary>
        private void RequestRefreshInApiContext()
        {
            if (_handler == null || _externalEvent == null) return;

            _handler.RequestRefresh();
            try { _externalEvent.Raise(); }
            catch { }
        }

        /// <summary>
        /// A closed Document still looks like a live object to C#. IsValidObject
        /// is the only cheap way to ask Revit whether the managed wrapper still
        /// points at something real.
        /// </summary>
        private static bool IsUsable(Document doc)
        {
            try { return doc != null && doc.IsValidObject; }
            catch { return false; }
        }

        /// <summary>
        /// Drops the cached document and selection so nothing in the pane can
        /// outlive the model it came from. Called on DocumentClosing.
        /// </summary>
        public void ClearCachedSelection()
        {
            _pendingDocument = null;
            _pendingElementIds = null;
            _pendingDirty = false;

            try { _refreshTimer.Stop(); } catch { }

            if (Dispatcher.CheckAccess()) Clear();
            else Dispatcher.BeginInvoke(new Action(Clear));
        }

        public void RefreshFromUIApplication(UIApplication uiapp)
        {
            if (uiapp == null || uiapp.ActiveUIDocument == null) { Clear(); return; }

            List<ElementId> ids = uiapp.ActiveUIDocument.Selection.GetElementIds().ToList();
            if (ids.Count == 0) { Clear(); return; }

            _pendingDocument = uiapp.ActiveUIDocument.Document;
            _pendingElementIds = ids;

            ShowElements(_pendingDocument, ids);
        }

        // ============================================================
        // ELEMENT LIMIT
        //
        // Reading fabrication data is not free even with a fixed field list, so
        // a big selection is capped rather than allowed to freeze Revit. The
        // limit is a user setting; 100 is the default.
        // ============================================================

        private static int ElementLimit
        {
            get
            {
                ElementToolsConfiguration config = UtilitiesConfigurationStore.Current;
                int limit = config.MechanicalPropertiesElementLimit;
                return limit <= 0 ? 100 : limit;
            }
        }

        private MechanicalPropertiesSnapshot _currentSnapshot;

        private void ShowElements(Document doc, IList<ElementId> ids)
        {
            if (!IsUsable(doc) || ids == null || ids.Count == 0) { Clear(); return; }

            int limit = ElementLimit;

            List<Element> elements = new List<Element>();
            int skipped = 0;

            foreach (ElementId id in ids)
            {
                if (elements.Count >= limit) { skipped++; continue; }

                Element element = null;
                try { element = doc.GetElement(id); }
                catch { }

                if (element is FabricationPart) elements.Add(element);
            }

            if (elements.Count == 0) { Clear(); return; }

            MechanicalPropertiesSnapshot snapshot = MechanicalPropertiesInspector.Inspect(elements);
            snapshot.SkippedByLimit = skipped;

            ShowSnapshot(snapshot);
        }

        public void ShowSnapshot(MechanicalPropertiesSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ElementCount == 0) { Clear(); return; }

            _currentSnapshot = snapshot;
            _pendingEdits.Clear();
            _ancillaries = null;

            IdText.Text = snapshot.ElementId;
            CategoryText.Text = snapshot.Category;
            FamilyText.Text = snapshot.Family;
            TypeText.Text = snapshot.Type;

            HeaderSubtitle.Text = snapshot.ElementCount + " item(s) reported";

            string status = "Ready";
            if (snapshot.SkippedByLimit > 0)
            {
                status = snapshot.SkippedByLimit +
                         " element(s) beyond the " + ElementLimit +
                         " element limit were not read. Raise the limit in CTS settings if you need them.";
            }

            SetToolStatus(status);
            UpdateApplyState();
            BuildRows();
        }

        // ============================================================
        // PENDING EDITS
        //
        // Nothing is written as it is typed. Edits accumulate here and APPLY
        // sends them in one go, which keeps one user action inside one Revit
        // transaction instead of one transaction per keystroke.
        // ============================================================

        private readonly Dictionary<string, MechanicalEdit> _pendingEdits =
            new Dictionary<string, MechanicalEdit>(StringComparer.Ordinal);

        private static string EditKey(MechanicalPropertyItem item)
        {
            return item.Kind + "\u0001" + (item.Key ?? item.Name);
        }

        private void StageEdit(MechanicalPropertyItem item, string newValue)
        {
            if (item == null) return;

            string key = EditKey(item);
            string original = item.Value ?? "";

            if (string.Equals(newValue ?? "", original, StringComparison.Ordinal))
            {
                _pendingEdits.Remove(key);
            }
            else
            {
                _pendingEdits[key] = new MechanicalEdit
                {
                    IsDimension = item.Kind == MechanicalPropertyKind.Dimension,
                    Name = item.Key ?? item.Name,
                    Value = newValue ?? ""
                };
            }

            UpdateApplyState();
        }

        private void UpdateApplyState()
        {
            if (ApplyButton == null) return;

            ApplyButton.IsEnabled = _pendingEdits.Count > 0;

            if (_pendingEdits.Count > 0)
                SetToolStatus(_pendingEdits.Count + " pending change(s). Press APPLY to write them.");
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSnapshot == null || _pendingEdits.Count == 0) return;
            if (_handler == null || _externalEvent == null) return;

            _handler.RequestMechanicalEdits(
                _currentSnapshot.SourceElementIds,
                _pendingEdits.Values.ToList());

            try { _externalEvent.Raise(); }
            catch (Exception ex) { SetToolStatus("Could not apply: " + ex.Message); }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_handler == null || _externalEvent == null) return;

            // Drops anything not applied: REFRESH means "show me what the model
            // actually holds", so keeping edits would misrepresent it.
            _pendingEdits.Clear();
            UpdateApplyState();

            // The cached configuration is keyed by document, so switching models
            // rebuilds it on its own. Loading a different fabrication
            // configuration into the SAME model does not, and REFRESH is the
            // obvious place for the user to force that.
            FabricationConfigurationCache.Invalidate();

            _handler.RequestRefresh();

            try { _externalEvent.Raise(); }
            catch (Exception ex) { SetToolStatus("Could not refresh: " + ex.Message); }
        }

        // ============================================================
        // ANCILLARIES - LOADED ONLY WHEN ASKED FOR
        // ============================================================

        private List<MechanicalPropertyItem> _ancillaries;

        private void RequestAncillaries()
        {
            if (_currentSnapshot == null || _currentSnapshot.SourceElementId == null) return;
            if (_handler == null || _externalEvent == null) return;

            SetToolStatus("Reading ancillary data...");

            _handler.RequestAncillaries(_currentSnapshot.SourceElementId);

            try { _externalEvent.Raise(); }
            catch (Exception ex) { SetToolStatus("Could not read ancillaries: " + ex.Message); }
        }

        /// <summary>
        /// Called from the external event once an APPLY has been written.
        /// Re-reading matters: Revit may snap a fabrication dimension to the
        /// nearest size its definition accepts, so the typed value and the
        /// stored value are not always the same.
        /// </summary>
        public void ReloadAfterApply(Document doc, IList<ElementId> ids)
        {
            if (doc == null || ids == null || ids.Count == 0) return;

            Action reload = delegate
            {
                // ShowElements resets the status line, but the handler has just
                // put the result of the write there and that is what the user
                // needs to read, so it is restored afterwards.
                string result = StatusText == null ? null : StatusText.Text;

                _pendingEdits.Clear();
                _ancillaries = null;
                ShowElements(doc, ids);

                if (!string.IsNullOrWhiteSpace(result)) SetToolStatus(result);
            };

            if (Dispatcher.CheckAccess()) reload();
            else Dispatcher.BeginInvoke(reload);
        }

        public void ShowAncillaries(List<MechanicalPropertyItem> items)
        {
            _ancillaries = items ?? new List<MechanicalPropertyItem>();
            SetToolStatus(_ancillaries.Count + " ancillary row(s).");
            BuildRows();
        }

        // ============================================================
        // COLLAPSED SECTIONS
        // ============================================================

        private readonly HashSet<string> _collapsed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void ToggleSection(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) return;

            if (!_collapsed.Remove(group)) _collapsed.Add(group);
            BuildRows();
        }

        // ============================================================
        // ROW BUILDING
        //
        // FindResource walks the resource tree up to the Application on every
        // call, so the lookups happen once per build and the rows reuse them.
        // ============================================================

        private Style _cellStyle;
        private Style _cellReadOnlyStyle;
        private Style _rowButtonStyle;
        private Style _sectionHeaderStyle;
        private Brush _textBrush;
        private Brush _inputBrush;
        private Brush _inputBorderBrush;

        private void CacheResources()
        {
            _cellStyle = RevitThemeService.StyleOrNull(this, "Cell");
            _cellReadOnlyStyle = RevitThemeService.StyleOrNull(this, "CellReadOnly");
            _rowButtonStyle = RevitThemeService.StyleOrNull(this, "RowButton");
            _sectionHeaderStyle = RevitThemeService.StyleOrNull(this, "SectionHeader");
            _textBrush = RevitThemeService.ThemeBrush(this, "TextBrush");
            _inputBrush = RevitThemeService.ThemeBrush(this, "InputBrush");
            _inputBorderBrush = RevitThemeService.ThemeBrush(this, "InputBorderBrush");
        }

        /// <summary>
        /// Fenced off because every caller is a WPF handler. An exception that
        /// escapes a WPF handler inside Revit is not shown to anyone - it
        /// terminates the process (journal: ExceptionCode=0xe0434352). A failure
        /// here becomes a status line instead.
        /// </summary>
        private void BuildRows()
        {
            try { BuildRowsCore(); }
            catch (Exception ex)
            {
                try { SetToolStatus("Could not refresh: " + ex.Message); }
                catch { }
            }
        }

        private void BuildRowsCore()
        {
            PropertiesPanel.Children.Clear();
            if (_currentSnapshot == null) return;

            CacheResources();

            string filter = GetSearchText();

            List<MechanicalPropertyItem> items = _currentSnapshot.Items
                .Where(item => Matches(item, filter))
                .ToList();

            // The ancillary rows replace the "..." action row once they arrive.
            if (_ancillaries != null && _ancillaries.Count > 0)
            {
                items = items
                    .Where(x => !(x.Group == MechanicalPropertiesInspector.GroupAncillaries &&
                                  x.Kind == MechanicalPropertyKind.Action))
                    .ToList();

                items.AddRange(_ancillaries.Where(item => Matches(item, filter)));
            }

            // Section order is fixed rather than inherited from the order the
            // rows happen to be built in: Ancillaries is appended last but
            // belongs above Custom Data.
            foreach (IGrouping<string, MechanicalPropertyItem> group in
                     items.GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                          .OrderBy(g => SectionOrder(g.Key)))
            {
                bool collapsed = _collapsed.Contains(group.Key);

                PropertiesPanel.Children.Add(BuildSectionHeader(group.Key, group.Count(), collapsed));
                if (collapsed) continue;

                foreach (MechanicalPropertyItem item in group)
                    PropertiesPanel.Children.Add(BuildRow(item));
            }

            if (PropertiesPanel.Children.Count == 0)
            {
                PropertiesPanel.Children.Add(new TextBlock
                {
                    Text = "Nothing matches the current search.",
                    Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                    Margin = new Thickness(10),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private static readonly string[] SectionSequence =
        {
            MechanicalPropertiesInspector.GroupGeneral,
            MechanicalPropertiesInspector.GroupConnectors,
            MechanicalPropertiesInspector.GroupDimensions,
            MechanicalPropertiesInspector.GroupAncillaries,
            MechanicalPropertiesInspector.GroupCustomData
        };

        private static int SectionOrder(string group)
        {
            for (int i = 0; i < SectionSequence.Length; i++)
            {
                if (string.Equals(SectionSequence[i], group, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return SectionSequence.Length;
        }

        private UIElement BuildSectionHeader(string group, int count, bool collapsed)
        {
            Button header = new Button
            {
                Style = _sectionHeaderStyle,
                ToolTip = collapsed ? "Expand " + group : "Collapse " + group
            };

            System.Windows.Controls.Grid layout = new System.Windows.Controls.Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = group,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = RevitThemeService.ThemeBrush(this, "PrimaryBrush")
            };
            System.Windows.Controls.Grid.SetColumn(title, 0);
            layout.Children.Add(title);

            TextBlock marker = new TextBlock
            {
                Text = (collapsed ? "\u25B8 " : "\u25BE ") + count,
                FontSize = 10,
                Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush")
            };
            System.Windows.Controls.Grid.SetColumn(marker, 1);
            layout.Children.Add(marker);

            header.Content = layout;

            string captured = group;
            header.Click += delegate { ToggleSection(captured); };

            return header;
        }

        private UIElement BuildRow(MechanicalPropertyItem item)
        {
            System.Windows.Controls.Grid row =
                new System.Windows.Controls.Grid { Margin = new Thickness(3, 1, 3, 1) };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock name = new TextBlock
            {
                Text = item.Name,
                Style = _cellStyle,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = item.Name
            };
            System.Windows.Controls.Grid.SetColumn(name, 0);
            row.Children.Add(name);

            UIElement value = BuildValueEditor(item);
            System.Windows.Controls.Grid.SetColumn(value, 1);
            row.Children.Add(value);

            if (item.Kind != MechanicalPropertyKind.Action)
                row.Children.Add(BuildCopyButton(item));

            return row;
        }

        private UIElement BuildValueEditor(MechanicalPropertyItem item)
        {
            // The "..." row: a button that loads the ancillaries on demand.
            if (item.Kind == MechanicalPropertyKind.Action)
            {
                Button open = new Button
                {
                    Content = "...",
                    Height = 22,
                    Margin = new Thickness(6, 2, 6, 2),
                    FontSize = 11,
                    ToolTip = "Load the ancillary data for this part."
                };
                open.Click += delegate { RequestAncillaries(); };
                return open;
            }

            bool editable = item.IsEditable &&
                            _currentSnapshot != null &&
                            _currentSnapshot.SourceElementIds.Count > 0;

            if (!editable)
            {
                return new TextBlock
                {
                    Text = item.Value,
                    Style = _cellReadOnlyStyle,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = string.IsNullOrWhiteSpace(item.Value) ? null : item.Value
                };
            }

            // A configuration-backed list (Status) becomes a real dropdown, so
            // the user cannot type a value the configuration would reject.
            if (item.Choices != null && item.Choices.Count > 0)
            {
                WpfComboBox combo = new WpfComboBox
                {
                    Height = 24,
                    Margin = new Thickness(6, 2, 6, 2),
                    FontSize = 11,
                    IsEditable = false
                };

                foreach (string choice in item.Choices) combo.Items.Add(choice);

                if (!string.IsNullOrWhiteSpace(item.Value) && combo.Items.Contains(item.Value))
                    combo.SelectedItem = item.Value;

                MechanicalPropertyItem captured = item;
                combo.SelectionChanged += delegate
                {
                    StageEdit(captured, combo.SelectedItem as string);
                };

                return combo;
            }

            WpfTextBox box = new WpfTextBox
            {
                Text = item.Value,
                FontSize = 11,
                Margin = new Thickness(6, 2, 6, 2),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = _textBrush,
                Background = _inputBrush,
                BorderBrush = _inputBorderBrush,
                BorderThickness = new Thickness(1),
                ToolTip = item.Kind == MechanicalPropertyKind.Dimension
                    ? "Fabrication dimension. Imperial, e.g. 1' - 4 1/2\""
                    : "Edit " + item.Name
            };

            MechanicalPropertyItem capturedItem = item;

            box.LostKeyboardFocus += delegate { StageEdit(capturedItem, box.Text); };
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    box.Text = capturedItem.Value ?? "";
                    StageEdit(capturedItem, box.Text);
                    e.Handled = true;
                    Keyboard.ClearFocus();
                    return;
                }

                if (e.Key != Key.Enter) return;

                StageEdit(capturedItem, box.Text);
                e.Handled = true;
            };

            return box;
        }

        private UIElement BuildCopyButton(MechanicalPropertyItem item)
        {
            string copyValue = item.Value;

            Button copy = new Button
            {
                Content = "\u29C9",
                Style = _rowButtonStyle,
                ToolTip = "Copy value"
            };

            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(copyValue ?? "");
                    copy.Content = "\u2713";
                    SetToolStatus(item.Name + " copied.");
                }
                catch { copy.Content = "!"; }
            };

            System.Windows.Controls.Grid.SetColumn(copy, 2);
            return copy;
        }

        private static bool Matches(MechanicalPropertyItem item, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;

            return (item.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (item.Value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (item.Group ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ============================================================
        // SEARCH
        // ============================================================

        private string GetSearchText()
        {
            if (SearchTextBox == null) return string.Empty;

            string text = SearchTextBox.Text ?? string.Empty;
            return string.Equals(text, SearchPlaceholder, StringComparison.Ordinal)
                ? string.Empty
                : text.Trim();
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(SearchTextBox.Text, SearchPlaceholder, StringComparison.Ordinal))
            {
                SearchTextBox.Text = string.Empty;
                SearchTextBox.Foreground = RevitThemeService.ThemeBrush(this, "TextBrush");
            }
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                SearchTextBox.Text = SearchPlaceholder;
                SearchTextBox.Foreground = RevitThemeService.ThemeBrush(this, "TextMutedBrush");
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentSnapshot != null) BuildRows();
        }

        // ============================================================
        // EMPTY STATE
        // ============================================================

        private void Clear()
        {
            _currentSnapshot = null;
            _ancillaries = null;
            _pendingEdits.Clear();

            IdText.Text = "-";
            CategoryText.Text = "-";
            FamilyText.Text = "-";
            TypeText.Text = "-";
            HeaderSubtitle.Text = "Select an MEP Fabrication element";

            if (ApplyButton != null) ApplyButton.IsEnabled = false;

            SetToolStatus("No element selected");

            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(new TextBlock
            {
                Text = "Select an MEP Fabrication element to inspect its properties.",
                Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}

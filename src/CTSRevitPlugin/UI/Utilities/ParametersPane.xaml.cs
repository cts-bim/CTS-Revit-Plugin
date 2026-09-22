using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using CTSRevitPlugin.UI.ElementTools;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class ParametersPane : Page, IDockablePaneProvider, IUtilitiesPaneHost
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("4A2D1B7C-7C4A-4E72-9B1D-8E6F7A5C1201"));

        public static ParametersPane Instance { get; private set; }
        private readonly List<string> _availableParameterNames = new List<string>();
        private UtilitiesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;
        private Document _lastDocument;
        private List<ElementId> _lastSelectedIds = new List<ElementId>();

        public ParametersPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

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

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(ParametersPane).Assembly.Location), "Resources", "Icons");
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

        private bool _pendingDirty;

        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer =
            new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

        public static void OnSelectionChanged(object sender, Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            if (Instance == null || e == null) return;

            try
            {
                Document doc = e.GetDocument();

                // Reading the parameters of the selection and rebuilding the rows has
                // no value while the pane is closed, and with a large selection
                // GetSharedValue walks every element once per pinned parameter.
                //
                // The test has to be DockablePane.IsShown(): Revit closes a pane by
                // hiding its Win32 host window, which the hosted Page's IsVisible does
                // not notice, so a guard written against IsVisible never fires.
                Instance._lastDocument = doc;
                Instance._lastSelectedIds = e.GetSelectedElements().ToList();
                Instance._pendingDirty = true;

                if (!PaneVisibility.IsShown(doc, PaneId)) return;

                Instance._refreshTimer.Stop();
                Instance._refreshTimer.Start();
            }
            catch { }
        }

        /// <summary>
        /// Brings the pane up to date with the pending selection.
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
            _lastDocument = null;
            _lastSelectedIds = new List<ElementId>();
            _pendingDirty = false;

            try { _refreshTimer.Stop(); } catch { }

            if (Dispatcher.CheckAccess()) Clear();
            else Dispatcher.BeginInvoke(new Action(Clear));
        }

        public void RefreshFromUIApplication(UIApplication uiapp)
        {
            if (uiapp == null || uiapp.ActiveUIDocument == null) { Clear(); return; }
            UpdateSelection(uiapp.ActiveUIDocument.Document, uiapp.ActiveUIDocument.Selection.GetElementIds());
        }

        private void UpdateSelection(Document doc, IEnumerable<ElementId> ids)
        {
            if (!IsUsable(doc) || ids == null) { Clear(); return; }

            List<ElementId> selected = ids.ToList();
            _lastDocument = doc;
            _lastSelectedIds = new List<ElementId>(selected);

            if (selected.Count == 0) { Clear(); return; }

            Element element = doc.GetElement(selected[0]);
            if (element == null) { Clear(); return; }

            UpdateAvailableParameters(element);
            ElementIdValue.Text = element.Id.IdValue().ToString(CultureInfo.InvariantCulture);
            BuildParameters(element);

            bool fabrication = element is FabricationPart;
            MechanicalPropertiesButton.Visibility = fabrication ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            StatusText.Text = selected.Count == 1
                ? "1 element selected"
                : selected.Count + " elements selected · edits apply to all";
        }

        private void UpdateAvailableParameters(Element element)
        {
            _availableParameterNames.Clear();
            try
            {
                foreach (Parameter p in element.Parameters)
                {
                    if (p == null || p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!_availableParameterNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                        _availableParameterNames.Add(name);
                }
                _availableParameterNames.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        private void BuildParameters(Element element)
        {
            ParametersPanel.Children.Clear();
            ElementToolsConfiguration config = UtilitiesConfigurationStore.Current;

            if (config.PinnedParameters == null || config.PinnedParameters.Count == 0)
            {
                ParametersPanel.Children.Add(new TextBlock
                {
                    Text = "No parameters pinned. Use + ADD PARAMETER.",
                    Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                    FontSize = 11, Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            foreach (string parameterName in config.PinnedParameters)
                if (!string.IsNullOrWhiteSpace(parameterName))
                    AddParameterRow(element, parameterName);
        }

        /// <summary>Marker shown when the selected elements disagree on a value.</summary>
        private const string VariesText = "<varies>";

        private void AddParameterRow(Element element, string parameterName)
        {
            WpfGrid row = new WpfGrid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            row.Children.Add(new TextBlock
            {
                Text = parameterName,
                Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Parameter p = element.LookupParameter(parameterName);
            bool editable = p != null && !p.IsReadOnly &&
                            (p.StorageType == StorageType.String ||
                             p.StorageType == StorageType.Double ||
                             p.StorageType == StorageType.Integer ||
                             p.StorageType == StorageType.ElementId);

            // With several elements picked the boxes must not show element #1's
            // value as if it applied to all of them - committing on focus loss
            // would then silently stamp it over the others.
            bool varies;
            string shownValue = GetSharedValue(parameterName, out varies);

            WpfTextBox valueBox = new WpfTextBox
            {
                Text = shownValue,
                Foreground = RevitThemeService.ThemeBrush(this, "TextBrush"),
                Background = editable ? RevitThemeService.ThemeBrush(this, "InputBrush") : Brushes.Transparent,
                BorderBrush = editable ? RevitThemeService.ThemeBrush(this, "InputBorderBrush") : Brushes.Transparent,
                BorderThickness = editable ? new Thickness(1) : new Thickness(0),
                FontSize = 11, Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = !editable,
                ToolTip = editable
                    ? (varies
                        ? "The selected elements have different values. Type to set them all."
                        : (_lastSelectedIds.Count > 1
                            ? "Applies to all " + _lastSelectedIds.Count + " selected elements"
                            : "Edit parameter value"))
                    : "Read-only parameter"
            };

            if (varies) valueBox.FontStyle = FontStyles.Italic;

            valueBox.Tag = new ParameterEditInfo
            {
                ElementIds = new List<ElementId>(_lastSelectedIds),
                ParameterName = parameterName,
                OriginalText = shownValue,
                Varies = varies
            };

            if (editable)
            {
                valueBox.KeyDown += ParameterValue_KeyDown;

                // LostFocus is *logical* focus, which the pane keeps when the click
                // lands on the Revit drawing area: that HWND is outside the WPF
                // island, so the event never fired and the user had to press Enter.
                // LostKeyboardFocus follows the Win32 focus, so clicking anywhere
                // outside commits, like the native Properties palette.
                valueBox.LostKeyboardFocus += ParameterValue_LostKeyboardFocus;
            }

            WpfGrid.SetColumn(valueBox, 1);
            row.Children.Add(valueBox);

            Button copy = new Button { Content = "⧉", Width = 24, Height = 22, Padding = new Thickness(0), FontSize = 12 };
            copy.Click += delegate
            {
                try { Clipboard.SetText(valueBox.Text ?? ""); copy.Content = "✓"; }
                catch { copy.Content = "!"; }
            };
            WpfGrid.SetColumn(copy, 2);
            row.Children.Add(copy);
            ParametersPanel.Children.Add(row);
        }

        private void ParameterValue_KeyDown(object sender, KeyEventArgs e)
        {
            WpfTextBox box = sender as WpfTextBox;
            if (box == null) return;

            if (e.Key == Key.Escape)
            {
                // Put the original value back and drop focus without writing.
                ParameterEditInfo info = box.Tag as ParameterEditInfo;
                if (info != null) box.Text = info.OriginalText ?? "";
                e.Handled = true;
                Keyboard.ClearFocus();
                return;
            }

            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveParameterEdit(box);
        }

        private void ParameterValue_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SaveParameterEdit(sender as WpfTextBox);
        }

        private void SaveParameterEdit(WpfTextBox box)
        {
            if (box == null || _handler == null || _externalEvent == null) return;

            ParameterEditInfo info = box.Tag as ParameterEditInfo;
            if (info == null || info.ElementIds == null || info.ElementIds.Count == 0) return;

            string text = box.Text ?? "";

            // Committing on focus loss means this runs on every click away, so an
            // untouched box must not open a transaction.
            if (string.Equals(text, info.OriginalText ?? "", StringComparison.Ordinal)) return;

            // "<varies>" left as-is is not a value anyone typed.
            if (info.Varies && string.Equals(text, VariesText, StringComparison.Ordinal)) return;

            info.OriginalText = text;
            info.Varies = false;

            _handler.RequestSetParameter(info.ElementIds, info.ParameterName, text);
            try { _externalEvent.Raise(); } catch (Exception ex) { SetToolStatus("Could not save parameter: " + ex.Message); }
        }

        /// <summary>
        /// The value shared by every selected element, or the varies marker when they
        /// disagree. Elements that lack the parameter are ignored.
        /// </summary>
        private string GetSharedValue(string parameterName, out bool varies)
        {
            varies = false;
            if (!IsUsable(_lastDocument) || _lastSelectedIds.Count == 0) return "";

            string first = null;
            bool haveFirst = false;

            foreach (ElementId id in _lastSelectedIds)
            {
                Element element = _lastDocument.GetElement(id);
                if (element == null) continue;
                if (element.LookupParameter(parameterName) == null) continue;

                string value = GetParameterValue(element, parameterName);

                if (!haveFirst) { first = value; haveFirst = true; continue; }

                if (!string.Equals(first, value, StringComparison.Ordinal))
                {
                    varies = true;
                    return VariesText;
                }
            }

            return first ?? "";
        }

        private string GetParameterValue(Element element, string parameterName)
        {
            try
            {
                Parameter p = element.LookupParameter(parameterName);
                if (p == null) return "";
                string value = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                if (p.StorageType == StorageType.Double) return p.AsDouble().ToString(CultureInfo.InvariantCulture);
                if (p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    Element referenced = id == null || id == ElementId.InvalidElementId ? null : element.Document.GetElement(id);
                    return referenced != null ? referenced.Name : (id == null ? "" : id.IdValue().ToString(CultureInfo.InvariantCulture));
                }
            }
            catch { }
            return "";
        }

        private void CopyElementId_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ElementIdValue.Text ?? ""); CopyElementIdButton.Content = "✓"; }
            catch { CopyElementIdButton.Content = "!"; }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) { OpenSettings(1); }
        private void AddParameterButton_Click(object sender, RoutedEventArgs e) { OpenSettings(1); }

        private void MechanicalPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSelectedIds.Count == 0 || _handler == null || _externalEvent == null) return;
            _handler.RequestMechanicalProperties(_lastSelectedIds[0]);
            try { _externalEvent.Raise(); } catch (Exception ex) { SetToolStatus("Could not open Mechanical Properties: " + ex.Message); }
        }

        private void OpenSettings(int tab)
        {
            ElementToolsSettings settings = new ElementToolsSettings(
                UtilitiesConfigurationStore.Current, _availableParameterNames, tab, ApplyConfiguration);
            settings.Owner = Window.GetWindow(this);
            settings.ShowDialog();
        }

        private void ApplyConfiguration(ElementToolsConfiguration configuration)
        {
            UtilitiesConfigurationStore.Apply(configuration);

            // Rebuilding the rows reads the model, so it belongs in an API
            // context - not in the settings dialog's callback, which is plain WPF.
            RequestRefreshInApiContext();
        }

        public void ShowMechanicalProperties(MechanicalPropertiesSnapshot snapshot)
        {
            if (snapshot == null) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                MechanicalPropertiesWindow window = new MechanicalPropertiesWindow(snapshot);
                window.Owner = Window.GetWindow(this);
                window.Show();
                window.Activate();
            }));
        }

        public void SetToolStatus(string message)
        {
            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? "";
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? ""; }));
        }

        public void SetHangerAdjustmentStatus(string message) { SetToolStatus(message); }

        private void Clear()
        {
            ElementIdValue.Text = "-";
            ParametersPanel.Children.Clear();
            MechanicalPropertiesButton.Visibility = System.Windows.Visibility.Collapsed;
            StatusText.Text = "No element selected";
        }

        private class ParameterEditInfo
        {
            public List<ElementId> ElementIds { get; set; }
            public string ParameterName { get; set; }

            /// <summary>Last committed text, so an untouched box writes nothing.</summary>
            public string OriginalText { get; set; }

            public bool Varies { get; set; }
        }

        public void DisposeExternalEvent()
        {
            try { if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; } } catch { }
        }
    }
}
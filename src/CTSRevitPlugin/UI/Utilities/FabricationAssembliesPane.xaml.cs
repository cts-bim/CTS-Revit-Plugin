using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class FabricationAssembliesPane : Page, IDockablePaneProvider, IUtilitiesPaneHost, IFabricationAssembliesHost
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("6F4B4E6D-6A7B-4C4B-A2E0-4B2A6D8F1C51"));

        public static FabricationAssembliesPane Instance { get; private set; }

        private FabricationAssembliesExternalEventHandler _handler;
        private ExternalEvent _externalEvent;
        private List<FabricationAssemblyTemplate> _templates;
        private FabricationAssemblyTemplate _selectedTemplate;
        private bool _loadingControls;
        private FabricationAssembliesEditorWindow _editorWindow;

        public FabricationAssembliesPane()
        {
            Instance = this;
            InitializeComponent();
            Loaded += FabricationAssembliesPane_Loaded;
            RevitThemeService.Register(this);
            LoadBranding();

            _templates = FabricationAssemblyTemplateStore.Load();
            _handler = new FabricationAssembliesExternalEventHandler { Host = this };
            _externalEvent = ExternalEvent.Create(_handler);

            RefreshTemplateList();
            SetToolStatus("Select an assembly and click PLACE ASSEMBLY. The O-Let must already be placed in Revit.");
        }

        private void FabricationAssembliesPane_Loaded(object sender, RoutedEventArgs e)
        {
            SetToolStatus("Ready. Configure the assembly first, then place the O-Let in Revit.");
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(FabricationAssembliesPane).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "FabricationAssemblies.png"));
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
            ReloadTemplatesFromStore();
            SetToolStatus("Templates refreshed. Select an assembly and click PLACE ASSEMBLY.");
        }

        public void ReloadTemplatesFromStore()
        {
            string previousName = _selectedTemplate == null ? null : _selectedTemplate.Name;
            _templates = FabricationAssemblyTemplateStore.Load();
            _selectedTemplate = _templates.Find(x => string.Equals(x.Name, previousName, StringComparison.OrdinalIgnoreCase));
            RefreshTemplateList();
        }

        public void ReceiveServices(List<FabricationServiceInfo> services, string status)
        {
            // Services are intentionally no longer exposed in the placement pane.
            SetToolStatus(status);
        }

        public void ReceiveCatalog(List<FabricationPartButtonInfo> catalog, List<FabricationPaletteInfo> palettes, string status)
        {
            SetToolStatus(status);
        }

        private void RefreshTemplateList()
        {
            _loadingControls = true;
            try
            {
                TemplateList.ItemsSource = null;
                TemplateList.ItemsSource = _templates;
                if (_selectedTemplate != null && _templates.Contains(_selectedTemplate))
                    TemplateList.SelectedItem = _selectedTemplate;
                else if (_templates.Count > 0)
                    TemplateList.SelectedIndex = 0;
            }
            finally { _loadingControls = false; }
            TemplateList_SelectionChanged(null, null);
        }

        private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingControls) return;
            _selectedTemplate = TemplateList.SelectedItem as FabricationAssemblyTemplate;
            UpdateSelectedAssemblyInfo();
        }

        private void UpdateSelectedAssemblyInfo()
        {
            if (_selectedTemplate == null)
            {
                SelectedAssemblyText.Text = "Select an assembly";
                SelectedAssemblyPartsText.Text = "";
                SelectedAssemblyDiameterText.Text = "";
                return;
            }

            int count = _selectedTemplate.Parts == null ? 0 : _selectedTemplate.Parts.Count;
            SelectedAssemblyText.Text = _selectedTemplate.Name;
            SelectedAssemblyPartsText.Text = count + " part(s) · first item is the existing O-Let anchor";
            SelectedAssemblyDiameterText.Text = "Service: O-Let · size: " +
                (_selectedTemplate.UseAnchorDiameter ? "AUTO (O-Let)" : ImperialLength.FormatInches(_selectedTemplate.DefaultDiameterInches));
        }

        private void ConfigureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_editorWindow != null)
                {
                    if (_editorWindow.IsVisible) { _editorWindow.Activate(); return; }
                    _editorWindow = null;
                }

                _editorWindow = new FabricationAssembliesEditorWindow(_handler, _externalEvent, this);
                _editorWindow.Closed += delegate
                {
                    _editorWindow = null;
                    ReloadTemplatesFromStore();
                };
                _editorWindow.Show();
            }
            catch (Exception ex) { SetToolStatus("Could not open configuration: " + ex.Message); }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadTemplatesFromStore();
            SetToolStatus("Templates refreshed.");
        }

        private void CaptureAssembly_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null)
            {
                SetToolStatus("Select an assembly name before CAPTURE.");
                return;
            }
            try
            {
                _handler.Host = this;
                _handler.RequestCaptureAssembly(_selectedTemplate);
                ExternalEventRequest request = _externalEvent.Raise();
                if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
                    SetToolStatus("Revit did not accept CAPTURE. Try again after current command ends.");
                else
                    SetToolStatus("CAPTURE: O-Let first, pick each following part IN ORDER; press ESC to finish. Read-only.");
            }
            catch (Exception ex) { SetToolStatus("Could not start CAPTURE: " + ex.Message); }
        }

        private void PlaceAssembly_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null)
            {
                SetToolStatus("Select an assembly template first.");
                return;
            }

            if (_selectedTemplate.Parts == null || _selectedTemplate.Parts.Count < 2)
            {
                SetToolStatus("Open CONFIGURE and add O-Let Weld Gap first, followed by the remaining assembly parts.");
                return;
            }

            if (!IsOLetDefinition(_selectedTemplate.Parts[0]))
            {
                SetToolStatus("The first item must be O-Let Weld Gap. It will be used as the existing anchor in Revit.");
                return;
            }

            try
            {
                // A late catalog request from a closed modeless window must never
                // steal status callbacks for placement from the dockable pane.
                _handler.Host = this;
                _handler.RequestPlaceAssembly(_selectedTemplate);
                ExternalEventRequest request = _externalEvent.Raise();
                if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
                {
                    SetToolStatus("Revit did not accept the placement request.");
                    return;
                }
                SetToolStatus("Processing selected O-Lets, or select multiple O-Lets and click Finish in Revit...");
            }
            catch (Exception ex) { SetToolStatus("Could not start placement: " + ex.Message); }
        }

        private static bool IsOLetDefinition(FabricationAssemblyPartDefinition definition)
        {
            if (definition == null) return false;
            string text = (definition.Name ?? "") + " " + (definition.Code ?? "");
            return text.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void SetToolStatus(string message)
        {
            Action action = delegate
            {
                // CAPTURE updates the persistent recipe in the Revit API context.
                // Reload the pane only after the full reference was certified.
                if ((message ?? "").StartsWith("CAPTURED ", StringComparison.Ordinal))
                    ReloadTemplatesFromStore();
                StatusText.Text = message ?? "";
                StatusText.ToolTip = message ?? "";
            };
            if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
        }

        public void SetHangerAdjustmentStatus(string message) { SetToolStatus(message); }

        public void DisposeExternalEvent()
        {
            try { if (_externalEvent != null) { _externalEvent.Dispose(); _externalEvent = null; } } catch { }
        }

        public void OpenConfigurationWindow()
        {
            ConfigureButton_Click(this, new RoutedEventArgs());
        }
    }
}

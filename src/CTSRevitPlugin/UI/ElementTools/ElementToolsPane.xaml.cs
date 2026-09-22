using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using WpfGrid = System.Windows.Controls.Grid;
using WpfVisibility = System.Windows.Visibility;
using WpfGridLength = System.Windows.GridLength;
using WpfGridUnitType = System.Windows.GridUnitType;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CTSRevitPlugin.UI.ElementTools
{
    public partial class ElementToolsPane : Page, IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(
                new Guid("D7F2A1E8-6E0D-4E5F-9A31-4C7F2B8D6101")
            );

        public static ElementToolsPane Instance
        {
            get;
            private set;
        }

        private ElementToolsConfiguration _configuration;

        private readonly List<string> _availableParameterNames =
            new List<string>();

        private ElementToolsExternalEventHandler _externalEventHandler;

        private ExternalEvent _externalEvent;

        private Document _lastDocument;

        private List<ElementId> _lastSelectedIds =
            new List<ElementId>();


        // ============================================================
        // CONSTRUCTOR
        // ============================================================

        public ElementToolsPane()
        {
            Instance = this;

            TaskDialog.Show(
                "CTS TEST",
                "ElementToolsPane foi criado."
            );

            _configuration =
                ElementToolsConfiguration.Load();

            // Load configuration from XML.
            // If the XML does not exist, Load() creates it
            // with the default configuration.
            _configuration =
                ElementToolsConfiguration.Load();

            InitializeComponent();

            _externalEventHandler =
                new ElementToolsExternalEventHandler();

            _externalEvent =
                ExternalEvent.Create(
                    _externalEventHandler);

            _externalEventHandler.Pane =
                this;

            ClearElement();

            UpdateUtilityModules();
        }


        // ============================================================
        // DOCKABLE PANE
        // ============================================================

        public void SetupDockablePane(
            DockablePaneProviderData data)
        {
            data.FrameworkElement =
                this;

            data.InitialState =
                new DockablePaneState
                {
                    DockPosition =
                        DockPosition.Right
                };

            data.VisibleByDefault =
                false;
        }


        // ============================================================
        // SELECTION CHANGED
        // ============================================================

        public static void OnSelectionChanged(
            object sender,
            Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            if (Instance == null || e == null)
                return;

            try
            {
                Document doc =
                    e.GetDocument();

                if (doc == null)
                    return;

                ISet<ElementId> selectedIds =
                    e.GetSelectedElements();

                Instance.UpdateSelection(
                    doc,
                    selectedIds);
            }
            catch
            {
            }
        }


        // ============================================================
        // REFRESH
        // ============================================================

        public void RefreshFromUIApplication(
            UIApplication uiapp)
        {
            if (
                uiapp == null ||
                uiapp.ActiveUIDocument == null)
            {
                ClearElement();
                return;
            }

            Document doc =
                uiapp.ActiveUIDocument.Document;

            ICollection<ElementId> selectedIds =
                uiapp
                    .ActiveUIDocument
                    .Selection
                    .GetElementIds();

            UpdateSelection(
                doc,
                selectedIds);
        }


        // ============================================================
        // UPDATE SELECTION
        // ============================================================

        private void UpdateSelection(
            Document doc,
            IEnumerable<ElementId> ids)
        {
            if (doc == null || ids == null)
            {
                ClearElement();
                return;
            }

            List<ElementId> selectedIds =
                ids.ToList();

            _lastDocument =
                doc;

            _lastSelectedIds =
                new List<ElementId>(
                    selectedIds);

            if (selectedIds.Count == 0)
            {
                ClearElement();
                return;
            }

            Element element =
                doc.GetElement(
                    selectedIds[0]);

            if (element == null)
            {
                ClearElement();
                return;
            }

            UpdateAvailableParameters(
                element);


            // --------------------------------------------------------
            // BASIC INFORMATION
            // --------------------------------------------------------

            string elementId =
                element.Id.IdValue().ToString();

            string category =
                element.Category != null
                    ? element.Category.Name
                    : "-";

            string family = "-";

            string type = "-";

            FamilyInstance familyInstance =
                element as FamilyInstance;

            if (
                familyInstance != null &&
                familyInstance.Symbol != null)
            {
                family =
                    familyInstance
                        .Symbol
                        .FamilyName ?? "-";

                type =
                    familyInstance
                        .Symbol
                        .Name ??
                    element.Name ??
                    "-";
            }
            else
            {
                type =
                    element.Name ?? "-";
            }

            string mark =
                GetParameterValue(
                    element,
                    "Mark");

            if (string.IsNullOrWhiteSpace(mark))
                mark = "-";


            IdValue.Text =
                elementId;

            CategoryValue.Text =
                category;

            FamilyValue.Text =
                family;

            TypeValue.Text =
                type;

            MarkValue.Text =
                mark;


            // --------------------------------------------------------
            // PARAMETERS
            // --------------------------------------------------------

            BuildPinnedParameters(
                element);


            // --------------------------------------------------------
            // SHORTCUTS
            // --------------------------------------------------------

            BuildEnabledTools();


            // --------------------------------------------------------
            // HANGERS
            // --------------------------------------------------------

            bool isFabricationHanger =
                IsFabricationHanger(
                    element);

            if (isFabricationHanger)
            {
                HangerAdjustmentBorder.Visibility =
                    WpfVisibility.Visible;

                HangerAdjustmentStatus.Text =
                    "Ready to adjust selected hanger(s).";
            }
            else
            {
                HangerAdjustmentBorder.Visibility =
                    WpfVisibility.Collapsed;
            }


            // --------------------------------------------------------
            // MODULE VISIBILITY
            // --------------------------------------------------------

            UpdateUtilityModules();


            // --------------------------------------------------------
            // STATUS
            // --------------------------------------------------------

            if (selectedIds.Count > 1)
            {
                StatusText.Text =
                    selectedIds.Count +
                    " elements selected · showing first";
            }
            else
            {
                StatusText.Text =
                    "1 element selected";
            }
        }


        // ============================================================
        // UTILITIES BUTTON
        // ============================================================

        private void UtilitiesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (UtilitiesMenu.Visibility ==
                WpfVisibility.Visible)
            {
                UtilitiesMenu.Visibility =
                    WpfVisibility.Collapsed;
            }
            else
            {
                UtilitiesMenu.Visibility =
                    WpfVisibility.Visible;
            }
        }


        // ============================================================
        // UTILITY CHECKBOX CHANGED
        // ============================================================

        private void UtilityCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            UpdateUtilityModules();

            SaveConfiguration();
        }


        // ============================================================
        // UPDATE MODULE VISIBILITY
        // ============================================================

        private void UpdateUtilityModules()
        {
            if (
                ParametersModule == null ||
                ShortcutsModule == null ||
                HangersModule == null)
            {
                return;
            }

            ParametersModule.Visibility =
                ParametersUtilityCheck.IsChecked == true
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;

            ShortcutsModule.Visibility =
                ShortcutsUtilityCheck.IsChecked == true
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;

            HangersModule.Visibility =
                HangersUtilityCheck.IsChecked == true
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;
        }


        // ============================================================
        // SAVE CONFIGURATION
        // ============================================================

        private void SaveConfiguration()
        {
            if (_configuration == null)
                return;

            try
            {
                _configuration.ShowParameters =
                    ParametersUtilityCheck != null &&
                    ParametersUtilityCheck.IsChecked == true;

                _configuration.ShowShortcuts =
                    ShortcutsUtilityCheck != null &&
                    ShortcutsUtilityCheck.IsChecked == true;

                _configuration.ShowHangers =
                    HangersUtilityCheck != null &&
                    HangersUtilityCheck.IsChecked == true;

                _configuration.Save();
            }
            catch
            {
            }
        }


        // ============================================================
        // AVAILABLE PARAMETERS
        // ============================================================

        private void UpdateAvailableParameters(
            Element element)
        {
            _availableParameterNames.Clear();

            try
            {
                foreach (
                    Parameter parameter
                    in element.Parameters)
                {
                    if (
                        parameter == null ||
                        parameter.Definition == null)
                    {
                        continue;
                    }

                    string name =
                        parameter.Definition.Name;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    bool exists =
                        _availableParameterNames.Any(
                            x => string.Equals(
                                x,
                                name,
                                StringComparison.OrdinalIgnoreCase));

                    if (!exists)
                    {
                        _availableParameterNames.Add(
                            name);
                    }
                }

                _availableParameterNames.Sort(
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }


        // ============================================================
        // PARAMETERS
        // ============================================================

        private void BuildPinnedParameters(
            Element element)
        {
            PinnedParametersPanel.Children.Clear();

            if (
                _configuration.PinnedParameters == null ||
                _configuration.PinnedParameters.Count == 0)
            {
                TextBlock emptyText =
                    new TextBlock
                    {
                        Text =
                            "No parameters added.",

                        Foreground =
                            Brushes.Gray,

                        FontSize =
                            11,

                        Margin =
                            new Thickness(
                                0,
                                4,
                                0,
                                4)
                    };

                PinnedParametersPanel.Children.Add(
                    emptyText);

                return;
            }

            foreach (
                string parameterName
                in _configuration.PinnedParameters)
            {
                if (string.IsNullOrWhiteSpace(parameterName))
                    continue;

                AddEditableParameterRow(
                    element,
                    parameterName);
            }
        }


        // ============================================================
        // EDITABLE PARAMETER ROW
        // ============================================================

        private void AddEditableParameterRow(
            Element element,
            string parameterName)
        {
            string value =
                GetParameterValue(
                    element,
                    parameterName);

            WpfGrid row =
                new WpfGrid
                {
                    Margin =
                        new Thickness(
                            0,
                            2,
                            0,
                            2)
                };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new WpfGridLength(
                            115)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new WpfGridLength(
                            1,
                            WpfGridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new WpfGridLength(
                            30)
                });


            // --------------------------------------------------------
            // NAME
            // --------------------------------------------------------

            TextBlock nameText =
                new TextBlock
                {
                    Text =
                        parameterName,

                    Foreground =
                        Brushes.LightGray,

                    FontSize =
                        11,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                };

            WpfGrid.SetColumn(
                nameText,
                0);

            row.Children.Add(
                nameText);


            // --------------------------------------------------------
            // VALUE
            // --------------------------------------------------------

            WpfTextBox valueTextBox =
                new WpfTextBox
                {
                    Text =
                        string.IsNullOrWhiteSpace(value)
                            ? ""
                            : value,

                    Foreground =
                        Brushes.White,

                    Background =
                        Brushes.Transparent,

                    BorderThickness =
                        new Thickness(0),

                    FontSize =
                        11,

                    Padding =
                        new Thickness(
                            3,
                            2,
                            3,
                            2),

                    VerticalContentAlignment =
                        VerticalAlignment.Center,

                    ToolTip =
                        "Edit parameter value"
                };

            valueTextBox.Tag =
                new ParameterEditInfo
                {
                    ElementId =
                        element.Id,

                    ParameterName =
                        parameterName
                };

            valueTextBox.KeyDown +=
                ParameterValueTextBox_KeyDown;

            valueTextBox.LostFocus +=
                ParameterValueTextBox_LostFocus;

            WpfGrid.SetColumn(
                valueTextBox,
                1);

            row.Children.Add(
                valueTextBox);


            // --------------------------------------------------------
            // COPY
            // --------------------------------------------------------

            Button copyButton =
                new Button
                {
                    Content =
                        "⧉",

                    Width =
                        24,

                    Height =
                        22,

                    Padding =
                        new Thickness(0),

                    FontSize =
                        12,

                    Tag =
                        value
                };

            copyButton.Click +=
                delegate
                {
                    CopyToClipboard(
                        valueTextBox.Text,
                        copyButton);
                };

            WpfGrid.SetColumn(
                copyButton,
                2);

            row.Children.Add(
                copyButton);

            PinnedParametersPanel.Children.Add(
                row);
        }


        // ============================================================
        // PARAMETER ENTER
        // ============================================================

        private void ParameterValueTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            WpfTextBox textBox =
                sender as WpfTextBox;

            if (textBox == null)
                return;

            if (e.Key != Key.Enter)
                return;

            e.Handled =
                true;

            SaveParameterEdit(
                textBox);
        }


        // ============================================================
        // PARAMETER LOST FOCUS
        // ============================================================

        private void ParameterValueTextBox_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            WpfTextBox textBox =
                sender as WpfTextBox;

            if (textBox == null)
                return;

            SaveParameterEdit(
                textBox);
        }


        // ============================================================
        // SAVE PARAMETER
        // ============================================================

        private void SaveParameterEdit(
            WpfTextBox textBox)
        {
            if (textBox == null)
                return;

            ParameterEditInfo info =
                textBox.Tag as ParameterEditInfo;

            if (info == null)
                return;

            if (
                _externalEventHandler == null ||
                _externalEvent == null)
            {
                return;
            }

            string newValue =
                textBox.Text ?? "";

            _externalEventHandler.RequestSetParameter(
                info.ElementId,
                info.ParameterName,
                newValue);

            try
            {
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                SetToolStatus(
                    "Could not save parameter: " +
                    ex.Message);
            }
        }


        // ============================================================
        // PARAMETER EDIT INFO
        // ============================================================

        private class ParameterEditInfo
        {
            public ElementId ElementId
            {
                get;
                set;
            }

            public string ParameterName
            {
                get;
                set;
            }
        }


        // ============================================================
        // GET PARAMETER VALUE
        // ============================================================

        private string GetParameterValue(
            Element element,
            string parameterName)
        {
            if (
                element == null ||
                string.IsNullOrWhiteSpace(parameterName))
            {
                return "-";
            }

            try
            {
                Parameter parameter =
                    element.LookupParameter(
                        parameterName);

                if (parameter == null)
                    return "-";

                string value =
                    parameter.AsValueString();

                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value =
                    parameter.AsString();

                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                switch (parameter.StorageType)
                {
                    case StorageType.Integer:

                        return parameter
                            .AsInteger()
                            .ToString(
                                CultureInfo.InvariantCulture);


                    case StorageType.Double:

                        return parameter
                            .AsDouble()
                            .ToString(
                                CultureInfo.InvariantCulture);


                    case StorageType.ElementId:

                        ElementId id =
                            parameter.AsElementId();

                        if (
                            id == null ||
                            id == ElementId.InvalidElementId)
                        {
                            return "-";
                        }

                        Element referenced =
                            element.Document.GetElement(
                                id);

                        if (referenced != null)
                        {
                            return
                                referenced.Name ??
                                id.IdValue().ToString();
                        }

                        return id.IdValue().ToString();


                    default:

                        return "-";
                }
            }
            catch
            {
                return "-";
            }
        }


        // ============================================================
        // SHORTCUTS
        // ============================================================

        private void BuildEnabledTools()
        {
            ToolsPanel.Children.Clear();

            if (
                _configuration.ShortcutTools == null ||
                _configuration.ShortcutTools.Count == 0)
            {
                TextBlock emptyText =
                    new TextBlock
                    {
                        Text =
                            "No shortcuts added.",

                        Foreground =
                            Brushes.Gray,

                        FontSize =
                            11,

                        Margin =
                            new Thickness(
                                0,
                                4,
                                0,
                                4)
                    };

                ToolsPanel.Children.Add(
                    emptyText);

                return;
            }

            foreach (
                string toolId
                in _configuration.ShortcutTools)
            {
                ElementToolDefinition tool =
                    ElementToolsRegistry.GetById(
                        toolId);

                if (tool == null)
                    continue;

                string tooltipText =
                    tool.Name +
                    "\n\n" +
                    "Description\n" +
                    tool.Description +
                    "\n\n" +
                    "How to use\n" +
                    tool.Usage +
                    "\n\n" +
                    "Author: " +
                    tool.Author +
                    "\n" +
                    "Version: " +
                    tool.Version;

                if (!string.IsNullOrWhiteSpace(
                    tool.Notes))
                {
                    tooltipText +=
                        "\n\nNotes\n" +
                        tool.Notes;
                }

                Button toolButton =
                    new Button
                    {
                        Content =
                            tool.Name,

                        Tag =
                            tool.Id,

                        Height =
                            30,

                        Margin =
                            new Thickness(
                                0,
                                2,
                                0,
                                2),

                        HorizontalContentAlignment =
                            HorizontalAlignment.Left,

                        Padding =
                            new Thickness(
                                10,
                                0,
                                10,
                                0),

                        ToolTip =
                            tooltipText
                    };

                ToolTipService.SetInitialShowDelay(
                    toolButton,
                    300);

                ToolTipService.SetBetweenShowDelay(
                    toolButton,
                    100);

                ToolTipService.SetShowDuration(
                    toolButton,
                    30000);

                toolButton.Click +=
                    ToolButton_Click;

                ToolsPanel.Children.Add(
                    toolButton);
            }
        }


        // ============================================================
        // TOOL CLICK
        // ============================================================

        private void ToolButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Button button =
                sender as Button;

            if (button == null)
                return;

            string toolId =
                button.Tag as string;

            if (string.IsNullOrWhiteSpace(toolId))
                return;

            ElementToolDefinition tool =
                ElementToolsRegistry.GetById(
                    toolId);

            if (tool == null)
                return;

            if (
                _externalEventHandler == null ||
                _externalEvent == null)
            {
                SetToolStatus(
                    "External event is not available.");

                return;
            }

            SetToolStatus(
                "Running " +
                tool.Name +
                "...");

            _externalEventHandler
                .RequestTool(
                    toolId);

            try
            {
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                SetToolStatus(
                    "Could not start " +
                    tool.Name +
                    ": " +
                    ex.Message);
            }
        }


        // ============================================================
        // SETTINGS
        // ============================================================

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSettings(0);
        }


        private void AddParameterButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSettings(1);
        }


        private void AddMoreToolsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSettings(2);
        }


        private void OpenSettings(
            int selectedTab)
        {
            ElementToolsSettings settings =
                new ElementToolsSettings(
                    _configuration,
                    _availableParameterNames,
                    selectedTab,
                    ApplyConfiguration);

            settings.Owner =
                Window.GetWindow(this);

            settings.ShowDialog();
        }


        private void ApplyConfiguration(
            ElementToolsConfiguration configuration)
        {
            if (configuration == null)
                return;

            _configuration =
                CloneConfiguration(
                    configuration);

            // Persist the new configuration immediately.
            _configuration.Save();

            if (
                _externalEventHandler != null &&
                _externalEvent != null)
            {
                _externalEventHandler
                    .RequestRefresh();

                try
                {
                    _externalEvent.Raise();
                }
                catch
                {
                }
            }

            // Refresh the WPF UI immediately.
            if (_lastDocument != null)
            {
                UpdateSelection(
                    _lastDocument,
                    _lastSelectedIds);
            }
            else
            {
                UpdateUtilityModules();
            }
        }


        // ============================================================
        // CONFIGURATION CLONE
        // ============================================================

        private static ElementToolsConfiguration CloneConfiguration(
            ElementToolsConfiguration source)
        {
            if (source == null)
            {
                return
                    ElementToolsConfiguration.Load();
            }

            return new ElementToolsConfiguration
            {
                ShowParameters =
                    source.ShowParameters,

                ShowShortcuts =
                    source.ShowShortcuts,

                ShowHangers =
                    source.ShowHangers,

                PinnedParameters =
                    new List<string>(
                        source.PinnedParameters ??
                        new List<string>()),
                // Carried through so a round-trip here does not wipe the stars
                // set in CTS Mechanical Properties.
                FavoriteMechanicalProperties =
                    new List<string>(
                        source.FavoriteMechanicalProperties ??
                        new List<string>()),


                ShortcutTools =
                    new List<string>(
                        source.ShortcutTools ??
                        new List<string>()),

                HangerTools =
                    new List<string>(
                        source.HangerTools ??
                        new List<string>()),

                ShowHangerRodAdjustment =
                    source.ShowHangerRodAdjustment,

                HangerRodAdjustmentDefault =
                    source.HangerRodAdjustmentDefault,

                // Carried through for the same reason as the stars above: a
                // round-trip through this pane must not reset the limit.
                MechanicalPropertiesElementLimit =
                    source.MechanicalPropertiesElementLimit,

                ShowElementId =
                    source.ShowElementId,

                ShowMark =
                    source.ShowMark,

                ShowCategory =
                    source.ShowCategory,

                ShowFamily =
                    source.ShowFamily,

                ShowType =
                    source.ShowType,

                EnabledTools =
                    new List<string>(
                        source.EnabledTools ??
                        new List<string>())
            };
        }


        // ============================================================
        // HANGER
        // ============================================================

        private bool IsFabricationHanger(
            Element element)
        {
            if (element == null)
                return false;

            try
            {
                return
                    element.Category != null &&
                    element.Category.Id.IdValue() ==
                        (long)BuiltInCategory
                            .OST_FabricationHangers;
            }
            catch
            {
                return false;
            }
        }


        // ============================================================
        // DECREASE ROD
        // ============================================================

        private void DecreaseRodButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            double amount;

            if (
                !TryParseImperial(
                    RodAdjustmentTextBox.Text,
                    out amount))
            {
                SetHangerAdjustmentStatus(
                    "Invalid value. Example: 0.50\" or 1' 6\".");

                return;
            }

            if (amount <= 0)
            {
                SetHangerAdjustmentStatus(
                    "Enter a value greater than zero.");

                return;
            }

            if (
                _externalEventHandler == null ||
                _externalEvent == null)
            {
                SetHangerAdjustmentStatus(
                    "External event is not available.");

                return;
            }

            _externalEventHandler
                .RequestDecrease(
                    amount);

            _externalEvent.Raise();

            SetHangerAdjustmentStatus(
                "Decreasing rod length...");
        }


        // ============================================================
        // INCREASE ROD
        // ============================================================

        private void IncreaseRodButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            double amount;

            if (
                !TryParseImperial(
                    RodAdjustmentTextBox.Text,
                    out amount))
            {
                SetHangerAdjustmentStatus(
                    "Invalid value. Example: 0.50\" or 1' 6\".");

                return;
            }

            if (amount <= 0)
            {
                SetHangerAdjustmentStatus(
                    "Enter a value greater than zero.");

                return;
            }

            if (
                _externalEventHandler == null ||
                _externalEvent == null)
            {
                SetHangerAdjustmentStatus(
                    "External event is not available.");

                return;
            }

            _externalEventHandler
                .RequestIncrease(
                    amount);

            _externalEvent.Raise();

            SetHangerAdjustmentStatus(
                "Increasing rod length...");
        }


        // ============================================================
        // IMPERIAL PARSER
        // ============================================================

        private static bool TryParseImperial(
            string input,
            out double feet)
        {
            feet = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string s =
                input.Trim();

            bool negative =
                s.StartsWith("-");

            if (
                negative ||
                s.StartsWith("+"))
            {
                s =
                    s.Substring(1)
                        .Trim();
            }

            try
            {
                double f = 0;

                double inches = 0;

                if (s.Contains("'"))
                {
                    string[] parts =
                        s.Split(
                            new[] { '\'' },
                            2);

                    if (
                        !string.IsNullOrWhiteSpace(
                            parts[0]))
                    {
                        f =
                            double.Parse(
                                parts[0].Trim(),
                                CultureInfo.InvariantCulture);
                    }

                    s =
                        parts.Length > 1
                            ? parts[1]
                            : "";
                }

                s =
                    s.Replace(
                        "\"",
                        "")
                    .Trim();

                if (s.Length > 0)
                {
                    string[] parts =
                        s.Split(
                            new[]
                            {
                                ' ',
                                '\t'
                            },
                            StringSplitOptions
                                .RemoveEmptyEntries);

                    foreach (
                        string part
                        in parts)
                    {
                        if (string.IsNullOrWhiteSpace(part))
                            continue;

                        if (part.Contains("/"))
                        {
                            string[] fraction =
                                part.Split('/');

                            if (fraction.Length != 2)
                                return false;

                            double numerator =
                                double.Parse(
                                    fraction[0],
                                    CultureInfo
                                        .InvariantCulture);

                            double denominator =
                                double.Parse(
                                    fraction[1],
                                    CultureInfo
                                        .InvariantCulture);

                            if (
                                Math.Abs(
                                    denominator) <
                                0.0000001)
                            {
                                return false;
                            }

                            inches +=
                                numerator /
                                denominator;
                        }
                        else
                        {
                            inches +=
                                double.Parse(
                                    part,
                                    CultureInfo
                                        .InvariantCulture);
                        }
                    }
                }

                feet =
                    f +
                    inches / 12.0;

                if (negative)
                    feet = -feet;

                return true;
            }
            catch
            {
                return false;
            }
        }


        // ============================================================
        // TOOL STATUS
        // ============================================================

        public void SetToolStatus(
            string message)
        {
            if (Dispatcher.CheckAccess())
            {
                if (StatusText != null)
                {
                    StatusText.Text =
                        message;
                }

                return;
            }

            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (StatusText != null)
                        {
                            StatusText.Text =
                                message;
                        }
                    }));
        }


        // ============================================================
        // HANGER STATUS
        // ============================================================

        public void SetHangerAdjustmentStatus(
            string message)
        {
            if (Dispatcher.CheckAccess())
            {
                if (HangerAdjustmentStatus != null)
                {
                    HangerAdjustmentStatus.Text =
                        message;
                }

                return;
            }

            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (
                            HangerAdjustmentStatus != null)
                        {
                            HangerAdjustmentStatus.Text =
                                message;
                        }
                    }));
        }


        // ============================================================
        // CLEAR
        // ============================================================

        private void ClearElement()
        {
            if (IdValue != null)
                IdValue.Text = "-";

            if (CategoryValue != null)
                CategoryValue.Text = "-";

            if (FamilyValue != null)
                FamilyValue.Text = "-";

            if (TypeValue != null)
                TypeValue.Text = "-";

            if (MarkValue != null)
                MarkValue.Text = "-";


            if (PinnedParametersPanel != null)
                PinnedParametersPanel.Children.Clear();

            if (ToolsPanel != null)
                ToolsPanel.Children.Clear();


            if (StatusText != null)
                StatusText.Text =
                    "No element selected";


            if (HangerAdjustmentStatus != null)
            {
                HangerAdjustmentStatus.Text =
                    "Select a fabrication hanger";
            }


            if (HangerAdjustmentBorder != null)
            {
                HangerAdjustmentBorder.Visibility =
                    WpfVisibility.Collapsed;
            }
        }


        // ============================================================
        // COPY BUTTONS
        // ============================================================

        private void CopyId_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyToClipboard(
                IdValue.Text,
                CopyIdButton);
        }


        private void CopyCategory_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyToClipboard(
                CategoryValue.Text,
                CopyCategoryButton);
        }


        private void CopyFamily_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyToClipboard(
                FamilyValue.Text,
                CopyFamilyButton);
        }


        private void CopyType_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyToClipboard(
                TypeValue.Text,
                CopyTypeButton);
        }


        private void CopyMark_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyToClipboard(
                MarkValue.Text,
                CopyMarkButton);
        }


        private async void CopyToClipboard(
            string text,
            Button button)
        {
            if (
                string.IsNullOrWhiteSpace(text) ||
                text == "-")
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);

                button.Content =
                    "✓";

                await
                    System.Threading.Tasks.Task
                        .Delay(1000);

                button.Content =
                    "⧉";
            }
            catch
            {
                button.Content =
                    "!";
            }
        }


        // ============================================================
        // CLEANUP
        // ============================================================

        public void DisposeExternalEvent()
        {
            try
            {
                if (_externalEvent != null)
                {
                    _externalEvent.Dispose();

                    _externalEvent = null;
                }
            }
            catch
            {
            }
        }
    }
}
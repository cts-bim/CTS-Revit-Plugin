using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using CTSRevitPlugin.UI.Utilities;

namespace CTSRevitPlugin.UI.ElementTools
{
    public partial class ElementToolsSettings : Window
    {
        private readonly ElementToolsConfiguration _workingConfiguration;
        private readonly List<string> _availableParameters;
        private readonly Action<ElementToolsConfiguration> _onSave;
        private readonly List<CTSRevitPlugin.UI.Utilities.ShortcutDefinition> _shortcutCatalog;

        private readonly Dictionary<string, CheckBox> _parameterCheckBoxes =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, CheckBox> _shortcutCheckBoxes =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, CheckBox> _hangerCheckBoxes =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        public ElementToolsSettings(
            ElementToolsConfiguration configuration,
            IEnumerable<string> availableParameters,
            int selectedTab,
            Action<ElementToolsConfiguration> onSave,
            IEnumerable<CTSRevitPlugin.UI.Utilities.ShortcutDefinition> shortcutCatalog = null)
        {
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();
            ApplyWindowIcon();

            _workingConfiguration =
                UtilitiesConfigurationStore.Clone(configuration);

            _availableParameters =
                availableParameters?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList()
                ?? new List<string>();

            _onSave = onSave;
            _shortcutCatalog = shortcutCatalog?.ToList() ?? new List<CTSRevitPlugin.UI.Utilities.ShortcutDefinition>();

            SettingsTabs.SelectedIndex =
                Math.Max(
                    0,
                    Math.Min(
                        selectedTab,
                        SettingsTabs.Items.Count - 1));

            LoadConfiguration();
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(ElementToolsSettings).Assembly.Location), "Resources", "Icons");
            string path = Path.Combine(root, "CTS Icon.png");
            if (!File.Exists(path)) return;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                FooterCtsLogo.Source = bitmap;
            }
            catch { }
        }

        private void ApplyWindowIcon()
        {
            try
            {
                string root = Path.Combine(Path.GetDirectoryName(typeof(ElementToolsSettings).Assembly.Location), "Resources", "Icons");
                string path = Path.Combine(root, "CTS Icon.png");
                if (!File.Exists(path)) return;
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                Icon = bitmap;
            }
            catch { }
        }

        private void LoadConfiguration()
        {
            ShowHangerRodAdjustmentCheck.IsChecked =
                _workingConfiguration.ShowHangerRodAdjustment;

            BuildParameterList();
            BuildShortcutList();
            BuildHangerList();
        }

        private void BuildParameterList()
        {
            ParameterListPanel.Children.Clear();
            _parameterCheckBoxes.Clear();

            if (_availableParameters.Count == 0)
            {
                ParameterListPanel.Children.Add(
                    new TextBlock
                    {
                        Text = "No parameters available. Select an element in Revit first.",
                        Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                        Margin = new Thickness(0, 10, 0, 10),
                        TextWrapping = TextWrapping.Wrap
                    });
                return;
            }

            foreach (string name in _availableParameters)
            {
                bool isChecked =
                    _workingConfiguration.PinnedParameters.Any(
                        x => string.Equals(
                            x,
                            name,
                            StringComparison.OrdinalIgnoreCase));

                CheckBox checkBox = new CheckBox
                {
                    Content = name,
                    IsChecked = isChecked,
                    Tag = name
                };

                _parameterCheckBoxes[name] = checkBox;
                ParameterListPanel.Children.Add(checkBox);
            }

            ApplyParameterFilter();
        }

        private void BuildShortcutList()
        {
            ShortcutListPanel.Children.Clear();
            _shortcutCheckBoxes.Clear();

            IEnumerable<CTSRevitPlugin.UI.Utilities.ShortcutDefinition> catalog =
                _shortcutCatalog.Count > 0
                    ? _shortcutCatalog
                    : ElementToolsRegistry.All
                        .Where(tool => !string.Equals(tool.Category, "Hanger", StringComparison.OrdinalIgnoreCase))
                        .Select(tool => new CTSRevitPlugin.UI.Utilities.ShortcutDefinition
                        {
                            Key = tool.Id,
                            Name = tool.Name,
                            Group = tool.Category,
                            CtsToolId = tool.Id,
                            Description = tool.Description
                        });

            foreach (CTSRevitPlugin.UI.Utilities.ShortcutDefinition shortcut in catalog)
            {
                bool isChecked = (_workingConfiguration.ShortcutTools ?? new List<string>())
                    .Contains(shortcut.Key, StringComparer.OrdinalIgnoreCase) ||
                    (shortcut.IsCts && (_workingConfiguration.ShortcutTools ?? new List<string>())
                        .Contains(shortcut.CtsToolId, StringComparer.OrdinalIgnoreCase));

                CheckBox checkBox = new CheckBox
                {
                    Content = string.IsNullOrWhiteSpace(shortcut.Group) ? shortcut.Name : shortcut.Group + "  •  " + shortcut.Name,
                    IsChecked = isChecked,
                    Tag = shortcut.Key,
                    ToolTip = shortcut.Description
                };

                ShortcutListPanel.Children.Add(checkBox);
                _shortcutCheckBoxes[shortcut.Key] = checkBox;
            }
        }

        private void BuildHangerList()
        {
            HangerListPanel.Children.Clear();
            _hangerCheckBoxes.Clear();

            foreach (ElementToolDefinition tool in ElementToolsRegistry.All)
            {
                if (!string.Equals(
                    tool.Category,
                    "Hanger",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isChecked =
                    (_workingConfiguration.HangerTools ?? new List<string>())
                        .Contains(tool.Id);

                CheckBox checkBox = new CheckBox
                {
                    Content = tool.Name,
                    IsChecked = isChecked,
                    Tag = tool.Id
                };

                HangerListPanel.Children.Add(checkBox);
                _hangerCheckBoxes[tool.Id] = checkBox;
            }
        }

        private void ShortcutSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string search = ShortcutSearchBox == null ? "" : ShortcutSearchBox.Text.Trim();
            foreach (KeyValuePair<string, CheckBox> item in _shortcutCheckBoxes)
            {
                string text = item.Value.Content == null ? "" : item.Value.Content.ToString();
                item.Value.Visibility = string.IsNullOrWhiteSpace(search) ||
                    text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ParameterSearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplyParameterFilter();
        }

        private void ApplyParameterFilter()
        {
            string search =
                ParameterSearchBox == null
                    ? ""
                    : ParameterSearchBox.Text.Trim();

            foreach (KeyValuePair<string, CheckBox> item
                     in _parameterCheckBoxes)
            {
                item.Value.Visibility =
                    string.IsNullOrWhiteSpace(search) ||
                    item.Key.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }

        private void ClearParameterSearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ParameterSearchBox.Text = "";
            ParameterSearchBox.Focus();
        }

        private void Save_Click(
            object sender,
            RoutedEventArgs e)
        {
            ReadConfigurationFromUI();

            if (_onSave != null)
            {
                _onSave(
                    UtilitiesConfigurationStore.Clone(
                        _workingConfiguration));
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ReadConfigurationFromUI()
        {
            _workingConfiguration.ShowHangerRodAdjustment =
                ShowHangerRodAdjustmentCheck.IsChecked == true;

            _workingConfiguration.PinnedParameters =
                _parameterCheckBoxes.Values
                    .Where(x => x.IsChecked == true)
                    .Select(x => x.Tag as string)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _workingConfiguration.ShortcutTools =
                _shortcutCheckBoxes.Values
                    .Where(x => x.IsChecked == true)
                    .Select(x => x.Tag as string)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _workingConfiguration.HangerTools =
                _hangerCheckBoxes.Values
                    .Where(x => x.IsChecked == true)
                    .Select(x => x.Tag as string)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }
}

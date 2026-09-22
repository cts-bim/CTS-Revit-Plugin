using Autodesk.Revit.DB;
using CTSRevitPlugin.UI.Utilities;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace CTSRevitPlugin.UI.ElementFilter
{
    public partial class FilterDesignerWindow : Window
    {
        private sealed class CategoryChoice
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public CheckBox Box { get; set; }
        }

        private readonly FilterDefinition _filter;
        private readonly Document _doc;
        private readonly List<CategoryChoice> _categories = new List<CategoryChoice>();
        private readonly List<string> _parameterNames = new List<string>();

        public FilterDesignerWindow(FilterDefinition filter, bool isNew, Document doc)
        {
            _filter = filter ?? new FilterDefinition();
            _doc = doc;

            InitializeComponent();
            RevitThemeService.Register(this);

            Title = isNew ? "CTS Filter Designer - New filter" : "CTS Filter Designer - " + _filter.Name;

            LoadChoices();
            LoadCategories();
            LoadParameterNames();
            LoadFilter();
        }

        // ============================================================
        // LOADING
        // ============================================================

        private void LoadChoices()
        {
            MatchBox.Items.Add("Match ALL (AND)");
            MatchBox.Items.Add("Match ANY (OR)");

            ScopeBox.Items.Add("Active view");
            ScopeBox.Items.Add("Entire model");

            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Ask));
            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Select));
            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Isolate));
            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Hide));
            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Halftone));
            ActionBox.Items.Add(FilterDefinition.ActionText(FilterAction.Transparency));
        }

        private void LoadCategories()
        {
            _categories.Clear();

            if (_doc == null)
            {
                // No document: keep whatever the filter already stored so saving
                // does not quietly wipe the category list.
                for (int i = 0; i < _filter.CategoryIds.Count; i++)
                {
                    string name = i < _filter.CategoryNames.Count
                        ? _filter.CategoryNames[i]
                        : _filter.CategoryIds[i].ToString(CultureInfo.InvariantCulture);

                    _categories.Add(new CategoryChoice { Id = _filter.CategoryIds[i], Name = name });
                }

                FooterHint.Text = "No open document, so only the categories already saved are listed.";
            }
            else
            {
                try
                {
                    foreach (Category category in _doc.Settings.Categories)
                    {
                        if (category == null) continue;
                        if (category.CategoryType != CategoryType.Model) continue;
                        if (!category.AllowsBoundParameters) continue;

                        _categories.Add(new CategoryChoice
                        {
                            Id = category.Id.IdValue(),
                            Name = category.Name
                        });
                    }
                }
                catch { }
            }

            _categories.Sort(delegate(CategoryChoice a, CategoryChoice b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            RebuildCategoryList();
        }

        private void RebuildCategoryList()
        {
            CategoriesPanel.Children.Clear();

            string search = (CategorySearchBox.Text ?? "").Trim();
            HashSet<long> ticked = new HashSet<long>(_filter.CategoryIds ?? new List<long>());

            foreach (CategoryChoice choice in _categories)
            {
                if (!string.IsNullOrEmpty(search) &&
                    choice.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Keep the control alive so a ticked-but-filtered-out category is
                    // not lost when the list is rebuilt.
                    continue;
                }

                if (choice.Box == null)
                {
                    choice.Box = new CheckBox
                    {
                        Content = choice.Name,
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 2),
                        IsChecked = ticked.Contains(choice.Id)
                    };

                    choice.Box.Checked += delegate { OnCategoriesChanged(); };
                    choice.Box.Unchecked += delegate { OnCategoriesChanged(); };
                }

                CategoriesPanel.Children.Add(choice.Box);
            }
        }

        private void OnCategoriesChanged()
        {
            LoadParameterNames();
            UpdateCategoryHint();
        }

        private void UpdateCategoryHint()
        {
            int count = _categories.Count(c => c.Box != null && c.Box.IsChecked == true);

            CategoryHint.Text = count == 0
                ? "Nothing ticked means every model category (slower on big models)."
                : count + " category(ies) selected.";
        }

        private void LoadParameterNames()
        {
            _parameterNames.Clear();
            if (_doc == null) return;

            List<long> selected = _categories
                .Where(c => c.Box != null && c.Box.IsChecked == true)
                .Select(c => c.Id)
                .ToList();

            _parameterNames.AddRange(
                ElementFilterEngine.CollectParameterNames(
                    _doc, _doc.ActiveView, selected));
        }

        private void LoadFilter()
        {
            NameBox.Text = _filter.Name;
            MatchBox.SelectedIndex = _filter.Match == RuleMatch.All ? 0 : 1;
            ScopeBox.SelectedIndex = _filter.Scope == FilterScope.ActiveView ? 0 : 1;
            ActionBox.SelectedIndex = (int)_filter.DefaultAction;
            TransparencyBox.Text = _filter.Transparency.ToString(CultureInfo.InvariantCulture);

            foreach (CategoryChoice choice in _categories)
            {
                if (choice.Box != null)
                    choice.Box.IsChecked = _filter.CategoryIds.Contains(choice.Id);
            }

            UpdateCategoryHint();

            ConditionsPanel.Children.Clear();
            foreach (FilterCondition condition in _filter.Conditions)
                ConditionsPanel.Children.Add(BuildConditionRow(condition));

            if (_filter.Conditions.Count == 0) AddCondition_Click(null, null);
        }

        // ============================================================
        // CONDITION ROWS
        // ============================================================

        private UIElement BuildConditionRow(FilterCondition condition)
        {
            Border border = new Border
            {
                Background = (Brush)FindResource("SurfaceAltBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = condition
            };

            WpfGrid grid = new WpfGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel fields = new StackPanel();

            // --- parameter name (editable combo: the list is a sample, not a limit)
            WpfComboBox parameterBox = new WpfComboBox
            {
                Height = 30,
                IsEditable = true,
                Text = condition.ParameterName ?? "",
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Revit parameter name. The list is a sample of what the chosen " +
                          "categories expose; you can type any name."
            };

            foreach (string name in _parameterNames) parameterBox.Items.Add(name);
            parameterBox.LostFocus += delegate { condition.ParameterName = parameterBox.Text ?? ""; };

            fields.Children.Add(parameterBox);

            // --- operator + value + input type
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal };

            WpfComboBox operatorBox = new WpfComboBox { Width = 150, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
            foreach (RuleOperator op in Enum.GetValues(typeof(RuleOperator)))
                operatorBox.Items.Add(FilterCondition.OperatorText(op));
            operatorBox.SelectedIndex = (int)condition.Operator;

            WpfTextBox valueBox = new WpfTextBox
            {
                Width = 190,
                Height = 30,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = condition.Value ?? "",
                Margin = new Thickness(0, 0, 6, 0)
            };
            valueBox.LostFocus += delegate { condition.Value = valueBox.Text ?? ""; };

            CheckBox promptBox = new CheckBox
            {
                Content = "Ask when run",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = condition.InputType == RuleInputType.Prompt,
                ToolTip = "The palette asks for this value every time the filter runs."
            };

            Action syncValueState = delegate
            {
                bool needsValue =
                    condition.Operator != RuleOperator.IsEmpty &&
                    condition.Operator != RuleOperator.IsNotEmpty;

                valueBox.IsEnabled = needsValue;
                promptBox.IsEnabled = needsValue;

                valueBox.ToolTip = condition.InputType == RuleInputType.Prompt
                    ? "Default offered when the palette asks."
                    : "Value to compare against.";
            };

            operatorBox.SelectionChanged += delegate
            {
                if (operatorBox.SelectedIndex < 0) return;
                condition.Operator = (RuleOperator)operatorBox.SelectedIndex;
                syncValueState();
            };

            promptBox.Checked += delegate { condition.InputType = RuleInputType.Prompt; syncValueState(); };
            promptBox.Unchecked += delegate { condition.InputType = RuleInputType.Fixed; syncValueState(); };

            syncValueState();

            line.Children.Add(operatorBox);
            line.Children.Add(valueBox);
            line.Children.Add(promptBox);
            fields.Children.Add(line);

            WpfGrid.SetColumn(fields, 0);
            grid.Children.Add(fields);

            Button remove = new Button
            {
                Content = "\u2715",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)FindResource("SurfaceBrush"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = "Remove this condition"
            };

            remove.Click += delegate
            {
                _filter.Conditions.Remove(condition);
                ConditionsPanel.Children.Remove(border);
            };

            WpfGrid.SetColumn(remove, 1);
            grid.Children.Add(remove);

            border.Child = grid;
            return border;
        }

        private void AddCondition_Click(object sender, RoutedEventArgs e)
        {
            FilterCondition condition = new FilterCondition();
            _filter.Conditions.Add(condition);
            ConditionsPanel.Children.Add(BuildConditionRow(condition));
        }

        private void CategorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildCategoryList();
        }

        // ============================================================
        // SAVING
        // ============================================================

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Commit whatever still has focus: the field handlers run on LostFocus,
            // and clicking SAVE directly from a text box would otherwise drop the
            // last thing typed.
            CommitFocusedEditor();

            string name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                RevitUtils.Alert("Give the filter a name.", "CTS Filter Designer");
                NameBox.Focus();
                return;
            }

            _filter.Conditions.RemoveAll(
                c => c == null || string.IsNullOrWhiteSpace(c.ParameterName));

            if (_filter.Conditions.Count == 0)
            {
                RevitUtils.Alert(
                    "Add at least one condition with a parameter name.",
                    "CTS Filter Designer");
                return;
            }

            _filter.Name = name;
            _filter.Match = MatchBox.SelectedIndex == 1 ? RuleMatch.Any : RuleMatch.All;
            _filter.Scope = ScopeBox.SelectedIndex == 1 ? FilterScope.EntireModel : FilterScope.ActiveView;
            _filter.DefaultAction = ActionBox.SelectedIndex < 0
                ? FilterAction.Ask
                : (FilterAction)ActionBox.SelectedIndex;

            int transparency;
            if (!int.TryParse((TransparencyBox.Text ?? "").Trim(), out transparency)) transparency = 70;
            _filter.Transparency = Math.Max(0, Math.Min(100, transparency));

            _filter.CategoryIds = _categories
                .Where(c => c.Box != null && c.Box.IsChecked == true)
                .Select(c => c.Id)
                .ToList();

            _filter.CategoryNames = _categories
                .Where(c => c.Box != null && c.Box.IsChecked == true)
                .Select(c => c.Name)
                .ToList();

            DialogResult = true;
            Close();
        }

        private void CommitFocusedEditor()
        {
            try
            {
                System.Windows.Input.FocusManager.SetFocusedElement(this, this);
                System.Windows.Input.Keyboard.ClearFocus();
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

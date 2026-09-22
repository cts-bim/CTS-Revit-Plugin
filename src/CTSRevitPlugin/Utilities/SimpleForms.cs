using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CTSRevitPlugin.UI.Utilities;

namespace CTSRevitPlugin.Utilities
{
    public static class SimpleForms
    {
        public static string AskString(string title, string prompt, string defaultValue = "")
        {
            var window = CreateBaseWindow(title, 540, 230);
            var root = (Grid)window.Content;
            var body = (StackPanel)root.Children[1];

            body.Children.Add(new TextBlock
            {
                Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = (Brush)window.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 10)
            });

            TextBox box = new TextBox
            {
                Text = defaultValue ?? "", Height = 34, Padding = new Thickness(9, 0, 9, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            box.SetResourceReference(Control.BackgroundProperty, "InputBrush");
            box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "InputBorderBrush");
            body.Children.Add(box);

            bool ok = false;
            AddButtons(root, window, delegate { ok = true; window.Close(); }, delegate { window.Close(); });
            window.ShowDialog();
            return ok ? box.Text : null;
        }

        public static T Choose<T>(string title, string prompt, IList<T> items, Func<T, string> display)
        {
            if (items == null || items.Count == 0) return default(T);

            var window = CreateBaseWindow(title, 560, 250);
            var root = (Grid)window.Content;
            var body = (StackPanel)root.Children[1];
            body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)window.FindResource("TextBrush"), Margin = new Thickness(0,0,0,10) });

            ComboBox combo = new ComboBox { Height = 34, Padding = new Thickness(7, 0, 7, 0), VerticalContentAlignment = VerticalAlignment.Center };
            foreach (T item in items) combo.Items.Add(new DisplayItem<T>(item, display(item)));
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            body.Children.Add(combo);

            bool ok = false;
            AddButtons(root, window, delegate { ok = true; window.Close(); }, delegate { window.Close(); });
            window.ShowDialog();
            return ok && combo.SelectedItem != null ? ((DisplayItem<T>)combo.SelectedItem).Value : default(T);
        }

        public static List<T> ChooseMany<T>(string title, string prompt, IList<T> items, Func<T, string> display)
        {
            var result = new List<T>();
            if (items == null || items.Count == 0) return result;

            var window = CreateBaseWindow(title, 600, 620);
            var root = (Grid)window.Content;
            var body = (StackPanel)root.Children[1];
            body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)window.FindResource("TextBrush"), Margin = new Thickness(0,0,0,10) });

            var scroll = new ScrollViewer { Height = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list = new StackPanel();
            foreach (T item in items)
            {
                CheckBox check = new CheckBox
                {
                    Content = display(item), Tag = item, FontSize = 11, Margin = new Thickness(3, 5, 3, 5)
                };
                check.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                list.Children.Add(check);
            }
            scroll.Content = list;
            body.Children.Add(scroll);

            bool ok = false;
            AddButtons(root, window, delegate
            {
                ok = true;
                foreach (CheckBox check in list.Children.OfType<CheckBox>())
                    if (check.IsChecked == true) result.Add((T)check.Tag);
                window.Close();
            }, delegate { window.Close(); });

            window.ShowDialog();
            return ok ? result : new List<T>();
        }

        // =================================================================
        // MULTI-FIELD FORM
        //
        // Commands used to build these with System.Windows.Forms, which is why
        // Smart Concat showed raw Windows chrome instead of the CTS look. This
        // is the themed replacement: any number of text boxes / combo boxes in
        // one dialog.
        // =================================================================

        public sealed class FormField
        {
            public string Label { get; set; }

            /// <summary>Null means a free text box; otherwise a read-only combo box.</summary>
            public IList<string> Options { get; set; }

            /// <summary>Default value on the way in, chosen/typed value on the way out.</summary>
            public string Value { get; set; }

            public static FormField Text(string label, string value = "")
            {
                return new FormField { Label = label, Value = value ?? "" };
            }

            public static FormField Choice(string label, IList<string> options, string value = null)
            {
                return new FormField { Label = label, Options = options, Value = value };
            }
        }

        /// <summary>
        /// Shows the supplied fields in a single CTS-themed dialog. Returns false when
        /// the user cancels; on true each field's Value carries the answer.
        /// </summary>
        public static bool AskFields(string title, string prompt, IList<FormField> fields, string okText = "OK")
        {
            if (fields == null || fields.Count == 0) return false;

            Window window = CreateBaseWindow(title, 560, 0);
            window.SizeToContent = SizeToContent.Height;
            window.ResizeMode = ResizeMode.NoResize;

            Grid root = (Grid)window.Content;
            StackPanel body = (StackPanel)root.Children[1];

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                body.Children.Add(new TextBlock
                {
                    Text = prompt,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                body.Children[body.Children.Count - 1]
                    .SetValue(TextBlock.ForegroundProperty, window.FindResource("TextSecondaryBrush"));
            }

            List<Control> editors = new List<Control>();

            foreach (FormField field in fields)
            {
                body.Children.Add(new TextBlock
                {
                    Text = field.Label ?? "",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                if (field.Options == null)
                {
                    TextBox box = new TextBox
                    {
                        Text = field.Value ?? "",
                        Height = 32,
                        Margin = new Thickness(0, 0, 0, 12),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    body.Children.Add(box);
                    editors.Add(box);
                }
                else
                {
                    ComboBox combo = new ComboBox
                    {
                        Height = 32,
                        Margin = new Thickness(0, 0, 0, 12),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };

                    foreach (string option in field.Options)
                        combo.Items.Add(option);

                    int index = field.Value == null ? -1 : field.Options.IndexOf(field.Value);
                    combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);

                    body.Children.Add(combo);
                    editors.Add(combo);
                }
            }

            bool ok = false;
            AddButtons(root, window, delegate
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    TextBox box = editors[i] as TextBox;
                    if (box != null) { fields[i].Value = box.Text ?? ""; continue; }

                    ComboBox combo = editors[i] as ComboBox;
                    if (combo != null)
                        fields[i].Value = combo.SelectedItem == null ? "" : combo.SelectedItem.ToString();
                }

                ok = true;
                window.Close();
            }, delegate { window.Close(); }, okText);

            window.ShowDialog();
            return ok;
        }

        private static Window CreateBaseWindow(string title, double width, double height)
        {
            Window window = new Window
            {
                Title = title, Width = width, Height = height,
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = Brushes.White,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            RevitThemeService.Register(window);
            ApplyWindowIcon(window);

            Grid root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid { Margin = new Thickness(0,0,0,14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Image logo = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform, Margin = new Thickness(0,0,12,0) };
            SetLogo(logo);
            header.Children.Add(logo);

            TextBlock titleBlock = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(titleBlock,1); header.Children.Add(titleBlock);
            Grid.SetRow(header,0); root.Children.Add(header);

            StackPanel body = new StackPanel();
            Grid.SetRow(body,1); root.Children.Add(body);
            window.Content = root;
            // Establish the theme before callers create controls and request
            // TextBrush/InputBrush through FindResource().
            RevitThemeService.Register(window);
            ApplyWindowIcon(window);
            return window;
        }

        private static void ApplyWindowIcon(Window window)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(SimpleForms).Assembly.Location), "Resources", "Icons", "CTS Icon.png");
                if (!File.Exists(path)) return;
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                window.Icon = bitmap;
            }
            catch { }
        }

        private static void AddButtons(Grid root, Window window, Action ok, Action cancel, string okText = "OK")
        {
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
            Button cancelButton = new Button { Content = "CANCEL", Width = 88, Height = 34 };
            Button okButton = new Button { Content = okText ?? "OK", MinWidth = 88, Padding = new Thickness(14, 0, 14, 0), Height = 34, BorderThickness = new Thickness(0), Margin = new Thickness(8, 0, 0, 0) };
            okButton.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush");
            okButton.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");
            cancelButton.Click += delegate { cancel(); };
            okButton.Click += delegate { ok(); };
            buttons.Children.Add(cancelButton); buttons.Children.Add(okButton);
            Grid.SetRow(buttons,2); root.Children.Add(buttons);
        }

        private static void SetLogo(Image image)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(SimpleForms).Assembly.Location), "Resources", "Icons", "CTS Icon.png");
                if (!File.Exists(path)) return;
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            }
            catch { }
        }

        private sealed class DisplayItem<T>
        {
            public T Value { get; }
            private readonly string _text;
            public DisplayItem(T value, string text) { Value = value; _text = text; }
            public override string ToString() { return _text; }
        }
    }
}
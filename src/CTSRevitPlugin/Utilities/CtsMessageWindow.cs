using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.Utilities
{
    public sealed class CtsMessageWindow : Window
    {
        public CtsMessageWindow(string title, string message, string primaryText = "OK")
        {
            Title = title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            MinHeight = 210;
            MaxHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            // Load the CTS theme BEFORE any control calls FindResource().
            // Revit commands are executed outside the WPF pane tree, so these
            // windows must establish their own ResourceDictionary first.
            CTSRevitPlugin.UI.Utilities.RevitThemeService.Register(this);

            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var logo = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) };
            SetLogo(logo);
            header.Children.Add(logo);

            var titleBlock = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
            Grid.SetColumn(titleBlock, 1);
            header.Children.Add(titleBlock);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var text = new TextBlock { Text = message ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(4, 0, 4, 14) };
            Grid.SetRow(text, 1);
            grid.Children.Add(text);

            var button = new Button { Content = primaryText, Width = 88, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(255,77,35)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            button.Click += delegate { DialogResult = true; Close(); };
            Grid.SetRow(button, 2);
            grid.Children.Add(button);

            Content = grid;
            ApplyWindowIcon();
        }

        private static void SetLogo(Image image)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(CtsMessageWindow).Assembly.Location), "Resources", "Icons", "CTS Icon.png");
                if (!File.Exists(path)) return;
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            }
            catch { }
        }

        private void ApplyWindowIcon()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(CtsMessageWindow).Assembly.Location), "Resources", "Icons", "CTS Icon.png");
                if (!File.Exists(path)) return;
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                Icon = bitmap;
            }
            catch { }
        }

        public static void Show(string title, string message)
        {
            new CtsMessageWindow(title, message).ShowDialog();
        }
    }
}
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.Utilities
{
    /// <summary>
    /// Confirmation pop-up with the same look as CtsMessageWindow (CTS logo, theme,
    /// orange primary button) but with a second CANCEL button.
    /// </summary>
    public sealed class CtsConfirmWindow : Window
    {
        public CtsConfirmWindow(string title, string message, string confirmText, string cancelText)
        {
            Title = title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            MinHeight = 210;
            MaxHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            // The theme must be registered BEFORE any FindResource() call.
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

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextBrush")
            };
            Grid.SetColumn(titleBlock, 1);
            header.Children.Add(titleBlock);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var text = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(4, 0, 4, 14)
            };
            Grid.SetRow(text, 1);
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var cancel = new Button
            {
                Content = cancelText,
                Width = 88,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)FindResource("SurfaceAltBrush"),
                Foreground = (Brush)FindResource("TextBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };
            cancel.Click += delegate { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);

            var confirm = new Button
            {
                Content = confirmText,
                Width = 88,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(255, 77, 35)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            confirm.Click += delegate { DialogResult = true; Close(); };
            buttons.Children.Add(confirm);

            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            Content = grid;
            ApplyWindowIcon();
        }

        private static string LogoPath()
        {
            return Path.Combine(
                Path.GetDirectoryName(typeof(CtsConfirmWindow).Assembly.Location),
                "Resources", "Icons", "CTS Icon.png");
        }

        private static BitmapImage LoadLogo()
        {
            try
            {
                string path = LogoPath();
                if (!File.Exists(path)) return null;
                var bitmap = new BitmapImage();
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

        private static void SetLogo(Image image)
        {
            BitmapImage bitmap = LoadLogo();
            if (bitmap != null) image.Source = bitmap;
        }

        private void ApplyWindowIcon()
        {
            BitmapImage bitmap = LoadLogo();
            if (bitmap != null) Icon = bitmap;
        }

        /// <summary>Shows the pop-up and returns true when the user confirms.</summary>
        public static bool Ask(string title, string message, string confirmText = "OK", string cancelText = "CANCEL")
        {
            bool? result = new CtsConfirmWindow(title, message, confirmText, cancelText).ShowDialog();
            return result == true;
        }
    }
}

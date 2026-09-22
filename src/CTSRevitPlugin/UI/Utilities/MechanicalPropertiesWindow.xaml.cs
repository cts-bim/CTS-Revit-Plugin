using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Utilities
{
    public partial class MechanicalPropertiesWindow : Window
    {
        public MechanicalPropertiesWindow(MechanicalPropertiesSnapshot snapshot)
        {
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();
            ApplyWindowIcon();
            IdText.Text = snapshot.ElementId;
            CategoryText.Text = snapshot.Category;
            FamilyText.Text = snapshot.Family;
            TypeText.Text = snapshot.Type;
            HeaderSubtitle.Text = snapshot.ElementCount + " item(s) reported";
            BuildRows(snapshot);
        }

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(MechanicalPropertiesWindow).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterCtsLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterMotto, Path.Combine(root, "No bull Just build.png"));
        }

        private static void SetImage(Image image, string path)
        {
            if (image == null || !File.Exists(path)) return;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap;
            } catch { }
        }

        private void ApplyWindowIcon()
        {
            try
            {
                string root = Path.Combine(Path.GetDirectoryName(typeof(MechanicalPropertiesWindow).Assembly.Location), "Resources", "Icons");
                string path = Path.Combine(root, "CTS Icon.png");
                if (!File.Exists(path)) return;
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                Icon = bitmap;
            }
            catch { }
        }

        private void BuildRows(MechanicalPropertiesSnapshot snapshot)
        {
            string group = null;
            foreach (MechanicalPropertyItem item in snapshot.Items)
            {
                // This window is a static read-only report, so the row that only
                // exists to trigger the pane's on-demand ancillary load has
                // nothing to do here.
                if (item.Kind == MechanicalPropertyKind.Action) continue;

                if (!string.Equals(group, item.Group, StringComparison.OrdinalIgnoreCase))
                {
                    group = item.Group;
                    PropertiesPanel.Children.Add(new TextBlock { Text = group, Style = (Style)FindResource("Group") });
                }

                Grid row = new Grid { Margin = new Thickness(3, 1, 3, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = item.Name, Style = (Style)FindResource("Cell"), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(row.Children[row.Children.Count - 1], 0);
                row.Children.Add(new TextBlock { Text = item.Value, Style = (Style)FindResource("Cell"), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(row.Children[row.Children.Count - 1], 1);
                PropertiesPanel.Children.Add(row);
            }
        }
    }
}
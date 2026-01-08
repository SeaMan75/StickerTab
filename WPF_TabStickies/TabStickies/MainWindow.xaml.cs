using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TabStickies
{
    public partial class MainWindow : Window
    {
        private const string ConfigPath = "sticker.yaml";
        private List<StickerTab> _tabs = new();
        private StickerTab? _selectedTab;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RenameCurrentTab()
        {
            if (_selectedTab == null) return;

            // Создаём маленькое окно для ввода
            var dialog = new Window
            {
                Title = "Переименовать вкладку",
                Width = 300,
                Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(10) };
            stack.Children.Add(new TextBlock { Text = "Новое название:", Margin = new Thickness(0, 0, 0, 5) });

            var textBox = new TextBox { Text = _selectedTab.Title, Width = 250 };
            textBox.SelectAll();
            textBox.Focus();

            var okButton = new Button { Content = "OK", Width = 60, Margin = new Thickness(0, 10, 0, 0) };
            bool confirmed = false;

            okButton.Click += (_, __) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    _selectedTab.Title = textBox.Text.Trim();
                    RefreshTabs();
                    confirmed = true;
                }
                dialog.Close();
            };

            textBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                    okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else if (args.Key == Key.Escape)
                    dialog.Close();
            };

            stack.Children.Add(textBox);
            stack.Children.Add(okButton);
            dialog.Content = stack;

            dialog.ShowDialog();

            if (confirmed)
            {
                Title = _selectedTab.Title; // обновляем заголовок окна
                SaveConfig(); // сразу сохраняем!
            }
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && _selectedTab != null)
            {
                RenameCurrentTab();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var config = LoadConfig();            // ← получаем конфиг
            this.Topmost = config.AlwaysOnTop;    // ← применяем Topmost
            RefreshTabs();
            if (_tabs.Count > 0)
                SelectTab(_tabs[0]);
        }

        private StickerConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                SaveConfig();
                return new StickerConfig(); // пустой конфиг
            }

            try
            {
                var yaml = File.ReadAllText(ConfigPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var config = deserializer.Deserialize<StickerConfig>(yaml);
                _tabs = config.Tabs;
                return config;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}");
                return new StickerConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new StickerConfig { Tabs = _tabs };
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var yaml = serializer.Serialize(config);
                File.WriteAllText(ConfigPath, yaml);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private void RefreshTabs()
        {
            TabsPanel.Children.Clear();
            foreach (var tab in _tabs)
            {
                var button = CreateTabButton(tab);
                TabsPanel.Children.Add(button);
            }
        }

        private Button CreateTabButton(StickerTab tab)
        {
            // Создаём трапецию (скошенный прямоугольник)
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L100,0 L120,30 L0,30 Z"),
                Fill = tab == _selectedTab ? Brushes.LightYellow : Brushes.GreenYellow,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                Width = 120,
                Height = 30
            };

            // Надпись
            var text = new TextBlock
            {
                Text = tab.Title,
                Foreground = Brushes.Black,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };

            // Комбинируем в один элемент
            var grid = new Grid();
            grid.Children.Add(path);
            grid.Children.Add(text);

            // Кнопка-вкладка
            var button = new Button
            {
                Content = grid,
                Width = 120,
                Height = 30,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent
            };

            // Одиночный клик — выбор вкладки
            button.Click += (_, __) => SelectTab(tab);

            // 🔥 Двойной клик — удаление
            button.MouseDoubleClick += (_, __) =>
            {
                if (_tabs.Count <= 1)
                {
                    MessageBox.Show("Нельзя удалить последнюю вкладку.", "Инфо", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _tabs.Remove(tab);
                RefreshTabs();
                SelectTab(_tabs[0]); // выбираем первую оставшуюся
            };

            return button;
        }

        private void SelectTab(StickerTab tab)
        {
            _selectedTab = tab;
            ContentEditor.Text = tab.Content;
            RefreshTabs();
            Title = tab.Title;
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var tab = new StickerTab
            {
                Id = now,
                Title = $"Записка ({_tabs.Count + 1})",
                Content = ""
            };
            _tabs.Add(tab);
            RefreshTabs();
            SelectTab(tab);
        }

        private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedTab != null)
                _selectedTab.Content = ContentEditor.Text;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            Close();
        }
    }

    public class StickerConfig
    {
        public bool AlwaysOnTop { get; set; } = true;
        public List<StickerTab> Tabs { get; set; } = new();
    }

    public class StickerTab
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
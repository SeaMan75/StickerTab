using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using WpfPath = System.Windows.Shapes.Path;

namespace TabStickies
{
    public partial class MainWindow : Window
    {
        private const string ConfigPath = "sticker.yaml";
        private List<StickerTab> _tabs = new();
        private StickerTab? _selectedTab;
        private int _lastSearchOffset = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RenameCurrentTab()
        {
            if (_selectedTab == null) return;

            var dialog = new Window
            {
                Title = "Переименовать вкладку",
                Width = 300,
                Height = 150, // ← увеличено с 120 до 150
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(10) };
            stack.Children.Add(new TextBlock { Text = "Новое название:", Margin = new Thickness(0, 0, 0, 5) });

            var textBox = new TextBox { Text = _selectedTab.Title, Width = 250 };
            textBox.SelectAll();
            textBox.Focus();

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var okButton = new Button { Content = "OK", Width = 60, Margin = new Thickness(0, 0, 5, 0) };
            var cancelButton = new Button { Content = "Отмена", Width = 70 };

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

            cancelButton.Click += (_, __) => dialog.Close();
            textBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    args.Handled = true;
                }
                else if (args.Key == Key.Escape)
                {
                    dialog.Close(); // ← точно закрываем
                    args.Handled = true;
                }
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(textBox);
            stack.Children.Add(buttonPanel);
            dialog.Content = stack;
            dialog.SourceInitialized += (_, __) => textBox.Focus();

            dialog.ShowDialog();

            if (confirmed)
            {
                Title = _selectedTab.Title;
                SaveConfig();
            }
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void TextEditor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Получаем текущий размер шрифта
                double currentSize = TextEditor.FontSize;

                // Изменяем на 1 пункт за "щелчок" колеса
                if (e.Delta > 0)
                {
                    // Увеличить, но не больше 72
                    TextEditor.FontSize = Math.Min(currentSize + 1, 72);
                }
                else if (currentSize > 6) // Минимум 6pt
                {
                    // Уменьшить
                    TextEditor.FontSize = currentSize - 1;
                }

                e.Handled = true; // ← предотвращаем скролл текста

                if (_selectedTab != null)
                {
                    _selectedTab.FontSize = TextEditor.FontSize;
                    SaveConfig(); // или SaveConfigSilent()
                }
            }
        }

        private void ApplyHighlight(string highlightName)
        {
            if (string.IsNullOrEmpty(highlightName) || highlightName == "Plain Text")
            {
                TextEditor.SyntaxHighlighting = null;
                return;
            }

            try
            {
                TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(highlightName);
            }
            catch
            {
                TextEditor.SyntaxHighlighting = null;
                StatusText.Text = $"Подсветка '{highlightName}' не найдена";
            }
        }

        private void SaveConfigSilent(StickerConfig config)
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yaml = serializer.Serialize(config);
                File.WriteAllText(ConfigPath, yaml);
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            var config = LoadConfig();
            config.WindowLeft = this.Left;
            config.WindowTop = this.Top;
            config.WindowWidth = this.Width;
            config.WindowHeight = this.Height;
            config.WindowPositionSet = true;
            SaveConfigSilent(config);

            base.OnClosing(e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        
            // Загрузка данных
            var config = LoadConfig();
            // Восстанавливаем позицию и размер
            if (config.WindowPositionSet)
            {
                this.Left = config.WindowLeft;
                this.Top = config.WindowTop;
                this.Width = config.WindowWidth;
                this.Height = config.WindowHeight;
            }
            else
            {
                this.Width = 600;
                this.Height = 400;
                // Центрируем при первом запуске
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Topmost = config.AlwaysOnTop;
            // Фон — зелёный
            //var bgColor = Color.FromRgb(0xCC, 0xFF, 0x90);
            //TextEditor.Background = new SolidColorBrush(bgColor);
            //TextEditor.TextArea.Background = new SolidColorBrush(bgColor);

            // Курсор — полупрозрачный красный, толстый
            TextEditor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.FromArgb(180, 255, 0, 0));
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
                StatusText.Text = "Конфигурация сохранена";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private void TextEditor_TextChanged(object sender, EventArgs e)
        {
            if (_selectedTab != null)
            {
                _selectedTab.Content = TextEditor.Text;
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
        private LinearGradientBrush CreateTabBrush(bool isSelected)
        {
            return new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops = new GradientStopCollection
        {
            new GradientStop(isSelected
                ? Color.FromRgb(0xAA, 0xDD, 0x70) // темнее
                : Color.FromRgb(0xEE, 0xFF, 0xB0), // светлее
                0.0),
            new GradientStop(Colors.White, 1.0)
        }
            };
        }

        private Geometry CreateTabGeometry(double width, double height, bool isSelected)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                double cornerRadius = 4;
                double skewOffset = 10; // скос

                if (isSelected)
                {
                    // Активная вкладка — прямоугольник со скруглением (без скоса справа)
                    ctx.BeginFigure(new Point(0, cornerRadius), true, true);
                    ctx.ArcTo(new Point(cornerRadius, 0), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                    ctx.LineTo(new Point(width - cornerRadius, 0), true, true);
                    ctx.ArcTo(new Point(width, cornerRadius), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                    ctx.LineTo(new Point(width, height - cornerRadius), true, true);
                    ctx.ArcTo(new Point(width - cornerRadius, height), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                    ctx.LineTo(new Point(cornerRadius, height), true, true);
                    ctx.ArcTo(new Point(0, height - cornerRadius), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                }
                else
                {
                    // Неактивная — скос справа
                    ctx.BeginFigure(new Point(0, cornerRadius), true, true);
                    ctx.ArcTo(new Point(cornerRadius, 0), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                    ctx.LineTo(new Point(width - skewOffset, 0), true, true);
                    ctx.LineTo(new Point(width, height / 2), true, true);
                    ctx.LineTo(new Point(width - skewOffset, height), true, true);
                    ctx.LineTo(new Point(cornerRadius, height), true, true);
                    ctx.ArcTo(new Point(0, height - cornerRadius), new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise, true, true);
                }
            }
            geometry.Freeze();
            return geometry;
        }
        //    private Button CreateTabButton(StickerTab tab)
        //    {
        //        var gradient = new LinearGradientBrush
        //        {
        //            StartPoint = new Point(0.5, 0),
        //            EndPoint = new Point(0.5, 1),
        //            GradientStops =
        //{
        //    new GradientStop(
        //        tab == _selectedTab
        //            ? Color.FromRgb(0xFF, 0xFF, 0xC0) // светло-жёлтый
        //            : Color.FromRgb(0xCC, 0xFF, 0x90), // обычный зелёный
        //        0.0),
        //    new GradientStop(Colors.White, 1.0)
        //}
        //        };

        //        // Создаём трапецию (скошенный прямоугольник)
        //        var path = new System.Windows.Shapes.Path
        //        {
        //            Data = Geometry.Parse("M0,0 L100,0 L120,30 L0,30 Z"),
        //            //Fill = tab == _selectedTab ? Brushes.LightYellow : Brushes.GreenYellow,
        //            //Fill = new LinearGradientBrush
        //            //{
        //            //    StartPoint = new Point(0.5, 0),
        //            //    EndPoint = new Point(0.5, 1),
        //            //    GradientStops =
        //            //    {
        //            //        new GradientStop(Color.FromRgb(0xCC, 0xFF, 0x90), 0.0),
        //            //        new GradientStop(Colors.White, 1.0)
        //            //    }
        //            //},
        //            Fill = gradient,
        //            Stroke = Brushes.Gray,
        //            StrokeThickness = 2,
        //            Width = 120,
        //            Height = 30
        //        };

        //        // Надпись
        //        var text = new TextBlock
        //        {
        //            Text = tab.Title,
        //            Foreground = Brushes.Black,
        //            FontSize = 12,
        //            HorizontalAlignment = HorizontalAlignment.Center,
        //            VerticalAlignment = VerticalAlignment.Center,
        //            Margin = new Thickness(0, 0, 20, 0)
        //        };

        //        // Комбинируем в один элемент
        //        var grid = new Grid();
        //        grid.Children.Add(path);
        //        grid.Children.Add(text);

        //        // Кнопка-вкладка
        //        var button = new Button
        //        {
        //            Content = grid,
        //            Width = 120,
        //            Height = 30,
        //            Background = Brushes.Transparent,
        //            BorderBrush = Brushes.Transparent
        //        };

        //        // Одиночный клик — выбор вкладки
        //        button.Click += (_, __) => SelectTab(tab);

        //        // 🔥 Двойной клик — удаление
        //        button.MouseDoubleClick += (_, __) =>
        //        {
        //            if (_tabs.Count <= 1)
        //            {
        //                MessageBox.Show("Нельзя удалить последнюю вкладку.", "Инфо", MessageBoxButton.OK, MessageBoxImage.Information);
        //                return;
        //            }

        //            _tabs.Remove(tab);
        //            RefreshTabs();
        //            SelectTab(_tabs[0]); // выбираем первую оставшуюся
        //        };

        //        return button;
        //    }

        private Button CreateTabButton(StickerTab tab)
        {
            double width = 120;
            double height = 30;

            var path = new WpfPath
            {
                Data = CreateTabGeometry(width, height, tab == _selectedTab),
                Fill = CreateTabBrush(tab == _selectedTab),
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                Width = width,
                Height = height
            };

            var text = new TextBlock
            {
                Text = tab.Title,
                Foreground = Brushes.Black,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var grid = new Grid();
            grid.Children.Add(path);
            grid.Children.Add(text);

            var button = new Button
            {
                Content = grid,
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent
            };

            button.Click += (_, __) => SelectTab(tab);
            button.MouseDoubleClick += (_, __) =>
            {
                if (_tabs.Count <= 1) return;
                _tabs.Remove(tab);
                RefreshTabs();
                SelectTab(_tabs[0]);
            };

            return button;
        }

        private void ToggleSearchPanel(object sender, RoutedEventArgs e)
        {
            bool isVisible = SearchBox.Visibility == Visibility.Visible;
            SearchBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            CloseSearchButton.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;

            if (!isVisible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        }

        private void CloseSearchPanel(object sender, RoutedEventArgs e)
        {
            SearchBox.Visibility = Visibility.Collapsed;
            CloseSearchButton.Visibility = Visibility.Collapsed;
            _lastSearchOffset = 0;
        }

        private void ShowGlobalSearchDialog()
        {
            var dialog = new Window
            {
                Title = "Поиск по всем вкладкам",
                Width = this.Width / 2,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Owner = this
            };

            // Позиционируем справа
            dialog.Left = this.Left + this.Width - dialog.Width;
            dialog.Top = this.Top + 30;

            string currentQuery = "";

            var grid = new Grid();
            var textBox = new TextBox { Margin = new Thickness(5) };
            var resultsList = new ListBox { Margin = new Thickness(5) };
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 5, 5)
            };
            var findButton = new Button { Content = "Найти", Width = 60, Margin = new Thickness(0, 0, 5, 0) };
            var closeButton = new Button { Content = "Закрыть", Width = 70 };

            buttonPanel.Children.Add(findButton);
            buttonPanel.Children.Add(closeButton);

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(textBox, 0);
            Grid.SetRow(resultsList, 1);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(textBox);
            grid.Children.Add(resultsList);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.SourceInitialized += (_, __) => textBox.Focus();

            findButton.Click += (_, __) =>
            {
                resultsList.Items.Clear();
                currentQuery = textBox.Text.Trim();
                if (string.IsNullOrEmpty(currentQuery)) return;

                foreach (var tab in _tabs)
                {
                    var content = tab.Content;
                    var index = 0;
                    while ((index = content.IndexOf(currentQuery, index, StringComparison.OrdinalIgnoreCase)) != -1)
                    {
                        resultsList.Items.Add(new SearchResultItem
                        {
                            Tab = tab,
                            StartIndex = index,
                            Preview = GetPreview(content, index, currentQuery.Length)
                        });
                        index += currentQuery.Length;
                    }
                }

                if (resultsList.Items.Count == 0)
                {
                    StatusText.Text = "Ничего не найдено";
                }
            };

            resultsList.SelectionChanged += (_, __) =>
            {
                if (resultsList.SelectedItem is SearchResultItem item)
                {
                    SelectTab(item.Tab);
                    TextEditor.Select(item.StartIndex, currentQuery.Length);
                    TextEditor.ScrollToLine(TextEditor.Document.GetLineByOffset(item.StartIndex).LineNumber);
                }
            };

            closeButton.Click += (_, __) => dialog.Close();

            dialog.Show(); // ← Show(), а не ShowDialog(), чтобы не блокировать стикер
        }
        private string GetPreview(string content, int startIndex, int length)
        {
            int start = Math.Max(0, startIndex - 20);
            int end = Math.Min(content.Length, startIndex + length + 20);
            string preview = content.Substring(start, end - start);
            return "..." + preview + "...";
        }

        private class SearchResultItem
        {
            public StickerTab Tab { get; set; } = null!;
            public int StartIndex { get; set; }
            public string Preview { get; set; } = "";

            public override string ToString() => $"{Tab.Title}: {Preview}";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && _selectedTab != null)
            {
                RenameCurrentTab();
            }

            else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                ShowGlobalSearchDialog();
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveConfig();
            }

            else if (e.Key == Key.F3)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    FindPrevious();
                else
                    FindNext();
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+F — показать/скрыть панель поиска и фокус на поле
                if (SearchBox.Visibility == Visibility.Visible)
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }
                else
                {
                    ToggleSearchPanel(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Insert)
            {
                TextEditor.TextArea.OverstrikeMode = !TextEditor.TextArea.OverstrikeMode;
            }
        }

        private void SelectTab(StickerTab tab)
        {
            _selectedTab = tab;
            TextEditor.Text = tab.Content ?? "";
            TextEditor.FontSize = tab.FontSize; // ← загружаем размер
            ApplyHighlight(tab.Highlight);
            RefreshTabs();
            Title = tab.Title;
        }

        private void Editor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var editor = (TextEditor)sender;
            var point = e.GetPosition(editor.TextArea.TextView);
            var pos = editor.TextArea.TextView.GetPosition(point);
            if (pos != null)
            {
                editor.CaretOffset = editor.Document.GetOffset(pos.Value.Location);
            }

            var menu = new ContextMenu();

            // Вставка шаблонов (если есть)
            // ... (оставь свой код)

            // Подсветка
            var highlightMenu = new MenuItem { Header = "Подсветка" };
            var definitions = HighlightingManager.Instance.HighlightingDefinitions;

            var noHighlightItem = new MenuItem
            {
                Header = "Без подсветки",
                Tag = "Plain Text",
                IsChecked = (_selectedTab?.Highlight == "Plain Text")
            };
            noHighlightItem.Click += HighlightMenuItem_Click; // ✅ Правильно
            highlightMenu.Items.Add(noHighlightItem);

            foreach (var def in definitions)
            {
                var item = new MenuItem
                {
                    Header = def.Name,
                    Tag = def.Name,
                    IsChecked = (_selectedTab?.Highlight == def.Name)
                };
                item.Click += HighlightMenuItem_Click; // ✅
                highlightMenu.Items.Add(item);
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    FindPrevious();
                else
                    FindNext();
                e.Handled = true;
            }
        }

        private void HighlightMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string name && _selectedTab != null)
            {
                _selectedTab.Highlight = name;
                ApplyHighlight(name);
                SaveConfig(); // сохраняем сразу
            }
        }

        private void FindNext()
        {
            var query = SearchBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            var start = _lastSearchOffset + 1;
            if (start >= TextEditor.Text.Length) start = 0;

            var index = TextEditor.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                TextEditor.Select(index, query.Length);
                _lastSearchOffset = index;
                StatusText.Text = $"Найдено: позиция {index}";
            }
            else
            {
                StatusText.Text = "Ничего не найдено";
                _lastSearchOffset = 0;
            }
        }

        private void FindPrevious()
        {
            var query = SearchBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            var start = _lastSearchOffset - 1;
            if (start < 0) start = TextEditor.Text.Length - 1;

            var index = TextEditor.Text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                TextEditor.Select(index, query.Length);
                _lastSearchOffset = index;
                StatusText.Text = $"Найдено: позиция {index}";
            }
            else
            {
                StatusText.Text = "Ничего не найдено";
                _lastSearchOffset = TextEditor.Text.Length;
            }
        }
          
        private void TextEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var editor = (TextEditor)sender;

            // Убедимся, что позиция корректна
            var caretOffset = editor.CaretOffset;
            if (caretOffset < 0) caretOffset = 0;
            if (caretOffset > editor.Document.TextLength) caretOffset = editor.Document.TextLength;
            editor.CaretOffset = caretOffset;

            var menu = new ContextMenu();

            // Подсветка
            var highlightMenu = new MenuItem { Header = "Подсветка" };
            var definitions = HighlightingManager.Instance.HighlightingDefinitions;

            var noHighlightItem = new MenuItem
            {
                Header = "Без подсветки",
                Tag = "Plain Text",
                IsChecked = (_selectedTab?.Highlight == "Plain Text")
            };
            noHighlightItem.Click += HighlightMenuItem_Click;
            highlightMenu.Items.Add(noHighlightItem);

            foreach (var def in definitions)
            {
                var item = new MenuItem
                {
                    Header = def.Name,
                    Tag = def.Name,
                    IsChecked = (_selectedTab?.Highlight == def.Name)
                };
                item.Click += HighlightMenuItem_Click;
                highlightMenu.Items.Add(item);
            }

            menu.Items.Add(highlightMenu);
            editor.ContextMenu = menu;
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

        // Позиция и размер главного окна
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 600;
        public double WindowHeight { get; set; } = 400;
        public bool WindowPositionSet { get; set; } = false;
    }

    public class StickerTab
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Highlight { get; set; } = "Markdown";
        public double FontSize { get; set; } = 14;
    }
}
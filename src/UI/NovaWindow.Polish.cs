using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using RezeNova.Core;

namespace RezeNova.UI
{
    internal sealed partial class NovaWindow
    {
        private void ShowStyledGuide()
        {
            var dialog = new Window
            {
                Owner = this,
                Title = "Инструкция Reze Nova",
                Width = 1040,
                Height = 880,
                MinWidth = 820,
                MinHeight = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                Foreground = TextBrush,
                FontFamily = FontFamily,
                FontSize = 13
            };

            var shell = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("0C0818"), ColorFrom("171129"), 25),
                BorderBrush = Brush(ColorFrom("664A76")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    BlurRadius = 32,
                    ShadowDepth = 0,
                    Opacity = 0.48
                }
            };

            dialog.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape)
                {
                    dialog.Close();
                    args.Handled = true;
                }
            };
            dialog.SizeChanged += delegate
            {
                shell.Clip = new RectangleGeometry(new Rect(0, 0, dialog.ActualWidth, dialog.ActualHeight), 24, 24);
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

            var header = new Grid { Margin = new Thickness(1) };
            header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.16, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.84, GridUnitType.Star) });
            var headerArt = new Border { Background = Brush(ColorFrom("211A30")), Margin = new Thickness(14, 14, 0, 14), CornerRadius = new CornerRadius(15), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            ImageSource image = LoadImage("reze-set-02.jpg");
            if (image == null) image = LoadImage("hero.jpg");
            if (image != null)
            {
                headerArt.Background = new ImageBrush(image)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            }
            Grid.SetColumn(headerArt, 1);
            Grid.SetRow(headerArt, 1);
            header.Children.Add(headerArt);
            var headerShade = new Border { Background = new LinearGradientBrush(Color.FromArgb(245, 10, 7, 20), Color.FromArgb(12, 10, 7, 20), 0) };
            Grid.SetColumn(headerShade, 1);
            Grid.SetRow(headerShade, 1);
            header.Children.Add(headerShade);

            var headerCopy = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(28, 0, 28, 22)
            };
            Grid.SetColumn(headerCopy, 0);
            Grid.SetRow(headerCopy, 1);
            headerCopy.Children.Add(new TextBlock
            {
                Text = "REZE NOVA  /  GUIDE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = CyanBrush
            });
            headerCopy.Children.Add(new TextBlock
            {
                Text = "Тихий маршрут.\nПонятный контроль.",
                FontSize = 28,
                LineHeight = 32,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0)
            });
            headerCopy.Children.Add(new TextBlock
            {
                Text = "Основные шаги запуска, режимы, сервисы и восстановление.",
                FontSize = 11,
                Foreground = TextBrush,
                Opacity = 0.86,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(1, 8, 0, 0)
            });
            header.Children.Add(headerCopy);
            headerCopy.MouseLeftButtonDown += delegate { dialog.DragMove(); };

            var close = GuideButton("ЗАКРЫТЬ  ×", FieldBrush, delegate { dialog.Close(); });
            close.Width = 116;
            close.Height = 34;
            close.FontSize = 10;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Center;
            close.Margin = new Thickness(0, 0, 18, 0);
            Grid.SetColumn(close, 0);
            Grid.SetColumnSpan(close, 2);
            Grid.SetRow(close, 0);
            header.Children.Add(close);
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            var content = new StackPanel { Margin = new Thickness(24, 24, 24, 28) };
            content.Children.Add(new TextBlock
            {
                Text = "Сначала запусти маршрут и дождись статуса «Активен». Затем выбери профиль, открой сервис и выполни проверку сети. Если Nova сообщает о другом запрете, это означает, что WinDivert уже занят отдельным winws2 или winws.",
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 22)
            });

            var start = GuideSection(
                "01  /  СТАРТ МАРШРУТА",
                "Запуск и запрос UAC",
                "Нажми «Запустить маршрут». Для WinDivert нужны права администратора. Если Windows покажет окно контроля учётных записей, подтверди запрос. После подтверждения Nova продолжит запуск в повышенном окне автоматически. Дождись статуса «АКТИВЕН», затем открывай приложения и проверяй соединение.");
            ((StackPanel)start.Child).Children.Add(GuideButtonRow(
                GuideButton("ЗАПУСТИТЬ МАРШРУТ", PinkBrush, delegate { StartRoute(); }),
                GuideButton("ОСТАНОВИТЬ МАРШРУТ", FieldBrush, delegate { StopRoute(); })));
            content.Children.Add(start);

            var modes = GuideSection(
                "02  /  ПРОФИЛИ",
                "Четыре режима и горячие клавиши",
                "Режим можно менять до запуска или во время работы. При смене активного маршрута Nova применит выбранный профиль и перезапустит его.");
            ((StackPanel)modes.Child).Children.Add(GuideModeLine("A", "Авто", "сбалансированный маршрут", "Alt + A", NovaMode.Auto));
            ((StackPanel)modes.Child).Children.Add(GuideModeLine("1", "Быстрый", "минимальная задержка", "Alt + 1", NovaMode.Fast));
            ((StackPanel)modes.Child).Children.Add(GuideModeLine("2", "Стабильный", "запас устойчивости", "Alt + 2", NovaMode.Stable));
            ((StackPanel)modes.Child).Children.Add(GuideModeLine("3", "Широкий", "максимальный охват", "Alt + 3", NovaMode.Wide));
            content.Children.Add(modes);

            var services = GuideSection(
                "03  /  СЕРВИСЫ",
                "Управление приложениями",
                "После запуска маршрута открой нужное приложение. Discord можно открыть из Nova. Кнопка Telegram поднимает локальный прокси на 127.0.0.1:1443 и открывает Telegram Desktop. Для полной остановки прокси используй отдельную кнопку.");
            ((StackPanel)services.Child).Children.Add(GuideButtonRow(
                GuideButton("ОТКРЫТЬ DISCORD", FieldBrush, delegate { OpenDiscord(); }),
                GuideButton("ОТКРЫТЬ YOUTUBE", FieldBrush, delegate
                {
                    Process.Start(new ProcessStartInfo("https://www.youtube.com/") { UseShellExecute = true });
                }),
                GuideButton("ПОДКЛЮЧИТЬ TELEGRAM", CyanBrush, delegate { StartTelegram(true); }),
                GuideButton("ОСТАНОВИТЬ ПРОКСИ", FieldBrush, delegate { StopTelegram(); })));
            content.Children.Add(services);

            var diagnostics = GuideSection(
                "04  /  ПРОВЕРКА",
                "Сеть и диагностика",
                "«Проверить сеть» отдельно открывает страницу YouTube, запрашивает player API, находит настоящий URL googlevideo.com и получает небольшой фрагмент видеопотока. Так проверяется именно доставка видео. Если Nova сообщает о другом запрете, закрой второй winws2 или winws и запусти Nova снова.");
            ((StackPanel)diagnostics.Child).Children.Add(GuideButtonRow(
                GuideButton("ПРОВЕРИТЬ СЕТЬ", FieldBrush, delegate { RunNetworkProbe(); }),
                GuideButton("ДИАГНОСТИКА DISCORD", FieldBrush, delegate { RunDiscordDiagnostics(); }),
                GuideButton("СКОПИРОВАТЬ ОТЧЁТ", FieldBrush, delegate { CopyDiagnostics(); }),
                GuideButton("ОТКРЫТЬ ЛОГИ", FieldBrush, delegate { OpenFolder(logDirectory); })));
            content.Children.Add(diagnostics);

            var links = GuideSection(
                "05  /  МОИ ССЫЛКИ",
                "Сайты и игровые входы",
                "Открой отдельное окно «Мои ссылки» из правой панели. Там можно сохранить название и адрес сайта или игрового сервера. HTTPS-домен Nova добавит в пользовательский маршрут. Для Steam используй steam://connect/адрес:порт или просто адрес:порт. Сохранённый сервер можно открыть кнопкой «Открыть», а его IP и порт будут учтены в маршруте.");
            content.Children.Add(links);

            var tray = GuideSection(
                "06  /  ТРЕЙ",
                "Работа в фоне",
                "Кнопка закрытия окна прячет Nova в системный трей, а маршрут и сервисы продолжают работать. Двойной щелчок по значку снова показывает окно. В контекстном меню доступны запуск, остановка маршрута, диагностика Discord и полный выход из приложения.");
            ((StackPanel)tray.Child).Children.Add(new TextBlock
            {
                Text = "Подсказка  ·  Alt + Ctrl + D запускает диагностику Discord из любого приложения.",
                FontSize = 10,
                Foreground = AmberBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            content.Children.Add(tray);

            var body = new Grid { Margin = new Thickness(24, 22, 24, 24) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var navigation = new Border
            {
                Background = Brush(ColorFrom("120D20")),
                BorderBrush = Brush(ColorFrom("33274B")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10, 12, 10, 10),
                Margin = new Thickness(0, 0, 14, 0)
            };
            var navigationStack = new StackPanel();
            navigationStack.Children.Add(new TextBlock
            {
                Text = "РАЗДЕЛЫ",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = MutedBrush,
                Margin = new Thickness(3, 0, 0, 9)
            });
            navigationStack.Children.Add(GuideNavigationButton("01  Старт", start));
            navigationStack.Children.Add(GuideNavigationButton("02  Профили", modes));
            navigationStack.Children.Add(GuideNavigationButton("03  Сервисы", services));
            navigationStack.Children.Add(GuideNavigationButton("04  Проверка", diagnostics));
            navigationStack.Children.Add(GuideNavigationButton("05  Ссылки", links));
            navigationStack.Children.Add(GuideNavigationButton("06  Трей", tray));
            navigationStack.Children.Add(new TextBlock
            {
                Text = "Колесо мыши, ползунок и клик по дорожке работают в правой панели.", 
                FontSize = 9,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(3, 18, 3, 0)
            });
            navigation.Child = navigationStack;
            Grid.SetColumn(navigation, 0);
            body.Children.Add(navigation);

            var scroll = ThemedScroll(content);
            Grid.SetColumn(scroll, 1);
            body.Children.Add(scroll);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            var footer = new Grid { Margin = new Thickness(24, 8, 24, 12) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            footer.Children.Add(new TextBlock
            {
                Text = "REZE NOVA  ·  Esc — закрыть", 
                Foreground = MutedBrush,
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center
            });
            var done = GuideButton("ЗАКРЫТЬ ИНСТРУКЦИЮ", PinkBrush, delegate { dialog.Close(); });
            done.Height = 34;
            Grid.SetColumn(done, 1);
            footer.Children.Add(done);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            shell.Child = layout;
            dialog.Content = shell;
            dialog.ShowDialog();
        }

        private Border GuideSection(string number, string title, string description)
        {
            var card = new Border
            {
                Background = Brush(ColorFrom("151128")),
                BorderBrush = Brush(ColorFrom("3F315B")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(18, 17, 18, 18),
                Margin = new Thickness(0, 0, 0, 18)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = number,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = PinkBrush
            });
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        private Button GuideNavigationButton(string text, UIElement target)
        {
            var button = GuideButton(text, FieldBrush, delegate { ((FrameworkElement)target).BringIntoView(); });
            button.Width = 112;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Height = 32;
            button.FontSize = 9;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(8, 3, 5, 3);
            button.Margin = new Thickness(0, 2, 0, 0);
            return button;
        }

        private UIElement GuideModeLine(string key, string title, string description, string shortcut, NovaMode mode)
        {
            var button = GuideButton(title + "  ·  " + shortcut, mode == selectedMode ? Brush(ColorFrom("35203E")) : FieldBrush, delegate { SelectMode(mode, true); });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 5, 12, 5);
            button.Margin = new Thickness(0, 8, 0, 0);
            button.ToolTip = description;
            return button;
        }

        private WrapPanel GuideButtonRow(params Button[] buttons)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 17, 0, 0) };
            foreach (Button button in buttons) row.Children.Add(button);
            return row;
        }

        private Button GuideButton(string text, Brush background, Action action)
        {
            var button = ActionButton(text, background, action);
            button.Width = 164;
            button.Height = 40;
            button.MinHeight = 40;
            button.Margin = new Thickness(0, 0, 9, 9);
            ApplyButtonTemplate(button, background, background == PinkBrush || background == CyanBrush ? background : LineBrush, background == PinkBrush || background == CyanBrush ? CanvasBrush : TextBrush, 10);
            return button;
        }

        private ScrollViewer ThemedScroll(UIElement content)
        {
            const string template =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ScrollViewer}\">" +
                "<ControlTemplate.Resources>" +
                "<Style x:Key=\"RezeScrollBar\" TargetType=\"{x:Type ScrollBar}\">" +
                "<Setter Property=\"Background\" Value=\"Transparent\"/>" +
                "<Setter Property=\"Foreground\" Value=\"#FFFF4BD8\"/>" +
                "<Setter Property=\"Template\">" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type ScrollBar}\">" +
                "<Grid Margin=\"1\">" +
                "<Track x:Name=\"PART_Track\" Minimum=\"{TemplateBinding Minimum}\" Maximum=\"{TemplateBinding Maximum}\" Value=\"{TemplateBinding Value}\" ViewportSize=\"{TemplateBinding ViewportSize}\" Orientation=\"{TemplateBinding Orientation}\" IsDirectionReversed=\"True\">" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton x:Name=\"PART_DecreaseButton\" Command=\"{x:Static ScrollBar.PageUpCommand}\" CommandTarget=\"{Binding RelativeSource={RelativeSource TemplatedParent}}\" Background=\"Transparent\" BorderThickness=\"0\"/>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb x:Name=\"PART_Thumb\">" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType=\"{x:Type Thumb}\">" +
                "<Border Margin=\"1\" Background=\"#FFC03C9A\" BorderBrush=\"#FFFF78D9\" BorderThickness=\"1\" CornerRadius=\"4\"/>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton x:Name=\"PART_IncreaseButton\" Command=\"{x:Static ScrollBar.PageDownCommand}\" CommandTarget=\"{Binding RelativeSource={RelativeSource TemplatedParent}}\" Background=\"Transparent\" BorderThickness=\"0\"/>" +
                "</Track.IncreaseRepeatButton>" +
                "</Track>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"Orientation\" Value=\"Horizontal\">" +
                "<Setter TargetName=\"PART_Track\" Property=\"IsDirectionReversed\" Value=\"False\"/>" +
                "<Setter TargetName=\"PART_DecreaseButton\" Property=\"Command\" Value=\"{x:Static ScrollBar.PageLeftCommand}\"/>" +
                "<Setter TargetName=\"PART_IncreaseButton\" Property=\"Command\" Value=\"{x:Static ScrollBar.PageRightCommand}\"/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>" +
                "</ControlTemplate.Resources>" +
                "<Grid Background=\"{TemplateBinding Background}\">" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
                "<Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
                "<ScrollContentPresenter x:Name=\"PART_ScrollContentPresenter\" Grid.Row=\"0\" Grid.Column=\"0\" Content=\"{TemplateBinding Content}\" ContentTemplate=\"{TemplateBinding ContentTemplate}\" Margin=\"{TemplateBinding Padding}\" CanContentScroll=\"{TemplateBinding CanContentScroll}\"/>" +
                "<ScrollBar x:Name=\"PART_VerticalScrollBar\" Grid.Row=\"0\" Grid.Column=\"1\" Width=\"10\" Style=\"{StaticResource RezeScrollBar}\" Orientation=\"Vertical\" Maximum=\"{TemplateBinding ScrollableHeight}\" ViewportSize=\"{TemplateBinding ViewportHeight}\" Value=\"{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}\" Visibility=\"{TemplateBinding ComputedVerticalScrollBarVisibility}\"/>" +
                "<ScrollBar x:Name=\"PART_HorizontalScrollBar\" Grid.Row=\"1\" Grid.Column=\"0\" Height=\"10\" Style=\"{StaticResource RezeScrollBar}\" Orientation=\"Horizontal\" Maximum=\"{TemplateBinding ScrollableWidth}\" ViewportSize=\"{TemplateBinding ViewportWidth}\" Value=\"{Binding HorizontalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}\" Visibility=\"{TemplateBinding ComputedHorizontalScrollBarVisibility}\"/>" +
                "</Grid>" +
                "</ControlTemplate>";

            var viewer = new ScrollViewer
            {
                Content = content,
                Background = Brushes.Transparent,
                Foreground = TextBrush,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(0),
                Focusable = true,
                IsTabStop = false,
                Template = (ControlTemplate)XamlReader.Parse(template)
            };
            ScrollViewer.SetCanContentScroll(viewer, false);
            ScrollViewer.SetPanningMode(viewer, PanningMode.VerticalOnly);
            return viewer;
        }
    }
}

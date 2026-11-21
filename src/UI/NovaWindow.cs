using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using RezeNova.Core;
using RezeNova.Proxy;
using RezeNova.Runtime;

namespace RezeNova.UI
{
    internal sealed partial class NovaWindow : Window
    {
        private static readonly Color Canvas = ColorFrom("080D18");
        private static readonly Color Rail = ColorFrom("0D1424");
        private static readonly Color Card = ColorFrom("121D31");
        private static readonly Color Field = ColorFrom("0B1424");
        private static readonly Color Line = ColorFrom("263B59");
        private static readonly Color Text = ColorFrom("F5F8FF");
        private static readonly Color Muted = ColorFrom("8FA3C1");
        private static readonly Color Cyan = ColorFrom("62E6FF");
        private static readonly Color Pink = ColorFrom("FF4BD8");
        private static readonly Color Green = ColorFrom("54F6B3");
        private static readonly Color Amber = ColorFrom("FFC857");
        private static readonly Brush CanvasBrush = Brush(Canvas);
        private static readonly Brush RailBrush = Brush(Rail);
        private static readonly Brush CardBrush = Brush(Card);
        private static readonly Brush FieldBrush = Brush(Field);
        private static readonly Brush LineBrush = Brush(Line);
        private static readonly Brush TextBrush = Brush(Text);
        private static readonly Brush MutedBrush = Brush(Muted);
        private static readonly Brush CyanBrush = Brush(Cyan);
        private static readonly Brush PinkBrush = Brush(Pink);
        private static readonly Brush GreenBrush = Brush(Green);
        private static readonly Brush AmberBrush = Brush(Amber);

        private readonly string root;
        private readonly string runtimeDirectory;
        private readonly string logDirectory;
        private readonly bool isAdministrator;
        private readonly WinDivertRuntimeService runtimeService;
        private readonly TelegramProxyService telegramService;
        private readonly NovaSettings settings;
        private readonly DispatcherTimer refreshTimer;
        private readonly Dictionary<NovaMode, Button> modeButtons = new Dictionary<NovaMode, Button>();
        private readonly Dictionary<string, Button> navigationButtons = new Dictionary<string, Button>();
        private ScrollViewer commandScroll;
        private FrameworkElement modesSection;
        private FrameworkElement servicesSection;
        private FrameworkElement diagnosticsSection;
        private FrameworkElement customLinksSection;
        private StackPanel customLinksPanel;
        private TextBox customLinkNameInput;
        private TextBox customLinkAddressInput;
        private Button sceneStartButton;
        private Button routeStartButton;
        private Button routeStopButton;
        private Button proxyStopButton;
        private NovaMode selectedMode;
        private readonly TextBlock routeValue = LabelText(18, Text);
        private readonly TextBlock routeDetail = LabelText(11, Muted);
        private readonly TextBlock commandRouteValue = LabelText(18, Text);
        private readonly TextBlock commandRouteDetail = LabelText(11, Muted);
        private readonly TextBlock telegramValue = LabelText(18, Text);
        private readonly TextBlock telegramDetail = LabelText(11, Muted);
        private readonly TextBlock manifestValue = LabelText(18, Text);
        private readonly TextBlock manifestDetail = LabelText(11, Muted);
        private readonly TextBlock networkValue = LabelText(18, Text);
        private readonly TextBlock networkDetail = LabelText(11, Muted);
        private readonly TextBlock selectedModeValue = LabelText(12, Cyan);
        private readonly TextBlock activity = LabelText(12, Muted);
        private readonly TextBlock footerValue = LabelText(11, Muted);
        private readonly TextBlock recoveryValue = LabelText(11, Muted);
        private readonly TextBlock diagnosticValue = LabelText(11, Muted);
        private Forms.NotifyIcon trayIcon;
        private Forms.ToolStripMenuItem trayStatus;
        private bool closing;
        private bool allowExit;
        private bool routeWasRunning;
        private bool recoveryInProgress;
        private DateTime lastRecovery = DateTime.MinValue;
        private DateTime routeStartedAt;
        private int recoveryCount;
        private HwndSource windowSource;
        private const int WmHotKey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const int HotKeyAuto = 1;
        private const int HotKeyFast = 2;
        private const int HotKeyStable = 3;
        private const int HotKeyWide = 4;
        private const int HotKeyDiagnostics = 5;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public NovaWindow(string applicationRoot)
        {
            root = Path.GetFullPath(applicationRoot);
            runtimeDirectory = Path.Combine(root, "runtime");
            logDirectory = Path.Combine(root, "logs");
            isAdministrator = IsAdministrator();
            settings = NovaSettings.Load();
            SyncCustomHostList();
            runtimeService = new WinDivertRuntimeService(runtimeDirectory, logDirectory);
            telegramService = new TelegramProxyService(root);
            selectedMode = settings.Mode;

            Title = "Reze Nova";
            Width = 1500;
            Height = 940;
            MinWidth = 1280;
            MinHeight = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = TextBrush;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 13;
            SnapsToDevicePixels = true;
            Content = BuildLayout();
            InitializeTray();

            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            refreshTimer.Tick += delegate { RefreshState(); };
            Loaded += delegate
            {
                refreshTimer.Start();
                RefreshState();
                WriteActivity("Nova готова. Контроль сети активен.");
                if (settings.StartWithWindows) WriteActivity("Автозапуск Windows включён.");
                if (settings.StartRouteOnLaunch && isAdministrator)
                    Dispatcher.BeginInvoke(new Action(StartRoute), DispatcherPriority.Background);
                if (settings.StartMinimized) HideToTray();
            };
            SourceInitialized += delegate { RegisterGlobalHotKeys(); };
            Closing += OnWindowClosing;
            Closed += delegate
            {
                closing = true;
                refreshTimer.Stop();
                UnregisterGlobalHotKeys();
                DisposeTray();
                StopServices();
            };
        }

        private UIElement BuildLayout()
        {
            var shell = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("0A0712"), ColorFrom("151127"), 25),
                BorderBrush = Brush(ColorFrom("49395F")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(26),
                Clip = new RectangleGeometry(new Rect(0, 0, 1460, 900), 26, 26),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 35, ShadowDepth = 0, Opacity = 0.45 }
            };
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            var topBar = BuildTopBar();
            topBar.MouseLeftButtonDown += delegate { DragMove(); };
            rootGrid.Children.Add(topBar);

            var studio = new Grid { Margin = new Thickness(18, 0, 18, 0) };
            studio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            studio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            studio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            Grid.SetRow(studio, 1);
            rootGrid.Children.Add(studio);
            studio.Children.Add(BuildNavigation());
            var center = BuildEditorialCenter();
            Grid.SetColumn(center, 1);
            studio.Children.Add(center);
            var command = BuildCommandColumn();
            Grid.SetColumn(command, 2);
            studio.Children.Add(command);

            var footer = new Border { BorderBrush = Brush(ColorFrom("2E2544")), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 8, 24, 0) };
            footer.Child = footerValue;
            Grid.SetRow(footer, 2);
            rootGrid.Children.Add(footer);
            shell.Child = rootGrid;
            return shell;
        }

        private UIElement BuildNavigation()
        {
            var panel = new Border { Background = Brush(ColorFrom("120D20")), BorderBrush = Brush(ColorFrom("2E2544")), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 22, 12, 18) };
            var stack = new StackPanel();
            panel.Child = stack;
            var logo = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(16), Background = PinkBrush, HorizontalAlignment = HorizontalAlignment.Left };
            logo.Child = new TextBlock { Text = "R", FontSize = 27, FontWeight = FontWeights.Black, Foreground = CanvasBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(logo);
            stack.Children.Add(new TextBlock { Text = "REZE NOVA", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = TextBrush, Margin = new Thickness(0, 13, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "ПРОСТРАНСТВО REZE", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = CyanBrush, Margin = new Thickness(1, 3, 0, 0) });
            stack.Children.Add(new Border { Height = 42, Background = Brushes.Transparent });
            stack.Children.Add(new TextBlock { Text = "СЕЙЧАС", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(4, 0, 0, 10) });
            stack.Children.Add(NavigationItem("◈", "Главная", "состояние маршрута", "главная", true));
            stack.Children.Add(NavigationItem("✦", "Режимы", "подбери свой ритм", "режимы", false));
            stack.Children.Add(NavigationItem("⌁", "Сервисы", "Discord · YouTube · TG", "сервисы", false));
            stack.Children.Add(NavigationItem("⌕", "Диагностика", "проверить соединение", "диагностика", false));
            stack.Children.Add(NavigationItem("＋", "Мои ссылки", "сайты и игровые входы", "ссылки", false));
            stack.Children.Add(new Border { Height = 38, Background = Brushes.Transparent });
            var note = new Border { Background = Brush(ColorFrom("23152A")), BorderBrush = Brush(ColorFrom("4B315C")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12) };
            var noteStack = new StackPanel();
            noteStack.Children.Add(new TextBlock { Text = "REZE / НАСТРОЕНИЕ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            noteStack.Children.Add(new TextBlock { Text = "Твой сигнал.\nТвой темп.", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 7, 0, 0) });
            noteStack.Children.Add(new TextBlock { Text = "Reze Nova держит маршрут тихо, пока ты занимаешься своим.", FontSize = 9, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            note.Child = noteStack;
            stack.Children.Add(note);
            var shortcuts = new StackPanel { Margin = new Thickness(4, 25, 0, 0) };
            shortcuts.Children.Add(new TextBlock { Text = "БЫСТРЫЙ ДОСТУП", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush });
            shortcuts.Children.Add(ShortcutLine("ALT + A", "Авто"));
            shortcuts.Children.Add(ShortcutLine("ALT + 1", "Быстрый"));
            shortcuts.Children.Add(ShortcutLine("ALT + 2", "Стабильный"));
            shortcuts.Children.Add(ShortcutLine("ALT + 3", "Широкий"));
            stack.Children.Add(shortcuts);
            return panel;
        }

        private UIElement NavigationItem(string glyph, string title, string detail, string target, bool selected)
        {
            var border = new Button { Background = selected ? Brush(ColorFrom("291936")) : Brushes.Transparent, BorderBrush = selected ? Brush(ColorFrom("70436D")) : Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 7, 10), Margin = new Thickness(0, 3, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = target };
            ApplyButtonTemplate(border, selected ? Brush(ColorFrom("291936")) : Brushes.Transparent, selected ? Brush(ColorFrom("70436D")) : Brushes.Transparent, TextBrush, 12);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = glyph, FontSize = 16, Foreground = selected ? PinkBrush : MutedBrush, VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel();
            copy.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            copy.Children.Add(new TextBlock { Text = detail, FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            border.Content = grid;
            border.Click += delegate { NavigateTo(target); };
            navigationButtons[target] = border;
            return border;
        }

        private void NavigateTo(string target)
        {
            if (target == "главная")
            {
                commandScroll.ScrollToTop();
                return;
            }
            if (target == "ссылки")
            {
                ShowCustomLinksWindow();
                return;
            }
            FrameworkElement section = target == "режимы" ? modesSection : target == "сервисы" ? servicesSection : target == "диагностика" ? diagnosticsSection : customLinksSection;
            if (section == null) return;
            Point point = section.TransformToAncestor(commandScroll).Transform(new Point(0, 0));
            commandScroll.ScrollToVerticalOffset(Math.Max(0, commandScroll.VerticalOffset + point.Y - 12));
        }

        private UIElement BuildEditorialCenter()
        {
            var center = new Grid { Margin = new Thickness(18, 0, 18, 0) };
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(205) });
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var headingCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headingCopy.Children.Add(new TextBlock { Text = "ЦЕНТР УПРАВЛЕНИЯ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            headingCopy.Children.Add(new TextBlock { Text = "Режим Reze. Чистый сигнал.", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 2, 0, 0) });
            heading.Children.Add(headingCopy);
            var clock = new TextBlock { Text = "REZE NOVA  /  " + DateTime.Now.ToString("dd.MM.yyyy"), FontSize = 9, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(clock, 1);
            heading.Children.Add(clock);
            center.Children.Add(heading);

            var feature = BuildFeatureScene();
            Grid.SetRow(feature, 1);
            center.Children.Add(feature);
            var gallery = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddGalleryCard(gallery, 0, "reze-set-02.jpg", "МЯГЧЕ, ЧЕМ ШУМ", "Твой маршрут уже рядом.");
            AddGalleryCard(gallery, 1, "reze-set-03.jpg", "СМОТРИ ВПЕРЁД", "Всё под контролем.");
            Grid.SetRow(gallery, 2);
            center.Children.Add(gallery);
            return center;
        }

        private UIElement BuildFeatureScene()
        {
            var card = new Border { Background = Brush(ColorFrom("171126")), BorderBrush = Brush(ColorFrom("503A64")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Clip = new RectangleGeometry(new Rect(0, 0, 820, 570), 22, 22), Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 28, ShadowDepth = 0, Opacity = 0.34 } };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
            var artFrame = new Border { Background = Brush(ColorFrom("211A30")), Margin = new Thickness(18), CornerRadius = new CornerRadius(17), Clip = new RectangleGeometry(new Rect(0, 0, 420, 480), 17, 17) };
            ImageSource mainImage = LoadImage("reze-set-01.jpg");
            if (mainImage != null) artFrame.Background = new ImageBrush(mainImage) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            Grid.SetColumn(artFrame, 0);
            grid.Children.Add(artFrame);
            var copy = new StackPanel { Margin = new Thickness(12, 40, 30, 24), VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = "01  /  REZE NOVA", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = CyanBrush });
            copy.Children.Add(new TextBlock { Text = "Твоя сеть\nв своём ритме.", FontSize = 32, LineHeight = 36, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 12, 0, 0) });
            copy.Children.Add(new TextBlock { Text = "Reze Nova собирает Discord, YouTube и Telegram\nв одно спокойное пространство без лишнего шума.", FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 13, 18, 0) });
            var status = new Border { Background = Brush(ColorFrom("3A1A38")), BorderBrush = Brush(ColorFrom("805C456F")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(13), Margin = new Thickness(0, 19, 0, 0) };
            var statusStack = new StackPanel();
            statusStack.Children.Add(new TextBlock { Text = "СОСТОЯНИЕ СЕЙЧАС", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            statusStack.Children.Add(routeValue);
            statusStack.Children.Add(routeDetail);
            status.Child = statusStack;
            copy.Children.Add(status);
            var primary = ActionButton("ЗАПУСТИТЬ МАРШРУТ", PinkBrush, delegate { StartRoute(); });
            primary.Height = 43;
            primary.Margin = new Thickness(0, 12, 0, 0);
            sceneStartButton = primary;
            copy.Children.Add(primary);
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            card.Child = grid;
            return card;
        }

        private void AddGalleryCard(Grid parent, int column, string file, string title, string subtitle)
        {
            var card = new Border { Background = Brush(ColorFrom("171126")), BorderBrush = Brush(ColorFrom("3B2C51")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(column == 0 ? 0 : 7, 0, column == 1 ? 0 : 7, 0), Clip = new RectangleGeometry(new Rect(0, 0, 420, 190), 16, 16) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var imageBox = new Border { Background = Brush(ColorFrom("241B31")), Margin = new Thickness(8), CornerRadius = new CornerRadius(11), Clip = new RectangleGeometry(new Rect(0, 0, 156, 174), 11, 11) };
            ImageSource source = LoadImage(file);
            if (source != null) imageBox.Background = new ImageBrush(source) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            grid.Children.Add(imageBox);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 14, 0) };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            copy.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            card.Child = grid;
            Grid.SetColumn(card, column);
            parent.Children.Add(card);
        }

        private UIElement BuildCommandColumn()
        {
            var card = new Border { Background = Brush(ColorFrom("120D20")), BorderBrush = Brush(ColorFrom("382B50")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 18, 16, 14), Margin = new Thickness(0) };
            var scroll = ThemedScroll(null);
            commandScroll = scroll;
            var stack = new StackPanel();
            scroll.Content = stack;
            card.Child = scroll;
            stack.Children.Add(new TextBlock { Text = "ПУЛЬТ", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            selectedModeValue.Text = "Активный режим Reze · " + ModeName(selectedMode);
            stack.Children.Add(selectedModeValue);
            stack.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
            var routeCard = new Border { Background = Brush(ColorFrom("1D1831")), BorderBrush = Brush(ColorFrom("46345E")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12) };
            var routeStack = new StackPanel();
            routeStack.Children.Add(new TextBlock { Text = "МАРШРУТ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = CyanBrush });
            routeStack.Children.Add(commandRouteValue);
            routeStack.Children.Add(commandRouteDetail);
            routeCard.Child = routeStack;
            stack.Children.Add(routeCard);
            var start = ActionButton("ЗАПУСТИТЬ", PinkBrush, delegate { StartRoute(); });
            start.Height = 42;
            start.Margin = new Thickness(0, 10, 0, 0);
            routeStartButton = start;
            stack.Children.Add(start);
            routeStopButton = ActionButton("ОСТАНОВИТЬ", FieldBrush, delegate { StopRoute(); });
            stack.Children.Add(routeStopButton);
            var profileLabel = new TextBlock { Text = "ПРОФИЛЬ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 22, 0, 10) };
            modesSection = profileLabel;
            stack.Children.Add(profileLabel);
            var modes = new StackPanel();
            AddCompactMode(modes, NovaMode.Auto, "A", "Авто", "баланс", "Alt+A");
            AddCompactMode(modes, NovaMode.Fast, "1", "Быстрый", "скорость", "Alt+1");
            AddCompactMode(modes, NovaMode.Stable, "2", "Стабильный", "запас", "Alt+2");
            AddCompactMode(modes, NovaMode.Wide, "3", "Широкий", "охват", "Alt+3");
            stack.Children.Add(modes);
            var servicesLabel = new TextBlock { Text = "СЕРВИСЫ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 22, 0, 10) };
            servicesSection = servicesLabel;
            stack.Children.Add(servicesLabel);
            stack.Children.Add(ServiceLine("DISCORD", "Открыть приложение", CyanBrush, delegate { OpenDiscord(); }));
            stack.Children.Add(ServiceLine("YOUTUBE", "Открыть YouTube", PinkBrush, delegate { OpenYouTube(); }));
            stack.Children.Add(ServiceLine("TELEGRAM", "Подключить прокси", AmberBrush, delegate { StartTelegram(true); }));
            proxyStopButton = ServiceLine("ПРОКСИ", "Остановить Telegram", AmberBrush, delegate { StopTelegram(); });
            stack.Children.Add(proxyStopButton);
            var diagnosticsLabel = new TextBlock { Text = "ИНСТРУМЕНТЫ REZE", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 22, 0, 10) };
            diagnosticsSection = diagnosticsLabel;
            stack.Children.Add(diagnosticsLabel);
            stack.Children.Add(ActionButton("ПРОВЕРИТЬ СЕТЬ", FieldBrush, delegate { RunNetworkProbe(); }));
            stack.Children.Add(ActionButton("ДИАГНОСТИКА DISCORD", FieldBrush, delegate { RunDiscordDiagnostics(); }));
            stack.Children.Add(ActionButton("СКОПИРОВАТЬ ОТЧЁТ", FieldBrush, delegate { CopyDiagnostics(); }));
            stack.Children.Add(ActionButton("ОТКРЫТЬ ЖУРНАЛЫ", FieldBrush, delegate { OpenFolder(logDirectory); }));
            var linksLabel = new TextBlock { Text = "МОИ ССЫЛКИ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 22, 0, 10) };
            customLinksSection = linksLabel;
            stack.Children.Add(linksLabel);
            stack.Children.Add(ActionButton("ОТКРЫТЬ ОКНО ССЫЛОК", FieldBrush, delegate { ShowCustomLinksWindow(); }));
            stack.Children.Add(ActionButton("ОТКРЫТЬ ИНСТРУКЦИЮ", FieldBrush, delegate { ShowGuide(); }));
            var activityCard = new Border { Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0), MinHeight = 60 };
            activityCard.Child = activity;
            stack.Children.Add(activityCard);
            return card;
        }

        private void AddCompactMode(StackPanel parent, NovaMode mode, string key, string title, string description, string hint)
        {
            var active = mode == selectedMode;
            var button = new Button { Height = 58, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(11, 7, 11, 7), Background = active ? Brush(ColorFrom("34203A")) : FieldBrush, BorderBrush = active ? PinkBrush : LineBrush, BorderThickness = new Thickness(1), Foreground = TextBrush, HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = mode };
            ApplyButtonTemplate(button, active ? Brush(ColorFrom("34203A")) : FieldBrush, active ? PinkBrush : LineBrush, TextBrush, 10);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var badge = new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(8), Background = active ? PinkBrush : Brush(ColorFrom("241B31")), VerticalAlignment = VerticalAlignment.Center };
            badge.Child = new TextBlock { Text = key, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = active ? CanvasBrush : CyanBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(badge);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            copy.Children.Add(new TextBlock { Text = description, FontSize = 10, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            var hintText = new TextBlock { Text = hint, FontSize = 8, Foreground = active ? PinkBrush : MutedBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hintText, 2);
            grid.Children.Add(hintText);
            button.Content = grid;
            button.Click += delegate { SelectMode(mode, true); };
            modeButtons[mode] = button;
            parent.Children.Add(button);
        }

        private Button ServiceLine(string name, string detail, Brush accent, Action action)
        {
            var button = new Button { Height = 62, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(11, 7, 11, 7), Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ApplyButtonTemplate(button, FieldBrush, LineBrush, TextBrush, 10);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = accent, VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            copy.Children.Add(new TextBlock { Text = detail, FontSize = 10, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            button.Content = grid;
            button.Click += delegate { try { action(); } catch (Exception error) { ShowError(error.Message); } };
            return button;
        }

        private void ApplyButtonTemplate(Button button, Brush background, Brush border, Brush foreground, double radius)
        {
            button.Foreground = foreground;
            var template = new ControlTemplate(typeof(Button));
            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "Chrome";
            chrome.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            chrome.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            chrome.SetValue(Border.PaddingProperty, new Thickness(0));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new System.Windows.Data.Binding("ContentTemplate") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            template.VisualTree = chrome;
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.SetValue(Control.BackgroundProperty, Brush(ColorFrom("3A2143")));
            hover.SetValue(Control.BorderBrushProperty, Brush(ColorFrom("8C5D8F")));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.SetValue(UIElement.OpacityProperty, 0.82);
            template.Triggers.Add(pressed);
            button.Template = template;
        }

        private UIElement BuildCinematicHero()
        {
            var hero = new Border { Background = CardBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 0, 12, 0), Clip = new RectangleGeometry(new Rect(0, 0, 850, 700), 22, 22) };
            var grid = new Grid();
            ImageSource source = LoadImage("hero.jpg");
            if (source != null) grid.Children.Add(new Border { Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center } });
            grid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(238, 7, 12, 25), Color.FromArgb(15, 7, 12, 25), 0) });
            grid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(245, 7, 12, 25), Color.FromArgb(0, 7, 12, 25), 90) });

            var top = new Grid { Margin = new Thickness(28, 26, 28, 0), VerticalAlignment = VerticalAlignment.Top };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            top.Children.Add(new TextBlock { Text = "REZE NOVA  /  01", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            var live = new Border { Background = Brush(ColorFrom("7A0A1420")), BorderBrush = Brush(ColorFrom("8A54F6B3")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Right };
            var liveStack = new StackPanel { Orientation = Orientation.Horizontal };
            liveStack.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = GreenBrush, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            liveStack.Children.Add(new TextBlock { Text = "СИСТЕМА ГОТОВА", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = GreenBrush });
            live.Child = liveStack;
            Grid.SetColumn(live, 1);
            top.Children.Add(live);
            grid.Children.Add(top);

            var copy = new StackPanel { Margin = new Thickness(30, 0, 180, 34), VerticalAlignment = VerticalAlignment.Bottom };
            copy.Children.Add(new TextBlock { Text = "ТИШИНА В ЭФИРЕ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            copy.Children.Add(new TextBlock { Text = "Связь,\nкоторая чувствуется.", FontSize = 42, LineHeight = 45, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 8, 0, 0) });
            copy.Children.Add(new TextBlock { Text = "Reze Nova держит Discord, YouTube и Telegram\nв одном красивом и спокойном ритме.", FontSize = 14, Foreground = TextBrush, Opacity = 0.86, Margin = new Thickness(1, 13, 0, 0) });
            var route = new Border { Background = Brush(ColorFrom("CC0A1424")), BorderBrush = Brush(ColorFrom("805DE7FF")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 20, 0, 0) };
            var routeGrid = new Grid();
            routeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            routeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            var routeCopy = new StackPanel();
            routeCopy.Children.Add(new TextBlock { Text = "СОСТОЯНИЕ МАРШРУТА", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = CyanBrush });
            routeCopy.Children.Add(routeValue);
            routeCopy.Children.Add(routeDetail);
            routeGrid.Children.Add(routeCopy);
            var primary = ActionButton("ЗАПУСТИТЬ", PinkBrush, delegate { StartRoute(); });
            primary.Height = 42;
            primary.FontSize = 11;
            Grid.SetColumn(primary, 1);
            routeGrid.Children.Add(primary);
            route.Child = routeGrid;
            copy.Children.Add(route);
            grid.Children.Add(copy);

            var orb = new Border { Width = 125, Height = 125, CornerRadius = new CornerRadius(63), BorderBrush = Brush(ColorFrom("B05DE7FF")), BorderThickness = new Thickness(1), Background = new RadialGradientBrush(Color.FromArgb(170, 93, 231, 255), Color.FromArgb(0, 93, 231, 255)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 58, 0), RenderTransform = new TranslateTransform() };
            orb.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-17, 17, new Duration(TimeSpan.FromSeconds(4.6))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            grid.Children.Add(orb);
            hero.Child = grid;
            return hero;
        }

        private UIElement BuildSideDeck()
        {
            var card = CardPanel();
            card.Margin = new Thickness(0);
            var scroll = ThemedScroll(null);
            var stack = new StackPanel();
            scroll.Content = stack;
            card.Child = scroll;
            stack.Children.Add(new TextBlock { Text = "ПУЛЬС СЕТИ", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            stack.Children.Add(new TextBlock { Text = "Три сервиса под одним маршрутом.", FontSize = 11, Foreground = MutedBrush, Margin = new Thickness(0, 5, 0, 18) });
            stack.Children.Add(BuildServiceCard("DISCORD", "Шлюз, голос и демонстрации", routeValue, CyanBrush, delegate { OpenDiscord(); }));
            stack.Children.Add(BuildServiceCard("YOUTUBE", "Видео и потоковая доставка", networkValue, PinkBrush, delegate { OpenYouTube(); }));
            stack.Children.Add(BuildServiceCard("TELEGRAM", "Локальный защищённый прокси", telegramValue, AmberBrush, delegate { StartTelegram(true); }));
            stack.Children.Add(new TextBlock { Text = "АКТИВНЫЙ ПРОФИЛЬ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 20, 0, 8) });
            selectedModeValue.Text = "Активный режим Reze · " + ModeName(selectedMode);
            stack.Children.Add(selectedModeValue);
            var modes = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            modes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            modes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddModeCard(modes, NovaMode.Auto, "A", "Авто", "баланс", "Alt+A", 0, 0);
            AddModeCard(modes, NovaMode.Fast, "1", "Быстрый", "скорость", "Alt+1", 1, 0);
            AddModeCard(modes, NovaMode.Stable, "2", "Стабильный", "запас", "Alt+2", 0, 1);
            AddModeCard(modes, NovaMode.Wide, "3", "Широкий", "охват", "Alt+3", 1, 1);
            stack.Children.Add(modes);
            stack.Children.Add(new TextBlock { Text = "БЫСТРЫЕ ДЕЙСТВИЯ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 20, 0, 8) });
            stack.Children.Add(ActionButton("ДИАГНОСТИКА DISCORD", FieldBrush, delegate { RunDiscordDiagnostics(); }));
            stack.Children.Add(ActionButton("ОТКРЫТЬ ИНСТРУКЦИЮ", FieldBrush, delegate { ShowGuide(); }));
            var activityCard = new Border { Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11), Margin = new Thickness(0, 14, 0, 0), MinHeight = 64 };
            activityCard.Child = activity;
            stack.Children.Add(activityCard);
            return card;
        }

        private UIElement BuildServiceCard(string name, string description, TextBlock value, Brush accent, Action action)
        {
            var button = new Button { Height = 76, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(12), Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ApplyButtonTemplate(button, FieldBrush, LineBrush, TextBrush, 12);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) });
            var copy = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            copy.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = TextBrush });
            copy.Children.Add(new TextBlock { Text = description, FontSize = 9, Foreground = MutedBrush, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            var status = new TextBlock { Text = "ГОТОВ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(status, 2);
            grid.Children.Add(status);
            button.Content = grid;
            button.Click += delegate { action(); };
            return button;
        }

        private UIElement BuildTopBar()
        {
            var bar = new Grid { Margin = new Thickness(24, 0, 20, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Background = PinkBrush };
            mark.Child = new TextBlock { Text = "◇", FontSize = 21, Foreground = CanvasBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(mark);
            var name = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            name.Children.Add(new TextBlock { Text = "REZE NOVA", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = TextBrush });
            name.Children.Add(new TextBlock { Text = "СЕТЕВОЙ ЦЕНТР", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = CyanBrush });
            brand.Children.Add(name);
            bar.Children.Add(brand);
            var live = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            live.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = GreenBrush, Margin = new Thickness(0, 0, 8, 0) });
            live.Children.Add(new TextBlock { Text = isAdministrator ? "ПРАВА АДМИНИСТРАТОРА" : "ОБЫЧНЫЙ РЕЖИМ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(live, 1);
            bar.Children.Add(live);
            var close = new Button { Content = "×", Width = 34, Height = 30, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = MutedBrush, FontSize = 22, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += delegate { HideToTray(); };
            Grid.SetColumn(close, 2);
            bar.Children.Add(close);
            return bar;
        }

        private UIElement BuildDashboardMain()
        {
            var stack = new StackPanel();
            stack.Children.Add(BuildOverview());
            stack.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
            stack.Children.Add(BuildModes());
            return stack;
        }

        private UIElement BuildRail()
        {
            var panel = new Border { Background = RailBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 22, 0, 18) };
            var stack = new StackPanel();
            panel.Child = stack;
            stack.Children.Add(new TextBlock { Text = "РАЗДЕЛЫ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(13, 0, 0, 14) });
            stack.Children.Add(RailItem("◈", "Обзор", "состояние сети", true));
            stack.Children.Add(RailItem("⌁", "Режимы", "четыре профиля", false));
            stack.Children.Add(RailItem("⌕", "Диагностика", "живые проверки", false));
            stack.Children.Add(RailItem("▣", "Активность", "история маршрута", false));
            stack.Children.Add(new Border { Height = 24, Background = Brushes.Transparent });
            stack.Children.Add(new TextBlock { Text = "СЕРВИСЫ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(13, 0, 0, 8) });
            stack.Children.Add(ServicePill("DISCORD", "шлюз · голос · демонстрации", CyanBrush));
            stack.Children.Add(ServicePill("YOUTUBE", "видеопоток", PinkBrush));
            stack.Children.Add(ServicePill("TELEGRAM", "локальный прокси", AmberBrush));
            var bottom = new StackPanel { Margin = new Thickness(13, 28, 13, 0) };
            bottom.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(0, 0, 0, 14) });
            bottom.Children.Add(new TextBlock { Text = "ГОРЯЧИЕ КЛАВИШИ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush });
            bottom.Children.Add(ShortcutLine("ALT + A", "Авто"));
            bottom.Children.Add(ShortcutLine("ALT + 1", "Быстрый"));
            bottom.Children.Add(ShortcutLine("ALT + 2", "Стабильный"));
            bottom.Children.Add(ShortcutLine("ALT + 3", "Широкий"));
            bottom.Children.Add(new Border { Height = 14, Background = Brushes.Transparent });
            bottom.Children.Add(ActionButton("ОТКРЫТЬ ИНСТРУКЦИЮ", FieldBrush, delegate { ShowGuide(); }));
            stack.Children.Add(bottom);
            return panel;
        }

        private UIElement ServicePill(string name, string detail, Brush accent)
        {
            var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(13, 9, 8, 9), Margin = new Thickness(0, 2, 0, 0) };
            var stack = new StackPanel();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = accent, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = TextBrush });
            stack.Children.Add(line);
            stack.Children.Add(new TextBlock { Text = detail, FontSize = 9, Foreground = MutedBrush, Margin = new Thickness(14, 3, 0, 0) });
            border.Child = stack;
            return border;
        }

        private UIElement BuildHeader()
        {
            var card = new Border { Background = CardBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Clip = new RectangleGeometry(new Rect(0, 0, 1100, 270), 18, 18) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
            var image = LoadImageAny("hero-generated.png", "hero.jpg");
            if (image != null)
            {
                var art = new Border { Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center } };
                Grid.SetColumn(art, 1);
                grid.Children.Add(art);
            }
            var wash = new Border { Background = new LinearGradientBrush(Color.FromArgb(250, 12, 18, 31), Color.FromArgb(15, 12, 18, 31), 0) };
            Grid.SetColumnSpan(wash, 2);
            grid.Children.Add(wash);
            var copy = new StackPanel { Margin = new Thickness(28, 28, 20, 24), VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = "REZE NOVA / ЦЕНТР СЕТИ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            copy.Children.Add(new TextBlock { Text = "Твоя сеть.\nТвой контроль.", FontSize = 34, LineHeight = 39, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 9, 0, 0) });
            copy.Children.Add(new TextBlock { Text = "Discord, YouTube и Telegram в одном красивом и спокойном центре управления.", FontSize = 13, Foreground = TextBrush, Opacity = 0.86, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 12, 80, 0) });
            var state = new Border { Background = Brush(ColorFrom("B20E1828")), BorderBrush = Brush(ColorFrom("5262E6FF")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 8, 13, 8), Margin = new Thickness(0, 18, 80, 0) };
            var stateGrid = new Grid();
            stateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            stateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stateGrid.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = GreenBrush, VerticalAlignment = VerticalAlignment.Center });
            var stateText = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            stateText.Children.Add(new TextBlock { Text = "СИСТЕМА ГОТОВА К РАБОТЕ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = GreenBrush });
            stateText.Children.Add(new TextBlock { Text = "Автовосстановление и диагностика включены", FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(stateText, 1);
            stateGrid.Children.Add(stateText);
            state.Child = stateGrid;
            copy.Children.Add(state);
            grid.Children.Add(copy);
            var floating = new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(46), BorderBrush = Brush(ColorFrom("AA62E6FF")), BorderThickness = new Thickness(1), Background = new RadialGradientBrush(Color.FromArgb(150, 98, 230, 255), Color.FromArgb(0, 98, 230, 255)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 28, 24), RenderTransform = new TranslateTransform() };
            var orbit = new DoubleAnimation(-10, 10, new Duration(TimeSpan.FromSeconds(3.8))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            floating.RenderTransform.BeginAnimation(TranslateTransform.YProperty, orbit);
            grid.Children.Add(floating);
            card.Child = grid;
            return card;
        }

        private UIElement BuildOverview()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddOverviewCard(grid, 0, "МАРШРУТ", routeValue, routeDetail, CyanBrush);
            AddOverviewCard(grid, 1, "TELEGRAM", telegramValue, telegramDetail, PinkBrush);
            AddOverviewCard(grid, 2, "ЯДРО", manifestValue, manifestDetail, GreenBrush);
            return grid;
        }

        private UIElement BuildModes()
        {
            var card = CardPanel();
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(156) });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "ВЫБЕРИ СВОЙ РЕЖИМ", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            heading.Children.Add(new TextBlock { Text = "Четыре профиля под разное состояние сети.", FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(heading);
            var modes = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            modes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            modes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddModeCard(modes, NovaMode.Auto, "A", "Авто", "сбалансированный маршрут", "Alt + A", 0, 0);
            AddModeCard(modes, NovaMode.Fast, "1", "Быстрый", "минимальная задержка", "Alt + 1", 1, 0);
            AddModeCard(modes, NovaMode.Stable, "2", "Стабильный", "запас устойчивости", "Alt + 2", 0, 1);
            AddModeCard(modes, NovaMode.Wide, "3", "Широкий", "максимальный охват", "Alt + 3", 1, 1);
            Grid.SetRow(modes, 1);
            grid.Children.Add(modes);
            var art = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            art.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
            art.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.82, GridUnitType.Star) });
            AddArtCard(art, 0, "hero.jpg", "СИГНАЛ ОСТАЁТСЯ ЧИСТЫМ", "Спокойный маршрут для всей системы.");
            AddArtCard(art, 1, "portrait-garden.jpg", "ТИХО ОНЛАЙН", "Без лишнего шума. Только связь.");
            Grid.SetRow(art, 2);
            grid.Children.Add(art);
            card.Child = grid;
            return card;
        }

        private UIElement BuildControlPanel()
        {
            var card = CardPanel();
            var scroll = ThemedScroll(null);
            var stack = new StackPanel();
            scroll.Content = stack;
            card.Child = scroll;
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            heading.Children.Add(new TextBlock { Text = "ПУЛЬТ УПРАВЛЕНИЯ", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            selectedModeValue.Text = "Активный режим Reze · " + ModeName(selectedMode);
            heading.Children.Add(selectedModeValue);
            stack.Children.Add(heading);
            var primary = ActionButton("ЗАПУСТИТЬ МАРШРУТ", PinkBrush, delegate { StartRoute(); });
            primary.Height = 46;
            primary.FontSize = 12;
            stack.Children.Add(primary);
            stack.Children.Add(ActionButton("ОСТАНОВИТЬ МАРШРУТ", FieldBrush, delegate { StopRoute(); }));
            stack.Children.Add(new Border { Height = 10, Background = Brushes.Transparent });
            stack.Children.Add(ActionButton("ОТКРЫТЬ DISCORD", FieldBrush, delegate { OpenDiscord(); }));
            stack.Children.Add(ActionButton("ПЕРЕЗАПУСТИТЬ DISCORD", FieldBrush, delegate { RestartDiscord(); }));
            stack.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(0, 14, 0, 14) });
            var telegram = ActionButton("ПОДКЛЮЧИТЬ TELEGRAM", CyanBrush, delegate { StartTelegram(true); });
            telegram.Height = 38;
            stack.Children.Add(telegram);
            stack.Children.Add(ActionButton("ОСТАНОВИТЬ TELEGRAM", FieldBrush, delegate { StopTelegram(); }));
            stack.Children.Add(new TextBlock { Text = "ИНСТРУМЕНТЫ", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(0, 18, 0, 5) });
            stack.Children.Add(ActionButton("ПРОВЕРИТЬ СЕТЬ", FieldBrush, delegate { RunNetworkProbe(); }));
            stack.Children.Add(ActionButton("ДИАГНОСТИКА DISCORD", FieldBrush, delegate { RunDiscordDiagnostics(); }));
            stack.Children.Add(ActionButton("СКОПИРОВАТЬ ОТЧЁТ", FieldBrush, delegate { CopyDiagnostics(); }));
            stack.Children.Add(ActionButton("ОТКРЫТЬ ЛОГИ", FieldBrush, delegate { OpenFolder(logDirectory); }));
            var options = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            options.Children.Add(OptionCheck("Запускать Nova вместе с Windows", settings.StartWithWindows, delegate(bool value) { settings.StartWithWindows = value; WindowsStartup.SetEnabled(value, Process.GetCurrentProcess().MainModule.FileName); settings.Save(); WriteActivity(value ? "Автозапуск Windows включён." : "Автозапуск Windows выключен."); }));
            options.Children.Add(OptionCheck("Запускать маршрут при старте Nova", settings.StartRouteOnLaunch, delegate(bool value) { settings.StartRouteOnLaunch = value; settings.Save(); }));
            options.Children.Add(OptionCheck("Сразу открывать Nova в трее", settings.StartMinimized, delegate(bool value) { settings.StartMinimized = value; settings.Save(); }));
            stack.Children.Add(options);
            var activityCard = new Border { Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(11), Margin = new Thickness(0, 14, 0, 0), MinHeight = 76 };
            activityCard.Child = activity;
            stack.Children.Add(activityCard);
            var diagnosticCard = new Border { Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0), MinHeight = 62 };
            diagnosticCard.Child = diagnosticValue;
            stack.Children.Add(diagnosticCard);
            return card;
        }

        private CheckBox OptionCheck(string text, bool value, Action<bool> changed)
        {
            var check = new CheckBox { Content = text, IsChecked = value, Foreground = MutedBrush, FontSize = 10, Margin = new Thickness(0, 3, 0, 0) };
            check.Checked += delegate { changed(true); };
            check.Unchecked += delegate { changed(false); };
            return check;
        }

        private void AddPortrait(Grid parent, int column, string file, string caption)
        {
            ImageSource source = LoadImage(file);
            var border = new Border { Height = 60, CornerRadius = new CornerRadius(8), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 1 ? 0 : 4, 0), BorderBrush = LineBrush, BorderThickness = new Thickness(1), Background = FieldBrush, Clip = new RectangleGeometry(new Rect(0, 0, 160, 60), 8, 8) };
            if (source != null) border.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill, Opacity = 0.56 };
            var shade = new Border { Background = new LinearGradientBrush(Color.FromArgb(20, 8, 13, 24), Color.FromArgb(230, 8, 13, 24), 0) };
            shade.Child = new TextBlock { Text = caption, Foreground = TextBrush, FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9, 0, 0, 7) };
            border.Child = shade;
            Grid.SetColumn(border, column);
            parent.Children.Add(border);
        }

        private void AddModeCard(Grid parent, NovaMode mode, string key, string title, string description, string hint, int column, int row)
        {
            var active = mode == selectedMode;
            var button = new Button
            {
                Height = 78,
                Margin = new Thickness(column == 0 ? 0 : 5, row == 0 ? 0 : 5, 5, 5),
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = active ? Brush(ColorFrom("3A1D42")) : FieldBrush,
                BorderBrush = active ? PinkBrush : LineBrush,
                BorderThickness = new Thickness(active ? 1.5 : 1),
                Foreground = TextBrush,
                Tag = mode
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = active ? PinkBrush : CardBrush, VerticalAlignment = VerticalAlignment.Center };
            badge.Child = new TextBlock { Text = key, FontSize = 12, Foreground = active ? CanvasBrush : CyanBrush, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            layout.Children.Add(badge);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            copy.Children.Add(new TextBlock { Text = description, FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(copy, 1);
            layout.Children.Add(copy);
            var keyText = new TextBlock { Text = hint, FontSize = 9, Foreground = active ? PinkBrush : MutedBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(keyText, 2);
            layout.Children.Add(keyText);
            button.Content = layout;
            button.Click += delegate { SelectMode(mode, true); };
            modeButtons[mode] = button;
            Grid.SetColumn(button, column);
            Grid.SetRow(button, row);
            parent.Children.Add(button);
        }

        private void AddArtCard(Grid parent, int column, string file, string title, string subtitle)
        {
            ImageSource source = LoadImage(file);
            var card = new Border
            {
                Height = 142,
                Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 1 ? 0 : 6, 0),
                CornerRadius = new CornerRadius(14),
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(1),
                Background = FieldBrush,
                Clip = new RectangleGeometry(new Rect(0, 0, 360, 142), 14, 14)
            };
            var art = new Grid();
            if (source != null) art.Children.Add(new Border { Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center } });
            art.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(15, 8, 13, 24), Color.FromArgb(238, 8, 13, 24), 90) });
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 13) };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TextBrush });
            copy.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 4, 0, 0) });
            art.Children.Add(copy);
            card.Child = art;
            Grid.SetColumn(card, column);
            parent.Children.Add(card);
        }

        private void AddOverviewCard(Grid grid, int column, string caption, TextBlock value, TextBlock detail, Brush accent)
        {
            var card = new Border { Background = CardBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(16), Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 2 ? 0 : 5, 0) };
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.22 };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = caption, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = accent });
            stack.Children.Add(value);
            stack.Children.Add(detail);
            card.Child = stack;
            Grid.SetColumn(card, column);
            grid.Children.Add(card);
        }

        private Border CardPanel()
        {
            var card = new Border { Background = CardBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 0, 8, 0) };
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.28 };
            return card;
        }

        private UIElement CardHeading(string title, string subtitle)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = MutedBrush });
            stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = MutedBrush, Margin = new Thickness(0, 3, 0, 0) });
            return stack;
        }

        private UIElement BuildCustomLinksPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 10) };
            customLinksPanel = panel;
            var hint = new TextBlock
            {
                Text = "Добавь сайт или игровой сервер. Для сайта укажи https://..., для Steam — steam://connect/адрес:порт, для сервера можно просто написать адрес:порт.",
                FontSize = 14,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(hint);
            panel.Children.Add(new TextBlock { Text = "НАЗВАНИЕ                                      АДРЕС САЙТА ИЛИ СЕРВЕРА", FontSize = 10, Foreground = MutedBrush, Margin = new Thickness(0, 8, 0, 6) });
            var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            customLinkNameInput = new TextBox { Text = "", Height = 46, FontSize = 14, Foreground = TextBrush, Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), ToolTip = "Название" };
            customLinkAddressInput = new TextBox { Text = "", Height = 46, FontSize = 14, Foreground = TextBrush, Background = FieldBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), ToolTip = "https://... или steam://..." };
            form.Children.Add(customLinkNameInput);
            Grid.SetColumn(customLinkAddressInput, 1);
            form.Children.Add(customLinkAddressInput);
            var add = ActionButton("ДОБАВИТЬ", PinkBrush, AddCustomLink);
            add.Height = 46;
            add.FontSize = 11;
            add.Margin = new Thickness(5, 0, 0, 0);
            Grid.SetColumn(add, 2);
            form.Children.Add(add);
            panel.Children.Add(form);
            foreach (NovaLink link in settings.CustomLinks) AddCustomLinkRow(panel, link);
            return panel;
        }

        private void ShowCustomLinksWindow()
        {
            var dialog = new Window
            {
                Owner = this,
                Title = "Мои ссылки · Reze Nova",
                Width = 920,
                Height = 760,
                MinWidth = 720,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                Foreground = TextBrush,
                FontFamily = FontFamily,
                FontSize = 14
            };
            var shell = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("0C0818"), ColorFrom("171129"), 25),
                BorderBrush = Brush(ColorFrom("664A76")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(28),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.48 }
            };
            dialog.SizeChanged += delegate { shell.Clip = new RectangleGeometry(new Rect(0, 0, dialog.ActualWidth, dialog.ActualHeight), 22, 22); };
            dialog.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { dialog.Close(); args.Handled = true; }
            };
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "МОИ ССЫЛКИ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = PinkBrush });
            heading.Children.Add(new TextBlock { Text = "Сайты и игровые серверы", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 5, 0, 0) });
            heading.Children.Add(new TextBlock { Text = "Сохраняй адреса, открывай их одной кнопкой и подключай нужные домены к маршруту Nova.", FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            header.Children.Add(heading);
            var close = ActionButton("ЗАКРЫТЬ", FieldBrush, delegate { dialog.Close(); });
            close.Width = 120;
            close.Height = 38;
            close.FontSize = 11;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            header.MouseLeftButtonDown += delegate { dialog.DragMove(); };
            layout.Children.Add(header);
            var scroll = ThemedScroll(null);
            scroll.Margin = new Thickness(0, 16, 0, 0);
            scroll.Content = BuildCustomLinksPanel();
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            var footer = new TextBlock { Text = "Сайт HTTPS добавляется в доменный список. Адрес вида 123.123.123.123:27015 сохраняется как игровой сервер, запускается через Steam и учитывается по IP и порту. Esc — закрыть окно.", FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            shell.Child = layout;
            dialog.Content = shell;
            dialog.ShowDialog();
        }

        private void AddCustomLink()
        {
            string name = customLinkNameInput == null ? String.Empty : customLinkNameInput.Text.Trim();
            string address = customLinkAddressInput == null ? String.Empty : customLinkAddressInput.Text.Trim();
            Uri uri;
            if (String.IsNullOrWhiteSpace(name)) { ShowError("Укажи название ссылки. Например, «Мой сервер» или «Любимый сайт»."); return; }
            if (!TryGetLinkUri(address, out uri)) { ShowError("Адрес не распознан. Используй https://, http://, steam:// или формат IP:порт, например 203.0.113.10:27015."); return; }
            foreach (NovaLink existing in settings.CustomLinks)
                if (String.Equals(existing.Address, address, StringComparison.OrdinalIgnoreCase)) { ShowError("Такая ссылка уже добавлена. Открой её в окне «Мои ссылки» или удали старую запись."); return; }
            var link = new NovaLink(name, address);
            settings.CustomLinks.Add(link);
            settings.Save();
            SyncCustomHostList();
            if (runtimeService.State == RuntimeState.Running)
            {
                runtimeService.Restart(selectedMode);
                WriteActivity("Маршрут обновлён для новой ссылки.");
            }
            if (customLinksPanel != null) AddCustomLinkRow(customLinksPanel, link);
            customLinkNameInput.Text = String.Empty;
            customLinkAddressInput.Text = String.Empty;
            WriteActivity("Добавлена ссылка «" + name + "».");
        }

        private void AddCustomLinkRow(Panel parent, NovaLink link)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0), MinHeight = 68 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock { Text = link.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            copy.Children.Add(new TextBlock { Text = link.Address, FontSize = 11, Foreground = MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            row.Children.Add(copy);
            var open = ActionButton("ОТКРЫТЬ", FieldBrush, delegate { OpenCustomLink(link); });
            open.Height = 40;
            open.FontSize = 11;
            open.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            var remove = ActionButton("УДАЛИТЬ", FieldBrush, delegate { RemoveCustomLink(link, row); });
            remove.Height = 40;
            remove.FontSize = 11;
            remove.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            parent.Children.Add(row);
        }

        private void OpenCustomLink(NovaLink link)
        {
            Uri uri;
            if (!TryGetLinkUri(link.Address, out uri)) { ShowError("Ссылка больше не распознаётся."); return; }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            WriteActivity("Открыта ссылка «" + link.Name + "».");
        }

        private void RemoveCustomLink(NovaLink link, UIElement row)
        {
            settings.CustomLinks.Remove(link);
            settings.Save();
            SyncCustomHostList();
            if (runtimeService.State == RuntimeState.Running) runtimeService.Restart(selectedMode);
            if (customLinksPanel != null) customLinksPanel.Children.Remove(row);
            WriteActivity("Удалена ссылка «" + link.Name + "».");
        }

        private static bool TryGetLinkUri(string address, out Uri uri)
        {
            uri = null;
            if (String.IsNullOrWhiteSpace(address)) return false;
            if (address.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                return Uri.TryCreate(address, UriKind.Absolute, out uri);
            Uri typedUri;
            if (Uri.TryCreate(address, UriKind.Absolute, out typedUri) && (typedUri.Scheme == Uri.UriSchemeHttp || typedUri.Scheme == Uri.UriSchemeHttps))
            {
                uri = typedUri;
                return true;
            }
            int portSeparator = address.LastIndexOf(':');
            int port;
            if (portSeparator > 0 && Int32.TryParse(address.Substring(portSeparator + 1), out port) && port > 0 && port < 65536)
            {
                string host = address.Substring(0, portSeparator).Trim();
                if (host.Length > 0)
                {
                    address = "steam://connect/" + host + ":" + port;
                    return Uri.TryCreate(address, UriKind.Absolute, out uri);
                }
            }
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        private void SyncCustomHostList()
        {
            try
            {
                string directory = Path.Combine(Path.GetFullPath(root), "runtime", "lists");
                Directory.CreateDirectory(directory);
                var hosts = new List<string>();
                var addresses = new List<string>();
                var ports = new List<string>();
                foreach (NovaLink link in settings.CustomLinks)
                {
                    Uri uri;
                    if (!TryGetLinkUri(link.Address, out uri)) continue;
                    if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    {
                        string host = uri.Host.Trim().ToLowerInvariant();
                        if (!String.IsNullOrWhiteSpace(host) && !hosts.Contains(host)) hosts.Add(host);
                    }
                    else if (uri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase) && uri.Host.Equals("connect", StringComparison.OrdinalIgnoreCase))
                    {
                        string target = uri.AbsolutePath.Trim('/');
                        int separator = target.LastIndexOf(':');
                        if (separator > 0)
                        {
                            string host = target.Substring(0, separator).Trim().ToLowerInvariant();
                            string port = target.Substring(separator + 1).Trim();
                            int portNumber;
                            if (!String.IsNullOrWhiteSpace(host) && !addresses.Contains(host))
                            {
                                IPAddress[] resolved = ResolveHostAddresses(host);
                                foreach (IPAddress address in resolved)
                                    if (address.AddressFamily == AddressFamily.InterNetwork && !addresses.Contains(address.ToString())) addresses.Add(address.ToString());
                            }
                            if (Int32.TryParse(port, out portNumber) && portNumber > 0 && portNumber < 65536 && !ports.Contains(port)) ports.Add(port);
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(directory, "include-user.txt"), hosts.ToArray(), Encoding.ASCII);
                File.WriteAllLines(Path.Combine(directory, "ipset-user.txt"), addresses.ToArray(), Encoding.ASCII);
                File.WriteAllLines(Path.Combine(directory, "game-ports.txt"), ports.ToArray(), Encoding.ASCII);
            }
            catch (Exception error)
            {
                WriteActivity("Не удалось обновить список пользовательских хостов: " + error.Message);
            }
        }

        private static IPAddress[] ResolveHostAddresses(string host)
        {
            try { return Dns.GetHostAddresses(host); }
            catch (Exception) { return new IPAddress[0]; }
        }

        private Button ActionButton(string text, Brush background, Action action)
        {
            var button = new Button { Content = text, Height = 42, MinHeight = 42, Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(14, 6, 14, 6), Background = background, BorderBrush = LineBrush, BorderThickness = new Thickness(1), Foreground = background == PinkBrush || background == CyanBrush ? CanvasBrush : TextBrush, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ApplyButtonTemplate(button, background, LineBrush, button.Foreground, 10);
            button.Click += delegate { try { action(); } catch (Exception error) { ShowError(error.Message); } };
            return button;
        }

        private UIElement RailItem(string glyph, string title, string detail, bool selected)
        {
            var border = new Border { Background = selected ? CardBrush : Brushes.Transparent, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(0, 3, 0, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = glyph, FontSize = 17, Foreground = selected ? CyanBrush : MutedBrush, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = TextBrush });
            text.Children.Add(new TextBlock { Text = detail, FontSize = 10, Foreground = MutedBrush });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            border.Child = grid;
            return border;
        }

        private TextBlock SectionText(string value)
        {
            return new TextBlock { Text = value, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = MutedBrush, Margin = new Thickness(9, 0, 0, 6) };
        }

        private TextBlock ShortcutLine(string key, string value)
        {
            var text = new TextBlock { Margin = new Thickness(9, 7, 0, 0), FontSize = 13 };
            text.Inlines.Add(new System.Windows.Documents.Run(key) { Foreground = CyanBrush, FontWeight = FontWeights.Bold });
            text.Inlines.Add(new System.Windows.Documents.Run("   " + value) { Foreground = MutedBrush });
            return text;
        }

        private void InitializeTray()
        {
            trayIcon = new Forms.NotifyIcon { Visible = true, Text = "Reze Nova · маршрут остановлен", Icon = LoadTrayIcon() };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            var menu = new Forms.ContextMenuStrip();
            trayStatus = new Forms.ToolStripMenuItem("Reze Nova · запуск") { Enabled = false };
            menu.Items.Add(trayStatus);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(MenuItem("Показать Nova", delegate { ShowFromTray(); }));
            menu.Items.Add(MenuItem("Запустить маршрут", delegate { Dispatcher.BeginInvoke(new Action(StartRoute)); }));
            menu.Items.Add(MenuItem("Остановить маршрут", delegate { Dispatcher.BeginInvoke(new Action(StopRoute)); }));
            menu.Items.Add(MenuItem("Диагностика Discord", delegate { Dispatcher.BeginInvoke(new Action(RunDiscordDiagnostics)); }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(MenuItem("Выход", delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); }));
            trayIcon.ContextMenuStrip = menu;
        }

        private Forms.ToolStripMenuItem MenuItem(string text, Action action)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += delegate { action(); };
            return item;
        }

        private Drawing.Icon LoadTrayIcon()
        {
            string path = Path.Combine(root, "assets", "ui", "reze-nova.ico");
            try { if (File.Exists(path)) return new Drawing.Icon(path); } catch (Exception) { }
            return Drawing.SystemIcons.Application;
        }

        private void DisposeTray()
        {
            if (trayIcon == null) return;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void HideToTray()
        {
            Hide();
            if (trayIcon != null) trayIcon.ShowBalloonTip(1200, "Reze Nova", "Nova продолжает работать в системном трее.", Forms.ToolTipIcon.Info);
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            allowExit = true;
            Close();
        }

        private void RegisterGlobalHotKeys()
        {
            windowSource = PresentationSource.FromVisual(this) as HwndSource;
            if (windowSource == null) return;
            windowSource.AddHook(HandleWindowMessage);
            bool registered = RegisterHotKey(windowSource.Handle, HotKeyAuto, ModAlt, 0x41)
                && RegisterHotKey(windowSource.Handle, HotKeyFast, ModAlt, 0x31)
                && RegisterHotKey(windowSource.Handle, HotKeyStable, ModAlt, 0x32)
                && RegisterHotKey(windowSource.Handle, HotKeyWide, ModAlt, 0x33)
                && RegisterHotKey(windowSource.Handle, HotKeyDiagnostics, ModAlt | ModControl, 0x44);
            if (!registered)
            {
                UnregisterGlobalHotKeys();
                WriteActivity("Горячие клавиши заняты другим приложением.");
            }
        }

        private IntPtr HandleWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmHotKey)
            {
                switch (wParam.ToInt32())
                {
                    case HotKeyAuto: SelectMode(NovaMode.Auto, true); break;
                    case HotKeyFast: SelectMode(NovaMode.Fast, true); break;
                    case HotKeyStable: SelectMode(NovaMode.Stable, true); break;
                    case HotKeyWide: SelectMode(NovaMode.Wide, true); break;
                    case HotKeyDiagnostics: RunDiscordDiagnostics(); break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void UnregisterGlobalHotKeys()
        {
            if (windowSource == null) return;
            UnregisterHotKey(windowSource.Handle, HotKeyAuto);
            UnregisterHotKey(windowSource.Handle, HotKeyFast);
            UnregisterHotKey(windowSource.Handle, HotKeyStable);
            UnregisterHotKey(windowSource.Handle, HotKeyWide);
            UnregisterHotKey(windowSource.Handle, HotKeyDiagnostics);
            windowSource.RemoveHook(HandleWindowMessage);
            windowSource = null;
        }

        private void SelectMode(NovaMode mode, bool restartIfRunning)
        {
            selectedMode = mode;
            settings.Mode = mode;
            settings.Save();
            selectedModeValue.Text = "Активный режим Reze · " + ModeName(mode);
            foreach (KeyValuePair<NovaMode, Button> entry in modeButtons)
            {
                bool active = entry.Key == mode;
                entry.Value.BorderBrush = active ? PinkBrush : LineBrush;
                entry.Value.Background = active ? Brush(ColorFrom("34203A")) : FieldBrush;
                entry.Value.Foreground = TextBrush;
            }
            WriteActivity("Выбран профиль «" + ModeName(mode) + "».");
            if (restartIfRunning && runtimeService.State == RuntimeState.Running)
            {
                runtimeService.Restart(mode);
                WriteActivity("Маршрут перезапущен в режиме «" + ModeName(mode) + "».");
            }
            RefreshState();
        }

        private void StartRoute()
        {
            if (!isAdministrator)
            {
                RelaunchElevated();
                return;
            }
            try
            {
                runtimeService.Start(selectedMode);
                routeWasRunning = true;
                routeStartedAt = DateTime.Now;
                WriteActivity("Маршрут запущен в режиме «" + ModeName(selectedMode) + "».");
                Notify("Маршрут активен", "WinDivert и Lua готовы к работе.", Forms.ToolTipIcon.Info);
                RefreshState();
            }
            catch (Exception error)
            {
                routeWasRunning = false;
                string message = error.Message;
                WriteActivity("Маршрут не запущен: " + DescribeRouteError(message));
                RefreshState();
                ShowError(message);
            }
        }

        private static string DescribeRouteError(string message)
        {
            if (IsEngineConflict(message))
            {
                return "Другой запрет уже запущен. Nova обнаружила работающий winws2 или winws, поэтому не стала запускать второй сетевой маршрут.\n\nЗакрой другое приложение или службу запрета, дождись исчезновения процесса winws2.exe в Диспетчере задач и нажми «Запустить маршрут» ещё раз. Пока другой запрет занимает WinDivert, YouTube может открываться, но видео будет зависать.\n\nПодробности:\n" + message;
            }
            if (message.IndexOf("administrator", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("elevated", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Для маршрута нужны права администратора. Закрой это окно, запусти Nova от имени администратора и повтори запуск.\n\nПодробности:\n" + message;
            return "Nova не смогла запустить сетевой маршрут. Проверь состояние в карточке «Маршрут» и открой журналы для подробностей.\n\nПодробности:\n" + message;
        }

        private void StopRoute()
        {
            runtimeService.Stop();
            routeWasRunning = false;
            WriteActivity("Маршрут остановлен.");
            RefreshState();
        }

        private void OpenYouTube()
        {
            Process.Start(new ProcessStartInfo("https://www.youtube.com/") { UseShellExecute = true });
            WriteActivity("YouTube открывается в браузере.");
        }

        private void OpenDiscord()
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
            if (!Directory.Exists(local)) throw new FileNotFoundException("Discord Desktop не найден.");
            string executable = null;
            foreach (string candidate in Directory.GetFiles(local, "Discord.exe", SearchOption.AllDirectories))
                if (executable == null || candidate.Length > executable.Length) executable = candidate;
            if (executable == null) throw new FileNotFoundException("Discord Desktop не найден.");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            WriteActivity("Discord открывается.");
        }

        private void RestartDiscord()
        {
            string[] names = { "Discord", "Update" };
            foreach (string name in names)
            {
                Process[] processes = Process.GetProcessesByName(name);
                try { foreach (Process process in processes) { try { if (name == "Discord") process.Kill(); } catch (InvalidOperationException) { } finally { process.Dispose(); } } }
                catch (Exception) { }
            }
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
            string executable = null;
            if (Directory.Exists(local))
            {
                foreach (string candidate in Directory.GetFiles(local, "Discord.exe", SearchOption.AllDirectories))
                    if (executable == null || candidate.Length > executable.Length) executable = candidate;
            }
            if (executable == null) throw new FileNotFoundException("Discord Desktop не найден.");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            WriteActivity("Discord перезапускается после обновления маршрута.");
        }

        private void StartTelegram(bool open)
        {
            telegramService.Start(CancellationToken.None);
            if (open)
            {
                Uri uri = telegramService.ConnectionUri;
                if (uri != null) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            WriteActivity("Telegram-прокси слушает 127.0.0.1:1443.");
            RefreshState();
        }

        private void StopTelegram()
        {
            telegramService.Stop();
            WriteActivity("Telegram-прокси остановлен.");
            RefreshState();
        }

        private void RunNetworkProbe()
        {
            networkValue.Text = "ПРОВЕРКА";
            networkDetail.Text = "Discord · YouTube · реальный видеопоток · Telegram";
            WriteActivity("Запущены проверки сайтов и отдельного видеопотока YouTube.");
            Task<NetworkResult> probe = Task.Factory.StartNew<NetworkResult>(delegate { return ProbeNetwork(); });
            probe.ContinueWith(delegate(Task<NetworkResult> task)
            {
                if (closing) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (task.IsFaulted) { networkValue.Text = "ОШИБКА"; networkDetail.Text = task.Exception.GetBaseException().Message; WriteActivity("Сетевой тест завершился с ошибкой."); return; }
                    networkValue.Text = task.Result.Summary;
                    networkDetail.Text = task.Result.Detail;
                    WriteActivity(task.Result.Log);
                }));
            }, TaskScheduler.Default);
        }

        private void RunDiscordDiagnostics()
        {
            diagnosticValue.Text = "Проверка Discord API · CDN · media · UDP...";
            WriteActivity("Запущена расширенная диагностика Discord.");
            Task<DiscordDiagnosticResult> task = Task.Factory.StartNew<DiscordDiagnosticResult>(ProbeDiscord);
            task.ContinueWith(delegate(Task<DiscordDiagnosticResult> result)
            {
                if (closing) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (result.IsFaulted) { diagnosticValue.Text = "Ошибка диагностики: " + result.Exception.GetBaseException().Message; return; }
                    diagnosticValue.Text = result.Result.Summary;
                    WriteActivity(result.Result.Log);
                    Notify("Диагностика Discord завершена", result.Result.Summary, result.Result.Success ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
                }));
            }, TaskScheduler.Default);
        }

        private NetworkResult ProbeNetwork()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var detail = new StringBuilder();
            string[] names = { "Discord", "YouTube", "YouTube видео", "Telegram" };
            string[] urls = { "https://discord.com/", "https://www.youtube.com/", "https://telegram.org/" };
            int success = 0;
            string[] results = { ProbeHttp(urls[0], "HEAD"), ProbeHttp(urls[1], "HEAD"), ProbeYoutubeVideo(), ProbeHttp(urls[2], "HEAD") };
            for (int index = 0; index < results.Length; index++)
            {
                string result = results[index];
                if (IsSuccessfulProbe(names[index], result)) success++;
                if (detail.Length > 0) detail.Append(" · ");
                detail.Append(names[index]).Append(" ").Append(FormatProbeResult(names[index], result));
            }
            string summary = success + "/4 ПРОВЕРКИ ДОСТУПНЫ";
            if (success < 4) summary += " · YouTube может не загружаться";
            return new NetworkResult(summary, detail.ToString(), "Сетевой тест сайтов и реального видеопотока YouTube завершён: " + detail);
        }

        private DiscordDiagnosticResult ProbeDiscord()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string api = ProbeHttp("https://discord.com/api/v9/experiments", "GET");
            string cdn = ProbeHttp("https://discordcdn.com/", "HEAD");
            string media = ProbeHttp("https://discord.media/", "HEAD");
            string gateway = ProbeHttp("https://gateway.discord.gg/", "HEAD");
            string udp = ProbeUdp("discord.media");
            bool success = IsReachable(api) && IsReachable(cdn) && IsReachable(media) && IsReachable(gateway) && udp == "OK";
            string summary = "API " + api + " · CDN " + cdn + " · media " + media + " · gateway " + gateway + " · UDP " + udp;
            return new DiscordDiagnosticResult(success, summary, "Диагностика Discord: " + summary);
        }

        private static string ProbeHttp(string address, string method)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(address);
                request.Method = method;
                request.Proxy = null;
                request.Timeout = 7000;
                request.ReadWriteTimeout = 7000;
                request.UserAgent = "Reze-Nova/1.0";
                using (var response = (HttpWebResponse)request.GetResponse()) return ((int)response.StatusCode).ToString();
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null) { using (response) return ((int)response.StatusCode).ToString(); }
                return error.Status == WebExceptionStatus.Timeout ? "TIMEOUT" : "ERROR";
            }
            catch (Exception) { return "ERROR"; }
        }

        private static string ProbeYoutubeVideo()
        {
            try
            {
                const string playerUrl = "https://www.youtubei.googleapis.com/youtubei/v1/player?prettyPrint=false";
                const string playerBody = "{\"context\":{\"client\":{\"clientName\":\"WEB\",\"clientVersion\":\"2.20250101.00.00\"}},\"videoId\":\"aqz-KE-bpKQ\"}";
                var player = (HttpWebRequest)WebRequest.Create(playerUrl);
                player.Method = "POST";
                player.Proxy = null;
                player.Timeout = 10000;
                player.ReadWriteTimeout = 10000;
                player.UserAgent = "Reze-Nova/1.0";
                player.ContentType = "application/json";
                byte[] body = Encoding.UTF8.GetBytes(playerBody);
                player.ContentLength = body.Length;
                using (Stream stream = player.GetRequestStream()) stream.Write(body, 0, body.Length);
                string responseText;
                using (var response = (HttpWebResponse)player.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream())) responseText = reader.ReadToEnd();
                responseText = responseText.Replace("\\/", "/").Replace("\\u0026", "&");
                int marker = responseText.IndexOf(".googlevideo.com/videoplayback", StringComparison.OrdinalIgnoreCase);
                if (marker < 0) return "API 200 · STREAM URL НЕ НАЙДЕН";
                int start = responseText.LastIndexOf("https://", marker, StringComparison.OrdinalIgnoreCase);
                int end = responseText.IndexOf('"', marker);
                if (start < 0 || end <= start) return "API 200 · STREAM URL НЕ НАЙДЕН";
                string streamUrl = responseText.Substring(start, end - start).Replace("\\u0026", "&").Replace("\\/", "/");
                var segment = (HttpWebRequest)WebRequest.Create(streamUrl);
                segment.Method = "GET";
                segment.Proxy = null;
                segment.Timeout = 10000;
                segment.ReadWriteTimeout = 10000;
                segment.UserAgent = "Reze-Nova/1.0";
                segment.AddRange(0, 1023);
                using (var response = (HttpWebResponse)segment.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    return status == 200 || status == 206 ? "STREAM " + status : "STREAM ERROR " + status;
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null) { using (response) return "STREAM ERROR " + (int)response.StatusCode; }
                return error.Status == WebExceptionStatus.Timeout ? "TIMEOUT" : "ERROR";
            }
            catch (Exception) { return "ERROR"; }
        }

        private static string ProbeUdp(string host)
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                if (addresses.Length == 0) return "ERROR";
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.SendTimeout = 2500;
                    socket.Connect(new IPEndPoint(addresses[0], 443));
                    socket.Send(new byte[] { 0, 0, 0, 0 });
                    return "OK";
                }
            }
            catch (Exception) { return "ERROR"; }
        }

        private static bool IsSuccessfulProbe(string name, string result)
        {
            if (name == "YouTube видео") return result.StartsWith("STREAM ");
            return result.StartsWith("2") || result.StartsWith("3");
        }

        private static string FormatProbeResult(string name, string result)
        {
            if (name == "YouTube видео" && result.StartsWith("STREAM ")) return "ВИДЕОПОТОК ДОСТУПЕН (" + result.Substring("STREAM ".Length) + ")";
            if (result == "TIMEOUT") return "ТАЙМ-АУТ — соединение не завершилось";
            if (result == "ERROR") return "ОШИБКА СОЕДИНЕНИЯ";
            if (result.StartsWith("API ")) return result;
            return "HTTP " + result;
        }

        private static bool IsReachable(string value)
        {
            return value != "TIMEOUT" && value != "ERROR";
        }

        private void RefreshState()
        {
            if (closing) return;
            RuntimeState routeState = runtimeService.State;
            if (routeState == RuntimeState.Running) routeWasRunning = true;
            if (routeWasRunning && routeState == RuntimeState.Failed && !recoveryInProgress && DateTime.UtcNow - lastRecovery > TimeSpan.FromSeconds(8))
                RecoverRoute();
            routeValue.Text = routeState == RuntimeState.Running ? "АКТИВЕН" : routeState == RuntimeState.Starting ? "ЗАПУСК" : routeState == RuntimeState.Failed ? "ОШИБКА" : "ОСТАНОВЛЕН";
            routeValue.Foreground = routeState == RuntimeState.Running ? GreenBrush : routeState == RuntimeState.Failed ? PinkBrush : TextBrush;
            routeDetail.Text = routeState == RuntimeState.Running
                ? ModeName(selectedMode) + " · " + FormatUptime() + " · автоконтроль"
                : runtimeService.LastError ?? "Готов к запуску";
            commandRouteValue.Text = routeValue.Text;
            commandRouteValue.Foreground = routeValue.Foreground;
            commandRouteDetail.Text = routeDetail.Text;
            recoveryValue.Text = recoveryCount == 0 ? "Восстановление готово" : "Восстановлений: " + recoveryCount;
            TelegramProxyStatus telegram = telegramService.Status;
            telegramValue.Text = telegram.State == TelegramProxyState.Failed ? "ОШИБКА" : telegram.State == TelegramProxyState.Running ? (telegram.DesktopVerified ? "АКТИВЕН" : "ОЖИДАНИЕ") : "ОСТАНОВЛЕН";
            telegramValue.Foreground = telegram.State == TelegramProxyState.Failed ? PinkBrush : telegram.DesktopVerified ? GreenBrush : telegram.State == TelegramProxyState.Running ? AmberBrush : TextBrush;
            telegramDetail.Text = telegram.State == TelegramProxyState.Running ? (telegram.DesktopVerified ? "Telegram Desktop подтверждён · " : "Ожидание Telegram Desktop · ") + telegram.ClientCount + " подключений · порт " + telegram.Port : telegram.LastError ?? "Порт 127.0.0.1:1443";
            try
            {
                new RuntimeManifest(runtimeDirectory).Validate();
                manifestValue.Text = "ПРОВЕРЕН";
                manifestValue.Foreground = GreenBrush;
                manifestDetail.Text = "zapret2 v1.0.7 · SHA-256 OK";
            }
            catch (Exception error) { manifestValue.Text = "НЕ ГОТОВ"; manifestValue.Foreground = AmberBrush; manifestDetail.Text = error.Message; }
            footerValue.Text = "REZE NOVA  ·  " + (isAdministrator ? "повышенные права" : "обычный режим") + "  ·  " + DateTime.Now.ToString("HH:mm:ss") + "  ·  " + recoveryValue.Text;
            UpdateActionStates(routeState, telegram);
            if (trayIcon != null)
            {
                string status = routeState == RuntimeState.Running ? "маршрут активен" : routeState == RuntimeState.Failed ? "ошибка маршрута" : "маршрут остановлен";
                trayIcon.Text = "Reze Nova · " + status;
                trayStatus.Text = "Reze Nova · " + status;
            }
        }

        private void UpdateActionStates(RuntimeState routeState, TelegramProxyStatus telegram)
        {
            bool routeRunning = routeState == RuntimeState.Running || routeState == RuntimeState.Starting;
            if (sceneStartButton != null) sceneStartButton.IsEnabled = !routeRunning;
            if (routeStartButton != null) routeStartButton.IsEnabled = !routeRunning;
            if (routeStopButton != null) routeStopButton.IsEnabled = routeRunning;
            bool telegramRunning = telegram.State == TelegramProxyState.Running;
            if (proxyStopButton != null) proxyStopButton.IsEnabled = telegramRunning;
        }

        private string FormatUptime()
        {
            if (routeStartedAt == DateTime.MinValue) return "00:00:00";
            TimeSpan uptime = DateTime.Now - routeStartedAt;
            return uptime.ToString(uptime.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

        private void RecoverRoute()
        {
            recoveryInProgress = true;
            lastRecovery = DateTime.UtcNow;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    runtimeService.Restart(selectedMode);
                    Dispatcher.BeginInvoke(new Action(delegate { recoveryCount++; recoveryInProgress = false; WriteActivity("Маршрут автоматически восстановлен."); Notify("Маршрут восстановлен", "Nova снова перехватила трафик.", Forms.ToolTipIcon.Info); RefreshState(); }));
                }
                catch (Exception error)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { recoveryInProgress = false; WriteActivity("Автовосстановление не удалось: " + error.Message); RefreshState(); }));
                }
            });
        }

        private void RelaunchElevated()
        {
            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "ui") { UseShellExecute = true, Verb = "runas" });
            allowExit = true;
            WriteActivity("Запрошены права администратора. Новое окно продолжит работу.");
            Close();
        }

        private void StopServices()
        {
            try { runtimeService.Stop(); } catch (Exception) { }
            try { telegramService.Stop(); } catch (Exception) { }
            runtimeService.Dispose();
            telegramService.Dispose();
        }

        private void ShowGuide()
        {
            ShowStyledGuide();
        }

        private void CopyDiagnostics()
        {
            string text = CreateDiagnosticsText();
            Clipboard.SetText(text);
            WriteActivity("Диагностика скопирована в буфер обмена.");
            Notify("Диагностика скопирована", "Отчёт готов для отправки или сохранения.", Forms.ToolTipIcon.Info);
        }

        private string CreateDiagnosticsText()
        {
            RuntimeState route = runtimeService.State;
            TelegramProxyStatus telegram = telegramService.Status;
            string manifest;
            try { new RuntimeManifest(runtimeDirectory).Validate(); manifest = "OK"; } catch (Exception error) { manifest = error.Message; }
            return "Reze Nova diagnostics\r\n" +
                "Time: " + DateTime.Now.ToString("O") + "\r\n" +
                "Mode: " + ModeName(selectedMode) + "\r\n" +
                "Route: " + route + "\r\n" +
                "Route error: " + (runtimeService.LastError ?? "none") + "\r\n" +
                "Runtime manifest: " + manifest + "\r\n" +
                "Telegram: " + telegram.State + ", clients=" + telegram.ClientCount + ", port=" + telegram.Port + "\r\n" +
                "Recovery count: " + recoveryCount + "\r\n" +
                "Process: " + Process.GetCurrentProcess().Id + "\r\n";
        }

        private void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        private void Notify(string title, string text, Forms.ToolTipIcon icon)
        {
            if (trayIcon != null) trayIcon.ShowBalloonTip(1800, title, text, icon);
        }

        private void WriteActivity(string message)
        {
            activity.Text = DateTime.Now.ToString("HH:mm:ss") + "\n" + message;
        }

        private void ShowError(string message)
        {
            string display = IsEngineConflict(message) ? DescribeRouteError(message) : message;
            WriteActivity("Ошибка: " + display);
            Notify("Reze Nova", display, Forms.ToolTipIcon.Warning);
            ShowMessageWindow(IsEngineConflict(message) ? "Маршрут не запущен" : "Нужна проверка", display, AmberBrush);
        }

        private static bool IsEngineConflict(string message)
        {
            return message.IndexOf("already active", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("winws2", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("winws", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ShowMessageWindow(string title, string message, Brush accent)
        {
            var dialog = new Window
            {
                Owner = this,
                Title = "Reze Nova",
                Width = 820,
                Height = 560,
                MinWidth = 700,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                Foreground = TextBrush,
                FontFamily = FontFamily
            };
            var shell = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("0C0818"), ColorFrom("171129"), 25),
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(30),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0, 0, 0), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.48 }
            };
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "REZE NOVA  /  СООБЩЕНИЕ", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = accent });
            heading.Children.Add(new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 5, 0, 0) });
            heading.MouseLeftButtonDown += delegate { dialog.DragMove(); };
            layout.Children.Add(heading);
            var bodyText = new TextBlock { Text = message, FontSize = 15, LineHeight = 23, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 12, 12) };
            var bodyScroll = new ScrollViewer
            {
                Content = bodyText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Padding = new Thickness(0, 8, 0, 0),
                Background = Brushes.Transparent
            };
            Grid.SetRow(bodyScroll, 1);
            layout.Children.Add(bodyScroll);
            var close = ActionButton("ПОНЯТНО", accent, delegate { dialog.Close(); });
            close.Width = 170;
            close.Height = 46;
            close.FontSize = 12;
            close.Margin = new Thickness(0, 8, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(close, 2);
            layout.Children.Add(close);
            dialog.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) { dialog.Close(); args.Handled = true; } };
            shell.Child = layout;
            dialog.Content = shell;
            dialog.ShowDialog();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                if (e.Key == Key.A) { SelectMode(NovaMode.Auto, true); e.Handled = true; }
                else if (e.Key == Key.D1) { SelectMode(NovaMode.Fast, true); e.Handled = true; }
                else if (e.Key == Key.D2) { SelectMode(NovaMode.Stable, true); e.Handled = true; }
                else if (e.Key == Key.D3) { SelectMode(NovaMode.Wide, true); e.Handled = true; }
            }
            base.OnPreviewKeyDown(e);
        }

        private static string ModeName(NovaMode mode)
        {
            switch (mode)
            {
                case NovaMode.Fast: return "Быстрый";
                case NovaMode.Stable: return "Стабильный";
                case NovaMode.Wide: return "Широкий";
                default: return "Авто";
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private ImageSource LoadImageAny(params string[] files)
        {
            foreach (string file in files)
            {
                ImageSource image = LoadImage(file);
                if (image != null) return image;
            }
            return null;
        }

        private ImageSource LoadImage(string file)
        {
            string path = Path.Combine(root, "assets", "ui", file);
            if (!File.Exists(path)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        }

        private static TextBlock LabelText(double size, Color color)
        {
            return new TextBlock { FontSize = size, Foreground = Brush(color), FontWeight = size > 15 ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        }

        private static Color ColorFrom(string value)
        {
            return (Color)ColorConverter.ConvertFromString("#" + value);
        }

        private static Brush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private sealed class NetworkResult
        {
            public readonly string Summary;
            public readonly string Detail;
            public readonly string Log;
            public NetworkResult(string summary, string detail, string log) { Summary = summary; Detail = detail; Log = log; }
        }

        private sealed class DiscordDiagnosticResult
        {
            public readonly bool Success;
            public readonly string Summary;
            public readonly string Log;
            public DiscordDiagnosticResult(bool success, string summary, string log) { Success = success; Summary = summary; Log = log; }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace OdysseusDesktop
{
    public static class Motion
    {
        public const double EntranceSeconds = 1.65;
        public static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return 1 - Math.Pow(1 - t, 3); }
        public static double Height(double t)
        {
            if (t < .16) return -150 * (1 - Ease(t / .16));
            if (t < .84) return 138 * Math.Sin(Math.PI * (t - .16) / .68);
            return 0;
        }
        public static double Angle(double t) { return t < .16 ? 0 : -360 * Ease(Math.Min(1, (t - .16) / .68)); }
        public static int Gaze(double dx, double dy)
        {
            double degree = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            return ((int)Math.Round(degree / 22.5)) % 16;
        }
        public static readonly int[][] Durations = {
            new [] {280,110,110,140,140,320}, new [] {120,120,120,120,120,120,120,220},
            new [] {120,120,120,120,120,120,120,220}, new [] {140,140,140,280},
            new [] {140,140,140,140,280}, new [] {140,140,140,140,140,140,140,240},
            new [] {150,150,150,150,150,260}, new [] {120,120,120,120,120,220},
            new [] {150,150,150,150,150,280}
        };
        public static int Frame(int row, double ms)
        {
            int sum = 0; foreach (int d in Durations[row]) sum += d;
            double phase = Math.Max(0, ms) % sum;
            for (int i = 0; i < Durations[row].Length; i++) { if (phase < Durations[row][i]) return i; phase -= Durations[row][i]; }
            return 0;
        }
    }

    public static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "OdysseusDesktopPet";
        public static string Command { get { return "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Odysseus.exe") + "\""; } }
        public static bool Enabled
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && (k.GetValue(Name) as string) == Command; }
        }
        public static void Set(bool enabled)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                if (enabled) k.SetValue(Name, Command, RegistryValueKind.String); else k.DeleteValue(Name, false);
        }
    }

    public static class TogglePlacement
    {
        public static Point BottomRightFallback(Rect workArea, double screenWidth, double screenHeight, double width, double height)
        {
            double taskbarHeight = Math.Max(0, screenHeight - workArea.Bottom);
            double left = Math.Max(workArea.Left + 8, Math.Min(screenWidth - width - 8, workArea.Right - width - 132));
            double top = taskbarHeight >= height
                ? workArea.Bottom + (taskbarHeight - height) / 2
                : screenHeight - height - 6;
            return new Point(left, top);
        }
    }

    public sealed class PetWindow : Window
    {
        const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        readonly Canvas stage = new Canvas();
        readonly Image pet = new Image();
        readonly Image sleepingPet = new Image();
        readonly ScaleTransform squash = new ScaleTransform(1, 1);
        readonly RotateTransform spin = new RotateTransform(0);
        readonly TranslateTransform lift = new TranslateTransform();
        readonly ScaleTransform sleepBreath = new ScaleTransform(1,1);
        readonly TranslateTransform sleepSettle = new TranslateTransform();
        readonly Border bubble = new Border();
        readonly TextBlock message = new TextBlock();
        readonly TextBlock[] sleepSymbols = new TextBlock[3];
        readonly TranslateTransform[] sleepSymbolLifts = new TranslateTransform[3];
        readonly StackPanel modulePanel = new StackPanel();
        readonly TranslateTransform moduleSlide = new TranslateTransform(0, 20);
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        readonly BitmapSource[,] frames = new BitmapSource[11, 8];
        BitmapSource sleepFrame;
        readonly byte[,][] pixels = new byte[11,8][];
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        double actionAt, actionUntil, bubbleUntil, lastPet, lastPointerAt, lastTick;
        double sleepPose, sleepPoseFrom, sleepTransitionAt, sleepTransitionDuration=.01;
        int actionRow, currentRow = -1, currentFrame = -1, pats;
        bool flipping, sleeping, quiet, menuOpen, shutdown, modulesShown;
        bool isMouseDown, isDragging, walking;
        Point dragStartScreen, dragStartWindow;
        double walkStartLeft, walkTargetLeft, walkDuration, walkStartTime;
        double modulesVisibleUntil;
        int walkDirection;
        double size = 152, foot = 195;
        Point lastPointer;
        MenuItem startupItem, quietItem, sleepItem;
        VoyageDeskWindow organizerWindow;
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdysseusDesktop", "preferences.txt");

        public PetWindow()
        {
            Title = "Odysseus Desktop Pet"; Width = 320; Height = 410;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            stage.ClipToBounds = true; Content = stage;
            LoadSettings(); LoadFrames();
            TransformGroup transforms = new TransformGroup();
            transforms.Children.Add(squash); transforms.Children.Add(spin); transforms.Children.Add(lift);
            pet.RenderTransform = transforms; pet.RenderTransformOrigin = new Point(.5, .6);
            pet.Stretch = Stretch.Fill; pet.Cursor = Cursors.Hand;
            RenderOptions.SetBitmapScalingMode(pet, BitmapScalingMode.NearestNeighbor);
            System.Windows.Automation.AutomationProperties.SetName(pet, "Odysseus. Click to pet, double-click to backflip, drag to move, right-click for menu.");
            stage.Children.Add(pet);
            TransformGroup sleepTransforms=new TransformGroup();sleepTransforms.Children.Add(sleepBreath);sleepTransforms.Children.Add(sleepSettle);sleepingPet.RenderTransform=sleepTransforms;sleepingPet.RenderTransformOrigin=new Point(.5,1);sleepingPet.Stretch=Stretch.Fill;sleepingPet.Cursor=Cursors.Hand;sleepingPet.Opacity=0;sleepingPet.IsHitTestVisible=false;RenderOptions.SetBitmapScalingMode(sleepingPet,BitmapScalingMode.NearestNeighbor);System.Windows.Automation.AutomationProperties.SetName(sleepingPet,"Sleeping Odysseus. Click to wake him.");stage.Children.Add(sleepingPet);
            message.Foreground = new SolidColorBrush(Color.FromRgb(80, 62, 58));
            message.FontFamily = new FontFamily("Cascadia Mono"); message.FontWeight = FontWeights.SemiBold; message.FontSize = 12;
            TextOptions.SetTextFormattingMode(message,TextFormattingMode.Display); TextOptions.SetTextRenderingMode(message,TextRenderingMode.Aliased); TextOptions.SetTextHintingMode(message,TextHintingMode.Fixed);
            message.TextWrapping = TextWrapping.Wrap; message.TextAlignment = TextAlignment.Center;
            bubble.Child = message; bubble.Background = new SolidColorBrush(Color.FromRgb(255, 249, 225));
            bubble.BorderBrush = new SolidColorBrush(Color.FromRgb(113,132,137)); bubble.BorderThickness = new Thickness(2);
            bubble.CornerRadius = new CornerRadius(10); bubble.Padding = new Thickness(13,9,13,9);
            bubble.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 3, Direction = 315, Opacity = .22, Color = Colors.Black };
            bubble.MaxWidth = 245; bubble.Opacity = 0; bubble.IsHitTestVisible = false;
            stage.Children.Add(bubble);
            string[] zText={"Z","ZZ","ZZZ"};
            for(int i=0;i<sleepSymbols.Length;i++)
            {
                TextBlock symbol=new TextBlock{Text=zText[i],Foreground=new SolidColorBrush(Color.FromRgb(23,61,70)),FontFamily=new FontFamily("Cascadia Mono"),FontWeight=FontWeights.Bold,FontSize=12+i*2,Opacity=0,IsHitTestVisible=false};
                TranslateTransform rise=new TranslateTransform();symbol.RenderTransform=rise;sleepSymbols[i]=symbol;sleepSymbolLifts[i]=rise;TextOptions.SetTextFormattingMode(symbol,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(symbol,TextRenderingMode.Aliased);TextOptions.SetTextHintingMode(symbol,TextHintingMode.Fixed);stage.Children.Add(symbol);
            }
            BuildHoverModules();
            pet.MouseLeftButtonDown += OnMouseDown;
            pet.MouseMove += OnMouseMove;
            pet.MouseLeftButtonUp += OnMouseUp;
            pet.MouseRightButtonUp += delegate { pet.ContextMenu.IsOpen = true; };
            sleepingPet.MouseLeftButtonDown+=OnMouseDown;sleepingPet.MouseMove+=OnMouseMove;sleepingPet.MouseLeftButtonUp+=OnMouseUp;sleepingPet.MouseRightButtonUp+=delegate{sleepingPet.ContextMenu.IsOpen=true;};
            BuildMenu(); BuildTray();
            SourceInitialized += delegate {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
                HwndSource.FromHwnd(hwnd).AddHook(WindowHook);
                Place();
            };
            Loaded += delegate { Place(); Flip(true); };
            SystemParameters.StaticPropertyChanged += WorkAreaChanged;
            timer.Interval = TimeSpan.FromMilliseconds(33); timer.Tick += Tick; timer.Start();
            Closed += delegate {
                shutdown = true; timer.Stop(); tray.Visible = false; tray.Dispose();
                if (organizerWindow != null) organizerWindow.Close();
                SystemParameters.StaticPropertyChanged -= WorkAreaChanged;
            };
        }

        void LoadFrames()
        {
            string path = Path.Combine(root, "assets", "spritesheet.png");
            BitmapImage atlas = new BitmapImage(); atlas.BeginInit(); atlas.CacheOption = BitmapCacheOption.OnLoad;
            atlas.UriSource = new Uri(path); atlas.EndInit(); atlas.Freeze();
            if (atlas.PixelWidth != 1536 || atlas.PixelHeight != 2288) throw new InvalidDataException("Odysseus requires the validated v2 spritesheet (1536 x 2288).");
            for (int r = 0; r < 11; r++) for (int c = 0; c < 8; c++)
            {
                CroppedBitmap crop = new CroppedBitmap(atlas, new Int32Rect(c*192, r*208, 192, 208)); crop.Freeze();
                frames[r,c] = crop;
                FormatConvertedBitmap rgba = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                pixels[r,c] = new byte[192*208*4]; rgba.CopyPixels(pixels[r,c],192*4,0);
            }
            for (int y = 207; y >= 0; y--) {
                bool found = false; for (int x = 0; x < 192; x++) if (pixels[0,0][(y*192+x)*4+3] > 80) { found = true; break; }
                if (found) { foot = y; break; }
            }
            string sleepPath=Path.Combine(root,"assets","sleeping.png");BitmapImage sleep=new BitmapImage();sleep.BeginInit();sleep.CacheOption=BitmapCacheOption.OnLoad;sleep.UriSource=new Uri(sleepPath);sleep.EndInit();sleep.Freeze();sleepFrame=sleep;sleepingPet.Source=sleepFrame;
            SetFrame(0, 0);
        }
        void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            foreach (string line in File.ReadAllLines(settingsPath)) {
                if (line == "quiet=true") quiet = true;
                if (line == "size=small") size = 120;
                if (line == "size=large") size = 182;
            }
        }
        void SaveSettings()
        {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllLines(settingsPath, new [] {"quiet=" + quiet.ToString().ToLowerInvariant(), "size=" + (size < 140 ? "small" : size > 170 ? "large" : "medium")});
            } catch (Exception ex) { Program.Log(ex); Say("I couldn't save that preference."); }
        }

        Button ModuleButton(string text)
        {
            ScaleTransform pressScale = new ScaleTransform(1,1);
            Button button = new Button {
                Content = text, Width = 74, Height = 27, Margin = new Thickness(0,0,0,5),
                Background = new SolidColorBrush(Color.FromRgb(255,253,247)),
                Foreground = new SolidColorBrush(Color.FromRgb(32,30,31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(113,132,137)), BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono"), FontWeight = FontWeights.Bold, FontSize = 12,
                Cursor = Cursors.Hand, Focusable = false
            };
            FrameworkElementFactory frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(content); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
            button.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 315, Opacity = .18, Color = Colors.Black };
            button.UseLayoutRounding = true; button.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(button,TextFormattingMode.Display); TextOptions.SetTextRenderingMode(button,TextRenderingMode.Auto); TextOptions.SetTextHintingMode(button,TextHintingMode.Fixed);
            button.RenderTransformOrigin = new Point(.5,.5); button.RenderTransform = pressScale;
            button.PreviewMouseLeftButtonDown += delegate {
                QuadraticEase ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                pressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(pressScale.ScaleX,.92,TimeSpan.FromMilliseconds(85)) { EasingFunction = ease });
                pressScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(pressScale.ScaleY,.92,TimeSpan.FromMilliseconds(85)) { EasingFunction = ease });
            };
            button.PreviewMouseLeftButtonUp += delegate {
                DoubleAnimationUsingKeyFrames spring = new DoubleAnimationUsingKeyFrames();
                spring.KeyFrames.Add(new EasingDoubleKeyFrame(1.035,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)),new CubicEase { EasingMode = EasingMode.EaseOut }));
                spring.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                bool reset = false; spring.Completed += delegate { if (reset) return; reset = true; pressScale.ScaleX = pressScale.ScaleY = 1; pressScale.BeginAnimation(ScaleTransform.ScaleXProperty,null); pressScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); };
                pressScale.BeginAnimation(ScaleTransform.ScaleXProperty,spring); pressScale.BeginAnimation(ScaleTransform.ScaleYProperty,spring);
            };
            button.MouseEnter += delegate { modulesVisibleUntil = watch.Elapsed.TotalSeconds + 1.0; };
            return button;
        }

        void BuildHoverModules()
        {
            modulePanel.Opacity = 0; modulePanel.Visibility = Visibility.Collapsed;
            modulePanel.RenderTransform = moduleSlide;
            Button emote = ModuleButton("EMOTE");
            Button tools = ModuleButton("TOOLS");
            modulePanel.Children.Add(emote); modulePanel.Children.Add(tools); stage.Children.Add(modulePanel);
            System.Windows.Automation.AutomationProperties.SetName(emote, "Open Odysseus emotes");
            System.Windows.Automation.AutomationProperties.SetName(tools, "Open to-do and notes tools");

            ContextMenu emotes = new ContextMenu(); ApplyEmoteMenu(emotes);
            emotes.Opened += delegate { menuOpen = true; modulesVisibleUntil = watch.Elapsed.TotalSeconds + 10; };
            emotes.Closed += delegate { menuOpen = false; modulesVisibleUntil = watch.Elapsed.TotalSeconds + .7; };
            Add(emotes, "Backflip", delegate { Flip(false); });
            Add(emotes, "Say hello", delegate { Wake(); Act(3, 2.1); Say("Hi! Ready for our next voyage."); });
            Add(emotes, "Ponder", Ponder);
            Add(emotes, "Heroic stumble", Stumble);
            Add(emotes, "Give a snack", Snack);
            Add(emotes, "Nap / wake", Nap);
            emote.ContextMenu = emotes;
            emote.Click += delegate { emotes.PlacementTarget = emote; emotes.Placement = System.Windows.Controls.Primitives.PlacementMode.Left; emotes.IsOpen = true; };
            tools.Click += delegate { OpenOrganizer(); };
        }

        void OpenOrganizer()
        {
            modulesVisibleUntil = watch.Elapsed.TotalSeconds + 1;
            if (organizerWindow == null)
            {
                organizerWindow = new VoyageDeskWindow();
                organizerWindow.Closed += delegate { organizerWindow = null; };
                organizerWindow.ShowAnimated();
            }
            else if (organizerWindow.IsVisible) { organizerWindow.DismissAnimated(); return; }
            else organizerWindow.ShowAnimated();
        }

        void ShowModules()
        {
            modulesVisibleUntil = watch.Elapsed.TotalSeconds + .7;
            if (modulesShown) return;
            modulesShown = true; modulePanel.Visibility = Visibility.Visible;
            moduleSlide.Y = 20; modulePanel.Opacity = 0;
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            moduleSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing });
            modulePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
        }

        void HideModules()
        {
            if (!modulesShown || menuOpen) return;
            modulesShown = false;
            DoubleAnimation fade = new DoubleAnimation(modulePanel.Opacity, 0, TimeSpan.FromMilliseconds(130));
            fade.Completed += delegate { if (!modulesShown) { modulePanel.Visibility = Visibility.Collapsed; moduleSlide.Y = 20; } };
            moduleSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(moduleSlide.Y, 20, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            modulePanel.BeginAnimation(OpacityProperty, fade);
        }

        void UpdateHoverModules(double now)
        {
            POINT cursor;
            if (!GetCursorPos(out cursor)) return;
            Point point = PointFromScreen(new Point(cursor.X, cursor.Y));
            double petLeft = Canvas.GetLeft(pet), petTop = Canvas.GetTop(pet);
            Rect hoverZone = new Rect(Math.Max(0, petLeft - 104), petTop - 8, pet.Width + 112, pet.Height + 16);
            if (hoverZone.Contains(point)) ShowModules();
            else if (now > modulesVisibleUntil) HideModules();
        }

        void Place()
        {
            if (shutdown) return;
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - Width - 18; Top = area.Bottom - Height;
            pet.Width = size; pet.Height = size * 208 / 192;
            Canvas.SetLeft(pet, Width - size - 12);
            Canvas.SetTop(pet, Height - (foot + 1) * size / 192);
            double sleepWidth=size*1.55;sleepingPet.Width=sleepWidth;sleepingPet.Height=sleepWidth*144/256;Canvas.SetLeft(sleepingPet,Width-sleepWidth-12);Canvas.SetTop(sleepingPet,Height-sleepingPet.Height);
            Canvas.SetRight(bubble, 12); Canvas.SetBottom(bubble, size * 208 / 192 + 18);
            double sleepLeft=Canvas.GetLeft(sleepingPet),sleepTop=Canvas.GetTop(sleepingPet);
            for(int i=0;i<sleepSymbols.Length;i++){Canvas.SetLeft(sleepSymbols[i],sleepLeft+36+i*21);Canvas.SetTop(sleepSymbols[i],sleepTop-5-i*23);}
            Canvas.SetLeft(modulePanel, Math.Max(8, Width - size - 98));
            Canvas.SetTop(modulePanel, Math.Max(80, Canvas.GetTop(pet) + 30));
        }
        void WorkAreaChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) { Dispatcher.BeginInvoke(new Action(Place)); }
        IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
            if (msg == 0x02E0 || msg == 0x007E || msg == 0x001A) Dispatcher.BeginInvoke(new Action(Place));
            if (msg == WM_NCHITTEST && !menuOpen && !isDragging) {
                long lp = lParam.ToInt64(); Point p = PointFromScreen(new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff)));
                if (modulePanel.Visibility == Visibility.Visible) {
                    Rect moduleBounds = new Rect(Canvas.GetLeft(modulePanel), Canvas.GetTop(modulePanel), modulePanel.ActualWidth, modulePanel.ActualHeight);
                    if (moduleBounds.Contains(p)) return IntPtr.Zero;
                }
                Point local = stage.TranslatePoint(p, pet);
                int x = (int)(local.X * 192 / pet.Width), y = (int)(local.Y * 208 / pet.Height);
                bool solid = x >= 0 && x < 192 && y >= 0 && y < 208 && currentRow >= 0 && pixels[currentRow,currentFrame][(y*192+x)*4+3] > 28;
                if (!solid) { handled = true; return new IntPtr(HTTRANSPARENT); }
            }
            return IntPtr.Zero;
        }
        MenuItem Add(ContextMenu menu, string label, Action action)
        {
            MenuItem item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); return item;
        }
        void ApplyEmoteMenu(ContextMenu menu)
        {
            SolidColorBrush cream = new SolidColorBrush(Color.FromRgb(255,249,240));
            SolidColorBrush ink = new SolidColorBrush(Color.FromRgb(37,50,56));
            SolidColorBrush softLine = new SolidColorBrush(Color.FromRgb(169,179,177));
            SolidColorBrush sage = new SolidColorBrush(Color.FromRgb(207,228,215));
            menu.Background=cream;menu.Foreground=ink;menu.BorderBrush=softLine;menu.BorderThickness=new Thickness(1);menu.Padding=new Thickness(7);menu.MinWidth=184;
            menu.FontFamily=new FontFamily("Cascadia Mono");menu.FontWeight=FontWeights.Medium;menu.FontSize=12;menu.HasDropShadow=false;
            TextOptions.SetTextFormattingMode(menu,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(menu,TextRenderingMode.Auto);TextOptions.SetTextHintingMode(menu,TextHintingMode.Fixed);

            FrameworkElementFactory surface=new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(ContextMenu.BackgroundProperty));
            surface.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(ContextMenu.BorderBrushProperty));
            surface.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(ContextMenu.BorderThicknessProperty));
            surface.SetValue(Border.PaddingProperty,new TemplateBindingExtension(ContextMenu.PaddingProperty));
            surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(14));
            surface.SetValue(Border.EffectProperty,new DropShadowEffect{BlurRadius=10,ShadowDepth=3,Direction=315,Opacity=.2,Color=Colors.Black});
            FrameworkElementFactory items=new FrameworkElementFactory(typeof(StackPanel));items.SetValue(StackPanel.IsItemsHostProperty,true);surface.AppendChild(items);
            menu.Template=new ControlTemplate(typeof(ContextMenu)){VisualTree=surface};

            Style itemStyle=new Style(typeof(MenuItem));itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty,Brushes.Transparent));itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty,ink));itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty,new Thickness(12,8,12,8)));itemStyle.Setters.Add(new Setter(MenuItem.MarginProperty,new Thickness(1)));
            FrameworkElementFactory itemSurface=new FrameworkElementFactory(typeof(Border));itemSurface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(MenuItem.BackgroundProperty));itemSurface.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
            FrameworkElementFactory label=new FrameworkElementFactory(typeof(ContentPresenter));label.SetValue(ContentPresenter.ContentSourceProperty,"Header");label.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(MenuItem.PaddingProperty));label.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);itemSurface.AppendChild(label);
            ControlTemplate itemTemplate=new ControlTemplate(typeof(MenuItem)){VisualTree=itemSurface};Trigger hover=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};hover.Setters.Add(new Setter(MenuItem.BackgroundProperty,sage));itemTemplate.Triggers.Add(hover);itemStyle.Setters.Add(new Setter(MenuItem.TemplateProperty,itemTemplate));menu.Resources[typeof(MenuItem)]=itemStyle;
        }
        void ApplyPixelMenu(ContextMenu menu)
        {
            menu.Background = new SolidColorBrush(Color.FromRgb(255,249,225));
            menu.Foreground = new SolidColorBrush(Color.FromRgb(32,30,31));
            menu.BorderBrush = new SolidColorBrush(Color.FromRgb(32,30,31)); menu.BorderThickness = new Thickness(3);
            menu.Padding = new Thickness(3); menu.FontFamily = new FontFamily("Segoe UI"); menu.FontWeight = FontWeights.Medium; menu.FontSize = 12;
            menu.HasDropShadow = false; menu.Effect = new DropShadowEffect { BlurRadius=0,ShadowDepth=4,Direction=315,Opacity=.32,Color=Colors.Black };
            Style itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty,new Thickness(10,6,12,6)));
            itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty,Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty,new SolidColorBrush(Color.FromRgb(32,30,31))));
            Trigger highlight = new Trigger { Property=MenuItem.IsHighlightedProperty,Value=true };
            highlight.Setters.Add(new Setter(MenuItem.BackgroundProperty,new SolidColorBrush(Color.FromRgb(244,201,120))));
            itemStyle.Triggers.Add(highlight); menu.Resources[typeof(MenuItem)] = itemStyle;
        }
        void BuildMenu()
        {
            ContextMenu menu = new ContextMenu();
            ApplyPixelMenu(menu);
            menu.Opened += delegate { menuOpen = true; startupItem.IsChecked = Startup.Enabled; quietItem.IsChecked = quiet; sleepItem.Header = sleeping ? "Wake Odysseus" : "Take a nap"; };
            menu.Closed += delegate { menuOpen = false; };
            Add(menu, "Odysseus  ·  your little desktop companion", delegate { Say("Click to pet me. Double-click for a flip!"); });
            menu.Items.Add(new Separator());
            Add(menu, "Do a backflip", delegate { Flip(false); });
            Add(menu, "Patrol the taskbar", StartWalk);
            Add(menu, "Give a snack", Snack);
            Add(menu, "Say hello", delegate { Wake(); Act(3, 2.1); Say("Hi! Happy to keep you company."); });
            Add(menu, "Ponder next voyage", Ponder);
            Add(menu, "Heroic stumble", Stumble);
            Add(menu, "Wait for orders", ImpatientWait);
            sleepItem = Add(menu, "Take a nap", Nap);
            Add(menu, "Come back to the corner", delegate { Place(); Flip(false); });
            menu.Items.Add(new Separator());
            MenuItem sizes = new MenuItem { Header = "Pet size" };
            foreach (double value in new [] {120.0,152.0,182.0}) {
                double selected = value; MenuItem item = new MenuItem { Header = value == 120 ? "Small" : value == 152 ? "Medium" : "Large" };
                item.Click += delegate { size = selected; Place(); SaveSettings(); }; sizes.Items.Add(item);
            }
            menu.Items.Add(sizes);
            quietItem = Add(menu, "Quiet mode (less motion)", delegate { quiet = !quiet; quietItem.IsChecked = quiet; SaveSettings(); Say(quiet ? "Quiet company. I'm right here." : "Ready to play!"); }); quietItem.IsCheckable = true;
            startupItem = Add(menu, "Start with Windows", delegate {
                try { Startup.Set(!Startup.Enabled); startupItem.IsChecked = Startup.Enabled; Say(Startup.Enabled ? "See you when you sign in!" : "Automatic startup is off."); }
                catch (Exception ex) { Program.Log(ex); Say("Windows couldn't change my startup setting."); }
            }); startupItem.IsCheckable = true;
            menu.Items.Add(new Separator());
            Add(menu, "Hide Odysseus (use taskbar switch)", HidePet);
            Add(menu, "Exit Odysseus and switch", delegate { Application.Current.Shutdown(); });
            pet.ContextMenu = menu;sleepingPet.ContextMenu=menu;
        }
        void BuildTray()
        {
            string icon = Path.Combine(root, "assets", "odysseus.ico");
            tray.Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.SystemIcons.Application;
            tray.Text = "Odysseus — click to say hello"; tray.Visible = true;
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Font = new System.Drawing.Font("Segoe UI",9,System.Drawing.FontStyle.Regular); menu.BackColor = System.Drawing.Color.FromArgb(255,249,225); menu.ForeColor = System.Drawing.Color.FromArgb(32,30,31); menu.ShowImageMargin = false;
            menu.Items.Add("Show / hide Odysseus", null, delegate { Dispatcher.Invoke(new Action(delegate { if (IsVisible) HidePet(); else ShowPet(true); })); });
            menu.Items.Add("To-do & notes", null, delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(false); OpenOrganizer(); })); });
            menu.Items.Add("Say hello", null, delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(false); Wake(); Place(); Act(3,2); Say("Here I am!"); })); });
            menu.Items.Add("Backflip", null, delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(false); Flip(false); })); });
            menu.Items.Add("Patrol taskbar", null, delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(false); StartWalk(); })); });
            menu.Items.Add("Give a snack", null, delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(false); Snack(); })); });
            menu.Items.Add("Nap / wake", null, delegate { Dispatcher.Invoke(new Action(delegate { if (!IsVisible) ShowPet(false); Nap(); })); });
            menu.Items.Add("Exit Odysseus and switch", null, delegate { Dispatcher.Invoke(new Action(delegate { Application.Current.Shutdown(); })); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(delegate { ShowPet(true); })); };
        }

        public void ShowPet(bool entrance)
        {
            if (!IsVisible) Show();
            Wake(); Place();
            if (entrance) Flip(true);
        }

        public void HidePet()
        {
            flipping = false; walking = false; actionUntil = 0;
            modulesShown = false; modulePanel.Visibility = Visibility.Collapsed;
            bubble.Opacity = 0; spin.Angle = 0; lift.X = lift.Y = 0;sleepingPet.Opacity=0;sleepingPet.IsHitTestVisible=false;
            squash.ScaleX = squash.ScaleY = 1;
            Hide();
        }
        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2) { Flip(false); return; }
            isMouseDown = true;
            isDragging = false;
            dragStartScreen = PointToScreen(e.GetPosition(this));
            dragStartWindow = new Point(Left, Top);
            UIElement target=sender as UIElement;if(target!=null)target.CaptureMouse();
        }
        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isMouseDown) return;
            Point currentScreen = PointToScreen(e.GetPosition(this));
            Vector delta = currentScreen - dragStartScreen;
            if (!isDragging && delta.Length > 6)
            {
                isDragging = true;
                Wake();
                flipping = false;
                walking = false;
                Say("Wheee! Leading the voyage!");
            }
            if (isDragging)
            {
                Left = dragStartWindow.X + delta.X;
                Top = dragStartWindow.Y + delta.Y;
                SetFrame(4, 1);
            }
        }
        void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isMouseDown) return;
            UIElement target=sender as UIElement;if(target!=null)target.ReleaseMouseCapture();
            isMouseDown = false;
            if (isDragging)
            {
                isDragging = false;
                DropToTaskbar();
            }
            else
            {
                OnPet(sender, e);
            }
        }
        void DropToTaskbar()
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left));
            Top = area.Bottom - Height;
            Act(4, 0.6);
            Say("Landed on my feet!");
        }
        void OnPet(object sender, MouseButtonEventArgs e)
        {
            bool wasSleeping=sleeping||sleepPose>.01;Wake();if(wasSleeping){Say("Good morning! I'm getting back on my feet.");return;}if (flipping || walking) return;
            double now = watch.Elapsed.TotalSeconds;
            if (now - lastPet > 8) pats = 0; lastPet = now; pats++;
            Act(3, 1.15);
            Say(pats > 3 ? "Best desk buddy. That's you!" : pats > 1 ? "Even heroes like head pats." : "A hero deserves a little rest.");
        }
        void Wake()
        {
            if(sleeping||sleepPose>0){sleepPoseFrom=sleepPose;sleepTransitionAt=watch.Elapsed.TotalSeconds;sleepTransitionDuration=Math.Max(.14,.82*sleepPose);}
            sleeping=false;HideSleepSymbols();if(sleepPose<=.001)pet.Opacity=1;
        }
        void Nap()
        {
            flipping=false;walking=false;actionUntil=0;
            if(sleeping){Wake();Say("I'm back! Did I miss anything?");return;}
            sleepPoseFrom=sleepPose;sleepTransitionAt=watch.Elapsed.TotalSeconds;sleepTransitionDuration=Math.Max(.18,1.05*(1-sleepPose));sleeping=true;
            Say("Resting by the taskbar. Click me when you need me.");
        }
        void Snack() { Wake(); flipping = false; walking = false; Act(7, 2.4); Say("Nom nom... delicious! Ready for another journey."); }
        void Stumble() { Wake(); flipping = false; walking = false; Act(5, 2.5); Say("Whoops! A minor misstep on the road to glory!"); }
        void Ponder() { Wake(); flipping = false; walking = false; Act(8, 2.5); Say("Plotting a course past Scylla and Charybdis..."); }
        void ImpatientWait() { Wake(); flipping = false; walking = false; Act(6, 2.2); Say("Standing by, Captain! What's our next adventure?"); }
        void StartWalk()
        {
            Wake(); flipping = false; isDragging = false;
            Rect area = SystemParameters.WorkArea;
            walkStartLeft = Left;
            walkTargetLeft = Math.Max(area.Left + 40, area.Right - Width - 360);
            walkDuration = 4.0;
            walkStartTime = watch.Elapsed.TotalSeconds;
            walkDirection = -1;
            walking = true;
            Say("Patrolling the taskbar!");
        }
        void Act(int row, double seconds) { actionRow = row; actionAt = watch.Elapsed.TotalSeconds; actionUntil = actionAt + seconds; }
        void Say(string text) { message.Text = text; bubbleUntil = watch.Elapsed.TotalSeconds + 4; bubble.Opacity = 1; }
        public void Flip(bool startup)
        {
            Wake(); flipping = true; walking = false; actionAt = watch.Elapsed.TotalSeconds; actionUntil = actionAt + Motion.EntranceSeconds;
            Say(startup ? "Hi, I'm Odysseus! Click to pet.\nDrag to move · Right-click for menu." : "A little flip, just for you!");
        }
        void SetFrame(int row, int frame) { if (row == currentRow && frame == currentFrame) return; pet.Source = frames[row,frame]; currentRow = row; currentFrame = frame; }
        void UpdateSleepPose(double now)
        {
            double progress=Math.Max(0,Math.Min(1,(now-sleepTransitionAt)/sleepTransitionDuration));double eased=progress*progress*(3-2*progress);
            sleepPose=sleeping?sleepPoseFrom+(1-sleepPoseFrom)*eased:sleepPoseFrom*(1-eased);
        }
        void HideSleepSymbols(){for(int i=0;i<sleepSymbols.Length;i++){sleepSymbols[i].Opacity=0;sleepSymbolLifts[i].Y=0;}}
        void ApplySleepPose(double now)
        {
            double crouch=Math.Max(0,Math.Min(1,sleepPose/.62));int crouchFrame=Math.Max(0,Math.Min(6,(int)Math.Round(crouch*6)));SetFrame(5,crouchFrame);
            bool handoff=sleepPose>=.65;pet.Opacity=handoff?0:1;spin.Angle=-4*crouch;lift.X=-6*crouch;lift.Y=9*crouch;squash.ScaleX=1+.025*crouch;squash.ScaleY=1-.055*crouch;
            double settle=Math.Max(0,Math.Min(1,(sleepPose-.65)/.35));settle=settle*settle*(3-2*settle);double dip=settle>.72?Math.Sin((settle-.72)/.28*Math.PI)*2.5:0;
            sleepingPet.Opacity=handoff?1:0;sleepingPet.IsHitTestVisible=handoff;sleepSettle.X=50*(1-Math.Pow(settle,4));sleepSettle.Y=(1-settle)*14+dip;double breath=sleeping&&sleepPose>.98?.5+.5*Math.Sin(now*1.8):0;sleepBreath.ScaleX=.92+.08*settle+.006*breath;sleepBreath.ScaleY=.92+.08*settle-.012*breath;
            if(!sleeping||sleepPose<.80){HideSleepSymbols();return;}
            for(int i=0;i<sleepSymbols.Length;i++)
            {
                double cycle=(now*.58+i*.24)%1.0;double fade=Math.Sin(Math.PI*cycle);sleepSymbols[i].Opacity=Math.Max(0,fade)*sleepPose;sleepSymbolLifts[i].Y=-8*cycle;
            }
        }
        void Tick(object sender, EventArgs args)
        {
            if (!IsVisible) return;
            double now = watch.Elapsed.TotalSeconds;
            if (now - lastTick > 5) { Place(); actionUntil = now; flipping = false; }
            lastTick = now;
            UpdateHoverModules(now);
            bubble.Opacity = Math.Max(0, Math.Min(1, (bubbleUntil - now) * 2));
            spin.Angle = 0; lift.X = lift.Y = 0; squash.ScaleX = squash.ScaleY = 1;pet.Opacity=1;sleepingPet.Opacity=0;sleepingPet.IsHitTestVisible=false;sleepBreath.ScaleX=sleepBreath.ScaleY=1;sleepSettle.X=sleepSettle.Y=0;
            UpdateSleepPose(now);
            if (isDragging) return;
            if (walking) {
                double elapsed = now - walkStartTime;
                double progress = elapsed / walkDuration;
                if (progress >= 1.0) {
                    if (walkDirection == -1) {
                        walkDirection = 1;
                        walkStartLeft = Left;
                        Rect area = SystemParameters.WorkArea;
                        walkTargetLeft = area.Right - Width - 18;
                        walkStartTime = now;
                        walkDuration = 4.0;
                        Act(3, 1.2);
                        Say("All clear! Heading back.");
                        return;
                    } else {
                        walking = false;
                        Place();
                        Act(0, 0);
                        Say("Back at my post!");
                        return;
                    }
                } else {
                    Left = walkStartLeft + (walkTargetLeft - walkStartLeft) * progress;
                    int row = walkDirection == -1 ? 2 : 1;
                    SetFrame(row, Motion.Frame(row, elapsed * 1000));
                    return;
                }
            }
            if (flipping) {
                double t = (now - actionAt) / Motion.EntranceSeconds;
                if (t >= 1) { flipping = false; Act(3, .85); }
                else {
                    SetFrame(4, t < .16 ? 0 : t < .36 ? 1 : t < .66 ? 2 : t < .84 ? 3 : 4);
                    if (!quiet) { lift.Y = -Motion.Height(t); spin.Angle = Motion.Angle(t);
                        if (t > .84) { double settle = Math.Sin((t - .84) / .16 * Math.PI) * .16; squash.ScaleX = 1 + settle; squash.ScaleY = 1 - settle; }
                    }
                    return;
                }
            }
            if(sleeping||sleepPose>.001){ApplySleepPose(now);return;}else HideSleepSymbols();
            if (now < actionUntil) { SetFrame(actionRow, Motion.Frame(actionRow,(now-actionAt)*1000)); return; }
            POINT cursor;
            if (!quiet && !menuOpen && !walking && !isDragging && GetCursorPos(out cursor)) {
                Point point = PointFromScreen(new Point(cursor.X, cursor.Y));
                if ((point - lastPointer).Length > 3) { lastPointerAt = now; lastPointer = point; }
                double dx = point.X - (Canvas.GetLeft(pet) + pet.Width / 2), dy = point.Y - (Canvas.GetTop(pet) + pet.Height * .38);
                double distance = Math.Sqrt(dx*dx + dy*dy);
                if (distance > 55 && distance < 470 && now - lastPointerAt < 2.5) {
                    int gaze = Motion.Gaze(dx,dy); SetFrame(9 + gaze / 8, gaze % 8); return;
                }
            }
            SetFrame(0, quiet ? 0 : Motion.Frame(0, now*1000));
        }

        public void Capture(string output)
        {
            Place(); flipping = false; SetFrame(0,0); bubble.Opacity = 1;
            message.Text = "Hi, I'm Odysseus!\nClick to pet · Double-click to flip";
            stage.Measure(new Size(Width,Height)); stage.Arrange(new Rect(0,0,Width,Height)); stage.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(stage); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(output)) encoder.Save(stream);
            File.WriteAllText(Path.ChangeExtension(output, ".placement.txt"), "Window DIP: " + Left + "," + Top + "; " + Width + "x" + Height + "\r\nWork area DIP: " + SystemParameters.WorkArea + "\r\nSprite foot DIP: " + (Top + Canvas.GetTop(pet) + (foot+1)*size/192) + "\r\nTaskbar edge DIP: " + SystemParameters.WorkArea.Bottom + "\r\n");
        }

        public void CaptureModules(string output)
        {
            Place(); flipping = false; SetFrame(0,0); bubble.Opacity = 0;
            modulePanel.Visibility = Visibility.Visible; modulePanel.Opacity = 1; moduleSlide.Y = 0;
            stage.Measure(new Size(Width,Height)); stage.Arrange(new Rect(0,0,Width,Height)); stage.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(stage); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(output)) encoder.Save(stream);
        }

        public void CaptureNap(string output,double pose=1)
        {
            Place();flipping=false;walking=false;bubble.Opacity=0;sleeping=true;sleepPoseFrom=Math.Max(0,Math.Min(1,pose));sleepPose=sleepPoseFrom;sleepTransitionAt=watch.Elapsed.TotalSeconds-1;sleepTransitionDuration=1;ApplySleepPose(watch.Elapsed.TotalSeconds);
            stage.Measure(new Size(Width,Height));stage.Arrange(new Rect(0,0,Width,Height));stage.UpdateLayout();
            RenderTargetBitmap bitmap=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);bitmap.Render(stage);PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream stream=File.Create(output))encoder.Save(stream);
        }
        public void ToggleNapForTest(){Nap();}
        public bool IsSleepingForTest{get{return sleeping;}}
        public double SleepPoseForTest{get{return sleepPose;}}
        public bool SleepSymbolsVisibleForTest{get{for(int i=0;i<sleepSymbols.Length;i++)if(sleepSymbols[i].Opacity>.05)return true;return false;}}
    }

    public sealed class TaskbarToggleWindow : Window
    {
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        readonly PetWindow petWindow;
        readonly Border track = new Border();
        readonly Border thumb = new Border();
        readonly Canvas rail = new Canvas();
        readonly DispatcherTimer topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        bool isOn;

        public TaskbarToggleWindow(PetWindow pet)
        {
            petWindow = pet;
            Title = "Odysseus taskbar switch";
            Width = 44; Height = 26;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            SnapsToDevicePixels = true; UseLayoutRounding = true;

            Grid root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            track.Width = 42; track.Height = 24;
            track.HorizontalAlignment = HorizontalAlignment.Center;
            track.VerticalAlignment = VerticalAlignment.Center;
            track.CornerRadius = new CornerRadius(4);
            track.BorderThickness = new Thickness(3);
            track.BorderBrush = new SolidColorBrush(Color.FromRgb(32, 30, 31));
            track.Background = Brushes.White;
            rail.Width = 36; rail.Height = 18;
            thumb.Width = 18; thumb.Height = 18;
            thumb.Background = new SolidColorBrush(Color.FromRgb(32, 30, 31));
            thumb.CornerRadius = new CornerRadius(2);
            rail.Children.Add(thumb); track.Child = rail; root.Children.Add(track); Content = root;
            System.Windows.Automation.AutomationProperties.SetName(root, "Show or hide Odysseus");

            root.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                TogglePet();
                e.Handled = true;
            };
            root.MouseEnter += delegate { if (!isOn) track.Opacity = .82; };
            root.MouseLeave += delegate { UpdateVisual(petWindow.IsVisible); };
            petWindow.IsVisibleChanged += delegate { UpdateVisual(petWindow.IsVisible); };

            ContextMenu menu = new ContextMenu();
            MenuItem toggleItem = new MenuItem { Header = "Show / hide Odysseus" };
            toggleItem.Click += delegate { TogglePet(); };
            MenuItem exitItem = new MenuItem { Header = "Exit Odysseus and switch" };
            exitItem.Click += delegate { Application.Current.Shutdown(); };
            menu.Items.Add(toggleItem); menu.Items.Add(new Separator()); menu.Items.Add(exitItem);
            root.ContextMenu = menu;

            SourceInitialized += delegate {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
                KeepAboveTaskbar();
                HwndSource.FromHwnd(hwnd).AddHook(WindowHook);
                PlaceOnTaskbar();
            };
            Loaded += delegate {
                PlaceOnTaskbar(); UpdateVisual(petWindow.IsVisible, false);
                topmostTimer.Tick += delegate { PlaceOnTaskbar(); KeepAboveTaskbar(); };
                topmostTimer.Start();
            };
            SystemParameters.StaticPropertyChanged += WorkAreaChanged;
            Closed += delegate { topmostTimer.Stop(); SystemParameters.StaticPropertyChanged -= WorkAreaChanged; };
        }

        public void TogglePet()
        {
            if (petWindow.IsVisible) petWindow.HidePet(); else petWindow.ShowPet(true);
            KeepAboveTaskbar();
        }

        public bool IsThumbAnimating { get { return thumb.HasAnimatedProperties; } }

        void KeepAboveTaskbar()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
        }

        IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
            return IntPtr.Zero;
        }

        void WorkAreaChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(PlaceOnTaskbar));
        }

        void UpdateVisual(bool on, bool animate = true)
        {
            isOn = on;
            double targetLeft = on ? 18 : 0;
            double targetOpacity = on ? 1.0 : .62;
            if (animate && IsLoaded)
            {
                double currentLeft = Canvas.GetLeft(thumb);
                if (Double.IsNaN(currentLeft)) currentLeft = on ? 0 : 18;
                double currentOpacity = track.Opacity;
                Canvas.SetLeft(thumb, targetLeft);
                track.Opacity = targetOpacity;
                CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                thumb.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(currentLeft, targetLeft,
                    TimeSpan.FromMilliseconds(190)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
                track.BeginAnimation(OpacityProperty, new DoubleAnimation(currentOpacity, targetOpacity,
                    TimeSpan.FromMilliseconds(150)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
            }
            else
            {
                thumb.BeginAnimation(Canvas.LeftProperty, null);
                track.BeginAnimation(OpacityProperty, null);
                Canvas.SetLeft(thumb, targetLeft);
                track.Opacity = targetOpacity;
            }
            Canvas.SetTop(thumb, 0);
        }

        void PlaceOnTaskbar()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            RECT taskbarRect;
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out taskbarRect))
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
                    Point taskbarTopLeft = fromDevice.Transform(new Point(taskbarRect.Left, taskbarRect.Top));
                    Point taskbarBottomRight = fromDevice.Transform(new Point(taskbarRect.Right, taskbarRect.Bottom));
                    double rightEdge = taskbarBottomRight.X;
                    IntPtr notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                    RECT notificationRect;
                    if (notificationArea != IntPtr.Zero && GetWindowRect(notificationArea, out notificationRect))
                        rightEdge = fromDevice.Transform(new Point(notificationRect.Left, notificationRect.Top)).X;
                    Left = Math.Max(taskbarTopLeft.X + 8, rightEdge - Width - 8);
                    Top = taskbarTopLeft.Y + Math.Max(2, (taskbarBottomRight.Y - taskbarTopLeft.Y - Height) / 2);
                    return;
                }
            }

            Point fallback = TogglePlacement.BottomRightFallback(SystemParameters.WorkArea,
                SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight, Width, Height);
            Left = fallback.X; Top = fallback.Y;
        }

        public void Capture(string output, bool on)
        {
            UpdateVisual(on, false);
            Measure(new Size(Width, Height)); Arrange(new Rect(0, 0, Width, Height)); UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(output)) encoder.Save(stream);
        }
    }

    public static class Program
    {
        public static void Log(Exception ex)
        {
            try { string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdysseusDesktop"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"error.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
        }
        static void Assert(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); }
        static int VisiblePixels(BitmapSource source)
        {
            FormatConvertedBitmap rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] data = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
            rgba.CopyPixels(data, rgba.PixelWidth * 4, 0);
            int visible = 0;
            for (int i = 3; i < data.Length; i += 4) if (data[i] > 0) visible++;
            return visible;
        }
        static void ValidatePackagedAtlas()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "spritesheet.png");
            Assert(File.Exists(path), "packaged spritesheet exists");
            BitmapImage atlas = new BitmapImage();
            atlas.BeginInit(); atlas.CacheOption = BitmapCacheOption.OnLoad; atlas.UriSource = new Uri(path); atlas.EndInit(); atlas.Freeze();
            Assert(atlas.PixelWidth == 1536 && atlas.PixelHeight == 2288, "v2 atlas dimensions");
            int[] frameCounts = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };
            for (int row = 0; row < 11; row++) for (int column = 0; column < 8; column++)
            {
                bool required = column < frameCounts[row] || (row == 0 && column == 6);
                CroppedBitmap cell = new CroppedBitmap(atlas, new Int32Rect(column * 192, row * 208, 192, 208));
                int visible = VisiblePixels(cell);
                Assert(required ? visible > 300 : visible == 0, "atlas cell " + row + "," + column + (required ? " populated" : " empty"));
            }
        }
        static void Test()
        {
            Assert(Motion.Gaze(0,-100)==0 && Motion.Gaze(100,0)==4 && Motion.Gaze(0,100)==8 && Motion.Gaze(-100,0)==12, "cardinal gaze");
            Assert(Motion.Gaze(100,-100)==2 && Motion.Gaze(-100,100)==10, "diagonal gaze");
            Assert(Math.Abs(Motion.Height(1))<.01 && Math.Abs(Motion.Angle(1)+360)<.01, "backflip completes upright on taskbar");
            Assert(Motion.Height(0)<-100 && Motion.Height(.5)>100, "entrance rises from below into jump");
            Assert(Motion.Frame(0,279)==0 && Motion.Frame(0,280)==1 && Motion.Frame(0,1100)==0, "atlas timings wrap correctly");
            Assert(Startup.Command == "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Odysseus.exe") + "\"", "startup executable path quoting");
            Point toggle = TogglePlacement.BottomRightFallback(new Rect(0,0,1536,816),1536,864,44,26);
            Assert(toggle.X > 1200 && toggle.Y >= 816 && toggle.Y + 26 <= 864, "taskbar switch bottom-right placement");
            for(int r=0;r<9;r++) for(int ms=0;ms<5000;ms+=17) Assert(Motion.Frame(r,ms)<Motion.Durations[r].Length,"frame bounds");
            ValidatePackagedAtlas();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: taskbar toggle placement; cardinal and diagonal gaze; backflip trajectory and upright landing; atlas timing and all frame bounds; quoted startup path; complete v2 atlas structure.\r\n");
        }
        [STAThread] public static int Main(string[] args)
        {
            try {
                if (Array.IndexOf(args,"--self-test")>=0) { Test(); return 0; }
                if (Array.IndexOf(args,"--enable-startup")>=0) { Startup.Set(true); return 0; }
                if (Array.IndexOf(args,"--disable-startup")>=0) { Startup.Set(false); return 0; }
                bool capture = args.Length==2 && args[0]=="--capture";
                bool captureModules = args.Length==2 && args[0]=="--capture-modules";
                bool captureNap = (args.Length==2||args.Length==3) && args[0]=="--capture-nap";
                bool captureOrganizer = (args.Length==2 || args.Length==3) && args[0]=="--capture-organizer";
                bool captureToggle = args.Length==3 && args[0]=="--capture-toggle";
                bool testToggle = args.Length==1 && args[0]=="--test-toggle-runtime";
                bool testVoyage = args.Length==1 && args[0]=="--test-voyage-runtime";
                bool testNap = args.Length==1 && args[0]=="--test-nap-runtime";
                bool created;
                using (Mutex mutex = new Mutex(true, @"Local\OdysseusDesktopPet.Prototype", out created)) {
                    if (!created && !capture && !captureModules && !captureNap && !captureOrganizer && !captureToggle && !testToggle && !testVoyage && !testNap) return 0;
                    Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
                    Application app = new Application(); app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e) { Log(e.Exception); };
                    PetWindow window = new PetWindow();
                    if (capture || captureModules || captureNap || testNap) {
                        app.MainWindow = window;
                        if(testNap)window.Loaded+=async delegate{window.ToggleNapForTest();await System.Threading.Tasks.Task.Delay(1150);Assert(window.IsSleepingForTest&&window.SleepPoseForTest>.98,"nap settles into horizontal sleeping pose");Assert(window.SleepSymbolsVisibleForTest,"nap displays animated sleep symbols");window.ToggleNapForTest();await System.Threading.Tasks.Task.Delay(900);Assert(!window.IsSleepingForTest&&window.SleepPoseForTest<.02,"wake returns from sleeping pose");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"nap-runtime-test.txt"),"PASS: staged crouch-to-rest nap settles horizontally, shows animated Z symbols, and wakes upright through the reverse pose sequence.\r\n");window.Close();};
                        else window.Loaded += delegate { window.Dispatcher.BeginInvoke(new Action(delegate { if (capture) window.Capture(args[1]); else if(captureNap) window.CaptureNap(args[1],args.Length==3?Double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture):1); else window.CaptureModules(args[1]); window.Close(); }), DispatcherPriority.ApplicationIdle); };
                        app.Run(window);
                    } else if (captureOrganizer) {
                        VoyageDeskWindow organizer = new VoyageDeskWindow(); app.MainWindow = organizer;
                        organizer.Loaded += delegate { organizer.Dispatcher.BeginInvoke(new Action(delegate { organizer.Capture(args[1], args.Length==3 ? Int32.Parse(args[2]) : 0); organizer.Close(); window.Close(); }), DispatcherPriority.ApplicationIdle); };
                        app.Run(organizer);
                    } else if (testVoyage) {
                        VoyageDeskWindow organizer = new VoyageDeskWindow(); app.MainWindow = organizer;
                        organizer.Loaded += async delegate {
                            organizer.ShowAnimated(); await System.Threading.Tasks.Task.Delay(40);
                            Assert(organizer.IsVisible && organizer.IsPopupMotionActive, "Voyage Desk slide and fade in starts");
                            await System.Threading.Tasks.Task.Delay(340); organizer.SelectTabForTest(1); await System.Threading.Tasks.Task.Delay(40);
                            Assert(organizer.IsTabMotionActive, "To-Do to Notes directional transition starts");
                            await System.Threading.Tasks.Task.Delay(320); organizer.SelectTabForTest(0); await System.Threading.Tasks.Task.Delay(40);
                            Assert(organizer.IsTabMotionActive, "Notes to To-Do directional transition starts");
                            await System.Threading.Tasks.Task.Delay(320); organizer.DismissAnimated(); await System.Threading.Tasks.Task.Delay(40);
                            Assert(organizer.IsPopupMotionActive, "Voyage Desk slide and fade out starts");
                            await System.Threading.Tasks.Task.Delay(240); Assert(!organizer.IsVisible, "Voyage Desk hides after exit animation");
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voyage-runtime-test.txt"), "PASS: panel trigger, settle, exit, directional page pairing, depth, and tab-indicator motion all started and completed.\r\n");
                            organizer.Close(); window.Close();
                        };
                        app.Run(organizer);
                    } else {
                        TaskbarToggleWindow toggleWindow = new TaskbarToggleWindow(window); app.MainWindow = toggleWindow;
                        if (captureToggle) {
                            toggleWindow.Loaded += delegate { toggleWindow.Dispatcher.BeginInvoke(new Action(delegate {
                                toggleWindow.Capture(args[1], true); toggleWindow.Capture(args[2], false); toggleWindow.Close(); window.Close();
                            }), DispatcherPriority.ApplicationIdle); };
                            app.Run(toggleWindow);
                        } else if (testToggle) {
                            toggleWindow.Loaded += delegate { toggleWindow.Dispatcher.BeginInvoke(new Action(delegate {
                                window.ShowPet(false);
                                toggleWindow.TogglePet(); Assert(!window.IsVisible, "taskbar switch hides pet");
                                Assert(toggleWindow.IsThumbAnimating, "taskbar switch thumb slide animation starts");
                                toggleWindow.TogglePet(); Assert(window.IsVisible, "taskbar switch shows pet");
                                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toggle-runtime-test.txt"),
                                    "PASS: the compact taskbar switch hides Odysseus, shows him again, and animates its thumb.\r\n");
                                toggleWindow.Close(); window.Close();
                            }), DispatcherPriority.ApplicationIdle); };
                            app.Run(toggleWindow);
                        } else {
                            toggleWindow.Show(); window.Show(); app.Run();
                        }
                    }
                }
                return 0;
            } catch (Exception ex) { Log(ex); if (args.Length==0) MessageBox.Show("Odysseus couldn't start. " + ex.Message,"Odysseus"); return 1; }
        }
    }
}






using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace OdysseusDesktop
{
    public sealed class VoyageDeskWindow : Window
    {
        static readonly Brush Navy = Brush("#173D46"), Cream = Brush("#FFF9F0"), Paper = Brush("#FFFEFA");
        static readonly Brush Sage = Brush("#CFE4D7"), SageDark = Brush("#507769"), Coral = Brush("#F29A82");
        static readonly Brush Gold = Brush("#F4C978"), Ink = Brush("#253238"), Muted = Brush("#626D70"), Line = Brush("#E8E0D5");
        readonly OrganizerStore store = OrganizerStore.Instance;
        readonly Border shell = new Border(), tabIndicator = new Border();
        readonly Grid outer = new Grid(), pageHost = new Grid();
        readonly TranslateTransform popupSlide = new TranslateTransform(0,20), indicatorSlide = new TranslateTransform();
        readonly ScaleTransform popupScale = new ScaleTransform(.92,.92);
        readonly StackPanel todoItems = new StackPanel();
        readonly TextBox todoInput = new TextBox(), notesInput = new TextBox();
        readonly TextBlock notesStatus = new TextBlock();
        readonly List<Button> tabs = new List<Button>();
        readonly List<FrameworkElement> pages = new List<FrameworkElement>();
        readonly List<TranslateTransform> pageSlides = new List<TranslateTransform>();
        readonly List<ScaleTransform> pageScales = new List<ScaleTransform>();
        readonly double[] tabWidths = { 92, 82 };
        readonly double[] tabIndicatorWidths = { 44, 40 };
        int activeTab = -1;
        bool dismissing;

        public VoyageDeskWindow()
        {
            Title = "Odysseus tools"; Width = 420; Height = 440; MinWidth = 380; MinHeight = 390;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize; ShowInTaskbar = true; Topmost = true; FontFamily = new FontFamily("Cascadia Mono"); FontWeight = FontWeights.Medium;
            UseLayoutRounding=true;SnapsToDevicePixels=true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display); TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto); TextOptions.SetTextHintingMode(this,TextHintingMode.Fixed);
            Rect area = SystemParameters.WorkArea; Left = area.Right-Width-24; Top = Math.Max(area.Top+18,area.Bottom-Height-18);

            outer.Margin = new Thickness(10); outer.Opacity = 0; outer.RenderTransformOrigin = new Point(1,1);
            TransformGroup popupTransform = new TransformGroup(); popupTransform.Children.Add(popupScale); popupTransform.Children.Add(popupSlide); outer.RenderTransform = popupTransform;
            shell.Background = Cream; shell.CornerRadius = new CornerRadius(16); shell.ClipToBounds = true; shell.BorderBrush=Brush("#718489"); shell.BorderThickness=new Thickness(2);
            shell.Effect = new DropShadowEffect { BlurRadius=0,ShadowDepth=6,Direction=315,Opacity=.34,Color=Colors.Black }; outer.Children.Add(shell); Content=outer;
            shell.Child=BuildBody();

            StateChanged += delegate { bool max=WindowState==WindowState.Maximized; outer.Margin=max?new Thickness(0):new Thickness(10); shell.CornerRadius=max?new CornerRadius(0):new CornerRadius(16); };
            Loaded += delegate { RefreshTodos(); };
            Deactivated += delegate { if(IsVisible) DismissAnimated(); };
            PreviewKeyDown += delegate(object sender,KeyEventArgs e){ if(e.Key==Key.Escape){DismissAnimated();e.Handled=true;} };
            Closing += delegate { store.SaveNotes(notesInput.Text); };
        }

        UIElement BuildBody()
        {
            Grid body=new Grid{Margin=new Thickness(18,16,18,18)};body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());
            UIElement rail=BuildTabs();body.Children.Add(rail);
            pages.Add(PreparePage(BuildTodoPage()));pages.Add(PreparePage(BuildNotesPage()));foreach(FrameworkElement page in pages)pageHost.Children.Add(page);pageHost.ClipToBounds=true;Grid.SetRow(pageHost,1);body.Children.Add(pageHost);SelectTab(0,false);return body;
        }

        UIElement BuildTabs()
        {
            Border rail=new Border{Background=Brush("#EEE7DC"),CornerRadius=new CornerRadius(12),BorderBrush=Brush("#A9B3B1"),BorderThickness=new Thickness(1),Padding=new Thickness(3),Margin=new Thickness(0,0,0,13),HorizontalAlignment=HorizontalAlignment.Left};
            Grid layer=new Grid{ClipToBounds=true};tabIndicator.Background=Coral;tabIndicator.CornerRadius=new CornerRadius(2);tabIndicator.Width=tabIndicatorWidths[0];tabIndicator.Height=4;tabIndicator.Margin=new Thickness(0,0,0,2);tabIndicator.HorizontalAlignment=HorizontalAlignment.Left;tabIndicator.VerticalAlignment=VerticalAlignment.Bottom;tabIndicator.RenderTransform=indicatorSlide;layer.Children.Add(tabIndicator);
            StackPanel row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Tab("To-Do",0));row.Children.Add(Tab("Notes",1));layer.Children.Add(row);rail.Child=layer;return rail;
        }

        FrameworkElement PreparePage(FrameworkElement page)
        {
            TranslateTransform slide=new TranslateTransform();ScaleTransform scale=new ScaleTransform(1,1);TransformGroup group=new TransformGroup();group.Children.Add(scale);group.Children.Add(slide);page.RenderTransform=group;page.RenderTransformOrigin=new Point(.5,.5);pageSlides.Add(slide);pageScales.Add(scale);return page;
        }

        FrameworkElement BuildTodoPage()
        {
            Grid panel=new Grid();panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});panel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});Border addCard=Card();StackPanel add=new StackPanel();add.Children.Add(SectionTitle("What’s next?","Keep small promises close and check them off as you go."));
            Grid row=new Grid{Margin=new Thickness(0,13,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(Input(todoInput,"Write a new task…"));
            Button addTodo=PrimaryButton("Add task",Gold);addTodo.Margin=new Thickness(9,0,0,0);addTodo.Click+=delegate{store.AddTodo(todoInput.Text);todoInput.Clear();RefreshTodos();};todoInput.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){addTodo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}};Grid.SetColumn(addTodo,1);row.Children.Add(addTodo);add.Children.Add(row);addCard.Child=add;panel.Children.Add(addCard);
            Border listCard=Card();listCard.Margin=new Thickness(0,13,0,4);Grid.SetRow(listCard,1);Grid list=new Grid();list.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});list.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});list.Children.Add(SectionTitle("Your voyage list","One step at a time — Odysseus is keeping watch."));ScrollViewer todoScroll=SoftScroll(todoItems);todoScroll.Margin=new Thickness(0,11,0,0);todoScroll.Padding=new Thickness(0,0,3,0);Grid.SetRow(todoScroll,1);list.Children.Add(todoScroll);listCard.Child=list;panel.Children.Add(listCard);return panel;
        }

        FrameworkElement BuildNotesPage()
        {
            StackPanel panel=new StackPanel();Border notesCard=Card();StackPanel notes=new StackPanel();notes.Children.Add(SectionTitle("Captain’s notes","Capture thoughts and details you want to remember."));
            notesInput.Text=store.NotesText;notesInput.AcceptsReturn=true;notesInput.TextWrapping=TextWrapping.Wrap;notesInput.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;notesInput.MinHeight=185;Border field=Input(notesInput,"Write anything here…");field.CornerRadius=new CornerRadius(9);field.Margin=new Thickness(0,11,0,0);notes.Children.Add(field);
            Grid action=new Grid{Margin=new Thickness(0,11,0,0)};action.ColumnDefinitions.Add(new ColumnDefinition());action.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});notesStatus.Text="Saved only on this computer.";notesStatus.Foreground=Muted;notesStatus.FontSize=12;notesStatus.VerticalAlignment=VerticalAlignment.Center;action.Children.Add(notesStatus);
            Button save=PrimaryButton("Save note",Sage);save.Click+=delegate{store.SaveNotes(notesInput.Text);notesStatus.Text="Saved — your note is safe here.";};Grid.SetColumn(save,1);action.Children.Add(save);notes.Children.Add(action);notesCard.Child=notes;panel.Children.Add(notesCard);
            Border prompt=new Border{CornerRadius=new CornerRadius(10),Background=Brush("#F7E8DE"),BorderBrush=Brush("#D8BCAE"),BorderThickness=new Thickness(1),Padding=new Thickness(13),Margin=new Thickness(0,13,4,4),Effect=new DropShadowEffect{BlurRadius=6,ShadowDepth=2,Direction=315,Opacity=.14}};prompt.Child=new TextBlock{Text="A quiet mind travels farther. Write the loose thought down, then return to your day.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#7B5C51"),FontStyle=FontStyles.Italic};panel.Children.Add(prompt);return SoftScroll(panel);
        }

        void RefreshTodos()
        {
            todoItems.Children.Clear();if(store.Todos.Count==0){todoItems.Children.Add(Empty("Your list is clear — enjoy the breathing room."));return;}
            foreach(TodoItem item in store.Todos.ToList())
            {
                Border card=new Border{BorderBrush=Line,BorderThickness=new Thickness(0,0,0,2),Padding=new Thickness(2,8,0,9),Margin=new Thickness(0,0,0,3)};Grid row=new Grid{VerticalAlignment=VerticalAlignment.Center};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                TextBlock label=new TextBlock{Text=item.Text,Foreground=item.Done?Muted:Ink,FontSize=14,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};if(item.Done)label.TextDecorations=TextDecorations.Strikethrough;
                Button toggle=RoundButton("",Brushes.Transparent,item.Done?Muted:Ink,0);toggle.Content=label;toggle.BorderThickness=new Thickness(0);toggle.Padding=new Thickness(2,3,8,3);toggle.HorizontalContentAlignment=HorizontalAlignment.Left;toggle.ToolTip=item.Done?"Click to restore task":"Click to mark done";toggle.Click+=delegate{item.Done=!item.Done;store.SaveTodos();RefreshTodos();};row.Children.Add(toggle);
                Button remove=RoundButton("×",Brushes.Transparent,Muted,0);remove.BorderThickness=new Thickness(0);remove.Width=28;remove.Height=28;remove.Padding=new Thickness(0);remove.FontSize=18;remove.Opacity=.42;remove.ToolTip="Delete task";remove.MouseEnter+=delegate{remove.Opacity=.82;};remove.MouseLeave+=delegate{remove.Opacity=.42;};remove.Click+=delegate{store.RemoveTodo(item);RefreshTodos();};Grid.SetColumn(remove,1);row.Children.Add(remove);card.Child=row;todoItems.Children.Add(card);
            }
        }

        Button Tab(string label,int index)
        {
            Button b=RoundButton(label,Brushes.Transparent,Muted,10);b.Width=tabWidths[index];b.Padding=new Thickness(13,8,13,10);b.Margin=new Thickness(0);b.BorderThickness=new Thickness(0);b.FontSize=13;b.Click+=delegate{SelectTab(index,true);};tabs.Add(b);return b;
        }

        void SelectTab(int index,bool animate)
        {
            if(index<0||index>=pages.Count||index==activeTab)return;int previous=activeTab;int direction=previous<0?1:(index>previous?1:-1);AnimateIndicator(index,direction,animate);
            for(int i=0;i<tabs.Count;i++){tabs[i].Foreground=i==index?Navy:Muted;tabs[i].FontWeight=i==index?FontWeights.SemiBold:FontWeights.Normal;}
            FrameworkElement incoming=pages[index];incoming.Visibility=Visibility.Visible;Panel.SetZIndex(incoming,2);
            if(!animate||previous<0){for(int i=0;i<pages.Count;i++)pages[i].Visibility=i==index?Visibility.Visible:Visibility.Collapsed;ResetPage(index);activeTab=index;return;}
            FrameworkElement outgoing=pages[previous];Panel.SetZIndex(outgoing,1);TranslateTransform inSlide=pageSlides[index],outSlide=pageSlides[previous];ScaleTransform inScale=pageScales[index],outScale=pageScales[previous];
            incoming.Opacity=0;inSlide.X=direction*34;inSlide.Y=4;inScale.ScaleX=inScale.ScaleY=.985;outgoing.Opacity=1;outSlide.X=0;outSlide.Y=0;outScale.ScaleX=outScale.ScaleY=1;
            CubicEase settle=new CubicEase{EasingMode=EasingMode.EaseOut};QuadraticEase leave=new QuadraticEase{EasingMode=EasingMode.EaseIn};TimeSpan duration=TimeSpan.FromMilliseconds(290);
            DoubleAnimation arrive=new DoubleAnimation(0,1,duration){EasingFunction=settle};arrive.Completed+=delegate{outgoing.Visibility=Visibility.Collapsed;ResetPage(previous);ResetPage(index);};incoming.BeginAnimation(OpacityProperty,arrive);inSlide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(direction*34,0,duration){EasingFunction=settle});inSlide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(4,0,duration){EasingFunction=settle});inScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.985,1,duration){EasingFunction=settle});inScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.985,1,duration){EasingFunction=settle});
            outgoing.BeginAnimation(OpacityProperty,new DoubleAnimation(1,.12,duration){EasingFunction=leave});outSlide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,-direction*24,duration){EasingFunction=leave});outSlide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-2,duration){EasingFunction=leave});outScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,.975,duration){EasingFunction=leave});outScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.975,duration){EasingFunction=leave});activeTab=index;
        }

        void AnimateIndicator(int index,int direction,bool animate)
        {
            double targetX=0;for(int i=0;i<index;i++)targetX+=tabWidths[i];double targetWidth=tabIndicatorWidths[index];targetX+=(tabWidths[index]-targetWidth)/2;if(!animate||activeTab<0){indicatorSlide.X=targetX;tabIndicator.Width=targetWidth;return;}
            double fromX=indicatorSlide.X,fromWidth=tabIndicator.Width;DoubleAnimationUsingKeyFrames x=new DoubleAnimationUsingKeyFrames();x.KeyFrames.Add(new LinearDoubleKeyFrame(fromX,KeyTime.FromTimeSpan(TimeSpan.Zero)));x.KeyFrames.Add(new EasingDoubleKeyFrame(targetX+direction*2,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225)),new CubicEase{EasingMode=EasingMode.EaseOut}));x.KeyFrames.Add(new EasingDoubleKeyFrame(targetX,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(290)),new QuadraticEase{EasingMode=EasingMode.EaseInOut}));DoubleAnimationUsingKeyFrames width=new DoubleAnimationUsingKeyFrames();width.KeyFrames.Add(new LinearDoubleKeyFrame(fromWidth,KeyTime.FromTimeSpan(TimeSpan.Zero)));width.KeyFrames.Add(new EasingDoubleKeyFrame(targetWidth+3,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225)),new CubicEase{EasingMode=EasingMode.EaseOut}));width.KeyFrames.Add(new EasingDoubleKeyFrame(targetWidth,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(290)),new QuadraticEase{EasingMode=EasingMode.EaseInOut}));indicatorSlide.BeginAnimation(TranslateTransform.XProperty,x);tabIndicator.BeginAnimation(WidthProperty,width);
        }
        void ResetPage(int index){FrameworkElement page=pages[index];page.BeginAnimation(OpacityProperty,null);page.Opacity=1;TranslateTransform t=pageSlides[index];t.BeginAnimation(TranslateTransform.XProperty,null);t.BeginAnimation(TranslateTransform.YProperty,null);t.X=t.Y=0;ScaleTransform s=pageScales[index];s.BeginAnimation(ScaleTransform.ScaleXProperty,null);s.BeginAnimation(ScaleTransform.ScaleYProperty,null);s.ScaleX=s.ScaleY=1;}

        public void ShowAnimated()
        {
            dismissing=false;if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;if(!IsVisible)Show();outer.BeginAnimation(OpacityProperty,null);popupSlide.BeginAnimation(TranslateTransform.YProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);outer.Opacity=0;popupSlide.Y=20;popupScale.ScaleX=popupScale.ScaleY=.92;
            CubicEase ease=new CubicEase{EasingMode=EasingMode.EaseOut};outer.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(235)){EasingFunction=ease});DoubleAnimationUsingKeyFrames rise=new DoubleAnimationUsingKeyFrames();rise.KeyFrames.Add(new EasingDoubleKeyFrame(-2,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)),ease));rise.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),new QuadraticEase{EasingMode=EasingMode.EaseInOut}));popupSlide.BeginAnimation(TranslateTransform.YProperty,rise);DoubleAnimationUsingKeyFrames grow=new DoubleAnimationUsingKeyFrames();grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.012,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)),ease));grow.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),new QuadraticEase{EasingMode=EasingMode.EaseInOut}));bool cleaned=false;grow.Completed+=delegate{if(cleaned||dismissing)return;cleaned=true;outer.Opacity=1;popupSlide.Y=0;popupScale.ScaleX=popupScale.ScaleY=1;outer.BeginAnimation(OpacityProperty,null);popupSlide.BeginAnimation(TranslateTransform.YProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);};popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);Activate();
        }

        public void DismissAnimated()
        {
            if(!IsVisible||dismissing)return;dismissing=true;QuadraticEase ease=new QuadraticEase{EasingMode=EasingMode.EaseIn};DoubleAnimation fade=new DoubleAnimation(outer.Opacity,0,TimeSpan.FromMilliseconds(190)){EasingFunction=ease};fade.Completed+=delegate{if(!dismissing)return;Hide();outer.BeginAnimation(OpacityProperty,null);popupSlide.BeginAnimation(TranslateTransform.YProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);outer.Opacity=0;popupSlide.Y=20;popupScale.ScaleX=popupScale.ScaleY=.92;dismissing=false;};outer.BeginAnimation(OpacityProperty,fade);popupSlide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(popupSlide.Y,14,TimeSpan.FromMilliseconds(215)){EasingFunction=ease});popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(popupScale.ScaleX,.965,TimeSpan.FromMilliseconds(215)){EasingFunction=ease});popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(popupScale.ScaleY,.965,TimeSpan.FromMilliseconds(215)){EasingFunction=ease});
        }
        public bool IsPopupMotionActive{get{return outer.HasAnimatedProperties||popupSlide.HasAnimatedProperties||popupScale.HasAnimatedProperties;}}
        public void SelectTabForTest(int index){SelectTab(index,true);}
        public bool IsTabMotionActive{get{return indicatorSlide.HasAnimatedProperties||tabIndicator.HasAnimatedProperties||pages.Any(page=>page.HasAnimatedProperties)||pageSlides.Any(slide=>slide.HasAnimatedProperties)||pageScales.Any(scale=>scale.HasAnimatedProperties);}}

        static Border Card(){return new Border{Background=Paper,CornerRadius=new CornerRadius(12),Padding=new Thickness(14),BorderBrush=Brush("#A9B3B1"),BorderThickness=new Thickness(1),Effect=new DropShadowEffect{BlurRadius=8,ShadowDepth=3,Direction=315,Opacity=.16,Color=Colors.Black}};}
        static Border SoftItem(){return new Border{Background=Brush("#F3EBDD"),CornerRadius=new CornerRadius(10),BorderBrush=Line,BorderThickness=new Thickness(1),Padding=new Thickness(12,9,10,9),Margin=new Thickness(0,0,0,7)};}
        static TextBlock Empty(string text){return new TextBlock{Text=text,Foreground=Muted,FontSize=13,FontStyle=FontStyles.Italic,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,8,4,10)};}
        static StackPanel SectionTitle(string title,string helper){StackPanel p=new StackPanel();p.Children.Add(new TextBlock{Text=title,Foreground=Ink,FontSize=16,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text=helper,Foreground=Muted,FontSize=12,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap});return p;}
        static Border Input(TextBox box,string hint){box.Background=Brushes.Transparent;box.BorderThickness=new Thickness(0);box.Foreground=Ink;box.FontSize=13;box.FontFamily=new FontFamily("Cascadia Mono");box.FontWeight=FontWeights.Medium;box.Padding=new Thickness(9,7,9,7);box.ToolTip=hint;TextOptions.SetTextFormattingMode(box,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(box,TextRenderingMode.Auto);TextOptions.SetTextHintingMode(box,TextHintingMode.Fixed);return new Border{Background=Brushes.White,CornerRadius=new CornerRadius(9),BorderBrush=Brush("#A9B3B1"),BorderThickness=new Thickness(1),Child=box};}
        static Button PrimaryButton(string text,Brush color){Button b=RoundButton(text,color,Navy,9);b.Padding=new Thickness(13,8,13,8);b.FontWeight=FontWeights.Bold;b.BorderBrush=Brush("#C59A4E");b.BorderThickness=new Thickness(1);b.Effect=new DropShadowEffect{BlurRadius=6,ShadowDepth=2,Direction=315,Opacity=.2};return b;}
        static Button GhostButton(string text){Button b=RoundButton(text,Brushes.Transparent,Muted,8);b.Width=30;b.Height=30;b.FontSize=17;b.Padding=new Thickness(0);return b;}
        static Button RoundButton(string text,Brush background,Brush foreground,double radius)
        {
            ScaleTransform press=new ScaleTransform(1,1);Button b=new Button{Content=text,Background=background,Foreground=foreground,BorderBrush=Ink,BorderThickness=new Thickness(2),FontFamily=new FontFamily("Cascadia Mono"),FontWeight=FontWeights.SemiBold,Cursor=Cursors.Hand,FocusVisualStyle=null,RenderTransformOrigin=new Point(.5,.5),RenderTransform=press,SnapsToDevicePixels=true,UseLayoutRounding=true,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};TextOptions.SetTextFormattingMode(b,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(b,TextRenderingMode.Auto);TextOptions.SetTextHintingMode(b,TextHintingMode.Fixed);FrameworkElementFactory border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(radius));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Button.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Button.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Button.BorderThicknessProperty));FrameworkElementFactory cp=new FrameworkElementFactory(typeof(ContentPresenter));cp.SetValue(ContentPresenter.HorizontalAlignmentProperty,new TemplateBindingExtension(Button.HorizontalContentAlignmentProperty));cp.SetValue(ContentPresenter.VerticalAlignmentProperty,new TemplateBindingExtension(Button.VerticalContentAlignmentProperty));cp.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Button.PaddingProperty));border.AppendChild(cp);b.Template=new ControlTemplate(typeof(Button)){VisualTree=border};b.MouseEnter+=delegate{b.Opacity=.82;};b.MouseLeave+=delegate{b.Opacity=1;};b.PreviewMouseLeftButtonDown+=delegate{QuadraticEase ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};press.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(press.ScaleX,.93,TimeSpan.FromMilliseconds(85)){EasingFunction=ease});press.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(press.ScaleY,.93,TimeSpan.FromMilliseconds(85)){EasingFunction=ease});};b.PreviewMouseLeftButtonUp+=delegate{DoubleAnimationUsingKeyFrames spring=new DoubleAnimationUsingKeyFrames();spring.KeyFrames.Add(new EasingDoubleKeyFrame(1.025,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)),new CubicEase{EasingMode=EasingMode.EaseOut}));spring.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new QuadraticEase{EasingMode=EasingMode.EaseInOut}));bool reset=false;spring.Completed+=delegate{if(reset)return;reset=true;press.ScaleX=press.ScaleY=1;press.BeginAnimation(ScaleTransform.ScaleXProperty,null);press.BeginAnimation(ScaleTransform.ScaleYProperty,null);};press.BeginAnimation(ScaleTransform.ScaleXProperty,spring);press.BeginAnimation(ScaleTransform.ScaleYProperty,spring);};return b;
        }
        static ScrollViewer SoftScroll(UIElement content){ScrollViewer v=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};try{v.Resources[typeof(ScrollBar)]=XamlReader.Parse(ScrollbarStyle);}catch{}return v;}
        const string ScrollbarStyle=@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'><Setter Property='Width' Value='10'/><Setter Property='Background' Value='#E9E1D4'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'><Grid Background='#E9E1D4'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight='28'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#507769' BorderBrush='#8AA49A' BorderThickness='1' CornerRadius='5' Margin='1'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0'/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
        static SolidColorBrush Brush(string hex){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
        public void Capture(string output,int tabIndex=0){RefreshTodos();SelectTab(Math.Max(0,Math.Min(1,tabIndex)),false);outer.BeginAnimation(OpacityProperty,null);popupSlide.BeginAnimation(TranslateTransform.YProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);popupScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);outer.Opacity=1;popupSlide.Y=0;popupScale.ScaleX=popupScale.ScaleY=1;Measure(new Size(Width,Height));Arrange(new Rect(0,0,Width,Height));UpdateLayout();RenderTargetBitmap bitmap=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);bitmap.Render(this);PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream stream=File.Create(output))encoder.Save(stream);}
    }
}

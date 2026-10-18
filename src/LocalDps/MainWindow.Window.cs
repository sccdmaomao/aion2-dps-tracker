using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LocalDps;

public partial class MainWindow
{
    WindowPreferences windowPreferences=new();
    bool compactMode,windowPreferencesReady,persistWindowPreferences;
    double windowOpacity=1;
    bool opacityUpdateQueued;
    bool synchronizingOpacity;
    sealed record OpacityChoice(int Percent,string Label);
    readonly DispatcherTimer preferencesSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};

    void InitializeWindowPreferences(bool persist,WindowPreferences? initial)
    {
        persistWindowPreferences=persist;
        try{windowPreferences=(initial??(persist?WindowPreferences.Load():new())).Normalize();}
        catch(Exception ex){statusMessage="Window settings could not load: "+ex.Message;}
        Width=windowPreferences.Width;Height=windowPreferences.Height;
        SetWindowOpacity(windowPreferences.Opacity*100);
        if(windowPreferences.Left is double left&&windowPreferences.Top is double top)
        {
            WindowStartupLocation=WindowStartupLocation.Manual;Left=left;Top=top;
        }
        SetCompact(windowPreferences.Compact);
        windowPreferencesReady=true;
        SourceInitialized+=(_,_)=>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
            KeepWindowVisible();QueueWindowOpacity();
        };
        LocationChanged+=(_,_)=>ScheduleWindowPreferencesSave();
        SizeChanged+=(_,_)=>ScheduleWindowPreferencesSave();
        preferencesSaveTimer.Tick+=(_,_)=>{preferencesSaveTimer.Stop();SaveWindowPreferences();};
        Closing+=(_,_)=>{preferencesSaveTimer.Stop();SaveWindowPreferences();};
    }

    void CompactClick(object sender,RoutedEventArgs e)=>SetCompact(!compactMode);
    void SetCompact(bool compact)
    {
        if(compactMode==compact){Topmost=compact;return;}
        if(windowPreferencesReady)CaptureWindowPreferences();
        if(WindowState!=WindowState.Normal)WindowState=WindowState.Normal;
        compactMode=compact;
        Topmost=compact;
        NormalRoot.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        CompactRoot.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        WindowStyle=compact?WindowStyle.None:WindowStyle.SingleBorderWindow;
        ResizeMode=compact?ResizeMode.CanResizeWithGrip:ResizeMode.CanResize;
        MinWidth=compact?260:460;MinHeight=compact?180:520;
        Width=compact?windowPreferences.CompactWidth:windowPreferences.Width;
        Height=compact?windowPreferences.CompactHeight:windowPreferences.Height;
        KeepWindowVisible();
        ApplyWindowOpacity();
        ScheduleWindowPreferencesSave();
    }
    void CompactDrag(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton==MouseButton.Left&&e.ButtonState==MouseButtonState.Pressed)DragMove();
    }
    void CompactOpacityChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!synchronizingOpacity&&CompactOpacity.SelectedItem is OpacityChoice choice)SetWindowOpacity(choice.Percent);
    }
    void SynchronizeOpacityDropdown()
    {
        if(CompactOpacity is null)return;
        int percent=(int)Math.Round(windowOpacity*100);
        var choices=Enumerable.Range(3,8).Select(n=>n*10).Append(percent).Distinct().OrderDescending()
            .Select(n=>new OpacityChoice(n,$"{n}%")).ToArray();
        synchronizingOpacity=true;
        try
        {
            CompactOpacity.ItemsSource=choices;
            CompactOpacity.SelectedItem=choices.Single(c=>c.Percent==percent);
        }
        finally{synchronizingOpacity=false;}
    }
    void SetWindowOpacity(double percent)
    {
        windowOpacity=Math.Clamp(percent/100,.3,1);
        SynchronizeOpacityDropdown();
        ApplyWindowOpacity();
        ScheduleWindowPreferencesSave();
    }
    void ApplyWindowOpacity()
    {
        // Fade our whole desktop window, rather than WPF content against an
        // opaque HWND background. The game window is never modified.
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==IntPtr.Zero)return;
        const int extendedStyle=-20;
        const long layered=0x00080000;
        long style=GetWindowLongPtr(handle,extendedStyle).ToInt64();
        if(windowOpacity<1||(style&layered)!=0)
        {
            if((style&layered)==0)SetWindowLongPtr(handle,extendedStyle,new IntPtr(style|layered));
            if(!SetLayeredWindowAttributes(handle,0,(byte)Math.Round(windowOpacity*255),2))
                statusMessage="Window opacity could not be applied.";
        }
    }
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // HwndTarget removes WS_EX_LAYERED for opaque WPF HWNDs. Preserve our
        // constant-alpha style on this window; per-pixel transparency stays off.
        if(message==0x007C&&wParam.ToInt32()==-20&&windowOpacity<1)
        {
            var styles=Marshal.PtrToStructure<NativeStyles>(lParam);
            styles.New|=0x00080000;
            Marshal.StructureToPtr(styles,lParam,false);
            handled=true;
        }
        // WPF refreshes native styles during show, move, resize and mode changes.
        // Reapply our alpha after that work, without intercepting game input.
        if(message is 0x0047 or 0x007D or 0x0018)QueueWindowOpacity();
        return IntPtr.Zero;
    }
    void QueueWindowOpacity()
    {
        if(opacityUpdateQueued||Dispatcher.HasShutdownStarted)return;
        opacityUpdateQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>
        {
            try{if(IsVisible)ApplyWindowOpacity();}
            finally{opacityUpdateQueued=false;}
        });
    }
    void ScheduleWindowPreferencesSave()
    {
        if(!windowPreferencesReady||!persistWindowPreferences)return;
        preferencesSaveTimer.Stop();preferencesSaveTimer.Start();
    }
    void CaptureWindowPreferences()
    {
        Rect bounds=WindowState==WindowState.Normal?new(Left,Top,Width,Height):RestoreBounds;
        windowPreferences=windowPreferences with {Compact=compactMode,AlwaysOnTop=Topmost,Opacity=windowOpacity,Left=bounds.Left,Top=bounds.Top};
        windowPreferences=compactMode?windowPreferences with {CompactWidth=bounds.Width,CompactHeight=bounds.Height}
            :windowPreferences with {Width=bounds.Width,Height=bounds.Height};
    }
    void SaveWindowPreferences()
    {
        if(!windowPreferencesReady||!persistWindowPreferences)return;
        try{CaptureWindowPreferences();windowPreferences.Save();}
        catch(Exception ex){statusMessage="Window settings could not save: "+ex.Message;}
    }

    // Monitor work areas are in desktop pixels; convert using this HWND's DPI.
    // This also recovers a window whose saved monitor has been disconnected.
    void KeepWindowVisible()
    {
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==IntPtr.Zero||!GetWindowRect(handle,out var rect))return;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromRect(ref rect,2),ref info))return;
        var dpi=VisualTreeHelper.GetDpi(this);
        double left=info.Work.Left/dpi.DpiScaleX,top=info.Work.Top/dpi.DpiScaleY;
        double width=(info.Work.Right-info.Work.Left)/dpi.DpiScaleX,height=(info.Work.Bottom-info.Work.Top)/dpi.DpiScaleY;
        if(Width>width)Width=Math.Max(MinWidth,width);
        if(Height>height)Height=Math.Max(MinHeight,height);
        Left=Math.Clamp(double.IsFinite(Left)?Left:left,left,Math.Max(left,left+width-Width));
        Top=Math.Clamp(double.IsFinite(Top)?Top:top,top,Math.Max(top,top+height-Height));
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct NativeStyles {public int Old,New;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref NativeRect rect,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint color,byte alpha,uint flags);
}

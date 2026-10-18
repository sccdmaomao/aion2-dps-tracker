using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalDps.Capture;
using LocalDps.Protocol;
namespace LocalDps;
public partial class MainWindow : Window
{
    sealed record TargetChoice(int? EncounterId,int? TargetId,string Label);
    readonly EncounterTracker encounters=new();
    readonly Meter emptyMeter=new();
    EncounterTracker.Encounter? SelectedEncounter=>(TargetBox.SelectedItem as TargetChoice)?.EncounterId is int id?encounters.Encounters.FirstOrDefault(e=>e.Id==id)??encounters.Latest:encounters.Latest;
    Meter meter=>SelectedEncounter?.Meter??emptyMeter;
    readonly PacketCaptureService capture=new();
    readonly GameProtocolDecoder decoder=new();
    readonly IdentityRecordStream identityRecords=new();
    readonly HashSet<int> serverPorts=new();
    readonly PartyHitAttribution attribution;
    readonly CombatScope scope=new();
    readonly TargetIdentityDecoder targetIdentity=new();
    readonly TargetHealthDecoder targetHealth=new();
    readonly object gate=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    readonly bool persistKnownNames;
    long savedKnownNamesVersion;
    DateTimeOffset lastNamesSave;
    string nameCacheStatus="";
    string savedSelfName="";
    string profileSaveStatus="";
    DateTimeOffset lastProfileSaveAttempt;
    long chunks;
    bool autoCapturing,noPacketsWarned,connectionWaiting;
    DateTimeOffset captureStarted,lastConnectionCheck;
    string activeConnection="";
    string statusMessage="Enter Aion 2, then start capturing.";
    int shareFeedbackVersion;
    void UpdateCaptureButton(bool active)
    {
        string action=active?"Stop capturing":"Start capturing";
        StartIcon.Data=(Geometry)FindResource(active?"StopGeometry":"PlayGeometry");
        System.Windows.Automation.AutomationProperties.SetName(StartButton,action);
        StartButton.ToolTip=action+"\n"+statusMessage+nameCacheStatus+profileSaveStatus+(targetIdentity.CatalogWarning is {} warning?" · "+warning:"");
        CompactStartIcon.Data=StartIcon.Data;CompactStart.ToolTip=StartButton.ToolTip;
        System.Windows.Automation.AutomationProperties.SetName(CompactStart,action);
    }
    BitmapSource CreateChartSnapshot()
    {
        // Build an independent view so scrolling/selection and active capture
        // remain untouched, and every party row fits in the copied image.
        var rows=PlayersGrid.Items.Cast<PlayerRow>().ToArray();
        var columns=PlayersGrid.Columns.Where(c=>c.Visibility==Visibility.Visible).ToArray();
        // The full grid can be unmeasured when the app opens in compact mode.
        // Keep the copied chart readable and opaque regardless of overlay size.
        double ColumnWidth(DataGridColumn column)=>!compactMode&&column.ActualWidth>0?column.ActualWidth
            :column.Width.IsAbsolute?column.Width.Value:Math.Max(220,windowPreferences.Width-44-155);
        double width=Math.Max(400,columns.Sum(ColumnWidth));
        var table=new DataGrid
        {
            ItemsSource=rows,AutoGenerateColumns=false,IsReadOnly=true,
            CanUserAddRows=false,CanUserResizeColumns=false,CanUserReorderColumns=false,
            EnableRowVirtualization=false,EnableColumnVirtualization=false,
            HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,
            GridLinesVisibility=DataGridGridLinesVisibility.None,BorderThickness=new Thickness(0),
            Background=Brushes.Transparent,Foreground=Foreground,IsHitTestVisible=false,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility=ScrollBarVisibility.Disabled
        };
        table.ColumnHeaderStyle=(Style)FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        table.CellStyle=(Style)FindResource(typeof(DataGridCell));
        table.RowStyle=(Style)FindResource(typeof(DataGridRow));
        foreach(var column in columns)
        {
            DataGridColumn copy=column switch
            {
                DataGridTemplateColumn template=>new DataGridTemplateColumn{CellTemplate=template.CellTemplate},
                DataGridTextColumn text=>new DataGridTextColumn{Binding=text.Binding,ElementStyle=text.ElementStyle},
                _=>throw new InvalidOperationException("Unsupported chart column.")
            };
            copy.Header=column.Header;copy.Width=new DataGridLength(ColumnWidth(column));table.Columns.Add(copy);
        }
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=(TargetBox.SelectedItem as TargetChoice)?.Label??"All targets",FontSize=13,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(16,12,16,4)});
        panel.Children.Add(new TextBlock{Text=EncounterTime.Text,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(16,0,16,4)});
        panel.Children.Add(new TextBlock{Text=Totals.Text,FontSize=11,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(16,0,16,12)});
        panel.Children.Add(table);
        var surface=new Border{Width=width,Background=new SolidColorBrush(Color.FromRgb(20,29,44)),Child=panel,Resources=Resources};
        System.Windows.Documents.TextElement.SetForeground(surface,Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(surface,FontFamily);
        surface.Measure(new Size(width,double.PositiveInfinity));surface.Arrange(new Rect(new Point(),surface.DesiredSize));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth*2),(int)Math.Ceiling(surface.ActualHeight*2),192,192,PixelFormats.Pbgra32);
        // Fractional layout sizes can leave a transparent final pixel row/column.
        var background=new DrawingVisual();
        using(var drawing=background.RenderOpen())drawing.DrawRectangle(surface.Background,null,new Rect(-1,-1,bitmap.PixelWidth/2.0+2,bitmap.PixelHeight/2.0+2));
        bitmap.Render(background);
        bitmap.Render(surface);
        var opaque=new FormatConvertedBitmap(bitmap,PixelFormats.Bgr32,null,0);
        opaque.Freeze();return opaque;
    }
    async void CopyChartClick(object sender,RoutedEventArgs e)
    {
        int version=++shareFeedbackVersion;
        try
        {
            Render();Clipboard.SetImage(CreateChartSnapshot());
            ShareIcon.Data=CompactShareIcon.Data=(Geometry)FindResource("CopiedGeometry");ShareButton.ToolTip=CompactShare.ToolTip="Damage chart copied";
        }
        catch(Exception ex)
        {
            ShareIcon.Data=CompactShareIcon.Data=(Geometry)FindResource("CopyErrorGeometry");ShareButton.ToolTip=CompactShare.ToolTip="Could not copy chart: "+ex.Message;
        }
        await Task.Delay(2500);
        if(version!=shareFeedbackVersion)return;
        ShareIcon.Data=CompactShareIcon.Data=(Geometry)FindResource("ShareGeometry");ShareButton.ToolTip=CompactShare.ToolTip="Copy damage chart to clipboard";
    }
    public MainWindow(bool persistKnownNames=true,KnownCharacterName[]? rememberedNames=null,TrackingProfile? trackingProfile=null,WindowPreferences? windowPreferences=null,bool? persistWindowPreferences=null)
    {
        InitializeComponent();
        this.persistKnownNames=persistKnownNames;
        if(rememberedNames is not null)scope.Party.RestoreKnownNames(rememberedNames);
        if(persistKnownNames)try{scope.Party.RestoreKnownNames(PartyNameStore.Load(Path.Combine(AppContext.BaseDirectory,"learned-character-names.json")));var saved=PartyNameStore.Load();scope.Party.RestoreKnownNames(saved);var known=scope.Party.KnownNames;savedKnownNamesVersion=known.Length==saved.Length&&known.All(n=>saved.Contains(n))?scope.Party.KnownNamesVersion:-1;}catch(Exception ex){nameCacheStatus=" · name cache: "+ex.Message;}
        try{var profile=trackingProfile??TrackingProfile.Load();profile.Apply(scope);savedSelfName=profile.SelfName;}catch(Exception ex){statusMessage="Character name could not load: "+ex.Message;}
        attribution=new(scope,encounters.Add);
        decoder.IncludeHit=SkillRules.LoadBundled().Include;
        encounters.DisplayName=scope.Label;
        encounters.DescribePlayers=rows=>scope.DisplayRows(rows);
        encounters.TargetName=ResolveTarget;
        identityRecords.RecordObserved+=scope.Observe;
        identityRecords.RecordObserved+=targetIdentity.Observe;
        identityRecords.RecordObserved+=targetHealth.Observe;
        decoder.HitDecoded+=attribution.Add;
        capture.StreamChunk+=AcceptChunk;
        capture.Status+=message=>Dispatcher.BeginInvoke(()=>{if(message.StartsWith("Capture stopped:")||message.StartsWith("TCP gap"))statusMessage=message;});
        timer.Tick+=(_,_)=>{Render();CheckAutomatic();SaveKnownNames();SaveDetectedCharacter();}; timer.Start();
        Closed+=(_,_)=>{timer.Stop();capture.Dispose();SaveKnownNames(true);};
        InitializeWindowPreferences(persistWindowPreferences??persistKnownNames,windowPreferences);
    }
    void SaveDetectedCharacter()
    {
        if(!persistKnownNames||!autoCapturing)return;
        if(DateTimeOffset.UtcNow-lastProfileSaveAttempt<TimeSpan.FromSeconds(5))return;
        string? detected;
        lock(gate)detected=scope.Identity.LocalPlayerId is int id?scope.Identity.Names.GetValueOrDefault(id):null;
        if(string.IsNullOrEmpty(detected))detected=savedSelfName;
        if(string.IsNullOrEmpty(detected)||(detected==savedSelfName&&File.Exists(TrackingProfile.FilePath)))return;
        lastProfileSaveAttempt=DateTimeOffset.UtcNow;
        try{TrackingProfile.Create(detected).Save();savedSelfName=detected;profileSaveStatus="";}
        catch(Exception ex){profileSaveStatus=" · character name could not be saved: "+ex.Message;}
    }
    void RenderCharacter()
    {
        string? detected=scope.Identity.LocalPlayerId is int id?scope.Identity.Names.GetValueOrDefault(id):null;
        string? name=detected??scope.ConfirmedSelfName;
        bool hasName=!string.IsNullOrWhiteSpace(name);
        CharacterNameText.Text=detected is not null?$"{detected} · detected automatically":hasName?$"{name} · waiting for automatic confirmation":"Detect your character automatically";
        CharacterHelp.Visibility=detected is null?Visibility.Visible:Visibility.Collapsed;
        CharacterHelp.Text=autoCapturing?"Teleport once while capture is running to detect your character name.":"Start capturing, then teleport once to detect your character name.";
    }
    void SaveKnownNames(bool force=false)
    {
        if(!persistKnownNames||(!force&&DateTimeOffset.UtcNow-lastNamesSave<TimeSpan.FromSeconds(5)))return;
        KnownCharacterName[] names;long version;
        lock(gate){version=scope.Party.KnownNamesVersion;if(version==savedKnownNamesVersion)return;names=scope.Party.KnownNames;}
        lastNamesSave=DateTimeOffset.UtcNow;
        try{PartyNameStore.Save(names);savedKnownNamesVersion=version;nameCacheStatus="";}
        catch(Exception ex){nameCacheStatus=" · name cache: "+ex.Message;}
    }
    void AcceptChunk(byte[] data,DateTimeOffset time,string flow){lock(gate){encounters.AdvanceTo(time.ToUnixTimeMilliseconds());scope.BeginChunk();try{if(GameFlowDirection.IsServerToClient(flow,serverPorts))identityRecords.Feed(flow,data,time.ToUnixTimeMilliseconds());decoder.Feed(flow,data,time.ToUnixTimeMilliseconds());attribution.Flush(time.ToUnixTimeMilliseconds());chunks++;}finally{scope.EndChunk();}}}
    void StopAutomatic(){autoCapturing=false;capture.Stop();lock(gate)encounters.Stop();UpdateCaptureButton(false);activeConnection="";}
    void DetectAndCapture()
    {
        var connections=GameConnectionDetection.Find();var devices=PacketCaptureService.ListDevices();
        var device=AdapterSelection.ForGame(devices,connections)??throw new InvalidOperationException("No active capture adapter was found. Check your network connection and Npcap installation.");
        int[] ports=connections.Select(c=>c.RemotePort).Distinct().Order().ToArray();
        string signature=device.Name+":"+string.Join(",",ports);
        if(signature==activeConnection&&capture.IsRunning){connectionWaiting=false;return;}
        capture.Stop();lock(gate){decoder.Reset();identityRecords.Reset();scope.Reset();targetIdentity.Reset();targetHealth.Reset();encounters.Reset();encounters.Start();attribution.Reset();serverPorts.Clear();serverPorts.UnionWith(ports);}
        capture.StartGamePorts(device.Name,ports);activeConnection=signature;captureStarted=DateTimeOffset.UtcNow;noPacketsWarned=false;connectionWaiting=false;
        statusMessage="Capturing. Waiting for combat…";
    }
    void CheckAutomatic()
    {
        if(!autoCapturing)return;var now=DateTimeOffset.UtcNow;
        if(now-lastConnectionCheck>TimeSpan.FromSeconds(3))
        {
            lastConnectionCheck=now;
            try{DetectAndCapture();}
            catch(Exception ex){connectionWaiting=true;statusMessage="Waiting for the game connection: "+ex.Message;return;}
        }
        if(connectionWaiting)return;
        if(!noPacketsWarned&&capture.PacketsReceived==0&&now-captureStarted>TimeSpan.FromSeconds(5))
        {noPacketsWarned=true;statusMessage="No game packets received yet. Checking the game connection automatically; if this persists, verify Npcap can access the adapter.";}
        if(capture.PacketsReceived>0){lock(gate)statusMessage=scope.LocalPlayerId is null?$"Capturing · waiting for your identity · {scope.AllowedActors().Count} verified teammate(s) tracked":meter.TotalHits>0?$"Capturing · {meter.TotalHits:N0} hits from you and confirmed party members":"Capturing. No hits from you or confirmed party members.";}
    }
    void StartClick(object sender,RoutedEventArgs e)
    {
        try
        {
            if(autoCapturing){StopAutomatic();statusMessage="Stopped. Your encounter is available to review or copy.";return;}
            capture.Stop();Reset(true);DetectAndCapture();autoCapturing=true;lastConnectionCheck=DateTimeOffset.UtcNow;UpdateCaptureButton(true);
        }
        catch(Exception ex){autoCapturing=false;UpdateCaptureButton(false);statusMessage="Cannot start: "+ex.Message;}
    }
    void Reset(bool clearIdentity=false){lock(gate){encounters.Reset();if(autoCapturing)encounters.Start();attribution.Reset();decoder.Reset();identityRecords.Reset();if(clearIdentity){scope.Reset();targetIdentity.Reset();targetHealth.Reset();serverPorts.Clear();}chunks=0;}Render();}
    string ResolveTarget(int id)=>scope.Identity.Names.TryGetValue(id,out string? name)?name:targetIdentity.Resolve(id)??$"Unknown target {id}";
    TargetDeath? DeathFor(int id)=>meter.TargetTimes(id) is {} times?targetHealth.FindDeath(id,times.First,times.Last):null;
    // Only verified hits enter encounters; later roster changes must not filter history.
    MeterView CurrentView(int? targetId)=>encounters.View(SelectedEncounter,targetId);
    void UpdateTargets()
    {
        var items=new List<TargetChoice>{new(null,null,"Latest fight · All targets")};
        foreach(var fight in encounters.Encounters.Reverse())
        {
            items.Add(new(fight.Id,null,$"All targets · Fight {fight.Id}"));
            var names=fight.Meter.TargetIds.ToDictionary(id=>id,ResolveTarget);
            foreach(var (id,name) in names)
            {
                string label=names.Values.Count(n=>n==name)>1?$"{name} · {id}":name;
                bool defeated=fight.Meter.TargetTimes(id) is {} times&&targetHealth.FindDeath(id,times.First,times.Last) is not null;
                items.Add(new(fight.Id,id,$"{label} · Fight {fight.Id}"+(defeated?" · defeated":"")));
            }
        }
        if(TargetBox.ItemsSource is TargetChoice[] old&&old.SequenceEqual(items))return;
        var selected=TargetBox.SelectedItem as TargetChoice;
        TargetBox.ItemsSource=items.ToArray();
        TargetBox.SelectedItem=items.FirstOrDefault(x=>x.EncounterId==selected?.EncounterId&&x.TargetId==selected?.TargetId)??items[0];
    }
    void Render()
    {
        lock(gate)
        {
            RenderCharacter();UpdateCaptureButton(autoCapturing);
            if(autoCapturing)encounters.AdvanceTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            UpdateTargets();
            var view=CurrentView((TargetBox.SelectedItem as TargetChoice)?.TargetId);
            PlayersGrid.ItemsSource=scope.DisplayRows(view.Players);CompactPlayers.ItemsSource=PlayersGrid.ItemsSource;
            Totals.Text=$"{view.Damage:N0} damage · {view.Damage/Math.Max(1,view.Seconds):N1} DPS · {view.Hits:N0} hits";
            EncounterTime.Text=SelectedEncounter is {} fight?$"{encounters.State(fight)} · {TimeSpan.FromSeconds(view.Seconds).ToString(@"hh\:mm\:ss")}":"Waiting for combat · 00:00:00";
        }
    }
    public object CheckRecording(string path,int actor,int target)
    {
        Reset(true);RecordedStreamReplay.Read(path,AcceptChunk);Render();
        TargetBox.SelectedItem=((TargetChoice[])TargetBox.ItemsSource).FirstOrDefault(c=>c.EncounterId==encounters.Latest?.Id&&c.TargetId==target)??TargetBox.Items[0];Render();
        var rows=PlayersGrid.Items.Cast<PlayerRow>().ToArray();
        return new{rows=rows.Length,players=rows,hits=rows.Sum(r=>r.Hits),damage=rows.Sum(r=>r.Damage),displayed=Totals.Text,characterDisplay=CharacterNameText.Text,confirmedSelfName=scope.ConfirmedSelfName,encounter=EncounterInfo(),observedDeaths=targetHealth.Deaths,targetDeaths=meter.TargetIds.Select(DeathFor).Where(d=>d is not null).ToArray(),encounterCount=encounters.Encounters.Count,encounterHistory=encounters.Encounters.Select(e=>new {id=e.Id,damage=e.Meter.TotalDamage,hits=e.Meter.TotalHits,seconds=encounters.Seconds(e),state=encounters.State(e)}).ToArray(),tcpChunks=chunks,localPlayerId=scope.LocalPlayerId,selfProof=scope.SelfProof,observedNames=scope.Identity.Names,scopeStatus=scope.Status,outsideScopeHits=scope.ExcludedHits,identityFrames=identityRecords.RecordsFramed,identityBundles=identityRecords.BundlesDecoded,identityRejected=identityRecords.RejectedRecords,partyCandidateCount=scope.Party.CandidateSnapshotsSeen,partyPeriodicUpdates=scope.Party.PeriodicUpdatesSeen,partyRecoveredAtStartup=scope.Party.RecoveredAtStartup,partyAuthoritative=scope.Party.IsAuthoritative,partyMembers=scope.Party.Members,partyProof=scope.Party.Proof,targets=meter.Targets(elapsedSeconds:SelectedEncounter is {} selected?encounters.Seconds(selected):0),observedTargetNames=targetIdentity.Names,recommendedAdapter=AdapterSelection.Recommend(PacketCaptureService.ListDevices())?.Description};
    }
    object? EncounterInfo()=>SelectedEncounter is {} fight?new {id=fight.Id,startUnixMs=fight.FirstMs,endUnixMs=encounters.EndMs(fight),lastDamageUnixMs=fight.LastMs,durationSeconds=encounters.Seconds(fight),state=encounters.State(fight),idleTimeoutSeconds=EncounterTracker.IdleTimeoutMs/1000}:null;
}

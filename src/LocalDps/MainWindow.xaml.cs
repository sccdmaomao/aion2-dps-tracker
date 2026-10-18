using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
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
    readonly DiagnosticRecording diagnostics=new();
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
    bool demo;
    long chunks;
    bool replaying;
    bool autoCapturing,noPacketsWarned,connectionWaiting;
    DateTimeOffset captureStarted,lastConnectionCheck;
    string activeConnection="";
    int shareFeedbackVersion;
    void UpdateCaptureButton(bool active)
    {
        string action=active?"Stop capturing":"Start capturing";
        StartIcon.Data=(Geometry)FindResource(active?"StopGeometry":"PlayGeometry");
        System.Windows.Automation.AutomationProperties.SetName(StartButton,action);
        StartButton.ToolTip=action+"\n"+StatusText.Text;
    }
    BitmapSource CreateChartSnapshot()
    {
        // Build an independent view so scrolling/selection and active capture
        // remain untouched, and every party row fits in the copied image.
        var rows=PlayersGrid.Items.Cast<PlayerRow>().ToArray();
        var columns=PlayersGrid.Columns.Where(c=>c.Visibility==Visibility.Visible).ToArray();
        double width=Math.Max(400,columns.Sum(c=>c.ActualWidth));
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
            copy.Header=column.Header;copy.Width=new DataGridLength(Math.Max(1,column.ActualWidth));table.Columns.Add(copy);
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
        bitmap.Render(surface);bitmap.Freeze();return bitmap;
    }
    async void CopyChartClick(object sender,RoutedEventArgs e)
    {
        int version=++shareFeedbackVersion;
        try
        {
            Render();Clipboard.SetImage(CreateChartSnapshot());
            ShareIcon.Data=(Geometry)FindResource("CopiedGeometry");ShareButton.ToolTip="Damage chart copied";
        }
        catch(Exception ex)
        {
            ShareIcon.Data=(Geometry)FindResource("CopyErrorGeometry");ShareButton.ToolTip="Could not copy chart: "+ex.Message;
        }
        await Task.Delay(2500);
        if(version!=shareFeedbackVersion)return;
        ShareIcon.Data=(Geometry)FindResource("ShareGeometry");ShareButton.ToolTip="Copy damage chart to clipboard";
    }
    public MainWindow(bool persistKnownNames=true,KnownCharacterName[]? rememberedNames=null,TrackingProfile? trackingProfile=null)
    {
        InitializeComponent();
        this.persistKnownNames=persistKnownNames;
        if(rememberedNames is not null)scope.Party.RestoreKnownNames(rememberedNames);
        if(persistKnownNames)try{scope.Party.RestoreKnownNames(PartyNameStore.Load(Path.Combine(AppContext.BaseDirectory,"learned-character-names.json")));var saved=PartyNameStore.Load();scope.Party.RestoreKnownNames(saved);var known=scope.Party.KnownNames;savedKnownNamesVersion=known.Length==saved.Length&&known.All(n=>saved.Contains(n))?scope.Party.KnownNamesVersion:-1;}catch(Exception ex){nameCacheStatus=" · name cache: "+ex.Message;}
        try{var profile=trackingProfile??TrackingProfile.Load();profile.Apply(scope);savedSelfName=profile.SelfName;}catch(Exception ex){StatusText.Text="Character name could not load: "+ex.Message;}
        attribution=new(scope,encounters.Add);
        decoder.IncludeHit=SkillRules.LoadBundled().Include;
        encounters.DisplayName=scope.Label;
        encounters.DescribePlayers=rows=>scope.DisplayRows(rows);
        encounters.TargetName=ResolveTarget;
        identityRecords.RecordObserved+=scope.Observe;
        identityRecords.RecordObserved+=targetIdentity.Observe;
        identityRecords.RecordObserved+=targetHealth.Observe;
        decoder.HitDecoded+=attribution.Add;
        FramingBox.ItemsSource=new[]{"Legacy combat","Width-aware"};FramingBox.SelectedIndex=0;
        FramingBox.SelectionChanged+=(_,_)=>
        {
            if(replaying){FramingBox.IsEnabled=false;return;}
            StopAutomatic();Reset();decoder.Framing=FramingBox.SelectedIndex==0?GameFraming.LegacyCombat:GameFraming.WidthAware;
            StatusText.Text="Framing changed. Encounter reset; start a new capture or replay.";
        };
        capture.StreamChunk+=AcceptChunk;
        capture.Status+=message=>Dispatcher.BeginInvoke(()=>{if(message.StartsWith("Capture stopped:")||message.StartsWith("TCP gap"))StatusText.Text=message;});
        timer.Tick+=(_,_)=>{Render();CheckAutomatic();SaveKnownNames();SaveDetectedCharacter();}; timer.Start();
        Closed+=(_,_)=>{timer.Stop();capture.Dispose();SaveKnownNames(true);};
    }
    void SaveDetectedCharacter()
    {
        if(!persistKnownNames||!autoCapturing||replaying||demo)return;
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
    void AcceptChunk(byte[] data,DateTimeOffset time,string flow){lock(gate){encounters.AdvanceTo(time.ToUnixTimeMilliseconds());diagnostics.Add(flow,time.ToUnixTimeMilliseconds(),data);scope.BeginChunk();try{if(GameFlowDirection.IsServerToClient(flow,serverPorts))identityRecords.Feed(flow,data,time.ToUnixTimeMilliseconds());decoder.Feed(flow,data,time.ToUnixTimeMilliseconds());attribution.Flush(time.ToUnixTimeMilliseconds());chunks++;}finally{scope.EndChunk();}}}
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
        AutoDetails.Text=$"Adapter: {device.Description}. Game server port(s): {string.Join(", ",connections.Select(c=>c.RemotePort).Distinct())}. Game service filters: {ports.Length}.";
        StatusText.Text="Capturing. Waiting for combat…";
    }
    void CheckAutomatic()
    {
        if(!autoCapturing)return;var now=DateTimeOffset.UtcNow;
        if(now-lastConnectionCheck>TimeSpan.FromSeconds(3))
        {
            lastConnectionCheck=now;
            try{DetectAndCapture();}
            catch(Exception ex){connectionWaiting=true;StatusText.Text="Waiting for the game connection: "+ex.Message;return;}
        }
        if(connectionWaiting)return;
        if(!noPacketsWarned&&capture.PacketsReceived==0&&now-captureStarted>TimeSpan.FromSeconds(5))
        {noPacketsWarned=true;StatusText.Text="No game packets received yet. Checking the game connection automatically; if this persists, verify Npcap can access the adapter.";}
        if(capture.PacketsReceived>0){lock(gate)StatusText.Text=scope.LocalPlayerId is null?$"Capturing · waiting for your identity · {scope.AllowedActors().Count} verified teammate(s) tracked":meter.TotalHits>0?$"Capturing · {meter.TotalHits:N0} hits from you and confirmed party members":"Capturing. No hits from you or confirmed party members.";}
    }
    void StartClick(object sender,RoutedEventArgs e)
    {
        try
        {
            if(replaying) throw new InvalidOperationException("Wait for the current replay to finish.");
            if(autoCapturing){StopAutomatic();StatusText.Text="Stopped. Your encounter is available to save.";return;}
            capture.Stop();Reset(true);DetectAndCapture();autoCapturing=true;lastConnectionCheck=DateTimeOffset.UtcNow;UpdateCaptureButton(true);
        }
        catch(Exception ex){autoCapturing=false;UpdateCaptureButton(false);StatusText.Text="Cannot start: "+ex.Message;}
    }
    async void ReplayClick(object sender,RoutedEventArgs e)
    {
        if(replaying)return;
        var dialog=new OpenFileDialog{Filter="Local recordings|*.pcap;*.cap;*.ndjson|All files|*.*"};
        if(dialog.ShowDialog()!=true)return;
        StopAutomatic();Reset(true);
        replaying=true;
        FramingBox.IsEnabled=false;
        try{await Task.Run(()=>{if(Path.GetExtension(dialog.FileName).Equals(".ndjson",StringComparison.OrdinalIgnoreCase))RecordedStreamReplay.Read(dialog.FileName,AcceptChunk);else capture.ReplayPcap(dialog.FileName);});StatusText.Text="Replay finished. Compare decoded totals with a known encounter to validate accuracy.";}
        catch(Exception ex){StatusText.Text="Replay failed: "+ex.Message;}
        finally{replaying=false;FramingBox.IsEnabled=true;}
    }
    void Reset(bool clearIdentity=false){lock(gate){encounters.Reset();if(autoCapturing)encounters.Start();attribution.Reset();decoder.Reset();identityRecords.Reset();if(clearIdentity){diagnostics.Reset();scope.Reset();targetIdentity.Reset();targetHealth.Reset();serverPorts.Clear();}chunks=0;demo=false;}Render();}
    void ResetClick(object sender,RoutedEventArgs e){if(replaying)return;Reset();StatusText.Text=autoCapturing?"Encounter history cleared. Capturing…":"Encounter history cleared. Click Start capturing to begin.";}
    void DemoClick(object sender,RoutedEventArgs e)
    {
        if(replaying)return;
        StopAutomatic();Reset();lock(gate){demo=true;for(int i=0;i<20;i++)encounters.Add(new CombatHit(i*1000,100+i%3,900,200+i%4,1000+i*137,false,i));encounters.AdvanceTo(34000);}
        StatusText.Text="DEMO DATA — synthetic hits to preview the display; this does not validate the game decoder.";Render();
    }
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
    void Render(){lock(gate){RenderCharacter();UpdateCaptureButton(autoCapturing);if(autoCapturing)encounters.AdvanceTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());var allowed=scope.AllowedActors();UpdateTargets();var view=CurrentView((TargetBox.SelectedItem as TargetChoice)?.TargetId);PlayersGrid.ItemsSource=demo?view.Players:scope.DisplayRows(view.Players);IdentityStatus.Text=demo?"Demo: synthetic data only":scope.Status;Totals.Text=$"{(demo?"DEMO · ":"")}{view.Damage:N0} damage · {view.Damage/Math.Max(1,view.Seconds):N1} DPS · {view.Hits:N0} hits";EncounterTime.Text=SelectedEncounter is {} fight?$"{encounters.State(fight)} · {TimeSpan.FromSeconds(view.Seconds).ToString(@"hh\:mm\:ss")}":"Waiting for combat · 00:00:00";DiagnosticsText.Text=$"Packets {capture.PacketsReceived:N0} · TCP chunks {chunks:N0} · frames {decoder.RecordsFramed:N0} · awaiting identity {attribution.PendingCount:N0} · party updates {scope.Party.PeriodicUpdatesSeen:N0} · names {scope.Identity.Names.Count} · outside-scope hits {scope.ExcludedHits:N0} · malformed {decoder.RejectedRecords:N0} · dropped {capture.DroppedFrames:N0}{nameCacheStatus}{profileSaveStatus}{(targetIdentity.CatalogWarning is {} warning?" · "+warning:"")}";EntityNamesText.Text=string.Join(Environment.NewLine,scope.Identity.Names.OrderBy(p=>p.Value).Take(64).Select(p=>$"{p.Value} · ID {p.Key} · {(allowed.Contains(p.Key)?p.Key==scope.LocalPlayerId?"you":"party":"excluded")} "));}}
    void TopChanged(object sender,RoutedEventArgs e)=>Topmost=OnTop.IsChecked==true;
    void DetailsChanged(object sender,RoutedEventArgs e)
    {
        if(DamageColumn is null)return;
        var visibility=((System.Windows.Controls.CheckBox)sender).IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        DamageColumn.Visibility=HitsColumn.Visibility=ShareColumn.Visibility=visibility;
    }
    void SaveDiagnosticsClick(object sender,RoutedEventArgs e)
    {
        string[] lines;lock(gate)lines=diagnostics.Lines();
        if(lines.Length==0){StatusText.Text="Start capturing first; no game traffic is available to save.";return;}
        var dialog=new SaveFileDialog{Filter="Local diagnostic recording|*.ndjson",FileName="LocalDpsTracker-diagnostic-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".ndjson",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(dialog.ShowDialog()!=true)return;
        try{File.WriteAllLines(dialog.FileName,lines);StatusText.Text="Saved local game diagnostic: "+dialog.FileName;}
        catch(Exception ex){StatusText.Text="Diagnostic save failed: "+ex.Message;}
    }
    public object CheckRecording(string path,int actor,int target)
    {
        Reset(true);RecordedStreamReplay.Read(path,AcceptChunk);Render();
        TargetBox.SelectedItem=((TargetChoice[])TargetBox.ItemsSource).FirstOrDefault(c=>c.EncounterId==encounters.Latest?.Id&&c.TargetId==target)??TargetBox.Items[0];Render();
        var rows=PlayersGrid.Items.Cast<PlayerRow>().ToArray();
        return new{rows=rows.Length,players=rows,hits=rows.Sum(r=>r.Hits),damage=rows.Sum(r=>r.Damage),displayed=Totals.Text,characterDisplay=CharacterNameText.Text,confirmedSelfName=scope.ConfirmedSelfName,encounter=EncounterInfo(),observedDeaths=targetHealth.Deaths,targetDeaths=meter.TargetIds.Select(DeathFor).Where(d=>d is not null).ToArray(),encounterCount=encounters.Encounters.Count,encounterHistory=encounters.Encounters.Select(e=>new {id=e.Id,damage=e.Meter.TotalDamage,hits=e.Meter.TotalHits,seconds=encounters.Seconds(e),state=encounters.State(e)}).ToArray(),tcpChunks=chunks,localPlayerId=scope.LocalPlayerId,selfProof=scope.SelfProof,observedNames=scope.Identity.Names,scopeStatus=scope.Status,outsideScopeHits=scope.ExcludedHits,identityFrames=identityRecords.RecordsFramed,identityBundles=identityRecords.BundlesDecoded,identityRejected=identityRecords.RejectedRecords,partyCandidateCount=scope.Party.CandidateSnapshotsSeen,partyPeriodicUpdates=scope.Party.PeriodicUpdatesSeen,partyRecoveredAtStartup=scope.Party.RecoveredAtStartup,partyAuthoritative=scope.Party.IsAuthoritative,partyMembers=scope.Party.Members,partyProof=scope.Party.Proof,targets=meter.Targets(elapsedSeconds:SelectedEncounter is {} selected?encounters.Seconds(selected):0),observedTargetNames=targetIdentity.Names,recommendedAdapter=AdapterSelection.Recommend(PacketCaptureService.ListDevices())?.Description};
    }
    object? EncounterInfo()=>SelectedEncounter is {} fight?new {id=fight.Id,startUnixMs=fight.FirstMs,endUnixMs=encounters.EndMs(fight),lastDamageUnixMs=fight.LastMs,durationSeconds=encounters.Seconds(fight),state=encounters.State(fight),idleTimeoutSeconds=EncounterTracker.IdleTimeoutMs/1000}:null;
    void SaveClick(object sender,RoutedEventArgs e)
    {
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDps","encounters");
        try
        {
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
            lock(gate)
            {
                int? targetId=(TargetBox.SelectedItem as TargetChoice)?.TargetId;
                var view=CurrentView(targetId);
                File.WriteAllText(path,JsonSerializer.Serialize(new {schemaVersion=8,demo,encounter=EncounterInfo(),targetDeaths=meter.TargetIds.Select(DeathFor).Where(d=>d is not null).ToArray(),tracking=new{selfName=scope.ConfirmedSelfName,partyMembers=scope.Party.Members,localPlayerId=scope.LocalPlayerId,selfProof=scope.SelfProof,membershipSource=scope.Party.IsAuthoritative?"Decoded active roster":"Self only; awaiting roster",partyProof=scope.Party.Proof},scope=new{targetId},firstHitUnixMs=view.FirstMs,lastHitUnixMs=view.LastMs,durationSeconds=view.Seconds,totalDps=view.Damage/Math.Max(1,view.Seconds),totalDamage=view.Damage,trackedDamageAllTargets=meter.TotalDamage,framing=decoder.Framing.ToString(),diagnostics=new {packets=capture.PacketsReceived,tcpChunks=chunks,framedRecords=decoder.RecordsFramed,bundles=decoder.BundlesDecoded,rejected=decoder.RejectedRecords,excluded=decoder.FilteredHits,dropped=capture.DroppedFrames,opcodes=decoder.OpcodeCounts,rejectionReasons=decoder.RejectionReasons,ignoredNearbyHits=scope.ExcludedHits},players=demo?view.Players:scope.DisplayRows(view.Players),skills=view.Skills,targets=meter.Targets(elapsedSeconds:SelectedEncounter is {} selected?encounters.Seconds(selected):0)},new JsonSerializerOptions{WriteIndented=true}));
            }
            StatusText.Text="Saved locally: "+path;
        }catch(Exception ex){StatusText.Text="Save failed: "+ex.Message;}
    }
}

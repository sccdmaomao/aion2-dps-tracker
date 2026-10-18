using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using LocalDps.Capture;
using LocalDps.Protocol;
namespace LocalDps;
public partial class MainWindow : Window
{
    sealed record FilterChoice(int? Id,string Label);
    readonly EncounterTracker encounters=new();
    readonly Meter emptyMeter=new();
    EncounterTracker.Encounter? SelectedEncounter=>(EncounterBox.SelectedItem as FilterChoice)?.Id is int id?encounters.Encounters.FirstOrDefault(e=>e.Id==id)??encounters.Latest:encounters.Latest;
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
    bool editingCharacter;
    string savedSelfName="";
    string profileSaveStatus="";
    DateTimeOffset lastProfileSaveAttempt;
    bool demo;
    long chunks;
    bool replaying;
    bool autoCapturing,noPacketsWarned,connectionWaiting;
    DateTimeOffset captureStarted,lastConnectionCheck;
    string activeConnection="";
    public MainWindow(bool persistKnownNames=true,KnownCharacterName[]? rememberedNames=null,TrackingProfile? trackingProfile=null)
    {
        InitializeComponent();
        this.persistKnownNames=persistKnownNames;
        if(rememberedNames is not null)scope.Party.RestoreKnownNames(rememberedNames);
        if(persistKnownNames)try{scope.Party.RestoreKnownNames(PartyNameStore.Load(Path.Combine(AppContext.BaseDirectory,"learned-character-names.json")));var saved=PartyNameStore.Load();scope.Party.RestoreKnownNames(saved);var known=scope.Party.KnownNames;savedKnownNamesVersion=known.Length==saved.Length&&known.All(n=>saved.Contains(n))?scope.Party.KnownNamesVersion:-1;}catch(Exception ex){nameCacheStatus=" · name cache: "+ex.Message;}
        try{var profile=trackingProfile??TrackingProfile.Load();profile.Apply(scope);SelfNameBox.Text=savedSelfName=profile.SelfName;}catch(Exception ex){StatusText.Text="Character name could not load: "+ex.Message;}
        SelfNameBox.GotKeyboardFocus+=(_,_)=>editingCharacter=true;
        SelfNameBox.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){ApplyPeopleClick(SelfNameBox,e);e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Escape){CancelCharacterClick(SelfNameBox,e);e.Handled=true;}};
        attribution=new(scope,encounters.Add);
        decoder.IncludeHit=SkillRules.LoadBundled().Include;
        encounters.DisplayName=scope.Label;
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
        CharacterNameText.Text=detected is not null?$"{detected} · detected automatically":hasName?$"{name} · saved name":"Detect your character automatically";
        CharacterEntry.Visibility=editingCharacter||SelfNameBox.IsKeyboardFocusWithin||!hasName?Visibility.Visible:Visibility.Collapsed;
        ChangeCharacterButton.Visibility=hasName?Visibility.Visible:Visibility.Collapsed;
        CancelCharacterButton.Visibility=hasName?Visibility.Visible:Visibility.Collapsed;
        CharacterHelp.Text=detected is not null?"Identified from game traffic. You can leave this field blank to use automatic detection.":hasName?"Your saved name is a fallback when the game identity message is missed.":autoCapturing?"Teleport once while capture is running to detect your character name. Or enter your name above.":"Start capturing, then teleport once to detect your character name. Or enter your name above.";
        if(!editingCharacter&&!SelfNameBox.IsKeyboardFocusWithin&&SelfNameBox.Text!=(name??""))SelfNameBox.Text=name??"";
    }
    void ChangeCharacterClick(object sender,RoutedEventArgs e){editingCharacter=true;lock(gate)RenderCharacter();SelfNameBox.Focus();SelfNameBox.SelectAll();}
    void CancelCharacterClick(object sender,RoutedEventArgs e){editingCharacter=false;System.Windows.Input.Keyboard.ClearFocus();lock(gate){SelfNameBox.Text=scope.ConfirmedSelfName??"";RenderCharacter();}}
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
    void StopAutomatic(){autoCapturing=false;capture.Stop();lock(gate)encounters.Stop();StartButton.Content="Start capturing";activeConnection="";}
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
            capture.Stop();Reset(true);DetectAndCapture();autoCapturing=true;lastConnectionCheck=DateTimeOffset.UtcNow;StartButton.Content="Stop capturing";
        }
        catch(Exception ex){autoCapturing=false;StartButton.Content="Start capturing";StatusText.Text="Cannot start: "+ex.Message;}
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
    void UpdateEncounters()
    {
        var items=new[]{new FilterChoice(null,"Latest encounter · automatic")}.Concat(encounters.Encounters.Reverse().Select(e=>new FilterChoice(e.Id,$"Encounter {e.Id} · {e.Meter.TotalDamage:N0} damage"))).ToArray();
        if(EncounterBox.ItemsSource is FilterChoice[] old&&old.SequenceEqual(items))return;
        int? selected=(EncounterBox.SelectedItem as FilterChoice)?.Id;
        EncounterBox.ItemsSource=items;EncounterBox.SelectedItem=items.FirstOrDefault(x=>x.Id==selected)??items[0];
    }
    MeterView CurrentView(int? targetId,IReadOnlySet<int>? allowed)=>encounters.View(SelectedEncounter,targetId,allowed);
    void UpdateTargets()
    {
        var box=TargetBox;var ids=meter.TargetIds;int defeated=ids.Count(id=>DeathFor(id) is not null);string all=defeated>0?$"All targets · {defeated} defeated":"All targets";
        var old=box.ItemsSource as FilterChoice[];
        var names=ids.ToDictionary(id=>id,ResolveTarget);
        string Label(int id)=>(names.Values.Count(name=>name==names[id])>1?$"{names[id]} · {id}":names[id])+(DeathFor(id) is not null?" · defeated":"");
        if(old is not null&&old[0].Label==all&&old.Skip(1).Select(x=>x.Id!.Value).SequenceEqual(ids)&&old.Skip(1).All(x=>x.Label==Label(x.Id!.Value)))return;
        int? selected=(box.SelectedItem as FilterChoice)?.Id;
        var items=new[]{new FilterChoice(null,all)}.Concat(ids.Select(id=>new FilterChoice(id,Label(id)))).ToArray();
        box.ItemsSource=items;box.SelectedItem=items.FirstOrDefault(x=>x.Id==selected)??items[0];
    }
    void Render(){lock(gate){RenderCharacter();if(autoCapturing)encounters.AdvanceTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());var allowed=scope.AllowedActors();UpdateEncounters();UpdateTargets();var view=CurrentView((TargetBox.SelectedItem as FilterChoice)?.Id,demo?null:allowed);PlayersGrid.ItemsSource=demo?view.Players:scope.DisplayRows(view.Players);IdentityStatus.Text=demo?"Demo: synthetic data only":scope.Status;Totals.Text=$"{(demo?"DEMO · ":"")}{view.Damage:N0} damage · {view.Damage/Math.Max(1,view.Seconds):N1} DPS · {view.Hits:N0} hits";EncounterTime.Text=SelectedEncounter is {} fight?$"Encounter {fight.Id} · {encounters.State(fight)} · {TimeSpan.FromSeconds(view.Seconds).ToString(@"hh\:mm\:ss")}":"Waiting for combat · 00:00:00";DiagnosticsText.Text=$"Packets {capture.PacketsReceived:N0} · TCP chunks {chunks:N0} · frames {decoder.RecordsFramed:N0} · awaiting identity {attribution.PendingCount:N0} · party updates {scope.Party.PeriodicUpdatesSeen:N0} · names {scope.Identity.Names.Count} · outside-scope hits {scope.ExcludedHits:N0} · malformed {decoder.RejectedRecords:N0} · dropped {capture.DroppedFrames:N0}{nameCacheStatus}{profileSaveStatus}{(targetIdentity.CatalogWarning is {} warning?" · "+warning:"")}";EntityNamesText.Text=string.Join(Environment.NewLine,scope.Identity.Names.OrderBy(p=>p.Value).Take(64).Select(p=>$"{p.Value} · ID {p.Key} · {(allowed.Contains(p.Key)?p.Key==scope.LocalPlayerId?"you":"party":"excluded")} "));}}
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
    void ApplyPeopleClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var profile=TrackingProfile.Create(SelfNameBox.Text);
            lock(gate)
            {
                if(scope.Identity.LocalPlayerId is int id&&scope.Identity.Names.TryGetValue(id,out var detected))profile=TrackingProfile.Create(detected);
                if(profile.SelfName!=scope.ConfirmedSelfName){profile.Apply(scope);encounters.Reset();if(autoCapturing)encounters.Start();attribution.Reset();}
            }
            editingCharacter=false;System.Windows.Input.Keyboard.ClearFocus();
            string message=profile.SelfName.Length>0?$"Character set to {profile.SelfName}.":"Automatic character detection enabled. Start capturing, then teleport once to detect your name.";
            try{if(persistKnownNames)profile.Save();savedSelfName=profile.SelfName;profileSaveStatus="";}
            catch(Exception ex){message+=" Using it for this session; could not save: "+ex.Message;}
            SelfNameBox.Text=profile.SelfName;Render();StatusText.Text=message;
        }
        catch(Exception ex){StatusText.Text="Cannot update character name: "+ex.Message;}
    }
    public object CheckRecording(string path,int actor,int target)
    {
        Reset(true);RecordedStreamReplay.Read(path,AcceptChunk);Render();
        TargetBox.SelectedItem=((FilterChoice[])TargetBox.ItemsSource).FirstOrDefault(c=>c.Id==target)??TargetBox.Items[0];Render();
        var rows=PlayersGrid.Items.Cast<PlayerRow>().ToArray();
        return new{rows=rows.Length,players=rows,hits=rows.Sum(r=>r.Hits),damage=rows.Sum(r=>r.Damage),displayed=Totals.Text,characterDisplay=CharacterNameText.Text,characterEntryVisible=CharacterEntry.Visibility==Visibility.Visible,confirmedSelfName=scope.ConfirmedSelfName,encounter=EncounterInfo(),observedDeaths=targetHealth.Deaths,targetDeaths=meter.TargetIds.Select(DeathFor).Where(d=>d is not null).ToArray(),encounterCount=encounters.Encounters.Count,encounterHistory=encounters.Encounters.Select(e=>new {id=e.Id,damage=e.Meter.TotalDamage,hits=e.Meter.TotalHits,seconds=encounters.Seconds(e),state=encounters.State(e)}).ToArray(),tcpChunks=chunks,localPlayerId=scope.LocalPlayerId,selfProof=scope.SelfProof,observedNames=scope.Identity.Names,scopeStatus=scope.Status,outsideScopeHits=scope.ExcludedHits,identityFrames=identityRecords.RecordsFramed,identityBundles=identityRecords.BundlesDecoded,identityRejected=identityRecords.RejectedRecords,partyCandidateCount=scope.Party.CandidateSnapshotsSeen,partyPeriodicUpdates=scope.Party.PeriodicUpdatesSeen,partyRecoveredAtStartup=scope.Party.RecoveredAtStartup,partyAuthoritative=scope.Party.IsAuthoritative,partyMembers=scope.Party.Members,partyProof=scope.Party.Proof,targets=meter.Targets(allowedActors:demo?null:scope.AllowedActors(),elapsedSeconds:SelectedEncounter is {} selected?encounters.Seconds(selected):0),observedTargetNames=targetIdentity.Names,recommendedAdapter=AdapterSelection.Recommend(PacketCaptureService.ListDevices())?.Description};
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
                int? targetId=(TargetBox.SelectedItem as FilterChoice)?.Id;
                var view=CurrentView(targetId,demo?null:scope.AllowedActors());
                File.WriteAllText(path,JsonSerializer.Serialize(new {schemaVersion=8,demo,encounter=EncounterInfo(),targetDeaths=meter.TargetIds.Select(DeathFor).Where(d=>d is not null).ToArray(),tracking=new{selfName=scope.ConfirmedSelfName,partyMembers=scope.Party.Members,localPlayerId=scope.LocalPlayerId,selfProof=scope.SelfProof,membershipSource=scope.Party.IsAuthoritative?"Decoded active roster":"Self only; awaiting roster",partyProof=scope.Party.Proof},scope=new{targetId},firstHitUnixMs=view.FirstMs,lastHitUnixMs=view.LastMs,durationSeconds=view.Seconds,totalDps=view.Damage/Math.Max(1,view.Seconds),totalDamage=view.Damage,trackedDamageAllTargets=meter.TotalDamage,framing=decoder.Framing.ToString(),diagnostics=new {packets=capture.PacketsReceived,tcpChunks=chunks,framedRecords=decoder.RecordsFramed,bundles=decoder.BundlesDecoded,rejected=decoder.RejectedRecords,excluded=decoder.FilteredHits,dropped=capture.DroppedFrames,opcodes=decoder.OpcodeCounts,rejectionReasons=decoder.RejectionReasons,ignoredNearbyHits=scope.ExcludedHits},players=demo?view.Players:scope.DisplayRows(view.Players),skills=view.Skills,targets=meter.Targets(allowedActors:demo?null:scope.AllowedActors(),elapsedSeconds:SelectedEncounter is {} selected?encounters.Seconds(selected):0)},new JsonSerializerOptions{WriteIndented=true}));
            }
            StatusText.Text="Saved locally: "+path;
        }catch(Exception ex){StatusText.Text="Save failed: "+ex.Message;}
    }
}

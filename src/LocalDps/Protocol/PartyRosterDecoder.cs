using System.Buffers.Binary;
using System.Text;

namespace LocalDps.Protocol;

public sealed record PartyMemberIdentity(int EntityId, string Name, int? ServerId, ulong? CharacterId = null);

/// <summary>
/// Bounded active-party observer for the controlled two-person lifecycle:
/// 0D92 peer join, 0092 one-person replacement, 1392 self-leave and
/// 1C92 existing-party recovery. Unsupported
/// group layouts fail closed. Nearby identities, rooms and damage are not proof.
/// </summary>
public sealed class PartyRosterDecoder
{
    private static readonly IReadOnlyList<PartyMemberIdentity> NoMembers = Array.Empty<PartyMemberIdentity>();
    private IReadOnlyList<PartyMemberIdentity> members = NoMembers;
    private readonly CharacterNameDirectory directory=new();
    private readonly Dictionary<(ulong Character,int Server),uint> recovered=new();
    private bool recoveryBlocked;
    public KnownCharacterName[] KnownNames=>directory.KnownNames;
    public long KnownNamesVersion=>directory.Version;
    public void RestoreKnownNames(IEnumerable<KnownCharacterName> names)
    {
        foreach(var name in names.Take(512))directory.Remember(name.CharacterId,name.ServerId,name.ClassId,name.Name);
    }
    public long PeriodicUpdatesSeen {get;private set;}
    public bool RecoveredAtStartup {get;private set;}
    public IReadOnlyList<PartyMemberIdentity> Members => members;
    public bool IsAuthoritative { get; private set; }
    public string Proof { get; private set; } = "Waiting for a supported active-party record; only proven self may be included. Automatic party support is limited to two people.";
    public long RoomRosterRecordsSeen { get; private set; }
    public long LastRoomRosterAtMs { get; private set; }
    /// <summary>The latest supported two-character join observation. Entity IDs remain unknown.</summary>
    public IReadOnlyList<PartyMemberIdentity> CandidateMembers { get; private set; } = NoMembers;
    /// <summary>Legacy diagnostic name for the raw entry field; its ID namespace is unvalidated.</summary>
    public uint? CandidatePartyId { get; private set; }
    public uint? RawEntryId => CandidatePartyId;
    public long CandidateSnapshotAtMs { get; private set; }
    public long CandidateSnapshotsSeen { get; private set; }
    public IReadOnlyList<PartyMemberIdentity> FullSnapshotMembers { get; private set; } = NoMembers;
    public long FullSnapshotsSeen { get; private set; }
    public long FullSnapshotAtMs { get; private set; }
    public string? LastMembershipObservation { get; private set; }

    public void Observe(ReadOnlySpan<byte> framedRecord, long timestampMs)
    {
        // Observe only complete framed records; persistent IDs are never combat IDs.
        if (!TryRecordHeader(framedRecord, out int offset)) return;
        byte op=framedRecord[offset],family=framedRecord[offset+1];
        if(op==5&&family==0x8a)
        {
            directory.Replace(framedRecord[(offset+2)..]);
            if(RecoveredAtStartup)RefreshRecoveredNames();
            return;
        }
        if(op==0x1c&&family==0x92)
        {
            ObservePeriodic(framedRecord[(offset+2)..]);return;
        }
        if (framedRecord[offset] == 0x13 && framedRecord[offset + 1] == 0x92)
        {
            recoveryBlocked=true;RecoveredAtStartup=false;recovered.Clear();
            members = NoMembers; CandidateMembers = NoMembers; CandidatePartyId = null; FullSnapshotMembers = NoMembers;
            var body = framedRecord[(offset + 2)..];
            IsAuthoritative = body.Length == 2 && body[0] == 0 && body[1] == 0;
            Proof = IsAuthoritative
                ? "Observed 13 92 self-leave clear; no party peers included. Two-person party lifecycle validated."
                : "Unsupported 13 92 clear variant; party membership unavailable.";
            LastMembershipObservation = Proof;
        }
        if (framedRecord[offset] == 0 && framedRecord[offset + 1] == 0x92)
        {
            recoveryBlocked=true;RecoveredAtStartup=false;recovered.Clear();
            // A replacement must invalidate previous candidates even when its
            // larger/unknown layout cannot yet be decoded safely.
            CandidateMembers = NoMembers; CandidatePartyId = null; FullSnapshotMembers = NoMembers; members = NoMembers; IsAuthoritative = false;
            Proof = "Unsupported full roster; party peers excluded. Automatic party support is limited to two people.";
            LastMembershipObservation = "Unsupported full roster replacement; membership unavailable.";
            if (TrySingleMemberReplacement(framedRecord[(offset + 2)..], out var soleMember,out uint soleClass))
            {
                directory.Remember(soleMember!.CharacterId!.Value,soleMember.ServerId!.Value,soleClass,soleMember.Name);
                FullSnapshotMembers = Array.AsReadOnly(new[] { soleMember! });
                FullSnapshotAtMs = timestampMs; FullSnapshotsSeen++;
                members = FullSnapshotMembers; IsAuthoritative = true;
                Proof = "Observed 00 92 complete one-character roster replacement after peer departure; two-person lifecycle only.";
                LastMembershipObservation = "00 92 replacement contains one character; previous peer candidates cleared.";
            }
        }
        if (framedRecord[offset] == 0x0d && framedRecord[offset + 1] == 0x92)
        {
            recoveryBlocked=true;RecoveredAtStartup=false;recovered.Clear();
            CandidateMembers = NoMembers; CandidatePartyId = null; members = NoMembers; IsAuthoritative = false;
            Proof = "Unsupported join roster; party peers excluded. Automatic party support is limited to two people.";
            if (TryTwoMemberJoinSnapshot(framedRecord[(offset + 2)..], out var peer, out uint partyId,out uint peerClass))
            {
                directory.Remember(peer!.CharacterId!.Value,peer.ServerId!.Value,peerClass,peer.Name);
                CandidateMembers = Array.AsReadOnly(new[] { peer! }); CandidatePartyId = partyId;
                CandidateSnapshotAtMs = timestampMs; CandidateSnapshotsSeen++;
                members = CandidateMembers; IsAuthoritative = true;
                Proof = "Observed 0D 92 two-person active-party join; peer identity validated. Larger groups remain unsupported.";
                LastMembershipObservation = Proof;
            }
        }
        if (framedRecord[offset] == 0x02 && framedRecord[offset + 1] == 0x97)
        {
            RoomRosterRecordsSeen++;
            LastRoomRosterAtMs = timestampMs;
            RestoreKnownNames(RoomCharacterNames.Read(framedRecord[(offset+2)..]));
            if(RecoveredAtStartup)RefreshRecoveredNames();
        }
    }

    public bool IsConfirmedMember(int entityId) => IsAuthoritative && entityId > 0 && members.Any(m => m.EntityId == entityId);
    public void Reset()
    {
        directory.Reset();recovered.Clear();recoveryBlocked=false;RecoveredAtStartup=false;PeriodicUpdatesSeen=0;
        RoomRosterRecordsSeen = 0; LastRoomRosterAtMs = 0;
        CandidateMembers = NoMembers; CandidatePartyId = null; CandidateSnapshotAtMs = CandidateSnapshotsSeen = 0;
        FullSnapshotMembers = NoMembers; FullSnapshotsSeen = FullSnapshotAtMs = 0; LastMembershipObservation = null;
        members = NoMembers; IsAuthoritative = false;
        Proof = "Waiting for a supported active-party record; only proven self may be included. Automatic party support is limited to two people.";
    }

    private void ObservePeriodic(ReadOnlySpan<byte> body)
    {
        // Observed repeatedly while already grouped and before both controlled
        // departures. Exactly the same member identity as the validated join.
        if(body.Length<63)return;
        int guildBytes=body[48];
        if(guildBytes>90||body.Length!=63+guildBytes||body[^1]!=1)return;
        if(guildBytes>0)
        {
            try{if(new UTF8Encoding(false,true).GetString(body.Slice(49,guildBytes)).Any(char.IsControl))return;}
            catch(DecoderFallbackException){return;}
        }
        ulong character=Read48(body);int server=BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
        uint job=BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
        uint level=BinaryPrimitives.ReadUInt32LittleEndian(body[36..]);
        if(character==0||server is <1001 or >3000||job is <1 or >64||level is <1 or >1000)return;
        PeriodicUpdatesSeen++;
        // Explicit snapshots/leave messages outrank delayed periodic updates.
        if(recoveryBlocked)return;
        recovered[(character,server)]=job;
        if(recovered.Count>4)
        {
            members=NoMembers;IsAuthoritative=false;RecoveredAtStartup=false;recoveryBlocked=true;recovered.Clear();
            Proof="More than four periodic peers observed; membership exceeds the five-player party limit.";return;
        }
        RecoveredAtStartup=true;IsAuthoritative=true;RefreshRecoveredNames();
    }
    private void RefreshRecoveredNames()
    {
        members=recovered.Select(p=>new PartyMemberIdentity(0,directory.TryResolve(p.Key.Character,p.Key.Server,p.Value,out var name)?name:"Unknown party member",p.Key.Server,p.Key.Character)).ToArray();
        Proof="Recovered party from 1C 92 periodic member updates (up to four teammates); names resolved separately by persistent ID/server/class.";
        LastMembershipObservation=Proof;
    }

    private static bool TrySingleMemberReplacement(ReadOnlySpan<byte> body, out PartyMemberIdentity? member,out uint memberClass)
    {
        member = null;memberClass=0;
        // One captured authoritative replacement after the other participant left.
        // Header: kind0, group-token8, leader-character6, leader-home-server2,
        // four bytes plus five policy bytes, kind3/count1, then one member entry.
        if (body.Length < 172 || body[0] != 0 || body[26] != 3 || body[27] != 1 || body[36] != 36) return false;
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            if (!Guid.TryParseExact(utf8.GetString(body.Slice(37, 36)), "D", out _)) return false;
            ulong id = Read48(body[73..]);
            int server = BinaryPrimitives.ReadUInt16LittleEndian(body[79..]);
            int n = body[81], start = 82;
            if (id == 0 || server == 0 || n is < 1 or > 90 || start + n + 89 != body.Length) return false;
            string name = utf8.GetString(body.Slice(start, n));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30 || name.Any(char.IsControl)) return false;
            if (Read48(body[9..]) != id || BinaryPrimitives.ReadUInt16LittleEndian(body[15..]) != server) return false;
            uint classId = BinaryPrimitives.ReadUInt32LittleEndian(body[(start + n)..]);
            if (classId is < 1 or > 64) return false;
            memberClass=classId;
            member = new PartyMemberIdentity(0, name, server, id); return true;
        }
        catch (DecoderFallbackException) { return false; }
    }
    private static ulong Read48(ReadOnlySpan<byte> body)
    {
        ulong value = 0; for (int i = 0; i < 6; i++) value |= (ulong)body[i] << (8 * i); return value;
    }

    private static bool TryTwoMemberJoinSnapshot(ReadOnlySpan<byte> body, out PartyMemberIdentity? peer, out uint partyId,out uint peerClass)
    {
        peer = null; partyId = 0;peerClass=0;
        // Observed only for a successful controlled two-person rejoin. Larger groups,
        // other subtypes, short records and unknown tails remain unsupported.
        if (body.Length < 145 || body[0] != 3 || body[1] != 2 || body[10] != 36) return false;
        partyId = BinaryPrimitives.ReadUInt32LittleEndian(body[2..]);
        int currentServer = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
        if (partyId == 0 || currentServer == 0 || currentServer != BinaryPrimitives.ReadUInt16LittleEndian(body[8..])) return false;
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            string ticket = utf8.GetString(body.Slice(11, 36));
            if (!Guid.TryParseExact(ticket, "D", out _)) return false;
            int p = 47;
            ulong characterId = 0; for (int i = 0; i < 6; i++) characterId |= (ulong)body[p++] << (8 * i);
            int homeServer = BinaryPrimitives.ReadUInt16LittleEndian(body[p..]); p += 2;
            int nameLength = body[p++];
            if (characterId == 0 || homeServer == 0 || nameLength is < 1 or > 90 || p + nameLength + 88 != body.Length) return false;
            string name = utf8.GetString(body.Slice(p, nameLength)); p += nameLength;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30 || name.Any(char.IsControl)) return false;
            uint classId = BinaryPrimitives.ReadUInt32LittleEndian(body[p..]);
            if (classId is < 1 or > 64) return false;
            peerClass=classId;
            // Party ID and persistent UInt48 character ID are NOT combat entity IDs.
            peer = new PartyMemberIdentity(0, name, homeServer, characterId); return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool TryRecordHeader(ReadOnlySpan<byte> record, out int offset)
    {
        offset = 0; uint declared = 0;
        for (int i = 0; i < 5 && i < record.Length; i++)
        {
            byte value = record[i];
            if (i == 4 && (value & 0xf8) != 0) return false;
            declared |= (uint)(value & 0x7f) << (7 * i);
            if ((value & 0x80) != 0) continue;
            offset = i + 1;
            long physicalLength = offset + (long)declared - 4;
            return physicalLength == record.Length && record.Length >= offset + 2 && record.Length <= 1024 * 1024;
        }
        return false;
    }
}

using System.Buffers.Binary;
using System.Text;

namespace LocalDps.Protocol;

public static class PartyProtocolSelfTests
{
    public static void Run()
    {
        byte[] body = new byte[150]; body[0] = 3; body[1] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2), 16296);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), 1101);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8), 1101);
        body[10] = 36; Encoding.ASCII.GetBytes("00000000-0000-0000-0000-000000000001").CopyTo(body, 11);
        ulong persistentId = 410001; for (int i = 0; i < 6; i++) body[47 + i] = (byte)(persistentId >> (8 * i));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(53), 1102);
        body[55] = 6; Encoding.UTF8.GetBytes("隊友").CopyTo(body, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(62), 9);
        byte[] record = new byte[154]; record[0] = 0x9c; record[1] = 1; record[2] = 0x0d; record[3] = 0x92; body.CopyTo(record, 4);
        for (int split = 0; split <= record.Length; split++)
        {
            var party = new PartyRosterDecoder(); var lane = new IdentityRecordStream(); lane.RecordObserved += party.Observe;
            lane.Feed("server", record.AsSpan(0, split), 1); lane.Feed("server", record.AsSpan(split), 2);
            Require(party.CandidateSnapshotsSeen == 1 && party.CandidateMembers.Single().CharacterId == persistentId, "two-byte-length snapshot split " + split);
            Require(party.IsAuthoritative && party.Members.Single().Name == "隊友" && !party.IsConfirmedMember(16296), "party identity admitted but raw entry ID cannot become combat entity");
        }
        var gate = new PartyRosterDecoder(); gate.Observe(record, 1);
        byte[] malformed = (byte[])record.Clone(); malformed[5] = 3; gate.Observe(malformed, 2);
        Require(gate.CandidateMembers.Count == 0 && gate.Members.Count == 0 && !gate.IsAuthoritative, "unsupported party size fails closed");
        gate.Observe(new byte[] { 6, 2, 0x97 }, 3);
        Require(gate.RoomRosterRecordsSeen == 1 && gate.Members.Count == 0, "room roster is diagnostic only");
        gate.Reset(); Require(gate.CandidateSnapshotsSeen == 0 && gate.CandidatePartyId == null, "reset");
        // Full one-character replacement: leader and sole member share persistent identity.
        byte[] soloBody = new byte[177]; soloBody[26] = 3; soloBody[27] = 1; soloBody[36] = 36;
        Encoding.ASCII.GetBytes("00000000-0000-0000-0000-000000000002").CopyTo(soloBody, 37);
        ulong selfCharacter = 420001;
        for (int i = 0; i < 6; i++) { soloBody[9 + i] = (byte)(selfCharacter >> (8 * i)); soloBody[73 + i] = (byte)(selfCharacter >> (8 * i)); }
        BinaryPrimitives.WriteUInt16LittleEndian(soloBody.AsSpan(15), 1102);
        BinaryPrimitives.WriteUInt16LittleEndian(soloBody.AsSpan(79), 1102);
        soloBody[81] = 6; Encoding.UTF8.GetBytes("勇者").CopyTo(soloBody, 82);
        BinaryPrimitives.WriteUInt32LittleEndian(soloBody.AsSpan(88), 34);
        byte[] solo = new byte[181]; solo[0] = 0xb7; solo[1] = 1; solo[2] = 0; solo[3] = 0x92; soloBody.CopyTo(solo, 4);
        gate.Observe(record, 10); gate.Observe(solo, 11);
        Require(gate.IsAuthoritative && gate.FullSnapshotsSeen == 1 && gate.Members.Single().Name == "勇者", "replacement removes departed peer");
        gate.Observe(record, 12); gate.Observe(new byte[] { 8, 0x13, 0x92, 0, 0 }, 13);
        Require(gate.IsAuthoritative && gate.Members.Count == 0 && gate.CandidateMembers.Count == 0, "self-leave clears all party members");
        gate.Observe(record, 14); gate.Observe(new byte[] { 8, 0x13, 0x92, 0, 1 }, 15);
        Require(!gate.IsAuthoritative && gate.Members.Count == 0, "unknown clear variant fails closed");
        gate.Observe(record, 16); var unsupportedSolo = (byte[])solo.Clone(); unsupportedSolo[31] = 4; gate.Observe(unsupportedSolo, 17);
        Require(!gate.IsAuthoritative && gate.Members.Count == 0, "unknown larger replacement cannot retain peers");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Party protocol self-test failed: " + message);
    }
}

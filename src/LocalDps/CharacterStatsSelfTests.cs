using System.Buffers.Binary;
using System.Text;
using LocalDps.Protocol;
namespace LocalDps;

public static class CharacterStatsSelfTests
{
    public static void Run()
    {
        byte[] Frame(byte op,byte family,byte[] body)=>new[]{(byte)(body.Length+6),op,family}.Concat(body).ToArray();
        byte[] UInt(int n)=>BitConverter.GetBytes(n);
        var name=Encoding.UTF8.GetBytes("Hero");
        var block=new byte[]{0x4d,4}.Concat(UInt(34)).Concat(new byte[]{1}).Concat(UInt(45)).Concat(UInt(2500)).Concat(UInt(2500)).Concat(UInt(45)).ToArray();
        var local=Frame(0x33,0x36,new byte[]{200,1,0,0,0,0,7,(byte)name.Length}.Concat(name).Concat(block).ToArray());
        var scope=new CombatScope();scope.Observe(local,1);
        void Require(bool condition,string message){if(!condition)throw new Exception("Character stats: "+message);}
        Require(scope.DisplayRows(Array.Empty<PlayerRow>()).Single() is {Actor:"Hero",ItemLevel:2500,CombatPower:null},"local identity supplies item level with unknown power");
        var header=new byte[24];header[5]=1;header[23]=1;
        var entry=new byte[11];entry[0]=12;entry[1]=1;BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(2),410001);BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(8),1102);entry[10]=(byte)name.Length;
        var tail=new byte[16];BinaryPrimitives.WriteUInt16LittleEndian(tail,1101);tail[2]=7;tail[3]=0x10;tail[4]=4;BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(5),125000);
        var room=Frame(2,0x97,header.Concat(entry).Concat(name).Concat(UInt(34)).Concat(UInt(45)).Concat(UInt(2500)).Concat(tail).Concat(new byte[]{0,9}).ToArray());
        scope.Observe(room,2);
        var row=scope.DisplayRows(Array.Empty<PlayerRow>()).Single();
        Require(row is {Actor:"Hero",ItemLevel:2500,CombatPower:125000},"room stats bind self across home/current server differences");
        Require(!row.DisplayActor.Contains("(you)")&&!row.DisplayActor.Contains("(party)"),"display uses stats instead of role suffixes");
        Require(!scope.Party.IsAuthoritative&&scope.AllowedActors().SetEquals(new[]{200}),"room stats cannot establish party membership");
        Require(scope.Statistics.ForMember(new(0,"Hero",1102,410001)) is {ItemLevel:2500,CombatPower:125000},"member stats require exact persistent identity");
        Require(scope.Statistics.ForMember(new(0,"Hero",1103,410001)) is null,"same name on another home server cannot borrow stats");
        Require(scope.Statistics.ForMember(new(0,"Hero",1102,410002)) is null,"different character cannot borrow stats");
        var malformed=(byte[])room.Clone();malformed[3+24+11+name.Length+12+4]=5;
        var clean=new CombatScope();clean.Observe(local,1);clean.Observe(malformed,2);
        Require(clean.Statistics.ForSelf(clean.Identity)?.CombatPower is null,"unrecognized CP marker cannot produce a number");
        byte[] DirectoryEntry(int job=34,int gear=2500,byte state=2,bool alias=false)
        {
            var entryBody=new byte[12];entryBody[0]=alias?(byte)2:(byte)0;entryBody[1]=123;
            var idBytes=new byte[6];BitConverter.GetBytes(410001UL).AsSpan(0,6).CopyTo(idBytes);
            var beforeName=new[]{entryBody[0]}.Concat(entryBody.AsSpan(1,8).ToArray()).Concat(idBytes).Concat(new byte[]{0x4e,4}).Concat(UInt(job)).Concat(new[]{(byte)name.Length}).Concat(name).Concat(UInt(45)).Concat(new[]{state}).ToList();
            if(state==1)beforeName.Add(1);
            if(alias)beforeName.AddRange(new byte[]{3,(byte)'A',(byte)'l',(byte)'t'});
            var stats=new byte[24];UInt(gear).CopyTo(stats,12);UInt(125000).CopyTo(stats,16);
            return beforeName.Concat(stats).ToArray();
        }
        byte[] Directory(params byte[][] entries)=>Frame(5,0x8a,new byte[]{0,0,(byte)entries.Length}.Concat(entries.SelectMany(x=>x)).ToArray());
        foreach(var entryBody in new[]{DirectoryEntry(),DirectoryEntry(state:1),DirectoryEntry(alias:true)})
        {
            var direct=new CombatScope();direct.Observe(Directory(entryBody),1);direct.Observe(local,2);
            Require(direct.DisplayRows(Array.Empty<PlayerRow>()).Single() is {ItemLevel:2500,CombatPower:125000},"directory CP arrives without recruiting-room traffic, even before local identity");
            Require(direct.Statistics.ForMember(new(0,"Hero",1102,410001)) is {CombatPower:125000},"directory peer stats bind persistent ID/server");
            Require(direct.Statistics.ForMember(new(0,"Hero",1103,410001)) is null,"directory peer stats reject the wrong server");
            Require(!direct.Party.IsAuthoritative,"directory stats do not establish membership");
            direct.Reset();Require(direct.Statistics.ForMember(new(0,"Hero",1102,410001)) is null,"reset discards directory stats");
        }
        foreach(var badDirectory in new[]{Directory(DirectoryEntry(job:9)),Directory(DirectoryEntry(gear:2600)),Directory(DirectoryEntry(),DirectoryEntry())})
        {
            var direct=new CombatScope();direct.Observe(local,1);direct.Observe(badDirectory,2);
            Require(direct.Statistics.ForSelf(direct.Identity)?.CombatPower is null,"class/gear mismatches and ambiguous directory entries cannot supply self CP");
        }
        scope.Reset();Require(scope.Statistics.ForMember(new(0,"Hero",1102,410001)) is null,"stats do not survive connection reset");
    }
}

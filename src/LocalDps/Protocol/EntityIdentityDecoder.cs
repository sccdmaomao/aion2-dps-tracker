using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LocalDps.Protocol;

public sealed record EntityIdentity(int EntityId, string Name, string SourceOpcode, long ObservedAtMs, bool IsLocalPlayer);

/// <summary>Observes identity records only. Seeing a name does not prove party membership.</summary>
public sealed class EntityIdentityDecoder
{
    private readonly Dictionary<int,string> names = new();
    private readonly Dictionary<int,EntityIdentity> identities = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false,true);
    public IReadOnlyDictionary<int,string> Names => names;
    public IReadOnlyDictionary<int,EntityIdentity> Identities => identities;
    public int? LocalPlayerId { get; private set; }
    public string? LocalPlayerProof { get; private set; }
    public void Reset() { names.Clear(); identities.Clear(); LocalPlayerId=null; LocalPlayerProof=null; }

    public void Observe(ReadOnlySpan<byte> framedRecord,long timestamp)
    {
        int p=0;
        if(!Var(framedRecord,ref p,out int declared) || declared<6 || p+2>framedRecord.Length) return;
        // Never scan arbitrary bytes for local33 36: it must be this framed record's opcode.
        byte op=framedRecord[p++], family=framedRecord[p++];
        if(family==0x36 && op==0x33)
        {
            int begin=p;
            if(!Var(framedRecord,ref p,out int id) || p-begin>3 || id<100 || id>999999) return;
            p+=4;
            if(p+2>framedRecord.Length || framedRecord[p] is not (7 or 0x37 or 0x3f)) return;
            int n=framedRecord[p+1];p+=2;
            if(n<2 || n>30 || !Name(framedRecord,p,n,out string name)) return;
            if(LocalPlayerId is int oldId && oldId!=id && identities.TryGetValue(oldId,out var oldIdentity))
                identities[oldId]=oldIdentity with { IsLocalPlayer=false };
            Bind(id,name,"33 36",timestamp,true);
            LocalPlayerId=id;
            LocalPlayerProof="Anchored 33 36 local-character record: entity varint, four bytes, name marker and strict UTF-8 name.";
        }
        else if(family==0x36 && op is 0x44 or 0x45)
        {
            if(!Var(framedRecord,ref p,out int id) || id<=0) return;
            int end=Math.Min(p+20,framedRecord.Length-2);
            for(int i=p;i<end;i++)
            {
                if(framedRecord[i] is not (7 or 23)) continue;
                int n=framedRecord[i+1];
                if(n<1 || n>40 || (n==1 && i!=p+4) || !Name(framedRecord,i+2,n,out string name)) continue;
                Bind(id,name,op==0x44?"44 36":"45 36",timestamp,false);break;
            }
        }
        else if(op==4 && family==0x8d)
        {
            if(!Var(framedRecord,ref p,out _)) return;p+=4;
            if(!Var(framedRecord,ref p,out int id) || id<=0 || p+3>framedRecord.Length) return;
            p+=2; int n=framedRecord[p++];
            if(n<1 || n>72 || !Name(framedRecord,p,n,out string name)) return;
            Bind(id,name,"04 8D",timestamp,false);
        }
    }
    private void Bind(int id,string name,string opcode,long time,bool local)
    {
        if(names.Count>=8192 && !names.ContainsKey(id))
        {
            int oldest=identities.Values.Where(x=>x.EntityId!=LocalPlayerId).MinBy(x=>x.ObservedAtMs)!.EntityId;
            names.Remove(oldest);identities.Remove(oldest);
        }
        // An observed nearby record cannot rename a self identity established by local-character data.
        if(!local && id==LocalPlayerId && names.TryGetValue(id,out var previous) && previous!=name) return;
        names[id]=name;identities[id]=new(id,name,opcode,time,local || id==LocalPlayerId);
    }
    private static bool Name(ReadOnlySpan<byte> bytes,int p,int count,out string name)
    {
        name="";if(p<0 || count<1 || p+count>bytes.Length)return false;
        try {name=StrictUtf8.GetString(bytes.Slice(p,count));}catch(DecoderFallbackException){return false;}
        // Player names accept Unicode letters/numbers plus conventional separators, no arbitrary prose.
        return name.Length is >=1 and <=40 && name.Any(char.IsLetter) && name.All(c=>char.IsLetterOrDigit(c) || c is '_' or '-');
    }
    private static bool Var(ReadOnlySpan<byte> data,ref int p,out int value)
    {
        value=0;if(p<0)return false;
        for(int shift=0;shift<35;shift+=7)
        {
            if(p>=data.Length)return false;byte b=data[p++];if(shift==28 && (b&0xf0)!=0)return false;
            value|=(b&127)<<shift;if((b&128)==0)return value>=0;
        }
        return false;
    }
}

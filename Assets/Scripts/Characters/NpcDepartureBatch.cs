using System;
using System.Collections.Generic;

namespace Guochao
{
    public readonly struct NpcDepartureCandidate
    {
        public readonly int Id;public readonly float SlotX,Radius;
        public NpcDepartureCandidate(int id,float slotX,float radius){Id=id;SlotX=slotX;Radius=radius;}
    }
    public readonly struct NpcDepartureState
    {
        public readonly bool Exited,ReachedRearLane;public readonly float X;
        public NpcDepartureState(bool exited,bool reachedRearLane,float x){Exited=exited;ReachedRearLane=reachedRearLane;X=x;}
    }
    // Snapshot membership is a traffic reservation, not a collision bypass.
    // Newly finished guests cannot enter a batch already flowing across their slot.
    public sealed class NpcDepartureBatch
    {
        private readonly List<NpcDepartureCandidate> members=new List<NpcDepartureCandidate>();
        private int granted;
        public int Serial {get;private set;}
        public bool Active=>members.Count>0;
        public bool Contains(int id){foreach(var item in members)if(item.Id==id)return true;return false;}
        public bool HasGranted(int id){for(int i=0;i<granted;i++)if(members[i].Id==id)return true;return false;}
        public bool TryBegin(IEnumerable<NpcDepartureCandidate> candidates)
        {
            if(Active)return false;
            members.AddRange(candidates);members.Sort((a,b)=>a.SlotX!=b.SlotX?a.SlotX.CompareTo(b.SlotX):a.Id.CompareTo(b.Id));
            if(members.Count==0)return false;granted=0;Serial++;return true;
        }
        public bool TryGrant(Func<int,NpcDepartureState> sample,out int id)
        {
            id=-1;if(granted>=members.Count)return false;
            var next=members[granted];
            for(int i=0;i<granted;i++)
            {
                var previous=members[i];var state=sample(previous.Id);if(state.Exited)continue;
                if(!state.ReachedRearLane||next.SlotX-state.X<next.Radius+previous.Radius+.10f)return false;
            }
            id=next.Id;granted++;return true;
        }
        public bool FinishIfClear(Func<int,NpcDepartureState> sample)
        {
            if(!Active)return false;
            foreach(var member in members)if(!sample(member.Id).Exited)return false;
            members.Clear();granted=0;return true;
        }
    }
}

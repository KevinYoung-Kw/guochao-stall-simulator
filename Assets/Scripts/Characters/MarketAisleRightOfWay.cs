using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Market-source coordinates, measured from the real wall (x=0) and shelf
    // (x=2.4, z=6.5..13.7). Two full Auntie/Courier envelopes cannot pass here.
    // NPC through-traffic is southbound. The player is unrestricted and has
    // priority: an NPC backs out through the north mouth into the wide bay.
    // This class decides a route; NpcNavigationSpace remains the only mover.
    public sealed class MarketAisleRightOfWay
    {
        public enum Phase { Flowing, RetreatToEntry, MoveToRefuge, WaitInRefuge, ReturnToEntry }
        [Serializable] public sealed class SaveData
        {public int phase,yieldCount;public float clearTime,yieldSeconds;}
        readonly Vector3 origin,xAxis,zAxis;
        readonly List<Collider> users=new List<Collider>();
        readonly NpcNavigationSpace navigation;
        readonly NpcNavigationAgent agent;
        float clearTime;
        public Phase State {get;private set;}
        public float YieldSeconds {get;private set;}
        public int YieldCount {get;private set;}
        public string BlockingUser {get;private set;}="";
        public bool Active=>State!=Phase.Flowing;
        public SaveData CaptureSave()=>new SaveData{phase=(int)State,yieldCount=YieldCount,clearTime=clearTime,yieldSeconds=YieldSeconds};
        public void RestoreSave(SaveData data)
        {
            if(data==null){State=Phase.Flowing;clearTime=0;YieldCount=0;YieldSeconds=0;return;}
            if(data.phase<0||data.phase>(int)Phase.ReturnToEntry)throw new ArgumentException("Invalid market right-of-way phase");
            State=(Phase)data.phase;clearTime=Mathf.Max(0,data.clearTime);YieldCount=Mathf.Max(0,data.yieldCount);YieldSeconds=Mathf.Max(0,data.yieldSeconds);BlockingUser="";
        }
        public Vector3 Entry=>Point(1.25f,15.45f);
        public Vector3 Refuge=>Point(3.50f,15.45f);

        public MarketAisleRightOfWay(Vector3 origin,Vector3 xAxis,Vector3 zAxis,
            NpcNavigationSpace navigation,NpcNavigationAgent agent,IEnumerable<Collider> userVolumes)
        {
            this.origin=origin;this.xAxis=xAxis;this.zAxis=zAxis;
            this.navigation=navigation;this.agent=agent;
            foreach(var volume in userVolumes)if(volume)users.Add(volume);
        }
        Vector3 Point(float x,float z)=>origin+xAxis*x+zAxis*z;
        Vector2 Local(Vector3 p)
        {Vector3 d=p-origin;return new Vector2(Vector3.Dot(d,xAxis)/xAxis.sqrMagnitude,Vector3.Dot(d,zAxis)/zAxis.sqrMagnitude);}
        public bool AllowsSegment(Vector3 from,Vector3 to)=>
            State==Phase.RetreatToEntry||AllowsSouthboundSegment(from,to,origin,xAxis,zAxis);
        public static bool AllowsSouthboundSegment(Vector3 from,Vector3 to,Vector3 origin,Vector3 xAxis,Vector3 zAxis)
        {
            float fx=Vector3.Dot(from-origin,xAxis)/xAxis.sqrMagnitude,tx=Vector3.Dot(to-origin,xAxis)/xAxis.sqrMagnitude;
            float fz=Vector3.Dot(from-origin,zAxis)/zAxis.sqrMagnitude,tz=Vector3.Dot(to-origin,zAxis)/zAxis.sqrMagnitude;
            return MarketAisleDirectionRule.AllowsSouthbound(fx,fz,tx,tz);
        }
        bool UserInPassage(float aheadOf=float.PositiveInfinity)
        {
            BlockingUser="";
            foreach(Collider volume in users)
            {
                if(!volume||!volume.enabled||!volume.gameObject.activeInHierarchy)continue;
                Bounds b=volume.bounds;if(b.max.y<origin.y+.05f)continue;
                Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=-min;
                foreach(float x in new[]{b.min.x,b.max.x})foreach(float z in new[]{b.min.z,b.max.z})
                {Vector2 p=Local(new Vector3(x,origin.y,z));min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
                // Includes the approaches, so the agent yields before nose-to-nose
                // contact rather than hoping local avoidance can find nonexistent room.
                if(max.x>=0&&min.x<=2.4f&&max.y>=4.8f&&min.y<=15.8f&&min.y<=aheadOf)
                {BlockingUser=volume.name;return true;}
            }
            return false;
        }
        public bool Decide(Vector3 routeTarget,float dt,out Vector3 target,out bool hold)
        {
            target=routeTarget;hold=false;if(dt<=0)return Active;
            Vector2 here=Local(agent.Body.position),goal=Local(routeTarget);
            if(State==Phase.Flowing)
            {
                bool inPassage=here.x<2.4f&&here.y>5.65f&&here.y<15.65f;
                bool approaching=goal.x<2.4f&&goal.y>14.4f&&here.y>14.4f;
                if(!(inPassage||approaching)||!UserInPassage(inPassage?here.y+agent.Profile.Radius:float.PositiveInfinity))return false;
                YieldCount++;YieldSeconds=0;clearTime=0;
                State=inPassage&&!navigation.HasArrived(agent,Entry)?Phase.RetreatToEntry:Phase.MoveToRefuge;
            }
            YieldSeconds+=dt;
            if(State==Phase.RetreatToEntry)
            {
                target=Entry;if(!navigation.HasArrived(agent,target))return true;
                State=Phase.MoveToRefuge;
            }
            if(State==Phase.MoveToRefuge)
            {
                target=Refuge;if(!navigation.HasArrived(agent,target))return true;
                State=Phase.WaitInRefuge;
            }
            if(State==Phase.WaitInRefuge)
            {
                bool occupied=UserInPassage();
                // An actor can leave the rectangular aisle and still cover the
                // entry with its complete footprint. Reserve the entire merge
                // sweep as well, otherwise the waiting NPC leaves its safe bay
                // and stops nose-to-nose at the mouth.
                if(!occupied&&(!navigation.CanOccupy(Entry,agent.Profile,agent)||!navigation.CanReachDirect(agent,Entry)))
                {occupied=true;BlockingUser="entry-or-merge-occupied";}
                clearTime=occupied?0:clearTime+dt;
                target=Refuge;hold=true;
                if(clearTime<.4f)return true;
                State=Phase.ReturnToEntry;hold=false;
            }
            if(State==Phase.ReturnToEntry)
            {
                if(UserInPassage()||!navigation.CanOccupy(Entry,agent.Profile,agent)||!navigation.CanReachDirect(agent,Entry))
                {State=Phase.MoveToRefuge;clearTime=0;target=Refuge;return true;}
                target=Entry;if(!navigation.HasArrived(agent,target))return true;
                State=Phase.Flowing;BlockingUser="";return false;
            }
            return true;
        }
    }
}

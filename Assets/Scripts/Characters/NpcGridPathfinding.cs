using System;
using System.Collections.Generic;

namespace Guochao
{
    public readonly struct NpcPathPoint
    {
        public readonly float X,Z;
        public NpcPathPoint(float x,float z){X=x;Z=z;}
    }

    // Engine-independent search. Occupancy and every complete edge are supplied by
    // the caller, so this cannot shorten a path through a shelf or a diagonal corner.
    public static class NpcGridPathfinding
    {
        private readonly struct Entry
        {
            public readonly int Node;public readonly float Score;
            public Entry(int node,float score){Node=node;Score=score;}
        }
        private sealed class Heap
        {
            private readonly List<Entry> data=new List<Entry>();
            public int Count=>data.Count;
            public void Push(int node,float score)
            {
                var value=new Entry(node,score);int i=data.Count;data.Add(value);
                while(i>0){int parent=(i-1)/2;if(data[parent].Score<=score)break;data[i]=data[parent];i=parent;}data[i]=value;
            }
            public Entry Pop()
            {
                Entry result=data[0],tail=data[data.Count-1];data.RemoveAt(data.Count-1);if(data.Count==0)return result;
                int i=0;while(i*2+1<data.Count){int child=i*2+1;if(child+1<data.Count&&data[child+1].Score<data[child].Score)child++;if(tail.Score<=data[child].Score)break;data[i]=data[child];i=child;}data[i]=tail;return result;
            }
        }
        public static float Distance(float ax,float az,float bx,float bz)
        {float x=bx-ax,z=bz-az;return (float)Math.Sqrt(x*x+z*z);}
        public static float SegmentDistanceSquared(float ax,float az,float bx,float bz,float px,float pz)
        {
            float x=bx-ax,z=bz-az,l=x*x+z*z;
            float t=l<.0000001f?0:Math.Max(0,Math.Min(1,((px-ax)*x+(pz-az)*z)/l));
            float dx=px-(ax+t*x),dz=pz-(az+t*z);return dx*dx+dz*dz;
        }
        public static bool DiscSweepClear(float ax,float az,float bx,float bz,float radius,float otherX,float otherZ,float otherRadius,float gap=.035f)
        {float total=radius+otherRadius+gap;return SegmentDistanceSquared(ax,az,bx,bz,otherX,otherZ)>=total*total;}

        public static bool TryFind(NpcPathPoint start,NpcPathPoint goal,NpcPathPoint min,NpcPathPoint max,float step,
            Func<float,float,bool> clear,Func<float,float,float,float,bool> edge,List<NpcPathPoint> result,
            out float cost,bool useHeuristic=true,int expansionLimit=7000)
        {
            result.Clear();cost=float.PositiveInfinity;
            if(step<=0||max.X<min.X||max.Z<min.Z||!clear(goal.X,goal.Z))return false;
            if(edge(start.X,start.Z,goal.X,goal.Z)){result.Add(goal);cost=Distance(start.X,start.Z,goal.X,goal.Z);return true;}
            int width=(int)Math.Floor((max.X-min.X)/step)+1,depth=(int)Math.Floor((max.Z-min.Z)/step)+1,count=width*depth;
            if(count<=0||count>40000)return false;
            var open=new Heap();var distance=new float[count];var parent=new int[count];var closed=new bool[count];var occupancy=new byte[count];
            for(int i=0;i<count;i++){distance[i]=float.PositiveInfinity;parent[i]=-1;}
            float X(int i)=>min.X+(i%width)*step;
            float Z(int i)=>min.Z+(i/width)*step;
            bool OpenCell(int i){if(occupancy[i]==0)occupancy[i]=(byte)(clear(X(i),Z(i))?1:2);return occupancy[i]==1;}
            int Attach(NpcPathPoint p)
            {
                int cx=(int)Math.Round((p.X-min.X)/step),cz=(int)Math.Round((p.Z-min.Z)/step),best=-1;float nearest=float.PositiveInfinity;
                for(int ring=0;ring<=3;ring++)
                {
                    for(int z=cz-ring;z<=cz+ring;z++)for(int x=cx-ring;x<=cx+ring;x++)
                    {
                        if(x<0||z<0||x>=width||z>=depth)continue;int n=z*width+x;
                        float d=Distance(p.X,p.Z,X(n),Z(n));if(d>=nearest||!OpenCell(n)||!edge(p.X,p.Z,X(n),Z(n)))continue;best=n;nearest=d;
                    }
                    if(best>=0)break;
                }return best;
            }
            int first=Attach(start),last=Attach(goal);if(first<0||last<0)return false;
            float H(int n)=>useHeuristic?Distance(X(n),Z(n),X(last),Z(last)):0;
            distance[first]=0;open.Push(first,H(first));int expanded=0;
            while(open.Count>0&&expanded++<expansionLimit)
            {
                int n=open.Pop().Node;if(closed[n])continue;closed[n]=true;
                if(n==last)
                {
                    var reversed=new List<NpcPathPoint>();for(int p=last;p>=0;p=parent[p])reversed.Add(new NpcPathPoint(X(p),Z(p)));reversed.Reverse();
                    // String pulling is also swept; it cannot cut a corner opened by the grid.
                    NpcPathPoint from=start;int at=0;
                    while(at<reversed.Count){int far=at;for(int j=at+1;j<reversed.Count;j++){if(!edge(from.X,from.Z,reversed[j].X,reversed[j].Z))break;far=j;}result.Add(reversed[far]);from=reversed[far];at=far+1;}
                    if(Distance(from.X,from.Z,goal.X,goal.Z)>.001f)result.Add(goal);
                    cost=distance[last]+Distance(start.X,start.Z,X(first),Z(first))+Distance(goal.X,goal.Z,X(last),Z(last));return true;
                }
                int nx=n%width,nz=n/width;
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0&&dz==0)continue;int x=nx+dx,z=nz+dz;if(x<0||z<0||x>=width||z>=depth)continue;
                    int next=z*width+x;if(closed[next]||!OpenCell(next))continue;
                    if(dx!=0&&dz!=0&&(!OpenCell(nz*width+x)||!OpenCell(z*width+nx)))continue;
                    if(!edge(X(n),Z(n),X(next),Z(next)))continue;
                    float candidate=distance[n]+step*(dx!=0&&dz!=0?1.41421356237f:1);
                    if(candidate>=distance[next])continue;distance[next]=candidate;parent[next]=n;open.Push(next,candidate+H(next));
                }
            }return false;
        }
    }
}

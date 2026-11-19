using UnityEngine;

namespace Guochao
{
    // Exact nominal capsule/OBB geometry. CharacterController query shapes may
    // be smaller than their authored envelope; moving props must retain the same
    // full envelope that NPC route planning and overlap auditing already use.
    public static class NpcCapsuleGeometry
    {
        public static bool IntersectsBox(Vector3 lower,Vector3 upper,float radius,Vector3 center,Vector3 halfExtents,Quaternion rotation)
        {return SegmentBoxDistanceSquared(lower,upper,center,halfExtents,rotation)<=radius*radius;}

        // Exact horizontal sweep of an upright capsule against an upright OBB.
        // Axis heights stay fixed, so choose the capsule-axis height nearest the
        // box, then minimize the resulting segment/OBB distance analytically.
        public static bool UprightSweepClear(Vector3 lower,Vector3 upper,Vector3 displacement,float radius,Vector3 center,Vector3 half,Quaternion rotation)
        {
            if(Mathf.Abs(Vector3.Dot(rotation*Vector3.up,Vector3.up))<.99999f||Mathf.Abs(displacement.y)>.0001f)
                throw new System.ArgumentException("Upright clearance sweep received a tilted box or vertical move");
            Vector3 a=new Vector3(lower.x,Mathf.Clamp(center.y,lower.y,upper.y),lower.z),b=a+displacement;
            float start=SegmentBoxDistanceSquared(a,a,center,half,rotation),end=SegmentBoxDistanceSquared(b,b,center,half,rotation);
            float threshold=radius*radius;
            if(start<=threshold)
            {
                // A restored old save or an already approaching prop may occupy
                // safety clearance, while the real shapes still do not overlap.
                // Permit ONLY strictly increasing clearance; all real physical
                // sweeps remain mandatory in NpcNavigationSpace. Convex distance
                // with nonnegative initial derivative cannot dip toward the box.
                Quaternion inverse=Quaternion.Inverse(rotation);Vector3 p=inverse*(a-center),v=inverse*displacement;
                Vector3 closest=new Vector3(Mathf.Clamp(p.x,-half.x,half.x),Mathf.Clamp(p.y,-half.y,half.y),Mathf.Clamp(p.z,-half.z,half.z));
                return end>start+1e-8f&&Vector3.Dot(p-closest,v)>=0;
            }
            return SegmentBoxDistanceSquared(a,b,center,half,rotation)>threshold;
        }

        public static float SegmentBoxDistanceSquared(Vector3 a,Vector3 b,Vector3 center,Vector3 halfExtents,Quaternion rotation)
        {
            Quaternion inverse=Quaternion.Inverse(rotation);Vector3 start=inverse*(a-center),delta=inverse*(b-a);
            float best=Mathf.Min(PointDistanceSquared(start,halfExtents),PointDistanceSquared(start+delta,halfExtents));
            float lo=0;
            // Crossing any of six box planes changes which coordinates contribute
            // to squared distance. Each interval is one convex quadratic; evaluate
            // its exact stationary point. No samples, collision shrink or heap work.
            for(int interval=0;interval<7&&lo<1;interval++)
            {
                float hi=1;
                for(int axis=0;axis<3;axis++)if(delta[axis]!=0)
                {
                    float first=(-halfExtents[axis]-start[axis])/delta[axis];
                    float second=(halfExtents[axis]-start[axis])/delta[axis];
                    if(first>lo&&first<hi)hi=first;if(second>lo&&second<hi)hi=second;
                }
                float mid=(lo+hi)*.5f,A=0,B=0;
                for(int axis=0;axis<3;axis++)
                {
                    float value=start[axis]+delta[axis]*mid,offset;
                    if(value < -halfExtents[axis])offset=start[axis]+halfExtents[axis];
                    else if(value > halfExtents[axis])offset=start[axis]-halfExtents[axis];
                    else continue;
                    A+=delta[axis]*delta[axis];B+=delta[axis]*offset;
                }
                float t=A>0?Mathf.Clamp(-B/A,lo,hi):mid;
                best=Mathf.Min(best,PointDistanceSquared(start+delta*t,halfExtents));
                lo=hi;
            }
            return best;
        }
        private static float PointDistanceSquared(Vector3 point,Vector3 half)
        {
            float x=Mathf.Max(0,Mathf.Abs(point.x)-half.x),y=Mathf.Max(0,Mathf.Abs(point.y)-half.y),z=Mathf.Max(0,Mathf.Abs(point.z)-half.z);
            return x*x+y*y+z*z;
        }
    }
}

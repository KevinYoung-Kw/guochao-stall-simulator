using System;
using UnityEngine;

namespace Guochao
{
    // Original lightweight synthesized Foley; no downloaded or licensed recordings.
    public sealed class GameFeelAudio : MonoBehaviour
    {
        public static bool Muted => StreetAudioLevels.Effects <= 0;
        private WorldRuntime world;
        private AudioSource steps, rolling, contact;
        private AudioClip stepClip, rollClip, boxClip, metalClip;
        private float distance, pendingContact=-1;
        private bool metalContact, suspended;
        private void Awake()
        {
            world=GetComponent<WorldRuntime>();
            stepClip=MakeClip("Original_Footstep",.10f,1);
            rollClip=MakeClip("Original_CasterRolling",1.6f,2);
            boxClip=MakeClip("Original_CardboardSetdown",.16f,3);
            metalClip=MakeClip("Original_StoveSetdown",.22f,4);
            steps=Source();rolling=Source();contact=Source();rolling.clip=rollClip;rolling.loop=true;rolling.volume=0;rolling.Play();
        }
        private AudioSource Source()
        {
            var source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.volume=.12f;return source;
        }
        public void NotifyInteraction(string target)
        { pendingContact=.40f;metalContact=target=="HomeCart"||target=="StovePickup"; }
        private void Update()
        {
            GameFlow flow=GameFlow.Instance;
            bool stop=Muted||world==null||world.Player==null||flow==null||flow.Run.Paused||flow.IsTransitioning||
                (flow.Run.Phase!=GamePhase.Home&&flow.Run.Phase!=GamePhase.Market);
            if(stop!=suspended)
            {
                suspended=stop;
                if(stop){rolling.Pause();steps.Pause();contact.Pause();}
                else{rolling.UnPause();steps.UnPause();contact.UnPause();}
            }
            if(stop)return;
            steps.volume=contact.volume=.12f*StreetAudioLevels.Effects;
            float speed=new Vector2(world.Player.Velocity.x,world.Player.Velocity.z).magnitude;
            distance+=speed*Time.deltaTime;
            if(distance>.92f)
            { distance%=.92f;steps.pitch=world.Player.carrying?.88f:1;steps.PlayOneShot(stepClip,.35f); }
            float volume=world.ShoppingMotor!=null?Mathf.Clamp01(speed/3.5f)*.075f*StreetAudioLevels.Effects:0;
            rolling.volume=Mathf.MoveTowards(rolling.volume,volume,Time.deltaTime*.3f);
            rolling.pitch=.75f+Mathf.Clamp01(speed/4)*.4f;
            if(pendingContact>=0)
            {
                pendingContact-=Time.deltaTime;
                if(pendingContact<0)contact.PlayOneShot(metalContact?metalClip:boxClip,.6f);
            }
        }
        private static AudioClip MakeClip(string name,float duration,int kind)
        {
            const int rate=22050;float[] data=new float[(int)(rate*duration)];var random=new System.Random(4711+kind);float filtered=0;
            for(int i=0;i<data.Length;i++)
            {
                float t=i/(float)rate;filtered=.72f*filtered+.28f*((float)random.NextDouble()*2-1);
                float envelope=kind==2?.28f:Mathf.Exp(-t*(kind==4?22:kind==3?30:45));
                float tone=kind==4?Mathf.Sin(2*Mathf.PI*410*t)*.25f+Mathf.Sin(2*Mathf.PI*735*t)*.12f:
                    kind==1?Mathf.Sin(2*Mathf.PI*95*t)*.30f:0;
                data[i]=(filtered*(kind==2?.5f:.65f)+tone)*envelope;
            }
            AudioClip clip=AudioClip.Create(name,data.Length,1,rate,false);clip.SetData(data,0);return clip;
        }
        private void OnDestroy()
        { foreach(var clip in new[]{stepClip,rollClip,boxClip,metalClip})if(clip!=null)Destroy(clip); }
    }
}

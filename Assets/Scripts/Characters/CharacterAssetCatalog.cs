using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Guochao
{
    public enum CharacterRole { Vendor, Customer, MarketStaff }

    public enum CharacterMotion
    {
        Idle, Walk, Run, Turn, CarryIdle, CarryWalk, Lift, Place,
        PushWalk, PushTurn, PushIdle, Ride, Browse, TakeShelf, Queue, Pay,
        Cashier, Serve, Eat, Talk, Complain, ExitRush, CookPrep, StirFry,
        Spread, Soup, FryLift, TeaPound, Season, Pack, Clean, Repair, Refill,
        Read, Fold, Inspect
    }

    public sealed class CharacterClipAsset
    {
        public CharacterMotion Motion { get; }
        public string ResourcePath { get; }
        public string ClipName { get; }
        public string SourceTakeName { get; }
        public float SourceDurationSeconds { get; }
        public CharacterClipAsset(CharacterMotion motion,string path,string clip,float duration,string sourceTake=null)
        {Motion=motion;ResourcePath=path;ClipName=clip;SourceTakeName=sourceTake??clip;SourceDurationSeconds=duration;}
    }

    // Asset identity is independent of the number, outfit role or route of actors.
    // Every entry describes one actual source asset, independent of crowd instance count.
    public sealed class CharacterArchetypeAsset
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string ModelResourcePath { get; }
        public string AlbedoResourcePath { get; }
        public string OriginNode { get; }
        public string ForwardNode { get; }
        public string RigSource { get; }
        public string InspectionSource { get; }
        public string RigFamily { get; }
        public int GenerationTask { get; }
        public int RigTask { get; }
        public int AnimationTask { get; }
        public int BoneCount { get; }
        public float RestHeightMetres { get; }
        public Vector3 RestBoundsSizeMetres { get; }
        public float CollisionHeightMetres { get; }
        public float CollisionRadiusMetres { get; }
        public float CollisionCenterY { get; }
        public CharacterRole Role { get; }
        public bool IsPlayerSource => Role==CharacterRole.Vendor;
        public bool IsCustomerSource => Role==CharacterRole.Customer;
        public bool IsModularBaseComplete => false;
        public bool IsProductionComplete => false;
        public string ProductionLimitations { get; }
        public ReadOnlyCollection<CharacterClipAsset> Clips { get; }
        public ReadOnlyCollection<CharacterMotion> MissingMotions { get; }

        internal CharacterArchetypeAsset(string id,string displayName,string model,string albedo,
            string origin,string forward,string source,string inspection,int generation,int rig,int animation,
            float height,Vector3 bounds,CharacterRole role,string limitations,params CharacterClipAsset[] clips)
        {
            Id=id;DisplayName=displayName;ModelResourcePath=model;AlbedoResourcePath=albedo;
            OriginNode=origin;ForwardNode=forward;RigSource=source;InspectionSource=inspection;
            GenerationTask=generation;RigTask=rig;AnimationTask=animation;
            RigFamily="Aholo mixamorig 52; canonical retarget contract pending";BoneCount=52;
            RestHeightMetres=height;RestBoundsSizeMetres=bounds;
            // Existing navigation clearance is preserved; rest-pose arm span is not body clearance.
            CollisionHeightMetres=1.60f;CollisionRadiusMetres=.28f;CollisionCenterY=.81f;
            Role=role;ProductionLimitations=limitations;
            Clips=Array.AsReadOnly((CharacterClipAsset[])clips.Clone());
            var missing=new List<CharacterMotion>();
            foreach(CharacterMotion motion in Enum.GetValues(typeof(CharacterMotion)))
            {bool found=false;foreach(var clip in Clips)found|=clip.Motion==motion;if(!found)missing.Add(motion);}
            MissingMotions=missing.AsReadOnly();
        }
        public CharacterClipAsset Clip(CharacterMotion motion)
        {foreach(var clip in Clips)if(clip.Motion==motion)return clip;return null;}
    }

    public sealed class LoadedCharacterAsset
    {
        public CharacterArchetypeAsset Definition { get; }
        public GameObject Prefab { get; }
        public ReadOnlyDictionary<CharacterMotion,AnimationClip> Clips { get; }
        internal LoadedCharacterAsset(CharacterArchetypeAsset definition,GameObject prefab,Dictionary<CharacterMotion,AnimationClip> clips)
        {Definition=definition;Prefab=prefab;Clips=new ReadOnlyDictionary<CharacterMotion,AnimationClip>(clips);}
    }

    public static class CharacterAssetCatalog
    {
        public const string PlayerId="vendor_player_v6";
        public const string JadeId="customer_jade_v9";
        public const string ContractVersion="guochao.character-assets.v1";
        private static readonly ReadOnlyCollection<CharacterArchetypeAsset> all=BuildAll();
        private static ReadOnlyCollection<CharacterArchetypeAsset> BuildAll()
        {
            var entries=new System.Collections.Generic.List<CharacterArchetypeAsset>{
            new CharacterArchetypeAsset(PlayerId,"摊主（现有主角复用）","Models/Player",null,
                "PlayerOrigin","PlayerForward","art-v6/character/provenance.json",
                "art-v6/character/rig-animation-inspection.json",8242107,8243445,8244624,
                1.65f,new Vector3(1.215f,1.65f,.384f),CharacterRole.Vendor,
                "Existing protagonist reused as a crowd actor; not an independent NPC or modular base. Four source clips only; contact/retarget acceptance remains.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/Player@Idle_Loop","Idle_Loop",2.5f,"Scene"),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/Player@Walk_Loop","Walk_Loop",1.333333f,"Scene"),
                new CharacterClipAsset(CharacterMotion.CarryWalk,"Models/Player@Walk_Carry_Loop","Walk_Carry_Loop",2f,"Scene"),
                new CharacterClipAsset(CharacterMotion.PushWalk,"Models/Player@Push_Loop","Push_Loop",2.666667f,"Scene")),
            new CharacterArchetypeAsset(JadeId,"社区女顾客 Jade","Models/CustomerJade","Models/Textures/CustomerJade_Albedo",
                "CustomerOrigin","CustomerForward","art-v9/customers/provenance.json",
                "art-v9/customers/model-inspection.json",8263431,8263600,8263709,
                1.62f,new Vector3(1.002f,1.62f,.484f),CharacterRole.Customer,
                "One independent AI customer archetype with baked clothing; not a shared standard/micro-fat modular body. Only idle/walk are present.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerJade","Idle_Loop",2.5f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerJade","Walk_Loop",1.333333f))
            };
            entries.AddRange(CharacterNpcV11Assets.All);
            entries.AddRange(CharacterNpcV15Assets.All);return entries.AsReadOnly();
        }
        public static ReadOnlyCollection<CharacterArchetypeAsset> All=>all;
        public static CharacterArchetypeAsset Find(string id)
        {foreach(var asset in all)if(asset.Id==id)return asset;return null;}

        // Requested motions must be real legacy clips. No procedural/fallback pose is
        // silently substituted or advertised as an authored animation.
        public static bool TryLoad(string id,out LoadedCharacterAsset loaded,out string failure,params CharacterMotion[] required)
        {
            loaded=null;failure=null;var definition=Find(id);
            if(definition==null){failure="Unregistered archetype "+id;return false;}
            var prefab=Resources.Load<GameObject>(definition.ModelResourcePath);
            if(prefab==null){failure=id+": missing prefab "+definition.ModelResourcePath;return false;}
            bool origin=false,forward=false;
            foreach(Transform node in prefab.GetComponentsInChildren<Transform>(true))
            {origin|=node.name==definition.OriginNode;forward|=node.name==definition.ForwardNode;}
            if(!origin||!forward){failure=id+": missing authored origin/forward markers";return false;}
            var result=new Dictionary<CharacterMotion,AnimationClip>();
            foreach(var motion in required)
            {
                if(result.ContainsKey(motion))continue;
                var entry=definition.Clip(motion);
                if(entry==null){failure=id+": authored motion not available: "+motion;return false;}
                AnimationClip match=null;
                foreach(var clip in Resources.LoadAll<AnimationClip>(entry.ResourcePath))
                {
                    if(!clip.legacy||clip.name.StartsWith("__preview__",StringComparison.Ordinal))continue;
                    if(!NameMatches(clip.name,entry.ClipName)&&!NameMatches(clip.name,entry.SourceTakeName))continue;
                    if(match!=null){failure=id+": ambiguous clip "+entry.ClipName;return false;}
                    match=clip;
                }
                if(match==null){failure=id+": missing legacy clip "+entry.ResourcePath+" / "+entry.ClipName;return false;}
                result.Add(motion,match);
            }
            loaded=new LoadedCharacterAsset(definition,prefab,result);return true;
        }
        private static bool NameMatches(string actual,string expected)=>actual==expected ||
            actual.EndsWith("|"+expected,StringComparison.Ordinal) || actual.EndsWith("@"+expected,StringComparison.Ordinal) ||
            actual.EndsWith(":"+expected,StringComparison.Ordinal);
    }
}

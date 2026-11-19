using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    internal static class StreetAudioLevels
    {
        private const string MusicKey = "Guochao_MusicLevel";
        private const string EffectsKey = "Guochao_EffectsLevel";
        private static readonly float[] Gains = { 0f, .35f, .68f, 1f };
        public static int MusicLevel { get; private set; } = Read(MusicKey, 2);
        public static int EffectsLevel { get; private set; } = Read(EffectsKey, PlayerPrefs.GetInt("Guochao_Muted", 0) == 0 ? 3 : 0);
        public static float Music => Gains[MusicLevel];
        public static float Effects => Gains[EffectsLevel];

        private static int Read(string key, int fallback) => Mathf.Clamp(PlayerPrefs.GetInt(key, fallback), 0, Gains.Length - 1);

        public static void Cycle(bool music)
        {
            if (music)
            {
                MusicLevel = (MusicLevel + 1) % Gains.Length;
                PlayerPrefs.SetInt(MusicKey, MusicLevel);
            }
            else
            {
                EffectsLevel = (EffectsLevel + 1) % Gains.Length;
                PlayerPrefs.SetInt(EffectsKey, EffectsLevel);
            }
            PlayerPrefs.Save();
        }
    }

    // Presentation only. The run and sale events remain the sources of truth.
    internal sealed class StreetAudio : MonoBehaviour
    {
        private static StreetAudio active;
        private static readonly string[] HeatSounds = { "sizzle", "water", "steam" };
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastCue = new Dictionary<string, float>();
        private readonly AudioSource[] music = new AudioSource[2];
        private readonly AudioSource[] heat = new AudioSource[HeatSounds.Length];
        private readonly float[] hot = new float[HeatSounds.Length];
        private GameFlow flow;
        private AudioSource ambience, effects;
        private RunState currentRun;
        private Snapshot previous;
        private GamePhase? phase;
        private string musicId = "", ambienceId = "";
        private int musicSlot, district, counterCount;
        private bool armed;
        private BusinessSession session;
        private StreetStallStage stage;
        private LaneSound[] lanes = Array.Empty<LaneSound>();

        private readonly struct LaneSound
        {
            public readonly KitchenStep step;
            public readonly int added, egg, tools;
            public readonly bool cold;
            public LaneSound(KitchenLane lane)
            {
                step = lane.step;
                egg = lane.eggPrepPortions;
                tools = lane.toolClicks;
                cold = lane.freeBatch != null
                    ? FreeKitchenRules.IsColdMethod(lane.freeBatch.method)
                    : BusinessSession.Recipes[lane.recipeIndex].cold;
                added = 0;
                var inputs = lane.freeBatch?.inputs;
                if (inputs != null)
                    foreach (var input in inputs) added += input.units;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!GameFlow.PresentationEnabled || GameFlow.Instance == null) return;
            GameFlow.Instance.gameObject.AddComponent<StreetAudio>();
        }

        private void Awake()
        {
            active = this;
            flow = GetComponent<GameFlow>();
            if (GetComponent<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            for (int i = 0; i < music.Length; i++) music[i] = Source(true);
            for (int i = 0; i < heat.Length; i++) heat[i] = Source(true);
            ambience = Source(true);
            effects = Source(false);
#if !UNITY_WEBGL || UNITY_EDITOR
            armed = true;
#endif
        }

        private AudioSource Source(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.volume = 0;
            return source;
        }

        internal static void UiClick()
        {
            if (active == null) return;
            active.armed = true;
            active.Play("ui", .31f, .09f);
        }

        internal static string MusicFor(GamePhase phase, bool title, bool riding)
        {
            if (title) return "title";
            if (riding) return "travel";
            switch (phase)
            {
                case GamePhase.Home: return "home";
                case GamePhase.Market: return "market";
                case GamePhase.SiteSelection: return "map";
                case GamePhase.Business: return "stall";
                case GamePhase.DaySummary:
                case GamePhase.Night: return "night";
                default: return "";
            }
        }

        private void LateUpdate()
        {
            if (flow.Run == null) return;
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0)) armed = true;
            float dt = Mathf.Min(.075f, Time.unscaledDeltaTime);
            GamePhase now = flow.Run.Phase;
            bool paused = flow.Run.Paused && now != GamePhase.Night;
            bool title = CharacterCreationUI.IsOpen;
            bool riding = flow.MorningTravelPlaying;
            string score = MusicFor(now, title, riding);
            effects.volume = StreetAudioLevels.Effects;
            UpdateScore(score, paused, dt);
            UpdateLoop(ambience, "amb_", ref ambienceId, !paused && (score == "home" || score == "market" || score == "stall") ? score : "", .18f, dt);

            if (phase == GamePhase.Business && now == GamePhase.DaySummary && currentRun == flow.Run)
                Play("close", .53f);

            Snapshot snapshot = flow.State;
            if (!title && previous != null && currentRun == flow.Run && !ReferenceEquals(previous, snapshot))
                ObserveCargo(previous, snapshot);
            previous = snapshot;
            if (currentRun != flow.Run)
            {
                if (currentRun != null) currentRun.CheckoutCompleted -= OnCheckout;
                currentRun = flow.Run;
                currentRun.CheckoutCompleted += OnCheckout;
            }
            phase = now;

            FollowSession(flow.Business?.Session, flow.Business?.Stage);
            if (session != null && !paused && now == GamePhase.SiteSelection && district != session.DistrictIndex)
                Play("position", .38f);
            if (session != null) district = session.DistrictIndex;

            Array.Clear(hot, 0, hot.Length);
            if (session != null && now == GamePhase.Business && session.Started && !session.Finished)
                ObserveKitchen(!paused);
            for (int i = 0; i < heat.Length; i++)
                UpdateHeat(i, hot[i], dt);
        }

        private void ObserveCargo(Snapshot before, Snapshot after)
        {
            if (after.loadedCount > before.loadedCount || !before.stoveLoaded && after.stoveLoaded)
                Play("load", .48f);
        }

        private void FollowSession(BusinessSession next, StreetStallStage nextStage)
        {
            if (ReferenceEquals(session, next) && ReferenceEquals(stage, nextStage)) return;
            Unsubscribe();
            session = next;
            stage = nextStage;
            lanes = Array.Empty<LaneSound>();
            if (session != null)
            {
                session.SaleCompleted += OnSale;
                session.RiskSalePending += OnRisk;
                session.BatchBurned += OnBurn;
                district = session.DistrictIndex;
                counterCount = session.ReadyMeals.Count;
            }
            if (stage != null) stage.CustomerArrived += OnOrder;
        }

        private void ObserveKitchen(bool audible)
        {
            bool baseline = lanes.Length != session.Lanes.Length;
            if (baseline) lanes = new LaneSound[session.Lanes.Length];
            for (int i = 0; i < lanes.Length; i++)
            {
                KitchenLane lane = session.Lanes[i];
                LaneSound before = lanes[i];
                LaneSound now = new LaneSound(lane);
                if (audible && !baseline) ObserveLane(before, now);
                lanes[i] = now;
                if (audible && (now.step == KitchenStep.Heating || now.step == KitchenStep.Utensil && !now.cold))
                {
                    int voice = HeatVoice(lane);
                    hot[voice] = Mathf.Min(.28f, hot[voice] + .17f);
                }
            }
            int count = session.ReadyMeals.Count;
            if (audible && !baseline && count > counterCount) Play("package", .52f, .1f);
            counterCount = count;
        }

        private void ObserveLane(LaneSound before, LaneSound now)
        {
            if (now.added > before.added || now.egg > before.egg || now.step == KitchenStep.Prep && before.step == KitchenStep.Idle)
                Play("ingredient", .46f, .085f);
            if (now.step == KitchenStep.Heating && before.step != now.step)
                Play("ignite", .44f, .14f);
            if (now.step == KitchenStep.Utensil && before.step != now.step)
                Play(now.cold ? "cold" : "ready", now.cold ? .46f : .56f, .12f);
            if (now.tools > before.tools)
                Play(now.cold ? "cold" : "stir", .50f, .09f);
            bool cooked = now.step == KitchenStep.Pack || now.step == KitchenStep.Season || now.step == KitchenStep.ReadyStock;
            if (cooked && before.step == KitchenStep.Utensil)
                Play(now.cold ? "ready" : "stir", .42f, .1f);
            if (now.step == KitchenStep.Pack && before.step == KitchenStep.Season)
                Play("stir", .32f, .09f);
            if (cooked && before.step == KitchenStep.Heating)
                Play("ready", .52f, .12f);
        }

        internal static int HeatVoice(KitchenLane lane)
        {
            string method = lane.freeBatch?.method;
            if (method == FreeKitchenRules.MethodBoil) return 1;
            if (method == FreeKitchenRules.MethodReheat) return 2;
            if (!string.IsNullOrEmpty(method)) return 0;
            StallRecipe recipe = BusinessSession.Recipes[lane.recipeIndex];
            if (recipe.microwave) return 2;
            return Array.IndexOf(recipe.vessels, "V09") >= 0 || Array.IndexOf(recipe.vessels, "V10") >= 0 ? 1 : 0;
        }

        private void OnSale(int orderId, int recipeIndex, int paidCents, bool shortSeasoning)
        {
            if (paidCents > 0) Play("cash", .69f);
        }

        private void OnCheckout(int paidCents)
        {
            if (paidCents > 0) Play("checkout", .53f);
        }

        private void OnRisk(StallRiskEvent incident) => Play("risk", .59f);
        private void OnBurn(int lane, int recipe, int portions) => Play("burn", .64f, .16f);
        private void OnOrder(int order) => Play("order", .48f, .25f);

        private void UpdateScore(string score, bool paused, float dt)
        {
            if (armed && score != musicId)
            {
                // Reversing direction during a crossfade can reuse the previous loop.
                if (score != "" && music[1 - musicSlot].isPlaying && music[1 - musicSlot].clip.name == "bgm_" + score)
                    musicSlot = 1 - musicSlot;
                else if (score != "")
                {
                    musicSlot = 1 - musicSlot;
                    StartClip(music[musicSlot], "bgm_" + score);
                }
                musicId = score;
            }
            for (int i = 0; i < music.Length; i++)
            {
                float volume = i == musicSlot && musicId != "" ? (paused ? .15f : musicId == "stall" ? .34f : .46f) * StreetAudioLevels.Music : 0;
                music[i].volume = Mathf.MoveTowards(music[i].volume, volume, dt * .38f);
                if ((i != musicSlot || musicId == "") && music[i].volume == 0 && music[i].isPlaying) music[i].Stop();
            }
        }

        private void UpdateLoop(AudioSource source, string prefix, ref string current, string id, float volume, float dt)
        {
            if (!armed) return;
            if (id != current && source.volume > 0)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0, dt * .8f);
                return;
            }
            if (id != current)
            {
                current = id;
                if (id == "") source.Stop();
                else StartClip(source, prefix + id);
            }
            source.volume = Mathf.MoveTowards(source.volume, id == "" ? 0 : volume * StreetAudioLevels.Effects, dt * .8f);
        }

        private void UpdateHeat(int voice, float volume, float dt)
        {
            AudioSource source = heat[voice];
            float target = volume * StreetAudioLevels.Effects;
            if (armed && target > 0 && !source.isPlaying) StartClip(source, "cue_" + HeatSounds[voice]);
            source.volume = Mathf.MoveTowards(source.volume, target, dt * 1.3f);
            if (target == 0 && source.volume == 0 && source.isPlaying) source.Stop();
        }

        private void StartClip(AudioSource source, string key)
        {
            source.Stop();
            source.volume = 0;
            source.clip = Load(key);
            if (source.clip != null) source.Play();
        }

        private AudioClip Load(string key)
        {
            if (!clips.TryGetValue(key, out AudioClip clip))
            {
                clip = Resources.Load<AudioClip>("StreetAudio/" + key);
                clips.Add(key, clip);
            }
            return clip;
        }

        private void Play(string id, float volume, float gap = 0)
        {
            if (!armed || StreetAudioLevels.Effects <= 0) return;
            float now = Time.unscaledTime;
            if (gap > 0 && lastCue.TryGetValue(id, out float last) && now - last < gap) return;
            AudioClip clip = Load("cue_" + id);
            if (clip == null) return;
            lastCue[id] = now;
            effects.volume = StreetAudioLevels.Effects;
            effects.PlayOneShot(clip, volume);
        }

        private void Unsubscribe()
        {
            if (session != null)
            {
                session.SaleCompleted -= OnSale;
                session.RiskSalePending -= OnRisk;
                session.BatchBurned -= OnBurn;
            }
            if (stage != null) stage.CustomerArrived -= OnOrder;
        }

        private void OnDestroy()
        {
            if (currentRun != null) currentRun.CheckoutCompleted -= OnCheckout;
            Unsubscribe();
            if (active == this) active = null;
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Scene continuity plus the document's single morning travel stage.
    public sealed class SceneTransition : MonoBehaviour
    {
        public bool Active { get; private set; }
        internal bool MorningTravelPlaying => travel != null && !travel.Complete;
        private CanvasGroup group;
        private Text heading, detail;
        private string destination, description;
        private CinematicTravelStage travel;
        private Button skipButton;
        private bool skipRequested;
        private bool pauseWasActive;
        private int skipBlockedThroughFrame=-1;
        private void Update()
        {
            if(travel==null||travel.Complete||skipRequested)return;
            if(GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused){pauseWasActive=true;return;}
            // Space can submit the focused Resume button. That same key-down
            // must not also skip the journey after the pause menu closes.
            if(pauseWasActive){pauseWasActive=false;skipBlockedThroughFrame=Time.frameCount;return;}
            if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.JoystickButton3))SkipTravel();
        }
        // Skipping is presentation-only: the travel loop then exits normally,
        // so arrival fires exactly once and no cargo, money or market time
        // changes. Pausing (Esc) blocks both the button and the key, and the
        // method re-checks the pause state itself so a queued click at the
        // pause boundary can never skip either.
        private void SkipTravel()
        {
            if(travel==null||travel.Complete||skipRequested)return;
            if(pauseWasActive||Time.frameCount<=skipBlockedThroughFrame)return;
            if(GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused)return;
            skipRequested=true;travel.SkipToEnd();
            if(skipButton!=null)skipButton.interactable=false;
        }
        private void EnsureCanvas()
        {
            if(group!=null)return;
            GameObject root=new GameObject("JourneyTransition",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false);
            Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=500;
            CanvasScaler scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            group=root.GetComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;
            GameObject shade=new GameObject("WarmPaperFade",typeof(RectTransform),typeof(Image));shade.transform.SetParent(root.transform,false);
            RectTransform rt=shade.GetComponent<RectTransform>();rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
            shade.GetComponent<Image>().color=CampaignUI.Pale;
            heading=Label(root.transform,"Destination",38,new Vector2(0,25));
            detail=Label(root.transform,"JourneyCaption",21,new Vector2(0,-40));
        }
        // The fading transition group goes transparent mid-travel, so the skip
        // control lives on its own always-opaque canvas above the pause shade
        // (sorting 560 > 500) and is shown/hidden with SetActive. It is created
        // only after the travel stage suspends every other canvas, so
        // CinematicTravelStage never disables it; hidden canvases are excluded
        // from later suspension sweeps as well.
        private void EnsureSkipOverlay()
        {
            if(skipButton!=null)return;
            GameObject root=new GameObject("TravelSkipOverlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false); // overlay canvas ignores the parent transform; parenting only ties its lifetime to this stage
            Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=560;
            CanvasScaler scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            CanvasGroup groupOverlay=root.GetComponent<CanvasGroup>();groupOverlay.alpha=1;groupOverlay.blocksRaycasts=true;
            GameObject go=new GameObject("SkipTravel",typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(root.transform,false);
            RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(1,0);rt.pivot=new Vector2(1,0);rt.anchoredPosition=new Vector2(-26,26);rt.sizeDelta=new Vector2(196,52);
            Image face=go.GetComponent<Image>();face.color=CampaignUI.PaperColor;face.raycastTarget=true;
            Button button=go.GetComponent<Button>();button.targetGraphic=face;button.colors=CampaignUI.ButtonColours(button.colors);
            Text label=Label(go.transform,"SkipTravelLabel",20,Vector2.zero);
            label.rectTransform.sizeDelta=new Vector2(180,44);label.text="跳过行程 · Space";
            skipButton=button;root.SetActive(false);
        }
        private static Text Label(Transform parent,string name,int size,Vector2 offset)
        {
            GameObject go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
            rt.anchoredPosition=offset;rt.sizeDelta=new Vector2(1000,90);
            Text text=go.GetComponent<Text>();text.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");text.fontSize=size;
            text.color=CampaignUI.Ink;text.alignment=TextAnchor.MiddleCenter;text.verticalOverflow=VerticalWrapMode.Overflow;
            text.raycastTarget=false;return text;
        }
        public void Play(string title,string subtitle,Action atBlack,Action finished)
        {
            Cancel();EnsureCanvas();destination=title;description=subtitle;Active=true;group.blocksRaycasts=true;skipRequested=false;
            StartCoroutine(Run(atBlack,finished));
        }
        public void PlayTravel(bool stoveLoaded,Action atArrival,Action finished,string[] equipment=null,float duration=15,string[] cargo=null,string[] packages=null)
        {
            Cancel();EnsureCanvas();destination="清晨 · 出发采购";description="出租屋 → 惠民仓储超市";
            Active=true;group.blocksRaycasts=true;skipRequested=false;pauseWasActive=false;skipBlockedThroughFrame=-1;
            StartCoroutine(RunTravel(stoveLoaded,atArrival,finished,equipment,duration,cargo,packages));
        }
        private IEnumerator RunTravel(bool stoveLoaded,Action atArrival,Action finished,string[] equipment,float duration,string[] cargo,string[] packages)
        {
            yield return Fade(0,1,.30f);
            GameObject stageObject=new GameObject("MorningTravelStage");
            travel=stageObject.AddComponent<CinematicTravelStage>();
            travel.Initialize(stoveLoaded,group.GetComponent<Canvas>(),equipment,duration,cargo,packages);
            EnsureSkipOverlay();skipRequested=false;
            if(skipButton!=null){skipButton.transform.parent.gameObject.SetActive(true);skipButton.interactable=true;skipButton.onClick.RemoveAllListeners();skipButton.onClick.AddListener(SkipTravel);}
            while(!travel.Complete)
            {
                bool paused=GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused;
                travel.Advance(paused?0:Time.unscaledDeltaTime,paused);
                float entry=1-Mathf.SmoothStep(0,1,travel.PhaseTime/.5f);
                float exit=Mathf.SmoothStep(0,1,(travel.PhaseTime-(CinematicTravelStage.Duration-.5f))/.5f);
                group.alpha=paused?.88f:Mathf.Max(entry,exit);
                heading.text=paused?"行程已暂停":travel.PhaseTime>13?"惠民仓储超市":"清晨 · 出发采购";
                detail.text=paused?"按 Esc 继续":"";
                if(skipButton!=null)skipButton.interactable=!paused&&!skipRequested;
                yield return null;
            }
            if(skipButton!=null)skipButton.transform.parent.gameObject.SetActive(false);
            group.alpha=1;
            travel.Release();Destroy(travel.gameObject);travel=null;
            destination="惠民仓储超市";description="停好三轮车 · 开始采购";
            atArrival?.Invoke();
            yield return Fade(1,0,.45f);
            Active=false;group.blocksRaycasts=false;finished?.Invoke();
        }
        private IEnumerator Run(Action atBlack,Action finished)
        {
            yield return Fade(0,1,.38f);
            while(GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused)
            { heading.text="行程已暂停";detail.text="按 Esc 继续";yield return null; }
            atBlack?.Invoke();
            yield return Fade(1,1,.55f);
            yield return Fade(1,0,.45f);
            Active=false;group.blocksRaycasts=false;finished?.Invoke();
        }
        private IEnumerator Fade(float from,float to,float seconds)
        {
            float elapsed=0;
            while(elapsed<seconds)
            {
                bool paused=GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused;
                if(!paused)elapsed+=Time.unscaledDeltaTime;
                float alpha=Mathf.SmoothStep(from,to,Mathf.Clamp01(elapsed/seconds));
                group.alpha=paused?Mathf.Max(.85f,alpha):alpha;
                heading.text=paused?"行程已暂停":destination;
                detail.text=paused?"按 Esc 继续":description;
                yield return null;
            }
            group.alpha=to;
        }
        public void Cancel()
        {
            StopAllCoroutines();Active=false;
            if(travel!=null){travel.Release();Destroy(travel.gameObject);travel=null;}
            if(skipButton!=null)skipButton.transform.parent.gameObject.SetActive(false);
            if(group!=null){group.alpha=0;group.blocksRaycasts=false;}
        }
        private void OnDestroy(){if(travel!=null){travel.Release();Destroy(travel.gameObject);}}
    }
}

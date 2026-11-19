using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // A single pause surface for exploration and business. Saving belongs to
    // GameFlow; this screen never mutates the campaign or claims a save succeeded.
    public sealed class CampaignMenuUI : MonoBehaviour
    {
        private static CampaignMenuUI instance;
        public static bool IsOpen=>instance!=null;
        private GameFlow flow;
        private Text status;
        private CampaignFocusScope focusScope;

        public static void Open(GameFlow game)
        {
            if(instance!=null||game==null||NightHubUI.IsOpen||CharacterCreationUI.IsOpen)return;
            instance=new GameObject("Campaign pause menu").AddComponent<CampaignMenuUI>();
            instance.flow=game;instance.Build();
        }
        public static void Close()
        {
            if(instance==null)return;
            var closing=instance;instance=null;closing.focusScope?.Restore();
            closing.gameObject.SetActive(false);Destroy(closing.gameObject);
        }
        public static void Resume()
        {
            GameFlow game=instance!=null?instance.flow:GameFlow.Instance;
            Close();game?.SetPaused(false);
        }
        private void Build()
        {
            RectTransform safe=CampaignUI.Canvas(transform,"Pause canvas",1400,CampaignUI.WithAlpha(CampaignUI.Ink,.78f));
            RectTransform card=CampaignUI.Paper(safe,"Clock stopped",new Vector2(680,660),CampaignUI.PaperColor);
            string phase=flow.Run.Phase==GamePhase.Market?"采购途中":flow.Run.Phase==GamePhase.Business?"营业中":flow.Run.Phase==GamePhase.SiteSelection?"挑选摊位":"家中整备";
            CampaignUI.Text(card,(flow.Campaign!=null?"第 "+flow.Campaign.dayIndex+" 天 · ":"")+phase,new Vector2(0,255),new Vector2(560,40),20,CampaignUI.Muted,TextAnchor.MiddleCenter);
            CampaignUI.Text(card,"歇一会儿",new Vector2(0,198),new Vector2(560,70),42,CampaignUI.Ink,TextAnchor.MiddleCenter,true);
            CampaignUI.Rule(card,new Vector2(0,142),new Vector2(562,2));
            CampaignUI.Text(card,"时间和手上的动作都已停下",new Vector2(0,112),new Vector2(574,42),22,CampaignUI.Muted,TextAnchor.MiddleCenter);
            Button resume=CampaignUI.Button(card,"继续游戏",new Vector2(0,42),new Vector2(500,64),Resume,true);
            bool canSave=flow.Campaign!=null&&(flow.Business==null||!flow.Business.Session.Setup.practice);
            CampaignUI.Button(card,"保存当前进度",new Vector2(0,-40),new Vector2(500,60),()=>{flow.SaveCampaign();RefreshStatus();}).interactable=canSave;
            CampaignUI.Button(card,canSave?"保存并回到开局":"离开练习 · 返回开局",new Vector2(0,-122),new Vector2(500,60),()=>
            {
                if(canSave){flow.SaveAndReturnToTitle();if(instance!=null)RefreshStatus();}
                else{Close();flow.OpenCharacterCreation();}
            });
            status=CampaignUI.Text(card,"",new Vector2(0,-184),new Vector2(574,48),19,CampaignUI.Muted,TextAnchor.MiddleCenter);
            AudioSetting(card, true, -128);
            AudioSetting(card, false, 128);
            CampaignUI.Text(card,"Esc 继续",new Vector2(0,-294),new Vector2(500,32),18,CampaignUI.Muted,TextAnchor.MiddleCenter);
            RefreshStatus();focusScope=new CampaignFocusScope(transform);CampaignUI.Focus(resume);
            flow.Changed+=OnChanged;
        }
        private static void AudioSetting(Transform parent, bool music, float x)
        {
            string Title()
            {
                int level = music ? StreetAudioLevels.MusicLevel : StreetAudioLevels.EffectsLevel;
                return (music ? "音乐" : "音效") + " · " + (level == 0 ? "关" : level == 1 ? "低" : level == 2 ? "中" : "高");
            }
            Text value = null;
            var button = CampaignUI.Button(parent, Title(), new Vector2(x, -244), new Vector2(240, 48), () =>
            {
                StreetAudioLevels.Cycle(music);
                value.text = Title();
            });
            value = button.GetComponentInChildren<Text>();
        }
        private void OnChanged(Snapshot snapshot){RefreshStatus();}
        private void RefreshStatus()
        {
            if(status==null||flow==null)return;
            string value=flow.Campaign==null||flow.Business?.Session.Setup.practice==true?"练习场不覆盖经营存档。":flow.SaveStatus;
            value=CampaignUI.SaveFeedback(value);if(status.text!=value)status.text=value;
        }
        private void OnDestroy()
        {if(flow!=null)flow.Changed-=OnChanged;focusScope?.Restore();if(instance==this)instance=null;}
    }

    // Shared restrained paper/enamel vocabulary. Real inventory portraits are
    // supplied by GameItemIconLibrary; no generated labels pretend to be items.
    internal static class CampaignUI
    {
        // Shared paper / ink-blue / vermilion palette. Forest is retained only
        // as a source-compatible alias for ink-blue; no green theme remains.
        public static readonly Color Ink=new Color32(35,56,77,255),PaperColor=new Color32(246,240,227,255),
            Forest=Ink,Muted=new Color32(101,112,122,255),Rust=new Color32(146,45,42,255),
            Pale=new Color32(222,216,205,255),Vermilion=new Color32(185,71,50,255),
            Gold=new Color32(200,148,61,255),Selected=new Color32(234,213,175,255),
            Background=new Color32(233,228,218,255);
        public static Color WithAlpha(Color colour,float alpha){colour.a=alpha;return colour;}
        public static ColorBlock ButtonColours(ColorBlock colours)
        {
            colours.normalColor=Color.white;colours.highlightedColor=new Color32(255,245,226,255);
            colours.selectedColor=new Color32(255,226,174,255);colours.pressedColor=new Color32(224,203,169,255);
            colours.disabledColor=new Color32(180,174,165,173);return colours;
        }
        public static RectTransform Node(Transform parent,string name)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();}
        public static void Stretch(RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        public static RectTransform Canvas(Transform parent,string name,int order,Color shade)
        {
            RectTransform rect=Node(parent,name);var canvas=rect.gameObject.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=order;
            var scale=rect.gameObject.AddComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=1;
            rect.gameObject.AddComponent<GraphicRaycaster>();var image=rect.gameObject.AddComponent<Image>();image.color=shade;image.raycastTarget=true;
            if(EventSystem.current==null)new GameObject("Campaign event system",typeof(EventSystem),typeof(StandaloneInputModule));
            RectTransform safe=Node(rect,"Safe area");Stretch(safe);safe.gameObject.AddComponent<CampaignSafeArea>();return safe;
        }
        public static RectTransform Paper(Transform parent,string name,Vector2 size,Color colour)
        {
            RectTransform rt=Node(parent,name);rt.sizeDelta=size;
            // CanvasRenderer before Graphic, same native-component contract as UGUI Image;
            // Node() only guarantees RectTransform, so explicit ordering is deterministic.
            rt.gameObject.AddComponent<CanvasRenderer>();
            var face=rt.gameObject.AddComponent<CampaignPaper>();face.color=colour;
            var shadow=rt.gameObject.AddComponent<Shadow>();shadow.effectColor=WithAlpha(Ink,.35f);shadow.effectDistance=new Vector2(2,-8);
            return rt;
        }
        public static Text Text(Transform parent,string value,Vector2 at,Vector2 size,int pixels,Color colour,TextAnchor align=TextAnchor.MiddleLeft,bool bold=false)
        {
            RectTransform rt=Node(parent,"Text");rt.anchoredPosition=at;rt.sizeDelta=size;
            var label=rt.gameObject.AddComponent<UnityEngine.UI.Text>();label.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            label.fontSize=pixels;label.color=colour;label.alignment=align;label.text=value;label.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;
            label.raycastTarget=false;label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;return label;
        }
        public static void Rule(Transform parent,Vector2 at,Vector2 size)
        {RectTransform rt=Node(parent,"Paper rule");rt.anchoredPosition=at;rt.sizeDelta=size;Image image=rt.gameObject.AddComponent<Image>();image.color=WithAlpha(Ink,.22f);image.raycastTarget=false;}
        public static Button Button(Transform parent,string title,Vector2 at,Vector2 size,Action action,bool primary=false)
        {
            RectTransform rt=Paper(parent,title,size,primary?Vermilion:Pale);rt.anchoredPosition=at;
            var button=rt.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=rt.GetComponent<CampaignPaper>();button.onClick.AddListener(()=>{action?.Invoke();StreetAudio.UiClick();});
            ColorBlock colours=ButtonColours(button.colors);colours.fadeDuration=.08f;button.colors=colours;
            Text(rt,title,Vector2.zero,size-new Vector2(30,8),23,primary?PaperColor:Ink,TextAnchor.MiddleCenter,true);return button;
        }
        public static RectTransform Scroll(Transform parent,string name,Vector2 at,Vector2 size,out ScrollRect scroll)
        {
            RectTransform frame=Node(parent,name);frame.sizeDelta=size;frame.anchoredPosition=at;
            Image hit=frame.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,.002f);
            RectTransform viewport=Node(frame,"Viewport");Stretch(viewport);viewport.offsetMax=new Vector2(-16,0);viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content=Node(viewport,"Content");content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,size.y);
            scroll=frame.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=42;scroll.inertia=true;
            RectTransform track=Node(frame,"Scroll rail");track.anchorMin=new Vector2(1,0);track.anchorMax=Vector2.one;track.pivot=new Vector2(1,.5f);track.sizeDelta=new Vector2(9,0);track.anchoredPosition=Vector2.zero;
            Image trackImage=track.gameObject.AddComponent<Image>();trackImage.color=WithAlpha(Ink,.12f);
            RectTransform thumb=Node(track,"Scroll thumb");Stretch(thumb);Image thumbImage=thumb.gameObject.AddComponent<Image>();thumbImage.color=Gold;
            Scrollbar bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=thumb;bar.targetGraphic=thumbImage;bar.direction=Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;return content;
        }
        public static void Icon(Transform parent,Sprite sprite,Vector2 at,Vector2 size)
        {if(sprite==null)return;RectTransform rt=Node(parent,"Actual item portrait");rt.anchoredPosition=at;rt.sizeDelta=size;var image=rt.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;}
        public static void Focus(Selectable control)
        {if(EventSystem.current!=null&&control!=null&&control.IsInteractable())EventSystem.current.SetSelectedGameObject(control.gameObject);}
        public static string SaveFeedback(string value)=>!string.IsNullOrEmpty(value)&&value.StartsWith("未能保存",StringComparison.Ordinal)?"未能保存。当前进度仍在，请稍后重试。":value??"";
        public static string Money(int cents)=>CommerceReceiptData.Money(cents);
        public static string SignedMoney(int cents)=>(cents>=0?"+":"−")+Money(Math.Abs(cents));
    }

    internal sealed class CampaignFocusScope
    {
        private readonly Dictionary<Selectable,bool> states=new Dictionary<Selectable,bool>();
        private readonly GameObject previous;
        private bool restored;
        public CampaignFocusScope(Transform modal)
        {
            previous=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            foreach(Selectable selectable in Selectable.allSelectablesArray)
                if(selectable!=null&&!selectable.transform.IsChildOf(modal)){states[selectable]=selectable.interactable;selectable.interactable=false;}
        }
        public void Restore()
        {
            if(restored)return;restored=true;
            foreach(var pair in states)if(pair.Key!=null)pair.Key.interactable=pair.Value;
            if(previous!=null&&previous.activeInHierarchy&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(previous);
        }
    }

    public sealed class CampaignSafeArea : MonoBehaviour
    {
        private Rect previous;private Vector2Int screen;
        private void OnEnable(){Apply();}
        private void Update(){if(previous!=Screen.safeArea||screen!=new Vector2Int(Screen.width,Screen.height))Apply();}
        private void Apply()
        {
            if(Screen.width<=0||Screen.height<=0)return;previous=Screen.safeArea;screen=new Vector2Int(Screen.width,Screen.height);
            var rt=(RectTransform)transform;rt.anchorMin=new Vector2(previous.xMin/Screen.width,previous.yMin/Screen.height);rt.anchorMax=new Vector2(previous.xMax/Screen.width,previous.yMax/Screen.height);rt.offsetMin=rt.offsetMax=Vector2.zero;
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CampaignPaper : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();Rect r=rectTransform.rect;float cut=Mathf.Min(17,Mathf.Min(r.width,r.height)*.18f);
            Vector2[] rim={new Vector2(r.xMin+cut,r.yMin),new Vector2(r.xMax-cut,r.yMin),new Vector2(r.xMax,r.yMin+cut),new Vector2(r.xMax,r.yMax-cut),new Vector2(r.xMax-cut,r.yMax),new Vector2(r.xMin+cut,r.yMax),new Vector2(r.xMin,r.yMax-cut),new Vector2(r.xMin,r.yMin+cut)};
            mesh.AddVert(r.center,color,Vector2.zero);foreach(Vector2 p in rim)mesh.AddVert(p,color,Vector2.zero);
            for(int i=0;i<rim.Length;i++)mesh.AddTriangle(0,i+1,(i+1)%rim.Length+1);
        }
    }
}

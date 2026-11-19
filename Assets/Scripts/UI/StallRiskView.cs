using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // A01 假扫码逃单客的视觉表现（EVENT7 / EVENT7R）：
    //  · 正式集成优先复用已拿到餐的真实顾客：Stage 适配器实现 IStallRiskActor 并经
    //    Director.spawn 回调交给 Director；View 用 PresentExistingActor 只挂头顶 UI，
    //    绝不销毁 Stage 拥有的 body。
    //  · fallback Spawn 演员为暂用外观（已验收顾客模型复用；专用“黑鸭舌帽口罩”AI 模型另排）。
    //  · 头顶显著小图标 + 拦截倒计时环（Image Filled Radial360 真圆环）+“拦截！”小按钮。
    //  · 结算只闪一行短浮字（+¥已追回 / 逃单损失），不弹说明卡、不挡锅。
    //  · 米白/墨蓝/朱红/暖金四色，无绿色。
    //  · actor 生命周期唯一所有权在 Director；本类持有的 presented 是非拥有引用，
    //    EventSettled 后即清空，绝不调用 Dispose（EVENT7R 修复 View/Director 双引用不一致）。
    public sealed class StallRiskView : MonoBehaviour
    {
        // 现行米白/墨蓝/朱红/暖金
        public static readonly Color InkBlue=new Color32(0x1F,0x2A,0x44,0xFF);
        public static readonly Color Vermilion=new Color32(0xC7,0x3E,0x1D,0xFF);
        public static readonly Color WarmGold=new Color32(0xD9,0xA4,0x41,0xFF);
        public static readonly Color Cream=new Color32(0xF3,0xED,0xE2,0xFF);
        // 世界空间 UI 缩放：220px 头顶牌 ≈ 1.1m（EVENT7R 修复原 scale=1 数百米大问题）。
        private const float WorldUiScale=.005f;
        // 中文用工程实际资源 NotoSansCJKsc-Regular（Resources/UI/Fonts/），
        // 缺资源时退 LegacyRuntime.ttf 并告警（EVENT7R 修复原中文字形缺失）。
        private const string CjkFontResource="UI/Fonts/NotoSansCJKsc-Regular";
        private static Font LoadCjkFont()
        {
            var font=Resources.Load<Font>(CjkFontResource);
            if(font==null)Debug.LogWarning("[EVENT7] 缺中文字体资源 "+CjkFontResource+"，退 LegacyRuntime.ttf（中文可能缺字形）");
            return font!=null?font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private StallRiskDirector director;
        private Camera view;
        private IStallRiskActor presented;      // 非拥有引用（所有权在 Director/Stage）
        // RISK7SCENE：拦截入口由控制器统一把关（暂停/收摊/未开场一律无效），
        // 与实体点击顾客身体走同一 TryRiskIntercept 入口，绝不绕开控制器直调 Director。
        public Func<bool> CanIntercept;
        public Func<int,bool> InterceptHandler; // 带 orderId（头顶按钮 = 当前呈现演员）
        private Image countdownFill;
        private Text countdownLabel;
        private Button interceptButton;
        private Canvas canvas;
        private float flashRemaining;
        private Text flashText;
        private Canvas flashCanvas;

        public static StallRiskView Create(Transform parent,StallRiskDirector director,Camera viewCamera)
        {
            var go=new GameObject("StallRiskView");
            go.transform.SetParent(parent,false);
            var v=go.AddComponent<StallRiskView>();
            v.director=director;v.view=viewCamera;
            director.EventSettled+=v.OnSettled;
            return v;
        }
        public void Tick(float dt)
        {
            var e=director!=null?director.ActiveEvent:null;
            bool show=e!=null&&!e.Resolved;
            if(canvas!=null&&canvas.gameObject.activeSelf!=show)canvas.gameObject.SetActive(show);
            if(interceptButton!=null)interceptButton.interactable=CanIntercept==null||CanIntercept();
            if(show&&countdownFill!=null)
            {
                countdownFill.fillAmount=e.InterceptSeconds>0?Mathf.Clamp01(e.Remaining/e.InterceptSeconds):0f;
                if(countdownLabel!=null)countdownLabel.text=Mathf.CeilToInt(Mathf.Max(0f,e.Remaining)).ToString();
                FaceCamera();
            }
            if(flashRemaining>0f)
            {
                flashRemaining-=dt;
                if(flashText!=null)flashText.transform.position+=Vector3.up*(dt*.35f);
                if(flashRemaining<=0f)
                {
                    if(flashText!=null)Destroy(flashText.gameObject);
                    if(flashCanvas!=null)Destroy(flashCanvas.gameObject);
                    flashText=null;flashCanvas=null;
                }
            }
        }
        public void Dispose()
        {
            if(director!=null){director.EventSettled-=OnSettled;director=null;}
            CanIntercept=null;InterceptHandler=null;
            presented=null; // 非拥有：body 由 Director/Stage 清理
            if(canvas!=null)Destroy(canvas.gameObject);
            if(flashText!=null)Destroy(flashText.gameObject);
            if(flashCanvas!=null)Destroy(flashCanvas.gameObject);
            Destroy(gameObject);
        }
        private void OnSettled(StallRiskEvent e,bool recovered)
        {
            if(canvas!=null)canvas.gameObject.SetActive(false);
            // 一行短浮字：成功暖金 +¥，失败朱红 逃单损失；无大卡片。
            // 锚定当前演员头顶（退场动画期间位置仍有效），随后即清空非拥有引用。
            string note=recovered?"+¥"+(e.CollectCents/100f).ToString("0.00")+" 已追回":"逃单 -¥"+(e.PriceCents/100f).ToString("0.00");
            var anchor=presented!=null?presented.Anchor:null;
            ShowFlash(note,recovered?WarmGold:Vermilion,anchor);
            presented=null;
        }
        // 结算浮字：挂在自己的小型世界 Canvas 上（EVENT7R 修复原 Text 不在任何 Canvas
        // 漂到世界原点），随 view 相机朝向，到时整体销毁。
        private void ShowFlash(string note,Color color,Transform anchor)
        {
            if(flashText!=null)Destroy(flashText.gameObject);
            if(flashCanvas!=null)Destroy(flashCanvas.gameObject);
            var go=new GameObject("RiskFlashCanvas");
            go.transform.SetParent(transform,false);
            flashCanvas=go.AddComponent<Canvas>();flashCanvas.renderMode=RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>(); // 浮字不参与点击，不加 GraphicRaycaster
            flashCanvas.worldCamera=view;
            go.transform.localScale=Vector3.one*WorldUiScale;
            go.transform.position=(anchor!=null?anchor.position:transform.position)+Vector3.up*1.9f;
            if(view!=null)go.transform.forward=(go.transform.position-view.transform.position).normalized;
            var t=new GameObject("Text",typeof(Text));t.transform.SetParent(go.transform,false);
            flashText=t.GetComponent<Text>();flashText.text=note;flashText.font=LoadCjkFont();flashText.fontSize=26;flashText.fontStyle=FontStyle.Bold;
            flashText.color=color;flashText.alignment=TextAnchor.MiddleCenter;flashText.raycastTarget=false;
            flashText.rectTransform.sizeDelta=new Vector2(360,44);
            flashRemaining=1.4f;
        }
        // ── 展示入口 ──
        // RISK7SCENE：生产接线禁止 fallback 新生第二个顾客（Controller 已改为
        // 无活顾客不采纳 + Attach 失败只告警）。本方法仅保留给旧夹具/调试参考。
        public bool PresentActor(StallRiskEvent e,string archetypeId,Vector3 spawnPosition,Vector3 counterPosition)
        {
            if(e==null||presented!=null)return false;
            var spawned=StallRiskActor.Spawn(e.OrderId,archetypeId,spawnPosition,counterPosition);
            if(spawned==null)return false;
            presented=spawned;
            BuildHeadUI(spawned.Anchor);
            return true;
        }
        // 正式集成（EVENT7R 新增）：复用 Stage 已交给 Director 的真实顾客适配器。
        // Stage 适配器提供 Anchor 并通过 Director 回调接收成功/失败；本方法只挂头顶 UI，
        // 绝不销毁 Stage 拥有的 body（Director 是唯一 Dispose 调用方，适配器内部归还）。
        public bool PresentExistingActor(StallRiskEvent e,IStallRiskActor existing)
        {
            if(e==null||existing==null||existing.Anchor==null||presented!=null)return false;
            presented=existing;
            BuildHeadUI(existing.Anchor);
            return true;
        }
        private void BuildHeadUI(Transform anchor)
        {
            if(canvas!=null)Destroy(canvas.gameObject); // 第二事件重建前清掉上一轮隐藏牌
            var go=new GameObject("RiskHeadUI");
            go.transform.SetParent(anchor,false);
            go.transform.localPosition=Vector3.up*.35f;
            canvas=go.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            canvas.worldCamera=view; // EVENT7R：显式事件相机
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>(); // EVENT7R：真实可点事件管线
            go.transform.localScale=Vector3.one*WorldUiScale; // EVENT7R：220px → 1.1m
            var root=go.GetComponent<RectTransform>();root.sizeDelta=new Vector2(220,220);
            var font=LoadCjkFont();
            // 警告图标：朱红圆底 + 米白“!”（装饰，不挡点击）
            var icon=new GameObject("Icon",typeof(Image));icon.transform.SetParent(go.transform,false);
            var img=icon.GetComponent<Image>();img.color=Vermilion;img.raycastTarget=false;
            var irt=icon.GetComponent<RectTransform>();irt.sizeDelta=new Vector2(64,64);irt.anchoredPosition=new Vector2(0,70);
            var mark=new GameObject("Mark",typeof(Text));mark.transform.SetParent(icon.transform,false);
            var mt=mark.GetComponent<Text>();mt.text="!";mt.font=font;mt.fontSize=44;mt.fontStyle=FontStyle.Bold;mt.color=Cream;mt.alignment=TextAnchor.MiddleCenter;mt.raycastTarget=false;
            mt.rectTransform.sizeDelta=new Vector2(64,64);
            // 倒计时环：真圆环（Image Type.Filled / Radial360，墨蓝底 + 朱红余量），装饰不挡点击
            var ring=new GameObject("Ring",typeof(Image));ring.transform.SetParent(go.transform,false);
            var rim=ring.GetComponent<Image>();rim.type=Image.Type.Filled;rim.fillMethod=Image.FillMethod.Radial360;rim.fillOrigin=2;rim.color=InkBlue;rim.raycastTarget=false;
            var rrt=ring.GetComponent<RectTransform>();rrt.sizeDelta=new Vector2(96,96);rrt.anchoredPosition=new Vector2(0,70);
            var fill=new GameObject("Fill",typeof(Image));fill.transform.SetParent(ring.transform,false);
            var fim=fill.GetComponent<Image>();fim.type=Image.Type.Filled;fim.fillMethod=Image.FillMethod.Radial360;fim.fillOrigin=2;fim.color=Vermilion;fim.raycastTarget=false;
            var frt=fim.rectTransform;frt.anchorMin=Vector2.zero;frt.anchorMax=Vector2.one;frt.sizeDelta=Vector2.zero;
            countdownFill=fim;
            var secs=new GameObject("Seconds",typeof(Text));secs.transform.SetParent(ring.transform,false);
            countdownLabel=secs.GetComponent<Text>();countdownLabel.font=font;countdownLabel.fontSize=34;countdownLabel.fontStyle=FontStyle.Bold;countdownLabel.color=Cream;countdownLabel.alignment=TextAnchor.MiddleCenter;countdownLabel.raycastTarget=false;
            countdownLabel.rectTransform.sizeDelta=new Vector2(96,96);
            // 拦截按钮：朱红底（唯一 raycastTarget 目标，真实可点）+ 米白字
            var btn=new GameObject("Intercept",typeof(Image),typeof(Button));btn.transform.SetParent(go.transform,false);
            var bim=btn.GetComponent<Image>();bim.color=Vermilion;
            var brt=btn.GetComponent<RectTransform>();brt.sizeDelta=new Vector2(150,52);brt.anchoredPosition=new Vector2(0,-20);
            interceptButton=btn.GetComponent<Button>();interceptButton.targetGraphic=bim;
            interceptButton.onClick.AddListener(OnInterceptClicked);
            var bl=new GameObject("Label",typeof(Text));bl.transform.SetParent(btn.transform,false);
            var bt=bl.GetComponent<Text>();bt.text="拦截！";bt.font=font;bt.fontSize=26;bt.fontStyle=FontStyle.Bold;bt.color=Cream;bt.alignment=TextAnchor.MiddleCenter;bt.raycastTarget=false;
            bt.rectTransform.sizeDelta=new Vector2(150,52);
        }
        private void OnInterceptClicked()
        {
            if(CanIntercept!=null&&!CanIntercept())return; // 暂停/收摊/未开场：按钮存在但无效
            if(InterceptHandler!=null&&presented!=null)InterceptHandler(presented.OrderId);
        }
        private void FaceCamera()
        {
            if(canvas==null||view==null)return;
            var t=canvas.transform;t.forward=(t.position-view.transform.position).normalized;
        }
    }

    // A01 fallback 演员：复用已验收顾客模型 + 真实 Idle/Walk 动画。
    // 暂用外观（专用 AI 模型另排，已在类注释标注）。
    // EVENT7R：装配对齐 CustomerCrowd.Add——OriginNode/ForwardNode 脚底与朝向对齐、
    // 原 albedo 材质（Standard, gloss .18, 卡通渲染经 GameArtRendering.Apply）、
    // 禁用 Animator 走 Legacy Animation；不再把预制体根上当现成可用。
    // 生命周期：OnEscaped 只切 Flee 相位；3s 退场与销毁由 StallRiskDirector.ExitSeconds 负责。
    public sealed class StallRiskActor : IStallRiskActor
    {
        public int OrderId{get;}
        public Transform Anchor=>anchor;
        private readonly GameObject root;      // 本 actor 自建的根（Dispose 销毁它）
        private readonly Animation animation;
        private readonly Material ownedMaterial; // 自建 albedo 材质，Dispose 一并销毁
        private readonly string idle,walk;
        private readonly Vector3 counter;
        private enum Phase{Approach,Idle,Flee}
        private Phase phase=Phase.Approach;
        private const float WalkSpeed=1.4f;
        private readonly Transform anchor;
        private StallRiskActor(int orderId,GameObject root,Animation animation,Material ownedMaterial,string idle,string walk,Vector3 counterPosition,Transform anchor)
        {
            OrderId=orderId;this.root=root;this.animation=animation;this.ownedMaterial=ownedMaterial;this.idle=idle;this.walk=walk;counter=counterPosition;this.anchor=anchor;
        }
        public static StallRiskActor Spawn(int orderId,string archetypeId,Vector3 spawnPosition,Vector3 counterPosition)
        {
            if(string.IsNullOrEmpty(archetypeId))archetypeId=CharacterAssetCatalog.JadeId;
            if(!CharacterAssetCatalog.TryLoad(archetypeId,out var loaded,out string failure,CharacterMotion.Idle,CharacterMotion.Walk))
            {
                Debug.LogError("EVENT7 A01 actor load failed: "+failure);
                return null;
            }
            var def=loaded.Definition;
            var root=new GameObject("A01FakeScanner(order "+orderId+")");
            root.transform.position=spawnPosition;
            var visual=UnityEngine.Object.Instantiate(loaded.Prefab,root.transform);
            visual.transform.localPosition=Vector3.zero;
            visual.name=def.Id+"_Model";
            // 脚底/朝向对齐（对齐 CustomerCrowd.Add 的做法）
            Transform authoredOrigin=null,authoredForward=null;
            foreach(Transform t in visual.GetComponentsInChildren<Transform>(true))
            { if(t.name==def.OriginNode)authoredOrigin=t; if(t.name==def.ForwardNode)authoredForward=t; }
            if(authoredOrigin!=null)
            {
                Vector3 offset=authoredOrigin.position-visual.transform.position; // OriginNode 落到根原点 = 脚底贴地
                visual.transform.position-=offset;
            }
            if(authoredOrigin!=null&&authoredForward!=null)
            {
                Vector3 authored=authoredForward.position-authoredOrigin.position;authored.y=0f;
                Vector3 target=counterPosition-spawnPosition;target.y=0f;
                if(authored.sqrMagnitude>.001f&&target.sqrMagnitude>.001f)
                    visual.transform.rotation=Quaternion.FromToRotation(authored.normalized,target.normalized)*visual.transform.rotation;
            }
            // 原 albedo 材质（对齐 CustomerCrowd 参数；缺贴图则保留预制体自带材质）
            Material ownedMaterial=null;
            if(!string.IsNullOrEmpty(def.AlbedoResourcePath))
            {
                var texture=Resources.Load<Texture2D>(def.AlbedoResourcePath);
                if(texture!=null)
                {
                    ownedMaterial=new Material(Shader.Find("Standard")){name=def.Id+"_OriginalAlbedo",mainTexture=texture,color=Color.white};
                    ownedMaterial.SetFloat("_Glossiness",.18f);ownedMaterial.SetFloat("_Metallic",0f);
                    foreach(Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                    { var mats=renderer.sharedMaterials;for(int m=0;m<mats.Length;m++)mats[m]=ownedMaterial;renderer.sharedMaterials=mats; }
                }
            }
            foreach(Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
            var animator=visual.GetComponentInChildren<Animator>();if(animator)animator.enabled=false;
            GameArtRendering.Apply(visual,true); // 现行卡通渲染管线（与 CustomerCrowd 一致）
            var animation=visual.GetComponentInChildren<Animation>()??visual.AddComponent<Animation>();
            foreach(var pair in loaded.Clips)animation.AddClip(pair.Value,pair.Key.ToString());
            // 头顶锚点：OriginNode 上方约身高处
            var anchorObj=new GameObject("RiskAnchor");
            anchorObj.transform.SetParent(visual.transform,false);
            anchorObj.transform.position=(authoredOrigin!=null?authoredOrigin.position:root.transform.position)
                +Vector3.up*(def.RestHeightMetres>0f?def.RestHeightMetres+.12f:1.75f);
            var actor=new StallRiskActor(orderId,root,animation,ownedMaterial,CharacterMotion.Idle.ToString(),CharacterMotion.Walk.ToString(),counterPosition,anchorObj.transform);
            animation.Play(actor.walk);
            return actor;
        }
        public void Tick(float dt)
        {
            if(root==null)return;
            if(phase==Phase.Approach)
            {
                var to=counter-root.transform.position;to.y=0f;
                if(to.magnitude<.08f){phase=Phase.Idle;animation?.CrossFade(idle);}
                else Move(to.normalized,WalkSpeed,dt);
            }
            else if(phase==Phase.Flee)
            {
                var away=root.transform.forward+root.transform.right*.4f;away.y=0f;
                if(away.sqrMagnitude>.001f)Move(away.normalized,WalkSpeed*1.6f,dt);
            }
        }
        private void Move(Vector3 dir,float speed,float dt)
        {
            root.transform.position+=dir*(speed*dt);
            root.transform.rotation=Quaternion.Slerp(root.transform.rotation,Quaternion.LookRotation(dir),Mathf.Clamp01(8f*dt));
        }
        public void OnInterceptSucceeded(int collectCents)
        {
            // 停住、面向摊位交出钱款（暖金浮字由 View 结算事件给出）
            phase=Phase.Idle;
            if(root!=null)root.transform.rotation=Quaternion.Euler(0f,root.transform.eulerAngles.y+180f,0f);
            animation?.CrossFade(idle);
        }
        public void OnEscaped()
        {
            // 只切 Flee 相位：3s 退场展示与最终销毁由 StallRiskDirector.ExitSeconds 负责（EVENT7R）。
            phase=Phase.Flee;
            animation?.CrossFade(walk);
        }
        public void Dispose()
        {
            if(ownedMaterial!=null)UnityEngine.Object.Destroy(ownedMaterial);
            if(root!=null)UnityEngine.Object.Destroy(root);
        }
    }
}

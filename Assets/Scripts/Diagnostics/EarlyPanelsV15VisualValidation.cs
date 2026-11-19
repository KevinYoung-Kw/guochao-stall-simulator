using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // Explicit presentation fixture for the pre-run panels (career / traits /
    // ledger and the practice site-selection menu). It drives the real UI
    // callbacks, captures native screenshots at three resolutions and records
    // structural checks (icon presence, preserved career names, dish gating
    // text). It claims no pointer travel or earned-economy evidence.
    public sealed class EarlyPanelsV15VisualValidation : MonoBehaviour
    {
        [Serializable] public sealed class Checkpoint
        {
            public string name,screenshot,focus;
            public int width,height,careerIcons,traitIcons,districtIllustrations,dishCards,dishMarks;
            public bool careerNamesPreserved,selectionNotColorOnly,pointCostsClear,dishGatesVisible;
            public string selectedCareer,selectedCareerNumber;
            public string[] layoutIssues;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.early-panels.visual-fixture.v15.1";
            public string inputSource="Native standalone run; actual UI onClick callbacks; isolated CampaignSaveStore.TestDirectory; screenshots are engine captures, not pointer/keyboard gameplay";
            public bool success;public string failure="",isolatedSaveDirectory;
            public bool portraitRendered;public int portraitBones,paintedCareerIcons;
            public List<Checkpoint> checkpoints=new List<Checkpoint>();
            public string[] engineErrors;
            public string[] excluded={"Two-day economy reachability","Hardware pointer travel and input timing","In-business HUD / settlement logic"};
        }
        private readonly Report report=new Report();
        private readonly List<string> errors=new List<string>();
        private string output,directory,saveDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--early-panels-visual-test")>=0)new GameObject("EarlyPanelsV15VisualValidation").AddComponent<EarlyPanelsV15VisualValidation>();}
        private void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--early-panels-visual-output");
            output=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"early-panels-v15-validation.json");
            if(!output.EndsWith(".json",StringComparison.OrdinalIgnoreCase))output=Path.Combine(output,"early-panels-v15-validation.json");
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            saveDirectory=Path.Combine(directory,"isolated-early-panels-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            CampaignSaveStore.TestDirectory=saveDirectory;report.isolatedSaveDirectory=saveDirectory;
            Application.logMessageReceived+=Log;
        }
        private void Log(string message,string trace,LogType kind)
        {if(kind==LogType.Error||kind==LogType.Exception)errors.Add(message+"\n"+trace);}
        private IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception error){report.failure=error.ToString();break;}
                if(!moved){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(report.failure!="")
            {
                yield return new WaitForEndOfFrame();string failure=Path.Combine(directory,"early-panels-failure.png");ScreenCapture.CaptureScreenshot(failure);
                float until=Time.realtimeSinceStartup+3;while(!File.Exists(failure)&&Time.realtimeSinceStartup<until)yield return null;
            }
            report.engineErrors=errors.ToArray();report.success=report.failure==""&&errors.Count==0;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.success)Debug.Log("EARLY_PANELS_V15_FIXTURE_PASS "+output);else Debug.LogError("EARLY_PANELS_V15_FIXTURE_FAIL "+report.failure);
            yield return null;if(!Application.isEditor)Application.Quit(report.success?0:1);
        }
        private IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&!Application.isEditor,"Run in a graphical standalone Player.");
            foreach(string other in new[]{"--campaign-visual-test","--business-test","--receipt-visual-test","--interaction-test","--smoke-test"})
                Need(Array.IndexOf(Environment.GetCommandLineArgs(),other)<0,"Do not combine early-panels fixture with "+other);
            GameFlow flow=null;
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                flow=GameFlow.Instance;
                if(flow!=null&&CharacterCreationUI.IsOpen)break;
                yield return null;
            }
            Need(flow!=null&&CharacterCreationUI.IsOpen,"Character creation did not open.");
            Transform creation=FindRoot("CreationCanvas");
            Need(creation!=null,"CreationCanvas is missing.");

            yield return Resolution(1920,1080);
            // Structural checks on the career list: every origin keeps its real
            // name, has its own pictogram, and selection changes a text mark.
            var issues=new List<string>();
            int careerIcons=0;bool namesOk=true,notColorOnly=true;
            for(int i=0;i<CharacterCatalog.Data.careers.Length;i++)
            {
                Transform card=FindChild(creation,"Career_"+CharacterCatalog.Data.careers[i].id);
                Need(card!=null,"Career card missing: "+CharacterCatalog.Data.careers[i].id);
                if(FindChild(card,"CareerIcon")!=null)careerIcons++;
                Text name=FindText(card,"CareerName"),number=FindText(card,"Number");
                if(name==null||name.text!=CharacterCatalog.Data.careers[i].name){namesOk=false;issues.Add("Career name altered: "+CharacterCatalog.Data.careers[i].id);}
                if(number==null){notColorOnly=false;issues.Add("Career number text missing: "+CharacterCatalog.Data.careers[i].id);}
            }
            Need(careerIcons==CharacterCatalog.Data.careers.Length,"Each career must carry its own pictogram.");
            Need(namesOk,"Career names must survive selection.");
            // Count only pictograms actually painting through the painted
            // Image atlas: an enabled Image with a real sprite on the
            // CareerIcon node. A SelectionSymbol vector fallback is presence,
            // not a painted icon, and must not pass. The eight painted career
            // atlas sprites are expected to be wired in, so painted==8.
            int painted=0;
            foreach(CareerDefinition career in CharacterCatalog.Data.careers)
            {
                Transform iconNode=FindChild(FindChild(creation,"Career_"+career.id),"CareerIcon");if(iconNode==null)continue;
                Image graphic=iconNode.GetComponent<Image>();
                if(graphic!=null&&graphic.enabled&&graphic.sprite!=null)painted++;
            }
            report.paintedCareerIcons=painted;
            Need(painted==8,"Career pictograms not all rendering: "+painted+"/8 painted atlas sprites.");
            // The left column is the real rigged Player on a portrait stage:
            // a bound SkinnedMeshRenderer (Player ships 58 bones; below 52 is
            // not the real model) feeding a non-null RenderTexture view. Model
            // survival is not proof the picture looks right — the screenshots
            // go to a human reviewer.
            Transform portraitWorld=FindRoot("CreationPortraitWorld");
            Need(portraitWorld!=null,"Creation portrait stage (CreationPortraitWorld) is missing.");
            SkinnedMeshRenderer skinned=portraitWorld.GetComponentInChildren<SkinnedMeshRenderer>(false);
            Need(skinned!=null,"Portrait stage has no real SkinnedMeshRenderer.");
            report.portraitBones=skinned.bones.Length;
            Need(skinned.bones.Length>=52,"Portrait model is not the fully rigged Player: "+skinned.bones.Length+" bones.");
            // The portrait must be the Portrait node's RawImage inside
            // CreationCanvas showing a live RenderTexture — the room Backdrop
            // also carries a textured RawImage and is not portrait evidence.
            Transform portraitNode=FindChild(creation,"Portrait");
            Need(portraitNode!=null,"Portrait node is missing inside CreationCanvas.");
            RawImage portraitView=portraitNode.GetComponent<RawImage>();
            Need(portraitView!=null&&portraitView.isActiveAndEnabled,"Portrait node carries no enabled RawImage.");
            RenderTexture portraitTexture=portraitView.texture as RenderTexture;
            report.portraitRendered=portraitTexture!=null&&portraitTexture.IsCreated();
            Need(report.portraitRendered,"Portrait view shows no live RenderTexture.");
            Camera portraitCamera=portraitWorld.GetComponentInChildren<Camera>(false);
            Need(portraitCamera!=null,"Portrait stage has no camera.");
            Need(ReferenceEquals(portraitCamera.targetTexture,portraitTexture),"Portrait camera does not render into the Portrait RenderTexture.");
            Need((portraitCamera.cullingMask&(1<<31))!=0,"Portrait camera does not render layer 31.");
            Text firstNumber=FindText(FindChild(creation,"Career_career1"),"Number");
            Need(firstNumber!=null&&firstNumber.text=="选定","Default career is not marked 选定.");
            // The selection tick must be the card's own CareerTick node, live
            // and enabled — not just any icon found somewhere on the card.
            Transform firstTick=FindChild(FindChild(creation,"Career_career1"),"CareerTick");
            Need(RealTick(firstTick),"Default career shows no active CareerTick beyond colour.");
            yield return Capture("careers-default","CreationCanvas",issues,careerIcons,0,0,0,namesOk,notColorOnly,false,false,"career1",firstNumber!=null?firstNumber.text:"");
            // The opening career page also has to survive a narrower laptop
            // window and an ultrawide one: if the new layout crops an edge,
            // these captures fail and the failure stays — the fixture does not
            // patch the UI.
            foreach(Vector2Int size in new[]{new Vector2Int(1280,720),new Vector2Int(2560,1080)})
            {
                yield return Resolution(size.x,size.y);
                yield return Capture("careers-default-"+size.x+"x"+size.y,"CreationCanvas",new List<string>(),careerIcons,0,0,0,namesOk,notColorOnly,false,false,"career1",firstNumber!=null?firstNumber.text:"");
            }
            yield return Resolution(1920,1080);

            // Select 程序员 via its real callback: the pictogram must not eat
            // the name text, and the numeric mark must move to 选定.
            Button programmer=FindButton("Career_career3");Need(programmer!=null&&programmer.IsInteractable(),"Career_career3 cannot be chosen.");programmer.onClick.Invoke();
            yield return null;
            // A real callback may rebuild the creation board; the stale
            // Transform reference would be destroyed, so re-resolve by name.
            creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after selecting career3.");
            Text programmerName=FindText(FindChild(creation,"Career_career3"),"CareerName");
            Text programmerNumber=FindText(FindChild(creation,"Career_career3"),"Number");
            Need(programmerName!=null&&programmerName.text=="被裁大厂程序员","Selecting a career corrupted its name: "+(programmerName!=null?programmerName.text:"<null>"));
            Need(programmerNumber!=null&&programmerNumber.text=="选定","Selected career number mark did not become 选定.");
            // The tick must visibly move: the old card's CareerTick goes dark,
            // the new card's own CareerTick is live. Colour-only would not pass.
            Need(!RealTick(FindChild(FindChild(creation,"Career_career1"),"CareerTick")),"career1 CareerTick stayed active after switching away.");
            Need(RealTick(FindChild(FindChild(creation,"Career_career3"),"CareerTick")),"career3 CareerTick is not active after selection.");
            yield return Capture("careers-selected-programmer","CreationCanvas",new List<string>(),careerIcons,0,0,0,true,true,false,false,"career3",programmerNumber!=null?programmerNumber.text:"");

            // Trait pages: icons per group, costs separated and labelled.
            Click("Category_negative");yield return null;
            creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after opening negative traits.");
            int negativeIcons=CountSymbols(creation,"TraitIcon","negative");
            Need(negativeIcons>0,"Negative traits carry no group icons.");
            var costIssues=new List<string>();bool costsClear=CheckPointCosts(creation,costIssues);
            yield return Capture("traits-negative","CreationCanvas",costIssues,0,negativeIcons,0,0,true,true,costsClear,false,"career3","选定");
            Click("Category_hobby");yield return null;
            creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after opening hobbies.");
            int hobbyIcons=CountSymbols(creation,"TraitIcon","hobby");
            Need(hobbyIcons==6,"Each of the six hobbies needs its own pictogram.");
            var hobbyIssues=new List<string>();
            yield return Capture("traits-hobby","CreationCanvas",hobbyIssues,0,hobbyIcons,0,0,true,true,CheckPointCosts(creation,hobbyIssues),false,"career3","选定");

            // career8 (负债老摊主) opens with a real ¥3,000 debt, so the three
            // repayment-schedule buttons must be on screen, inside the
            // LedgerCard, and survive real onClick without an exception.
            Click("Career_career8");yield return null;
            creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after selecting career8.");
            Transform ledger=FindChild(creation,"LedgerCard");Need(ledger!=null,"LedgerCard missing on the creation board.");
            foreach(string difficulty in new[]{"宽限","标准","地狱"})
            {
                Button debt=FindButton("Debt_"+difficulty);
                Need(debt!=null&&debt.IsInteractable(),"Debt schedule button unavailable: "+difficulty);
                Need(FindChild(ledger,debt.gameObject.name)!=null,"Debt button sits outside the LedgerCard: "+difficulty);
                Need(InsideScreen(debt.GetComponent<RectTransform>()),"Debt button off screen: "+difficulty);
                debt.onClick.Invoke();yield return null;
                creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after debt schedule "+difficulty+".");
                ledger=FindChild(creation,"LedgerCard");Need(ledger!=null,"LedgerCard missing after debt schedule "+difficulty+".");
            }
            yield return Capture("creation-debt","CreationCanvas",new List<string>(),careerIcons,0,0,0,true,true,false,false,"career8","选定");
            // Back to the programmer so the practice flow matches the older
            // test path exactly.
            Click("Career_career3");yield return null;
            creation=FindRoot("CreationCanvas");Need(creation!=null,"CreationCanvas is missing after returning to career3.");
            Text restoredNumber=FindText(FindChild(creation,"Career_career3"),"Number");
            Need(restoredNumber!=null&&restoredNumber.text=="选定","Returning to career3 did not restore its selection mark.");

            // Enter the declared day-12 practice via the real button.
            Click("Practice");
            BusinessController controller=null;deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                controller=FindAnyObjectByType<BusinessController>();
                if(controller!=null&&controller.Session!=null&&!controller.Session.Started)break;
                controller=null;yield return null;
            }
            Need(controller!=null,"Practice site-selection did not open.");
            BusinessHUD hud=FindAnyObjectByType<BusinessHUD>();Need(hud!=null,"BusinessHUD is missing.");
            Transform page=FindRoot("Choose a real street and menu");Need(page!=null,"Selection page is missing.");
            int illustrations=0;bool gates=true;
            for(int i=0;i<BusinessSession.Districts.Length;i++)
            {
                Transform card=FindChild(page,"District "+i);Need(card!=null,"District card missing: "+i);
                Transform illustrationNode=FindChild(card,"District location illustration");
                Need(illustrationNode!=null,"District illustration missing: "+i);
                Image picture=illustrationNode.GetComponent<Image>();
                if(picture!=null&&picture.sprite!=null)illustrations++;
            }
            Need(illustrations==BusinessSession.Districts.Length,"The six real district illustrations must stay on the cards.");
            int dishCards=0,dishMarks=0;
            for(int i=0;i<BusinessSession.Recipes.Length;i++)
            {
                Transform card=FindChild(page,"Dish "+i);Need(card!=null,"Dish card missing: "+i);dishCards++;
                if(FindChild(card,"DishState")!=null)dishMarks++;
                Text status=StatusText(card);
                if(status==null||!(status.text.Contains("¥")||status.text.Contains("缺")))gates=false;
            }
            Need(dishMarks==BusinessSession.Recipes.Length,"Every dish card needs its state mark.");
            Need(gates,"Dish price / missing-gate captions must stay visible.");
            // Inspect one gated dish through the real callback, then capture.
            // BusinessHUD.NewPage destroys and recreates the selection page
            // (same root name) in response to a real dish click, so after the
            // yield the old Transform is destroyed — re-resolve by name.
            int gated=-1;for(int i=0;i<BusinessSession.Recipes.Length;i++)if(controller.Session.Missing(i)!=""){gated=i;break;}
            if(gated>=0){Button dish=FindButton("Dish "+gated);Need(dish!=null,"Gated dish card has no button.");dish.onClick.Invoke();yield return null;}
            page=FindRoot("Choose a real street and menu");Need(page!=null,"Selection page is missing after inspecting a gated dish.");
            yield return Capture("selection-inspected-dish","Choose a real street and menu",new List<string>(),0,0,illustrations,dishCards,true,true,true,gates,"career3","选定");
            foreach(Vector2Int size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1080)})
            {yield return Resolution(size.x,size.y);yield return Capture("selection-"+size.x+"x"+size.y,"Choose a real street and menu",new List<string>(),0,0,illustrations,dishCards,true,true,true,gates,"career3","选定");}
        }
        private int CountSymbols(Transform root,string childName,string category)
        {
            int count=0;
            foreach(TraitDefinition trait in CharacterCatalog.Data.traits)
            {
                if(trait.category!=category)continue;
                Transform row=FindChild(root,"Trait_"+trait.id);
                if(row!=null&&FindChild(row,childName)!=null)count++;
            }
            return count;
        }
        private bool CheckPointCosts(Transform root,List<string> issues)
        {
            bool clear=true;
            foreach(TraitDefinition trait in CharacterCatalog.Data.traits)
            {
                Transform row=FindChild(root,"Trait_"+trait.id);if(row==null)continue;
                Text cost=FindText(row,"PointCost");
                if(cost==null||!(cost.text=="免费"||cost.text.EndsWith(" 点",StringComparison.Ordinal))){clear=false;issues.Add("Point cost unclear: "+trait.id);}
            }
            return clear;
        }
        // Capture resolves the page by its semantic root name, not a cached
        // Transform: SettleTextForCapture yields while dynamic fonts rebuild,
        // and the page can legitimately be rebuilt (same root name) during
        // those frames, leaving any cached Transform destroyed.
        private IEnumerator Capture(string name,string rootName,List<string> issues,int careerIcons,int traitIcons,int illustrations,int dishCards,bool namesPreserved,bool notColorOnly,bool pointCosts,bool dishGates,string selectedCareer,string selectedNumber)
        {
            yield return SettleTextForCapture();yield return new WaitForEndOfFrame();
            Transform root=FindRoot(rootName);
            Need(root!=null,"Capture root was rebuilt away and is now missing: "+rootName+" ("+name+")");
            GameObject focus=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            Need(focus!=null&&focus.activeInHierarchy,"No active focus at "+name);
            foreach(Text label in root.GetComponentsInChildren<Text>(false))
            {
                // Text fully outside its ScrollRect viewport stays ignored
                // (IsVisible) — it is reachable by scrolling, not a defect.
                if(!IsVisible(label.rectTransform))continue;
                // Text visible in the viewport keeps the readability check:
                // legal scrolling content can still genuinely overflow its box,
                // so in-viewport labels are never blanket-skipped.
                if(!label.resizeTextForBestFit&&label.preferredHeight>label.rectTransform.rect.height+2)issues.Add("Text overflows: "+label.name+" = "+label.text);
                if(label.GetComponentInParent<ScrollRect>()==null&&!InsideScreen(label.rectTransform))issues.Add("Text outside screen: "+label.name+" = "+label.text);
            }
            string file=Path.Combine(directory,name+".png");if(File.Exists(file))file=Path.Combine(directory,name+"-"+DateTime.UtcNow.ToString("HHmmss-fff")+".png");
            ScreenCapture.CaptureScreenshot(file);float until=Time.realtimeSinceStartup+5;while(!File.Exists(file)&&Time.realtimeSinceStartup<until)yield return null;
            Need(File.Exists(file),"Screenshot missing: "+file);
            report.checkpoints.Add(new Checkpoint{name=name,screenshot=file,width=Screen.width,height=Screen.height,focus=focus!=null?focus.name:"",careerIcons=careerIcons,traitIcons=traitIcons,districtIllustrations=illustrations,dishCards=dishCards,dishMarks=dishCards,careerNamesPreserved=namesPreserved,selectionNotColorOnly=notColorOnly,pointCostsClear=pointCosts,dishGatesVisible=dishGates,selectedCareer=selectedCareer,selectedCareerNumber=selectedNumber,layoutIssues=issues.ToArray()});
            Need(issues.Count==0,"Layout failed at "+name+": "+string.Join(" | ",issues));
        }
        private IEnumerator Resolution(int width,int height)
        {
            Screen.SetResolution(width,height,false);float until=Time.realtimeSinceStartup+5;
            while((Screen.width!=width||Screen.height!=height)&&Time.realtimeSinceStartup<until)yield return null;
            Need(Screen.width==width&&Screen.height==height,"Requested resolution was not applied: "+width+"x"+height);yield return null;yield return null;
        }
        private static IEnumerator SettleTextForCapture()
        {
            int rebuiltFrame=Time.frameCount;float rebuiltAt=Time.realtimeSinceStartup;
            Action<Font> rebuilt=font=>{rebuiltFrame=Time.frameCount;rebuiltAt=Time.realtimeSinceStartup;};
            Font.textureRebuilt+=rebuilt;
            try
            {
                foreach(Text label in FindObjectsByType<Text>(FindObjectsSortMode.None))if(label.isActiveAndEnabled)label.SetAllDirty();
                Canvas.ForceUpdateCanvases();float deadline=Time.realtimeSinceStartup+3;
                while(Time.realtimeSinceStartup<deadline&&(Time.frameCount-rebuiltFrame<3||Time.realtimeSinceStartup-rebuiltAt<.30f))yield return null;
                Need(Time.frameCount-rebuiltFrame>=3&&Time.realtimeSinceStartup-rebuiltAt>=.30f,"Dynamic font atlas did not settle before capture.");
                Canvas.ForceUpdateCanvases();yield return null;
            }
            finally{Font.textureRebuilt-=rebuilt;}
        }
        private static bool IsVisible(RectTransform rect)
        {
            ScrollRect scroll=rect.GetComponentInParent<ScrollRect>();if(scroll==null)return true;
            return ScreenRect(rect).Overlaps(ScreenRect(scroll.viewport));
        }
        private static bool InsideScreen(RectTransform rect)
        {Rect r=ScreenRect(rect);return r.xMin>=-1&&r.yMin>=-1&&r.xMax<=Screen.width+1&&r.yMax<=Screen.height+1;}
        private static Rect ScreenRect(RectTransform rect)
        {var corners=new Vector3[4];rect.GetWorldCorners(corners);return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);}
        private static Transform FindRoot(string name)
        {
            foreach(Transform node in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(node.name==name&&node.gameObject.activeInHierarchy)return node;
            return null;
        }
        private static Transform FindChild(Transform root,string name)
        {if(root==null)return null;foreach(Transform node in root.GetComponentsInChildren<Transform>(false))if(node.name==name)return node;return null;}
        private static Text FindText(Transform root,string name)
        {if(root==null)return null;foreach(Text label in root.GetComponentsInChildren<Text>(false))if(label.name==name)return label;return null;}
        private static Text StatusText(Transform card)
        {
            Text found=null;
            foreach(Text label in card.GetComponentsInChildren<Text>(false))
                if(label.text.Contains("¥")||label.text.Contains("缺")||label.text.Contains("已选")||label.text.Contains("可制作"))found=label;
            return found;
        }
        private static Button FindButton(string name)
        {foreach(Selectable selectable in Selectable.allSelectablesArray)if(selectable is Button button&&button.gameObject.activeInHierarchy&&button.name==name)return button;return null;}
        private static void Click(string name)
        {Button button=FindButton(name);Need(button!=null&&button.IsInteractable(),"No enabled button: "+name);button.onClick.Invoke();}
        // A genuine selection tick: the card's own CareerTick node, active in
        // the hierarchy, with whatever graphic it carries (vector
        // SelectionSymbol or painted Image) actually enabled. Never inferred
        // from an arbitrary icon elsewhere on the card.
        private static bool RealTick(Transform node)
        {
            if(node==null||!node.gameObject.activeInHierarchy)return false;
            Behaviour graphic=node.GetComponent<Behaviour>();
            return graphic==null||graphic.enabled;
        }
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private void OnDestroy(){Application.logMessageReceived-=Log;CampaignSaveStore.TestDirectory=null;}
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Data contracts and geometry fixtures only. Geometry may position a fixture
    // at candidate stand points; it is explicitly not a traversal/input result.
    public static class V10Validation
    {
        private static readonly StringBuilder report=new StringBuilder();
        private static int failures;
        [MenuItem("Guochao/Validate v10 contracts and reach geometry")]
        public static void Run()
        {
            failures=0;report.Clear();report.AppendLine("V10_VALIDATION — model contracts + reach fixtures, not formal input traversal");
            Check("catalogue and trait rules",CheckProfiles);
            Check("all eight careers afford a real stocked opening",CheckCareerPurchases);
            Check("member price and single payment",CheckMembership);
            Check("timeout only charges delivered goods",CheckTimeoutBudget);
            Check("free carrying / basket conservation",CheckHolding);
            Check("business missing parts / energy type",CheckBusinessRequirements);
            Check("business batches / packages / revenue once",CheckBusinessConservation);
            Check("Home and Market reachable contact poses",CheckReachGeometry);
            report.AppendLine("NOT_COVERED: hardware input feel, rendered animation contact, controller pause timing, full world traversal. Use --interaction-test and graphical business pause validation.");
            report.AppendLine(failures==0?"V10_VALIDATION_PASS":"V10_VALIDATION_FAIL count="+failures);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v10/review/validation.txt"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--v10-output");if(at>=0&&at+1<args.Length)output=Path.GetFullPath(args[at+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString());
            if(failures==0)Debug.Log(report.ToString());else Debug.LogError(report.ToString());
            if(Application.isBatchMode)EditorApplication.Exit(failures==0?0:1);
        }
        private static void Check(string label,Action action)
        {try{action();report.AppendLine("PASS "+label);}catch(Exception exception){failures++;report.AppendLine("FAIL "+label+"\n"+exception);}}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

        private static RunProfile LegalProfile(CareerDefinition career)
        {
            var p=new RunProfile{careerId=career.id};var chosen=new List<string>();
            foreach(var trait in CharacterCatalog.Data.traits)
            {
                if(p.Balance>=0)break;if(trait.points<=0)continue;
                chosen.Add(trait.id);p.selectedTraits=chosen.ToArray();
                if(!p.Validate(out string reason)&&!reason.StartsWith("还缺 ",StringComparison.Ordinal))
                {chosen.RemoveAt(chosen.Count-1);p.selectedTraits=chosen.ToArray();}
            }
            Need(p.Validate(out string error),career.name+" has no legal tested trait allocation: "+error);return p;
        }
        private static void CheckProfiles()
        {
            var data=CharacterCatalog.Data;Need(data.careers.Length==8,"Expected eight original careers");Need(data.traits.Length==47,"Expected 20 positive + 21 negative + 6 hobbies");
            int[] cash={150,400,2500,350,900,600,800,500};int[] points={8,-4,-4,-2,0,0,-2,10};
            var ids=new HashSet<string>();int opposites=0,free=0;
            for(int i=0;i<data.careers.Length;i++)
            {
                CareerDefinition career=data.careers[i];Need(ids.Add(career.id),"Duplicate career ID");
                Need(career.cash==cash[i]&&career.points==points[i],career.id+" differs from source starting cash/points");
                RunProfile p=LegalProfile(career);var run=new RunState(4,p);
                Need(run.CashCents==cash[i]*100 && Mathf.Abs(run.Remaining-p.HomeSeconds)<.001f,"Career startup values not applied");
                foreach(var trait in data.traits)if(career.innate.Contains("【"+trait.id+"】"))
                {
                    var duplicate=LegalProfile(career);var selection=new List<string>(duplicate.selectedTraits){trait.id};duplicate.selectedTraits=selection.ToArray();
                    Need(!duplicate.Validate(out _),"Career gifted trait must not be selectable/charged again: "+trait.id);free++;
                }
            }
            foreach(var trait in data.traits)
            {
                if(string.IsNullOrEmpty(trait.opposite))continue;
                foreach(string raw in trait.opposite.Replace("、",",").Split(','))
                {
                    string opposite=raw.Trim();if(Array.Find(data.traits,t=>t.id==opposite)==null)continue;
                    var p=new RunProfile{careerId="career8",selectedTraits=new[]{trait.id,opposite}};
                    Need(!p.Validate(out string reason)&&reason.Contains("互斥"),"Opposite traits accepted: "+trait.id+" / "+opposite);opposites++;
                }
            }
            var deficit=new RunProfile{careerId="career2"};Need(deficit.Balance<0&&!deficit.Validate(out _),"Negative balance must block starting");
            var duplicateTrait=new RunProfile{selectedTraits=new[]{data.traits[0].id,data.traits[0].id}};Need(!duplicateTrait.Validate(out _),"Duplicate traits accepted");
            Need(opposites>0&&free>0,"Conflict/free-trait checks did not cover any entries");
            report.AppendLine("  tested career cash/points, "+opposites+" opposite pairs, "+free+" career-gifted traits");
        }

        private static void MoveHomeInventory(RunState run)
        {
            int limit=run.HomeEquipment.Count+1;
            while(run.HasUnloadedEquipment&&limit-->0)
            {Need(run.Act("StovePickup",true),"Could not pick actual starter equipment");string id=run.HeldEquipmentId;Need(run.Act("HomeCart",true)&&run.LoadedEquipment.Contains(id),"Starter equipment failed to load: "+id);}
            Need(!run.HasUnloadedEquipment,"Starter load loop failed");Need(run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"Home departure failed");
        }
        private static void CheckCareerPurchases()
        {
            foreach(var career in CharacterCatalog.Data.careers)
            {
                RunState run=new RunState(4,LegalProfile(career));MoveHomeInventory(run);
                foreach(string id in career.gearIds)
                    Need(run.LoadedEquipment.Contains(id)||run.Supplies.ContainsKey(id),"Actual starter gear omitted: "+career.id+" / "+id);
                // U01 is explicitly supplied with the initial chassis (Content Library U01),
                // so this public baseline is not an invisible grant invented by the test.
                Need(run.LoadedEquipment.Contains("U01"),"Initial chassis wooden spatula missing");
                var recommendation=OpeningProcurementPlan.For(run);
                var owned=new HashSet<string>(run.LoadedEquipment);owned.UnionWith(run.Cargo);owned.UnionWith(run.Supplies.Keys);
                var buy=new List<string>();foreach(string id in recommendation.supplies)if(!owned.Contains(id))buy.Add(id);
                foreach(string id in recommendation.shelves)if(!owned.Contains(id))buy.Add(id);
                int recipe=recommendation.menu=="料理盖饭"?1:recommendation.menu=="冰镇酸梅汤"?5:recommendation.menu=="鱼蛋热汤"?3:0;
                int initialCash=run.CashCents;
                foreach(string id in buy)Need(SupplyCatalog.Find(id)!=null?run.AddSupply(id):run.Act(GameCatalog.Find(id).anchor,true,true),career.id+" could not basket "+id);
                int total=run.CartTotalCents;Need(total<=initialCash,career.id+" cannot afford the tested complete starter recipe");
                Need(run.Act("Cashier",true),"Could not start real checkout");run.Tick(run.CheckoutDuration);
                Need(run.Paid&&run.CashCents==initialCash-total&&run.SpentCents==total,"Payment conservation failed");
                Need(run.Act("MarketCart",true)&&run.Cart.Count==0&&run.Cargo.Count==buy.Count,"Purchase loading failed");
                Need(run.Act("MarketExit",true)&&run.Phase==GamePhase.SiteSelection,"Profiled run did not reach site selection");
                GameObject fixture=new GameObject("V10ProcurementMappingFixture");
                try
                {
                    GameFlow flow=fixture.AddComponent<GameFlow>();typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);
                    BusinessSetup setup=flow.BuildBusinessSetup();
                    Need(setup.cashCents==run.CashCents,"Business entry granted or lost cash");
                    var session=new BusinessSession(setup);Need(session.Missing(recipe)=="",career.name+" actual business mapping missing: "+session.Missing(recipe));
                    Need(session.Open(),career.name+" cannot open a real affordable menu");
                }
                finally{UnityEngine.Object.DestroyImmediate(fixture);}
                report.AppendLine("  "+career.name+" starter → paid ¥"+(total/100f).ToString("0.00")+" → loaded → selectable recipe; cash ¥"+(run.CashCents/100f).ToString("0.00"));
            }
        }
        private static void CheckMembership()
        {
            var p=new RunProfile{careerId="career1",selectedTraits=new[]{"商超老会员"}};Need(p.Validate(out _),"Membership test profile invalid");
            var run=new RunState(4,p);MoveHomeInventory(run);
            Need(run.Act(GameCatalog.Items[0].anchor,true),"Member pickup failed");
            int price=Mathf.RoundToInt(GameCatalog.Items[0].priceCents*.85f),before=run.CashCents;
            Need(run.CartTotalCents==price,"Membership price must use actual 85% multiplier once");
            Need(run.Act("Cashier",true),"Member checkout failed");run.Tick(run.CheckoutDuration);
            Need(run.CashCents==before-price&&!run.Act("Cashier",true)&&run.CashCents==before-price,"Membership/repeated checkout double charged");
        }
        private static void CheckTimeoutBudget()
        {
            var run=new RunState(4,new RunProfile());MoveHomeInventory(run);
            Need(run.AddSupply("V01")&&run.AddSupply("rice_small")&&run.AddSupply("P02"),"Timeout fixture basket failed");
            run.SetBusinessCash(1000);run.Tick(120);
            Need(run.CashCents>=0&&run.CashCents+run.SpentCents==1000,"Timeout money conservation");
            int delivered=0;foreach(string id in run.Cargo)delivered+=run.PriceCents(id);
            Need(run.SpentCents==delivered,"Charged an undelivered timeout item");
            Need(!run.Cargo.Contains("V01")&&!run.Cargo.Contains("P02"),"Unaffordable goods appeared in cargo");
            int paid=run.SpentCents;run.Tick(120);Need(run.SpentCents==paid,"Repeated timeout charged again");
        }
        private static void CheckHolding()
        {
            var run=new RunState();MoveHomeInventory(run);string id=GameCatalog.Items[0].id,anchor=GameCatalog.Items[0].anchor;
            int cash=run.CashCents;Need(!run.Act(anchor,false,false),"Remote shelf pickup accepted");
            Need(run.Act(anchor,true,false)&&run.HeldGrocery==id&&run.Cart.Count==0,"Free pickup skipped held inventory");
            Need(!run.Act("ShoppingCart",false,false)&&run.HeldGrocery==id,"Remote basket deposit accepted/lost held box");
            Need(!run.Act("Cashier",true,false)&&run.HeldGrocery==id&&run.CashCents==cash,"Held unpaid box could pay remotely");
            run.Paused=true;float time=run.Remaining;run.Tick(5);Need(!run.Act("ShoppingCart",true,false)&&run.Remaining==time&&run.HeldGrocery==id,"Pause mutated held box/time");run.Paused=false;
            Need(run.Act("ShoppingCart",true,false)&&run.HeldGrocery==""&&run.Cart.Count==1,"Deposit did not transfer exactly once");
            Need(!run.Act("ShoppingCart",true,false)&&run.Cart.Count==1,"Repeated deposit duplicated a box");
            Need(!run.Act(anchor,true,false)&&run.Cart.Count==1,"Free shopper returned a distant basket item");
            Need(run.Act(anchor,true,true)&&run.Cart.Count==0,"Pushed basket could not return matching item");
            Need(run.Act(anchor,true,false)&&run.Act(anchor,true,false)&&run.HeldGrocery==""&&run.Cart.Count==0,"Held box return did not conserve stock");
            Need(run.CashCents==cash&&run.SpentCents==0,"Unpaid handling changed cash");
        }
        private static void CheckBusinessRequirements()
        {
            var empty=new BusinessSession(new BusinessSetup());Need(empty.Missing(0)!=""&&!empty.Open(),"Empty scene granted menu equipment/stock");
            var wrongEnergy=BusinessSetup.MidgamePractice();wrongEnergy.equipment.Remove("B04");wrongEnergy.equipment.Add("B01");wrongEnergy.coalPoints=0;wrongEnergy.gasPoints=45;
            Need(new BusinessSession(wrongEnergy).Missing(0).Contains("蜂窝煤"),"Gas incorrectly powers coal stove");
            var badPair=BusinessSetup.MidgamePractice();badPair.equipment.Remove("B04");badPair.equipment.Remove("V02");badPair.equipment.Add("B02");badPair.equipment.Add("V03");
            Need(new BusinessSession(badPair).Missing(0)!="","B02 accepted oversized V03");
            var noPackage=BusinessSetup.MidgamePractice();noPackage.inventory.Remove("P05");Need(new BusinessSession(noPackage).Missing(0)!="","Missing rice container accepted");
        }
        private static void CheckBusinessConservation()
        {
            var session=new BusinessSession(BusinessSetup.MidgamePractice());session.SelectedRecipes.Clear();session.SelectedRecipes.Add(0);
            Need(session.Open(),"Practice recipe could not open");int cash=session.CashCents,rice=session.Count("rice"),egg=session.Count("egg"),gas=session.Gas,pack=session.Count("P05");
            session.Tick(.81f);Need(session.Orders.Count==1,"Expected initial deterministic order");int order=session.Orders[0].id;session.MarkCustomerArrived(order); // Pure model fixture explicitly reports arrival; not an NPC movement test.
            Need(session.BeginOrder(order,0),"Could not begin valid first order");int batch=session.Lanes[0].batchRemaining;
            Need(batch>0&&session.Count("rice")==rice-batch&&session.Count("egg")==egg-batch&&session.Gas==gas-1&&session.CashCents==cash,"Raw stock/energy deducted incorrectly");
            Need(!session.BeginOrder(order,0)&&session.Count("rice")==rice-batch&&session.Gas==gas-1,"Repeated input duplicated ingredient deduction");
            Need(!session.Serve(0,"P05")&&session.Count("P05")==pack&&session.CashCents==cash,"Premature serving charged/consumed container");
            session.Tick(.65f);session.Tick(session.Lanes[0].remaining);
            Need(session.Lanes[0].step==KitchenStep.Utensil&&session.Action(0)&&session.Action(0),"Valid heat/tool/season sequence failed");
            Need(!session.Serve(0,"P04")&&session.CashCents==cash&&session.Count("P05")==pack,"Wrong package changed money/stock");
            Need(session.Serve(0,"P05")&&session.Count("P05")==pack-1&&session.CashCents==cash+BusinessSession.Recipes[0].priceCents,"Valid serving money/container mismatch");
            Need(!session.Serve(0,"P05")&&session.Count("P05")==pack-1,"Repeated serving duplicated payment/container use");
            if(batch>1)
            {
                session.Tick(12);StallOrder next=session.Orders.Find(x=>!x.served&&!x.departed&&x.lane<0);
                if(next!=null)session.MarkCustomerArrived(next.id);
                Need(next!=null&&session.BeginOrder(next.id,0)&&session.Gas==gas-1&&session.Count("rice")==rice-batch,"Serving prepared batch remainder consumed raw stock twice");
            }
            session.Close();int finalCash=session.CashCents,revision=session.Revision,lost=session.Lost;
            session.Close();session.Tick(200);Need(session.CashCents==finalCash&&session.Revision==revision&&session.Lost==lost,"Settlement/closure repeated");
        }
        private static void CheckReachGeometry()
        {
            GameObject root=new GameObject("V10ReachFixture");
            try
            {
                GameFlow flow=root.AddComponent<GameFlow>();flow.Initialize();
                TestReach(flow,"StovePickup");TestReach(flow,"HomeCart");
                flow.Run.Act("HomeExit",true);
                typeof(GameFlow).GetMethod("CreateWorld",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(flow,null);
                flow.World.ShoppingMotor.Detach();
                foreach(ItemDefinition item in GameCatalog.Items)TestReach(flow,item.anchor);
                TestReach(flow,"MarketCart");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void TestReach(GameFlow flow,string target)
        {
            Need(flow.World.HasAnchor(target),"Missing interaction anchor "+target);
            Vector3 anchor=flow.World.Anchor(target),original=flow.World.Player.transform.position;bool found=false;string blocker="none";
            var controller=flow.World.Player.Controller;bool enabled=controller.enabled;controller.enabled=false;
            try
            {
                Vector3 exact=anchor;
                if(Physics.Raycast(anchor+Vector3.up*.35f,Vector3.down,out RaycastHit exactGround,1,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))exact.y=exactGround.point.y+.03f;
                flow.World.Player.transform.position=exact;Physics.SyncTransforms();
                if(flow.World.TryInteractionContact(target,out Vector3 exactContact))
                {
                    Vector3 horizontal=exactContact-exact;horizontal.y=0;
                    Vector3 side=anchor-exactContact;side.y=0;
                    Vector3 ray=exactContact-(exact+Vector3.up*1.55f);
                    string hitName="none";
                    if(Physics.Raycast(exact+Vector3.up*1.55f,ray.normalized,out RaycastHit exactHit,Mathf.Max(0,ray.magnitude-.28f),WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))hitName=exactHit.collider.name+" distance="+exactHit.distance+" hit="+exactHit.point;
                    report.AppendLine("  AT_ANCHOR "+target+" canInteract="+flow.IsActionInRange(target)+" horizontalReach="+horizontal.magnitude+" contact="+exactContact+" standingSide="+side+" frontDot="+Vector3.Dot(exact-exactContact,side.normalized)+" LOS="+hitName);
                }
                for(int x=-6;x<=6&&!found;x++)for(int z=-6;z<=6&&!found;z++)
                {
                    Vector3 delta=new Vector3(x*.15f,0,z*.15f);if(delta.sqrMagnitude>.95f*.95f)continue;
                    Vector3 candidate=anchor+delta;
                    if(!Physics.Raycast(candidate+Vector3.up*.35f,Vector3.down,out RaycastHit ground,1,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))continue;
                    candidate.y=ground.point.y+.03f;
                    if(Physics.CheckCapsule(candidate+Vector3.up*.29f,candidate+Vector3.up*1.40f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))continue;
                    // Fixture placement solely evaluates the production reach gate.
                    // No movement success is inferred from these positions.
                    flow.World.Player.transform.position=candidate;Physics.SyncTransforms();
                    if(flow.IsActionInRange(target)){found=true;report.AppendLine("  REACH_FIXTURE "+target+" "+candidate);break;}
                    if(flow.World.TryInteractionContact(target,out Vector3 point))
                    {
                        Vector3 ray=point-(candidate+Vector3.up*1.55f);
                        if(Physics.Raycast(candidate+Vector3.up*1.55f,ray.normalized,out RaycastHit hit,Mathf.Max(0,ray.magnitude-.28f),WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))blocker=hit.collider.name+" at "+hit.point;
                    }
                }
            }
            finally{flow.World.Player.transform.position=original;controller.enabled=enabled;Physics.SyncTransforms();}
            Need(found,target+" has no tested collision-free front contact pose; last LOS blocker="+blocker);
        }
    }
}

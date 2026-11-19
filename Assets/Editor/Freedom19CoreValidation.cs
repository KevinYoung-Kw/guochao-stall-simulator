using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Guochao
{
    // FREEDOM19 core 自检：独立 Editor 夹具，只调用生产公开入口；不写任何正常存档，
    // 不注入钱/货/时间。入口 Run(args)：--freedom19-core-report <绝对路径> 输出报告。
    public static class Freedom19CoreValidation
    {
        static readonly List<string> failures=new List<string>();
        static int checks;

        public static void Run(){if(Validate(Environment.GetCommandLineArgs())!=0)throw new Exception("Freedom19 core validation failed");}
        public static int Validate(string[] args)
        {
            failures.Clear();checks=0;
            string reportPath=ParseReportPath(args);
            try
            {
                NoPlanOpenAndSingleEggDebit();
                GenericCombinationsAndMethods();
                QueriesDoNotMutate();
                BadMethodRejectedWithoutMutation();
                PhysicalEquipmentRejection();
                PackWithoutCustomerAndNextBatch();
                WrongAndDepartedCustomerKeepMeal();
                ChosenCustomerPaysExactlyOnce();
                SaveRoundtripDeepCopyAndLegacyV2();
                RiskPendingQuoteAndRecovery();
                ConservationAuditTruthful();
                LastPackageStillFindsCustomer();
                TwoBurnersAndActualVesselCapacity();
                PerPortionFlavorAndCounterCapacity();
                JsonEncodedFreeKitchenRoundtrip();
                AmbiguousFruitIsRealGenericOffer();
                ReheatNeedsNoExtraBaseSeasoning();
            }
            catch(Exception error){failures.Add("unhandled: "+error);}
            var builder=new StringBuilder();
            builder.Append("{\"validation\":\"freedom19-core\",\"executedInEditor\":true,");
            builder.Append("\"checks\":").Append(checks).Append(",\"failures\":").Append(failures.Count).Append(",\"failureList\":[");
            for(int i=0;i<failures.Count;i++){if(i>0)builder.Append(',');builder.Append('"').Append(Escape(failures[i])).Append('"');}
            builder.Append("]}");
            string json=builder.ToString();
            Debug.Log(json);
            if(!string.IsNullOrEmpty(reportPath))
                try{File.WriteAllText(reportPath,json);}catch(Exception error){Debug.LogError("report write failed: "+error);}
            return failures.Count==0?0:1;
        }

        static string ParseReportPath(string[] args)
        {
            if(args==null)return null;
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--freedom19-core-report")return args[i+1];
            return null;
        }
        static string Escape(string s)=>s==null?"":s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r");

        static void Check(bool condition,string name)
        {
            checks++;
            if(!condition)failures.Add(name);
        }

        static BusinessSetup SmallSetup()=>new BusinessSetup
        {
            cashCents=5000,seed=12017,dayDuration=300,gasPoints=5,stoveLoaded=true,
            customerIntervalSeconds=9999, // 测试自给订单，不依赖随机来客
            equipment=new HashSet<string>{"B04","V02","U02","V15","U13","U12","V16","V05","U04","V09"},
            inventory=new Dictionary<string,int>{{"egg",3},{"rice",2},{"meat",1},{"dough",1},{"fruit",2},{"seasoning",1},{"P05",4},{"P08",2}}
        };

        static BusinessSession NewOpenSession(BusinessSetup setup,out int lane)
        {
            var session=new BusinessSession(setup);
            session.SelectedRecipes.Clear(); // 无计划、无选中菜单
            Check(session.Open(),"open.no-plan");
            lane=0;
            return session;
        }

        // 真实小步走：投料→做法→开火→加热→出锅。
        static bool CookThrough(BusinessSession s,int lane,string method,int ticks=40)
        {
            if(!s.SetFreeMethod(lane,method))return false;
            if(!s.StartFreeCooking(lane))return false;
            for(int i=0;i<100&&s.Lanes[lane].step==KitchenStep.Heating;i++)s.Tick(.1f);
            if(s.Lanes[lane].step==KitchenStep.Utensil&&!s.Action(lane))return false;
            if(s.Lanes[lane].step==KitchenStep.Season&&!s.Action(lane))return false;
            return s.ProductAtLane(lane)!=null&&s.Lanes[lane].step==KitchenStep.Pack;
        }

        static void NoPlanOpenAndSingleEggDebit()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            int eggBefore=session.Count("egg"),riceBefore=session.Count("rice"),gasBefore=session.Gas;
            Check(session.FindFreeLane("egg")==0,"egg.freeLane");
            Check(session.CanAddFreeIngredient(lane,"egg",out _),"egg.canAdd");
            Check(session.AddFreeIngredient(lane,"egg"),"egg.add");
            Check(session.Count("egg")==eggBefore-1,"egg.singleDebit");
            Check(session.Count("rice")==riceBefore,"egg.noRiceDebit");
            Check(CookThrough(session,lane,FreeKitchenRules.MethodStirFry),"egg.cook");
            Check(session.Gas==gasBefore-1,"egg.fuelOnce");
            var product=session.ProductAtLane(lane);
            Check(product!=null&&product.portions==1,"egg.onePortionPerUnit");
            Check(product!=null&&product.matchedRecipeIndex==8,"egg.matchesEggGreens");
        }

        static void GenericCombinationsAndMethods()
        {
            // 组合1：蛋+肉 现炒（比例不符任何菜谱 → 泛称）
            var s1=NewOpenSession(SmallSetup(),out int l1);
            s1.AddFreeIngredient(l1,"egg");s1.AddFreeIngredient(l1,"meat");
            Check(CookThrough(s1,l1,FreeKitchenRules.MethodStirFry),"combo.stirFry.cook");
            var p1=s1.ProductAtLane(l1);
            Check(p1!=null&&p1.matchedRecipeIndex==-1,"combo.stirFry.generic");
            Check(p1!=null&&p1.priceCents>0&&p1.priceCents!=1000,"combo.stirFry.fairPrice");
            // 组合2：饼胚+蛋 煎（第二条物理做法）
            var s2=NewOpenSession(SmallSetup(),out int l2);
            s2.AddFreeIngredient(l2,"dough");s2.AddFreeIngredient(l2,"egg");
            Check(CookThrough(s2,l2,FreeKitchenRules.MethodGriddle),"combo.griddle.cook");
            var p2=s2.ProductAtLane(l2);
            Check(p2!=null&&p2.matchedRecipeIndex==-1,"combo.griddle.generic");
            // 组合3：米面+鲜肉 热汤（第三条物理做法）
            var s3=NewOpenSession(SmallSetup(),out int l3);
            int gas=s3.Gas;
            s3.AddFreeIngredient(l3,"rice");s3.AddFreeIngredient(l3,"meat");
            Check(CookThrough(s3,l3,FreeKitchenRules.MethodBoil),"combo.boil.cook");
            var p3=s3.ProductAtLane(l3);
            Check(p3!=null&&p3.matchedRecipeIndex==-1&&s3.Gas==gas-1,"combo.boil.fuelOnce");
            Check(p3!=null&&p3.portions==1,"combo.boil.onePortion");
        }

        static void QueriesDoNotMutate()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            session.AddFreeIngredient(lane,"egg");
            int egg=session.Count("egg");
            var before=JsonUtility.ToJson(session.Lanes[lane].freeBatch);
            for(int i=0;i<3;i++)
            {
                session.CanAddFreeIngredient(lane,"egg",out _);
                session.CanAddFreeIngredient(lane,"meat",out _);
                session.FreeMethods(lane);
                session.FindFreeLane("egg");
                session.FindServingOrder(lane);
            }
            Check(session.Count("egg")==egg,"query.noInventoryChange");
            Check(JsonUtility.ToJson(session.Lanes[lane].freeBatch)==before,"query.noBatchMutation");
        }

        static void BadMethodRejectedWithoutMutation()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            session.AddFreeIngredient(lane,"meat");
            var before=JsonUtility.ToJson(session.Lanes[lane].freeBatch);
            int meat=session.Count("meat");
            Check(!session.SetFreeMethod(lane,FreeKitchenRules.MethodMuddle),"badMethod.rejected");
            Check(JsonUtility.ToJson(session.Lanes[lane].freeBatch)==before,"badMethod.noMutation");
            Check(session.Count("meat")==meat,"badMethod.noDebit");
        }

        static void PhysicalEquipmentRejection()
        {
            var setup=SmallSetup();
            setup.equipment=new HashSet<string>{"B04"}; // 只有底座，没有锅/工具
            var session=new BusinessSession(setup);
            session.SelectedRecipes.Clear();
            session.Open();
            Check(!session.CanAddFreeIngredient(0,"egg",out string reason)&&reason!="","hardware.noVessel");
            Check(session.FreeMethods(0).Length==0,"hardware.noMethods");
        }

        static void PackWithoutCustomerAndNextBatch()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            session.AddFreeIngredient(lane,"egg");
            Check(CookThrough(session,lane,FreeKitchenRules.MethodStirFry),"pack.cook");
            int cash=session.CashCents,packages=session.Count("P05");
            Check(session.CanPackToCounter(lane,"P05",out _),"pack.canPack");
            Check(session.PackToCounter(lane,"P05"),"pack.pack");
            Check(session.ReadyMeals.Count==1,"pack.oneMeal");
            Check(session.CashCents==cash,"pack.noRevenue");
            Check(session.Count("P05")==packages-1,"pack.onePackage");
            Check(!session.IsFreeLane(lane),"pack.laneFreed");
            // 下一锅可以在餐等待时开做
            Check(session.AddFreeIngredient(lane,"rice"),"pack.nextBatch");
        }

        static StallOrder InjectArrivedOrder(BusinessSession session,int recipeIndex,string productId="",string productName="",int productPriceCents=0)
        {
            var order=new StallOrder{id=9999,recipeIndex=recipeIndex,patience=60,maxPatience=60,spawnedAt=session.Elapsed,productId=productId,productName=productName,productPriceCents=productPriceCents};
            session.Orders.Add(order);
            session.MarkCustomerArrived(9999);
            return order;
        }

        static StallOrder WaitForCustomer(BusinessSession session,CounterMeal meal)
        {
            for(int i=0;i<30;i++)
            {
                foreach(var order in session.Orders)if(!order.departed&&!order.served&&order.productId==meal.product.id)
                {session.MarkCustomerArrived(order.id);return order;}
                session.Tick(.2f);
            }
            throw new Exception("No actual customer arrived for counter supply");
        }

        static CounterMeal CookAndPack(BusinessSession session,int lane)
        {
            session.AddFreeIngredient(lane,"egg");
            CookThrough(session,lane,FreeKitchenRules.MethodStirFry);
            session.PackToCounter(lane,"P05");
            return session.ReadyMeals[session.ReadyMeals.Count-1];
        }

        static void WrongAndDepartedCustomerKeepMeal()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var meal=CookAndPack(session,lane);
            var wrong=InjectArrivedOrder(session,2); // 手抓饼客不要蛋菜小炒
            int cash=session.CashCents;
            Check(!session.CanServeCounterMeal(meal.id,wrong.id,out _),"serve.wrongRejected");
            Check(!session.ServeCounterMeal(meal.id,wrong.id),"serve.wrongNotExecuted");
            Check(session.ReadyMeals.Count==1&&session.CashCents==cash,"serve.mealIntact");
            wrong.departed=true;
            Check(!session.CanServeCounterMeal(meal.id,wrong.id,out _),"serve.departedRejected");
            Check(session.ReadyMeals.Count==1,"serve.mealStillThere");
        }

        static void ChosenCustomerPaysExactlyOnce()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var meal=CookAndPack(session,lane);
            var product=meal.product;
            var order=WaitForCustomer(session,meal);
            order.productId=product.id;order.productName=product.name;order.productPriceCents=product.priceCents;
            int recommended=session.FindServingCounterOrder(meal.id);
            Check(recommended==order.id,"serve.recommendsOldest");
            int cash=session.CashCents;
            int price=session.OrderPriceCents(order.id);
            Check(price==product.priceCents,"serve.quoteStable");
            Check(session.ServeCounterMeal(meal.id,order.id),"serve.succeeds");
            Check(session.CashCents==cash+price,"serve.onePayment");
            Check(session.ReadyMeals.Count==0,"serve.mealRemoved");
            cash=session.CashCents;
            Check(!session.ServeCounterMeal(meal.id,order.id),"serve.duplicateRejected");
            Check(session.CashCents==cash,"serve.noDoublePay");
            Check(session.OrderDishName(order.id)==product.name,"serve.namePersists");
        }

        static void SaveRoundtripDeepCopyAndLegacyV2()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var meal=CookAndPack(session,lane);
            session.AddFreeIngredient(lane,"rice");
            var save=session.CaptureSave();
            var restored=BusinessSession.Restore(save);
            Check(restored.ReadyMeals.Count==1,"save.mealsRoundtrip");
            Check(restored.IsFreeLane(lane),"save.batchRoundtrip");
            // 深拷贝：改原对象不影响恢复体
            session.ReadyMeals[0].product.remaining=99;
            session.Lanes[lane].freeBatch.inputs[0].units=99;
            Check(restored.ReadyMeals[0].product.remaining==1,"save.mealDeepCopy");
            Check(restored.Lanes[lane].freeBatch.inputs[0].units==1,"save.batchDeepCopy");
            // 旧 v2 档：无自由字段、旧计费迁移、旧批次语义保留
            var legacy=new BusinessSession(BusinessSetup.MidgamePractice());
            legacy.Open();
            Check(legacy.BeginBatch(0),"legacy.crackEgg");
            var eggPrepSaved=BusinessSession.Restore(legacy.CaptureSave());
            Check(eggPrepSaved.Lanes[0].eggPrepPortions>0&&eggPrepSaved.Lanes[0].batchRemaining==0,"legacy.eggPrepPreserved");
            Check(legacy.AddRiceBatch(0),"legacy.addRice");
            // Reconstruct a true v2 prepaid-seasoning fixture; v2 charged the full batch on start.
            legacy.Inventory["seasoning"]-=legacy.Lanes[0].batchRemaining;
            var legacySave=legacy.CaptureSave();
            legacySave.seasoningChargeVersion=0; // 旧计费档
            legacySave.freeKitchenVersion=0;
            legacySave.freeProduction=null;
            legacySave.readyMeals=null;
            var legacyRestored=BusinessSession.Restore(legacySave);
            Check(legacyRestored.Lanes[0].batchRemaining>0,"legacy.batchPreserved");
            Check(legacyRestored.Lanes[0].prepaidSeasoning==legacyRestored.Lanes[0].batchRemaining,"legacy.prepaidMigrated");
            Check(legacyRestored.ReadyMeals.Count==0,"legacy.noMeals");
            // 畸形档必须拒绝：剩余份数与成品不符
            var bad=restored.CaptureSave();
            bad.readyMeals[0].product.remaining=5;
            bool rejected=false;try{BusinessSession.Restore(bad);}catch(ArgumentException){rejected=true;}
            Check(rejected,"save.malformedRejected");
        }

        static void RiskPendingQuoteAndRecovery()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var meal=CookAndPack(session,lane);
            var product=meal.product;
            var order=WaitForCustomer(session,meal);
            session.ServeRiskGate=(orderId,recipeIndex,priceCents)=>new StallRiskGateDecision(true,priceCents*2,4.5f);
            int cash=session.CashCents;
            Check(session.ServeCounterMeal(meal.id,order.id),"risk.adopted");
            Check(session.CashCents==cash,"risk.noCashYet");
            Check(order.paidCents==0,"risk.unpaid");
            Check(session.OrderPriceCents(order.id)==product.priceCents,"risk.quoteStable");
            var active=session.RiskState.ActiveEvent;
            Check(active!=null&&active.RecipeId==product.id,"risk.stableIdentity");
            // The original burner may now cook something else while the claim waits.
            Check(session.AddFreeIngredient(lane,"rice"),"risk.nextBatchCanStart");
            var restored=JsonRoundtrip(session);
            Check(restored.RiskState.ActiveEvent!=null,"risk.restoreWithNewPrep");
            Check(session.TryCompleteRiskSale(active.Serial,active.CollectCents),"risk.recovered");
            Check(session.CashCents==cash+active.CollectCents,"risk.recoveryPaid");
            Check(session.OrderDishName(order.id)==product.name,"risk.namePersists");
        }

        static void LastPackageStillFindsCustomer()
        {
            var setup=SmallSetup();setup.inventory["P05"]=1;
            var session=NewOpenSession(setup,out int lane);var meal=CookAndPack(session,lane);
            Check(session.Count("P05")==0,"lastPackage.used");
            var order=WaitForCustomer(session,meal);
            Check(session.ServeCounterMeal(meal.id,order.id),"lastPackage.stillSellsPackedMeal");
        }
        static void TwoBurnersAndActualVesselCapacity()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            Check(session.AddFreeIngredient(0,"rice"),"hardware.firstPan");
            Check(session.SetFreeMethod(0,"stir_fry"),"hardware.firstMethod");
            Check(session.AddFreeIngredient(1,"dough"),"hardware.secondBurner");
            Check(session.SetFreeMethod(1,"griddle"),"hardware.secondMethod");
            Check(session.Lanes[0].freeBatch.baseId=="B04"&&session.Lanes[1].freeBatch.baseId=="B04","hardware.sharedDoubleBurner");
            Check(session.Lanes[0].freeBatch.vesselId!=session.Lanes[1].freeBatch.vesselId,"hardware.noDuplicatePan");
            Check(session.StartFreeCooking(0)&&session.StartFreeCooking(1),"hardware.concurrentHeat");
            var saved=BusinessSession.Restore(session.CaptureSave());
            Check(saved.Lanes[0].batchRemaining==1&&saved.Lanes[1].batchRemaining==1,"hardware.heatingSave");
            var thin=SmallSetup();thin.equipment.Remove("V02");thin.equipment.Add("V01");
            var s=NewOpenSession(thin,out int l);s.AddFreeIngredient(l,"rice");s.SetFreeMethod(l,"stir_fry");
            Check(!s.AddFreeIngredient(l,"rice"),"hardware.thinPanCapacity");
        }

        static void PerPortionFlavorAndCounterCapacity()
        {
            var setup=SmallSetup();setup.inventory["egg"]=8;setup.inventory["P05"]=8;setup.inventory["S01"]=2;
            var session=NewOpenSession(setup,out int lane);
            session.AddFreeIngredient(lane,"egg");session.AddFreeIngredient(lane,"egg");CookThrough(session,lane,"stir_fry");
            Check(session.Action(lane,"S01"),"flavor.chooseOnPackedStep");
            Check(session.PackToCounter(lane,"P05"),"flavor.firstPortion");
            Check(session.Count("S01")==1&&session.ReadyMeals[0].flavorId=="S01","flavor.onePhysicalCharge");
            Check(session.Action(lane,"seasoning"),"flavor.switchNextToPlain");
            Check(session.PackToCounter(lane,"P05")&&session.ReadyMeals[1].flavorId=="","flavor.plainSecondPortion");
            for(int batch=0;batch<2;batch++)
            {session.AddFreeIngredient(lane,"egg");session.AddFreeIngredient(lane,"egg");CookThrough(session,lane,"stir_fry");session.PackToCounter(lane,"P05");session.PackToCounter(lane,"P05");}
            Check(session.ReadyMeals.Count==BusinessSession.CounterCapacity,"counter.sixPhysicalMeals");
            session.AddFreeIngredient(lane,"egg");CookThrough(session,lane,"stir_fry");
            int packageCount=session.Count("P05");
            Check(!session.CanPackToCounter(lane,"P05",out string reason)&&reason!="","counter.fullRejected");
            Check(session.Count("P05")==packageCount&&session.ProductAtLane(lane)!=null,"counter.fullPreservesFoodAndPackage");
            var restored=BusinessSession.Restore(session.CaptureSave());
            Check(restored.ReadyMeals.Count==6&&restored.ReadyMeals[0].product.sourcePortions==2&&restored.ReadyMeals[0].product.portions==1,"counter.multiPortionProvenance");
        }

        static BusinessSession JsonRoundtrip(BusinessSession session)
        {
            string json=JsonUtility.ToJson(session.CaptureSave());
            var decoded=JsonUtility.FromJson<BusinessSaveData>(json);
            return BusinessSession.Restore(decoded);
        }
        static void JsonEncodedFreeKitchenRoundtrip()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var idle=JsonRoundtrip(session);
            Check(!idle.IsFreeLane(0)&&!idle.IsFreeLane(1),"json.idleNullBatch");
            session.AddFreeIngredient(lane,"egg");
            var prep=JsonRoundtrip(session);
            Check(prep.IsFreeLane(lane)&&prep.ProductAtLane(lane)==null&&!prep.IsFreeLane(1),"json.prepNullProduct");
            Check(CookThrough(session,lane,"stir_fry"),"json.cook");
            var cooked=JsonRoundtrip(session);
            Check(cooked.ProductAtLane(lane)!=null,"json.cookedProduct");
            session.PackToCounter(lane,"P05");
            var packed=JsonRoundtrip(session);
            Check(packed.ReadyMeals.Count==1&&!packed.IsFreeLane(lane),"json.packedAndFreed");
            var meal=packed.ReadyMeals[0];var order=WaitForCustomer(packed,meal);
            Check(packed.ServeCounterMeal(meal.id,order.id),"json.realDelivery");
            var sold=JsonRoundtrip(packed);
            Check(sold.RevenueCents==meal.priceCents&&sold.ReadyMeals.Count==0,"json.soldMoneyPersisted");
            Check(sold.OrderDishName(order.id)==meal.product.name,"json.soldIdentityPersisted");
        }
        static void AmbiguousFruitIsRealGenericOffer()
        {
            var setup=SmallSetup();var session=new BusinessSession(setup);
            session.SelectedRecipes.Clear();session.SelectedRecipes.Add(5);session.SelectedRecipes.Add(11);session.Open();
            session.Tick(1f);
            Check(session.Orders.Count==0,"fruit.noUnfulfillableNamedDemand");
            session.AddFreeIngredient(0,"fruit");Check(CookThrough(session,0,"cold_mix"),"fruit.coldMix");
            var product=session.ProductAtLane(0);
            Check(product.matchedRecipeIndex==-1&&product.name!=BusinessSession.Recipes[5].name&&product.name!=BusinessSession.Recipes[11].name,"fruit.genericIdentity");
            session.PackToCounter(0,"P08");var meal=session.ReadyMeals[0];var order=WaitForCustomer(session,meal);
            Check(order.productId==meal.product.id&&order.productName==meal.product.name,"fruit.actualOffer");
            Check(session.ServeCounterMeal(meal.id,order.id),"fruit.genericSale");
            Check(JsonRoundtrip(session).RevenueCents==meal.priceCents,"fruit.quotePersisted");
            Check(FreeKitchenRules.MatchRecipe(new[]{new FoodInput("fruit",1)},"muddle",1)==10,"fruit.uniqueMethodStillNamed");
            var legacy=new BusinessSession(setup);legacy.SelectedRecipes.Clear();legacy.SelectedRecipes.Add(5);legacy.Open();
            Check(legacy.BeginBatch(0),"fruit.legacyNamedPreparationRetained");
            Check(JsonRoundtrip(legacy).Lanes[0].batchRemaining>0,"fruit.legacySaveRetained");
        }
        static void ReheatNeedsNoExtraBaseSeasoning()
        {
            var setup=SmallSetup();setup.equipment=new HashSet<string>{"B08","RiceBucket","U14"};setup.electricityPoints=3;setup.inventory["seasoning"]=0;setup.inventory["prepared"]=1;
            var session=NewOpenSession(setup,out int lane);session.AddFreeIngredient(lane,"rice");session.AddFreeIngredient(lane,"prepared");
            Check(CookThrough(session,lane,"reheat"),"reheat.actualProcessing");
            Check(session.PackToCounter(lane,"P05"),"reheat.packWithoutExtraSalt");
            var meal=session.ReadyMeals[0];Check(!meal.shortSeasoning,"reheat.noFalseShortage");
            var order=WaitForCustomer(session,meal);Check(session.ServeCounterMeal(meal.id,order.id),"reheat.sell");
            Check(session.IntegrityChange==0&&session.UnseasonedPortionsServed==0,"reheat.noFalsePenalty");
        }

        static void ConservationAuditTruthful()
        {
            var session=NewOpenSession(SmallSetup(),out int lane);
            var meal=CookAndPack(session,lane);
            var order=WaitForCustomer(session,meal);
            session.ServeCounterMeal(meal.id,order.id);
            var audit=session.CaptureAudit();
            Check(audit.cashConserved,"audit.cash");
            Check(audit.packagesConserved,"audit.packages");
            Check(audit.nonNegativeInventory&&audit.nonNegativeEnergy,"audit.nonNegative");
            foreach(var recipe in audit.recipeProduction)Check(recipe.portionsConserved,"audit.portions."+recipe.recipe);
        }
    }
}

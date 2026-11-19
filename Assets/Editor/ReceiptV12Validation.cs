using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Real state-model transactions, not input/animation verification.
    public static class ReceiptV12Validation
    {
        [Serializable] private sealed class Report
        {public string schema="guochao.receipt.validation.v12";public bool success;public string error="";public List<CommerceReceiptAudit> checkpoints=new List<CommerceReceiptAudit>();}
        [MenuItem("Guochao/Validate v12 receipt ledgers")]
        public static void Run()
        {
            var report=new Report();
            try
            {
                var run=new RunState(4,new RunProfile());
                while(run.HasUnloadedEquipment){Need(run.Act("StovePickup",true),"Home pickup");Need(run.Act("HomeCart",true),"Home load");}
                Need(run.Act("HomeExit",true),"Market entry");
                Need(run.Act("RiceShelf",true,false)&&run.Act("ShoppingCart",true),"First basket");
                Record(report,CommerceReceiptData.Shopping(run,false));int initialCash=run.CashCents;
                Need(run.Act("Cashier",true),"Begin checkout");run.Tick(1,true);Record(report,CommerceReceiptData.Shopping(run,false));
                Need(run.CashCents==initialCash,"Checkout charged before completion");run.Tick(.1f,false);Need(!run.Paid&&run.CashCents==initialCash,"Interrupted checkout charged");
                Need(run.Act("Cashier",true),"Restart checkout");run.Tick(run.CheckoutDuration,true);Record(report,CommerceReceiptData.Shopping(run,false));
                Need(run.Act("MarketCart",true),"First load");Record(report,CommerceReceiptData.Shopping(run,true));
                Need(run.Act("EggShelf",true,false)&&run.Act("ShoppingCart",true),"Second basket");
                var current=CommerceReceiptData.Shopping(run,false);Record(report,current);Record(report,CommerceReceiptData.Shopping(run,true));
                Need(current.lines.Length==1&&current.lines[0].id=="egg"&&current.totalCents==run.PriceCents("egg"),"Second batch contains previous payment");
                Need(run.Act("Cashier",true),"Second checkout");run.Tick(run.CheckoutDuration,true);Record(report,CommerceReceiptData.Shopping(run,true));
                Need(run.Act("MarketCart",true),"Second load");Record(report,CommerceReceiptData.Shopping(run,true));
                Need(CommerceReceiptData.Shopping(run,true).lines.Length==2,"Loaded receipt lost prior item");
                Need(initialCash-run.CashCents==CommerceReceiptData.Shopping(run,true).totalCents,"Loaded cash mismatch");
                var member=new RunState(4,new RunProfile{selectedTraits=new[]{"商超老会员"}});member.Cart.Add("P04");
                var memberBill=CommerceReceiptData.Shopping(member,false);Record(report,memberBill);
                Need(memberBill.lines[0].unitCents==member.PriceCents("P04")&&memberBill.lines[0].unit=="捆","Member/bundle unit price mismatch");
                var business=new BusinessSession(BusinessSetup.MidgamePractice());business.SelectedRecipes.Clear();business.SelectedRecipes.Add(0);Need(business.Open(),"Open sales fixture");Record(report,CommerceReceiptData.Sales(business));
                business.Tick(.81f);StallOrder first=business.Orders[0];business.MarkCustomerArrived(first.id);Need(business.BeginOrder(first.id,0),"First order");
                business.Tick(.65f);business.Tick(business.Lanes[0].remaining);Need(business.Action(0)&&business.Action(0),"First cooking");Need(business.Serve(0,"P05"),"First serving");Record(report,CommerceReceiptData.Sales(business));
                business.Tick(12);StallOrder second=business.Orders.Find(x=>!x.served&&!x.departed);Need(second!=null,"Second arrival");business.MarkCustomerArrived(second.id);Need(business.BeginOrder(second.id,0)&&business.Serve(0,"P05"),"Second serving from batch");
                business.Close();var sale=CommerceReceiptData.Sales(business);Record(report,sale);Need(sale.lines.Length==1&&sale.lines[0].quantity==2,"Sales grouping");
                string firstSummary=JsonUtility.ToJson(sale);business.Close();Need(firstSummary==JsonUtility.ToJson(CommerceReceiptData.Sales(business)),"Reading/reclosing changed summary");
                report.success=true;
            }
            catch(Exception error){report.error=error.ToString();}
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v12/ui/receipt-ledger-validation.json"));
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--receipt-output");if(index>=0&&index+1<args.Length)output=Path.GetFullPath(args[index+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.success)Debug.Log("RECEIPT_V12_PASS "+output);else Debug.LogError("RECEIPT_V12_FAIL "+report.error);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        private static void Record(Report report,CommerceReceiptAudit audit)
        {Need(audit.totalsMatch,audit.kind+" sum != ledger");foreach(var line in audit.lines)Need(line.quantity*line.unitCents==line.subtotalCents,"Row arithmetic: "+line.id);report.checkpoints.Add(audit);}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

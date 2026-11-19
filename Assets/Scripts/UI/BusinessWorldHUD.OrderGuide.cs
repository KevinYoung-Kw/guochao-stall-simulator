using System;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public sealed partial class BusinessWorldHUD
    {
        private int guideOrderId=-1;
        private StallInteractionTarget guideFocusAtOpen;
        private string guideHeldAtOpen="",guideMealAtOpen="";
        private RectTransform orderGuide,guideNextBox;
        private Text guideTitle,guideSteps,guideEquipment,guidePackage,guideNext;
        private readonly Image[] guideIngredientIcons=new Image[6];
        private readonly Text[] guideIngredientLabels=new Text[6];
        private FixedCard heldCard;
        private string heldCardKey="";

        private void BuildOrderGuide()
        {
            orderGuide=Card("Customer cooking guide",safe,new Vector2(490,300),paper,false);
            var sheet=CreationMenuArt.SpriteFor(CreationMenuArt.Sheet.Paper);
            if(sheet!=null){var face=orderGuide.GetComponent<Image>();face.sprite=sheet;face.type=Image.Type.Sliced;face.color=Color.white;}
            Anchor(orderGuide,Vector2.one,Vector2.one,new Vector2(-16,-230));
            orderGuide.GetComponent<Image>().raycastTarget=true;
            guideTitle=Text(orderGuide,"",Vector2.zero,new Vector2(366,46),26,ink);guideTitle.fontStyle=FontStyle.Bold;
            Anchor(guideTitle.rectTransform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(-40,-29));
            Button close=Control("收起",orderGuide,new Vector2(72,42),()=>guideOrderId=-1);close.GetComponentInChildren<Text>().fontSize=20;
            Anchor((RectTransform)close.transform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(198,-29));
            for(int i=0;i<6;i++)
            {
                int column=i%3,row=i/3;float x=-218+column*160,y=-82-row*60;
                var rt=Node("Recipe ingredient "+i,orderGuide);Anchor(rt,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(x,y));rt.sizeDelta=Vector2.one*34;
                guideIngredientIcons[i]=rt.gameObject.AddComponent<Image>();guideIngredientIcons[i].preserveAspect=true;guideIngredientIcons[i].raycastTarget=false;
                guideIngredientLabels[i]=Text(orderGuide,"",Vector2.zero,new Vector2(112,60),20,ink);
                Anchor(guideIngredientLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(x+76,y));
            }
            guideSteps=Text(orderGuide,"",new Vector2(0,-4),new Vector2(460,35),22,ink);guideSteps.fontStyle=FontStyle.Bold;
            guideEquipment=Text(orderGuide,"",new Vector2(0,-42),new Vector2(460,60),20,ink);
            guidePackage=Text(orderGuide,"",new Vector2(0,-78),new Vector2(460,34),20,ink);
            guideNextBox=Card("Next preparation step",orderGuide,new Vector2(460,50),CampaignUI.Pale,false);
            guideNext=Text(guideNextBox,"",Vector2.zero,new Vector2(440,46),21,ink);
            orderGuide.gameObject.SetActive(false);
            heldCard=BuildFixedCard("Packed meal in hand",new Vector2(360,196));
            Anchor(heldCard.root,new Vector2(1,0),new Vector2(1,0),new Vector2(-16,110));
        }
        public void ShowOrderGuide(int orderId)
        {
            guideOrderId=guideOrderId==orderId?-1:orderId;
            guideFocusAtOpen=owner.InputHandler?.FocusedTarget;
            guideHeldAtOpen=session.HeldMeal?.id??"";
            guideMealAtOpen=owner.SelectedCounterMealId;
            RefreshOrderGuide(owner.IsPaused);
        }
        private void RefreshOrderGuide(bool paused)
        {
            if(orderGuide==null)return;
            var focus=owner.InputHandler?.FocusedTarget;
            if(guideOrderId>=0&&((focus!=guideFocusAtOpen&&focus!=null&&focus.kind!=StallInteractionKind.Order)
                ||guideHeldAtOpen!=(session.HeldMeal?.id??"")||guideMealAtOpen!=owner.SelectedCounterMealId))guideOrderId=-1;
            StallOrder order=session.FindOrder(guideOrderId);
            bool show=order!=null&&!order.served&&!order.departed&&!paused&&!HasOverlay;
            orderGuide.gameObject.SetActive(show);
            if(!show)return;
            var info=OrderCookingGuide.Read(session,guideOrderId);
            float extra=info.ingredients.Count>3?60:0;
            orderGuide.sizeDelta=new Vector2(490,300+extra);
            Anchor(guideSteps.rectTransform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(0,-131-extra));
            Anchor(guideEquipment.rectTransform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(0,-181-extra));
            Anchor(guidePackage.rectTransform,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(0,-229-extra));
            Anchor(guideNextBox,new Vector2(.5f,1),new Vector2(.5f,.5f),new Vector2(0,-272-extra));
            Set(guideTitle,info.title);
            for(int i=0;i<6;i++)
            {
                bool visible=i<info.ingredients.Count;
                guideIngredientIcons[i].gameObject.SetActive(visible);guideIngredientLabels[i].gameObject.SetActive(visible);
                if(!visible)continue;
                var item=info.ingredients[i];guideIngredientIcons[i].sprite=overlayIcons.Get(item.id);
                int missing=Math.Max(0,item.units-item.available-item.inPan);
                string state=item.alreadyPrepared?"已备":missing>0?"缺 "+missing:item.inPan>0?"锅"+item.inPan+" / 余"+item.available:"库存 "+item.available;
                Set(guideIngredientLabels[i],ChipName(item.id)+" ×"+item.units+"\n"+state);
                guideIngredientLabels[i].color=!item.alreadyPrepared&&missing>0?danger:ink;
            }
            Set(guideSteps,info.steps);Set(guideEquipment,info.equipment);
            Set(guidePackage,info.packaging+" · "+info.flavor);Set(guideNext,info.next);
        }
        private void BuildOrderIngredients(Tag tag,StallOrder order)
        {
            var readout=OrderCookingGuide.Read(session,order.id);
            tag.recipeIngredients=new Image[3];
            for(int i=0;i<3;i++)
            {
                bool hasFlavor=!string.IsNullOrEmpty(order.requestedFlavorId);
                var rt=Node("Order ingredient "+i,tag.root);rt.anchoredPosition=new Vector2(hasFlavor?-38+i*31:-90+i*36,-10);rt.sizeDelta=Vector2.one*29;
                var image=rt.gameObject.AddComponent<Image>();image.raycastTarget=false;image.preserveAspect=true;tag.recipeIngredients[i]=image;
                image.gameObject.SetActive(i<readout.ingredients.Count);
                if(i<readout.ingredients.Count)image.sprite=overlayIcons.Get(readout.ingredients[i].id);
            }
            tag.recipeMethod=Text(tag.root,(readout.ingredients.Count>3?"+"+(readout.ingredients.Count-3)+" · ":"")+OrderCookingGuide.MethodLabel(readout.method),new Vector2(82,-10),new Vector2(70,32),21,ink,TextAnchor.MiddleRight);
            tag.recipeMethod.fontStyle=FontStyle.Bold;
        }
        private void RefreshHeldMealCard(bool paused)
        {
            if(heldCard==null)return;
            CounterMeal meal=session.HeldMeal;
            bool show=meal!=null&&!paused&&!HasOverlay;
            heldCard.root.gameObject.SetActive(show);if(!show)return;
            if(lanePanel!=null)lanePanel.root.gameObject.SetActive(false);
            if(mealCard!=null)mealCard.root.gameObject.SetActive(false);
            Set(heldCard.title,"手上 · "+(meal.product?.name??"成品"));
            Set(heldCard.detail,"1份 · "+CampaignUI.Money(meal.priceCents)+"\n点顾客直接递餐，或先放到取餐区");
            bool place=session.CanPlaceHeldMeal(out string reason)&&owner.CanInteract;
            string key=meal.id+"|"+place+"|"+owner.CanInteract;
            if(key!=heldCardKey)
            {
                heldCardKey=key;RebuildChoices(heldCard,new Vector2(360,196),1);
                SetChoice(heldCard,0,"放到取餐区",()=>owner.PlaceHeldMeal(),place,false);
            }
        }
    }
    // Neutral plate for free combinations without authored dish art.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FreeMealPlateIcon : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();Rect rect=rectTransform.rect;
            for(int ring=0;ring<2;ring++)
            {
                float radius=rect.width*(ring==0?.43f:.34f);
                Color tint=ring==0?CampaignUI.Ink:CampaignUI.PaperColor;
                for(int i=0;i<32;i++)
                {
                    float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;int at=helper.currentVertCount;
                    helper.AddVert(rect.center,tint,Vector2.zero);
                    helper.AddVert(rect.center+new Vector2(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius*.6f),tint,Vector2.zero);
                    helper.AddVert(rect.center+new Vector2(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius*.6f),tint,Vector2.zero);
                    helper.AddTriangle(at,at+1,at+2);
                }
            }
        }
    }
}

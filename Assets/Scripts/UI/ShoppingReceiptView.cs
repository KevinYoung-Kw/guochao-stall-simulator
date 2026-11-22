using System;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public sealed class ShoppingReceiptView : MonoBehaviour
    {
        private readonly Color ink=CampaignUI.Ink,cream=CampaignUI.PaperColor,muted=CampaignUI.Muted,rust=CampaignUI.Vermilion;
        private readonly GameObject[] rows=new GameObject[4],cells=new GameObject[12];
        private readonly Image[] icons=new Image[4],cellIcons=new Image[12];
        private readonly Text[] names=new Text[4],prices=new Text[4],specs=new Text[4],totals=new Text[4],cellLabels=new Text[12];
        private readonly Button[] returns=new Button[4];
        private readonly int[] returnIndices=new int[4];
        private Font font;private GameFlow flow;private Snapshot state;private GameItemIconLibrary iconLibrary;
        private Sprite paper;private Texture2D paperTexture;private RectTransform root;
        private Text status,cash,total,footnote,pager,gridCaption;private Button basketTab,loadedTab,gridTab,previous,next;
        private Image progress;private int mode,page;private string stamp="";
        private CommerceReceiptAudit audit;
        public CommerceReceiptAudit CaptureAudit()=>audit;
        public void ShowLoadedPage(bool loaded){mode=loaded?1:0;page=0;Rebuild();}
        public int VisibleProductIconCount
        {
            get{int count=0;for(int i=0;i<icons.Length;i++)if(rows[i]!=null&&rows[i].activeInHierarchy&&icons[i].enabled&&icons[i].sprite!=null)count++;return count;}
        }
        public void Initialize(RectTransform parent,Font typeface,Action close)
        {
            font=typeface;root=gameObject.AddComponent<RectTransform>();root.SetParent(parent,false);root.anchorMin=root.anchorMax=root.pivot=new Vector2(0,1);root.anchoredPosition=new Vector2(29,-286);root.sizeDelta=new Vector2(500,776);
            iconLibrary=gameObject.AddComponent<GameItemIconLibrary>();paper=StallUIArt.RoundedPaper(out paperTexture);
            Image face=gameObject.AddComponent<Image>();face.sprite=paper;face.type=Image.Type.Sliced;face.color=cream;
            var shadow=gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(35/255f,56/255f,77/255f,.4f);shadow.effectDistance=new Vector2(3,-5);
            TextAt(root,"惠民仓储 · 采买小票",28,19,395,38,24,ink,true);ButtonAt(root,"×",439,17,38,40,close);
            status=TextAt(root,"",28,62,430,36,22,ink,true);cash=TextAt(root,"",28,101,432,32,19,muted);
            basketTab=ButtonAt(root,"购物篮",26,149,166,43,()=>{mode=0;page=0;Rebuild();});
            loadedTab=ButtonAt(root,"已装车",200,149,166,43,()=>{mode=1;page=0;Rebuild();});
            gridTab=ButtonAt(root,"篮格",374,149,100,43,()=>{mode=2;page=0;Rebuild();});
            for(int i=0;i<4;i++)
            {
                int index=i;RectTransform row=Rect(root,"商品 "+i,24,206+i*88,452,84);rows[i]=row.gameObject;
                icons[i]=Rect(row,"真实商品图",0,5,68,68).gameObject.AddComponent<Image>();icons[i].preserveAspect=true;icons[i].raycastTarget=false;
                names[i]=TextAt(row,"",77,0,248,35,21,ink,true);prices[i]=TextAt(row,"",77,32,235,30,19,muted);specs[i]=TextAt(row,"",77,60,280,24,16,muted);
                totals[i]=TextAt(row,"",309,0,143,35,22,ink,true,TextAnchor.MiddleRight);
                returns[i]=ButtonAt(row,"放回",372,41,80,34,()=>{if(flow!=null&&returnIndices[index]>=0)flow.ReturnItem(returnIndices[index]);});
                Rule(row,0,83,452);
            }
            for(int i=0;i<12;i++)
            {
                RectTransform cell=Rect(root,"实际篮格 "+i,130+i%3*82,218+i/3*82,76,76);cells[i]=cell.gameObject;
                Image back=cell.gameObject.AddComponent<Image>();back.sprite=paper;back.type=Image.Type.Sliced;back.color=CampaignUI.Pale;
                cellIcons[i]=Rect(cell,"商品图",4,4,68,68).gameObject.AddComponent<Image>();cellIcons[i].preserveAspect=true;cellIcons[i].raycastTarget=false;
                cellLabels[i]=TextAt(cell,"",5,5,66,66,20,muted,false,TextAnchor.MiddleCenter);
            }
            gridCaption=TextAt(root,"",28,536,444,42,26,ink,false,TextAnchor.MiddleCenter);
            previous=ButtonAt(root,"‹",24,568,68,48,()=>{page=Mathf.Max(0,page-1);Rebuild();});previous.GetComponentInChildren<Text>(true).fontSize=26;
            next=ButtonAt(root,"›",408,568,68,48,()=>{page++;Rebuild();});next.GetComponentInChildren<Text>(true).fontSize=26;
            pager=TextAt(root,"",96,575,308,38,26,ink,false,TextAnchor.MiddleCenter);
            Rule(root,26,616,448);total=TextAt(root,"",28,625,444,47,29,ink,true,TextAnchor.MiddleRight);
            progress=Rect(root,"收银进度",28,679,0,4).gameObject.AddComponent<Image>();progress.color=CampaignUI.Gold;progress.raycastTarget=false;
            footnote=TextAt(root,"",28,690,444,84,26,ink);
        }
        public void Present(GameFlow owner,Snapshot snapshot)
        {
            flow=owner;state=snapshot;if(flow?.Run==null)return;
            bool returnAvailable=false;
            for(int i=0;i<flow.Run.Cart.Count&&!returnAvailable;i++)returnAvailable=flow.CanReturnItem(i);
            string nextStamp=string.Join("|",flow.Run.Cart)+"/"+string.Join("|",flow.Run.Cargo)+":"+state.paid+":"+state.cash+":"+state.targetId+":"+state.primaryEnabled+":"+state.checkoutInProgress+":"+returnAvailable+":"+state.cartCapacity+":"+state.cartCellsUsed+":"+state.cartCount;
            if(nextStamp!=stamp){stamp=nextStamp;if(state.cartCount==0&&state.loaded)mode=1;else if(state.checkoutInProgress)mode=0;Rebuild();}
            if(state.checkoutInProgress)
            {
                status.text="收银中 · 尚未扣款";status.color=rust;
                progress.rectTransform.sizeDelta=new Vector2(444*(1-Mathf.Clamp01(state.checkoutRemaining/Mathf.Max(.01f,state.checkoutDuration))),4);
                footnote.text="请留在柜台 · 剩余 "+state.checkoutRemaining.ToString("0.0")+" 秒\n离开会中断，本次不扣款";
                footnote.color=ink;footnote.fontStyle=FontStyle.Normal;
            }
            else progress.rectTransform.sizeDelta=Vector2.zero;
        }
        private void Rebuild()
        {
            if(flow?.Run==null||state==null)return;
            audit=CommerceReceiptData.Shopping(flow.Run,mode==1);bool loaded=mode==1,grid=mode==2;
            status.text=loaded?"已付款 · 已装车":audit.lines.Length==0?"购物篮空着":audit.status;
            status.color=loaded||state.paid?ink:rust;cash.text="随身现金  "+CommerceReceiptData.Money(flow.Run.CashCents);
            basketTab.GetComponentInChildren<Text>().text="购物篮 "+flow.Run.Cart.Count;loadedTab.GetComponentInChildren<Text>().text="已装车 "+flow.Run.Cargo.Count;
            basketTab.GetComponent<Image>().color=mode==0?CampaignUI.Selected:cream;loadedTab.GetComponent<Image>().color=loaded?CampaignUI.Selected:cream;gridTab.GetComponent<Image>().color=grid?CampaignUI.Selected:cream;
            int pages=grid?Mathf.Max(1,(state.cartCapacity+11)/12):Mathf.Max(1,(audit.lines.Length+3)/4);page=Mathf.Clamp(page,0,pages-1);
            for(int i=0;i<4;i++)
            {
                int index=page*4+i;bool visible=!grid&&index<audit.lines.Length;rows[i].SetActive(visible);returnIndices[i]=-1;if(!visible)continue;
                CommerceReceiptLine line=audit.lines[index];icons[i].sprite=iconLibrary.Get(line.id);icons[i].enabled=icons[i].sprite!=null;
                names[i].text=line.name;prices[i].text=line.quantity+line.unit+" × "+CommerceReceiptData.Money(line.unitCents);specs[i].text=line.contents;totals[i].text=CommerceReceiptData.Money(line.subtotalCents);
                bool canReturn=!loaded&&!state.paid&&!state.checkoutInProgress;returns[i].gameObject.SetActive(canReturn);returns[i].interactable=canReturn&&flow.CanReturnItem(line.sourceIndex);
                returnIndices[i]=line.sourceIndex;
            }
            var layout=ShoppingBasketLayout.Plan(flow.Run.Cart,state.cartCapacity);var shown=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<12;i++)
            {
                int cellIndex=page*12+i;bool visible=grid&&cellIndex<state.cartCapacity;cells[i].SetActive(visible);if(!visible)continue;
                int item=layout.Success&&cellIndex<layout.CellOwners.Count?layout.CellOwners[cellIndex]:-1;bool first=item>=0&&shown.Add(item);
                cellIcons[i].enabled=first;if(first)cellIcons[i].sprite=iconLibrary.Get(flow.Run.Cart[item]);cellLabels[i].text=item<0?(cellIndex+1).ToString():first?"":"占用";
            }
            gridCaption.gameObject.SetActive(grid);gridCaption.text="购物篮 "+state.cartCellsUsed+" / "+state.cartCapacity+" 格";
            previous.gameObject.SetActive(pages>1);next.gameObject.SetActive(pages>1);previous.interactable=page>0;next.interactable=page+1<pages;
            Text previousLabel=previous.GetComponentInChildren<Text>(true),nextLabel=next.GetComponentInChildren<Text>(true);
            previousLabel.text=previous.interactable?"上页":"—";previousLabel.color=ink;
            nextLabel.text=next.interactable?"下页":"—";nextLabel.color=ink;
            pager.gameObject.SetActive(true);pager.text=grid?"篮格 "+(page+1)+" / "+pages:audit.lines.Length==0?(loaded?"还没有商品装上车":"到货架取货，再放进购物篮"):"第 "+(page+1)+" / "+pages+" 页 · "+audit.lines.Length+" 种商品";
            total.text=(loaded?"装车已付  ":state.paid?"本批已付  ":"本批应付  ")+CommerceReceiptData.Money(audit.totalCents);
            bool basketFull=!loaded&&!state.paid&&!state.checkoutInProgress&&state.cartCount>0&&state.cartCapacity>0&&state.cartCellsUsed>=state.cartCapacity;
            footnote.text=basketFull?"购物篮已满 · 推到收银台按 E\n选择要结账的商品":grid?"购物篮用于选货 · 摊车容量另算":loaded?"货物已在三轮车上 · 可继续采购或出发":state.paid?"付款完成 · 到门外三轮车旁按 E 装车":state.cartCount==0?"先挑选商品 · 只有付款才会扣现金":"单价为实际结账价 · 到对应货架可放回";
            footnote.color=basketFull?rust:ink;footnote.fontStyle=basketFull?FontStyle.Bold:FontStyle.Normal;
        }
        private static RectTransform Rect(RectTransform parent,string name,float x,float y,float w,float h)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
        private Text TextAt(RectTransform parent,string value,float x,float y,float w,float h,int size,Color color,bool bold=false,TextAnchor alignment=TextAnchor.MiddleLeft)
        {Text t=Rect(parent,"Label",x,y,w,h).gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.color=color;t.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;t.text=value;t.alignment=alignment;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
        private Button ButtonAt(RectTransform parent,string value,float x,float y,float w,float h,Action action)
        {RectTransform r=Rect(parent,value,x,y,w,h);Image image=r.gameObject.AddComponent<Image>();image.sprite=paper;image.type=Image.Type.Sliced;image.color=cream;Button b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;b.colors=CampaignUI.ButtonColours(b.colors);b.onClick.AddListener(()=>action?.Invoke());TextAt(r,value,2,0,w-4,h,20,ink,true,TextAnchor.MiddleCenter);return b;}
        private void Rule(RectTransform parent,float x,float y,float width){Image line=Rect(parent,"Fold",x,y,width,1).gameObject.AddComponent<Image>();line.color=new Color(35/255f,56/255f,77/255f,.22f);line.raycastTarget=false;}
        private void OnDestroy(){if(paper)Destroy(paper);if(paperTexture)Destroy(paperTexture);}
    }
}

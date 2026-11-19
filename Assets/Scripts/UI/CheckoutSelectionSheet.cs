using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public sealed class CheckoutSelectionSheet : MonoBehaviour
    {
        private sealed class Row
        {
            // FREEDOM19: identity is the basket slot, not the SKU — identical
            // consumable units each get their own row and their own checkbox.
            public int cartIndex;
            public string id;
            public Image paper;
            public Text name, details, price, mark;
        }

        private readonly HashSet<int> selectedIndices = new HashSet<int>();
        private readonly List<Row> rows = new List<Row>();
        private GameFlow flow;
        private GameObject sheet;
        private CampaignFocusScope focusScope;
        private GameItemIconLibrary icons;
        private Text total, cash, balance, count, unselected, status, selectionAction;
        private Button confirm;

        public void Bind(GameFlow owner)
        {
            if (flow == owner) return;
            Hide();
            if (flow != null) flow.CheckoutSelectionRequested -= Open;
            flow = owner;
            if (flow != null) flow.CheckoutSelectionRequested += Open;
        }

        private void Update()
        {
            if (sheet != null && (flow == null || !flow.IsChoosingCheckout)) Hide();
        }

        private void OnDestroy()
        {
            if (flow != null) flow.CheckoutSelectionRequested -= Open;
        }

        private void Open()
        {
            if (flow == null || flow.Run == null || !flow.IsChoosingCheckout || sheet != null) return;
            selectedIndices.Clear();
            for (int i = 0; i < flow.Run.Cart.Count; i++) selectedIndices.Add(i);

            sheet = new GameObject("Checkout selection", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            sheet.transform.SetParent(transform, false);
            Canvas canvas = sheet.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            var scaler = sheet.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var background = new GameObject("Checkout shade", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(sheet.transform, false);
            RectTransform shade = (RectTransform)background.transform;
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.offsetMin = shade.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = CampaignUI.WithAlpha(CampaignUI.Ink, .72f);

            RectTransform board = Panel(sheet.transform, "Checkout desk", Vector2.zero, new Vector2(1210, 754), CreationMenuArt.Sheet.Paper);
            Text title = CampaignUI.Text(board, "收银台", new Vector2(-347, 309), new Vector2(402, 84), 49, CampaignUI.Ink, TextAnchor.MiddleLeft, true);
            title.gameObject.name = "Checkout title";
            count = CampaignUI.Text(board, "", new Vector2(364, 310), new Vector2(376, 62), 29, CampaignUI.Rust, TextAnchor.MiddleRight, true);
            CampaignUI.Rule(board, new Vector2(0, 265), new Vector2(1096, 2));
            CampaignUI.Text(board, "勾选这次要买的货", new Vector2(-317, 227), new Vector2(451, 46), 29, CampaignUI.Ink, TextAnchor.MiddleLeft, true);

            Button clear = CampaignUI.Button(board, "取消全选", new Vector2(58, 227), new Vector2(157, 49), () =>
            {
                if (selectedIndices.Count == 0)
                    for (int i = 0; i < flow.Run.Cart.Count; i++) selectedIndices.Add(i);
                else selectedIndices.Clear();
                UpdateAmounts();
            });
            selectionAction = clear.GetComponentInChildren<Text>();

            RectTransform list = CampaignUI.Scroll(board, "Checkout goods", new Vector2(-214, -23), new Vector2(666, 439), out _);
            if (icons == null) icons = gameObject.AddComponent<GameItemIconLibrary>();
            int index = 0;
            foreach (string id in flow.Run.Cart)
            {
                ItemDefinition item = GameCatalog.Find(id);
                if (item == null) continue;
                AddRow(list, item, index++);
            }
            list.sizeDelta = new Vector2(0, Mathf.Max(439, index * 87 + 10));

            RectTransform bill = Panel(board, "Amount to pay", new Vector2(360, -11), new Vector2(376, 479), CreationMenuArt.Sheet.Navy);
            Color cream = CampaignUI.PaperColor;
            CampaignUI.Text(bill, StallUiCopy.Text("checkout.due","本次应付"), new Vector2(0, 173), new Vector2(319, 50), 30, cream, TextAnchor.MiddleLeft, true);
            total = CampaignUI.Text(bill, "", new Vector2(0, 96), new Vector2(320, 78), 53, cream, TextAnchor.MiddleLeft, true);
            cash = CampaignUI.Text(bill, "", new Vector2(0, 37), new Vector2(320, 38), 23, cream);
            balance = CampaignUI.Text(bill, "", new Vector2(0, -4), new Vector2(320, 42), 23, CampaignUI.Gold, TextAnchor.MiddleLeft, true);
            CampaignUI.Rule(bill, new Vector2(0, -39), new Vector2(313, 2));
            unselected = CampaignUI.Text(bill, "", new Vector2(0, -77), new Vector2(320, 63), 21, cream);
            status = CampaignUI.Text(board, "", new Vector2(361, -304), new Vector2(382, 84), 23, CampaignUI.Ink, TextAnchor.MiddleCenter, true);
            confirm = CampaignUI.Button(bill, "确认结账", new Vector2(0, -156), new Vector2(310, 72), Submit, true);
            confirm.GetComponentInChildren<Text>().fontSize = 31;

            Button back = CampaignUI.Button(board, "继续挑货", new Vector2(-425, -300), new Vector2(225, 66), () =>
            {
                flow.CancelCheckoutSelection();
                Hide();
            });
            back.GetComponentInChildren<Text>().fontSize = 28;
            UpdateAmounts();
            focusScope = new CampaignFocusScope(sheet.transform);
            CampaignUI.Focus(confirm.interactable ? confirm : rows.Count > 0 ? rows[0].paper.GetComponent<Button>() : back);
        }

        private void AddRow(RectTransform list, ItemDefinition item, int cartIndex)
        {
            RectTransform rt = Panel(list, "Checkout row " + cartIndex, Vector2.zero, new Vector2(633, 78), CreationMenuArt.Sheet.Card);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(-4, -cartIndex * 87 - 4);
            Image face = rt.GetComponent<Image>();
            face.raycastTarget = true;
            Button toggle = rt.gameObject.AddComponent<Button>();
            toggle.targetGraphic = face;
            toggle.colors = CampaignUI.ButtonColours(toggle.colors);
            toggle.onClick.AddListener(() =>
            {
                if (!selectedIndices.Remove(cartIndex)) selectedIndices.Add(cartIndex);
                UpdateAmounts();
            });
            CreationHoverLift.Attach(rt, CreationMenuArt.Frame(rt));
            CampaignUI.Icon(rt, icons.Get(item.id), new Vector2(-235, 0), new Vector2(66, 66));
            // Identical units must be distinguishable: label each occurrence
            // with its own ordinal inside the basket.
            int total = 0, ordinal = 0;
            for (int i = 0; i < flow.Run.Cart.Count; i++)
            {
                if (flow.Run.Cart[i] != item.id) continue;
                total++;
                if (i < cartIndex) ordinal++;
            }
            string details = item.name.Contains('·') ? item.name.Split('·')[1] : item.portions > 1 ? item.portions + " 份" : "1 件";
            if (total > 1) details = "篮中第 " + (ordinal + 1) + "/" + total + " 件 · " + details;
            rows.Add(new Row
            {
                cartIndex = cartIndex, id = item.id, paper = face,
                mark = CampaignUI.Text(rt, "", new Vector2(-289, 0), new Vector2(44, 52), 29, CampaignUI.Ink, TextAnchor.MiddleCenter, true),
                name = CampaignUI.Text(rt, item.name.Split('·')[0], new Vector2(-37, 15), new Vector2(289, 39), 26, CampaignUI.Ink, TextAnchor.MiddleLeft, true),
                details = CampaignUI.Text(rt, details,
                    new Vector2(-37, -17), new Vector2(289, 32), 21, CampaignUI.Ink),
                price = CampaignUI.Text(rt, CommerceReceiptData.Money(flow.Run.PurchaseItemPriceCents(item.id)),
                    new Vector2(226, 0), new Vector2(146, 51), 28, CampaignUI.Ink, TextAnchor.MiddleRight, true)
            });
        }

        private void UpdateAmounts()
        {
            foreach (Row row in rows)
            {
                bool included = selectedIndices.Contains(row.cartIndex);
                row.paper.color = included ? CampaignUI.Selected : Color.white;
                row.mark.text = included ? "✓" : "□";
                row.name.color = row.details.color = row.price.color = included ? CampaignUI.Ink : CampaignUI.Muted;
            }
            string[] items = SelectedIds();
            bool canPay = flow.Run.PreviewCheckoutSelection(items, out int due, out string reason);
            count.text = "已选 " + items.Length + "／" + flow.Run.Cart.Count + " 件";
            total.text = CommerceReceiptData.Money(due);
            int inHand = flow.Run.CashCents;
            cash.text = StallUiCopy.Format("checkout.cash","手头现金 {0}",CommerceReceiptData.Money(inHand));
            balance.text = canPay ? StallUiCopy.Format("checkout.balance","付完剩 {0}",CommerceReceiptData.Money(inHand-due)) : "";
            int left = flow.Run.Cart.Count - items.Length;
            unselected.text = left > 0 ? StallUiCopy.Format("checkout.returned","未选的 {0} 件结账后退回",left)
                : StallUiCopy.Text("checkout.all","这篮货都选上了");
            if (canPay) status.text = StallUiCopy.Text("checkout.next","付完去门外装车");
            else if (items.Length == 0) status.text = StallUiCopy.Text("checkout.empty","勾上这次要买的货");
            else if (due > inHand) status.text = StallUiCopy.Format("checkout.short","还差 {0}，可以少勾几件",CommerceReceiptData.Money(due-inHand));
            else if (due > 0 && flow.Run.Phase == GamePhase.Market && !flow.Run.Paused && !flow.Run.Paid && !flow.Run.CheckoutInProgress)
                status.text = StallUiCopy.Text("checkout.capacity","这批货装不下，先少勾几件");
            else status.text = reason;
            status.color = canPay ? CampaignUI.Ink : CampaignUI.Rust;
            confirm.interactable = canPay;
            selectionAction.text = items.Length == 0 ? "全选" : "取消全选";
        }

        private string[] SelectedIds()
        {
            var ids = new List<string>();
            for (int i = 0; i < flow.Run.Cart.Count; i++)
                if (selectedIndices.Contains(i)) ids.Add(flow.Run.Cart[i]);
            return ids.ToArray();
        }

        private void Submit()
        {
            if (flow.ConfirmCheckoutSelection(SelectedIds())) Hide();
            else UpdateAmounts();
        }

        private void Hide()
        {
            if (sheet == null) return;
            sheet.SetActive(false);
            Destroy(sheet);
            sheet = null;
            rows.Clear();
            selectedIndices.Clear();
            focusScope?.Restore();
            focusScope = null;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, CreationMenuArt.Sheet material)
        {
            RectTransform panel = CreationMenuArt.Panel(parent, name, 0, 0, size.x, size.y, material);
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = position;
            return panel;
        }
    }
}

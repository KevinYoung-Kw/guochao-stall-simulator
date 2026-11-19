using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Read-only customer chatter. The orders, input and accounts stay with the
    // business controller, and every message has to fit beside its own customer.
    public sealed class CustomerSpeechBubbles : MonoBehaviour
    {
        private const string ResourcePath = "UI/CustomerSpeech.zh-CN";
        private static readonly Color SpeechInk = new Color32(38, 52, 69, 255);
        private static readonly Color SpeechCream = new Color32(255, 243, 215, 255);
        // Free-kitchen orders have a product identity. Their recipe index is
        // sometimes just a visual adapter, so only named orders get dish lines.
        private static readonly FieldInfo ProductId = typeof(StallOrder).GetField("productId");
        private static readonly int[] Placements = { 0, 1, -1, 2, -2 };
        private static readonly string[] Flavors = { "S01", "S02", "S05" };

        [Serializable]
        private sealed class Line
        {
            public string trigger = "", speaker = "", recipe = "", text = "";
            public bool rare = false;
        }

        [Serializable]
        private sealed class Book
        {
            public Line[] lines = Array.Empty<Line>();
        }

        private BusinessController owner;
        private BusinessWorldHUD hud;
        private BusinessSession session;
        private Canvas canvas;
        private Line[] bank;
        private Font font;
        private readonly HashSet<int> arrivals = new(), waits = new(), departures = new();
        private readonly Queue<string> recent = new();
        private readonly List<StallInteractionTarget> targets = new();
        private readonly List<Button> controls = new();
        private readonly System.Random speechRandom = new();
        private readonly Vector3[] rectCorners = new Vector3[4];

        private RectTransform bubble, tip;
        private Text label;
        private Texture2D paperTexture;
        private Sprite paperSprite;
        private int activeOrder = -1, pendingOrder = -1, pendingPriority, placement;
        private Line activeLine, pendingLine;
        private float scanAt, quietUntil, rareAfter, activeUntil, pendingAt, pendingUntil;
        private bool firstArrival = true;
        private int screenWidth, screenHeight, layoutReadyAtFrame;
        private bool refitAfterResize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameFlow.PresentationEnabled && GameFlow.Instance != null)
                Connect(GameFlow.Instance);
        }

        // The normal scene's GameFlow already publishes when play reaches the
        // stall. Attach once per HUD so the Hub, market and setup are unaffected.
        private static void Connect(GameFlow flow)
        {
            BusinessWorldHUD mountedHud = null;
            BusinessController mountedController = null;
            void OnSnapshot(Snapshot state)
            {
                if (state == null || state.phase != GamePhase.Business) return;
                var controller = flow.Business;
                if (controller == null || controller.Session == null || controller.Stage == null) return;
                if (mountedController == controller && mountedHud != null && mountedHud.isActiveAndEnabled) return;

                var worldHud = controller.GetComponentInChildren<BusinessWorldHUD>();
                if (worldHud == null) return;
                mountedHud = worldHud;
                mountedController = controller;
                Attach(controller, worldHud);
            }

            flow.Changed += OnSnapshot;
            OnSnapshot(flow.State);
        }

        private static void Attach(BusinessController controller, BusinessWorldHUD worldHud)
        {
            if (worldHud.GetComponentInChildren<CustomerSpeechBubbles>(true) != null) return;
            var source = Resources.Load<TextAsset>(ResourcePath);
            var font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            var parentCanvas = worldHud.GetComponentInParent<Canvas>();
            if (source == null || font == null || parentCanvas == null ||
                parentCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) return;

            Book book;
            try { book = JsonUtility.FromJson<Book>(source.text); }
            catch (ArgumentException)
            {
                Debug.LogWarning("Customer speech lines could not be loaded.");
                return;
            }
            if (book?.lines == null || book.lines.Length == 0) return;

            RectTransform root = Node("顾客说话", worldHud.transform);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var presenter = root.gameObject.AddComponent<CustomerSpeechBubbles>();
            presenter.owner = controller;
            presenter.hud = worldHud;
            presenter.session = controller.Session;
            presenter.canvas = parentCanvas.rootCanvas;
            presenter.bank = book.lines;
            presenter.font = font;
            presenter.Build();

            // A restored guest should not replay greetings or past complaints.
            foreach (var order in presenter.session.Orders)
            {
                if (order.arrived) presenter.arrivals.Add(order.id);
                if (order.departed) presenter.departures.Add(order.id);
                if (order.arrived && order.patience <= order.maxPatience * .45f)
                    presenter.waits.Add(order.id);
            }
            presenter.session.SaleCompleted += presenter.OnSale;
        }

        private void Build()
        {
            bubble = Node("Speech bubble", transform);
            bubble.anchorMin = bubble.anchorMax = new Vector2(.5f, .5f);
            bubble.pivot = new Vector2(.5f, 0);
            var face = bubble.gameObject.AddComponent<Image>();
            paperSprite = StallUIArt.RoundedPaper(out paperTexture);
            face.sprite = paperSprite;
            face.type = Image.Type.Sliced;
            face.color = SpeechInk;
            face.raycastTarget = false;

            tip = Node("Speaker pointer", bubble);
            tip.sizeDelta = new Vector2(16, 10);
            tip.SetAsFirstSibling();
            var pointer = tip.gameObject.AddComponent<StallSpeechTail>();
            pointer.color = SpeechInk;
            pointer.raycastTarget = false;

            var text = Node("Customer line", bubble);
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(14, 5);
            text.offsetMax = new Vector2(-14, -5);
            label = text.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = SpeechCream;
            label.raycastTarget = false;
            label.supportRichText = false;
            bubble.gameObject.SetActive(false);
        }

        private static RectTransform Node(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        private void LateUpdate()
        {
            if (owner == null || hud == null || !owner.CanInteract || hud.HasOverlay)
            {
                if (bubble != null) bubble.gameObject.SetActive(false);
                return;
            }

            if (screenWidth != Screen.width || screenHeight != Screen.height)
            {
                screenWidth = Screen.width;
                screenHeight = Screen.height;
                layoutReadyAtFrame = Time.frameCount + 2;
                refitAfterResize = true;
                bubble.gameObject.SetActive(false);
            }
            if (Time.frameCount < layoutReadyAtFrame) return;

            float now = session.Elapsed;
            if (now >= scanAt)
            {
                scanAt = now + .16f;
                owner.Stage.GetComponentsInChildren(false, targets);
                ObserveOrders(now);
                if (activeOrder >= 0 || pendingOrder >= 0) hud.GetComponentsInChildren(false, controls);
            }

            if (activeOrder >= 0)
            {
                bool fits = now < activeUntil && StillRelevant(activeOrder, activeLine) &&
                    (refitAfterResize ? TryPlace(activeOrder) : Place(activeOrder, placement));
                refitAfterResize = false;
                if (fits)
                {
                    bubble.gameObject.SetActive(true);
                    return;
                }
                activeOrder = -1;
                quietUntil = now + 1.5f;
                bubble.gameObject.SetActive(false);
            }
            if (pendingOrder < 0 || now < quietUntil || now < pendingAt) return;
            if (now > pendingUntil || !StillRelevant(pendingOrder, pendingLine))
            {
                pendingOrder = -1;
                return;
            }

            label.text = pendingLine.text;
            float width = Mathf.Clamp(label.preferredWidth + 32, 142, 218);
            bubble.sizeDelta = new Vector2(width, 76);
            bubble.sizeDelta = new Vector2(width, Mathf.Clamp(label.preferredHeight + 14, 46, 104));
            if (!TryPlace(pendingOrder)) return;

            refitAfterResize = false;
            activeOrder = pendingOrder;
            activeLine = pendingLine;
            pendingOrder = -1;
            activeUntil = now + Mathf.Clamp(1.85f + activeLine.text.Length * .07f, 2.4f, 3.3f);
            if (activeLine.rare) rareAfter = now + 38f;
            recent.Enqueue(activeLine.text);
            if (recent.Count > 8) recent.Dequeue();
            bubble.gameObject.SetActive(true);
        }

        private void ObserveOrders(float now)
        {
            foreach (var order in session.Orders)
            {
                if (order.served || !order.arrived) continue;
                if (order.departed)
                {
                    if (departures.Add(order.id) &&
                        (order.departureReason == "patience_waiting" || order.departureReason == "patience_during_service") &&
                        speechRandom.NextDouble() < .28)
                        Offer(order, "left", 2, now);
                    continue;
                }
                if (arrivals.Add(order.id))
                {
                    if (firstArrival || speechRandom.NextDouble() < .54)
                    {
                        string flavor = order.requestedFlavorId;
                        Offer(order, Array.IndexOf(Flavors, flavor) >= 0 ? flavor : "arrived", 1, now);
                    }
                    firstArrival = false;
                }
                if (order.maxPatience > 0 && order.patience <= order.maxPatience * .45f &&
                    waits.Add(order.id) && speechRandom.NextDouble() < .32)
                    Offer(order, "wait", 1, now);
            }
        }

        private bool StillRelevant(int orderId, Line line)
        {
            var order = session.FindOrder(orderId);
            if (order == null || line == null) return false;
            if (line.trigger == "paid") return order.served && order.paidCents > 0 && order.riskSerial == 0;
            if (line.trigger == "left") return order.departed && !order.served;
            return order.arrived && !order.departed && !order.served;
        }

        private void OnSale(int orderId, int recipeIndex, int paidCents, bool shortSeasoning)
        {
            if (paidCents <= 0 || speechRandom.NextDouble() >= .48) return;
            StallOrder order = session.FindOrder(orderId);
            if (order == null || order.riskSerial > 0) return;
            // The amount gets the first moment of attention at the register.
            Offer(order, "paid", 3, session.Elapsed + 1.25f);
        }

        private void Offer(StallOrder order, string trigger, int priority, float when)
        {
            if (pendingOrder >= 0 && session.Elapsed <= pendingUntil && pendingPriority >= priority) return;
            string speaker = SpeakerFor(order.id);
            string recipe = RecipeFor(order);
            bool allowRare = session.Elapsed >= rareAfter && speechRandom.NextDouble() < .32;
            Line choice = null;
            int best = int.MinValue;
            int tied = 0;
            foreach (var line in bank)
            {
                if (line == null || line.trigger != trigger || string.IsNullOrWhiteSpace(line.text) ||
                    (line.rare && !allowRare) || (!string.IsNullOrEmpty(line.speaker) && line.speaker != speaker) ||
                    (!string.IsNullOrEmpty(line.recipe) && line.recipe != recipe) || recent.Contains(line.text)) continue;
                int score = (line.rare ? 4 : 0) + (string.IsNullOrEmpty(line.recipe) ? 0 : 2)
                    + (string.IsNullOrEmpty(line.speaker) ? 0 : 1);
                if (score > best)
                {
                    choice = line;
                    best = score;
                    tied = 1;
                }
                else if (score == best && speechRandom.Next(++tied) == 0)
                    choice = line;
            }
            if (choice == null) return;
            pendingLine = choice;
            pendingOrder = order.id;
            pendingPriority = priority;
            pendingAt = when;
            pendingUntil = when + 2.6f;
        }

        private static string RecipeFor(StallOrder order)
        {
            if (ProductId?.GetValue(order) is string id && id.Length > 0) return "";
            int index = order.recipeIndex;
            return index >= 0 && index < BusinessSession.Recipes.Length ? BusinessSession.Recipes[index].id : "";
        }

        private string SpeakerFor(int orderId)
        {
            var actor = FindOrderTarget(orderId)?.transform.parent;
            if (actor == null) return "";
            if (actor.Find("CustomerCourier")) return "courier";
            if (actor.Find("CustomerWorker")) return "worker";
            if (actor.Find("CustomerAuntie")) return "auntie";
            if (actor.Find("CustomerStudentV15")) return "student";
            return "";
        }

        private StallInteractionTarget FindOrderTarget(int id)
        {
            foreach (var target in targets)
                if (target != null && target.kind == StallInteractionKind.Order && target.orderId == id)
                    return target;
            return null;
        }

        private bool TryPlace(int id)
        {
            foreach (int side in Placements)
            {
                if (!Place(id, side)) continue;
                placement = side;
                return true;
            }
            return false;
        }

        private bool Place(int orderId, int side)
        {
            var stage = owner.Stage;
            if (stage == null || stage.View == null || !stage.TryGetGuestAnchor(orderId, out Vector3 world)) return false;
            StallInteractionTarget target = FindOrderTarget(orderId);
            Vector3 anchor = target != null ? target.WorldAnchor : world + Vector3.up * .55f;
            Vector3 screen = stage.View.WorldToScreenPoint(anchor);
            if (screen.z <= 0) return false;

            float scale = canvas.scaleFactor;
            float half = bubble.sizeDelta.x * scale * .5f;
            float height = bubble.sizeDelta.y * scale;
            float offset = side == 0 ? 0 : Mathf.Sign(side) * (half + 61 * scale);
            float bottom = side == 0 ? screen.y + 10 * scale : screen.y - height * .50f - (Mathf.Abs(side) == 2 ? 54 : 12) * scale;
            Vector2 point = new Vector2(screen.x + offset, bottom);
            var bounds = new Rect(point.x - half, bottom, half * 2, height);
            Rect safe = Screen.safeArea;
            if (bounds.xMin < safe.xMin + 8 || bounds.xMax > safe.xMax - 8 ||
                bounds.yMin < safe.yMin + 70 * scale || bounds.yMax > safe.yMax - 85 * scale) return false;

            foreach (var visible in targets)
            {
                if (visible == null) continue;
                Rect used = hud.ReadoutScreenRect(visible);
                if (used.width > 0 && used.height > 0 && bounds.Overlaps(used)) return false;
                if (visible.kind == StallInteractionKind.Order)
                {
                    Vector3 head = stage.View.WorldToScreenPoint(visible.WorldAnchor);
                    var face = new Rect(head.x - 53 * scale, head.y - 84 * scale, 106 * scale, 90 * scale);
                    if (head.z > 0 && bounds.Overlaps(face)) return false;
                }
            }
            // Include any clickable fixed controls, besides the world readouts.
            foreach (var button in controls)
            {
                if (button == null || !button.isActiveAndEnabled || !button.interactable) continue;
                ((RectTransform)button.transform).GetWorldCorners(rectCorners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(null, rectCorners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(null, rectCorners[2]);
                if (bounds.Overlaps(Rect.MinMaxRect(a.x, a.y, b.x, b.y))) return false;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, point, null, out Vector2 local)) return false;

            bubble.anchoredPosition = local;
            tip.anchorMin = tip.anchorMax = new Vector2(side == 0 ? .5f : side > 0 ? 0 : 1, side == 0 ? 0 : Math.Abs(side) == 2 ? .77f : .55f);
            tip.pivot = new Vector2(.5f, 1);
            tip.anchoredPosition = Vector2.zero;
            tip.localRotation = Quaternion.Euler(0, 0, side == 0 ? 0 : side > 0 ? -90 : 90);
            return true;
        }

        private void OnDestroy()
        {
            if (session != null) session.SaleCompleted -= OnSale;
            if (paperSprite != null) Destroy(paperSprite);
            if (paperTexture != null) Destroy(paperTexture);
        }
    }
}

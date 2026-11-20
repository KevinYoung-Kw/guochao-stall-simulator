using UnityEngine;

namespace Guochao
{
    // B05：共享单车堵住摊前的实体表现（纯程序建模与拾取挂载，不含业务逻辑）。
    // 在场显隐与文案严格读取 session.BikeBlocked / session.BikeVisibleMask，
    // 挪车事务统一由 Controller 经 session.TryMoveBike(index) 提交。
    public sealed partial class StreetStallStage
    {
        public const int BlockedBikeCount = 2;

        // 默认在场景中单车上方显示紧凑世界状态牌；若 2D HUD 已承担该状态条，
        // 可在接线处将本开关置为 false 以避免重复显示。
        public bool ShowWorldBikeStatusBanner { get; set; } = true;

        private static readonly Vector3[] BlockedBikePositions =
        {
            new Vector3(-.69f, 0f, 2.52f),
            new Vector3(.97f, 0f, 2.56f)
        };
        private static readonly float[] BlockedBikeYaws = { 24f, -22f };

        private Transform blockedBikesRoot;
        private readonly Transform[] blockedBikeRoots = new Transform[BlockedBikeCount];
        private readonly Transform[] blockedBikeHighlights = new Transform[BlockedBikeCount];
        private readonly StallInteractionTarget[] blockedBikeTargets = new StallInteractionTarget[BlockedBikeCount];
        private Transform bikeStatusBanner;
        private TextMesh bikeStatusTextMesh;
        private string lastBikeStatusText = "";
        private int highlightedBikeIndex = -1;

        private Material bikeFrameAmber, bikeFrameBlue, bikeRubber, bikeAccent, bikeHighlightMat, bikeBannerBackMat, bikeBannerEdgeMat;

        public static string FormatBikeBlockedStatus(BusinessSession session)
        {
            if (session == null || !session.BikeBlocked) return "";
            int mask = session.BikeVisibleMask;
            int remaining = ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0);
            // x 表示已挪开数量（0/2 -> 1/2）；若业务/UI 约定 x 为剩余数量，改为 remaining 即可。
            int cleared = Mathf.Clamp(BlockedBikeCount - remaining, 0, BlockedBikeCount);
            return "单车堵摊 · 客流减半 · 点单车挪开 (" + cleared + "/" + BlockedBikeCount + ")";
        }

        public bool TryGetBlockedBikeAnchor(int index, out Vector3 world)
        {
            world = Vector3.zero;
            if (index < 0 || index >= BlockedBikeCount) return false;
            StallInteractionTarget target = blockedBikeTargets[index];
            if (target == null || !target.isActiveAndEnabled) return false;
            world = target.WorldAnchor;
            return true;
        }

        public void SetHighlightedBicycle(int index)
        {
            int next = index >= 0 && index < BlockedBikeCount ? index : -1;
            highlightedBikeIndex = next;
            for (int i = 0; i < BlockedBikeCount; i++)
            {
                if (blockedBikeHighlights[i] == null) continue;
                bool bikeActive = blockedBikeRoots[i] != null && blockedBikeRoots[i].gameObject.activeSelf;
                blockedBikeHighlights[i].gameObject.SetActive(bikeActive && i == highlightedBikeIndex);
            }
        }

        private void BuildBlockedBicycles()
        {
            if (blockedBikesRoot != null) return;
            // 无绿色主题：暖明黄 + 晴钴蓝共享单车，搭配炭黑橡胶、拉丝钢、深墨与暖米灰。
            bikeFrameAmber = Paint("Shared bike frame amber", new Color(.90f, .64f, .14f), .38f);
            bikeFrameBlue = Paint("Shared bike frame cobalt", new Color(.22f, .49f, .76f), .38f);
            bikeRubber = Paint("Shared bike tire rubber", new Color(.15f, .15f, .16f), .08f);
            bikeAccent = Paint("Shared bike accent cream", new Color(.94f, .89f, .78f), .25f);
            bikeHighlightMat = Paint("Shared bike highlight ring", new Color(.95f, .76f, .30f), .20f);
            bikeBannerBackMat = Paint("Bike status banner ink", CampaignUI.Ink, .08f);
            bikeBannerEdgeMat = Paint("Bike status banner vermilion", new Color(.78f, .28f, .17f), .12f);

            blockedBikesRoot = Group("Blocked shared bicycles", transform);

            for (int i = 0; i < BlockedBikeCount; i++)
            {
                Transform bikeRoot = Group("Blocked shared bicycle " + (i + 1), blockedBikesRoot);
                bikeRoot.position = BlockedBikePositions[i];
                bikeRoot.rotation = Quaternion.Euler(0f, BlockedBikeYaws[i], 0f);

                // 静态程序建模车身：轮胎/轮圈/辐条、车架、前叉、车把、车筐、车座、智能锁与脚撑。
                Transform visual = Group("Procedural bicycle visual", bikeRoot);
                visual.localRotation = Quaternion.Euler(6f, 0f, 0f); // 靠向脚撑一侧自然微倾
                BuildProceduralBicycle(visual, i == 0 ? bikeFrameAmber : bikeFrameBlue);
                CombineDecorations(visual);

                // 静态暖琥珀选中/悬停地圈（无闪烁、无碰撞）。
                Transform highlight = Group("Bicycle selection ring", bikeRoot);
                BuildBicycleSelectionRing(highlight, .66f, .30f, .022f, bikeHighlightMat);
                CombineDecorations(highlight);
                highlight.gameObject.SetActive(false);

                // 仅挂 Layer 29 紧贴触发器，绝不加 SolidLayer/QueryVisualLayer，
                // 避免阻挡顾客导航、摊主走位或其他目标拾取。
                StallInteractionTarget target = Target(
                    bikeRoot,
                    StallInteractionKind.Bicycle,
                    "B05_bike_" + i,
                    i,
                    bikeRoot.position + new Vector3(0f, .84f, 0f),
                    new Vector3(1.12f, .88f, .44f),
                    24f);
                if (target.HitCollider is BoxCollider box)
                    box.center = new Vector3(0f, .45f, 0f);

                blockedBikeRoots[i] = bikeRoot;
                blockedBikeHighlights[i] = highlight;
                blockedBikeTargets[i] = target;

                // 组合完成后初始隐藏，由 RefreshBlockedBicycles 按业务 mask 驱动显隐。
                bikeRoot.gameObject.SetActive(false);
            }

            BuildBikeStatusBanner();
            SetLayers(blockedBikesRoot);
        }

        private void BuildProceduralBicycle(Transform parent, Material frameMat)
        {
            const float wheelRadius = .24f;
            const float axleY = .262f;
            Vector3 rearAxle = new Vector3(-.34f, axleY, 0f);
            Vector3 frontAxle = new Vector3(.34f, axleY, 0f);

            // 前后车轮（空心橡胶外胎 + 金属轮圈 + 轮毂 + 辐条）
            BuildBicycleWheel("Rear wheel", parent, rearAxle, wheelRadius);
            BuildBicycleWheel("Front wheel", parent, frontAxle, wheelRadius);

            // 主车架关键节点（低跨式共享单车轮廓）
            Vector3 bb = new Vector3(-.04f, .255f, 0f);
            Vector3 seatTop = new Vector3(-.14f, .61f, 0f);
            Vector3 headBot = new Vector3(.21f, .56f, 0f);
            Vector3 headTop = new Vector3(.17f, .75f, 0f);

            Between("Down tube", parent, bb, headBot, .042f, frameMat);
            Between("Step-through top tube", parent, new Vector3(-.11f, .52f, 0f), new Vector3(.18f, .69f, 0f), .030f, frameMat);
            Between("Head tube", parent, headBot, headTop, .042f, frameMat);
            Between("Seat tube", parent, bb, seatTop, .036f, frameMat);

            // 后叉（双链叉 + 双上叉）与前叉
            for (int side = -1; side <= 1; side += 2)
            {
                float zRear = side * .032f;
                float zFork = side * .034f;
                Between("Chainstay", parent, bb + new Vector3(0f, 0f, zRear), rearAxle + new Vector3(0f, 0f, zRear), .020f, frameMat);
                Between("Seatstay", parent, new Vector3(-.13f, .56f, side * .026f), rearAxle + new Vector3(0f, 0f, zRear), .018f, frameMat);
                Between("Front fork blade", parent, headBot + new Vector3(0f, 0f, zFork), frontAxle + new Vector3(0f, 0f, zFork), .024f, frameMat);
            }

            // 前后挡泥板弧段
            BuildBicycleFender("Rear fender", parent, rearAxle, wheelRadius + .035f, 55f, 145f, frameMat);
            BuildBicycleFender("Front fender", parent, frontAxle, wheelRadius + .035f, 50f, 130f, frameMat);

            // 立管与车把（横梁 + 微后掠把套 + 车铃）
            Vector3 stemTop = new Vector3(.15f, .88f, 0f);
            Between("Handlebar stem", parent, headTop, stemTop, .026f, steel);
            Between("Handlebar crossbar", parent, stemTop + new Vector3(0f, 0f, -.17f), stemTop + new Vector3(0f, 0f, .17f), .024f, steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 gripStart = stemTop + new Vector3(0f, 0f, side * .17f);
                Vector3 gripEnd = new Vector3(.08f, .87f, side * .24f);
                Between("Handlebar grip", parent, gripStart, gripEnd, .034f, ink);
            }
            Shape("Bike bell", parent, new Vector3(.14f, .905f, .12f), new Vector3(.035f, .028f, .035f), bikeAccent, PrimitiveType.Sphere);

            // 前车筐（共享单车高辨识度特征）
            BuildBicycleBasket(parent, headBot);

            // 座管与车座
            Vector3 saddleBase = new Vector3(-.17f, .74f, 0f);
            Between("Seatpost", parent, seatTop, saddleBase, .024f, steel);
            Shape("Saddle cushion", parent, new Vector3(-.18f, .76f, 0f), new Vector3(.21f, .052f, .13f), ink);
            Shape("Saddle nose", parent, new Vector3(-.08f, .758f, 0f), new Vector3(.08f, .042f, .065f), ink);

            // 牙盘与脚踏
            Transform chainring = Shape("Chainring", parent, bb + new Vector3(0f, 0f, .042f), new Vector3(.13f, .010f, .13f), ink, PrimitiveType.Cylinder).transform;
            chainring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Between("Crank arm R", parent, bb + new Vector3(0f, 0f, .048f), bb + new Vector3(.08f, -.07f, .055f), .016f, steel);
            Between("Crank arm L", parent, bb + new Vector3(0f, 0f, -.048f), bb + new Vector3(-.08f, .07f, -.055f), .016f, steel);
            Shape("Pedal R", parent, bb + new Vector3(.08f, -.07f, .085f), new Vector3(.065f, .022f, .055f), ink);
            Shape("Pedal L", parent, bb + new Vector3(-.08f, .07f, -.085f), new Vector3(.065f, .022f, .055f), ink);

            // 后轮智能马蹄锁与扫码牌（无绿）
            Shape("Rear smart lock", parent, new Vector3(-.24f, .45f, 0f), new Vector3(.07f, .075f, .095f), ink);
            Shape("Lock badge", parent, new Vector3(-.24f, .492f, 0f), new Vector3(.045f, .012f, .05f), bikeAccent);

            // 脚撑
            Between("Kickstand", parent, new Vector3(-.07f, .24f, -.03f), new Vector3(-.15f, .01f, -.16f), .016f, ink);
        }

        private void BuildBicycleWheel(string label, Transform parent, Vector3 axle, float radius)
        {
            const int tireSegments = 18;
            float rimRadius = radius - .035f;
            for (int s = 0; s < tireSegments; s++)
            {
                float a0 = s * Mathf.PI * 2f / tireSegments;
                float a1 = (s + 1) * Mathf.PI * 2f / tireSegments;
                Vector3 t0 = axle + new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0f);
                Vector3 t1 = axle + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0f);
                Between(label + " tire", parent, t0, t1, .044f, bikeRubber);

                Vector3 r0 = axle + new Vector3(Mathf.Cos(a0) * rimRadius, Mathf.Sin(a0) * rimRadius, 0f);
                Vector3 r1 = axle + new Vector3(Mathf.Cos(a1) * rimRadius, Mathf.Sin(a1) * rimRadius, 0f);
                Between(label + " rim", parent, r0, r1, .016f, steel);
            }

            // 轮毂与 6 向辐条（3 根贯通直径杆）
            Transform hub = Shape(label + " hub", parent, axle, new Vector3(.044f, .036f, .044f), steel, PrimitiveType.Cylinder).transform;
            hub.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int k = 0; k < 3; k++)
            {
                float angle = k * Mathf.PI / 3f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * rimRadius;
                Between(label + " spoke", parent, axle - dir, axle + dir, .009f, steel);
            }
        }

        private void BuildBicycleFender(string label, Transform parent, Vector3 axle, float radius, float startDeg, float endDeg, Material mat)
        {
            const int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(startDeg, endDeg, i / (float)steps) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(startDeg, endDeg, (i + 1) / (float)steps) * Mathf.Deg2Rad;
                Vector3 p0 = axle + new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0f);
                Vector3 p1 = axle + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0f);
                Between(label, parent, p0, p1, .028f, mat);
            }
        }

        private void BuildBicycleBasket(Transform parent, Vector3 headBot)
        {
            Vector3 center = new Vector3(.31f, .72f, 0f);
            float hx = .10f, hy = .075f, hz = .125f;

            Shape("Basket floor", parent, center + new Vector3(0f, -hy, 0f), new Vector3(hx * 2f, .018f, hz * 2f), ink);
            Between("Basket bracket", parent, headBot, center + new Vector3(-.04f, -hy, 0f), .018f, steel);

            Vector3[] bottom =
            {
                center + new Vector3(-hx, -hy, -hz),
                center + new Vector3( hx, -hy, -hz),
                center + new Vector3( hx, -hy,  hz),
                center + new Vector3(-hx, -hy,  hz)
            };
            Vector3[] top =
            {
                center + new Vector3(-hx, hy, -hz),
                center + new Vector3( hx, hy, -hz),
                center + new Vector3( hx, hy,  hz),
                center + new Vector3(-hx, hy,  hz)
            };
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                Between("Basket post", parent, bottom[i], top[i], .014f, steel);
                Between("Basket top rail", parent, top[i], top[next], .015f, steel);
                Between("Basket mid rail", parent, Vector3.Lerp(bottom[i], top[i], .5f), Vector3.Lerp(bottom[next], top[next], .5f), .011f, steel);
            }
        }

        private void BuildBicycleSelectionRing(Transform parent, float rx, float rz, float thickness, Material mat)
        {
            const int segments = 20;
            const float y = .018f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * rx, y, Mathf.Sin(a0) * rz);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * rx, y, Mathf.Sin(a1) * rz);
                Between("Selection ring segment", parent, p0, p1, thickness, mat);
            }
        }

        private void BuildBikeStatusBanner()
        {
            bikeStatusBanner = Group("Blocked bicycle status banner", blockedBikesRoot);
            bikeStatusBanner.position = new Vector3(.14f, 1.32f, 2.58f);
            if (View != null) bikeStatusBanner.rotation = View.transform.rotation;

            // 紧凑墨蓝底牌 + 朱红底边，无碰撞、无闪烁、无绿色。
            Shape("Banner plate", bikeStatusBanner, Vector3.zero, new Vector3(3.12f, .32f, .014f), bikeBannerBackMat);
            Shape("Banner accent bar", bikeStatusBanner, new Vector3(0f, -.164f, -.002f), new Vector3(3.12f, .024f, .016f), bikeBannerEdgeMat);

            Transform labelNode = Group("Banner text", bikeStatusBanner);
            labelNode.localPosition = new Vector3(0f, .004f, -.012f);
            bikeStatusTextMesh = labelNode.gameObject.AddComponent<TextMesh>();
            Font font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            if (font != null) bikeStatusTextMesh.font = font;
            bikeStatusTextMesh.fontSize = 48;
            bikeStatusTextMesh.characterSize = .028f;
            bikeStatusTextMesh.anchor = TextAnchor.MiddleCenter;
            bikeStatusTextMesh.alignment = TextAlignment.Center;
            bikeStatusTextMesh.color = new Color(1f, .94f, .82f);
            bikeStatusTextMesh.text = "";

            Shader labelShader = Resources.Load<Shader>("Shaders/StallWorldLabel");
            if (labelShader != null && bikeStatusTextMesh.font != null)
            {
                Material textMat = new Material(labelShader) { name = "Stall_BikeBannerLabel" };
                textMat.mainTexture = bikeStatusTextMesh.font.material.mainTexture;
                owned.Add(textMat);
                labelNode.GetComponent<Renderer>().sharedMaterial = textMat;
            }

            bikeStatusBanner.gameObject.SetActive(false);
        }

        public void RefreshBlockedBicycles(BusinessSession session)
        {
            if (blockedBikesRoot == null) return;
            int mask = session != null ? session.BikeVisibleMask : 0;
            bool anyChanged = false;
            for (int i = 0; i < BlockedBikeCount; i++)
            {
                Transform root = blockedBikeRoots[i];
                if (root == null) continue;
                bool visible = (mask & (1 << i)) != 0;
                if (root.gameObject.activeSelf != visible)
                {
                    root.gameObject.SetActive(visible);
                    anyChanged = true;
                }
                if (blockedBikeHighlights[i] != null)
                    blockedBikeHighlights[i].gameObject.SetActive(visible && i == highlightedBikeIndex);
            }

            if (bikeStatusBanner != null)
            {
                string status = ShowWorldBikeStatusBanner ? FormatBikeBlockedStatus(session) : "";
                bool showBanner = status != "";
                if (bikeStatusBanner.gameObject.activeSelf != showBanner)
                    bikeStatusBanner.gameObject.SetActive(showBanner);
                if (showBanner)
                {
                    if (View != null) bikeStatusBanner.rotation = View.transform.rotation;
                    if (bikeStatusTextMesh != null && status != lastBikeStatusText)
                    {
                        lastBikeStatusText = status;
                        bikeStatusTextMesh.text = status;
                    }
                }
                else
                {
                    lastBikeStatusText = "";
                }
            }

            if (anyChanged) Physics.SyncTransforms();
        }

        private void ReleaseBlockedBicycles()
        {
            highlightedBikeIndex = -1;
            lastBikeStatusText = "";
            bikeStatusTextMesh = null;
            bikeStatusBanner = null;
            blockedBikesRoot = null;
            for (int i = 0; i < BlockedBikeCount; i++)
            {
                blockedBikeRoots[i] = null;
                blockedBikeHighlights[i] = null;
                blockedBikeTargets[i] = null;
            }
            bikeFrameAmber = bikeFrameBlue = bikeRubber = bikeAccent = bikeHighlightMat = bikeBannerBackMat = bikeBannerEdgeMat = null;
        }
    }
}

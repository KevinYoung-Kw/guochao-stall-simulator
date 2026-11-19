# Unity 运行模型

全部为 Unity 可原生导入的 FBX，米制；导出轴为 `-Z Forward / Y Up`。场景内具名节点已经经过 FBX 回读验证。运行时读取节点的实际 Transform，不根据本文 Blender 源坐标手工猜轴。

## 模型

- `HomeWorld` / `MarketWorld`：接受过的实际 3D 布局；无玩家、无可拿 B04、无自有摊车、无购物车中动态原料。市场保留原 NPC。
- `CartAI`：当前游戏使用的三轮车；车体来自 Aholo G1 任务 8256244 的 30,000 面优化模型，独立后筐为本地 Blender 模块，总计 31,640 可见三角面。包含 `CartOrigin`、`CartForward`、`CartDeck`、`StoveLoadedPlacement`、`CargoSlot0..2` 与七个碰撞代理。实际后筐内放三箱原料，B04 保持独立。
- `Cart`：旧占位车，仅保留历史资源，当前运行不使用。
- `ShoppingCart`：空购物车，地面篮筐 XY 中心原点；包含 `ShoppingSlot0..2`、`HandleGrip`、`ShoppingCartForward`。前进方向用 Forward 与 Root 的位置差取得。
- `Stove`：真实 Aholo/Lux3D B04，0.92×0.58×0.30 米，底部中心原点。
- `RiceBox` / `MeatBox` / `EggBox`：与 v7 图片相同的独立真实标准筐模型，底部中心原点。
- `Player`：原始未修手 Aholo 角色，52 骨，约 1.65 米。保留原 `Idle_Loop`、`Walk_Loop`、`Walk_Carry_Loop`、`Push_Loop`；另有四份 `Player@动作名.fbx` 单动作备用。包含 `PlayerOrigin` 和 `PlayerForward`，不需要改骨骼猜面朝向。

保留模型原比例与子节点变换。当前 CartAI 含后筐约长 3.672×宽 1.326 米；这是统一拟合的游戏尺度。CargoSlot0..2 源旋转为 90°，运行必须保留槽位的相对旋转。1.65 米是角色站立标称身高，走路或推车动作的屈膝包络会降低，不据此再次缩放角色。

## 节点与碰撞

`StovePickup`、`HomeCart`、`HomeExit`、`RiceShelf`、`MeatShelf`、`EggShelf`、`Cashier`、`MarketCart`、`MarketExit` 是角色可达交互站点。物品实际落点另有 `StovePlacement`、`HomeCartPlacement`、`MarketCartPlacement`、`RicePlacement`、`MeatPlacement`、`EggPlacement`。市场 `Stock_rice_01`、`Stock_meat_01`、`Stock_egg_01` 各是一箱本次样本，可独立显隐。

只给 `Collider_` 前缀代理加 BoxCollider，并关闭其 Renderer；不要给所有装饰网格、地板拼缝、货架商品和 NPC 自动加碰撞。CartAI 的车身、座椅基座、前叉/轮、脚踏、后双轮与后筐各有代理。场景门保持原来的打开状态。 HomeWorld 的新车停车锚点已向巷道外移 0.60 米，HomeCart/出发锚点同步修正；门到脚踏约 1.32 米。可用导出脚本的 `--home-only` 只更新出租屋，其他模型清单保留。

世界含 `Spawn`、`CameraPosition`、`CameraTarget`。16:9 时原画面对应 Unity orthographicSize：Home **5.315625**，Market **7.875**。CameraPosition/Target 之间的向量决定真实镜头方向。

## 材质贴图

| 材质 | sRGB 底色 | 非颜色打包图 |
| --- | --- | --- |
| Player_Material_00 | Textures/Player_Image_0.png | Player_Image_2.png |
| Stove_Material_00 | Textures/Stove_Image_0.png | Stove_Image_2.png |
| Designer_Logic_PBR（CartAI） | Textures/CartAI_texture_00.png | CartAI_texture_01.png |

`Image_2` 不是法线贴图：G 为粗糙度，B 为金属度；原材料金属系数 0.1、粗糙系数 1.0。Unity 的 Metallic/Smoothness 图通道不同，不能把原打包图直接当作 Normal 或标准 Metallic 图。第一版可用真实底色配统一粗糙度。无贴图的世界材质颜色完整记录在 `runtime-models.json`。

CartAI 已由 `GuochaoBuild` 将 G/B 数据转换为 Standard 所需的 R=Metallic×0.1、A=1−Roughness，写入 `CartAI_MetallicSmoothness.png` 并以非 sRGB 导入。后筐的独立材质不被车体贴图覆盖。`cart-ai-runtime.json` 记录真实装载尺寸与来源，原始高模未放进游戏运行资源。

`fbx-roundtrip-checks.json` 记录 13 个文件、定位点、骨骼和动作回读；`route-proxy-checks.json` 记录九个交互目标的代理碰撞可达性，均有至少 0.5 米净距。这些验证不替代 Unity 内的实际运行检查。

# 资产来源与许可范围

本仓库的 Apache License 2.0 适用于项目原创代码。以下资产和依赖单独列明来源，不因随代码公开而统一改授 Apache License 2.0。

## Aholo 三维生成资产

角色、部分设备和物件通过 Aholo 生成服务制作，再由项目完成运行格式转换、减面、材质导入、绑定和场景装配。生成服务是制作环节，游戏运行时不需要调用其 API。

| 运行资源 | 内容 | 原生成任务 |
|---|---|---:|
| `Stove.fbx` | 猛火炉 | 8239826 |
| `Player.fbx` | 主角 | 8242107 |
| `CartAI.fbx` | 摆摊三轮车 | 8256244 |
| `CustomerJade.fbx` | 社区女顾客 | 8263431 |
| `HomeBedAI.fbx` | 木床 | 8264364 |
| `MarketProduceAI.fbx` | 果蔬花车 | 8264397 |
| `CustomerAuntie.fbx` | 卷发阿姨 | 8268253 |
| `CustomerCourier.fbx` | 外卖骑手 | 8268233 |
| `CustomerWorker.fbx` | 工人 | 8269270 |
| `PreparedFrozenRiceAI.fbx` | 速冻饭包装 | 8293462 |
| `PreparedMealPouchAI.fbx` | 料理包包装 | 8293481 |
| `CustomerStudentV15.fbx` | 学生 | 8351834 |
| `StreetShopfrontAI.fbx` | 沿街店面 | 8351835 |
| `MarketCashierV15.fbx` | 收银员 | 8355395 |
| `MarketRecoveryVendorV17.fbx` | 回收站摊贩 | 8383354 |
| `MarketStallEarlV17.fbx` | 摊贩伯爵 | 8383350 |

这些模型及其贴图保留自身的权利和适用服务条款，本文件记录来源，不将它们改授代码许可证。多个导出版本、角色动作和贴图副本不重复计为新的生成资产。

## 项目自制场景与家居

出租屋、市场、街道等场景装配包含项目自制的 Blender 或程序建模内容。搪瓷盆、榆木凳、铝水壶、布拖鞋来自项目原生 Blender 家居制作，不是 Aholo 生成模型。盆和凳在本版接入；壶和拖鞋原已包含在场景中。

生成几何、交互、装配和音频合成的原创代码适用仓库代码许可。独立导出的模型、美术图和贴图单独保留其资产权利，不统一改授 Apache License 2.0。

## 音效

`Assets/Scripts/Gameplay/GameFeelAudio.cs` 实时合成脚步、车轮滚动、纸箱和金属放置音效。该实现是项目原创代码，没有引入外部音效录音。

`Assets/Scripts/Gameplay/StreetAudio.cs` 管理分场景音乐、环境声和操作提示音。`Assets/Resources/StreetAudio/` 内含项目制作的 28 个 WAV：7 首场景配乐、3 段环境声和 18 个操作音效。旋律与音色按本项目的乐谱和提示音设计，通过程序合成并导出；没有使用外部录音或商业曲目采样。音频播放与合成的原创代码适用 Apache License 2.0，独立 WAV 文件保留项目美术/音频资产权利，不统一改授代码许可证。

## 字体

| 字体 | 上游 | 仓库内许可 |
|---|---|---|
| Noto Sans CJK SC Regular | [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk) | `Assets/Resources/UI/Fonts/OFL.txt` |
| Ma Shan Zheng Regular | [googlefonts/mashanzheng](https://github.com/googlefonts/mashanzheng) | `Assets/Resources/UI/Fonts/MaShanZheng-OFL.txt` |

两款字体依照 **SIL Open Font License 1.1** 分发，保留其版权及许可文本。字体不改授 Apache License 2.0。

## 引擎与包

Unity 引擎及 `Packages/manifest.json` 中的 Unity 依赖遵循各自许可，由 Unity Package Manager 安装。仓库的代码许可不替代 Unity 或第三方包的许可。

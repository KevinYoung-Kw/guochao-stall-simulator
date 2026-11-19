using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    /// <summary>
    /// 2D 彩绘图标库：从 Resources/UI/CreationV16/creation-icons-atlas.png
    /// (RGBA 1254x1254, 4x4 等分, top-first) 切片缓存职业 / 特性 / 装备图标。
    /// 仅依赖 UnityEngine 与 Collections，不依赖未来 UI6 布局类。
    /// 每个宿主只需创建 1 个组件；OnDestroy 只销毁自己创建的 Sprite，
    /// 绝不销毁共享的 Resources Texture。
    /// </summary>
    public class CreationMenuIcons : MonoBehaviour
    {
        private const string AtlasResourcePath = "UI/CreationV16/creation-icons-atlas";
        private const int Cols = 4;
        private const int Rows = 4;
        private const int InsetPx = 7; // 四周 padding + 少量色噪点，内缩避免邻格渗入

        private Texture2D _atlas;
        private Sprite[] _cells;            // [row * 4 + col]，row0 在图顶部
        private bool _loadAttempted;
        private bool _warnedMissing;        // 只 Warning 一次，不泛洪

        /// <summary>职业 career1..career8 → 图集 0..7。</summary>
        public Sprite Career(string careerId)
        {
            if (!TryGetCellIndex(careerId, "career", 1, 8, out int idx)) return null;
            return GetCell(idx);
        }

        /// <summary>
        /// 特性按语义映射：书(速学者/迟钝学者/速读/慢读/文盲)、包(收纳达人/丢三落四)、
        /// 骰子(幸运儿/倒霉蛋)、眼(眼尖/高度近视)、耳(顺风耳/耳背)、
        /// 手艺(心灵手巧/笨手笨脚/毛手毛脚)、奶茶控(index5)。
        /// 未收录的 trait 返回 null，由调用方现有 vector 作 fallback。
        /// </summary>
        public Sprite Trait(string traitId)
        {
            if (string.IsNullOrEmpty(traitId)) return null;
            int idx = traitId switch
            {
                // row0: 0=口哨 1=小吃车 2=笔记本 3=厨师帽 —— 不用于 trait
                // row1: 4=外卖盔饭 5=奶茶 6=勺盖饭 7=工厂
                "速学者" or "迟钝学者" or "速读" or "慢读" or "文盲" => 8,  // row2 col0 books
                "收纳达人" or "丢三落四" => 9,                               // row2 col1 leatherbag
                "幸运儿" or "倒霉蛋" => 10,                                 // row2 col2 dice
                "眼尖" or "高度近视" => 11,                                 // row2 col3 eye
                "顺风耳" or "耳背" => 12,                                   // row3 col0 ear
                "心灵手巧" or "笨手笨脚" or "毛手笨脚" => 13,               // row3 col1 craftinghandtool
                "奶茶控" => 5,                                              // row1 col1 bubbletea
                _ => -1,
            };
            return idx >= 0 ? GetCell(idx) : null;
        }

        /// <summary>装备：U14→剪刀(index14)，U15→抹布(index15)，其他返回 null。</summary>
        public Sprite Gear(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            int idx = itemId switch
            {
                "U14" => 14, // row3 col2 redscissors
                "U15" => 15, // row3 col3 foldedrag
                _ => -1,
            };
            return idx >= 0 ? GetCell(idx) : null;
        }

        void OnDestroy()
        {
            // 只销毁自己创建的 Sprite；共享 Texture 由 Resources 管理，不能动。
            if (_cells != null)
            {
                foreach (var s in _cells)
                    if (s != null) Destroy(s);
                _cells = null;
            }
            _atlas = null;
        }

        // ---- 内部：一次性加载 + 切片缓存 ----

        private bool TryGetCellIndex(string id, string prefix, int min, int max, out int idx)
        {
            idx = -1;
            if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            if (!int.TryParse(id.Substring(prefix.Length), out int n) || n < min || n > max)
                return false;
            idx = n - 1; // career1..8 → 0..7
            return true;
        }

        private Sprite GetCell(int index)
        {
            if (!EnsureAtlas()) return null;
            if (_cells[index] == null)
            {
                // top-first：图集第一行在纹理顶部，Unity 原点在左下，y 需翻转。
                // 用实际纹理宽度计算，不硬编码 1254。
                float w = _atlas.width / (float)Cols;
                float h = _atlas.height / (float)Rows;
                int row = index / Cols;
                int col = index % Cols;

                float x = col * w + InsetPx;
                float y = _atlas.height - (row + 1) * h + InsetPx; // Unity 底部原点，上下各留 7px
                float rw = w - InsetPx * 2f;
                float rh = h - InsetPx * 2f;

                var rect = new Rect(x, y, rw, rh);
                var pivot = new Vector2(0.5f, 0.5f);
                _cells[index] = Sprite.Create(_atlas, rect, pivot, 100f);
            }
            return _cells[index];
        }

        private bool EnsureAtlas()
        {
            if (_atlas != null) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;

            _atlas = Resources.Load<Texture2D>(AtlasResourcePath);
            if (_atlas == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning($"[CreationMenuIcons] 图集缺失: Resources/{AtlasResourcePath}.png，2D 图标不可用（返回 null，回退 vector）。");
                }
                return false;
            }
            _cells = new Sprite[Cols * Rows];
            return true;
        }
    }
}

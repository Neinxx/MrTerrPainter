using System.Collections.Generic;

namespace MrTerrainPainter.Editor.State
{
    public class EditorState
    {
        // 预制体缩略图选择集合（当前 Profile 范围内）
        public HashSet<int> SelectedThumbIndices { get; } = new HashSet<int>();

        // 当前选中的 Profile 条目索引
        public int SelectedItemIndex { get; set; } = -1;
    }
}
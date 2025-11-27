using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// 空间网格，用于笔刷绘制时的快速邻近查询
    /// 使用网格单元优化距离检查，避免O(N²)的暴力搜索
    /// </summary>
    public class BrushSpatialGrid
    {
        private float cellSize;
        private readonly Dictionary<(int, int), List<Vector2>> cells = new Dictionary<(int, int), List<Vector2>>();

        public BrushSpatialGrid(float spacing) => Reset(spacing);

        /// <summary>
        /// 重置网格，使用新的间距
        /// </summary>
        public void Reset(float spacing)
        {
            cellSize = Mathf.Max(spacing, 0.01f);
            cells.Clear();
        }

        /// <summary>
        /// 清空所有网格数据
        /// </summary>
        public void Clear() => cells.Clear();

        /// <summary>
        /// 将点添加到网格
        /// </summary>
        public void Add(Vector2 p)
        {
            var k = Key(p);
            if (!cells.TryGetValue(k, out var list))
            {
                list = new List<Vector2>();
                cells[k] = list;
            }
            list.Add(p);
        }

        /// <summary>
        /// 检查指定点附近是否有其他点（在minDist范围内）
        /// </summary>
        /// <param name="p">待检测点</param>
        /// <param name="minDist">最小距离阈值（必须≥0）</param>
        /// <returns>true：存在邻近点；false：无邻近点</returns>
        /// <exception cref="ArgumentOutOfRangeException">当minDist为负数时抛出</exception>
        public bool HasNearby(Vector2 p, float minDist)
        {
            // 输入合法性校验（提前返回/抛异常，减少嵌套）
            if (minDist < 0f)
                throw new ArgumentOutOfRangeException(nameof(minDist), "最小距离不能为负数");
            if (minDist <= float.Epsilon)
                return cells.Values.Any(list => list.Contains(p));

            var minSqrDist = minDist * minDist;
            var (cellX, cellY) = Key(p);

            // 遍历3x3网格：通过提取子方法，消除内层嵌套
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (CheckCellHasNearby(cellX + dx, cellY + dy, p, minDist, minSqrDist))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查单个网格单元内是否存在邻近点（提取子方法，扁平化缩进）
        /// </summary>
        private bool CheckCellHasNearby(int targetCellX, int targetCellY, Vector2 p, float minDist, float minSqrDist)
        {
            // 单元格不存在，直接返回false（提前返回，减少嵌套）
            if (!cells.TryGetValue((targetCellX, targetCellY), out var points))
                return false;

            // 遍历单元格内的点：无额外嵌套，逻辑清晰
            foreach (var point in points)
            {
                // 坐标粗判（提前continue，减少深层逻辑）
                if (Math.Abs(point.x - p.x) > minDist)
                    continue;
                if (Math.Abs(point.y - p.y) > minDist)
                    continue;

                // 细判平方距离
                if ((point - p).sqrMagnitude < minSqrDist)
                    return true;
            }

            return false;
        }

        private (int, int) Key(Vector2 p) => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
    }
}

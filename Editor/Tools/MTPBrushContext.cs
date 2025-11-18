using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Runtime.Profiles;
using System.Collections.Generic;
using UnityEngine;
using MrTerrainPainter.Editor.Config;

namespace MrTerrainPainter.Editor.Tools
{
    public static class MTPBrushContext
    {
        private static BrushSettings _brush;
        private static VegetationProfile _currentProfile;
        private static readonly List<VegetationProfile> _recent = new();
        private static readonly List<VegetationProfile> _extras = new();
        private const int MaxRecent = 8;
        public static event System.Action<VegetationProfile> ProfileChanged;
        public static event System.Action ExtrasChanged;
        public static event System.Action BrushReplaced;
        public static MrTerrainPainterConfig Config { get; private set; }
        private static readonly List<Terrain> _selectedTerrains = new();
        public static BrushSettings Brush
        {
            get
            {
                _brush ??= new BrushSettings();
                return _brush;
            }
        }

        public static void SetSharedBrush(BrushSettings bs)
        {
            if (bs == null) return;
            _brush = bs;
            BrushReplaced?.Invoke();
        }

        public static void SetConfig(MrTerrainPainterConfig cfg)
        {
            Config = cfg;
        }

        public static void SetSelectedTerrains(IReadOnlyList<Terrain> terrains)
        {
            _selectedTerrains.Clear();
            if (terrains == null) return;
            for (int i = 0; i < terrains.Count; i++)
            {
                var t = terrains[i];
                if (t != null) _selectedTerrains.Add(t);
            }
        }

        public static IReadOnlyList<Terrain> SelectedTerrains => _selectedTerrains;

        public static VegetationProfile CurrentProfile
        {
            get => _currentProfile;
            set
            {
                _currentProfile = value;
                if (value == null) return;
                for (int i = _recent.Count - 1; i >= 0; i--) if (_recent[i] == null) _recent.RemoveAt(i);
                _recent.Remove(value);
                _recent.Insert(0, value);
                if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
                ProfileChanged?.Invoke(_currentProfile);
            }
        }

        public static IReadOnlyList<VegetationProfile> RecentProfiles => _recent;

        public static IReadOnlyList<VegetationProfile> ExtraProfiles => _extras;

        public static void AddExtra(VegetationProfile p)
        {
            if (p == null) return;
            if (_extras.Contains(p)) return;
            _extras.Add(p);
            ExtrasChanged?.Invoke();
        }

        public static void RemoveExtra(VegetationProfile p)
        {
            if (p == null)
            {
                PruneExtrasNulls();
                return;
            }
            if (_extras.Remove(p)) ExtrasChanged?.Invoke();
        }

        public static void ClearExtras()
        {
            if (_extras.Count == 0) return;
            _extras.Clear();
            ExtrasChanged?.Invoke();
        }

        public static void PruneExtrasNulls()
        {
            bool changed = false;
            for (int i = _extras.Count - 1; i >= 0; i--)
            {
                if (_extras[i] == null)
                {
                    _extras.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) ExtrasChanged?.Invoke();
        }
    }
}

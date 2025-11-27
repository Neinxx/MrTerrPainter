using System.Collections.Generic;
using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;

namespace MrTerrainPainter.Editor.Controllers
{
    public class PaintingController
    {
        public void PaintOnTerrain(
            Terrain terrain,
            Vector3 center,
            VegetationProfile currentProfile,
            List<VegetationProfile> extraProfiles,
            BrushSettings brush,
            System.Random rnd,
            VegetationGenerator.PlacementOverrides ov,
            bool mixExtraProfiles)
        {
            if (terrain == null || currentProfile == null || currentProfile.IsEmpty()) return;

            if (mixExtraProfiles)
            {
                var list = new List<VegetationProfile>();
                for (int i = 0; i < extraProfiles.Count; i++)
                {
                    var p = extraProfiles[i];
                    if (p == null || p.IsEmpty()) continue;
                    list.Add(p);
                }
                if (list.Count >= 2)
                {
                    list.Insert(0, currentProfile);
                    var paintContext = new PaintContext
                    {
                        Terrain = terrain,
                        Profiles = list,
                        Center = center,
                        BrushSettings = brush,
                        Random = rnd,
                        Overrides = ov
                    };
                    BrushPainter.PaintMixed(paintContext);
                    return;
                }
            }

            var paintContext1 = new PaintContext
            {
                Terrain = terrain,
                Profile = currentProfile,
                Center = center,
                BrushSettings = brush,
                Random = rnd,
                Overrides = ov
            };
            BrushPainter.Paint(paintContext1);

            for (int i = 0; i < extraProfiles.Count; i++)
            {
                var p = extraProfiles[i];
                if (p == null || p.IsEmpty()) continue;

                var paintContext2 = new PaintContext
                {
                    Terrain = terrain,
                    Profile = p,
                    Center = center,
                    BrushSettings = brush,
                    Random = rnd,
                    Overrides = ov
                };
                BrushPainter.Paint(paintContext2);
            }
        }
    }
}

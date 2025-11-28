using MrTerrainPainter.Editor.Config;
using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadePathTracer
    {
        public static List<FacadeDetectionService.CliffSlice> Trace(FacadeTraceRequest req)
        {
            var cfg = req.Config ?? ConfigTools.GetCachedConfig();
            var builder = new FacadeDetectionService.FacadeTraceBuilder()
                .Terrain(req.Terrain)
                .Start(req.Start)
                .Length(req.Length)
                .FromItem(req.ItemRef)
                .FromConfig(cfg);
            return FacadeDetectionService.TraceVirtualFacade(builder.Build());
        }
    }
}

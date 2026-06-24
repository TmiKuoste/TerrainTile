using Kuoste.LasZipNetStandard;
using LasUtility.LAS;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using KLasPoint = Kuoste.LasZipNetStandard.LasPoint;
using LasPoint = LasUtility.LAS.LasPoint;

namespace Kuoste.TerrainEngine.TileBuilders.DemDsm
{
    /// <summary>
    /// Writes a source tile's ground edge-frame as a small <c>.laz</c> halo sidecar. The header
    /// (point format, scale, offset) is copied from the source so coordinates round-trip exactly;
    /// neighbouring source tiles read the sidecar back with the ordinary <see cref="LasZipNetReader"/>.
    /// </summary>
    internal static class HaloBandWriter
    {
        public static void Write(string sTemplateLazPath, string sOutPath, IReadOnlyList<LasPoint> groundPoints, Envelope frameExtent)
        {
            // Keep the reader open while writing: the copied header still references the source's
            // native VLR memory until the writer is closed (see LasZip.SetWriterHeader remarks).
            using LasZip lz = new(out _);
            lz.OpenReader(sTemplateLazPath);
            LaszipHeaderStruct h = lz.GetReaderHeader();

            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (LasPoint q in groundPoints)
            {
                if (q.z < minZ) minZ = q.z;
                if (q.z > maxZ) maxZ = q.z;
            }
            if (groundPoints.Count == 0)
            {
                minZ = maxZ = 0;
            }

            h.NumberOfPointRecords = (uint)groundPoints.Count;
            h.ExtendedNumberOfPointRecords = (ulong)groundPoints.Count;
            h.NumberOfPointsByReturn = new uint[5];
            h.ExtendedNumberOfPointsByReturn = new ulong[15];
            h.MinX = frameExtent.MinX;
            h.MaxX = frameExtent.MaxX;
            h.MinY = frameExtent.MinY;
            h.MaxY = frameExtent.MaxY;
            h.MinZ = minZ;
            h.MaxZ = maxZ;

            lz.SetWriterHeader(h);
            lz.OpenWriter(sOutPath, true);

            KLasPoint kp = new();
            foreach (LasPoint q in groundPoints)
            {
                kp.X = q.x;
                kp.Y = q.y;
                kp.Z = q.z;
                kp.Classification = q.classification;
                lz.WritePoint(ref kp);
            }

            lz.CloseWriter();
        }

        /// <summary>
        /// Reads a source <c>.laz</c> in full, collects its ground edge-frame (points within
        /// <paramref name="overlap"/> metres of any edge of <paramref name="extent"/>) and writes
        /// the halo sidecar. Used by <see cref="Common.Tiles.SeamMode.TwoPass"/> to materialise a
        /// neighbour's halo on demand.
        /// </summary>
        public static void ExtractFrameToBand(string sLazPath, Envelope extent, int overlap, string sOutBandPath)
        {
            int minX = (int)Math.Round(extent.MinX);
            int minY = (int)Math.Round(extent.MinY);
            int edgeX = (int)Math.Round(extent.Width);
            int edgeY = (int)Math.Round(extent.Height);

            ILasFileReader reader = new LasZipNetReader();
            reader.ReadHeader(sLazPath);
            reader.OpenReader(sLazPath);

            List<LasPoint> frame = new();
            foreach (LasPoint p in reader.Points())
            {
                if (p.classification != (byte)PointCloud05p.Classes.Ground)
                    continue;

                if (p.x < minX + overlap || p.x >= minX + edgeX - overlap ||
                    p.y < minY + overlap || p.y >= minY + edgeY - overlap)
                    frame.Add(p);
            }

            reader.CloseReader();

            Write(sLazPath, sOutBandPath, frame, extent);
        }
    }
}

using Kuoste.TerrainEngine.Common.Tiles;
using Kuoste.TerrainEngine.TileBuilders.DemDsm;
using Kuoste.TerrainEngine.TileBuilders.Tests.Helpers;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace Kuoste.TerrainEngine.TileBuilders.Tests.DemDsm;

public class SeamHaloTests : IDisposable
{
    // Four committed 1 km source fixtures forming a 2x2 km block (split from L4133B3):
    //   B3_2 (NW) | B3_5 (NE)        share vertical edge x = 384000
    //   B3_1 (SW) | B3_4 (SE)        share horizontal edge y = 6673000
    private static readonly string[] Tiles = { "L4133B3_1", "L4133B3_4", "L4133B3_2", "L4133B3_5" };
    private const string Version = "1";

    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempDirs = new();

    public SeamHaloTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (string d in _tempDirs)
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void TwoPassHalo_FillsOverlapAcrossSourceBoundary()
    {
        string? dataPath = FindSeamDataPath();
        if (dataPath == null) return;

        (double cov, double agree) none = OverlapStitch(BuildAll(dataPath, SeamMode.None));
        (double cov, double agree) two = OverlapStitch(BuildAll(dataPath, SeamMode.TwoPass));

        _output.WriteLine($"Overlap-band both-covered fraction: None={none.cov:F3}, TwoPass={two.cov:F3}");
        _output.WriteLine($"Overlap-band height agreement (m):  None={none.agree:F3}, TwoPass={two.agree:F3}");

        // Without halo, a tile's triangulation stops at its own data, so most of the 84 m band
        // straddling a source boundary is covered by only one side. The halo feeds the neighbour's
        // points across the seam, so both sides reconstruct the same surface over the whole band.
        Assert.True(none.cov < 0.5, $"Without halo the overlap band should be mostly one-sided, got {none.cov:F3}");
        Assert.True(two.cov > 0.9, $"TwoPass halo should cover the overlap band on both sides, got {two.cov:F3}");
        Assert.True(two.agree < 0.5, $"Where both sides cover, they should agree closely, got {two.agree:F3} m");
    }

    private Dictionary<string, VoxelGrid> BuildAll(string dataPath, SeamMode mode)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "SeamTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        var grids = new Dictionary<string, VoxelGrid>();
        var creator = new DemDsmCreator { CancellationToken = CancellationToken.None, Logger = NullLogger.Instance };

        foreach (string name in Tiles)
        {
            var tile = new Tile
            {
                Name = name,
                Common = new TileCommon(256, tempDir, dataPath, Version,
                    tileScheme: null, outputEdgeLength: 1000, blockEdgeLength: 1000, sourceEdgeLength: 1000, seamMode: mode)
            };
            grids[name] = creator.Build(tile);
        }

        return grids;
    }

    // Across each shared edge, sample an 84 m band straddling it (42 m into each tile). Returns the
    // fraction of sample points covered (non-NaN) by BOTH tiles, and the mean height agreement there.
    private static (double cov, double agree) OverlapStitch(Dictionary<string, VoxelGrid> g)
    {
        int both = 0, total = 0;
        double sum = 0;

        void Band(VoxelGrid a, VoxelGrid b, bool vertical, double edge, double from, double to)
        {
            for (double s = from + 100; s < to - 100; s += 25)
            {
                for (double d = -42; d <= 42; d += 8)
                {
                    double x = vertical ? edge + d : s;
                    double y = vertical ? s : edge + d;
                    double ha = Sample(a, x, y);
                    double hb = Sample(b, x, y);
                    total++;
                    if (!double.IsNaN(ha) && !double.IsNaN(hb)) { both++; sum += Math.Abs(ha - hb); }
                }
            }
        }

        Band(g["L4133B3_1"], g["L4133B3_4"], vertical: true,  edge: 384000,  from: 6672000, to: 6673000);
        Band(g["L4133B3_2"], g["L4133B3_5"], vertical: true,  edge: 384000,  from: 6673000, to: 6674000);
        Band(g["L4133B3_1"], g["L4133B3_2"], vertical: false, edge: 6673000, from: 383000,  to: 384000);
        Band(g["L4133B3_4"], g["L4133B3_5"], vertical: false, edge: 6673000, from: 384000,  to: 385000);

        double cov = total > 0 ? (double)both / total : 0;
        double agree = both > 0 ? sum / both : double.NaN;
        return (cov, agree);
    }

    private static double Sample(VoxelGrid g, double x, double y)
    {
        RcIndex rc = g.Bounds.ProjToCell(new Coordinate(x, y));
        if (rc.Row < 0 || rc.Column < 0 || rc.Row >= g.Bounds.RowCount || rc.Column >= g.Bounds.ColumnCount)
            return double.NaN;
        return g.GetValue(rc.Row, rc.Column);
    }

    private static string? FindSeamDataPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "TestData", "Seam");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

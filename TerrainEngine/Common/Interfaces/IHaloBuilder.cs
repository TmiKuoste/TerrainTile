namespace Kuoste.TerrainEngine.Common.Interfaces
{
    /// <summary>
    /// Cross-source seam halo: a source tile's 42 m ground edge-frame, persisted as a small
    /// <c>.laz</c> sidecar in the intermediate cache and consumed by neighbouring source tiles.
    /// </summary>
    public interface IHaloBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_Halo_v" + sVersion + ".laz";
    }
}

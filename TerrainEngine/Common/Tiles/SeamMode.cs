namespace Kuoste.TerrainEngine.Common.Tiles
{
    /// <summary>
    /// How cross-source seams are stitched. Each source writes its 42 m ground edge-frame as a
    /// <c>.laz</c> halo sidecar; neighbours consume those frames so adjacent source tiles share
    /// boundary points and their reconstructed surfaces agree.
    /// </summary>
    public enum SeamMode
    {
        /// <summary>No halo IO. Each source triangulates only its own points (cross-source seams remain).</summary>
        None,

        /// <summary>
        /// Write-behind only: a source writes its own halo and consumes neighbour halos that already
        /// exist. Single read per source; seams fill progressively as neighbours get built.
        /// </summary>
        SinglePass,

        /// <summary>
        /// Like <see cref="SinglePass"/>, but a missing neighbour halo is extracted on demand (the
        /// neighbour is read once and its halo cached) so every interior source gets the full set of
        /// neighbour frames regardless of build order. ~2x reads over an area; seams fully closed.
        /// </summary>
        TwoPass
    }
}

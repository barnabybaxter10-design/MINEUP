using System;

namespace BoxCutter
{
    /// <summary>
    /// Represents a 3D bounding box sub-region within a larger voxel space.
    /// Used for spatial partitioning and voxel-based destruction calculations.
    /// </summary>
    [Serializable]
    public struct SubBox
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Bounding Box Coordinates
        //─────────────────────────────────────────────────────────────────────────────────────
        public int minX;
        public int minY;
        public int minZ;
        public int maxX;
        public int maxY;
        public int maxZ;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                   Magica Data
        //─────────────────────────────────────────────────────────────────────────────────────
        public byte magicaIndex;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                    Mesh Data
        //─────────────────────────────────────────────────────────────────────────────────────
        public int quadStart;
        public int quadEnd;
        //─────────────────────────────────────────────────────────────────────────────────────
    }
}
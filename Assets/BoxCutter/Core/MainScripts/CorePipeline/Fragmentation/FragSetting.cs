using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Fragmentation Enums
    //─────────────────────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// Defines the type of fragmentation pattern to apply during destruction.
    /// Each type produces different visual and mechanical characteristics.
    /// </summary>
    public enum FragmentType
    {
        Standard,    // Basic cubic fragmentation
        Slab,        // Rectangular slab-like pieces
        Splinter,    // Long thin fragments
        Radial,      // Radial crack patterns from impact point
    }

    /// <summary>
    /// Defines the geometric shape of radial crack rings.
    /// Affects the visual appearance of radial fragmentation patterns.
    /// </summary>
    public enum RadialRingGenType
    {
        Perfect,     // Smooth circular rings
        Square,      // Square-shaped rings
        Sawtooth     // Zigzag-edged rings
    }

    /// <summary>
    /// Specifies the primary axis for radial crack propagation.
    /// Determines the orientation of the radial fragmentation plane.
    /// </summary>
    public enum RadialPlaneMode
    {
        Auto,        // Automatically chooses longest axis
        X,           // Radial cracks perpendicular to X axis
        Y,           // Radial cracks perpendicular to Y axis
        Z,           // Radial cracks perpendicular to Z axis
        Custom       // User-defined plane orientation
    }

    /// <summary>
    /// Specifies the primary axis for splinter fragmentation alignment.
    /// Determines which axis should be used as the longest dimension for splinters.
    /// </summary>
    public enum SplinterAxisMode
    {
        Auto,        // Automatically chooses longest axis
        X,           // Force X axis as the longest dimension
        Y,           // Force Y axis as the longest dimension
        Z            // Force Z axis as the longest dimension
    }

    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Fragmentation Settings
    //─────────────────────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// ScriptableObject containing all fragmentation parameters for BoxCutter destruction.
    /// Provides comprehensive control over fragment generation patterns and characteristics.
    /// </summary>
    [CreateAssetMenu(fileName = "FragSettings", menuName = "BoxCutter/Frag Setting")]
    public class FragSettings : ScriptableObject
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Core Fragment Settings
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Primary fragmentation algorithm to use for destruction events.
        /// Determines the overall pattern and characteristics of generated fragments.
        /// </summary>
        public FragmentType fragmentType = FragmentType.Slab;

        /// <summary>
        /// Multiplier for the outer radius scale in fragment calculations.
        /// Affects the overall size distribution of generated fragments.
        /// </summary>
        public float outRadiusScaleMul = 1;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Slab Fragment Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Enables randomized slab dimensions instead of using fixed values.
        /// When true, uses slabRange parameters; when false, uses fixed parameters.
        /// </summary>
        public bool canRandomSlabAxis;
        
        /// <summary>
        /// Fixed dimensions for slab fragments when canRandomSlabAxis is false.
        /// Provides precise control over fragment proportions.
        /// </summary>
        public int fixedSlabPrimaryAxis = 2;
        public int fixedSlabSecondaryAxis = 2;
        public int fixedSlabTertiaryAxis = 2;

        /// <summary>
        /// Random range for slab fragment dimensions when canRandomSlabAxis is true.
        /// Creates varied fragment sizes within specified bounds.
        /// </summary>
        public int2 slabRangePrimary = new int2(2, 3);
        public int2 slabRangeSecondary = new int2(1, 3);
        public int2 slabRangeTertiary = new int2(4, 6);
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Splinter Fragment Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Enables randomized splinter dimensions instead of using fixed values.
        /// When true, uses splinterRange parameters; when false, uses fixed parameters.
        /// </summary>
        public bool canRandomSplinterAxis;
        
        /// <summary>
        /// Fixed dimensions for splinter fragments when canRandomSplinterAxis is false.
        /// Provides precise control over splinter proportions.
        /// </summary>
        public int fixedSplinterPrimaryAxis = 6;
        public int fixedSplinterSecondaryAxis = 1;
        public int fixedSplinterTertiaryAxis = 1;

        /// <summary>
        /// Random range for splinter fragment dimensions when canRandomSplinterAxis is true.
        /// Creates varied splinter sizes within specified bounds.
        /// </summary>
        public int2 splinterRangePrimary = new int2(4, 8);
        public int2 splinterRangeSecondary = new int2(1, 2);
        public int2 splinterRangeTertiary = new int2(1, 2);
        
        /// <summary>
        /// Specifies the primary axis for splinter fragmentation alignment.
        /// Auto mode uses the shortest dimension, while X/Y/Z forces that specific axis.
        /// </summary>
        public SplinterAxisMode splinterAxisMode = SplinterAxisMode.Auto;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Clustering & Density Settings
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Enables fragment clustering around the impact point for slab fragments.
        /// Creates more realistic destruction patterns with higher fragment density near impact.
        /// Note: Clustering is automatically disabled for splinter fragments.
        /// </summary>
        public bool canCluster;
        
        /// <summary>
        /// Maximum radius for fragment clustering effects.
        /// Larger values create more widespread clustering patterns.
        /// </summary>
        public int maxClusterRadius = 5;
        
        /// <summary>
        /// Uses circular mask for cluster boundary instead of square.
        /// Provides more natural, organic-looking cluster shapes.
        /// </summary>
        public bool useDiscMask = true;
        
        /// <summary>
        /// Applies radial weighting to fragment distribution.
        /// Creates denser fragmentation near the impact center.
        /// </summary>
        public bool radialWeight = true;
        
        /// <summary>
        /// Exponential falloff for fragment density from impact point.
        /// Higher values create sharper density gradients.
        /// </summary>
        [Range(1, 4)] public float densityFallExp = 1.0f;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Radial Crack Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Number of radial crack lines emanating from the impact point.
        /// More spokes create finer, more detailed crack patterns.
        /// </summary>
        public int radialSpokes = 6;
        
        /// <summary>
        /// Multiplier for dynamic crack length calculation based on impact radius/bounds.
        /// Controls how far cracks propagate relative to the impact area size.
        /// </summary>
        public float crackLengthMultiplier = 1f;
        
        /// <summary>
        /// Length constraints for crack segments to ensure realistic variation.
        /// Prevents overly uniform or chaotic crack patterns.
        /// </summary>
        public float minSegmentLength = 0.2f;
        public float maxSegmentLength = 0.5f;
        
        /// <summary>
        /// Maximum angle deviation for zigzag crack patterns.
        /// Creates more natural, irregular crack propagation.
        /// </summary>
        public float maxZigZagAngleDeg = 30f;
        
        /// <summary>
        /// Width of crack lines as a multiplier of voxel size.
        /// Affects the thickness of fracture boundaries.
        /// </summary>
        public int crackThickness = 1;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Circular Ring Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Density factor for procedural ring generation based on impact area.
        /// Higher values create more rings within the same impact radius.
        /// Controls how many concentric rings are generated relative to the impact size.
        /// </summary>
        public float ringDensity = 0.5f;
        
        /// <summary>
        /// Multiplier for ring coverage area relative to crack length.
        /// 1.0 = rings extend to crack length, 0.5 = rings cover half the crack area.
        /// Controls how far out rings extend compared to the radial spokes.
        /// </summary>
        [Range(0.1f, 2.0f)] public float ringCoverage = 0.8f;
        
        /// <summary>
        /// Exponential growth factor for ring spacing distribution.
        /// 1.0 = uniform spacing, >1.0 = rings get farther apart toward edges.
        /// Controls how ring density changes from center to edge.
        /// </summary>
        [Range(0.5f, 3.0f)] public float ringGrowthExp = 1.2f;
        
        /// <summary>
        /// Controls ring density falloff toward the edge of the impact area.
        /// 0 = uniform density, 1 = rings concentrate toward center.
        /// Creates more natural, crater-like ring patterns.
        /// </summary>
        [Range(0f, 1f)] public float ringFalloff = 0.3f;
        
        /// <summary>
        /// Thickness of each ring boundary as a multiplier of voxel size.
        /// Affects the width of circular crack patterns.
        /// </summary>
        public int ringThickness = 1;
        
        /// <summary>
        /// Geometric shape style for ring generation.
        /// Determines the visual character of circular crack patterns.
        /// </summary>
        public RadialRingGenType radialRingGenType;
        
        /// <summary>
        /// Noise parameters for organic ring variation.
        /// Prevents perfectly uniform rings for more realistic results.
        /// </summary>
        public float ringNoiseAmplitude = 0.025f;
        public float ringNoiseFrequency = 0.5f;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Radial Plane Orientation
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Primary axis for radial crack plane orientation.
        /// Determines the direction normal to the fragmentation plane.
        /// </summary>
        public RadialPlaneMode planeMode = RadialPlaneMode.Auto;

        /// <summary>
        /// Custom normal vector for radial plane when planeMode is set to Custom.
        /// Allows arbitrary orientation of the fragmentation plane.
        /// </summary>
        [Tooltip("Used only if planeMode = Custom")]
        public Vector3 radialPlaneNormal = Vector3.up;

        /// <summary>
        /// Custom forward vector for radial plane when planeMode is set to Custom.
        /// Defines the reference direction for radial crack propagation.
        /// </summary>
        [Tooltip("Used only if planeMode = Custom")]
        public Vector3 radialPlaneForward = Vector3.forward;
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using VoxReader.Interfaces;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterObjectPool;
using MeshCollider = UnityEngine.MeshCollider;
using static BoxCutter.BoxCutterSpatialGrid;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.MeshBuildPipeline;
using static BoxCutter.BoxCutterWorldPhysics;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter Core Object Component
    //─────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Core BoxCutter component representing a destructible object with voxel-based fragmentation.
    /// Manages object state, physics simulation, spatial organization, and destruction processing.
    /// Handles both procedural and MagicaVoxel-based voxel data for realistic destruction effects.
    /// </summary>
    [ExecuteInEditMode]
    public class BoxObj : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debug & Visualization Settings
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Enable detailed debug logging for destruction operations</summary>
        [Tooltip("Enable detailed debug logging for destruction operations")]
        public bool canDebug = false;

        /// <summary>Show visual gizmos in scene view for debugging spatial data</summary>
        [Tooltip("Show visual gizmos in scene view for debugging spatial data")]
        public bool canShowGizmos = false;

        /// <summary>Visualize spatial partitioning cells in scene view</summary>
        [Tooltip("Visualize spatial partitioning cells in scene view")]
        public bool canShowPartitions = false;

        /// <summary>Visualizes the spatial grid cells rather then the kd tree</summary>
        public bool showGridCells;

        /// <summary>KD-tree cell index for spatial acceleration data structure</summary>
        public int kdCellIndex = -1;

        /// <summary>Grid cell index for spatial acceleration data structure</summary>
        public int gridCellIndex = -1;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Voxel Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Enable custom voxel size override for finer destruction detail</summary>
        [Tooltip("Enable custom voxel size override for finer destruction detail")]
        public bool canCusVoxelSize = false;

        /// <summary>Custom voxel size for increased fragmentation resolution</summary>
        [Tooltip("Custom voxel size for increased fragmentation resolution (0.01-2.0 range)")]
        public float cusVoxelSize = 0.5f;

        /// <summary>Voxel resolution multiplier for MagicaVoxel data (divides base voxel size)</summary>
        [Tooltip("Voxel resolution multiplier for MagicaVoxel data (1-4 range, higher = finer detail)")] [Range(1, 4)]
        public int voxelResolution = 1;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Physics & Anchoring State
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>True if parent object has dynamic physics enabled</summary>
        [HideInInspector] public bool parentIsDynamic;

        /// <summary>Enable dynamic physics simulation for this object</summary>
        [Tooltip("Enable dynamic physics simulation for this object")]
        public bool isDynamic = false;

        /// <summary>Previous frame's dynamic state for change detection</summary>
        [SerializeField] private bool lastIsDynamic = false;

        /// <summary>Current anchoring state determining physics behavior</summary>
        [Tooltip("Current anchoring state determining physics behavior")]
        public ConnectionStateEnum connectionState = ConnectionStateEnum.Connected;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Attachment Constraints
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Enable whitelist-based attachment restrictions</summary>
        [Tooltip("Enable whitelist-based attachment restrictions")]
        public bool canWlAttach = false;

        /// <summary>Whitelist of BoxObj instances this object can attach to</summary>
        public List<BoxObj> wlAttachedBox = new List<BoxObj>();

        /// <summary>Inheritable IDs of whitelisted attachment targets</summary>
        [Tooltip("Inheritable IDs of whitelisted attachment targets")]
        public List<int> wlAttachedBoxInheritId = new List<int>();

        /// <summary>Enable require-based attachment restrictions</summary>
        [Tooltip("Enable whitelist-based attachment restrictions")]
        public bool canRequireBoxes = false;

        /// <summary>Enable custom require attachment logic</summary>
        [Tooltip("Enable custom require attachment logic")]
        public bool canCusRequireLogic = false;

        /// <summary>Unity function events to call instead</summary>
        [Tooltip("Unity function events to call instead")]
        public MonoBehaviour cusRequireLogic;

        /// <summary>Required BoxObjs that need to connect for the island to be considered anchored</summary>
        [SerializeField] private List<BoxObj> requiredBoxes = new List<BoxObj>();

        /// <summary>Inheritable IDs of required attachment targets</summary>
        [Tooltip("Inheritable IDs of required targets")]
        public List<int> requiredBoxesInheritId = new List<int>();

        /// <summary>If enabled the connection solver will ignore if the island does not have an anchor and base it purely on if all requiredBoxes are part of the island</summary>
        [Tooltip("If enabled the connection solver will ignore if the island does not have an anchor and base it purely on if all requiredBoxes are part of the island")]
        public bool useRequireAsAnchor = true;

        [Tooltip("The Group Module that controls this object")]
        public BoxCutterGroup group;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Custom Data Storage
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Enable custom data variable storage</summary>
        [Tooltip("Enable custom data variable storage")]
        public bool canCustomData;

        /// <summary>List of custom data entries with name, type, and value</summary>
        [Tooltip("List of custom data entries with name, type, and value")]
        public List<CustomDataEntry> customDataList = new List<CustomDataEntry>();

        /// <summary>
        /// Custom data entry with variable name, type selector, and type-specific value storage
        /// </summary>
        [Serializable]
        public struct CustomDataEntry
        {
            /// <summary>Name identifier for this custom variable</summary>
            public string varName;

            /// <summary>Type of data stored in this entry</summary>
            public CustomDataType varType;

            // Type-specific value fields - only the one matching varType should be used
            /// <summary>Integer value (when varType is Int)</summary>
            public int intValue;

            /// <summary>Float value (when varType is Float)</summary>
            public float floatValue;

            /// <summary>String value (when varType is String)</summary>
            public string stringValue;

            /// <summary>Boolean value (when varType is Bool)</summary>
            public bool boolValue;

            /// <summary>Vector3 value (when varType is Vector3)</summary>
            public Vector3 vector3Value;

            /// <summary>Color value (when varType is Color)</summary>
            public Color colorValue;

            /// <summary>GameObject reference (when varType is GameObject)</summary>
            public GameObject gameObjectValue;

            /// <summary>Generic Object reference (when varType is Object)</summary>
            public UnityEngine.Object objectValue;
        }

        /// <summary>
        /// Supported custom data types for variable storage
        /// </summary>
        public enum CustomDataType
        {
            Int,
            Float,
            String,
            Bool,
            Vector3,
            Color,
            GameObject,
            Object
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Collider Generation
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Method for generating colliders on fragments</summary>
        [Tooltip("Method for generating colliders on fragments")]
        public ColliderGenModeEnum colliderGenMode;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                              Manager Settings Override
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>///Enables overriding the default \"IgnoreDiagonalsInIsland\" in the BoxCutterManager</summary>
        [Tooltip("Enables overriding the default \"IgnoreDiagonalsInIsland\" in the BoxCutterManager")]
        public bool canOverrideDiagInIsland;

        /// <summary>The custom value you want for this box</summary>
        [Tooltip("The custom value you want for this box")]
        public bool diagInIslandTo;

        /// <summary>///Enables overriding the default \"KdCellTightness\" in the BoxCutterManager</summary>
        [Tooltip("Enables overriding the default \"KdCellTightness\" in the BoxCutterManager")]
        public bool canOverrideKdCellTightness;

        /// <summary>The custom value you want for this box</summary>
        [Tooltip("The custom value you want for this box")]
        public float kdCellTightnessTo = 0.5f;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                                Inherit Child Data
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Enable reparenting/inheriting GameObjects to any new newly created Box from destruction</summary>
        [Tooltip("Enable reparenting/inheriting GameObjects to any new newly created Box from destruction")]
        public bool canInheritChildTrans;

        /// <summary>Enable reparenting/inheriting GameObjects to any new newly created Box from destruction</summary>
        [Tooltip("List of data related the children that should be re-parented")]
        public List<InheritChildData> inheritChildDataList = new List<InheritChildData>();

        /// <summary>
        /// Data contains the child to be re-parented under the new box with the bound data to determine if the child is touching the box or not.
        /// </summary>
        [Serializable]
        public struct InheritChildData
        {
            /// <summary>The child transform to be re-parented under the new Box</summary>
            public Transform childTrans;

            /// <summary>The position offset to the transform's position</summary>
            public Vector3 boundPosOffset;

            /// <summary>The rotation offset to the transform's position</summary>
            public Vector3 boundRotOffset;

            /// <summary>The bound's size when check if the child is touching the new box</summary>
            public Vector3 boundScale;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               MagicaVoxel Integration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>MagicaVoxel .vox file asset for voxel-based destruction</summary>
        [Tooltip("MagicaVoxel .vox file asset for voxel-based destruction")]
        public MagicaVoxFile magicaVoxelData;

        /// <summary>Specific model index within .vox file (-1 for all models)</summary>
        [Tooltip("Specific model index within .vox file (-1 for all models)")]
        public int modelIndex = -1;

        /// <summary>Previous .vox file reference for change detection</summary>
        public MagicaVoxFile lastMagicaVoxelData;

        /// <summary>Previous model index for change detection</summary>
        public int lastModelIndex = -1;

        /// <summary>Ignore taking in account the magica voxel color's during collider generation</summary>
        [Tooltip("Ignore taking in account the magica voxel color's during collider generation. Helps reduce the number of box colliders.")]
        public bool forceIgnoreColorCollider;

        /// <summary>Automatically refresh MagicaVoxel data when changes are detected (editor only)</summary>
        [Tooltip("Automatically refresh MagicaVoxel data when changes are detected (editor only)")]
        public bool autoRefreshMagicaData = true;

        /// <summary>True if .vox file contains multiple models</summary>
        public bool hasModels = false;

        /// <summary>Total number of models available in .vox file</summary>
        public int maxModelsLength = 0;

        /// <summary>Previous voxel size for MagicaVoxel change detection</summary>
        public float lastCusVoxelSize;

        /// <summary>Previous transform scale for MagicaVoxel change detection</summary>
        public Vector3 lastLossyScale;

        /// <summary>Previous custom size state for MagicaVoxel change detection</summary>
        public bool lastCanCusVoxelSize;

        /// <summary>Previous voxel resolution for MagicaVoxel change detection</summary>
        public int lastVoxelResolution;

        /// <summary>Number of distinct colors found in MagicaVoxel data</summary>
        [ReadOnly] public int totalColors = 0;

        /// <summary>Local position offset X from MagicaVoxel mesh bounds center</summary>
        public float magicaLocalPosOffsetX;

        /// <summary>Local position offset Y from MagicaVoxel mesh bounds center</summary>
        public float magicaLocalPosOffsetY;

        /// <summary>Local position offset Z from MagicaVoxel mesh bounds center</summary>
        public float magicaLocalPosOffsetZ;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Voxel Size & Spatial Data
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>World size of a single voxel unit for destruction calculations</summary>
        public float voxelSize;

        /// <summary>3D voxel size vector for spatial calculations</summary>
        public float3 voxelSize3D;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Unity Component References
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>If the async is done and the box is ready to be used</summary>
        public bool ready;

        /// <summary>True if this object is currently pooled and inactive</summary>
        public bool pooled;

        /// <summary>Cached GameObject reference for performance</summary>
        public GameObject gameObj;

        /// <summary>Cached Transform reference for performance</summary>
        public Transform obj;

        /// <summary>MeshRenderer component for visual rendering</summary>
        public MeshRenderer meshRend;

        /// <summary>The materials used for rendering</summary>
        public List<Material> mats = new List<Material> { };

        /// <summary>MeshFilter component containing mesh data</summary>
        public MeshFilter meshFilter;

        /// <summary>Array of pre-existing collider components</summary>
        public Collider[] existingColliders;

        /// <summary>Rigidbody component for physics simulation</summary>
        public Rigidbody rb;

        /// <summary>Game Object's Tag</summary>
        public string myTag;

        /// <summary>Game Object's Layer</summary>
        public LayerMask myLayer;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Spatial & Transform Data
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Object's position</summary>
        [HideInInspector] public float3 pos;

        /// <summary>3D center position vector for spatial calculations</summary>
        [HideInInspector] public float3 centerPos;

        /// <summary>Center position X coordinate in world space</summary>
        [HideInInspector] public float centerPosX;

        /// <summary>Center position Y coordinate in world space</summary>
        [HideInInspector] public float centerPosY;

        /// <summary>Center position Z coordinate in world space</summary>
        [HideInInspector] public float centerPosZ;

        /// <summary>Bounding box size along X axis</summary>
        public float sizeX = 1;

        /// <summary>Bounding box size along Y axis</summary>
        public float sizeY = 1;

        /// <summary>Bounding box size along Z axis</summary>
        public float sizeZ = 1;

        /// <summary>Rotation quaternion X component</summary>
        public float qx;

        /// <summary>Rotation quaternion Y component</summary>
        public float qy;

        /// <summary>Rotation quaternion Z component</summary>
        public float qz;

        /// <summary>Rotation quaternion W component</summary>
        public float qw = 1;

        /// <summary>Inverse rotation quaternion X component for coordinate transforms</summary>
        public float iqx;

        /// <summary>Inverse rotation quaternion Y component for coordinate transforms</summary>
        public float iqy;

        /// <summary>Inverse rotation quaternion Z component for coordinate transforms</summary>
        public float iqz;

        /// <summary>Inverse rotation quaternion W component for coordinate transforms</summary>
        public float iqw;

        /// <summary>Local right vector X component for coordinate system</summary>
        public float rightX;

        /// <summary>Local right vector Y component for coordinate system</summary>
        public float rightY;

        /// <summary>Local right vector Z component for coordinate system</summary>
        public float rightZ;

        /// <summary>Local up vector X component for coordinate system</summary>
        public float upX;

        /// <summary>Local up vector Y component for coordinate system</summary>
        public float upY;

        /// <summary>Local up vector Z component for coordinate system</summary>
        public float upZ;

        /// <summary>Local forward vector X component for coordinate system</summary>
        public float fwdX;

        /// <summary>Local forward vector Y component for coordinate system</summary>
        public float fwdY;

        /// <summary>Local forward vector Z component for coordinate system</summary>
        public float fwdZ;

        /// <summary>Pre-scaled voxel right vector X component for local-to-world transforms</summary>
        public float vRightX;

        /// <summary>Pre-scaled voxel right vector Y component for local-to-world transforms</summary>
        public float vRightY;

        /// <summary>Pre-scaled voxel right vector Z component for local-to-world transforms</summary>
        public float vRightZ;

        /// <summary>Pre-scaled voxel up vector X component for local-to-world transforms</summary>
        public float vUpX;

        /// <summary>Pre-scaled voxel up vector Y component for local-to-world transforms</summary>
        public float vUpY;

        /// <summary>Pre-scaled voxel up vector Z component for local-to-world transforms</summary>
        public float vUpZ;

        /// <summary>Pre-scaled voxel forward vector X component for local-to-world transforms</summary>
        public float vFwdX;

        /// <summary>Pre-scaled voxel forward vector Y component for local-to-world transforms</summary>
        public float vFwdY;

        /// <summary>Pre-scaled voxel forward vector Z component for local-to-world transforms</summary>
        public float vFwdZ;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Fragment & Physics Management
        //────────────────────────────────────────────────────────────────────────────────────
        /// <summary>List of all SubBox fragments contained within this BoxObj</summary>
        public List<SubBox> allSubList = new List<SubBox>();
        
        public QuadRect[] quadRects = new QuadRect[]{};

        /// <summary>The Offset applied to allSubList in the build pipeline</summary>
        public int minXOffset, minYOffset, minZOffset;

        /// <summary>List of collider holder components managing fragment collision geometry</summary>
        public List<BoxColliderHolder> boxColliderHolderList = new List<BoxColliderHolder>();

        /// <summary>Unique identifier for this BoxObj instance within the scene</summary>
        public int uniqueId = -1;

        /// <summary>Inheritable identifier passed down to child fragments</summary>
        public int inheritableId;

        /// <summary>Custom group identifier for coordinated destruction behavior</summary>
        public int cusGroupId;

        /// <summary>Turn number when this fragment started falling (0 = never fell)</summary>
        public int fellInTurn = 0;

        /// <summary>Parent holder for managing multiple fragments as a physics group</summary>
        public BoxCutterParent parentHolder;

        /// <summary>Previously recorded linear velocity for physics continuity</summary>
        public Vector3 lastVelocity;

        /// <summary>Previously recorded angular velocity for physics continuity</summary>
        public Vector3 lastAngularVelocity;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                        Internal Data & Spatial Acceleration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Initial SubBox used to seed the voxel data structure</summary>
        private SubBox startingSub;

        /// <summary>Debug position for node visualization and debugging</summary>
        private float3 nodeStartPosDebug;

        /// <summary>Starting position X coordinate for voxel space origin</summary>
        public float startPosX;

        /// <summary>Starting position Y coordinate for voxel space origin</summary>
        public float startPosY;

        /// <summary>Starting position Z coordinate for voxel space origin</summary>
        public float startPosZ;

        /// <summary>
        /// KD-tree cell data structure for spatial partitioning acceleration.
        /// Contains bounding box information and SubBox range indices for fast collision queries.
        /// </summary>
        [Serializable]
        public struct CellData
        {
            public int minX;
            public int minY;
            public int minZ;
            public int maxX;
            public int maxY;
            public int maxZ;

            public int rangeStart;
            public int rangeEnd;
        }

        /// <summary>Array of original SubBox indices for KD-tree spatial queries</summary>
        public int[] kdOriginalIndicesArr = new int[] { };

        /// <summary>Array of KD-tree cell data for Burst-compiled collision detection</summary>
        public CellData[] kdCellDataArr = new CellData[] { };

        /// <summary>Total number of cells in the KD-tree structure</summary>
        public int kdCellLength;

        /// <summary>Number of leaf nodes in the KD-tree for collision optimization</summary>
        public int kdLeafLength;


        /// <summary>Array of original SubBox indices for KD-tree spatial queries</summary>
        public int[] gridOriginalIndicesArr = new int[] { };

        /// <summary>Array of KD-tree cell data for Burst-compiled collision detection</summary>
        public CellData[] gridCellDataArr = new CellData[] { };

        /// <summary>Total number of cells in the KD-tree structure</summary>
        public int gridCellLength;

        /// <summary>Number of leaf nodes in the KD-tree for collision optimization</summary>
        public int gridLeafLength;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               State Management & Caching
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Active coroutine for spawning debris particles during destruction</summary>
        private Coroutine debrisSpawningCoroutine;

        /// <summary>Cached position X for change detection and optimization</summary>
        private float lastPosX = Infinity;

        /// <summary>Cached position Y for change detection and optimization</summary>
        private float lastPosY = Infinity;

        /// <summary>Cached position Z for change detection and optimization</summary>
        private float lastPosZ = Infinity;

        /// <summary>Cached rotation X for change detection and optimization</summary>
        private float lastRotX = Infinity;

        /// <summary>Cached rotation Y for change detection and optimization</summary>
        private float lastRotY = Infinity;

        /// <summary>Cached rotation Z for change detection and optimization</summary>
        private float lastRotZ = Infinity;

        /// <summary>Cached rotation W for change detection and optimization</summary>
        private float lastRotW = Infinity;

        /// <summary>Cached scale X for change detection and optimization</summary>
        private float lastScaleX = Infinity;

        /// <summary>Cached scale Y for change detection and optimization</summary>
        private float lastScaleY = Infinity;

        /// <summary>Cached scale Z for change detection and optimization</summary>
        private float lastScaleZ = Infinity;

        /// <summary>List of spatial grid cells this BoxObj occupies for collision detection</summary>
        public List<WorldCellData> cellDataList = new List<WorldCellData>();

        /// <summary>True if this fragment has been visited during fall analysis</summary>
        public bool fallVisited;

        /// <summary>True if fall update has already been processed this frame</summary>
        public bool fallUpdateAlready;

        /// <summary>True if custom initialization has already been performed</summary>
        public bool cusInitAlready = false;

        /// <summary>Returns the box back to the pool after the specified time</summary>
        public bool hasReturnDelay;

        public float returnDelay;

        /// <summary>Mass multiplier factor for physics density simulation</summary>
        [Tooltip("Mass multiplier factor for physics density simulation")]
        public float massFac = 1;

        /// <summary>Strength multiplier for destruction falloff calculations</summary>
        [Tooltip("Strength multiplier for destruction falloff calculations (0-5 range)")] [Range(0f, 5f)]
        public float callerStrengthMul = 1f;

        /// <summary>Enable advanced fragmentation features for this object</summary>
        [Tooltip("Enable advanced fragmentation features for this object")]
        public bool canAdvancedFrag;

        /// <summary>Fragmentation settings configuration for destruction behavior</summary>
        [Tooltip("Fragmentation settings configuration for destruction behavior")]
        public FragSettings fragSettings;

        /// <summary>Overrides the custom plane normal and automatic sets the plane to be based on caller's position and direction</summary>
        [Tooltip("Overrides the custom plane normal and automatic sets the plane to be based on caller's position and direction")]
        public bool planeBasedOnCaller;

        /// <summary>Event triggered when this BoxObj receives a hit for destruction</summary>
        public event Action<BoxObj, CallerData> OnHit;

        /// <summary>Event triggered when fragments are created from this BoxObj</summary>
        public event Action<BoxObj, BoxCutterWorldPhysics.CreatedBoxData[]> OnFragmentsCreated;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Destruction Management
        //─────────────────────────────────────────────────────────────────────────────────────

        //public bool destroying;

        /*public BoxObj pendingParentBox;
        
        public List<BoxObj> pendingBoxes = new List<BoxObj>();
        
        public List<BoxObj> pendingDebrisBoxes = new List<BoxObj>();
        
        public List<BoxCutterOneDebris> pendingDebrisOnes = new List<BoxCutterOneDebris>();

        public CallerData createdFromCallerData;*/

        public List<QueuedDestroyData> queuedCallers = new List<QueuedDestroyData>();
           
        [Serializable]
        public class QueuedDestroyData
        {
            public CallerData callerData;
            public float3 localPos;
            public Quaternion localRot;

            public int queueId;

            public QueuedDestroyData(CallerData callerData, float3 localPos, Quaternion localRot)
            {
                this.callerData = callerData;
                this.localPos = localPos;
                this.localRot = localRot;
                
                queueId = BoxCutterManagerInstance.queueId++;
            }
            
            public QueuedDestroyData(CallerData callerData, float3 localPos, Quaternion localRot, int queueId)
            {
                this.callerData = callerData;
                this.localPos = localPos;
                this.localRot = localRot;
                this.queueId = queueId;
            }
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Unity Lifecycle Methods
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Unity Start method - initializes BoxObj if not already custom initialized.
        /// Sets up all necessary components, spatial data, and collision systems.
        /// </summary>
        void Start()
        {
            if (!IsPlaying) return;

            // Skip initialization if already done through custom init path
            if (cusInitAlready)
            {
                cusInitAlready = false;
                return;
            }

            InitAll(); // Perform full initialization sequence
        }

        /// <summary>
        /// Comprehensive initialization method setting up all BoxObj systems.
        /// Configures voxel data, components, spatial structures, and physics.
        /// Called both during Start() and when manually initializing fragments.
        /// </summary>
        /// <param name="isCusInit">True if called from custom initialization path</param>
        public void InitAll(bool isCusInit = false)
        {
            ready = true;

            GetId(); // Assign unique and inheritable ID
            GetComps(); // Cache Unity component references
            GetStartingSize(); // Determine object bounds
            GetVoxelSize(); // Calculate voxel dimensions
            UpdateLiveVars(); // Refresh transform matrices
            AddStartingSubObj(); // Create initial voxel data
            StartCoroutine(BuildConnectionDataDelay(true)); // Build attachment whitelist
            CreateParentHolder(false); // Setup physics parent if needed
            BuildSpatialData(false); // Build spatial acceleration
            BuildSpatialData(true); // Build spatial acceleration
            CalcMesh();
            _ = CalcColliders(true); // Generate collision geometry
            
            BoxCutterManagerInstance.onDestroyFinish += RunDestroy;

            cusInitAlready = isCusInit; // Track initialization state
        }

        #region EDITOR

#if UNITY_EDITOR
        /// <summary>
        /// Unity OnValidate callback for editor-time validation and setup.
        /// Handles MagicaVoxel file changes, dynamic state propagation, and auto-refresh management.
        /// Only executes when not in play mode to avoid runtime interference.
        /// </summary>
        private void OnValidate()
        {
            if (IsPlaying) return; // Skip validation during runtime

            HandleMagicaFile(); // Process .vox file changes
            HandleDynamic(); // Update dynamic state for children

            // If auto-refresh is enabled, check and refresh immediately
            if (autoRefreshMagicaData)
            {
                CheckAndRefreshMagicaData();
            }
        }

        /// <summary>
        /// Processes changes to MagicaVoxel file assignments and model selection.
        /// Clears existing voxel data when file is removed and reloads when changed.
        /// Updates model count information for multi-model .vox files.
        /// </summary>
        private void HandleMagicaFile()
        {
            // Clear data when .vox file is removed
            if (lastMagicaVoxelData != null && magicaVoxelData == null)
            {
                allSubList.Clear();
                lastMagicaVoxelData = null;
                lastModelIndex = -1;
                hasModels = false;

                ResetBox();
            }

            // Reload when .vox file or model index changes
            if (magicaVoxelData != null)
            {
                if (lastMagicaVoxelData != magicaVoxelData || lastModelIndex != modelIndex)
                {
                    MagicaBuilder.LoadVoxFile(this); // Load new voxel data

                    // Update model information for UI display
                    IVoxFile voxFile = MagicaBuilder.GetVoxFile(magicaVoxelData);
                    if (voxFile != null)
                    {
                        maxModelsLength = voxFile.Models.Length;
                        hasModels = maxModelsLength > 1; // Enable model selection UI
                    }

                    // Cache current state for change detection
                    lastMagicaVoxelData = magicaVoxelData;
                    lastModelIndex = modelIndex;
                }
            }
        }

        /// <summary>
        /// Propagates dynamic state changes to all child BoxObj components.
        /// Ensures consistent behavior across hierarchical destruction setups.
        /// Updates both dynamic and parent-dynamic flags for proper inheritance.
        /// </summary>
        private void HandleDynamic()
        {
            if (isDynamic || lastIsDynamic)
            {
                BoxObj[] childBoxcutterObjs = GetComponentsInChildren<BoxObj>();
                int boxcutterObjsLength = childBoxcutterObjs.Length;

                // Skip the parent (index 0) and update all children
                for (int i = 0; i < boxcutterObjsLength; i++)
                {
                    if (i == 0) continue; // Skip self

                    BoxObj childBoxObj = childBoxcutterObjs[i];
                    childBoxObj.parentIsDynamic = isDynamic; // Track parent state
                    childBoxObj.isDynamic = isDynamic; // Apply dynamic behavior
                }
            }

            lastIsDynamic = isDynamic; // Cache for change detection
        }

        /// <summary>
        /// Checks if MagicaVoxel data needs to be refreshed due to parameter changes.
        /// Detects changes in voxel file, transform scale, custom voxel size, and related settings.
        /// Uses caching to avoid expensive operations every frame.
        /// </summary>
        /// <returns>True if MagicaVoxel data needs refreshing</returns>
        public int CheckForMagicaDataChanges()
        {
            // Only check for objects with MagicaVoxel data
            if (magicaVoxelData == null)
            {
                return 0;
            }

            // Perform change detection
            int changeAmount = 0;

            bool sizeChanged = lastCusVoxelSize != cusVoxelSize;
            bool scaleChanged = lastLossyScale != transform.lossyScale;
            bool customChanged = lastCanCusVoxelSize != canCusVoxelSize;
            bool voxChanged = lastMagicaVoxelData != magicaVoxelData;
            bool modelChanged = lastModelIndex != modelIndex;
            bool resolutionChanged = lastVoxelResolution != voxelResolution;

            if (sizeChanged) changeAmount++;
            if (scaleChanged) changeAmount++;
            if (customChanged) changeAmount++;
            if (voxChanged) changeAmount++;
            if (modelChanged) changeAmount++;
            if (resolutionChanged) changeAmount++;

            return changeAmount;
        }

        /// <summary>
        /// Checks if the current custom voxel size is compatible with the object's scale.
        /// Returns true if compatible or if no custom voxel size is being used.
        /// </summary>
        /// <returns>True if custom voxel size is compatible with object scale</returns>
        private bool IsCustomVoxelSizeCompatible()
        {
            // If not using custom voxel size, always compatible
            if (!canCusVoxelSize) return true;

            // If using MagicaVoxel data, always compatible (suppress warnings)
            if (magicaVoxelData != null) return true;

            // For non-MagicaVoxel objects, check against world scale
            float defVoxelSize = BoxCutterManager.defVoxelSize;
            Vector3 worldScale = transform.lossyScale;
            float lowestDim = Mathf.Min(worldScale.x, Mathf.Min(worldScale.y, worldScale.z));
            float baseVoxelSize = lowestDim < defVoxelSize ? lowestDim : defVoxelSize;
            float effectiveVoxelSize = cusVoxelSize;

            const float epsilon = 0.0001f;
            float round = Mathf.Pow(10f, 5);

            worldScale.x = Mathf.Round(worldScale.x * round) / round;
            worldScale.y = Mathf.Round(worldScale.y * round) / round;
            worldScale.z = Mathf.Round(worldScale.z * round) / round;

            float modX = Mathf.Abs(worldScale.x % effectiveVoxelSize);
            float modY = Mathf.Abs(worldScale.y % effectiveVoxelSize);
            float modZ = Mathf.Abs(worldScale.z % effectiveVoxelSize);

            bool xBad = modX > epsilon && Mathf.Abs(modX - effectiveVoxelSize) > epsilon;
            bool yBad = modY > epsilon && Mathf.Abs(modY - effectiveVoxelSize) > epsilon;
            bool zBad = modZ > epsilon && Mathf.Abs(modZ - effectiveVoxelSize) > epsilon;

            return !(xBad || yBad || zBad);
        }

        /// <summary>
        /// Performs automatic MagicaVoxel data refresh if changes are detected and auto-refresh is enabled.
        /// Only executes in editor mode and only refreshes if custom voxel size is compatible.
        /// This prevents auto-refresh from hiding compatibility warnings in the editor.
        /// </summary>
        private void CheckAndRefreshMagicaData()
        {
            if (magicaVoxelData == null) return;

            int changeAmount = CheckForMagicaDataChanges();
            if (changeAmount > 0)
            {
                // Only auto-refresh if custom voxel size is compatible
                if (IsCustomVoxelSizeCompatible())
                {
                    if (changeAmount == 1 && lastVoxelResolution != voxelResolution)
                    {
                        MagicaBuilder.UpdateResolution(this);
                    }
                    else
                    {
                        MagicaBuilder.LoadVoxFile(this);
                    }

                    // Update cached values after refresh
                    lastCusVoxelSize = cusVoxelSize;
                    lastLossyScale = transform.lossyScale;
                    lastCanCusVoxelSize = canCusVoxelSize;
                    lastMagicaVoxelData = magicaVoxelData;
                    lastModelIndex = modelIndex;
                    lastVoxelResolution = voxelResolution;

#if UNITY_EDITOR
                    // Mark object as dirty for editor
                    EditorUtility.SetDirty(this);
#endif
                }
                // If incompatible, don't auto-refresh to allow editor warning to show
            }
        }

#endif

        #endregion

        /// <summary>
        /// Unity Update method - handles dynamic object updates and MagicaVoxel auto-refresh in editor.
        /// Processes frame-based updates and change detection for automatic data refresh.
        /// </summary>
        private void Update()
        {
            if (!IsPlaying)
            {
#if UNITY_EDITOR
                if (autoRefreshMagicaData)
                    CheckAndRefreshMagicaData();

                return;
#endif
            }

            if (isDynamic) UpdateLiveVars(true);
            fallUpdateAlready = false; // Reset fall processing flag
        }

        #region INIT

        #region VARS

        /// <summary>
        /// Calculates voxel size based on density settings and MagicaVoxel data.
        /// Uses default voxel size unless MagicaVoxel file provides specific dimensions.
        /// Applies density scaling for higher resolution destruction when enabled.
        /// </summary>
        public void GetVoxelSize()
        {
            // MagicaVoxel files define their own size, the original magicaVoxelData != null case did not account for if the user deleted the vox file
            if (allSubList.Count > 0) return;

            // Calc Voxel Size
            float lowestDim = Mathf.Min(sizeX, Mathf.Min(sizeY, sizeZ));
            voxelSize = lowestDim < defVoxelSize ? lowestDim : defVoxelSize;
            voxelSize3D = defVoxelSize3D;

            // Apply custom voxel size override
            if (canCusVoxelSize)
            {
                voxelSize = cusVoxelSize; // Override with custom size
                voxelSize3D = cusVoxelSize; // Maintain proportional scaling
            }
        }

        public void GetId()
        {
            if (!IsPlaying) return;
            int idTo = BoxCutterManagerInstance.boxAllUniqueID++;
            uniqueId = idTo;
            inheritableId = idTo;
        }

        /// <summary>
        /// Caches Unity component references for performance optimization.
        /// Stores transform, GameObject, and all relevant components to avoid repeated GetComponent calls.
        /// Removes pre-existing physics components during runtime to prevent conflicts.
        /// </summary>
        public void GetComps()
        {
            obj = transform; // Cache transform reference
            gameObj = gameObject; // Cache GameObject reference

            // Cache all collision and rendering components
            existingColliders = GetComponents<Collider>();
            meshRend = GetComponent<MeshRenderer>();
            if (IsPlaying) mats = meshRend.materials.ToList();
            meshFilter = GetComponent<MeshFilter>();
            rb = GetComponent<Rigidbody>();

            myTag = gameObj.tag;
            myLayer = gameObj.layer;

            // Clean up conflicting physics during runtime initialization
            if (IsPlaying)
            {
                RemovePreExistingPhysics();
            }
        }

        /// <summary>
        /// Updates cached position and rotation data from Unity Transform.
        /// Calculates both forward and inverse quaternion components for coordinate transformations.
        /// Applies MagicaVoxel-specific position offset corrections when applicable.
        /// </summary>
        public void RefreshPosRot()
        {
            pos = obj.position; // Cache world position
            centerPos = pos;

            ApplyMagicaVoxelOffset(); // Apply .vox file position corrections

            // Cache rotation quaternion components
            Quaternion quatRot = obj.rotation;
            qx = quatRot.x;
            qy = quatRot.y;
            qz = quatRot.z;
            qw = quatRot.w;

            // Calculate inverse quaternion for coordinate transforms
            Quaternion inverseRot = Quaternion.Inverse(quatRot);
            iqx = inverseRot.x;
            iqy = inverseRot.y;
            iqz = inverseRot.z;
            iqw = inverseRot.w;
        }

        /// <summary>
        /// Determines object bounding box size from Unity Transform scale.
        /// Uses lossy scale to account for parent transform scaling effects.
        /// Only recalculates when forced or when size values are uninitialized.
        /// </summary>
        /// <param name="forceRefresh">Force recalculation even if size is already set</param>
        public void GetStartingSize(bool forceRefresh = false)
        {
            // Only calculate if forced or size is uninitialized
            if (forceRefresh || (sizeX == 0 && sizeY == 0 && sizeZ == 0) || allSubList.Count == 0)
            {
                Vector3 lossyScale = obj.lossyScale; // Account for parent scaling
                sizeX = lossyScale.x;
                sizeY = lossyScale.y;
                sizeZ = lossyScale.z;
            }
        }

        /// <summary>
        /// Applies MagicaVoxel-specific position offset corrections to center position.
        /// Transforms local offset using object's coordinate system to world space.
        /// Updates both vector and component representations of center position.
        /// </summary>
        public void ApplyMagicaVoxelOffset()
        {
            // Transform MagicaVoxel offset from local to world space
            centerPos.x += rightX * magicaLocalPosOffsetX + upX * magicaLocalPosOffsetY + fwdX * magicaLocalPosOffsetZ;
            centerPos.y += rightY * magicaLocalPosOffsetX + upY * magicaLocalPosOffsetY + fwdY * magicaLocalPosOffsetZ;
            centerPos.z += rightZ * magicaLocalPosOffsetX + upZ * magicaLocalPosOffsetY + fwdZ * magicaLocalPosOffsetZ;

            // Update component representations for performance
            centerPosX = centerPos.x;
            centerPosY = centerPos.y;
            centerPosZ = centerPos.z;
        }

        /// <summary>
        /// Calculates the true starting position (minimum corner) of the voxel space.
        /// Transforms from center position to corner position using half-size offsets.
        /// Accounts for object rotation by applying local coordinate system vectors.
        /// </summary>
        public void RefreshTrueStartPos()
        {
            // Calculate half-extents for offset calculations
            float ogHalfSizeX = sizeX / 2;
            float ogHalfSizeY = sizeY / 2;
            float ogHalfSizeZ = sizeZ / 2;

            // Transform from center to minimum corner using local coordinate system
            startPosX = centerPosX - rightX * ogHalfSizeX - upX * ogHalfSizeY - fwdX * ogHalfSizeZ;
            startPosY = centerPosY - rightY * ogHalfSizeX - upY * ogHalfSizeY - fwdY * ogHalfSizeZ;
            startPosZ = centerPosZ - rightZ * ogHalfSizeX - upZ * ogHalfSizeY - fwdZ * ogHalfSizeZ;
        }

        /// <summary>
        /// Calculates local coordinate system vectors from object rotation quaternion.
        /// Performs manual quaternion-to-matrix conversion for optimal performance.
        /// Generates both unit vectors and voxel-scaled vectors for coordinate transformations.
        /// </summary>
        public void RefreshLocalDirs()
        {
            Quaternion r = obj.rotation;
            float rx = r.x, ry = r.y, rz = r.z, rw = r.w;

            // Pre-calculate quaternion component products for matrix conversion
            float xx = rx * rx;
            float yy = ry * ry;
            float zz = rz * rz;
            float xy = rx * ry;
            float xz = rx * rz;
            float yz = ry * rz;
            float wx = rw * rx;
            float wy = rw * ry;
            float wz = rw * rz;

            // Calculate right vector (local X axis) from quaternion
            rightX = 1f - 2f * (yy + zz);
            rightY = 2f * (xy + wz);
            rightZ = 2f * (xz - wy);

            // Calculate up vector (local Y axis) from quaternion
            upX = 2f * (xy - wz);
            upY = 1f - 2f * (xx + zz);
            upZ = 2f * (yz + wx);

            // Calculate forward vector (local Z axis) from quaternion
            fwdX = 2f * (xz + wy);
            fwdY = 2f * (yz - wx);
            fwdZ = 1f - 2f * (xx + yy);

            // Pre-scale vectors by voxel size for coordinate transformations
            vRightX = rightX * voxelSize;
            vRightY = rightY * voxelSize;
            vRightZ = rightZ * voxelSize;

            vUpX = upX * voxelSize;
            vUpY = upY * voxelSize;
            vUpZ = upZ * voxelSize;

            vFwdX = fwdX * voxelSize;
            vFwdY = fwdY * voxelSize;
            vFwdZ = fwdZ * voxelSize;
        }

        /// <summary>
        /// Creates the initial SubBox representing the entire object's voxel space.
        /// Calculates voxel grid dimensions by dividing object size by voxel size.
        /// Only creates the starting SubBox if the list is empty to avoid duplicates.
        /// </summary>
        public void AddStartingSubObj()
        {
            if (allSubList.Count != 0) return; // Skip if already initialized

            // Create initial SubBox covering entire object bounds in voxel coordinates
            allSubList.Add(new SubBox
            {
                minX = 0, // Start at origin
                minY = 0,
                minZ = 0,
                // Calculate maximum voxel coordinates from object size
                maxX = (int)Round((sizeX / voxelSize), 1),
                maxY = (int)Round((sizeY / voxelSize), 1),
                maxZ = (int)Round((sizeZ / voxelSize), 1),
                
                quadStart = 0,
                quadEnd = 6,
            });
        }

        /// <summary>
        /// Rounds a floating-point value to the specified number of decimal places.
        /// </summary>
        /// <param name="value">Value to round</param>
        /// <param name="digits">Number of decimal places</param>
        /// <returns>Rounded value</returns>
        private float Round(float value, int digits)
        {
            float mult = Mathf.Pow(10.0f, (float)digits);
            return Mathf.Round(value * mult) / mult;
        }

        /// <summary>
        /// Ensures proper cleanup of data.
        /// </summary>
        private void OnDestroy()
        {
            if (!IsPlaying) return;
            if (BoxCutterManagerInstance.CheckExit()) return;
            if (BoxCutterPoolInstance != null)
            {
                RemoveFromPartition();
                DeActivate();
                ResetBox();
            }
        }

        /// <summary>
        /// Checks if object is being destroyed
        /// </summary>
        public bool CheckExit(int checkType = -1)
        {
            if ((checkType == -1 || checkType == 0) && obj == null)
            {
                //Debug.LogWarning("[BoxCutter] Removing the Transform component on a BoxObj object may produce unexpected results");
                return true;
            }

            if ((checkType == -1 || checkType == 1) && meshFilter == null)
            {
                //Debug.LogWarning("[BoxCutter] Removing the MeshFilter component on a BoxObj object may produce unexpected results");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a parent holder GameObject for managing dynamic physics on disconnected fragments.
        /// Sets up rigidbody simulation with proper mass calculation and optional impact force application.
        /// </summary>
        public void CreateParentHolder()
        {
            connectionState = ConnectionStateEnum.Disconnected;
            CreateParentHolder(false);
        }

        /// <summary>
        /// Creates a parent holder GameObject for managing dynamic physics on disconnected fragments.
        /// Sets up rigidbody simulation with proper mass calculation and optional impact force application.
        /// </summary>
        /// <param name="applyForce">Whether to apply destruction force to the rigidbody</param>
        /// <param name="callerData">Destruction data containing force information</param>
        /// <param name="parentHolderName">Name for the created parent GameObject</param>
        public void CreateParentHolder(bool applyForce, in CallerData callerData = null, string parentHolderName = "Rb Parent")
        {
            if (connectionState != ConnectionStateEnum.Disconnected || CheckExit(0) || BoxCutterManagerInstance.CheckExit()) return;

            isDynamic = true;

            // Create parent GameObject for physics management
            GameObject parentHolderGo = new GameObject(parentHolderName);
            parentHolder = parentHolderGo.AddComponent<BoxCutterParent>();

            fellInTurn = BoxCutterManagerInstance.boxCutterCurrentFallTurn += 1;

            // Setup transform hierarchy and rigidbody
            Transform islandParentObj = parentHolderGo.transform;
            islandParentObj.position = centerPos;
            Rigidbody parentRb = parentHolderGo.AddComponent<Rigidbody>();
            parentRb.isKinematic = true;
            parentHolder.obj = islandParentObj;
            parentHolder.rb = parentRb;
            parentHolder.childrenBoxes = new[] { this };

            parentHolder.obj.position = centerPos;
            parentRb.position = centerPos;

            obj.parent = islandParentObj;

            // Configure rigidbody physics properties
            parentRb.mass = CalcVolume(this);
            parentRb.interpolation = RigidbodyInterpolation.Interpolate;
            parentRb.isKinematic = false;

            // Apply destruction force if requested
            if (applyForce)
            {
                Vector3 forceDir = new Vector3(centerPosX - callerData.forcePos.x, centerPosY - callerData.forcePos.y, centerPosZ - callerData.forcePos.z);
                ApplyForce(parentRb, forceDir, callerData.forceRange, callerData.force);
            }
        }

        /// <summary>
        /// Finds the associated var name in the custom data list and returns it.
        /// </summary>
        /// <param name="varName">Name of the custom entry you want return</param>
        /// <returns></returns>
        public CustomDataEntry? GetCustomData(string varName)
        {
            int customDataLength = customDataList.Count;

            for (int i = 0; i < customDataLength; i++)
            {
                if (customDataList[i].varName.Equals(varName))
                    return customDataList[i];
            }

            return null;
        }

        /// <summary>
        /// Builds KD-tree spatial acceleration structure from current SubBox collection.
        /// Wrapper method that converts managed list to native array before processing.
        /// </summary>
        /// <param name="kdMode">Toggle for building a kd-tree instead</param>
        /// <param name="cusKdMaxSubInCell">Custom maximum SubBoxes per leaf (0 for default)</param>
        public void BuildSpatialData(bool kdMode, int cusKdMaxSubInCell = 0)
        {
            SubBox[] allSubObjArr = new SubBox[allSubList.Count];
            allSubList.CopyTo(allSubObjArr);
            using NativeArray<SubBox> allSubNa = new NativeArray<SubBox>(allSubObjArr, Allocator.TempJob);
            BuildSpatialData(allSubNa, kdMode, cusKdMaxSubInCell);
        }

        /// <summary>
        /// Builds KD-tree spatial acceleration structure from provided native SubBox array.
        /// Creates balanced hierarchy for efficient collision detection and spatial queries.
        /// </summary>
        /// <param name="subsNa">Native array of SubBoxes to organize</param>
        /// <param name="kdMode">Toggle for building a kd-tree instead</param>
        /// <param name="cusKdMaxSubInCell">Custom maximum SubBoxes per leaf (0 for default)</param>
        public async void BuildSpatialData(NativeArray<SubBox> subsNa, bool kdMode, int cusKdMaxSubInCell = 0)
        {
            int subCount = subsNa.Length;
            if (subCount == 0) return;

            // 1. Setup parameters
            int maxPerCell = cusKdMaxSubInCell == 0 ? BoxCutterManagerInstance.kdMaxSubInCell : cusKdMaxSubInCell;

            // Early exit optimization: if subCount <= maxPerCell, manually create simple structure to avoid job overhead
            if (subCount <= maxPerCell)
            {
                // Create sequential indices array [0, 1, 2, ..., subCount-1]
                int[] sequentialIndices = new int[subCount];
                for (int i = 0; i < subCount; i++)
                    sequentialIndices[i] = i;

                // Calculate bounding box across all SubBoxes
                int minX = IntInfinity, minY = IntInfinity, minZ = IntInfinity;
                int maxX = NegIntInfinity, maxY = NegIntInfinity, maxZ = NegIntInfinity;

                SubBox[] subArr = new SubBox[subCount];
                subsNa.CopyTo(subArr);

                for (int i = 0; i < subCount; i++)
                {
                    SubBox sub = subArr[i];
                    if (sub.minX < minX) minX = sub.minX;
                    if (sub.minY < minY) minY = sub.minY;
                    if (sub.minZ < minZ) minZ = sub.minZ;
                    if (sub.maxX > maxX) maxX = sub.maxX;
                    if (sub.maxY > maxY) maxY = sub.maxY;
                    if (sub.maxZ > maxZ) maxZ = sub.maxZ;
                }

                // Create single cell containing all SubBoxes
                CellData singleCell = new CellData
                {
                    minX = minX,
                    minY = minY,
                    minZ = minZ,
                    maxX = maxX,
                    maxY = maxY,
                    maxZ = maxZ,
                    rangeStart = 0,
                    rangeEnd = subCount
                };

                if (kdMode)
                {
                    kdLeafLength = subCount;
                    kdCellLength = 1;
                    kdOriginalIndicesArr = sequentialIndices;
                    kdCellDataArr = new CellData[] { singleCell };
                }
                else
                {
                    gridLeafLength = subCount;
                    gridCellLength = 1;
                    gridOriginalIndicesArr = sequentialIndices;
                    gridCellDataArr = new CellData[] { singleCell };
                }

                return;
            }

            float fillRatio = canOverrideKdCellTightness ? kdCellTightnessTo : BoxCutterManagerInstance.validCellFillRatio;

            using NativeArray<int> subStartsNa = new NativeArray<int>(new int[] { 0 }, Allocator.TempJob);
            using NativeArray<int> subCountsNa = new NativeArray<int>(new int[] { subCount }, Allocator.TempJob);
            using NativeArray<float> ratiosNa = new NativeArray<float>(new float[] { fillRatio }, Allocator.TempJob);

            (NativeArray<int> kdOriginalIndicesGlobalNa, NativeArray<CellData> kdCellDataGlobalNa, NativeArray<int> kdCellOwnerNa, NativeArray<int2> leafCellCountPerBoxNa) spatialResults = new ValueTuple<NativeArray<int>, NativeArray<CellData>, NativeArray<int>, NativeArray<int2>>();

            if (kdMode)
            {
                spatialResults = await DestructionPipeline.BuildKdTree(
                    subsNa,
                    ratiosNa,
                    subStartsNa,
                    subCountsNa,
                    Allocator.TempJob,
                    sync:true
                );

                kdLeafLength = spatialResults.leafCellCountPerBoxNa[0].x; // total sub count
                kdCellLength = spatialResults.leafCellCountPerBoxNa[0].y; // active grid cells

                kdOriginalIndicesArr = new int[subCount];
                kdCellDataArr = new CellData[kdCellLength];

                spatialResults.kdOriginalIndicesGlobalNa.CopyTo(kdOriginalIndicesArr);

                // We only copy the active cells belonging to this box (which is all of them in this single-box call)
                if (kdCellLength > 0)
                    NativeArray<CellData>.Copy(spatialResults.kdCellDataGlobalNa, 0, kdCellDataArr, 0, kdCellLength);
            }
            
            else
            {
                spatialResults = DestructionPipeline.BuildGrid(
                    subsNa,
                    ratiosNa,
                    subStartsNa,
                    subCountsNa,
                    Allocator.TempJob
                );

                gridLeafLength = spatialResults.leafCellCountPerBoxNa[0].x; // total sub count
                gridCellLength = spatialResults.leafCellCountPerBoxNa[0].y; // active grid cells

                gridOriginalIndicesArr = new int[gridLeafLength];
                gridCellDataArr = new CellData[gridCellLength];

                spatialResults.kdOriginalIndicesGlobalNa.CopyTo(gridOriginalIndicesArr);

                // We only copy the active cells belonging to this box (which is all of them in this single-box call)
                if (gridCellLength > 0)
                    NativeArray<CellData>.Copy(spatialResults.kdCellDataGlobalNa, 0, gridCellDataArr, 0, gridCellLength);
            }

            // 5. Dispose the NativeArrays returned by BuildGrid
            spatialResults.kdOriginalIndicesGlobalNa.Dispose();
            spatialResults.kdCellDataGlobalNa.Dispose();
            spatialResults.kdCellOwnerNa.Dispose();
            spatialResults.leafCellCountPerBoxNa.Dispose();
        }

        private IEnumerator BuildConnectionDataDelay(bool init = false)
        {
            // One frame delay as other BoxObjs may not have been initialized yet
            yield return null;
            BuildConnectionData(init);
        }

        /// <summary>
        /// Removes null references from connection related lists and caches the boxes inheritable IDs.
        /// </summary>
        /// <param name="init">If true, populate inheritable ID list from whitelist entries</param>
        public void BuildConnectionData(bool init = false)
        {
            CreateInheritIds(wlAttachedBox, wlAttachedBoxInheritId, init);
            CreateInheritIds(requiredBoxes, requiredBoxesInheritId, init);
        }

        private void CreateInheritIds(List<BoxObj> boxList, List<int> inheritIdList, bool init)
        {
            CleanUpBoxObjList(boxList);
            int count = boxList.Count;
            if (init)
                for (int i = 0; i < count; i++)
                    inheritIdList.Add(boxList[i].inheritableId);
        }

        private void CleanUpBoxObjList(List<BoxObj> boxList)
        {
            int boxListCount = boxList.Count;
            if (boxListCount > 0)
            {
                int forceStartIdx = boxListCount - 1;
                for (int i = forceStartIdx; i >= 0; i--)
                    if (boxList[i] == null)
                        boxList.RemoveAt(i);
            }
        }
            
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                   Destruction Handling
        //─────────────────────────────────────────────────────────────────────────────────────

        public void RunDestroy()
        {
            if (queuedCallers.Count == 0) return;
            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.destroying) return;
            manager.currentFrameQueuedBoxes.Add(this);
        }

        public bool AddCaller(CallerData callerData)
        {
            float3 localPos = obj.InverseTransformPoint(callerData.pos);
            Quaternion localRot = obj.InverseTransformRotation(callerData.boxQuat);
            
            // Determines if this queued caller would be different from the existing ones, if so then continue otherwise return
            int pendingCount = queuedCallers.Count;

            if (pendingCount > 0)
            {
                bool tooSimilar = false;
                float distThreshold = voxelSize * 0.5f;
                
                for (int i = 0; i < pendingCount; i++)
                {
                    QueuedDestroyData pending = queuedCallers[i];
                    float dist = math.distance(localPos, pending.localPos);
                    if (dist < distThreshold)
                    {
                        if (pending.callerData.boxMode)
                        {
                            // Check if box bounds are different
                            float3 boundsDiff = math.abs(pending.callerData.boxBounds - callerData.boxBounds);
                            if (boundsDiff.x > distThreshold || boundsDiff.y > distThreshold || boundsDiff.z > distThreshold)
                            {
                                continue;
                            }
    
                            // Check if rotations are different using quaternion dot product
                            // Dot product close to 1 (or -1) means similar rotation
                            float rotDot = math.abs(pending.callerData.boxQuat.x * callerData.boxQuat.x +
                                                    pending.callerData.boxQuat.y * callerData.boxQuat.y +
                                                    pending.callerData.boxQuat.z * callerData.boxQuat.z +
                                                    pending.callerData.boxQuat.w * callerData.boxQuat.w);
    
                            if (rotDot < 0.999f) // rotations are not similar enough
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (math.abs(pending.callerData.radius - callerData.radius) > distThreshold)
                            {
                                continue;
                            }
                        }
                        
                        tooSimilar = true;
                        break;
                    }
                }

                if (tooSimilar) return false;
            }

            queuedCallers.Add(new QueuedDestroyData(callerData, localPos, localRot));
            return true;
        }
        
        public List<QueuedDestroyData> TransferPending(BoxObj newBox)
        {
            int pendingCount = queuedCallers.Count;
            List<QueuedDestroyData> newPendingList = new List<QueuedDestroyData>();
            
            for (int i = 0; i < pendingCount; i++)
            {
                QueuedDestroyData pending = queuedCallers[i];
                
                float3 worldPos = obj.TransformPoint(pending.localPos);
                Quaternion worldRot = obj.TransformRotation(pending.localRot);

                CallerData pendingCallerData = pending.callerData;
                
                if (!BoxCutterCollisionUtil.CallerIntersectWithBox(pendingCallerData, newBox, true)) continue;
                newPendingList.Add(new QueuedDestroyData(pendingCallerData, newBox.obj.InverseTransformPoint(worldPos), newBox.obj.InverseTransformRotation(worldRot), pending.queueId));
            }
            
            return newPendingList;
        }
        
        /// <summary>
        /// Updates spatial grid partition assignment based on transform changes.
        /// Recalculates bounding volume and updates grid cells when movement exceeds threshold.
        /// </summary>
        /// <param name="forceRefresh">Force update regardless of change threshold (1 = force)</param>
        public void CalcPartitionLocation(bool forceRefresh = false)
        {
            if (!ready) return;
            const float triggerThreshold = 0.01f;

            float dx = centerPosX - lastPosX;
            float dy = centerPosY - lastPosY;
            float dz = centerPosZ - lastPosZ;
            float drx = qx - lastRotX;
            float dry = qy - lastRotY;
            float drz = qz - lastRotZ;
            float drw = qw - lastRotW;
            float dsx = sizeX - lastScaleX;
            float dsy = sizeY - lastScaleY;
            float dsz = sizeZ - lastScaleZ;

            if (forceRefresh ||
                (dx < 0 ? -dx : dx) >= triggerThreshold ||
                (dy < 0 ? -dy : dy) >= triggerThreshold ||
                (dz < 0 ? -dz : dz) >= triggerThreshold ||
                (drx < 0 ? -drx : drx) >= triggerThreshold ||
                (dry < 0 ? -dry : dry) >= triggerThreshold ||
                (drz < 0 ? -drz : drz) >= triggerThreshold ||
                (drw < 0 ? -drw : drw) >= triggerThreshold ||
                (dsx < 0 ? -dsx : dsx) >= triggerThreshold ||
                (dsy < 0 ? -dsy : dsy) >= triggerThreshold ||
                (dsz < 0 ? -dsz : dsz) >= triggerThreshold)
            {
                BoxCutterSpatialGrid boxCutterSpatialGrid = BoxCutterSpatialGridInstance;

                RemoveFromPartition();

                float halfSizeX = sizeX * 0.5f;
                float halfSizeY = sizeY * 0.5f;
                float halfSizeZ = sizeZ * 0.5f;

                float extentX = math.abs(rightX * halfSizeX) + math.abs(upX * halfSizeY) + math.abs(fwdX * halfSizeZ);
                float extentY = math.abs(rightY * halfSizeX) + math.abs(upY * halfSizeY) + math.abs(fwdY * halfSizeZ);
                float extentZ = math.abs(rightZ * halfSizeX) + math.abs(upZ * halfSizeY) + math.abs(fwdZ * halfSizeZ);

                float lowestX = centerPosX - extentX;
                float highestX = centerPosX + extentX;

                float lowestY = centerPosY - extentY;
                float highestY = centerPosY + extentY;

                float lowestZ = centerPosZ - extentZ;
                float highestZ = centerPosZ + extentZ;

                cellDataList = boxCutterSpatialGrid.AddBoxObj(this, lowestX, lowestY, lowestZ, highestX, highestY, highestZ);
            }

            lastPosX = centerPosX;
            lastPosY = centerPosY;
            lastPosZ = centerPosZ;

            lastRotX = qx;
            lastRotY = qy;
            lastRotZ = qz;
            lastRotW = qw;

            lastScaleX = sizeX;
            lastScaleY = sizeY;
            lastScaleZ = sizeZ;
        }

        /// <summary>
        /// Removes this BoxObj from all spatial grid cells it was previously assigned to.
        /// Used before recalculating partition assignment to prevent duplicate entries.
        /// </summary>
        public void RemoveFromPartition()
        {
            int gridCount = cellDataList.Count;

            for (int i = 0; i < gridCount; i++)
            {
                WorldCellData worldCellData = cellDataList[i];
                List<BoxObj> boxList = worldCellData.boxObjList;
                BoxObj[] boxArr = new BoxObj[boxList.Count];
                boxList.CopyTo(boxArr);

                int gridBoxCount = boxArr.Length - 1;

                for (int v = gridBoxCount; v >= 0; v--)
                {
                    if (boxArr[v].uniqueId == uniqueId)
                    {
                        boxList.RemoveAt(v);
                        break;
                    }
                }
            }
        }

        #endregion

        public void CalcMesh()
        {
            if (magicaVoxelData != null)
            {
                _ = BuildAccurate(new BoxObj[] { this }, true, nonDestructionCall: true);
            }
            else
            {
                SubBox startSub = allSubList[0];

                // Create 6 QuadRects for all 6 faces of the starting SubBox
                quadRects = new QuadRect[]
                {
                    // NegX (dir=0): U=Z, V=Y
                    new QuadRect
                    {
                        dir = 0,
                        minU = startSub.minZ,
                        maxU = startSub.maxZ,
                        minV = startSub.minY,
                        maxV = startSub.maxY
                    },
                    // PosX (dir=1): U=Z, V=Y
                    new QuadRect
                    {
                        dir = 1,
                        minU = startSub.minZ,
                        maxU = startSub.maxZ,
                        minV = startSub.minY,
                        maxV = startSub.maxY
                    },
                    // NegY (dir=2): U=X, V=Z
                    new QuadRect
                    {
                        dir = 2,
                        minU = startSub.minX,
                        maxU = startSub.maxX,
                        minV = startSub.minZ,
                        maxV = startSub.maxZ
                    },
                    // PosY (dir=3): U=X, V=Z
                    new QuadRect
                    {
                        dir = 3,
                        minU = startSub.minX,
                        maxU = startSub.maxX,
                        minV = startSub.minZ,
                        maxV = startSub.maxZ
                    },
                    // NegZ (dir=4): U=X, V=Y
                    new QuadRect
                    {
                        dir = 4,
                        minU = startSub.minX,
                        maxU = startSub.maxX,
                        minV = startSub.minY,
                        maxV = startSub.maxY
                    },
                    // PosZ (dir=5): U=X, V=Y
                    new QuadRect
                    {
                        dir = 5,
                        minU = startSub.minX,
                        maxU = startSub.maxX,
                        minV = startSub.minY,
                        maxV = startSub.maxY
                    }
                };
            }
        }

        /// <summary>
        /// Generates collision geometry for this BoxObj using either mesh colliders or box collider primitives.
        /// Automatically chooses optimal collider type based on anchoring state and user preferences.
        /// </summary>
        /// <param name="sync">Enable to force the colliders to be calculated in the same frame</param>
        /// <param name="maxFrames">Max amount of processing frames</param>
        public async Task CalcColliders(bool sync = false, int maxFrames = -1)
        {
            ColliderGenModeEnum finalGenMode = GetFinalColliderGen();
            if (finalGenMode == ColliderGenModeEnum.Coarse)
                await CalcSingleMeshCollider(sync, maxFrames);
            else if (finalGenMode == ColliderGenModeEnum.Smart)
                await CalcSmartColliders(sync, maxFrames);
            else
                CalcBoxColliders();
        }

        public ColliderGenModeEnum GetFinalColliderGen()
        {
            if (colliderGenMode == ColliderGenModeEnum.Coarse || (colliderGenMode == ColliderGenModeEnum.Auto && connectionState == ConnectionStateEnum.Anchored))
                return ColliderGenModeEnum.Coarse;
            if (colliderGenMode == ColliderGenModeEnum.Smart || colliderGenMode == ColliderGenModeEnum.Auto)
                return gridCellDataArr.Length == 1 || !ShouldConvex() ? ColliderGenModeEnum.Coarse : ColliderGenModeEnum.Smart;
            return ColliderGenModeEnum.Perfect;
        }

        private async Task CalcSmartColliders(bool sync = false, int maxFrames = -1)
        {
            Mesh[] meshes = await BuildAccuratePerKdCell(this, sync, maxFrames);

            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.CheckExit()) return;

#if UNITY_6000_3_OR_NEWER
            EntityId[] meshIdArr = new EntityId[kdCellLength];
            for (int i = 0; i < kdCellLength; i++)
                meshIdArr[i] = meshes[i].GetEntityId();
            using NativeArray<EntityId> meshId = new NativeArray<EntityId>(meshIdArr, Allocator.TempJob);
#else
            int[] meshIdArr = new int[kdCellLength];
            for (int i = 0; i < kdCellLength; i++)
                meshIdArr[i] = meshes[i].GetInstanceID();

            using NativeArray<int> meshId = new NativeArray<int>(meshIdArr, Allocator.TempJob);
#endif

            bool convexTo = ShouldConvex();

            JobHandle handle = new BakeMultiMeshJob
            {
                MeshId = meshId,
                Convex = convexTo,
                Options = cookingOptions,
            }.Schedule(kdCellLength, 32);

            await WaitJobComplete(handle, sync, maxFrames);
            if (manager.CheckExit()) return;

            string callerName = $"Calc Smart Mesh: {uniqueId}";
            manager.FreezeParent(this, callerName);

            UpdateLiveVars(true);
            ReturnColliderHolderToPool(false);

            for (int i = 0; i < kdCellLength; i++)
                CreateMeshCollider(convexTo, cookingOptions, meshes[i]);

            manager.UnFreezeParent(this, callerName);
        }

        private async Task CalcSingleMeshCollider(bool sync, int maxFrames = -1)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.CheckExit()) return;

            bool convexTo = ShouldConvex();

#if UNITY_6000_3_OR_NEWER
            var id = meshFilter.sharedMesh.GetEntityId();
#else
            var id = meshFilter.sharedMesh.GetInstanceID();
#endif
            
            JobHandle handle = new BakeSingleMeshJob
            {
                meshId = id,
                convex = convexTo,
                options = cookingOptions
            }.Schedule();

            await WaitJobComplete(handle, sync, maxFrames);
            if (manager.CheckExit()) return;

            string callerName = $"Calc Single Mesh: {uniqueId.ToString()}";
            manager.FreezeParent(this, callerName);

            UpdateLiveVars(true);

            ReturnColliderHolderToPool(false);
            CreateMeshCollider(convexTo, cookingOptions, meshFilter.sharedMesh);

            manager.UnFreezeParent(this, callerName);
        }

        public void CreateMeshCollider(bool convexTo, MeshColliderCookingOptions cookingOptionsTo, Mesh meshTo)
        {
            BoxColliderHolder holder = (BoxColliderHolder)BoxCutterPoolInstance.GetPooledObj(BoxCutterPrefabEnum.MeshCollider);

            SetUpHolder(holder);

            MeshCollider meshCollider = holder.meshCollider;

            meshCollider.cookingOptions = cookingOptionsTo;
            meshCollider.convex = convexTo;
            try
            {
                meshCollider.sharedMesh = meshTo;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            holder.obj.parent = obj;

            holder.obj.position = pos;
            holder.obj.rotation = new Quaternion(qx, qy, qz, qw);
            holder.obj.localScale = vecOne;

            holder.gameObj.layer = gameObj.layer;
            holder.tag = gameObj.tag;

            boxColliderHolderList.Add(holder);
        }

        public bool ShouldConvex()
        {
            //return connectionState != ConnectionStateEnum.Anchored || isDynamic;
            return connectionState == ConnectionStateEnum.Disconnected || isDynamic;
            //return true;
        }

        public void CalcBoxColliders()
        {
            string callerName = $"Calc Box: {uniqueId.ToString()}";
            BoxCutterManagerInstance.FreezeParent(this, callerName);

            ReturnColliderHolderToPool(false);

            SubBox[] colSubArr;
            int totalColCount;

            UpdateLiveVars(true);

            bool ignoreColorCollider = CalcIfColorDistributionIsDiverse();

            if ((forceIgnoreColorCollider && totalColors >= 2) || ignoreColorCollider)
            {
                // Use new KD-tree accelerated merge approach - much more efficient!
                colSubArr = MergeSubBoxesUsingKdTree();
                totalColCount = colSubArr.Length;

                // Fallback to original SubBoxes if merge produced no results
                if (totalColCount == 0)
                {
                    int subCnt = allSubList.Count;
                    colSubArr = new SubBox[subCnt];
                    allSubList.CopyTo(colSubArr);
                    totalColCount = subCnt;
                }
            }
            else
            {
                totalColCount = allSubList.Count;
                colSubArr = new SubBox[allSubList.Count];
                allSubList.CopyTo(colSubArr);
            }

            BoxCutterObjectPool objectPool = BoxCutterPoolInstance;
            Quaternion quatRot = new Quaternion(qx, qy, qz, qw);
            var plan = new List<(BoxCutterOPSetting setting, int capacity)>();
            int remaining = totalColCount;

            foreach (var (_, setting, cap) in activeBoxColliderHolderMap)
            {
                int fullHolders = remaining / cap;
                for (int i = 0; i < fullHolders; i++)
                {
                    plan.Add((setting, cap));
                }

                remaining -= fullHolders * cap;
            }

            if (remaining > 0)
            {
                var lastEntry = activeBoxColliderHolderMap[^1];
                plan.Add((lastEntry.opSetting, lastEntry.capacity));
            }

            int processed = 0;

            LayerMask thisLayer = gameObj.layer;
            string thisTag = gameObj.tag;

            foreach (var (setting, cap) in plan)
            {
                BoxColliderHolder holder = (BoxColliderHolder)objectPool.GetPooledObj(setting);

                SetUpHolder(holder);
                holder.obj.position = centerPos;
                holder.obj.rotation = quatRot;
                holder.obj.parent = obj;

                holder.gameObj.layer = thisLayer;
                holder.tag = thisTag;

                boxColliderHolderList.Add(holder);

                for (int slot = 0; slot < cap; slot++)
                {
                    int indexTo = processed + slot;

                    SubBox subBox = colSubArr[indexTo];
                    GetSubObjPositionAndSize(subBox.minX, subBox.minY, subBox.minZ, subBox.maxX, subBox.maxY, subBox.maxZ, out float worldPosX, out float worldPosY, out float worldPosZ, out float worldSizeX, out float worldSizeY, out float worldSizeZ);

                    Transform colObj = holder.boxColObjArr[slot];
                    colObj.position = new Vector3(worldPosX, worldPosY, worldPosZ);
                    colObj.localScale = new Vector3(worldSizeX, worldSizeY, worldSizeZ);

                    GameObject colGameObj = colObj.gameObject;
                    colGameObj.layer = thisLayer;
                    colGameObj.tag = thisTag;
                }

                processed += cap;
            }

            BoxCutterManagerInstance.UnFreezeParent(this, callerName);
        }

        private void SetUpHolder(BoxColliderHolder holder)
        {
            holder.boxObj = this;
            holder.pooled = false;
        }

        /// <summary>
        /// Analyzes color distribution across KD-tree cells to determine collider optimization strategy.
        /// Returns true if color distribution is diverse enough to benefit from greedy merging.
        /// Uses Simpson's Index to measure color dominance within spatial regions.
        /// </summary>
        /// <returns>True if diverse color distribution detected, false if colors are spatially concentrated</returns>
        private bool CalcIfColorDistributionIsDiverse()
        {
            if (totalColors < 2 || gridCellLength <= 1)
                return false;

            int cellIsTopColor = 0;

            for (int i = 0; i < gridCellLength; i++)
            {
                int[] colorDistribution = new int[255];
                var kdCell = gridCellDataArr[i];
                int startRange = kdCell.rangeStart;
                int endRange = kdCell.rangeEnd;
                int totalPixels = endRange - startRange;

                if (totalPixels <= 0)
                    continue;

                for (int v = startRange; v < endRange; v++)
                {
                    var sub = allSubList[gridOriginalIndicesArr[v]];
                    colorDistribution[sub.magicaIndex]++;
                }

                double sumSq = 0.0;
                for (int c = 0; c < colorDistribution.Length; c++)
                {
                    int count = colorDistribution[c];
                    if (count > 0)
                    {
                        double p = count / (double)totalPixels;
                        sumSq += p * p;
                    }
                }

                double simpsonIndex = sumSq;

                if (simpsonIndex >= 0.7)
                {
                    cellIsTopColor++;
                }
            }

            if (cellIsTopColor / (float)gridCellLength < 0.7f)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Merges SubBoxes using optimal global greedy algorithm for best collider generation results.
        /// Uses spatial hashing for fast adjacency checking while ensuring optimal merge solutions.
        /// Ignores magicaIndex colors and merges all adjacent SubBoxes for simplified collision geometry.
        /// </summary>
        /// <returns>Array of optimally merged SubBoxes for collider generation</returns>
        private SubBox[] MergeSubBoxesUsingKdTree()
        {
            int subCount = allSubList.Count;
            if (subCount == 0)
            {
                return new SubBox[0];
            }

            // Convert SubBox list to native array for job processing
            SubBox[] subBoxArray = new SubBox[subCount];
            allSubList.CopyTo(subBoxArray);
            using var allSubBoxesNa = new NativeArray<SubBox>(subBoxArray, Allocator.TempJob);

            // Create output collection with sufficient capacity
            using var mergedSubBoxesNl = new NativeList<SubBox>(subCount, Allocator.TempJob);

            // Execute global greedy merging job for optimal results
            var mergeJob = new SubMerge
            {
                AllSubBoxes = allSubBoxesNa,
                MergedOutput = mergedSubBoxesNl
            };

            // Execute as single job for global optimization
            mergeJob.Schedule().Complete();

            // Convert results back to managed array
            SubBox[] resultArray = new SubBox[mergedSubBoxesNl.Length];
            mergedSubBoxesNl.AsArray().CopyTo(resultArray);

            return resultArray;
        }

        #endregion

        //─────────────────────────────────────────────────────────────────────────────────────
        //                             Object Lifecycle & Event Handling
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers the OnHit event when this BoxObj receives damage.
        /// Used by destruction systems to notify listeners of impact events.
        /// </summary>
        /// <param name="callerData">Destruction event data containing impact information</param>
        public void FireOnHit(ref CallerData callerData)
        {
            OnHit?.Invoke(this, callerData);
        }

        /// <summary>
        /// Triggers the OnFragmentsCreated event when fragments are created from this BoxObj.
        /// Used by destruction systems to notify listeners of fragment creation.
        /// </summary>
        /// <param name="createdFragments">Array of created fragment data</param>
        public void FireOnFragmentsCreated(BoxCutterWorldPhysics.CreatedBoxData[] createdFragments)
        {
            OnFragmentsCreated?.Invoke(this, createdFragments);
        }

        public Coroutine returnToPoolCor;

        /// <summary>
        /// Schedules delayed return to object pool after specified time.
        /// Cancels any existing return coroutine to prevent duplicate pooling.
        public void StartReturnToPoolDelay()
        {
            if (!hasReturnDelay) return;
            if (returnToPoolCor != null) StopCoroutine(returnToPoolCor);
            returnToPoolCor = StartCoroutine(ReturnToPoolDelay(returnDelay));
        }

        /// <summary>
        /// Cancels any existing return coroutine to prevent duplicate pooling.
        /// </summary>
        public void StopReturnToPool()
        {
            if (returnToPoolCor != null) StopCoroutine(returnToPoolCor);
        }

        /// <summary>
        /// Coroutine implementation for returning disconnected Box's back to the pool.
        /// </summary>
        /// <param name="delay">Wait time in seconds</param>
        /// <returns>Coroutine enumerator</returns>
        private IEnumerator ReturnToPoolDelay(float delay)
        {
            // Wait at least one frame before returning, to ensure Attached Detection has set its new Anchoring state is set 
            yield return null;

            yield return new WaitForSeconds(delay);
            if (connectionState == ConnectionStateEnum.Disconnected)
            {
                if (parentHolder != null)
                {
                    int childrenCount = parentHolder.childrenBoxes.Length;
                    for (int i = 0; i < childrenCount; i++)
                    {
                        BoxObj child = parentHolder.childrenBoxes[i];
                        if (child != null && child.uniqueId != uniqueId)
                        {
                            child.ReturnToPool();
                        }
                    }
                }

                ReturnToPool();
            }
        }

        /// <summary>
        /// Completely returns this BoxObj to the object pool for reuse.
        /// Cleans up all state, spatial assignments, and parent relationships.
        /// </summary>
        public void ReturnToPool()
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            if (pooled || manager.CheckExit()) return;
            pooled = true;

            RemoveFromPartition();
            DestroyChildTrans();
            DeActivate();
            RemoveFromActive();
            ResetBox();

            BoxCutterPoolInstance.ReturnObjToPool(this, BoxCutterPrefabEnum.BoxcutterObj);

            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Deactivates this BoxObj without fully resetting state.
        /// Removes from spatial grid and returns collider holders to pool.
        /// </summary>
        public void DeActivate(bool fromOnDestroy = false)
        {
            RemoveFromPartition();
            ReturnColliderHolderToPool();

            if (!fromOnDestroy) meshRend.enabled = false;
            StopAllCoroutines();
        }

        /// <summary>
        /// Removes physics components that conflict with current anchoring state.
        /// Cleans up existing colliders and rigidbodies to prevent interference.
        /// </summary>
        public void RemovePreExistingPhysics()
        {
            int existingCollidersLength = existingColliders.Length;
            if (existingCollidersLength > 0)
            {
                for (int i = 0; i < existingCollidersLength; i++)
                {
                    Destroy(existingColliders[i]);
                }
            }

            existingColliders = new Collider[] { };

            if (connectionState == ConnectionStateEnum.Disconnected && rb != null) Destroy(rb);
        }

        /// <summary>
        /// Resets BoxObj to initial state for object pool reuse.
        /// Clears all cached data and restores default property values.
        /// </summary>
        private void ResetBox()
        {
            // Just in case, make sure mesh is enabled
            if (IsPlaying)
            {
                meshRend.enabled = true;
                RemovePreExistingPhysics();
            }

            mats.Clear();
            allSubList.Clear();
            boxColliderHolderList.Clear();
            cellDataList.Clear();

            canShowGizmos = false;

            kdCellLength = 0;
            kdLeafLength = 0;
            kdCellDataArr = new CellData[] { };
            kdOriginalIndicesArr = new int[] { };

            gridCellLength = 0;
            gridLeafLength = 0;
            gridCellDataArr = new CellData[] { };
            gridOriginalIndicesArr = new int[] { };

            connectionState = ConnectionStateEnum.Connected;

            parentHolder = null;

            canCusVoxelSize = false;
            parentIsDynamic = false;
            isDynamic = false;

            lastPosX = Infinity;
            lastPosY = Infinity;
            lastPosZ = Infinity;

            lastRotX = Infinity;
            lastRotY = Infinity;
            lastRotZ = Infinity;
            lastRotW = Infinity;

            lastScaleX = Infinity;
            lastScaleY = Infinity;
            lastScaleZ = Infinity;

            magicaLocalPosOffsetX = 0;
            magicaLocalPosOffsetY = 0;
            magicaLocalPosOffsetZ = 0;

            pos = vecZero;
            centerPos = vecZero;
            qx = 0;
            qy = 0;
            qz = 0;
            qw = 1;
            sizeX = 0;
            sizeY = 0;
            sizeZ = 0;

            voxelSize = defVoxelSize;
            voxelSize3D = defVoxelSize3D;
            cusVoxelSize = 0.5f;
            voxelResolution = 1;

            uniqueId = -1;
            inheritableId = 0;
            fellInTurn = 0;

            canWlAttach = false;
            wlAttachedBox.Clear();
            wlAttachedBoxInheritId.Clear();

            canRequireBoxes = false;
            requiredBoxes.Clear();
            requiredBoxesInheritId.Clear();

            canCusRequireLogic = false;
            cusRequireLogic = null;

            canInheritChildTrans = false;
            inheritChildDataList.Clear();

            group = null;

            canCustomData = false;
            customDataList.Clear();

            callerStrengthMul = 1f;

            canAdvancedFrag = false;
            fragSettings = null;

            hasReturnDelay = false;
            returnDelay = 0;

            canOverrideDiagInIsland = false;
            diagInIslandTo = false;
            canOverrideKdCellTightness = false;
            kdCellTightnessTo = 0.5f;

            quadRects = new QuadRect[] { };
            
            //destroying = false;
            queuedCallers.Clear();
            BoxCutterManagerInstance.onDestroyFinish -= RunDestroy;
            
            ready = false;
        }

        /// <summary>
        /// Removes the Box from the manager's active debris
        /// </summary>
        public void RemoveFromActive()
        {
            if (connectionState == ConnectionStateEnum.Disconnected)
            {
                BoxCutterManagerInstance.UnRegisterActiveDebris(this);
            }

            RemoveFromGroup();
        }

        /// <summary>
        /// Removes the Box from the group
        /// </summary>
        public void RemoveFromGroup()
        {
            if (group == null) return;

            group.RemoveNullBoxes();
            int boxCount = group.boxList.Count;

            for (int i = 0; i < boxCount; i++)
            {
                if (group.boxList[i].uniqueId == uniqueId)
                {
                    group.boxList.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Returns all collider holder objects to the object pool for reuse.
        /// Optionally preserves physics velocity during the transition.
        /// </summary>
        /// <param name="togglePhysics">Whether to temporarily disable physics during return</param>
        public void ReturnColliderHolderToPool(bool togglePhysics = true)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;

            if (togglePhysics) manager.FreezeParent(this);

            int holder = boxColliderHolderList.Count;
            for (int i = 0; i < holder; i++)
            {
                BoxColliderHolder boxColliderHolder = boxColliderHolderList[i];
                manager.ReturnColliderHolder(boxColliderHolder);
            }

            boxColliderHolderList.Clear();

            if (togglePhysics) manager.UnFreezeParent(this);
        }

        /// <summary>
        /// Destroys any children attached to this box
        /// </summary>
        public void DestroyChildTrans()
        {
            int inheritChildCount = inheritChildDataList.Count;

            for (int i = 0; i < inheritChildCount; i++)
            {
                InheritChildData inheritChildData = inheritChildDataList[i];
                if (inheritChildData.childTrans == null) continue;
                Destroy(inheritChildData.childTrans.gameObject);
            }
        }

        /// <summary>
        /// Updates all cached transform and spatial data for current frame.
        /// Refreshes coordinate system vectors and spatial grid assignment.
        /// </summary>
        public void UpdateLiveVars(bool calledByUpdate = false, bool force = false)
        {
            if (!force && !calledByUpdate && fallUpdateAlready || CheckExit(0)) return;
            RefreshLocalDirs();
            RefreshPosRot();
            RefreshTrueStartPos();

            if (IsPlaying)
            {
                CalcPartitionLocation();
                if (!calledByUpdate) fallUpdateAlready = true;
            }
        }

        public void GetSubObjPositionAndSize(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, out float outPosX, out float outPosY, out float outPosZ, out float outSizeX, out float outSizeY, out float outSizeZ)
        {
            float lSizeX = (maxX - minX) * voxelSize;
            float lSizeY = (maxY - minY) * voxelSize;
            float lSizeZ = (maxZ - minZ) * voxelSize;

            outSizeX = lSizeX < 0 ? -lSizeX : lSizeX;
            outSizeY = lSizeY < 0 ? -lSizeY : lSizeY;
            outSizeZ = lSizeZ < 0 ? -lSizeZ : lSizeZ;

            float midX = (maxX + minX) * 0.5f;
            float midY = (maxY + minY) * 0.5f;
            float midZ = (maxZ + minZ) * 0.5f;

            outPosX = startPosX + vRightX * midX
                                + vUpX * midY
                                + vFwdX * midZ;

            outPosY = startPosY + vRightY * midX
                                + vUpY * midY
                                + vFwdY * midZ;

            outPosZ = startPosZ + vRightZ * midX
                                + vUpZ * midY
                                + vFwdZ * midZ;
        }

        public DestructionPipeline.BoxTrans CreateBoxData()
        {
            return new DestructionPipeline.BoxTrans
            {
                voxelSize = voxelSize,
                posX = centerPosX, posY = centerPosY, posZ = centerPosZ,
                rotX = qx, rotY = qy, rotZ = qz, rotW = qw,
                qx = qx, qy = qy, qz = qz, qw = qw,
                iqx = iqx, iqy = iqy, iqz = iqz, iqw = iqw,
                startPosX = startPosX, startPosY = startPosY, startPosZ = startPosZ,
                rightX = rightX, rightY = rightY, rightZ = rightZ,
                upX = upX, upY = upY, upZ = upZ,
                fwdX = fwdX, fwdY = fwdY, fwdZ = fwdZ,
                vRightX = vRightX, vRightY = vRightY, vRightZ = vRightZ,
                vUpX = vUpX, vUpY = vUpY, vUpZ = vUpZ,
                vFwdX = vFwdX, vFwdY = vFwdY, vFwdZ = vFwdZ,
                id = uniqueId,
                sizeX = sizeX, sizeY = sizeY, sizeZ = sizeZ,
                callerStrengthMul = callerStrengthMul,
                magicaOffsetX = magicaLocalPosOffsetX, magicaOffsetY = magicaLocalPosOffsetY, magicaOffsetZ = magicaLocalPosOffsetZ,
                lossyScale = lastLossyScale,
            };
        }

        private void OnDrawGizmos()
        {
            Color ogGizmosColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;

            if (!canShowGizmos) return;

            float sizeOffset = 0.0001f;
            Gizmos.color = boxCutterPrimaryColor.WithA(1f);

            if (showGridCells)
                DrawLeaf(gridOriginalIndicesArr, gridCellDataArr, gridCellLength, gridCellIndex);
            else
                DrawLeaf(kdOriginalIndicesArr, kdCellDataArr, kdCellLength, kdCellIndex);
            if (IsPlaying)
            {
                if (canShowPartitions)
                    BoxCutterSpatialGridInstance.DrawCells(cellDataList.ToArray(), canShowBoxInPartition: true);
            }
            else
            {
                GetComps();
                UpdateLiveVars();
            }

            if (canInheritChildTrans)
            {
                int childTransCount = inheritChildDataList.Count;
                for (int i = 0; i < childTransCount; i++)
                {
                    InheritChildData childData = inheritChildDataList[i];

                    if (childData.childTrans == null) continue;
                    BoxCutterGizmosUtil.DrawBox(boxCutterSecColor,
                        childData.childTrans.position + childData.boundPosOffset,
                        Quaternion.Euler(childData.childTrans.eulerAngles + childData.boundRotOffset),
                        childData.boundScale + new Vector3(sizeOffset, sizeOffset, sizeOffset));
                }
            }
            
            /*Color queueColor = BoxCutterManagerInstance.GetColorForGroup(inheritableId).WithA(1.0f / queuedCallers.Count);
            foreach (QueuedDestroyData destroyData in queuedCallers)
            {
                float3 worldPos = obj.TransformPoint(destroyData.localPos);

                if (destroyData.callerData.boxMode)
                {
                    Quaternion worldRot = obj.TransformRotation(destroyData.localRot);
                    BoxCutterGizmosUtil.DrawBox(queueColor, worldPos, worldRot, destroyData.callerData.boxBounds);   
                }
                else
                {
                    BoxCutterGizmosUtil.DrawSphere(queueColor, worldPos, destroyData.callerData.radius);
                }
            }*/

            Gizmos.color = ogGizmosColor;
            Gizmos.matrix = ogMatrix;
        }

        private void DrawLeaf(int[] ogIndicesArr, CellData[] cellDataArr, int cellLength, int index)
        {
            Color boxColorTo = new Color(boxCutterPrimaryColor.r, boxCutterPrimaryColor.g, boxCutterPrimaryColor.b, 0.75f);
            Gizmos.color = boxColorTo;

            Color whiteTrans = new Color(1, 1, 1, 0.5f);

            Quaternion quatRot = new Quaternion(qx, qy, qz, qw);
            float sizeOffset = 0.0001f;
            bool showOnlyIndex = index != -1;

            if (index == -1)
            {
                int allSubCount = allSubList.Count;
                /*int subMeshLength = subMeshDataArr.Length;

                // Draw the actual mesh wireframe based on subMeshDataArr
                for (int i = 0; i < subMeshLength; i++)
                {
                    Gizmos.color = Color.cyan;
                    DrawSubMesh(subMeshDataArr[i]);
                }*/

                // Optionally draw the full SubBox bounds for reference
                //Gizmos.color = new Color(boxCutterPrimaryColor.r, boxCutterPrimaryColor.g, boxCutterPrimaryColor.b, 0.3f);
                for (int i = 0; i < allSubCount; i++)
                {
                    SubBox subBox = allSubList[i];
                    GetSubObjPositionAndSize(subBox.minX, subBox.minY, subBox.minZ, subBox.maxX, subBox.maxY, subBox.maxZ, out float subPosX, out float subPosY, out float subPosZ, out float subSizeX, out float subSizeY, out float subSizeZ);
                    BoxCutterGizmosUtil.DrawBox(boxCutterPrimaryColor, new Vector3(subPosX, subPosY, subPosZ), quatRot, new Vector3(subSizeX + sizeOffset, subSizeY + sizeOffset, subSizeZ + sizeOffset));
                    
                    //DrawSubMesh(subBox);
                }
            }

            //return;

            for (int i = 0; i < cellLength; i++)
            {
                if (showOnlyIndex)
                {
                    i = index;

                    CellData kdCell = cellDataArr[index];
                    int startRange = kdCell.rangeStart;
                    int endRange = kdCell.rangeEnd;

                    for (int v = startRange; v < endRange; v++)
                    {
                        var sub = allSubList[ogIndicesArr[v]];

                        GetSubObjPositionAndSize(
                            sub.minX, sub.minY, sub.minZ,
                            sub.maxX, sub.maxY, sub.maxZ,
                            out float px, out float py, out float pz,
                            out float sx, out float sy, out float sz
                        );

                        BoxCutterGizmosUtil.DrawBox(boxColorTo, new Vector3(px, py, pz), quatRot, new Vector3(sx + sizeOffset, sy + sizeOffset, sz + sizeOffset), false);
                    }
                }

                Gizmos.color = i == index ? boxCutterThirdColor : whiteTrans;
                var cell = cellDataArr[i];
                GetSubObjPositionAndSize(cell.minX, cell.minY, cell.minZ, cell.maxX, cell.maxY, cell.maxZ, out float cpx, out float cpy, out float cpz, out float csx, out float csy, out float csz);

                Gizmos.matrix = Matrix4x4.TRS(new Vector3(cpx, cpy, cpz), quatRot, vecOne);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(csx + sizeOffset, csy + sizeOffset, csz + sizeOffset));

                if (showOnlyIndex)
                    break;
            }
        }

        void DrawSubMesh(SubBox sb)
        {
            // Common color for all faces
            Color sharedColor = new Color(0f, 0.5f, 1f, 0.4f); // Semi-transparent Blue
            Gizmos.color = sharedColor;

            // Iterate through all quads for this SubBox
            for (int i = sb.quadStart; i < sb.quadEnd; i++)
            {
                QuadRect quad = quadRects[i];
                DrawQuadFace(quad, sb.minX, sb.minY, sb.minZ);
            }

            void DrawQuadFace(QuadRect quad, int subMinX, int subMinY, int subMinZ)
            {
                // Map QuadRect bounds to 3D coordinates based on direction
                // QuadRect coordinates are in absolute voxel space, not relative to SubBox
                int minX, maxX, minY, maxY, minZ, maxZ;

                switch (quad.dir)
                {
                    case 0: // NegX: U=Z, V=Y
                        minX = subMinX;
                        maxX = subMinX;
                        minY = quad.minV;
                        maxY = quad.maxV;
                        minZ = quad.minU;
                        maxZ = quad.maxU;
                        break;
                    case 1: // PosX: U=Z, V=Y
                        minX = sb.maxX;
                        maxX = sb.maxX;
                        minY = quad.minV;
                        maxY = quad.maxV;
                        minZ = quad.minU;
                        maxZ = quad.maxU;
                        break;
                    case 2: // NegY: U=X, V=Z
                        minX = quad.minU;
                        maxX = quad.maxU;
                        minY = subMinY;
                        maxY = subMinY;
                        minZ = quad.minV;
                        maxZ = quad.maxV;
                        break;
                    case 3: // PosY: U=X, V=Z
                        minX = quad.minU;
                        maxX = quad.maxU;
                        minY = sb.maxY;
                        maxY = sb.maxY;
                        minZ = quad.minV;
                        maxZ = quad.maxV;
                        break;
                    case 4: // NegZ: U=X, V=Y
                        minX = quad.minU;
                        maxX = quad.maxU;
                        minY = quad.minV;
                        maxY = quad.maxV;
                        minZ = subMinZ;
                        maxZ = subMinZ;
                        break;
                    default: // PosZ: U=X, V=Y
                        minX = quad.minU;
                        maxX = quad.maxU;
                        minY = quad.minV;
                        maxY = quad.maxV;
                        minZ = sb.maxZ;
                        maxZ = sb.maxZ;
                        break;
                }

                // Convert voxel integer coordinates to world space position and size
                GetSubObjPositionAndSize(minX, minY, minZ, maxX, maxY, maxZ,
                    out float px, out float py, out float pz,
                    out float sx, out float sy, out float sz);

                Vector3 center = new Vector3(px, py, pz);

                // To handle the flat nature of a face, we ensure no dimension is 0
                // so that Gizmos.DrawCube is actually visible.
                // We add a tiny thickness (0.001f) to the "collapsed" axis.
                Vector3 size = new Vector3(
                    (minX == maxX) ? 0.001f : sx,
                    (minY == maxY) ? 0.001f : sy,
                    (minZ == maxZ) ? 0.001f : sz
                );

                Quaternion rotation = new Quaternion(qx, qy, qz, qw);
                BoxCutterGizmosUtil.DrawBox(sharedColor, center, rotation, size);
            }
        }

        void OnDrawGizmosSelected()
        {
            Color ogGizmosColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;

            if (!canShowGizmos) return;

            // Draw edge from each WhiteListed bone
            if (!IsPlaying)
            {
                int wlCount = wlAttachedBox.Count;
                int reqCount = requiredBoxes.Count;

                for (int i = 0; i < wlCount; i++)
                {
                    var box = wlAttachedBox[i];
                    if (box == null) continue;

                    bool isReq = false;
                    if (canRequireBoxes)
                    {
                        for (int v = 0; v < reqCount; v++)
                        {
                            if (box == requiredBoxes[v])
                            {
                                isReq = true;
                                break;
                            }
                        }
                    }

                    DrawConnection(this, box, isReq ? boxCutterThirdColor : boxCutterPrimaryColor);
                }

                for (int i = 0; i < reqCount; i++)
                {
                    var box = requiredBoxes[i];
                    if (box == null) continue;
                    bool isWl = false;

                    if (canWlAttach)
                    {
                        for (int v = 0; v < wlCount; v++)
                        {
                            if (box == wlAttachedBox[v])
                            {
                                isWl = true;
                                break;
                            }
                        }
                    }

                    if (isWl) continue;

                    DrawConnection(this, box, boxCutterThirdColor);
                }
            }

            Gizmos.color = ogGizmosColor;
            Gizmos.matrix = ogMatrix;
        }

        void DrawConnection(BoxObj parentBone, BoxObj childBone, Color color)
        {
            if (parentBone == null) return;
            var parentPos = parentBone.transform.position;
            var childPos = childBone.transform.position;
            float voxel = GetBoneVoxelSize(childBone);
            var center = (parentPos + childPos) * 0.5f;
            var dir = (childPos - parentPos).normalized;
            float len = Vector3.Distance(parentPos, childPos);
            var rot = len > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            var boxSize = new Vector3(voxel, voxel, len);
            BoxCutterGizmosUtil.DrawBox(color, center, rot, boxSize, true);
        }

        private float GetBoneVoxelSize(BoxObj bone)
        {
            if (bone == null) return defVoxelSize / 2.0f;
            if (bone.canCusVoxelSize && bone.cusVoxelSize > 0) return bone.cusVoxelSize / 2.0f;
            if (bone.voxelSize > 0) return bone.voxelSize / 2.0f;
            return defVoxelSize / 2.0f;
        }
    }
}
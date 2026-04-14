using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using static BoxCutter.BoxcutterEditorUtil;

namespace BoxCutter
{
    [InitializeOnLoad]
    public class BoxCutterWelcomeWindow : EditorWindow
    {
        private const string WELCOME_KEY_BASE = "BoxCutter_WelcomeShown";
        private static string ProjectScopedKey => $"{WELCOME_KEY_BASE}_{Application.dataPath}";

        private Texture2D customIcon;
        private Vector2 scrollPosition;

        private static ListRequest _listRequest;
        private static AddRequest _addRequest;

        static BoxCutterWelcomeWindow()
        {
            EditorApplication.delayCall += ShowWelcomeIfFirstTime;
            EditorApplication.delayCall += CheckAndInstallDependencies;
        }

        private static void ShowWelcomeIfFirstTime()
        {
            if (!EditorPrefs.GetBool(ProjectScopedKey, false))
            {
                ShowWindow();
            }
        }

        // Check pipeline and install Shader Graph if needed
        private static void CheckAndInstallDependencies()
        {
            if (Extend.GetCurrentRenderPipeline() == Extend.RenderPipelineType.BuiltIn)
            {
                _listRequest = Client.List();
                EditorApplication.update += ProgressPackageCheck;
            }
        }

        private static void ProgressPackageCheck()
        {
            if (_listRequest != null && _listRequest.IsCompleted)
            {
                if (_listRequest.Status == StatusCode.Success)
                {
                    bool hasShaderGraph = false;
                    foreach (var package in _listRequest.Result)
                    {
                        if (package.name == "com.unity.shadergraph")
                        {
                            hasShaderGraph = true;
                            break;
                        }
                    }

                    if (!hasShaderGraph)
                    {
                        Debug.Log("BoxCutter: Built-in Render Pipeline detected. Shader Graph is missing. Installing automatically...");
                        _addRequest = Client.Add("com.unity.shadergraph");
                    }
                    else
                    {
                        EditorApplication.update -= ProgressPackageCheck;
                    }
                }
                else
                {
                    if (_listRequest.Error != null)
                        Debug.LogWarning($"BoxCutter: Failed to check packages. Error: {_listRequest.Error.message}");
                    
                    EditorApplication.update -= ProgressPackageCheck;
                }
                _listRequest = null;
            }

            if (_addRequest != null && _addRequest.IsCompleted)
            {
                if (_addRequest.Status == StatusCode.Success)
                {
                    Debug.Log("BoxCutter: Shader Graph package installed successfully.");
                }
                else if (_addRequest.Error != null)
                {
                    Debug.LogError($"BoxCutter: Failed to install Shader Graph. Error: {_addRequest.Error.message}");
                }
                
                _addRequest = null;
                EditorApplication.update -= ProgressPackageCheck;
            }
        }

        [MenuItem("Window/BoxCutter/Welcome")]
        public static void ShowWindow()
        {
            var window = GetWindow<BoxCutterWelcomeWindow>(true, "BoxCutter Welcome", true);
            window.minSize = new Vector2(500, 630);
            window.maxSize = new Vector2(500, 630);

            // Center roughly on the primary display
            var pos = window.position;
            var screen = new Rect(0f, 0f, Screen.currentResolution.width, Screen.currentResolution.height);
            pos.center = screen.center;
            window.position = pos;

            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EditorPrefs.SetBool(ProjectScopedKey, true);
            customIcon = LoadLocalAsset("BoxcutterIcon.png");
        }

        /*[MenuItem("Window/BoxCutter/Reset Welcome")]
        public static void ResetWelcomeWindow()
        {
            EditorPrefs.DeleteKey(ProjectScopedKey);
            Debug.Log("BoxCutter: Welcome window reset for THIS project. It will show again on next domain reload.");
        }*/

        private void ImportRenderPipelinePackage(Extend.RenderPipelineType type)
        {
            string packageName = type switch
            {
                Extend.RenderPipelineType.URP => "URP Package.unitypackage",
                Extend.RenderPipelineType.HDRP => "HDRP Packpage.unitypackage",
                _ => "Built In Package.unitypackage"
            };

            string packagePath = $"Assets/BoxCutter/DemoScenes/Packages/{packageName}";

            if (System.IO.File.Exists(packagePath))
            {
                AssetDatabase.ImportPackage(packagePath, true);
                string pipelineName = type == Extend.RenderPipelineType.BuiltIn ? "Built In" : type.ToString();
                Debug.Log($"Importing {pipelineName} package from: {packagePath}");
            }
            else
            {
                Debug.LogWarning($"Package not found at: {packagePath}");
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle();
            DrawBanner(customIcon, "WELCOME");
            DrawHorizontalDivider();

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Thank you for purchasing BoxCutter!", HeaderTitleStyle);
            GUILayout.Space(5);
            EditorGUILayout.LabelField(
                "Whether you are prototyping, crafting cinematic moments, or pushing new gameplay ideas, " +
                "BoxCutter is designed to stay fast, flexible, and reliable to create the best possible experience for you and your players.",
                BodyTextStyle
            );

            DrawHorizontalDivider(1, 8);

            var style = new GUIStyle(BodyTextStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(20, 10, 2, 2)
            };
            
            /*EditorGUILayout.LabelField("KEY FEATURES", HeaderTitleStyle);
            GUILayout.Space(5);

            string[] features = {
                "• Lightning Fast Voxel Destruction Pipeline",
                "• Real-time Fragmentation with vast customization options",
                "• Realistic Fragment Physics",
                "• Customizable Physics",
                "• Sphere and Cube Carving",
                "• Variable Voxel Size Per Object",
                "• Support For Rigs",
                "• MagicaVoxel Integration"
            };

            foreach (string feature in features)
            {
                EditorGUILayout.LabelField(feature, style);
                GUILayout.Space(2);
            }

            DrawHorizontalDivider(1, 8);*/

            EditorGUILayout.LabelField("GETTING STARTED", HeaderTitleStyle);
            GUILayout.Space(5);

            string[] steps = {
                "1. Check out the Demo Scene in Assets/BoxCutter/DemoScene/Scenes",
                "2. Drag and drop the BoxCutterManagers prefab to your scene",
                "3. Add BoxObj components to any object you want to destroy",
                "4. Use BoxCutterCaller to trigger destruction",
                "5. Customize fragmentation with FragSettings assets",
                "Read the full documentation for more information"
            };

            foreach (string step in steps)
            {
                EditorGUILayout.LabelField(step, style);
                GUILayout.Space(2);
            }

            DrawHorizontalDivider(1, 8);

            EditorGUILayout.LabelField("SUPPORT", HeaderTitleStyle);
            GUILayout.Space(5);
            EditorGUILayout.LabelField("• Matrix: @bitwisegames:matrix.org (Use for fastest support)", style);
            EditorGUILayout.LabelField("• Email: BitwiseGames@tuta.com", style);

            DrawHorizontalDivider(1, 8);

            if (DrawGradientButton("EXPLORE DEMO SCENE"))
            {
                var demoScenePath = "Assets/BoxCutter/DemoScenes/Scenes/Quick Start.unity";
                if (System.IO.File.Exists(demoScenePath))
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(demoScenePath);
                    Close();
                }
                else
                {
                    Debug.LogWarning("Demo scene not found at: " + demoScenePath);
                }
            }

            GUILayout.Space(5);

            Extend.RenderPipelineType currentPipeline = Extend.GetCurrentRenderPipeline();
            string packageName = currentPipeline == Extend.RenderPipelineType.BuiltIn ? "Built In" : currentPipeline.ToString();
            if (DrawGradientButton($"IMPORT {packageName.ToUpper()} PACKAGE"))
            {
                ImportRenderPipelinePackage(currentPipeline);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
    }
}
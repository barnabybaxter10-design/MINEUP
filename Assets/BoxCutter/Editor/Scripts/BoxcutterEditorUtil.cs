using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using System.IO;
using System.Diagnostics;

namespace BoxCutter
{
    public static class BoxcutterEditorUtil
    {
        public const string BoxCutterVersion = "2.15";
        public const string TagLine = "Engineered for Chaos, Perfected for Performance";

        public static readonly Color StartColor = new Color(0.42f, 0.32f, 0.70f, 1f);
        public static readonly Color EndColor = new Color(0.61f, 0.50f, 0.80f, 1f);

        private static Texture2D gradientNormal;
        private static Texture2D gradientHover;
        private static Texture2D gradientActive;
        private static Font cachedCoreFont;

        public static Texture2D LoadLocalAsset(string fileName)
        {
            var stack = new StackTrace(true);
            var frame = stack.GetFrame(1);
            string scriptPath = frame.GetFileName();
            string folder = Path.GetDirectoryName(scriptPath);
            string parent = Path.GetDirectoryName(folder);
            string assetPath = Path.Combine(parent, "Textures", fileName)
                .Replace("\\", "/");
            assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        public static Font LoadLocalFont(string fileName)
        {
            if (fileName == "Core Sans N 65 Bold.ttf" && cachedCoreFont != null)
                return cachedCoreFont;
                
            var stack = new StackTrace(true);
            var frame = stack.GetFrame(1);
            string scriptPath = frame.GetFileName();
            string folder = Path.GetDirectoryName(scriptPath);
            string parent = Path.GetDirectoryName(folder);
            string assetPath = Path.Combine(parent, "Fonts", fileName)
                .Replace("\\", "/");
            assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            
            var font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            if (fileName == "Core Sans N 65 Bold.ttf")
                cachedCoreFont = font;
                
            return font;
        }

        public static void ApplyIcon(Object target, Texture2D icon)
        {
            if (target == null) return;
            EditorGUIUtility.SetIconForObject(target, icon);

            MonoScript mono = null;
            if (target is MonoBehaviour mb)
            {
                mono = MonoScript.FromMonoBehaviour(mb);
            }
            else if (target is ScriptableObject so)
            {
                mono = MonoScript.FromScriptableObject(so);
            }

            if (mono != null)
            {
                EditorGUIUtility.SetIconForObject(mono, icon);
                var path = AssetDatabase.GetAssetPath(mono);
                var imp = AssetImporter.GetAtPath(path) as MonoImporter;
                if (imp != null)
                    imp.SetIcon(icon);
            }
        }

        static GUIStyle _centeredHeader, _noMarginHelpBox, _outerContainer, _headerTitle, _subtitle, _bodyText;

        public static GUIStyle CenteredHeaderStyle =>
            _centeredHeader ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(5, 5, 2, 2),
                normal = { textColor = Color.white }
            };

        public static GUIStyle NoMarginHelpBoxStyle =>
            _noMarginHelpBox ??= new GUIStyle(EditorStyles.helpBox)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(5, 5, 5, 5)
            };

        public static GUIStyle OuterContainerStyle =>
            _outerContainer ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(15, 15, 15, 15),
                margin = new RectOffset(10, 10, 10, 10)
            };

        public static GUIStyle HeaderTitleStyle =>
            _headerTitle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

        public static GUIStyle SubtitleStyle =>
            _subtitle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
            };

        public static GUIStyle BodyTextStyle =>
            _bodyText ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = true,
                padding = new RectOffset(10, 10, 4, 4),
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                alignment = TextAnchor.MiddleCenter
            };

        public static void DrawSeparator(int lines = 3)
        {
            for (int i = 0; i < lines; i++)
                EditorGUILayout.Space();
        }

        public static void DrawHorizontalDivider(
            float thickness = 2f,
            int verticalSpacing = 6,
            float widthPct = 1f
        )
        {
            Rect fullRect = EditorGUILayout.GetControlRect(
                GUILayout.Height(thickness + verticalSpacing * 2));

            widthPct = Mathf.Clamp01(widthPct);
            float dividerWidth = fullRect.width * widthPct;
            float offsetX = (fullRect.width - dividerWidth) * 0.5f;

            var thickDiv = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = (int)thickness,
                margin = new RectOffset(0, 0, verticalSpacing, verticalSpacing)
            };

            Rect drawRect = new Rect(
                fullRect.x + offsetX,
                fullRect.y,
                dividerWidth,
                fullRect.height
            );

            EditorGUI.LabelField(drawRect, GUIContent.none, thickDiv);
        }

        public static void DrawTitle(string desc = "")
        {
            EditorGUILayout.LabelField($"BOXCUTTER V{BoxCutterVersion}", HeaderTitleStyle);
            EditorGUILayout.LabelField(TagLine, SubtitleStyle);
            if (desc != "")
            {
                DrawHorizontalDivider(1);
                EditorGUILayout.LabelField(desc, BodyTextStyle);
                DrawHorizontalDivider(1);
            }
            else
            {
                DrawHorizontalDivider();
            }
        }

        public static void DrawBanner(Texture2D icon, string bannerText = "")
        {
            Font coreSansBold = LoadLocalFont("Core Sans N 65 Bold.ttf");
            int maxFontSize = 45;
            var textStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                font = coreSansBold,
                fontSize = maxFontSize,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            // Calculate width at maximum size
            float letterSpacing = 1f;
            float idealTextWidth = 0f;
            foreach (char c in bannerText)
            {
                idealTextWidth += textStyle.CalcSize(new GUIContent(c.ToString())).x;
            }
            if (bannerText.Length > 1) idealTextWidth += (bannerText.Length - 1) * letterSpacing;

            float iconTextSpacing = 6f;
            
            float idealHeight = maxFontSize * 1.15f; 

            float idealIconWidth = 0f;
            if (icon != null)
            {
                float aspect = icon.height / (float)icon.width;
                idealIconWidth = idealHeight / aspect;
            }

            float totalIdealWidth = (icon != null)
                ? idealIconWidth + iconTextSpacing + idealTextWidth
                : idealTextWidth;
            
            float containerPadding = 80f; 
            float availableWidth = EditorGUIUtility.currentViewWidth - containerPadding;
            
            if (availableWidth < 10) availableWidth = totalIdealWidth;

            float scaleFactor = (totalIdealWidth > availableWidth)
                ? availableWidth / totalIdealWidth
                : 1f;
            
            // Apply Scale
            int finalFontSize = Mathf.FloorToInt(maxFontSize * scaleFactor);
            finalFontSize = Mathf.Max(finalFontSize, 10);
            textStyle.fontSize = finalFontSize;

            float finalLetterSpacing = letterSpacing * scaleFactor;
            float finalIconSpacing = iconTextSpacing * scaleFactor;

            // Recalculate height tightly based on the clamped font size
            float finalHeight = finalFontSize * 1.15f; 

            float finalIconWidth = 0f;
            float finalIconHeight = finalHeight;

            if (icon != null)
            {
                float aspect = icon.height / (float)icon.width;
                finalIconWidth = finalIconHeight / aspect;
            }

            // Recalculate width for centering logic
            float finalTextWidth = 0f;
            for (int i = 0; i < bannerText.Length; i++)
            {
                finalTextWidth += textStyle.CalcSize(new GUIContent(bannerText[i].ToString())).x;
                if (i < bannerText.Length - 1) finalTextWidth += finalLetterSpacing;
            }

            float finalTotalWidth = (icon != null)
                ? finalIconWidth + finalIconSpacing + finalTextWidth
                : finalTextWidth;

            // Draw
            Rect groupRect = EditorGUILayout.GetControlRect(false, finalHeight);

            float startX = Mathf.Round(groupRect.x + (groupRect.width - finalTotalWidth) * 0.5f);
            float centerY = groupRect.y + (groupRect.height * 0.5f);

            // Draw Icon
            if (icon != null)
            {
                float iconY = centerY - (finalIconHeight * 0.5f);
                Rect iconRect = new Rect(startX, iconY, finalIconWidth, finalIconHeight);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }

            // Draw Text
            float textStartX = startX + ((icon != null)
                ? finalIconWidth + finalIconSpacing
                : 0f);

            float textY = centerY - (finalHeight * 0.5f);
            
            textY += (2f * scaleFactor); 

            float xPos = textStartX;

            for (int i = 0; i < bannerText.Length; i++)
            {
                string s = bannerText[i].ToString();
                Vector2 charSize = textStyle.CalcSize(new GUIContent(s));

                Rect charRect = new Rect(xPos, textY, charSize.x, charSize.y);
                EditorGUI.LabelField(charRect, s, textStyle);

                xPos += charSize.x + finalLetterSpacing;
            }
        }

        public static Texture2D GenerateVerticalGradient(int w, int h, Color c1, Color c2)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                tex.SetPixel(0, y, Color.Lerp(c1, c2, t));
            }

            tex.Apply();
            return tex;
        }

        public static void EnsureGradients(Color sc, Color ec)
        {
            if (gradientNormal == null ||
                gradientHover == null ||
                gradientActive == null)
            {
                float hm = 1.2f, am = 0.8f;
                gradientNormal = GenerateVerticalGradient(1, 50, sc, ec);
                gradientHover = GenerateVerticalGradient(1, 50, sc * hm, ec * hm);
                gradientActive = GenerateVerticalGradient(1, 50, sc * am, ec * am);
            }
        }

        public static void DrawFoldoutSection(
            string title,
            AnimBool anim,
            Color headerColor,
            System.Action content
        )
        {
            Rect hdr = EditorGUILayout.GetControlRect(false, 25);
            EditorGUI.DrawRect(hdr, headerColor);

            string arrow = anim.target ? "▼ " : "► ";
            if (Event.current.type == EventType.MouseDown &&
                hdr.Contains(Event.current.mousePosition))
            {
                anim.target = !anim.target;
                Event.current.Use();
            }

            GUI.Label(hdr, arrow + title, CenteredHeaderStyle);

            if (EditorGUILayout.BeginFadeGroup(anim.faded))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(NoMarginHelpBoxStyle);
                content.Invoke();
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFadeGroup();
        }

        private static readonly Dictionary<string, bool> _pressed = new Dictionary<string, bool>();

        public static bool DrawGradientButton(string label, string key = null)
        {
            key ??= label;

            EnsureGradients(StartColor, EndColor);

            bool wasPressed = _pressed.TryGetValue(key, out var isPressed) && isPressed;

            Rect r = EditorGUILayout.GetControlRect(false, 40);

            bool isHover = r.Contains(Event.current.mousePosition);
            var tex = wasPressed
                ? gradientActive
                : isHover
                    ? gradientHover
                    : gradientNormal;

            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);

            var style = new GUIStyle
            {
                normal = { background = null, textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white },
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            if (GUI.Button(r, label, style))
            {
                _pressed[key] = true;
                EditorApplication.delayCall += () =>
                {
                    _pressed[key] = false;
                    EditorWindow.focusedWindow?.Repaint();
                };
                return true;
            }

            return false;
        }
    }
}
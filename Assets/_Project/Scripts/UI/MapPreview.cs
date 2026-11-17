using System.Collections;
using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 地圖的俯視圖（上北右東）。
    ///
    /// 目前沒有用到：2026-11-17 做過「大廳和選身分畫面右邊放俯視圖」，使用者說先拿掉，地圖之後再想要怎麼呈現。
    /// 程式先留著（用 StartCoroutine(MapPreview.RenderAll()) 拍、MapPreview.Draw 畫），之後要用再接回介面。
    ///
    /// 遊戲一開始（還在大廳、場上沒有任何角色時）把每張地圖各拍一張存起來，之後只拿出來畫，
    /// 所以圖上永遠不會出現玩家的位置。圖上另外標出捕捉籠、收容區和人類出生點。
    /// </summary>
    public static class MapPreview
    {
        // 和測試工具的 camshot 同一個拍法：正上方往下看，正交鏡頭，涵蓋 84 x 63 公尺（地圖 80 x 60）。
        const float k_HalfHeight = 31.5f;
        const float k_Aspect = 4f / 3f;
        const int k_Width = 800;
        const int k_Height = 600;

        public struct Marker
        {
            public Vector3 Position;
            public string Label;
            public Color Color;
        }

        class Entry
        {
            public Texture2D Texture;
            public readonly List<Marker> Markers = new List<Marker>();
        }

        static readonly Dictionary<int, Entry> s_Entries = new Dictionary<int, Entry>();
        static bool s_Rendering;

        static readonly Color k_CageColor = new Color(0.86f, 0.24f, 0.22f);
        static readonly Color k_AnimalColor = new Color(0.45f, 0.25f, 0.68f);
        static readonly Color k_HumanColor = new Color(0.16f, 0.16f, 0.19f);

        /// <summary>
        /// 把每張地圖各拍一張。要在還沒有任何角色的時候呼叫（大廳）；拍的時候會輪流打開每張地圖，最後切回原本選的那張。
        /// </summary>
        public static IEnumerator RenderAll()
        {
            if (s_Rendering)
            {
                yield break;
            }

            s_Rendering = true;
            yield return null;

            MapRegistry registry = MapRegistry.Instance;
            if (registry != null && registry.Count > 0)
            {
                int keep = registry.ActiveIndex;
                for (int i = 0; i < registry.Count; i++)
                {
                    registry.Show(i);
                    Capture(i, registry.maps[i]);
                }

                registry.Show(keep);
            }

            s_Rendering = false;
        }

        static void Capture(int index, MapInfo map)
        {
            Entry entry;
            if (!s_Entries.TryGetValue(index, out entry))
            {
                entry = new Entry();
                s_Entries[index] = entry;
            }

            var cameraObject = new GameObject("Map Preview Camera");
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = k_HalfHeight;
                camera.aspect = k_Aspect;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 200f;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 80f, 0f), Quaternion.Euler(90f, 0f, 0f));

                // 先拍到暫時的 RenderTexture，再讀回一張一般的貼圖存起來。
                // 直接拿 RenderTexture 畫在介面上，顏色會偏白（線性色彩空間的關係）；讀回來的貼圖和截圖一樣是對的。
                target = RenderTexture.GetTemporary(k_Width, k_Height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;

                RenderTexture.active = target;
                if (entry.Texture == null)
                {
                    entry.Texture = new Texture2D(k_Width, k_Height, TextureFormat.RGB24, false);
                    entry.Texture.name = "Map Preview " + index;
                }

                entry.Texture.ReadPixels(new Rect(0f, 0f, k_Width, k_Height), 0, 0);
                CompensateGuiGamma(entry.Texture);
                entry.Texture.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null)
                {
                    RenderTexture.ReleaseTemporary(target);
                }

                Object.Destroy(cameraObject);
            }

            entry.Markers.Clear();
            for (int i = 0; i < CaptureCage.Active.Count; i++)
            {
                CaptureCage cage = CaptureCage.Active[i];
                if (cage != null)
                {
                    entry.Markers.Add(new Marker { Position = cage.transform.position, Label = cage.number.ToString(), Color = k_CageColor });
                }
            }

            if (map != null)
            {
                AddSpawn(entry, map.humanSpawn, Role.Human, k_HumanColor);
                AddSpawn(entry, map.rabbitSpawn, Role.Rabbit, k_AnimalColor);
                AddSpawn(entry, map.monkeySpawn, Role.Monkey, k_AnimalColor);
                AddSpawn(entry, map.raccoonSpawn, Role.Raccoon, k_AnimalColor);
                AddSpawn(entry, map.turtleSpawn, Role.Turtle, k_AnimalColor);
            }
        }

        /// <summary>
        /// 這個專案用線性色彩空間，介面（IMGUI）畫出來的顏色會被當成線性值再轉一次，整體偏白（介面的深色底板也是這樣）。
        /// 地圖預覽要和實際畫面一樣，所以先把每個像素轉成線性值，畫出來就會是原本的顏色。
        /// </summary>
        static void CompensateGuiGamma(Texture2D texture)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
            {
                return;
            }

            var table = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                table[i] = (byte)Mathf.RoundToInt(Mathf.GammaToLinearSpace(i / 255f) * 255f);
            }

            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                pixels[i] = new Color32(table[pixel.r], table[pixel.g], table[pixel.b], 255);
            }

            texture.SetPixels32(pixels);
        }

        static void AddSpawn(Entry entry, Transform point, Role role, Color color)
        {
            if (point == null)
            {
                return;
            }

            string name = GameConfig.Instance.GetRole(role).displayName;
            entry.Markers.Add(new Marker { Position = point.position, Label = name.Substring(0, 1), Color = color });
        }

        /// <summary>
        /// 在 rect 裡畫出第 index 張地圖的俯視圖和標記（保持 4:3，置中）。還沒拍好就什麼都不畫，回傳 false。
        /// </summary>
        public static bool Draw(Rect rect, int index)
        {
            Entry entry;
            if (!s_Entries.TryGetValue(index, out entry) || entry.Texture == null)
            {
                return false;
            }

            float width = Mathf.Min(rect.width, rect.height * k_Aspect);
            float height = width / k_Aspect;
            var image = new Rect(rect.x + (rect.width - width) * 0.5f, rect.y + (rect.height - height) * 0.5f, width, height);
            GUI.DrawTexture(image, entry.Texture, ScaleMode.StretchToFill, false);

            float markerSize = Mathf.Clamp(width / 30f, 14f, 26f);
            Color previous = GUI.color;
            for (int i = 0; i < entry.Markers.Count; i++)
            {
                Marker marker = entry.Markers[i];
                float u = (marker.Position.x + k_HalfHeight * k_Aspect) / (2f * k_HalfHeight * k_Aspect);
                float v = (k_HalfHeight - marker.Position.z) / (2f * k_HalfHeight);
                var box = new Rect(image.x + u * width - markerSize * 0.5f, image.y + v * height - markerSize * 0.5f, markerSize, markerSize);
                GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? marker.Color.linear : marker.Color;
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(box, marker.Label, UiKit.Tag);
            }

            GUI.color = previous;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Entries.Clear();
            s_Rendering = false;
        }
    }
}

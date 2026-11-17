using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 畫面邊緣的聲音提示圖示：一個點加三道往外的弧線（像聲波），用程式畫出來，不需要圖檔。
    /// 圖案朝上（+Y）；畫的時候旋轉到聲音的方向。
    /// </summary>
    public static class SoundCueIcon
    {
        const int k_Size = 64;

        static Texture2D s_Texture;

        public static Texture2D Texture
        {
            get
            {
                if (s_Texture == null)
                {
                    s_Texture = Build();
                }

                return s_Texture;
            }
        }

        static Texture2D Build()
        {
            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var origin = new Vector2(k_Size * 0.5f, 12f);
            float[] radii = { 14f, 26f, 38f };
            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - origin;
                    float distance = offset.magnitude;
                    float alpha = Mathf.Clamp01(5f - distance);

                    // 只畫上方左右各 45 度的弧
                    float angle = Vector2.Angle(Vector2.up, offset);
                    if (angle <= 45f)
                    {
                        for (int i = 0; i < radii.Length; i++)
                        {
                            alpha = Mathf.Max(alpha, Mathf.Clamp01(2.6f - Mathf.Abs(distance - radii[i])));
                        }
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Texture = null;
        }
    }
}

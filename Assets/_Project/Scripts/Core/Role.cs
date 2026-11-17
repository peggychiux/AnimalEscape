using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 玩家可以選的身分。None 表示還沒選（或正在重新選）。
    /// </summary>
    public enum Role : byte
    {
        None = 0,
        Human = 1,
        Rabbit = 2,
        Monkey = 3,
        Raccoon = 4,
        Turtle = 5
    }

    /// <summary>可以破壞的東西的大小（M4）：決定加多少混亂值、聲音傳多遠、要按多久。</summary>
    public enum BreakSize : byte
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    /// <summary>
    /// 一個身分的可調數值。實際數值放在 GameConfig 裡。
    /// </summary>
    [System.Serializable]
    public class RoleSettings
    {
        public string displayName = "";

        [Tooltip("身高（公尺）")]
        public float height = 1.8f;

        [Tooltip("身體半徑（公尺）")]
        public float radius = 0.35f;

        [Tooltip("跑步速度（公尺／秒）")]
        public float moveSpeed = 5f;

        [Tooltip("跳躍高度（公尺）")]
        public float jumpHeight = 1f;

        [Tooltip("第三人稱視角時，鏡頭和角色的距離（公尺）。第一人稱用不到。")]
        public float cameraDistance = 5f;

        public Color color = Color.white;
    }

    public static class Roles
    {
        /// <summary>選身分畫面上列出的身分，依這個順序顯示。</summary>
        public static readonly Role[] Selectable =
        {
            Role.Human,
            Role.Rabbit,
            Role.Monkey,
            Role.Raccoon,
            Role.Turtle
        };

        public static bool IsAnimal(Role role)
        {
            return role == Role.Rabbit || role == Role.Monkey || role == Role.Raccoon || role == Role.Turtle;
        }
    }
}

using System;
using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>一張地圖：場景裡的一組物件，加上它自己的出生點。</summary>
    [Serializable]
    public class MapInfo
    {
        public string displayName = "";

        [Tooltip("一句話說明這張地圖的特色（顯示在大廳）")]
        public string summary = "";

        [Tooltip("這張地圖所有物件的根物件；同一時間只有一張地圖是開著的")]
        public GameObject root;

        public Transform humanSpawn;
        public Transform[] animalSpawns;
    }

    /// <summary>
    /// 場景裡所有可以選的地圖。同一時間只顯示一張：
    /// - 在大廳時，顯示開房的人選的那一張（選擇會記住，下次開遊戲還在）。
    /// - 進房間後，由主機決定（MatchState.MapIndex），所有人都切到同一張。
    /// </summary>
    public class MapRegistry : MonoBehaviour
    {
        const string k_PrefsKey = "AnimalEscape.SelectedMap";

        public static MapRegistry Instance { get; private set; }

        public MapInfo[] maps = new MapInfo[0];

        public int ActiveIndex { get; private set; }

        public int Count
        {
            get { return maps.Length; }
        }

        public MapInfo Active
        {
            get { return maps.Length > 0 ? maps[Wrap(ActiveIndex)] : null; }
        }

        /// <summary>大廳裡選的地圖（開房時會用這一張）。</summary>
        public int SelectedIndex
        {
            get { return Wrap(PlayerPrefs.GetInt(k_PrefsKey, 0)); }
        }

        void Awake()
        {
            Instance = this;
            Show(SelectedIndex);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>把編號轉成合法的範圍（超過就繞回來）。</summary>
        public int Wrap(int index)
        {
            int count = maps.Length;
            return count == 0 ? 0 : ((index % count) + count) % count;
        }

        /// <summary>在大廳選地圖：記住選擇，並馬上切過去預覽。</summary>
        public void Select(int index)
        {
            index = Wrap(index);
            PlayerPrefs.SetInt(k_PrefsKey, index);
            Show(index);
        }

        /// <summary>只顯示指定的那張地圖，其他的關掉。</summary>
        public void Show(int index)
        {
            ActiveIndex = Wrap(index);
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] != null && maps[i].root != null)
                {
                    maps[i].root.SetActive(i == ActiveIndex);
                }
            }
        }
    }
}

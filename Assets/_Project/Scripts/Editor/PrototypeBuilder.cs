using System.Collections.Generic;
using System.IO;
using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using AnimalEscape.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Urp = UnityEngine.Rendering.Universal;

namespace AnimalEscape.EditorTools
{
    /// <summary>
    /// 用程式產生原型需要的場景、預製物、材質與設定檔，不需要在編輯器裡手動拉任何東西。
    /// - 第一次開啟專案時會自動執行一次。
    /// - 之後可從選單 Tools > Animal Escape > 重建原型場景 重新產生。
    /// 場景（Main.unity）、預製物和材質每次都會整個重建，所以要改它們請改這支程式；
    /// 數值設定檔（GameConfig.asset）只在不存在時建立，不會蓋掉調整過的數值。
    /// 新增含 NetworkObject 的預製物時，記得加進 BuildPrefabList。
    /// </summary>
    [InitializeOnLoad]
    public static class PrototypeBuilder
    {
        const string k_Root = "Assets/_Project";
        const string k_ScenesDir = k_Root + "/Scenes";
        const string k_PrefabsDir = k_Root + "/Prefabs";
        const string k_MaterialsDir = k_Root + "/Materials";
        const string k_SettingsDir = k_Root + "/Settings";
        const string k_ConfigDir = k_Root + "/Config";
        const string k_ConfigResourcesDir = k_ConfigDir + "/Resources";

        const string k_ScenePath = k_ScenesDir + "/Main.unity";
        const string k_SlotPrefabPath = k_PrefabsDir + "/PlayerSlot.prefab";
        const string k_AvatarPrefabPath = k_PrefabsDir + "/Avatar.prefab";
        const string k_MatchStatePrefabPath = k_PrefabsDir + "/MatchState.prefab";
        const string k_ConfigPath = k_ConfigResourcesDir + "/GameConfig.asset";
        const string k_PrefabListPath = k_ConfigDir + "/NetworkPrefabs.asset";
        const string k_PipelinePath = k_SettingsDir + "/URP_Pipeline.asset";
        const string k_RendererPath = k_SettingsDir + "/URP_Renderer.asset";

        const string k_AutoBuildKey = "AnimalEscape.PrototypeBuilder.AutoBuildTried";

        // 舊版本留下、現在已經不用的資源，重建時順便清掉。
        static readonly string[] k_ObsoleteAssets =
        {
            k_PrefabsDir + "/Player.prefab",
            k_MaterialsDir + "/PlayerBody.mat",
            k_MaterialsDir + "/PlayerAccent.mat",
            k_MaterialsDir + "/Block.mat",
            k_MaterialsDir + "/Ground.mat",
            k_Root + "/Scripts/Player/PlayerAppearance.cs"
        };

        // 地圖尺寸（公尺）
        const float k_WallHeight = 3f;
        const float k_WallThickness = 0.3f;
        const float k_DoorWidth = 1.6f;
        const float k_DoorHeight = 2.2f;
        const float k_VentSize = 0.8f;

        class Palette
        {
            public Material Floor;
            public Material Wall;
            public Material OuterWall;
            public Material Vent;
            public Material Crate;
            public Material Table;
            public Material Dark;
            public Material Light;
            public Material Plant;
            public Material Tank;
            public Material Counter;
            public Material Water;
            public Material Bush;
            public Material Exit;
            public Material Net;
            public Material Grass;
            public Material Soil;
            public Material Straw;
            public Material Tile;
            public Material Concrete;
            public readonly Dictionary<Role, Material> Roles = new Dictionary<Role, Material>();
        }

        static PrototypeBuilder()
        {
            EditorApplication.delayCall += AutoBuildIfNeeded;
        }

        static void AutoBuildIfNeeded()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (File.Exists(k_ScenePath))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoBuildIfNeeded;
                return;
            }

            if (SessionState.GetBool(k_AutoBuildKey, false))
            {
                return;
            }

            SessionState.SetBool(k_AutoBuildKey, true);
            Build();
        }

        [MenuItem("Tools/Animal Escape/重建原型場景")]
        static void BuildFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Animal Escape", "請先停止 Play 模式，再重建場景。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
        }

        [MenuItem("Tools/Animal Escape/開啟主場景")]
        static void OpenMainScene()
        {
            if (EditorApplication.isPlaying || !File.Exists(k_ScenePath))
            {
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(k_ScenePath);
            }
        }

        internal static void Build()
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                EnsureFolder(k_ScenesDir);
                EnsureFolder(k_PrefabsDir);
                EnsureFolder(k_MaterialsDir);
                EnsureFolder(k_SettingsDir);
                EnsureFolder(k_ConfigResourcesDir);

                bool usingUrp = EnsureUrp();
                PlayerSettings.runInBackground = true;

                GameConfig config = EnsureConfig();
                Palette palette = BuildMaterials(config, usingUrp);

                GameObject slotPrefab = BuildSlotPrefab();
                GameObject avatarPrefab = BuildAvatarPrefab(config, palette);
                GameObject matchStatePrefab = BuildMatchStatePrefab();
                NetworkPrefabsList prefabList = BuildPrefabList(slotPrefab, avatarPrefab, matchStatePrefab);

                BuildLighting();
                BuildCamera();
                MapInfo[] maps = BuildMaps(palette);
                BuildNetwork(prefabList);
                BuildGame(slotPrefab, avatarPrefab, matchStatePrefab, maps);

                EditorSceneManager.SaveScene(scene, k_ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
                AssetDatabase.SaveAssets();

                // 最後才清舊資源：其中有程式檔，刪掉後 Unity 會重新編譯一次。
                RemoveObsoleteAssets();

                Debug.Log("[PrototypeBuilder] 原型場景已建立：" + k_ScenePath + "。按 Play 就可以測試。");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PrototypeBuilder] 建立原型場景時發生錯誤，請把下面的訊息貼給 Claude。");
                Debug.LogException(e);
            }
        }

        // ------------------------------------------------------------------
        // 專案設定與資源
        // ------------------------------------------------------------------

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void RemoveObsoleteAssets()
        {
            foreach (string path in k_ObsoleteAssets)
            {
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        /// <summary>
        /// 啟用 URP（建立並指定 Render Pipeline 資源）。失敗時退回內建管線，不影響玩法測試。
        /// </summary>
        static bool EnsureUrp()
        {
            try
            {
                if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null)
                {
                    return true;
                }

                var pipeline = AssetDatabase.LoadAssetAtPath<Urp.UniversalRenderPipelineAsset>(k_PipelinePath);
                if (pipeline == null)
                {
                    string packagePath = Urp.UniversalRenderPipelineAsset.packagePath;

                    var rendererData = ScriptableObject.CreateInstance<Urp.UniversalRendererData>();
                    rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<Urp.PostProcessData>(
                        packagePath + "/Runtime/Data/PostProcessData.asset");
                    AssetDatabase.CreateAsset(rendererData, k_RendererPath);
                    UnityEngine.Rendering.ResourceReloader.ReloadAllNullIn(rendererData, packagePath);
                    EditorUtility.SetDirty(rendererData);

                    pipeline = Urp.UniversalRenderPipelineAsset.Create(rendererData);
                    AssetDatabase.CreateAsset(pipeline, k_PipelinePath);
                }

                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PrototypeBuilder] 設定 URP 時發生問題，先用內建渲染管線繼續：" + e.Message);
                return UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null;
            }
        }

        static GameConfig EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(k_ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, k_ConfigPath);
            }
            else
            {
                // 數值維持原樣，只是讓檔案重新存一次：程式新增或移除欄位之後，檔案內容才會跟著更新。
                EditorUtility.SetDirty(config);
            }

            return config;
        }

        static Palette BuildMaterials(GameConfig config, bool usingUrp)
        {
            var palette = new Palette();
            palette.Floor = EnsureMaterial("Floor", new Color(0.60f, 0.63f, 0.66f), usingUrp);
            palette.Wall = EnsureMaterial("Wall", new Color(0.45f, 0.49f, 0.55f), usingUrp);
            palette.OuterWall = EnsureMaterial("OuterWall", new Color(0.30f, 0.33f, 0.38f), usingUrp);
            palette.Vent = EnsureMaterial("Vent", new Color(0.22f, 0.55f, 0.60f), usingUrp);
            palette.Crate = EnsureMaterial("Crate", new Color(0.85f, 0.70f, 0.45f), usingUrp);
            palette.Table = EnsureMaterial("Table", new Color(0.55f, 0.40f, 0.28f), usingUrp);
            palette.Dark = EnsureMaterial("AccentDark", new Color(0.12f, 0.12f, 0.14f), usingUrp);
            palette.Light = EnsureMaterial("AccentLight", new Color(0.96f, 0.86f, 0.76f), usingUrp);
            palette.Plant = EnsureMaterial("Plant", new Color(0.36f, 0.62f, 0.30f), usingUrp);
            palette.Tank = EnsureMaterial("Tank", new Color(0.50f, 0.78f, 0.82f), usingUrp);
            palette.Counter = EnsureMaterial("Counter", new Color(0.84f, 0.86f, 0.88f), usingUrp);
            palette.Water = EnsureMaterial("Water", new Color(0.27f, 0.56f, 0.82f), usingUrp);
            palette.Bush = EnsureMaterial("Bush", new Color(0.20f, 0.44f, 0.22f), usingUrp);
            palette.Exit = EnsureMaterial("Exit", new Color(0.92f, 0.45f, 0.28f), usingUrp);

            // 網子：半透明的白色
            palette.Net = EnsureMaterial("Net", new Color(0.96f, 0.98f, 1f, 0.4f), usingUrp);
            MakeTransparent(palette.Net);

            // 各區地板的底色（只是讓人一眼看出自己在哪一區）
            palette.Grass = EnsureMaterial("FloorGrass", new Color(0.50f, 0.64f, 0.44f), usingUrp);
            palette.Soil = EnsureMaterial("FloorSoil", new Color(0.66f, 0.58f, 0.47f), usingUrp);
            palette.Straw = EnsureMaterial("FloorStraw", new Color(0.83f, 0.77f, 0.53f), usingUrp);
            palette.Tile = EnsureMaterial("FloorTile", new Color(0.76f, 0.84f, 0.90f), usingUrp);
            palette.Concrete = EnsureMaterial("FloorConcrete", new Color(0.47f, 0.48f, 0.51f), usingUrp);

            foreach (Role role in Roles.Selectable)
            {
                palette.Roles[role] = EnsureMaterial("Role_" + role, config.GetRole(role).color, usingUrp);
            }

            return palette;
        }

        /// <summary>
        /// 取得（或建立）材質，並把顏色更新成指定的顏色。
        /// </summary>
        static Material EnsureMaterial(string name, Color color, bool usingUrp)
        {
            string path = k_MaterialsDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.color = color;
                EditorUtility.SetDirty(material);
                return material;
            }

            Shader shader = null;
            if (usingUrp)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                    if (pipeline != null && pipeline.defaultMaterial != null)
                    {
                        shader = pipeline.defaultMaterial.shader;
                    }
                }
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                throw new System.InvalidOperationException("找不到可用的著色器（URP Lit 與 Standard 都不存在）。");
            }

            material = new Material(shader);
            material.color = color;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>把材質設成半透明（顏色的 alpha 才會生效）。支援 URP Lit 和內建的 Standard。</summary>
        static void MakeTransparent(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
            {
                // URP Lit
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_BlendModePreserveSpecular"))
                {
                    material.SetFloat("_BlendModePreserveSpecular", 0f);
                }

                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_SrcBlendAlpha"))
                {
                    material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                }

                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetShaderPassEnabled("DepthOnly", false);
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            else if (material.HasProperty("_Mode"))
            {
                // 內建的 Standard
                material.SetFloat("_Mode", 2f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        // ------------------------------------------------------------------
        // 預製物
        // ------------------------------------------------------------------

        static GameObject BuildSlotPrefab()
        {
            var root = new GameObject("PlayerSlot");
            root.AddComponent<NetworkObject>();
            root.AddComponent<PlayerSlot>();
            return SaveAsPrefab(root, k_SlotPrefabPath);
        }

        static GameObject BuildMatchStatePrefab()
        {
            var root = new GameObject("MatchState");
            root.AddComponent<NetworkObject>();
            root.AddComponent<MatchState>();
            return SaveAsPrefab(root, k_MatchStatePrefabPath);
        }

        static GameObject BuildAvatarPrefab(GameConfig config, Palette palette)
        {
            var root = new GameObject("Avatar");

            // 碰撞大小在執行時會依身分重新設定（PlayerAvatar），這裡先給人類的尺寸。
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.03f;

            root.AddComponent<NetworkObject>();

            // 位置由擁有這個角色的玩家決定（Owner），其他人只接收同步。
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;

            // PlayerAvatar 要排在 PlayerMovement 前面；PlayerCapture（追與抓）排最後。
            root.AddComponent<PlayerAvatar>();
            root.AddComponent<PlayerMovement>();
            root.AddComponent<PlayerCapture>();

            foreach (Role role in Roles.Selectable)
            {
                BuildVisual(root.transform, role, config.GetRole(role), palette);
            }

            BuildNetTool(root.transform, palette);
            BuildNetCover(root.transform, palette);

            return SaveAsPrefab(root, k_AvatarPrefabPath);
        }

        /// <summary>
        /// 人類手上的捕捉網：一根桿子加一個方框網。掛在角色的右肩，朝 +Z 伸出去，
        /// 平常關閉，由 PlayerCapture 在身分是人類時打開，並轉動它來做出揮網的動作。
        /// 它不在「Visual_人類」底下，所以第一人稱把自己身體藏起來時，網子還是看得到。
        /// </summary>
        static void BuildNetTool(Transform avatarRoot, Palette palette)
        {
            var tool = new GameObject(PlayerCapture.NetToolName).transform;
            tool.SetParent(avatarRoot, false);
            // 位置在右手邊偏前、腰的高度：第一人稱時只會在畫面右側看到一根細桿和右上角的網框，不會擋住視線。
            tool.localPosition = new Vector3(0.42f, 1.05f, 0.25f);
            tool.localRotation = Quaternion.Euler(-55f, 18f, 0f);

            AddPart(tool, PrimitiveType.Cube, "Pole", new Vector3(0f, 0f, 0.55f), new Vector3(0.035f, 0.035f, 1.1f), palette.Table);

            // 網框：0.7 公尺見方
            const float frame = 0.7f;
            const float bar = 0.045f;
            float middle = 1.1f + frame * 0.5f;
            AddPart(tool, PrimitiveType.Cube, "Frame Near", new Vector3(0f, 0f, 1.1f), new Vector3(frame, bar, bar), palette.Light);
            AddPart(tool, PrimitiveType.Cube, "Frame Far", new Vector3(0f, 0f, 1.1f + frame), new Vector3(frame, bar, bar), palette.Light);
            AddPart(tool, PrimitiveType.Cube, "Frame Left", new Vector3(-frame * 0.5f, 0f, middle), new Vector3(bar, bar, frame), palette.Light);
            AddPart(tool, PrimitiveType.Cube, "Frame Right", new Vector3(frame * 0.5f, 0f, middle), new Vector3(bar, bar, frame), palette.Light);
            AddPart(tool, PrimitiveType.Sphere, "Bag", new Vector3(0f, -0.14f, middle), new Vector3(frame * 0.95f, 0.42f, frame * 0.95f), palette.Net);

            DisableShadows(tool);
            tool.gameObject.SetActive(false);
        }

        /// <summary>
        /// 罩在被網住的動物身上的網子（半透明的球）。平常關閉，由 PlayerCapture 在被網住、被扛著時打開，
        /// 大小在執行時依身分調整。
        /// </summary>
        static void BuildNetCover(Transform avatarRoot, Palette palette)
        {
            var cover = new GameObject(PlayerCapture.NetCoverName).transform;
            cover.SetParent(avatarRoot, false);
            AddPart(cover, PrimitiveType.Sphere, "Net", Vector3.zero, Vector3.one, palette.Net);
            DisableShadows(cover);
            cover.gameObject.SetActive(false);
        }

        static void DisableShadows(Transform root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// 用基本形狀拼出一個身分的外觀（灰盒階段的替代品，之後換成正式模型）。
        /// 角色面向 +Z。外觀預設關閉，由 PlayerAvatar 在執行時打開符合身分的那一個。
        /// </summary>
        static void BuildVisual(Transform avatarRoot, Role role, RoleSettings settings, Palette palette)
        {
            var visual = new GameObject(PlayerAvatar.VisualPrefix + role).transform;
            visual.SetParent(avatarRoot, false);

            Material main = palette.Roles[role];
            float r = settings.radius;
            float h = Mathf.Max(settings.height, r * 2f);

            switch (role)
            {
                case Role.Human:
                    AddPart(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h * 0.5f, r * 2f), main);
                    AddPart(visual, PrimitiveType.Cube, "Visor", new Vector3(0f, h * 0.82f, r * 0.9f), new Vector3(r * 1.3f, h * 0.07f, r * 0.5f), palette.Dark);
                    AddPart(visual, PrimitiveType.Cube, "Backpack", new Vector3(0f, h * 0.55f, -r * 0.95f), new Vector3(r * 1.2f, h * 0.28f, r * 0.6f), palette.Dark);
                    break;

                case Role.Rabbit:
                    AddPart(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h * 0.5f, r * 2f), main);
                    AddPart(visual, PrimitiveType.Cube, "Ear L", new Vector3(-r * 0.45f, h + 0.15f, -0.02f), new Vector3(0.09f, 0.34f, 0.05f), main);
                    AddPart(visual, PrimitiveType.Cube, "Ear R", new Vector3(r * 0.45f, h + 0.15f, -0.02f), new Vector3(0.09f, 0.34f, 0.05f), main);
                    AddPart(visual, PrimitiveType.Cube, "Nose", new Vector3(0f, h * 0.7f, r * 0.95f), new Vector3(0.08f, 0.06f, 0.08f), palette.Dark);
                    AddPart(visual, PrimitiveType.Sphere, "Tail", new Vector3(0f, h * 0.3f, -r), new Vector3(0.14f, 0.14f, 0.14f), palette.Light);
                    break;

                case Role.Monkey:
                    AddPart(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h * 0.5f, r * 2f), main);
                    AddPart(visual, PrimitiveType.Sphere, "Ear L", new Vector3(-r * 1.05f, h * 0.82f, 0f), new Vector3(0.16f, 0.16f, 0.08f), palette.Light);
                    AddPart(visual, PrimitiveType.Sphere, "Ear R", new Vector3(r * 1.05f, h * 0.82f, 0f), new Vector3(0.16f, 0.16f, 0.08f), palette.Light);
                    AddPart(visual, PrimitiveType.Cube, "Face", new Vector3(0f, h * 0.78f, r * 0.9f), new Vector3(r * 1.1f, h * 0.16f, r * 0.4f), palette.Light);
                    AddPart(visual, PrimitiveType.Cube, "Tail", new Vector3(0f, h * 0.35f, -r - 0.2f), new Vector3(0.07f, 0.07f, 0.45f), main);
                    break;

                case Role.Raccoon:
                    AddPart(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h * 0.5f, r * 2f), main);
                    AddPart(visual, PrimitiveType.Cube, "Mask", new Vector3(0f, h * 0.76f, r * 0.9f), new Vector3(r * 1.6f, h * 0.13f, r * 0.45f), palette.Dark);
                    AddPart(visual, PrimitiveType.Cube, "Tail", new Vector3(0f, h * 0.3f, -r - 0.18f), new Vector3(0.14f, 0.14f, 0.4f), main);
                    AddPart(visual, PrimitiveType.Cube, "Tail Tip", new Vector3(0f, h * 0.3f, -r - 0.42f), new Vector3(0.15f, 0.15f, 0.1f), palette.Dark);
                    break;

                case Role.Turtle:
                    AddPart(visual, PrimitiveType.Sphere, "Shell", new Vector3(0f, h * 0.55f, 0f), new Vector3(0.7f, h * 0.9f, 0.9f), main);
                    AddPart(visual, PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.45f, 0.52f), new Vector3(0.2f, 0.2f, 0.22f), palette.Light);
                    break;
            }

            visual.gameObject.SetActive(false);
        }

        static void AddPart(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;

            // 外觀不需要碰撞，碰撞由角色身上的 CharacterController 負責。
            var collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static GameObject SaveAsPrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            // 重新從資源庫載入一次，確保拿到的是匯入完成的預製物（Netcode 會在這時產生它的識別碼）。
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return imported != null ? imported : prefab;
        }

        static NetworkPrefabsList BuildPrefabList(params GameObject[] prefabs)
        {
            if (AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabListPath) != null)
            {
                AssetDatabase.DeleteAsset(k_PrefabListPath);
            }

            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (GameObject prefab in prefabs)
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
            }

            AssetDatabase.CreateAsset(list, k_PrefabListPath);
            return list;
        }

        // ------------------------------------------------------------------
        // 場景：燈光與鏡頭
        // ------------------------------------------------------------------

        static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.63f);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = UnityEngine.LightType.Directional;
            light.shadows = UnityEngine.LightShadows.Soft;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.90f);
            lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            // 第一人稱時鏡頭就在角色身體裡、離牆很近，近裁切面要夠小才不會看穿牆。
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<PlayerCamera>();

            // 還沒有角色時（大廳、選身分），從高處俯瞰整張地圖。
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 60f, -48f), Quaternion.Euler(52f, 0f, 0f));
        }

        // ------------------------------------------------------------------
        // 場景：灰盒地圖（三張，都是 80 x 60 公尺；X 朝東、Z 朝北，原點在正中央）
        //
        // 三張地圖共用同一塊地板和外牆，裡面都沒有隔間牆，各區之間直接相通，靠家具、植栽和水來擋視線與路線：
        //   A 圓環：溫室在正中央（水池＋兩圈弧形植栽），四個設施區在四角，中間是一圈環狀動線。
        //   B 長廊：溫室是一條貫穿東西的花園（交錯的植栽牆＋東端水池），設施區在南北兩側。
        //   C 島群：整個場地都是花園，一條溪流從西北斜到東南，設施像小島一樣散落。
        //
        // 四種設施區是共用的積木（BuildNursery、BuildCages、BuildLab、BuildWarehouse），給一塊長方形範圍就會自動排滿：
        //   育苗區：一排排植栽桌，小動物可以從整排桌子底下鑽過去
        //   籠舍區：一格格 1.45 公尺高的矮圍欄（人類跳不過、看得過去），部分有頂蓋可以躲
        //   研究室：矮實驗台和高設備櫃交錯，部分高櫃底下有動物專用的管道
        //   倉庫：　高貨架排成要拐彎的窄巷，部分貨架底下有管道，貨車出口在這裡
        //
        // 水域：角色走不進去（隱形擋牆），人類只能走橋，小動物另外有管道可以過。
        // 執行時同一時間只開一張地圖，由 MapRegistry 切換。要改格局就改對應的 BuildMapXxx。
        // ------------------------------------------------------------------

        static MapInfo[] BuildMaps(Palette palette)
        {
            // 三張地圖共用的地板與外牆
            Transform shared = new GameObject("Level Shared").transform;
            CreateBlock(shared, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(80.6f, 1f, 60.6f), palette.Floor);
            WallAlongX(shared, "Outer North", 30f, -40.15f, 40.15f, palette.OuterWall);
            WallAlongX(shared, "Outer South", -30f, -40.15f, 40.15f, palette.OuterWall);
            WallAlongZ(shared, "Outer West", -40f, -30f, 30f, palette.OuterWall);
            WallAlongZ(shared, "Outer East", 40f, -30f, 30f, palette.OuterWall);

            Transform mapsRoot = new GameObject("Maps").transform;
            var maps = new[]
            {
                BuildMapRing(mapsRoot, palette),
                BuildMapSpine(mapsRoot, palette),
                BuildMapIslands(mapsRoot, palette)
            };

            // 場景裡先只開第一張；執行時由 MapRegistry 依選擇切換。
            for (int i = 1; i < maps.Length; i++)
            {
                maps[i].root.SetActive(false);
            }

            return maps;
        }

        static MapInfo NewMap(Transform mapsRoot, string objectName, string displayName, string summary)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(mapsRoot, false);
            return new MapInfo { displayName = displayName, summary = summary, root = root };
        }

        /// <summary>設定出生點。四個動物出生點以 animalCenter 為中心，沿 animalStep 的方向排開。</summary>
        static void SetSpawns(MapInfo map, Vector3 human, float humanYaw, Vector3 animalCenter, Vector3 animalStep, float animalYaw)
        {
            Transform spawnRoot = Group(map.root.transform, "SpawnPoints");
            map.humanSpawn = CreateSpawnPoint(spawnRoot, "Human Spawn", human, humanYaw);
            map.animalSpawns = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                map.animalSpawns[i] = CreateSpawnPoint(spawnRoot, "Animal Spawn " + (i + 1), animalCenter + animalStep * (i - 1.5f), animalYaw);
            }
        }

        // ------------------------------------------------------------------
        // 地圖 A：圓環
        // ------------------------------------------------------------------

        static MapInfo BuildMapRing(Transform mapsRoot, Palette palette)
        {
            MapInfo map = NewMap(mapsRoot, "Map A Ring", "A 圓環", "溫室在正中央，四個設施區在四角，中間是一圈環狀動線。最好認路。");
            Transform root = map.root.transform;
            Transform garden = Group(root, "Greenhouse");

            // 溫室的草地（橢圓形）和一圈溫室骨架的柱子
            FloorDisc(garden, "Grass", 0f, 0f, 21f, 14.5f, palette.Grass);
            for (int i = 0; i < 12; i++)
            {
                Vector3 position = OnEllipse(21f, 14.5f, 15f + i * 30f);
                Post(garden, "Post " + (i + 1), position.x, position.z, palette);
            }

            // 中央水池：一座南北向的橋把水池分成東西兩半；西半邊有一條動物專用的管道直通橋上。
            Water(garden, "Pond East", 1.2f, 7f, -4.5f, 2f, palette);
            Water(garden, "Pond West N", -7f, -1.2f, -1f, 2f, palette);
            Water(garden, "Pond West S", -7f, -1.2f, -4.5f, -2f, palette);
            Bridge(garden, "Pond Bridge", -1.2f, 1.2f, -5f, 2.5f, palette);
            WaterPipeAlongX(garden, "Pond Pipe", -4.35f, -1.5f, 3.15f, palette);

            // 內圈：圍著水池的弧形矮植栽箱，南北兩端留缺口對著橋頭。
            float[] innerAngles = { 20f, 55f, 125f, 160f, 200f, 235f, 305f, 340f };
            for (int i = 0; i < innerAngles.Length; i++)
            {
                Vector3 position = OnEllipse(11f, 8f, innerAngles[i]);
                Planter(garden, "Inner Planter " + (i + 1), position.x, position.z, 4f, 1f, TangentYaw(11f, 8f, innerAngles[i]), palette);
            }

            // 外圈：高樹籬（擋視線）和矮植栽箱交錯，東西南北各留一個入口。
            float[] hedgeAngles = { 22f, 68f, 112f, 158f, 202f, 248f, 292f, 338f };
            for (int i = 0; i < hedgeAngles.Length; i++)
            {
                Vector3 position = OnEllipse(18.5f, 12f, hedgeAngles[i]);
                Bush(garden, "Hedge " + (i + 1), position.x, position.z, 5f, 1.2f, TangentYaw(18.5f, 12f, hedgeAngles[i]), palette);
            }

            float[] outerPlanterAngles = { 45f, 135f, 225f, 315f };
            for (int i = 0; i < outerPlanterAngles.Length; i++)
            {
                Vector3 position = OnEllipse(18.5f, 12f, outerPlanterAngles[i]);
                Planter(garden, "Outer Planter " + (i + 1), position.x, position.z, 3f, 1f, TangentYaw(18.5f, 12f, outerPlanterAngles[i]), palette);
            }

            // 每個入口正後方再擋一道樹籬，進溫室要拐個彎，從外面看不穿。
            Bush(garden, "Gate Hedge East", 14.2f, 0f, 4f, 1.2f, 90f, palette);
            Bush(garden, "Gate Hedge West", -14.2f, 0f, 4f, 1.2f, 90f, palette);
            Bush(garden, "Gate Hedge North", 0f, 10.3f, 4f, 1.2f, 0f, palette);
            Bush(garden, "Gate Hedge South", 0f, -10.3f, 4f, 1.2f, 0f, palette);

            float[] treeAngles = { 35f, 145f, 215f, 325f };
            for (int i = 0; i < treeAngles.Length; i++)
            {
                Vector3 position = OnEllipse(14.8f, 10f, treeAngles[i]);
                TreeProp(garden, "Tree " + (i + 1), position.x, position.z, palette);
            }

            // 四個角落的設施區
            BuildCages(root, "Cages", -38f, -18f, 14f, 28.5f, palette);
            BuildLab(root, "Lab", 18f, 38f, 14f, 28.5f, palette);
            BuildNursery(root, "Nursery", -38f, -18f, -28.5f, -14f, palette);
            BuildWarehouse(root, "Warehouse", 18f, 36f, -28.5f, -14f, palette);
            ExitOnEastWall(root, "Truck Exit", -24f, -18f, palette);

            // 四邊中段：零星的掩體
            Transform edges = Group(root, "Edges");
            Steps(edges, "Steps", 0f, 26.5f, palette);
            Crate(edges, "North Crate 1", -9f, 20f, 1f, palette);
            Crate(edges, "North Crate 2", 10f, 18.5f, 1.5f, palette);
            CreateTable(edges, "South Table", new Vector3(-6f, 0f, -22f), palette);
            Crate(edges, "South Crate 1", 6f, -20f, 1f, palette);
            Crate(edges, "South Crate 2", 8f, -26f, 1.5f, palette);
            Planter(edges, "South Planter", 0f, -17f, 4f, 1f, 0f, palette);
            Planter(edges, "West Planter N", -30f, 6.5f, 4f, 1f, 0f, palette);
            Planter(edges, "West Planter S", -30f, -6.5f, 4f, 1f, 0f, palette);
            Planter(edges, "East Planter N", 30f, 6.5f, 4f, 1f, 0f, palette);
            Planter(edges, "East Planter S", 30f, -6.5f, 4f, 1f, 0f, palette);
            Bush(edges, "West Hedge", -24.5f, 0f, 4f, 1.2f, 90f, palette);
            Bush(edges, "East Hedge", 24.5f, 0f, 4f, 1.2f, 90f, palette);

            // 動物在西側中段，人類在東側中段，中間隔著整個溫室。
            SetSpawns(map, new Vector3(37f, 0.05f, 0f), 270f, new Vector3(-37f, 0.05f, 0f), new Vector3(0f, 0f, -1.6f), 90f);
            return map;
        }

        // ------------------------------------------------------------------
        // 地圖 B：長廊
        // ------------------------------------------------------------------

        static MapInfo BuildMapSpine(Transform mapsRoot, Palette palette)
        {
            MapInfo map = NewMap(mapsRoot, "Map B Spine", "B 長廊", "溫室是一條貫穿東西的花園，設施區在南北兩側。追逐感最強。");
            Transform root = map.root.transform;
            Transform garden = Group(root, "Greenhouse");

            // 花園裡交錯的植栽牆：一道從北邊伸出來、下一道從南邊伸出來，沿著花園走就得左右繞。
            // 每道由高樹籬和矮植栽箱交替組成；第 2、4 道的中段是有管道的高櫃，小動物可以直接鑽過去。
            FloorPatch(garden, "Grass", -39.8f, 39.8f, -10.5f, 10.5f, 0.02f, palette.Grass);
            for (int i = 0; i < 10; i++)
            {
                // 溫室骨架的柱子，沿著花園的南北兩條邊排
                float postX = -36f + i * 8f;
                Post(garden, "Post N" + (i + 1), postX, 10.5f, palette);
                Post(garden, "Post S" + (i + 1), postX, -10.5f, palette);
            }

            float[] baffleX = { -30f, -21f, -12f, -3f, 6f };
            for (int i = 0; i < baffleX.Length; i++)
            {
                Transform baffle = Group(garden, "Baffle " + (i + 1));
                float direction = i % 2 == 0 ? 1f : -1f;
                float x = baffleX[i];
                Bush(baffle, "Hedge Outer", x, direction * 8f, 4f, 1.4f, 90f, palette);
                Planter(baffle, "Planter Outer", x, direction * 4f, 4f, 1.4f, 90f, palette);
                if (i % 2 == 1)
                {
                    TallCabinet(baffle, "Hedge With Pipe", x, 0f, 4f, false, true, palette.Bush, palette);
                }
                else
                {
                    Bush(baffle, "Hedge Middle", x, 0f, 4f, 1.4f, 90f, palette);
                }

                Planter(baffle, "Planter Inner", x, direction * -4f, 4f, 1.4f, 90f, palette);
            }

            TreeProp(garden, "Tree 1", -25.5f, 4f, palette);
            TreeProp(garden, "Tree 2", -16.5f, -4f, palette);
            TreeProp(garden, "Tree 3", -7.5f, 4f, palette);
            TreeProp(garden, "Tree 4", 1.5f, -4f, palette);
            TreeProp(garden, "Tree 5", 10f, 6f, palette);
            Planter(garden, "Garden Planter 1", -25.5f, -6f, 3f, 1f, 0f, palette);
            Planter(garden, "Garden Planter 2", -16.5f, 6f, 3f, 1f, 0f, palette);
            Planter(garden, "Garden Planter 3", -7.5f, -6f, 3f, 1f, 0f, palette);
            Planter(garden, "Garden Planter 4", 1.5f, 6f, 3f, 1f, 0f, palette);
            Planter(garden, "Garden Planter 5", 9.5f, -3f, 3f, 1f, 90f, palette);

            // 東端水池：一座南北向的橋；西半邊有一條動物專用的管道通到橋上。
            Water(garden, "Pond East", 23.2f, 31f, -6f, 6f, palette);
            Water(garden, "Pond West S", 13f, 20.8f, -6f, 2.5f, palette);
            Water(garden, "Pond West N", 13f, 20.8f, 3.5f, 6f, palette);
            Bridge(garden, "Pond Bridge", 20.8f, 23.2f, -6.5f, 6.5f, palette);
            WaterPipeAlongX(garden, "Pond Pipe", 16.65f, 3f, 4.15f, palette);

            // 北側：籠舍區、研究室；南側：育苗區、倉庫
            BuildCages(root, "Cages", -38f, -4.5f, 12.5f, 28.5f, palette);
            BuildLab(root, "Lab", 4.5f, 38f, 12.5f, 28.5f, palette);
            BuildNursery(root, "Nursery", -38f, -4.5f, -28.5f, -12.5f, palette);
            BuildWarehouse(root, "Warehouse", 4.5f, 38f, -28.5f, -12.5f, palette);
            ExitOnSouthWall(root, "Truck Exit", 26f, 32f, palette);

            // 南北兩側中間的通道
            Transform middle = Group(root, "Middle");
            Steps(middle, "Steps", 0f, 20f, palette);
            Crate(middle, "Middle Crate 1", 0f, -20f, 1.5f, palette);
            Crate(middle, "Middle Crate 2", -0.8f, -15.5f, 1f, palette);
            Crate(middle, "Middle Crate 3", 0.6f, 26f, 1f, palette);

            // 動物在花園西端，人類在花園東端（水池後面）。
            SetSpawns(map, new Vector3(36f, 0.05f, 0f), 270f, new Vector3(-37f, 0.05f, 0f), new Vector3(0f, 0f, -1.6f), 90f);
            return map;
        }

        // ------------------------------------------------------------------
        // 地圖 C：島群
        // ------------------------------------------------------------------

        static MapInfo BuildMapIslands(Transform mapsRoot, Palette palette)
        {
            MapInfo map = NewMap(mapsRoot, "Map C Islands", "C 島群", "整個場地都是花園，一條溪流斜穿而過，設施像小島散落。變化最多。");
            Transform root = map.root.transform;
            FloorPatch(root, "Grass", -39.8f, 39.8f, -29.8f, 29.8f, 0.02f, palette.Grass);
            Transform stream = Group(root, "Stream");

            // 溪流：從西北一階一階往東南流，最後匯成一個水池。兩端離牆各留一段陸地可以繞。
            // 人類只能走三座橋（或繞兩端），小動物另外有兩條管道可以過。
            Water(stream, "Stream 1", -32f, -13f, 8f, 12f, palette);
            Water(stream, "Stream 2 N", -17f, -13f, 7.2f, 8f, palette);
            Water(stream, "Stream 2 S", -17f, -13f, 4f, 4.8f, palette);
            Bridge(stream, "Bridge 1", -17.5f, -12.5f, 4.8f, 7.2f, palette);

            Water(stream, "Stream 3 W", -17f, -8.5f, 0f, 4f, palette);
            Water(stream, "Stream 3 E", -7.5f, 5f, 0f, 4f, palette);
            WaterPipeAlongZ(stream, "Stream Pipe 1", -8f, 2f, 2.5f, palette);

            Water(stream, "Stream 4 N", 1f, 5f, -0.8f, 0f, palette);
            Water(stream, "Stream 4 S", 1f, 5f, -4f, -3.2f, palette);
            Bridge(stream, "Bridge 2", 0.5f, 5.5f, -3.2f, -0.8f, palette);

            Water(stream, "Stream 5 W", 1f, 9.5f, -8f, -4f, palette);
            Water(stream, "Stream 5 E", 10.5f, 21f, -8f, -4f, palette);
            WaterPipeAlongZ(stream, "Stream Pipe 2", 10f, -6f, 2.5f, palette);

            Water(stream, "Stream 6 N", 17f, 21f, -8.8f, -8f, palette);
            Water(stream, "Stream 6 S", 17f, 21f, -12f, -11.2f, palette);
            Bridge(stream, "Bridge 3", 16.5f, 21.5f, -11.2f, -8.8f, palette);

            Water(stream, "Pond", 17f, 33f, -17f, -12f, palette);

            // 散落的設施「島」
            BuildNursery(root, "Nursery", -37f, -16f, 15f, 28f, palette);
            BuildCages(root, "Cages", 14f, 37.5f, 13f, 28f, palette);
            BuildLab(root, "Lab", -37.5f, -14f, -25f, -9f, palette);
            BuildWarehouse(root, "Warehouse", 16f, 35.5f, -28.5f, -19.5f, palette);
            ExitOnEastWall(root, "Truck Exit", -27f, -21f, palette);

            // 花園：歪歪斜斜的樹籬、植栽箱和樹，沒有一條路是直的。
            Transform garden = Group(root, "Garden");
            Bush(garden, "Hedge N1", -6f, 16f, 5f, 1.2f, 30f, palette);
            Bush(garden, "Hedge N2", 5f, 22f, 5f, 1.2f, -20f, palette);
            Planter(garden, "Planter N1", -10f, 24f, 4f, 1f, 60f, palette);
            Planter(garden, "Planter N2", 8f, 15f, 4f, 1f, 45f, palette);
            TreeProp(garden, "Tree N", -1f, 26f, palette);

            Bush(garden, "Hedge M1", -4f, 8f, 5f, 1.2f, 20f, palette);
            Planter(garden, "Planter M1", 6f, 6f, 4f, 1f, -30f, palette);
            Bush(garden, "Hedge M2", 14f, 2f, 5f, 1.2f, 70f, palette);
            TreeProp(garden, "Tree M1", 22f, 5f, palette);
            TreeProp(garden, "Tree M2", 10f, 10f, palette);
            Planter(garden, "Planter M2", 30f, 6f, 4f, 1f, 15f, palette);
            Bush(garden, "Hedge M3", 33f, -3f, 5f, 1.2f, 80f, palette);
            Steps(garden, "Steps", 26f, 9.5f, palette);

            Planter(garden, "Planter W1", -28f, 0f, 4f, 1f, 40f, palette);
            Bush(garden, "Hedge W1", -24f, -5f, 5f, 1.2f, -15f, palette);
            TreeProp(garden, "Tree W", -31f, 5f, palette);
            Bush(garden, "Hedge W2", -22f, 14f, 4f, 1.2f, 0f, palette);

            Bush(garden, "Hedge S1", -6f, -14f, 5f, 1.2f, 25f, palette);
            Planter(garden, "Planter S1", 2f, -20f, 4f, 1f, -40f, palette);
            TreeProp(garden, "Tree S1", 8f, -14f, palette);
            Bush(garden, "Hedge S2", 11f, -23f, 5f, 1.2f, 60f, palette);
            Planter(garden, "Planter S2", -8f, -25f, 4f, 1f, 10f, palette);
            TreeProp(garden, "Tree S2", -2f, -8.5f, palette);
            Crate(garden, "Crate S", -11f, -4.5f, 1f, palette);
            Crate(garden, "Crate E", 27f, -5f, 1.5f, palette);

            // 動物在西側（溪流的西南岸），人類在東南角的水池旁，兩邊隔著溪流。
            SetSpawns(map, new Vector3(36.5f, 0.05f, -14.5f), 270f, new Vector3(-36.5f, 0.05f, 0f), new Vector3(0f, 0f, -1.6f), 90f);
            return map;
        }

        // ------------------------------------------------------------------
        // 設施區（給一塊長方形範圍，自動排滿）
        // ------------------------------------------------------------------

        /// <summary>
        /// 育苗區：一排排植栽桌（沿 X 方向），排與排之間是走道。桌下淨高 0.85 公尺，
        /// 兔子、浣熊、烏龜可以從整排桌子底下鑽過去；每排中間空一張桌子當橫向通道。
        /// </summary>
        static void BuildNursery(Transform parent, string name, float xMin, float xMax, float zMin, float zMax, Palette palette)
        {
            Transform zone = Group(parent, name);
            ZoneFloor(zone, xMin, xMax, zMin, zMax, palette.Soil);
            int perRow = Mathf.FloorToInt((xMax - xMin) / 2.4f);
            float startX = (xMin + xMax) * 0.5f - (perRow - 1) * 1.2f;
            int skip = perRow >= 6 ? perRow / 2 : -1;

            int row = 0;
            for (float z = zMax - 1.2f; z >= zMin + 1.19f; z -= 4.5f)
            {
                Transform rowGroup = Group(zone, "Bench Row " + (row + 1));
                for (int i = 0; i < perRow; i++)
                {
                    if (i == skip)
                    {
                        continue;
                    }

                    float x = startX + i * 2.4f;
                    CreateTable(rowGroup, "Bench " + (i + 1), new Vector3(x, 0f, z), palette);

                    // 一半的桌上有盆栽（高度加起來 1.4 公尺，人類還是看得過去）
                    if ((row + i) % 2 == 0)
                    {
                        CreateBlock(rowGroup, "Plants " + (i + 1), new Vector3(x, 1.175f, z), new Vector3(2f, 0.45f, 0.8f), palette.Plant);
                    }
                }

                // 每隔一條走道，在走道中間放一個高植栽架擋視線（兩側各留約 1.3 公尺可以走）。
                bool hasNextRow = z - 4.5f >= zMin + 1.19f;
                if (row % 2 == 0 && hasNextRow)
                {
                    float rackX = Mathf.Lerp(xMin, xMax, row % 4 == 0 ? 0.3f : 0.7f);
                    Rack(zone, "Plant Rack " + (row + 1), rackX, z - 2.25f, palette);
                }

                row++;
            }
        }

        /// <summary>
        /// 籠舍區：一排排背靠牆、正面開口的隔間（每格 2.5 x 2.5 公尺），圍欄高 1.45 公尺：
        /// 人類、浣熊、烏龜跳不過（人類站近了看得到裡面）；兔子和猴子跳得過。每三格有一格加了頂蓋（淨高 0.85 公尺），
        /// 小動物躲進去，人類要繞到正面才看得到。每排中間空一格當通道。
        /// </summary>
        static void BuildCages(Transform parent, string name, float xMin, float xMax, float zMin, float zMax, Palette palette)
        {
            const float stall = 2.5f;
            const float fenceHeight = 1.45f;
            const float fenceThickness = 0.15f;

            Transform zone = Group(parent, name);
            ZoneFloor(zone, xMin, xMax, zMin, zMax, palette.Straw);
            int stalls = Mathf.FloorToInt((xMax - xMin) / stall);
            float left = (xMin + xMax) * 0.5f - stalls * stall * 0.5f;
            int gap = stalls >= 6 ? stalls / 2 : -1;

            int row = 0;
            for (float back = zMax - 0.3f; back - stall >= zMin - 0.01f; back -= 6.5f)
            {
                Transform rowGroup = Group(zone, "Pen Row " + (row + 1));
                float middleZ = back - stall * 0.5f;
                for (int i = 0; i < stalls; i++)
                {
                    if (i == gap)
                    {
                        continue;
                    }

                    float stallLeft = left + i * stall;
                    float middleX = stallLeft + stall * 0.5f;
                    CreateBlock(rowGroup, "Back " + (i + 1), new Vector3(middleX, fenceHeight * 0.5f, back),
                        new Vector3(stall + fenceThickness, fenceHeight, fenceThickness), palette.Counter);
                    CreateBlock(rowGroup, "Side " + (i + 1), new Vector3(stallLeft, fenceHeight * 0.5f, middleZ),
                        new Vector3(fenceThickness, fenceHeight, stall), palette.Counter);

                    // 這一排的最後一格、或是缺口前的那一格，右邊要補一片隔板。
                    if (i == stalls - 1 || i + 1 == gap)
                    {
                        CreateBlock(rowGroup, "Side End " + (i + 1), new Vector3(stallLeft + stall, fenceHeight * 0.5f, middleZ),
                            new Vector3(fenceThickness, fenceHeight, stall), palette.Counter);
                    }

                    if (i % 3 == 1)
                    {
                        CreateBlock(rowGroup, "Roof " + (i + 1), new Vector3(middleX, 0.9f, middleZ),
                            new Vector3(stall, 0.1f, stall), palette.Wall);
                    }
                }

                // 走道的一端放一個高籠子擋視線（左右交替）
                bool hasNextRow = back - 6.5f - stall >= zMin - 0.01f;
                if (hasNextRow)
                {
                    float cageX = row % 2 == 0 ? xMin + 1.5f : xMax - 1.5f;
                    CreateBlock(zone, "Tall Cage " + (row + 1), new Vector3(cageX, 1.1f, back - stall - 2f),
                        new Vector3(2f, 2.2f, 2f), palette.Wall);
                }

                row++;
            }
        }

        /// <summary>
        /// 研究室：實驗島沿 Z 方向排成幾欄，矮實驗台（0.95 公尺，人類看得過去）和高設備櫃（2.2 公尺，擋視線）
        /// 像棋盤一樣交錯，所以每個高櫃都繞得過去。奇數欄的高櫃底下有動物專用的管道。
        /// </summary>
        static void BuildLab(Transform parent, string name, float xMin, float xMax, float zMin, float zMax, Palette palette)
        {
            const float length = 5f;
            const float spacing = 5.5f;

            Transform zone = Group(parent, name);
            ZoneFloor(zone, xMin, xMax, zMin, zMax, palette.Tile);
            float depth = zMax - zMin;
            var rowCenters = new List<float>();
            if (depth >= 13f)
            {
                rowCenters.Add(zMax - 0.5f - length * 0.5f);
                if (depth >= 21f)
                {
                    rowCenters.Add((zMin + zMax) * 0.5f);
                }

                rowCenters.Add(zMin + 0.5f + length * 0.5f);
            }
            else
            {
                rowCenters.Add((zMin + zMax) * 0.5f);
            }

            int columns = Mathf.FloorToInt((xMax - xMin - 1.2f) / spacing) + 1;
            float startX = (xMin + xMax) * 0.5f - (columns - 1) * spacing * 0.5f;
            for (int row = 0; row < rowCenters.Count; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x = startX + column * spacing;
                    float z = rowCenters[row];
                    string label = (row + 1) + "-" + (column + 1);
                    if ((row + column) % 2 == 0)
                    {
                        CreateBlock(zone, "Lab Counter " + label, new Vector3(x, 0.475f, z), new Vector3(1.2f, 0.95f, length), palette.Counter);
                        if (column % 4 < 2)
                        {
                            // 實驗台上的水槽：加起來 1.5 公尺高，人類還是看得過去，小動物躲在後面就看不到了。
                            CreateBlock(zone, "Lab Tank " + label, new Vector3(x, 1.225f, z + (row % 2 == 0 ? -1f : 1f)), new Vector3(0.9f, 0.55f, 1.8f), palette.Tank);
                        }
                    }
                    else
                    {
                        TallCabinet(zone, "Lab Cabinet " + label, x, z, length, false, column % 2 == 1, palette.Wall, palette);
                    }
                }
            }
        }

        /// <summary>
        /// 倉庫：高貨架（2.2 公尺）沿 X 方向排成幾排，每排中間留一個 2.5 公尺的缺口，
        /// 缺口位置一排偏左、一排偏右，所以穿過倉庫要一直拐彎，很容易跟丟。
        /// 每排有一段貨架底下有動物專用的管道，可以直接穿到隔壁走道。
        /// </summary>
        static void BuildWarehouse(Transform parent, string name, float xMin, float xMax, float zMin, float zMax, Palette palette)
        {
            Transform zone = Group(parent, name);
            ZoneFloor(zone, xMin, xMax, zMin, zMax, palette.Concrete);
            int row = 0;
            for (float z = zMax - 1.5f; z >= zMin + 1.49f; z -= 4.5f)
            {
                float gapCenter = Mathf.Lerp(xMin, xMax, row % 2 == 0 ? 0.35f : 0.65f);
                ShelfSpan(zone, "Shelf " + (row + 1) + "A", xMin + 0.5f, gapCenter - 1.25f, z, row % 2 == 1, palette);
                ShelfSpan(zone, "Shelf " + (row + 1) + "B", gapCenter + 1.25f, xMax - 0.5f, z, row % 2 == 0, palette);

                // 走道裡零星的箱子
                bool hasNextRow = z - 4.5f >= zMin + 1.49f;
                if (hasNextRow)
                {
                    float crateX = Mathf.Lerp(xMin, xMax, row % 2 == 0 ? 0.8f : 0.2f);
                    Crate(zone, "Aisle Crate " + (row + 1), crateX, z - 2.25f, row % 2 == 0 ? 1f : 1.5f, palette);
                }

                row++;
            }
        }

        static void ShelfSpan(Transform parent, string name, float xFrom, float xTo, float z, bool withPipe, Palette palette)
        {
            float length = xTo - xFrom;
            if (length < 1f)
            {
                return;
            }

            TallCabinet(parent, name, (xFrom + xTo) * 0.5f, z, length, true, withPipe && length >= 4f, palette.Table, palette);
        }

        // ------------------------------------------------------------------
        // 積木
        // ------------------------------------------------------------------

        static Vector3 OnEllipse(float a, float b, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(a * Mathf.Cos(radians), 0f, b * Mathf.Sin(radians));
        }

        /// <summary>讓長邊（物件自己的 X 軸）順著橢圓切線方向所需要的旋轉角度。</summary>
        static float TangentYaw(float a, float b, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float tangentX = -a * Mathf.Sin(radians);
            float tangentZ = b * Mathf.Cos(radians);
            return Mathf.Atan2(-tangentZ, tangentX) * Mathf.Rad2Deg;
        }

        /// <summary>一組可以整個旋轉的物件：建在 (x, z)、繞 Y 軸轉 yaw 度，裡面的積木用它自己的座標擺。</summary>
        static Transform Pivot(Transform parent, string name, float x, float z, float yaw)
        {
            Transform pivot = Group(parent, name);
            pivot.localPosition = new Vector3(x, 0f, z);
            pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return pivot;
        }

        /// <summary>
        /// 矮植栽箱：箱子 1.45 公尺高，上面再一層植物。人類看得過去但跳不上去，兔子和猴子跳得過，動物可以躲在後面。
        /// length 是長邊，yaw = 0 時長邊沿 X。
        ///
        /// 高度的規則（實測）：角色跳起來之後還能再「踩上」一小段（CharacterController 的 stepOffset，人類 0.3 公尺），
        /// 所以實際爬得上去的高度 = 跳躍高度 + stepOffset：人類 1.3、浣熊 1.25、烏龜 0.68、猴子 1.6、兔子 1.8 公尺
        /// （人類實測：1.3 公尺上得去，1.4 公尺上不去）。要擋住人類又讓兔子、猴子過得去，高度就抓 1.45 公尺。
        /// </summary>
        static void Planter(Transform parent, string name, float x, float z, float length, float width, float yaw, Palette palette)
        {
            Transform pivot = Pivot(parent, name, x, z, yaw);
            CreateBlock(pivot, "Box", new Vector3(0f, 0.725f, 0f), new Vector3(length, 1.45f, width), palette.Crate);
            CreateBlock(pivot, "Plants", new Vector3(0f, 1.49f, 0f), new Vector3(length - 0.2f, 0.08f, width - 0.2f), palette.Plant);
        }

        /// <summary>高樹籬：2.2 公尺高，擋視線用。length 是長邊，yaw = 0 時長邊沿 X。</summary>
        static void Bush(Transform parent, string name, float x, float z, float length, float width, float yaw, Palette palette)
        {
            Transform pivot = Pivot(parent, name, x, z, yaw);
            CreateBlock(pivot, "Hedge", new Vector3(0f, 1.1f, 0f), new Vector3(length, 2.2f, width), palette.Bush);
        }

        /// <summary>樹：1 公尺粗的樹幹，樹冠在 3.2 公尺以上（不擋路，只是讓場景有高有低）。</summary>
        static void TreeProp(Transform parent, string name, float x, float z, Palette palette)
        {
            Transform pivot = Pivot(parent, name, x, z, 0f);
            CreateBlock(pivot, "Trunk", new Vector3(0f, 1.6f, 0f), new Vector3(1f, 3.2f, 1f), palette.Table);
            // 樹冠沒有碰撞：沒有人上得去，留著只會讓第三人稱鏡頭在樹下被擠到角色身上。
            RemoveCollider(CreateBlock(pivot, "Canopy", new Vector3(0f, 3.9f, 0f), new Vector3(3.6f, 1.4f, 3.6f), palette.Plant));
        }

        /// <summary>高植栽架：2.2 公尺高、6 公尺長（沿 X）、0.6 公尺厚，擋視線用。</summary>
        static void Rack(Transform parent, string name, float x, float z, Palette palette)
        {
            Transform rack = Group(parent, name);
            CreateBlock(rack, "Frame", new Vector3(x, 1.1f, z), new Vector3(6f, 2.2f, 0.6f), palette.Table);
            CreateBlock(rack, "Plants", new Vector3(x, 2.35f, z), new Vector3(5.6f, 0.3f, 0.5f), palette.Plant);
        }

        /// <summary>
        /// 水域：一片貼地的水面，加上一道看不見的擋牆，角色走不進去也跳不過去。
        /// 擋牆放在 Water 圖層，鏡頭和「看不看得到對方」的判斷會略過它，所以隔著水還是看得到對岸。
        /// </summary>
        static void Water(Transform parent, string name, float xFrom, float xTo, float zFrom, float zTo, Palette palette)
        {
            float sizeX = xTo - xFrom;
            float sizeZ = zTo - zFrom;
            var center = new Vector3((xFrom + xTo) * 0.5f, 0f, (zFrom + zTo) * 0.5f);

            GameObject surface = CreateBlock(parent, name, center + Vector3.up * 0.03f, new Vector3(sizeX, 0.06f, sizeZ), palette.Water);
            RemoveCollider(surface);

            WaterBlocker(parent, name + " Blocker", center + Vector3.up * 1.5f, new Vector3(sizeX, 3f, sizeZ));
        }

        /// <summary>
        /// 橫跨水域的管道（沿 X）。和一般管道一樣，另外在管道上方加一道看不見的擋牆，
        /// 免得有人跳到管道頂上，從兩片水域中間走過去。
        /// </summary>
        static void WaterPipeAlongX(Transform parent, string name, float centerX, float z, float halfLength, Palette palette)
        {
            VentAlongX(parent, name, centerX, z, halfLength, palette);
            WaterBlocker(parent, name + " Top Blocker", new Vector3(centerX, 1.95f, z), new Vector3(halfLength * 2f, 2.1f, k_VentSize + 0.2f));
        }

        /// <summary>橫跨水域的管道（沿 Z）。</summary>
        static void WaterPipeAlongZ(Transform parent, string name, float x, float centerZ, float halfLength, Palette palette)
        {
            VentAlongZ(parent, name, x, centerZ, halfLength, palette);
            WaterBlocker(parent, name + " Top Blocker", new Vector3(x, 1.95f, centerZ), new Vector3(k_VentSize + 0.2f, 2.1f, halfLength * 2f));
        }

        /// <summary>看不見的擋牆，放在 Water 圖層（鏡頭和視線判斷會略過它）。</summary>
        static void WaterBlocker(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var blocker = new GameObject(name);
            blocker.transform.SetParent(parent, false);
            blocker.transform.localPosition = center;
            blocker.layer = GameLayers.Water;
            blocker.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// 地板的底色：一片貼地、沒有碰撞的薄板，只是用顏色標出區域。
        /// top 是薄板頂面的高度：大片的草地用 0.02，疊在上面的設施區用 0.03，才不會兩片疊在同一個高度閃爍。
        /// </summary>
        static void FloorPatch(Transform parent, string name, float xFrom, float xTo, float zFrom, float zTo, float top, Material material)
        {
            GameObject patch = CreateBlock(parent, name, new Vector3((xFrom + xTo) * 0.5f, top * 0.5f, (zFrom + zTo) * 0.5f),
                new Vector3(xTo - xFrom, top, zTo - zFrom), material);
            RemoveCollider(patch);
            patch.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>設施區的地板底色：比範圍往外多 0.75 公尺，但不超出外牆。</summary>
        static void ZoneFloor(Transform zone, float xMin, float xMax, float zMin, float zMax, Material material)
        {
            FloorPatch(zone, "Floor Tint",
                Mathf.Max(xMin - 0.75f, -39.8f), Mathf.Min(xMax + 0.75f, 39.8f),
                Mathf.Max(zMin - 0.75f, -29.8f), Mathf.Min(zMax + 0.75f, 29.8f), 0.03f, material);
        }

        /// <summary>橢圓形的地板底色（圓環地圖的溫室草地）。</summary>
        static void FloorDisc(Transform parent, string name, float x, float z, float radiusX, float radiusZ, Material material)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(x, 0.01f, z);
            disc.transform.localScale = new Vector3(radiusX * 2f, 0.01f, radiusZ * 2f);
            var renderer = disc.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            RemoveCollider(disc);
        }

        /// <summary>溫室骨架的柱子：0.4 公尺粗、和外牆一樣高，可以當一點點掩體。</summary>
        static void Post(Transform parent, string name, float x, float z, Palette palette)
        {
            CreateBlock(parent, name, new Vector3(x, k_WallHeight * 0.5f, z), new Vector3(0.4f, k_WallHeight, 0.4f), palette.Counter);
        }

        /// <summary>橋：水域的缺口上鋪一片橋面（只是外觀，本來就走得過去）。</summary>
        static void Bridge(Transform parent, string name, float xFrom, float xTo, float zFrom, float zTo, Palette palette)
        {
            GameObject deck = CreateBlock(parent, name, new Vector3((xFrom + xTo) * 0.5f, 0.04f, (zFrom + zTo) * 0.5f),
                new Vector3(xTo - xFrom, 0.08f, zTo - zFrom), palette.Table);
            RemoveCollider(deck);
        }

        /// <summary>三階台階（0.5、1.0、1.5 公尺），沿 X 排開，用來比較各身分的跳躍高度。</summary>
        static void Steps(Transform parent, string name, float x, float z, Palette palette)
        {
            Transform steps = Group(parent, name);
            Platform(steps, "Step 0.5m", x - 2f, z, 2f, 2f, 0.5f, palette);
            Platform(steps, "Step 1.0m", x, z, 2f, 2f, 1f, palette);
            Platform(steps, "Step 1.5m", x + 2f, z, 2f, 2f, 1.5f, palette);
        }

        /// <summary>貨車出口的記號（東牆上）：之後動物逃脫的地方。現在只是標示位置，還不能走出去。</summary>
        static void ExitOnEastWall(Transform parent, string name, float zFrom, float zTo, Palette palette)
        {
            Transform exit = Group(parent, name);
            float middle = (zFrom + zTo) * 0.5f;
            float length = zTo - zFrom;
            CreateBlock(exit, "Door", new Vector3(39.75f, 1.4f, middle), new Vector3(0.2f, 2.8f, length), palette.Exit);
            RemoveCollider(CreateBlock(exit, "Floor Mark", new Vector3(38.2f, 0.02f, middle), new Vector3(2.8f, 0.04f, length), palette.Exit));
        }

        /// <summary>貨車出口的記號（南牆上）。</summary>
        static void ExitOnSouthWall(Transform parent, string name, float xFrom, float xTo, Palette palette)
        {
            Transform exit = Group(parent, name);
            float middle = (xFrom + xTo) * 0.5f;
            float length = xTo - xFrom;
            CreateBlock(exit, "Door", new Vector3(middle, 1.4f, -29.75f), new Vector3(length, 2.8f, 0.2f), palette.Exit);
            RemoveCollider(CreateBlock(exit, "Floor Mark", new Vector3(middle, 0.02f, -28.2f), new Vector3(length, 0.04f, 2.8f), palette.Exit));
        }

        /// <summary>
        /// 高櫃／貨架／有洞的樹籬：2.2 公尺高、0.8 公尺厚，擋視線用。alongX 決定長邊的方向。
        /// withPipe 為 true 時，正中間的底部開一個 0.8 x 0.8 公尺的洞，並有一條 3 公尺長的管道垂直穿過去，
        /// 只有兔子、浣熊、烏龜鑽得過，人類和猴子要繞過整個櫃子。
        /// </summary>
        static void TallCabinet(Transform parent, string name, float x, float z, float length, bool alongX, bool withPipe, Material material, Palette palette)
        {
            const float height = 2.2f;
            const float thickness = 0.8f;

            Transform cabinet = Group(parent, name);
            if (!withPipe)
            {
                Vector3 size = alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);
                CreateBlock(cabinet, "Body", new Vector3(x, height * 0.5f, z), size, material);
                return;
            }

            float half = k_VentSize * 0.5f;
            float sideLength = length * 0.5f - half;
            float offset = half + sideLength * 0.5f;
            float aboveY = (k_VentSize + height) * 0.5f;
            float aboveHeight = height - k_VentSize;
            if (alongX)
            {
                CreateBlock(cabinet, "Body West", new Vector3(x - offset, height * 0.5f, z), new Vector3(sideLength, height, thickness), material);
                CreateBlock(cabinet, "Body East", new Vector3(x + offset, height * 0.5f, z), new Vector3(sideLength, height, thickness), material);
                CreateBlock(cabinet, "Body Above Pipe", new Vector3(x, aboveY, z), new Vector3(k_VentSize, aboveHeight, thickness), material);
                VentAlongZ(cabinet, "Pipe", x, z, 1.5f, palette);
            }
            else
            {
                CreateBlock(cabinet, "Body South", new Vector3(x, height * 0.5f, z - offset), new Vector3(thickness, height, sideLength), material);
                CreateBlock(cabinet, "Body North", new Vector3(x, height * 0.5f, z + offset), new Vector3(thickness, height, sideLength), material);
                CreateBlock(cabinet, "Body Above Pipe", new Vector3(x, aboveY, z), new Vector3(thickness, aboveHeight, k_VentSize), material);
                VentAlongX(cabinet, "Pipe", x, z, 1.5f, palette);
            }
        }

        static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        static Transform CreateSpawnPoint(Transform parent, string name, Vector3 position, float yaw)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return point;
        }

        static GameObject CreateBlock(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = center;
            block.transform.localScale = size;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        /// <summary>牆上的開口（門或通風管口）。Center 是開口中心在牆上的位置。</summary>
        struct Opening
        {
            public float Center;
            public float Width;
            public float Height;
        }

        /// <summary>
        /// 目前三張地圖都沒有隔間牆，Door 和 VentHole 暫時用不到；之後要圍獨立的房間
        /// （例如關動物的實驗室）時，用 WallAlongX／WallAlongZ 加上這兩個開口來描述。
        /// </summary>
        static Opening Door(float center)
        {
            return new Opening { Center = center, Width = k_DoorWidth, Height = k_DoorHeight };
        }

        static Opening VentHole(float center)
        {
            return new Opening { Center = center, Width = k_VentSize, Height = k_VentSize };
        }

        /// <summary>沿著 X 方向的牆（固定 z），從 xFrom 到 xTo，可以留門或通風管口（開口的位置寫 x）。</summary>
        static void WallAlongX(Transform parent, string name, float z, float xFrom, float xTo, Material material, params Opening[] openings)
        {
            BuildWall(parent, name, true, z, xFrom, xTo, material, openings);
        }

        /// <summary>沿著 Z 方向的牆（固定 x），從 zFrom 到 zTo，可以留門或通風管口（開口的位置寫 z）。</summary>
        static void WallAlongZ(Transform parent, string name, float x, float zFrom, float zTo, Material material, params Opening[] openings)
        {
            BuildWall(parent, name, false, x, zFrom, zTo, material, openings);
        }

        static void BuildWall(Transform parent, string name, bool alongX, float fixedCoordinate, float from, float to, Material material, Opening[] openings)
        {
            var sorted = new List<Opening>(openings);
            sorted.Sort((a, b) => a.Center.CompareTo(b.Center));

            float cursor = from;
            int piece = 1;
            foreach (Opening opening in sorted)
            {
                float start = opening.Center - opening.Width * 0.5f;
                float end = opening.Center + opening.Width * 0.5f;

                // 開口前面的實心牆，以及開口上方補起來的那一塊（門楣）
                WallPiece(parent, name + " " + piece, alongX, fixedCoordinate, cursor, start, 0f, k_WallHeight, material);
                WallPiece(parent, name + " above opening " + piece, alongX, fixedCoordinate, start, end, opening.Height, k_WallHeight, material);
                cursor = end;
                piece++;
            }

            WallPiece(parent, name + " " + piece, alongX, fixedCoordinate, cursor, to, 0f, k_WallHeight, material);
        }

        static void WallPiece(Transform parent, string name, bool alongX, float fixedCoordinate, float from, float to, float bottom, float top, Material material)
        {
            float length = to - from;
            float height = top - bottom;
            if (length <= 0.01f || height <= 0.01f)
            {
                return;
            }

            float middle = (from + to) * 0.5f;
            float y = (bottom + top) * 0.5f;
            Vector3 center = alongX ? new Vector3(middle, y, fixedCoordinate) : new Vector3(fixedCoordinate, y, middle);
            Vector3 size = alongX ? new Vector3(length, height, k_WallThickness) : new Vector3(k_WallThickness, height, length);
            CreateBlock(parent, name, center, size, material);
        }

        /// <summary>
        /// 沿 X 方向的管道（動物專用通道）：中心在 (centerX, z)，往東西兩邊各伸出 halfLength 公尺。
        /// 內部寬高都是 0.8 公尺：兔子、浣熊、烏龜進得去，猴子和人類進不去。
        /// </summary>
        static void VentAlongX(Transform parent, string name, float centerX, float z, float halfLength, Palette palette)
        {
            Transform vent = Group(parent, name);
            float side = k_VentSize * 0.5f + 0.05f;
            float length = halfLength * 2f;
            CreateBlock(vent, "Side South", new Vector3(centerX, k_VentSize * 0.5f, z - side), new Vector3(length, k_VentSize, 0.1f), palette.Vent);
            CreateBlock(vent, "Side North", new Vector3(centerX, k_VentSize * 0.5f, z + side), new Vector3(length, k_VentSize, 0.1f), palette.Vent);
            CreateBlock(vent, "Top", new Vector3(centerX, k_VentSize + 0.05f, z), new Vector3(length, 0.1f, k_VentSize + 0.2f), palette.Vent);
        }

        /// <summary>沿 Z 方向的管道：中心在 (x, centerZ)，往南北兩邊各伸出 halfLength 公尺。</summary>
        static void VentAlongZ(Transform parent, string name, float x, float centerZ, float halfLength, Palette palette)
        {
            Transform vent = Group(parent, name);
            float side = k_VentSize * 0.5f + 0.05f;
            float length = halfLength * 2f;
            CreateBlock(vent, "Side West", new Vector3(x - side, k_VentSize * 0.5f, centerZ), new Vector3(0.1f, k_VentSize, length), palette.Vent);
            CreateBlock(vent, "Side East", new Vector3(x + side, k_VentSize * 0.5f, centerZ), new Vector3(0.1f, k_VentSize, length), palette.Vent);
            CreateBlock(vent, "Top", new Vector3(x, k_VentSize + 0.05f, centerZ), new Vector3(k_VentSize + 0.2f, 0.1f, length), palette.Vent);
        }

        /// <summary>正方體的箱子，放在地板上。</summary>
        static void Crate(Transform parent, string name, float x, float z, float size, Palette palette)
        {
            CreateBlock(parent, name, new Vector3(x, size * 0.5f, z), new Vector3(size, size, size), palette.Crate);
        }

        /// <summary>矮平台或台階，放在地板上。</summary>
        static void Platform(Transform parent, string name, float x, float z, float sizeX, float sizeZ, float height, Palette palette)
        {
            CreateBlock(parent, name, new Vector3(x, height * 0.5f, z), new Vector3(sizeX, height, sizeZ), palette.Crate);
        }

        /// <summary>
        /// 桌子：桌面底下淨高 0.85 公尺，兔子、浣熊、烏龜可以躲到桌子底下，猴子和人類不行。
        /// position 是桌子中心在地板上的位置，桌面 2.4 x 1.2 公尺（長邊沿 X）。
        /// 注意：角色要通過一個空隙，空隙至少要比角色身高多 0.1 公尺；只多幾公分時 CharacterController 會被卡住
        /// （浣熊 0.7 公尺就鑽不進淨高 0.75 公尺的桌子）。
        /// </summary>
        static void CreateTable(Transform parent, string name, Vector3 position, Palette palette)
        {
            const float clearance = 0.85f;
            const float topThickness = 0.1f;

            Transform table = new GameObject(name).transform;
            table.SetParent(parent, false);
            table.localPosition = position;

            CreateBlock(table, "Top", new Vector3(0f, clearance + topThickness * 0.5f, 0f), new Vector3(2.4f, topThickness, 1.2f), palette.Table);
            CreateBlock(table, "Leg 1", new Vector3(-1.1f, clearance * 0.5f, -0.5f), new Vector3(0.1f, clearance, 0.1f), palette.Table);
            CreateBlock(table, "Leg 2", new Vector3(1.1f, clearance * 0.5f, -0.5f), new Vector3(0.1f, clearance, 0.1f), palette.Table);
            CreateBlock(table, "Leg 3", new Vector3(-1.1f, clearance * 0.5f, 0.5f), new Vector3(0.1f, clearance, 0.1f), palette.Table);
            CreateBlock(table, "Leg 4", new Vector3(1.1f, clearance * 0.5f, 0.5f), new Vector3(0.1f, clearance, 0.1f), palette.Table);
        }

        // ------------------------------------------------------------------
        // 場景：連線與遊戲管理
        // ------------------------------------------------------------------

        static void BuildNetwork(NetworkPrefabsList prefabList)
        {
            var networkObject = new GameObject("NetworkManager");
            var manager = networkObject.AddComponent<NetworkManager>();
            var transport = networkObject.AddComponent<UnityTransport>();

            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            // 玩家物件不交給 NetworkManager 自動產生，而是由 PlayerSpawner（主機端）產生。
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = null;
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };
            EditorUtility.SetDirty(manager);
        }

        static void BuildGame(GameObject slotPrefab, GameObject avatarPrefab, GameObject matchStatePrefab, MapInfo[] maps)
        {
            var gameObject = new GameObject("Game");
            gameObject.AddComponent<ConnectionManager>();
            gameObject.AddComponent<LobbyUI>();
            gameObject.AddComponent<GameHud>();

            var registry = gameObject.AddComponent<MapRegistry>();
            registry.maps = maps;

            var spawner = gameObject.AddComponent<PlayerSpawner>();
            spawner.slotPrefab = slotPrefab;
            spawner.avatarPrefab = avatarPrefab;
            spawner.matchStatePrefab = matchStatePrefab;
        }
    }
}

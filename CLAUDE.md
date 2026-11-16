# 給 Claude 的專案說明

這是「動物逃脫」4v1 非對稱多人遊戲的 Unity 原型。動工前先讀：

- `Docs/遊戲企劃書_v0.1.md`：已定案的遊戲規則
- `Docs/原型開發計畫_v0.1.md`：技術選擇、架構原則、里程碑 M0–M6
- `README.md`：開啟與測試方式

## 目前狀態

- M0（專案建置與連線）**已完成並實際執行驗證**（2026-11-15）。
- M1（移動、身分、灰盒地圖）**已完成並實際執行驗證**（2026-11-15，Unity 6000.3.25f1，主視窗＋Multiplayer Play Mode 的 Player 2）：
  - 編譯 0 錯誤 0 警告；整段測試兩個視窗的 Console 都沒有錯誤或警告。
  - 進房間後選身分；人類只能一位（第二個人選會被拒絕並顯示提示）；「換身分」可重選。
  - 五種身分的碰撞尺寸、速度正確；Shift 慢走是一半速度。
  - 通風管：兔子、浣熊、烏龜通過，猴子和人類被擋在管口；人類站在出口可以把管子裡的動物堵住。
  - 用戶端的位置同步到主機；離開房間後重開，Player 2 會自動再加入，編號仍是 P2。
  - 名牌只在看得到本人時顯示；鏡頭被擠到貼近角色時（通風管裡）會隱藏自己的身體。
  - 三個視窗同時連線（1 人類＋兔子＋浣熊）跑過一次「人類進房間 → 動物鑽通風管逃走 → 人類被擋在管口」，三邊看到的位置一致。
  - 桌子底下：兔子、浣熊、烏龜鑽得進去，猴子不行。
  - 同一天晚上錄了一段 57 秒的三人示範影片（兔子視角），過程中又跑了一次完整流程：選身分、通風管來回、
    跳上平台與三階台階（兔子、浣熊）、躲桌下、名牌遮蔽，全部照劇本走完，沒有錯誤。
  - 其中兔子、人類、猴子、Player 2 的兔子是用真的鍵盤輸入測的；其餘用 `DevTestDriver` 的 walk 指令測。
  - 晚上改成「人類第一人稱、動物第三人稱」後重測：人類鏡頭在眼睛高度（1.62 m）、身體隱藏、身體朝向跟著視線並同步給別人、
    有準星；動物維持第三人稱；換身分時視角跟著換；人類照樣被通風管擋住。這一輪都是用 DevTestDriver 測的，
    第一人稱用滑鼠實際轉視角的手感還沒有人工確認。
- **地圖（2026-11-16 定案：三張都保留）**：使用者覺得原本 40 x 30 的五房間地圖太小；改大之後又說概念應該是
  「研究室或溫室、不用隔板」；做了一個開放大廳又說太簡單。最後先畫了三種格局給使用者看，使用者說「都做出來看看」，
  所以現在場景裡有**三張地圖**（A 圓環、B 長廊、C 島群，都是 80 x 60 公尺、沒有隔間牆、共用地板和外牆），
  執行時只開一張。大廳可以選，主機在遊戲中也能換（`MatchState.SetMap`，所有人回到選身分）。使用者 2026-11-16 看過之後說
  **「三張地圖都保留」**，所以之後每個會放東西到地圖上的功能（M3 的籠子、M4 的控制台和出口…）**三張都要放、三張都要測**；
  盡量做成像設施區那樣「給範圍自動擺」的積木，不要三張各寫一份座標。密度要不要再加還沒談。主題是使用者選的「中央溫室＋周邊設施」，
  指定要有「動物籠舍區」和「水池與大型植栽」。
  - 三張都用 DevTestDriver 實測過：水擋住所有人、人類走橋、小動物走水上管道（猴子和人類被擋，跳到管道頂上也過不去）；
    高櫃／貨架／樹籬底下的管道；植栽桌和籠舍頂蓋底下（浣熊進得去、猴子不行）；1.45 公尺的圍欄和植栽箱
    （人類、浣熊跳不過，猴子、兔子跳得過）；換地圖後第二位玩家同步、兩邊都在新地圖的出生點出生。
  - 沒有人工試玩過；路線順不順、會不會太空或太擠，要等使用者的感想。
- **還沒測的**：線上房間（Relay，專案尚未連結 Unity Cloud）；手把操作；第一人稱用滑鼠轉視角的手感；
  M2 的按鍵用真的鍵盤滑鼠按；有網路延遲時揮網準不準（主機是用它看到的位置判定，沒有做延遲補償）。
- M2（追與抓）**已完成並實際執行驗證**（2026-11-15 晚上，主視窗＋Player 2＋Player 3，全部用 DevTestDriver）：
  - 揮網：正前方、範圍內網住最近的一隻；背後、太遠（3.4 公尺）、隔著樹籬都揮空；冷卻中和扛著時主機不理。
  - 被網住不能走；連按掙脫（地上約 18 下、肩上約 30 下）；掙脫後 3 秒保護，這期間再揮會回報 Protected。
  - 扛起（要走到 2 公尺內，比網子的 2.6 公尺短，所以在最遠處網住後要再走一步）、放下（還在網子裡、掙脫值保留）、
    扛著時速度 3.5 公尺／秒、被扛的動物在三個視窗看到的位置一致（都在人類肩上）。
  - 肩上掙脫：落在人類背後、人類踉蹌 1.2 秒。隊友推撞：全速撞上才算（慢走不算），兔子落地、人類踉蹌 2 秒。
  - 主機當人類、用戶端當人類兩種方向都測過；被網住時換身分會被擋；人類扛著時自己換身分，動物會被放掉。
  - 錄了一段 28 秒的示範影片（左人類第一人稱、右動物第三人稱）。
  - **還沒有人用真的鍵盤滑鼠玩過 M2**：按鍵（滑鼠左鍵、E、空白鍵）是照 M1 同樣的寫法接的，但沒有實際按過。
    數值（範圍、冷卻、掙脫難度、保護時間）全是第一版的猜測，等使用者試玩後調。
- 下一步：M3（捕捉籠與救援）。

### M1 和開發計畫不同的地方（都是刻意的，使用者還沒逐項確認）

- 通風管內部是 0.8 m（計畫寫 0.6 m）：這樣兔子 0.6、浣熊 0.7、烏龜 0.5 進得去，猴子 0.9 和人類進不去。
- 鏡頭是自己寫的 `PlayerCamera`，沒有用 Cinemachine（套件有裝但沒用到）。
- **視角：人類第一人稱、動物第三人稱**（使用者 2026-11-15 決定，參考《黎明死線》）。原本企劃寫全員第三人稱；
  中間試過全員第一人稱，使用者覺得怪（動物視線太低、看不到背後和自己）才改成現在這樣。
  設定在 `GameConfig` 的 `humanViewMode`、`animalViewMode`，`PlayerCamera` 每一格都會讀，Play 中可以直接切。
  第一人稱時：自己的身體一律隱藏（只留影子）、身體朝向跟著視線、畫面中央有準星、視野 70 度、上下各可看 80 度。
- 身分是「進房間之後」在遊戲內選，不是在大廳選；動物可以重複選同一種（企劃書這點還是待定）。
- 走／跑的區分做成「按住 Shift 慢走」（速度 ×0.5），之後腳步聲、被發現的機制可以掛在這上面。

## 環境

- Unity 6000.3.25f1，URP 17.3
- Netcode for GameObjects 2.11.2（主機模式，Client-Server）
- Multiplayer Services SDK 2.4.1（Session ＋ Relay）
- Input System 1.20.1，Cinemachine 3.1.7，Multiplayer Play Mode 2.0.2
- Active Input Handling = Both（原型介面用 IMGUI，執行時需要舊輸入後端才收得到事件）

## 架構規則

1. **主機說了算**：捕捉、扛起、關籠、救援、計時器、混亂值、勝負全部由主機判定，玩家端只送出操作（RPC）。
   例外：角色移動由擁有者端計算（NetworkTransform 的 AuthorityMode = Owner），手感才不會延遲。
2. **數值不寫死**：所有可調數值放 `GameConfig`（ScriptableObject，`GameConfig.Instance` 取得）。新增數值時加欄位並給預設值。
3. **場景與預製物用程式產生**：`Assets/_Project/Scripts/Editor/PrototypeBuilder.cs` 負責建立 `Main.unity`、
   `PlayerSlot.prefab`、`Avatar.prefab`、材質、URP 設定、NetworkPrefabs 清單，並刪掉舊版留下的資源（`k_ObsoleteAssets`）。
   要改場景或預製物就改這支程式；新增含 NetworkObject 的預製物時，要在 `BuildPrefabList` 一併登記。
   不要手寫 `.unity`、`.prefab`、`.meta` 檔。地圖的座標和尺寸都在 `BuildMaps` 和三個 `BuildMapRing`／`BuildMapSpine`／
   `BuildMapIslands` 裡（X 朝東、Z 朝北，單位公尺，原點在正中央）。四種設施區是給範圍就自動排滿的積木
   （`BuildNursery`、`BuildCages`、`BuildLab`、`BuildWarehouse`），其他是 `Planter`、`Bush`、`TreeProp`、`Water`、
   `WaterPipeAlongX/Z`、`Bridge`、`TallCabinet` 這些小積木。
   - **地圖切換**：場景裡每張地圖是 `Maps/` 底下的一個物件（含自己的出生點），`MapRegistry`（Core）記錄清單並負責只開一張；
     選擇存在 PlayerPrefs。`MatchState`（Match，網路物件，主機開房時產生）把「這一局用哪張」同步給所有人；
     之後計時器、混亂值這類整局共用的狀態也放這裡。出生點由 `PlayerSpawner` 向 `MapRegistry.Active` 拿。
   各身分的外觀是 `Avatar.prefab` 底下名為 `Visual_身分` 的子物件，用基本形狀拼的；改了 `GameConfig` 的身高、半徑、顏色後要重建場景。
   - **追與抓都在 `PlayerCapture`**（和 `PlayerAvatar`、`PlayerMovement` 掛在同一個角色上）。判定全在主機：
     玩家端只呼叫 `RequestSwing`／`RequestInteract`／`RequestStruggle`（鍵盤和測試工具走同一個入口），推撞由撞人的那一方回報、
     主機再確認距離。狀態用 NetworkVariable 同步；時間一律用 `PlayerCapture.Now`（主機時間）。
     被扛著的動物不靠位置同步，而是每台電腦各自在 LateUpdate 把牠擺到人類肩上；放下時主機先寫 `DropPoint` 再改 `State`
     （**`DropPoint` 一定要宣告在 `State` 前面**，Netcode 是照宣告順序套用並通知的），擁有者收到後自己瞬移過去。
     M3 的「關進籠子」可以照同一套做：多一個狀態、主機指定位置、擁有者自己搬過去。
   - **玩家有兩種網路物件**：`PlayerSlot`（每個連線一個，記身分與座位號碼，是 Netcode 的 player object，離開房間才消失）
     和 `PlayerAvatar`（場上的角色，換身分時整個重新產生）。之後「被抓次數、是否逃出」放 `PlayerSlot`，「現在的動作狀態」放 `PlayerAvatar`。
   - 兩者都由主機端的 `PlayerSpawner` 產生（`NetworkConfig.PlayerPrefab` 為空，不用自動產生）。
   - 產生前要帶給所有人的初始值，用「先填 `PendingXxx` 欄位，在 `OnNetworkSpawn` 裡由主機寫進 NetworkVariable」的做法
     （`PendingRole`、`PendingSeat`）。在產生前直接寫 NetworkVariable 會跳警告。
   - 玩家編號（P1、P2…）是主機分配的座位號碼 `PlayerSlot.Seat`（沒人用的最小號碼），**不要用 `OwnerClientId + 1`**：
     重開房間後 clientId 會跳號。動物出生點也是照座位號碼分配。
4. **物理只同步必要的東西**：影響玩法的物件才加 NetworkObject；純裝飾效果各自在本機播放。
5. **用狀態機**：動物狀態、人類狀態、比賽狀態照開發計畫第 2.4 節。
6. **一次只做一個里程碑**，每個里程碑附上測試清單，通過後才往下。

## 寫程式時的注意事項

- 命名空間：`AnimalEscape.Core`、`.Networking`、`.Players`、`.UI`、`.EditorTools`、`.DevTools`。
- 同時 `using System;` 和 `using UnityEngine;` 的檔案，`Object` 和 `Random` 要寫全名，否則會有歧義錯誤。
- 不要 `using Unity.Services.Multiplayer;` 整個命名空間，用別名 `using Mps = Unity.Services.Multiplayer;`，避免和 Netcode 的型別撞名。
- 遊戲操作一律用 Input System（`InputAction`、`Keyboard.current` 等），不要用舊的 `UnityEngine.Input`。
- 繼承 `NetworkBehaviour` 的類別不要自己宣告 `OnDestroy()`；需要時用 `public override void OnDestroy()` 並呼叫 `base.OnDestroy()`。
- 靜態欄位要用 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 重設，才能支援關閉 Domain Reload。
- 套件 API 以實際安裝的版本為準。不確定時查 `Library/PackageCache/` 裡的原始碼，不要憑印象寫。
- **CharacterController 會記住自己的位置**。物件被直接設定位置之後（包含 Netcode 在用戶端生成物件時），
  要先把 CharacterController 停用再啟用，否則下一次 `Move()` 會把角色拉回舊位置。之後做瞬移、被扛走、關進籠子都要注意。
- **讀取輸入前先檢查 `Application.isFocused`**。同一台電腦開多個視窗測試時，沒被點選的視窗不應該回應輸入。
- **IMGUI 不要指定系統字型**（例如用 `Font.CreateDynamicFontFromOSFont` 指定 PingFang）。在 Unity 6.3 會載入失敗，
  整個介面的文字都會錯亂。預設字型就能正常顯示中文。
- 遊戲畫面鎖定滑鼠（`Cursor.lockState = Locked`）時，IMGUI 按鈕點不到；要先按 Esc 放開滑鼠。
- **留給角色通過的空隙，至少要比角色身高多 0.1 公尺**。只多幾公分時 CharacterController 會被卡住
  （實測：0.7 公尺的浣熊鑽不進淨高 0.75 公尺的桌子，但過得了 0.8 公尺的通風管）。
- **跳得上去的高度 = 跳躍高度 + stepOffset**（跳到最高點時 CharacterController 還會再「踩上」一段，
  stepOffset = min(0.3, 身高 x 0.35)）：人類 1.3、浣熊 1.25、烏龜 0.68、猴子 1.6、兔子 1.8 公尺。
  實測人類上得了 1.3 公尺、上不了 1.4 公尺。要「擋人類、放兔子和猴子」的東西做 1.45 公尺（現在的矮圍欄和植栽箱）。
- **水**是「看得見的薄水面（沒有碰撞）＋看不見的擋牆」。擋牆放在 Water 圖層（`GameLayers.Water`），
  鏡頭的防穿牆和名牌的遮蔽判斷都用 `GameLayers.SightMask` 略過它，所以隔著水看得到對岸。之後新增視線類的判斷也要用這個遮罩。
  橫跨水面的管道要用 `WaterPipeAlongX/Z`（管道上方多一道擋牆），不然人類可以跳到管道頂上走過去。
- 之後做揮網等「朝準星方向」的動作時，人類用 `PlayerCamera` 的方向（第一人稱）；動物是第三人稱，
  角色面向的是移動方向而不是鏡頭方向，設計動物的互動時要記得這個差別。
  （揮網目前只看身體的水平朝向，不看上下看的角度。）
- **位置同步只同步水平轉向（Y 軸）**，X、Z 的傾斜不會傳，也不會被改回來。所以在本機把角色放倒、轉歪（例如被扛著時躺在肩上）
  之後，**每台電腦都要自己扶正**：擁有者用 Teleport 帶正的方向，其他電腦在狀態改變時直接設 `transform.rotation`。
  漏掉的話，那個角色在別人的畫面上會一直躺著，自己的畫面卻是正常的（使用者在 M2 試玩影片裡發現的，`report` 的「傾斜=」可以查）。
  M3 做關籠、M5 做疊羅漢和投擲時，只要動到角色的姿勢都要記得這件事。
- **別人的角色不是實心的**（2026-11-15 實測）：每個角色只在主人的電腦上做碰撞，別人的角色是照同步的位置直接擺過來的。
  走向「站著不動」的人會被擋住；但兩個人迎面對走時，對方是直接跳過來疊在我身上，CharacterController 下一格就不把他當障礙，
  兩個人會互相穿過去。現在 `PlayerMovement.PushOutOfOtherCharacters` 每一格移動完會把重疊的自己推開，兩邊各自做就頂得住
  （沒有對齊時會從旁邊滑過去，這是正常的）。**不要用 `OnControllerColliderHit` 判斷「撞到別的玩家」**，會漏掉一半；
  要判斷接觸就用距離（推撞就是這樣做的）。
- `OnGUI` 每一格會被呼叫好幾次。要做物理查詢或比較花時間的事（例如名牌的遮蔽判斷），只在 `EventType.Repaint` 做。
- **會洩漏位置的介面都要做遮蔽判斷**（名牌、之後的標記、警報圖示）：這是躲藏遊戲，穿牆顯示等於作弊。
  共用的判斷在 `GameHud.IsNameTagVisible`。
- 虛擬玩家視窗要「等主視窗開好房間」才自動加入：主機開房成功時會寫一個記號檔 `Temp/animal-escape-local-host.txt`，
  關房時刪掉，虛擬玩家看到記號檔才連線。不要改回「一進 Play 就一直嘗試連線」，那樣沒有主機時會逾時報錯。

## 除錯與測試

- `Logs/claude-compile.txt`：最近一次編譯結果，第一行是 `STATUS: OK` 或 `STATUS: ERRORS`。
- `Logs/claude-console.txt`：編輯器 Console 訊息（含 Play 模式的錯誤與堆疊）。
- Multiplayer Play Mode 的虛擬玩家（Player 2～4）是另一個 Unity 程序，它的記錄檔在 `Library/VP/<代號>/Logs/` 底下，檔名相同。

### 測試遙控器（DevTestDriver）——優先用這個，不要搶使用者的螢幕

`Assets/_Project/Scripts/Editor/DevTestDriver.cs` 會讀主專案的 `Logs/claude-commands.txt`，一行一個指令，
結果寫到 `Logs/claude-test-results.txt`，畫面存到 `Logs/shots/*.png`。主視窗和虛擬玩家視窗都會讀同一個檔。
Unity 在背景也照常運作，所以整個「改程式 → 編譯 → 進 Play → 多人測試 → 截圖」都可以只靠寫檔完成。

```
編號 對象 動作 參數…
12 main refresh            重新匯入並編譯（之後讀 claude-compile.txt）
13 main play               進 Play（虛擬玩家會跟著進）
14 main host               建立本機房間（虛擬玩家自動加入）
15 P1 role Rabbit          選身分；None 表示回到選身分畫面
16 P1 walk -12 8           直線走到 (x, z)，回報「走到了」或「被擋住」和最後位置
17 P1 walk 10 -7.7 0.4 jump    後面可加：力道 0～1、jump（起步時跳）、sneak（慢走）、thru（只是路過，不減速）
18 P1 jump                 原地跳一下
19 P2 teleport 2 0.05 8 270
20 P1 look 90 10 0.8       鏡頭 yaw、pitch；第三個數字是用幾秒平順地轉過去
21 P1 follow on            鏡頭自動轉向角色前進的方向（off 關掉）；只在程式控制移動時才會轉，不影響鍵盤操作
   main view human third   暫時切換人類或動物的視角（first／third），只改執行中的設定，測完要切回去
   main map b              換地圖（a／b／c）。沒開房時改大廳的選擇；開房後只有主機能換，所有人回到選身分
   P1 swing                （人類）揮網
   P1 interact             （人類）扛起附近被網住的動物；已經扛著就是放下
   P2 struggle 25 10       （動物）連按掙脫 25 下、每秒 10 下；掙脫了就提早結束
   all nethash             列出連線設定雜湊和每個網路預製物的識別碼（查 NetworkConfig mismatch 用）
22 P1 shot name            存一張遊戲畫面（含介面）到 Logs/shots/
   main camshot name       用臨時鏡頭拍一張沒有介面的畫面：不給位置是正上方俯視整張地圖（上北右東）；
   main camshot name 0 46 -52 0 48 55    給 x y z yaw pitch [視野] 就從那裡拍。看地圖格局用這個最快
23 P1 record take1 60 30   開始錄影 60 秒、每秒 30 張（不會等它錄完），存到 Logs/recordings/take1/
24 main res 1280 720       把 Game 視窗固定成指定解析度；錄完一定要 main res restore 還原
25 all t0 1794742760.4     多個視窗對時：設定共同起點（1970 年起算的秒數）
26 P2 until 8.2            等到「起點之後第 8.2 秒」才往下做
27 all report              各視窗看到的連線狀態、所有角色位置、名牌是否顯示、身體是否隱藏
28 main stop
```

- 對象：`main`、`vp`、`P1`～`P5`、`all`。編號要一直往上加（每個視窗只執行比上次大的編號）。
- 一個視窗的指令會照順序做完；**不同視窗之間不會互相等**，要等另一個視窗做完，就分兩次寫入、中間讀結果。
- 進出 Play 或重新編譯會重新載入程式，當下跑到一半的指令（例如 wait）會直接被丟掉。`play` 之後先等幾秒再對 `vp`／`all` 下指令。
- `walk`、`jump` 是透過 `PlayerMovement.SetExternalInput()`／`Jump()` 餵方向給角色，速度、轉身、重力、碰撞都走和鍵盤操作同一段程式，
  只差沒經過「讀鍵盤」那一步。新增或改動「輸入 → 動作」的程式時，還是要請使用者實際按一次。
  （`SetExternalInput` 之後也可以拿來做電腦控制的角色。）
- M2 需要測網子、掙扎等動作時，照同樣的做法：遊戲程式開一個公開方法，這支程式加對應的指令。
- **錄示範影片**：`python3 Tools/record_demo.py take1` 會寫入一整段三人劇本（步驟寫在那支程式開頭）。
  畫面是一張張 JPG，打包後搬到雲端用 ffmpeg 合成（那邊有 Noto Sans CJK TC 可以上中文字幕）。
  2026-11-15 第一次錄就一次過。**劇本裡的座標是舊的 40 x 30 地圖，現在不能直接用**，地圖定案後要重寫。
  錄完的 JPG 很佔空間（一分鐘約 100 MB），在 `Logs/recordings/`；影片做完就可以刪（2026-11-16 使用者同意後清過一次 1.3 GB）。
  刪檔要先用 device_request_delete_permission 請使用者按確認。
- `teleport` 之後角色有一瞬間不在地上，緊接著的 `walk … jump` 會跳不起來；中間加 `wait 0.5`。
- **改了預製物的內容（加元件、加子物件）之後，虛擬玩家視窗會連不進來**，主視窗出現 `NetworkConfig mismatch`：
  它手上還是舊的預製物，識別碼對不上。現在 `rebuild` 做完會自動再編譯一次讓所有視窗重新載入，等 `claude-compile.txt`
  的時間更新了再 `play`。還是連不上時，兩邊各下一次 `nethash` 比對是哪個預製物不一樣。
- 測追與抓時的坑：`swing` 網住的是**最近的**那一隻，旁邊站著別的動物會網錯；`teleport` 不管狀態，被網住的也搬得動
  （所以開始一段劇本前先 `report` 確認大家都是自由的）；`interact` 太早按（網子還沒判定）不會扛起來；
  鏡頭 `follow on` 轉向需要時間，剛轉彎就 `swing` 可能因為還沒面向目標而揮空。
- 要第三個視窗時，在 Multiplayer Play Mode 視窗勾選 Player 3（要用電腦操作功能在背景點，約 30 秒才會開好），
  **測完記得取消勾選**，不然使用者每次按 Play 都會多開一個視窗。
- 指令的最後一行寫 `all report` 時，虛擬玩家視窗常常先回報，不代表主視窗前面的指令做完了；要等主視窗的結果。
- `res` 會用反射去動 Game 視窗的解析度清單（編輯器沒有公開的還原方法）；Unity 改版後如果還原失敗，手動把 Game 視窗選回 Free Aspect。
- 第一次要讓 Unity 載入新版的 DevTestDriver，或它本身編譯壞掉時，才需要用電腦操作功能在背景按選單
  （`Edit > Play Mode > Play`、`Assets > Refresh`、`Tools > Animal Escape > 重建原型場景`）。

### 不要在使用者正在玩的那一局裡跑測試

2026-11-15 發生過：我自己按了 Play 跑測試，使用者同時切進 Unity 開始玩，我的指令（`follow on`）讓他的鏡頭自己亂轉，
他以為是遊戲壞了。之後的做法：

- 開始跑測試前先 `main report`：如果已經在 Play 而且不是我剛開的，表示使用者在用，先問或先等，不要下指令。
- 要跑一段測試之前，先在對話裡跟使用者說「接下來幾分鐘不要操作 Unity」，跑完再說好了。
- 測完一定要 `main stop`，不要把我的測試局留著。
- 改完程式或重建場景之後，虛擬玩家視窗要過一陣子才會跟上（實測最久要幾分鐘）。它還沒跟上就開房，
  會出現 `[Netcode] NetworkConfig mismatch` 而且加不進來；這時 `main stop` 再重新 `play`、`host` 即可。
  開房後用 `all report` 確認玩家數對了再開始測。

### 用電腦操作功能時

- 只用背景操作（`computer_app_*`）。**不要再要求全螢幕控制**：2026-11-15 測 M1 時兩次被中斷（緊急停止），
  而且使用者同時在用電腦時，滑鼠一動鏡頭就會轉，測試結果不可靠。需要按住按鍵的測試改用上面的遙控器。
- 使用者的輸入法是注音，遠端送出的字母鍵會被送成別的按鍵；真的要送按鍵時用方向鍵和具名按鍵。
- 讀數值的方法：在 Hierarchy 點物件，把 Inspector 拉寬後放大來看；看完把版面拉回去。

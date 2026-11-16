#!/usr/bin/env python3
"""
【注意】這份劇本的座標是 2026-11-15 白天那張 40 x 30 公尺的舊地圖。當天晚上地圖放大成 80 x 60 公尺、格局也換了，
劇本還沒有跟著重寫，現在直接執行角色會撞牆。要重錄的話，請先照新地圖（PrototypeBuilder.BuildLevel）改下面的座標。

錄一段約一分鐘的示範影片（兔子視角：打招呼 → 人類進房 → 鑽通風管 → 躲桌下 → 跳台階 → 換房間躲）。

這支程式只負責把「劇本」寫進測試遙控器的指令檔（Logs/claude-commands.txt），
真正執行的是 Unity 裡的 DevTestDriver。需要三個視窗：主視窗（P1 兔子，被錄影的那個）、
Player 2（人類）、Player 3（浣熊）。

步驟（指令都是寫進 Logs/claude-commands.txt，格式見 DevTestDriver.cs 開頭的說明）：

  1. Multiplayer Play Mode 視窗勾選 Player 2 和 Player 3。
  2. main play → 等約 10 秒 → main host → 等三個視窗都連上（all report 看玩家數=3）。
  3. main res 1280 720            把 Game 視窗固定成 16:9
     P2 role Human
     P3 role Raccoon
     P3 teleport -1.5 0.05 11 90
     （P1 先不要選身分，影片開頭會拍到選身分的畫面）
  4. python3 Tools/record_demo.py take1      寫入劇本，5 秒後開拍，全長 60 秒
  5. 錄完後：P1 follow off、main res restore、main stop
  6. 畫面在 Logs/recordings/take1/f_000001.jpg…（每秒 30 張），用 ffmpeg 合成：
       ffmpeg -framerate 30 -i Logs/recordings/take1/f_%06d.jpg -t 57.5 \\
         -vf "scale=in_color_matrix=bt601:out_color_matrix=bt709:in_range=pc:out_range=tv,format=yuv420p" \\
         -c:v libx264 -crf 19 -preset slow -movflags +faststart demo.mp4

劇本裡的座標是照 PrototypeBuilder.BuildLevel 的灰盒地圖寫的；地圖改了，這裡的座標要跟著改。
時間（until 後面的秒數）是從開拍起算；三個視窗靠同一個起點（t0）對時。
"""
import os
import re
import sys
import time

LOGS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Logs')
take = sys.argv[1] if len(sys.argv) > 1 else 'take'
lead = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0

# P1：兔子（主視窗，鏡頭跟著牠）
P1 = f"""
until 0
record {take} 60 30
until 3.0
role Rabbit
follow on
until 3.7
walk 0.5 10.4
jump
until 5.4
walk -0.4 11.2 0.5
until 6.5
jump
until 7.7
look 180 12 0.8
until 10.0
walk -2 8 thru
walk -12 8
until 12.5
look 90 10 0.7
until 14.3
walk -11 6.7
walk -11 4.7 0.4 jump
until 16.2
look 216 14 0.8
until 17.4
jump
until 19.2
walk -10 8 thru
walk 2 8
until 22.6
walk 1.0 12.0 thru
walk 0.45 13.5 0.5
until 24.8
look 180 5 0.8
until 26.5
look 160 5 1.2
until 28.0
look 185 4 1.0
until 31.2
walk -0.9 13.5 thru
walk -1.6 11 thru
walk 0 4.5 thru
walk 0 0.4 thru
walk 10 0.4 thru
walk 10 -5.7
walk 10 -7.7 0.4 jump
walk 11.9 -8 0.4 jump
until 38.6
look 0 28 0.8
until 40.0
jump
until 41.6
jump
until 42.4
look 297 25 1.0
until 45.5
jump
until 47.0
walk 11.9 -10.8 thru
walk 1.5 -9 thru
walk -3 -9 thru
walk -12.6 -9 thru
walk -14 -7 0.5
until 53.5
look 100 6 1.0
until 60.5
report
"""

# P2：人類（追兔子，兩次被擋在通風管口，最後追進房間 D）
P2 = """
until 3.5
walk -0.3 0.3
until 8.2
walk 0 4.6
until 10.2
walk -1.2 8 thru
walk -12 8
until 12.2
jump
until 13.0
walk 0 5 thru
walk 0 0.3 thru
walk -13 0.3 thru
walk -13 3.6
until 18.6
walk -12.95 4.7 0.7
until 19.5
walk -12.95 6.6 thru
walk -10.2 8 thru
walk -3 8
until 21.4
jump
until 22.0
walk -13 6.5 thru
walk -13 0.3 thru
walk 0 0.3 thru
walk 0 5 thru
walk 0.9 11.7
until 29.6
jump
until 30.6
jump
until 31.9
walk 0 4.5 thru
walk 0 0.4 thru
walk 10 0.4 thru
walk 10 -5.0 thru
walk 11.9 -6.0
until 38.8
jump
until 39.9
jump
until 41.0
jump
until 42.0
walk 8.2 -5.9
until 43.4
jump
until 44.6
walk 10 -3 sneak
until 48.6
walk 6 -6 thru
walk 6 -9.6 thru
walk 1.5 -9 thru
walk -3 -9 thru
walk -9.5 -8.2
until 60.5
report
"""

# P3：浣熊（躲桌下、繞去房間 E 爬台階、最後躲進房間 D 的桌下）
P3 = """
until 1.5
jump
until 7.0
jump
until 9.9
walk 1.6 12.3 thru
walk 1.6 13.5 0.6
until 31.3
walk 2.9 13.5 thru
walk 5 10.5 thru
walk 7.5 10 thru
walk 10 9
until 36.5
walk 10.5 5 thru
walk 13 3 thru
walk 13 0.5 thru
walk 10.3 -0.8 thru
walk 10 -3.5 thru
walk 8 -6.2
until 41.4
walk 8.4 -8 0.4 jump
until 43.0
walk 10 -8 0.4 jump
until 45.0
jump
until 48.0
walk 10 -10.8 thru
walk 1.5 -9.2 thru
walk -3 -9.5 thru
walk -12.6 -9.6 thru
walk -14 -9.6 thru
walk -14 -11 0.5
until 60.5
report
"""

path = os.path.join(LOGS, 'claude-commands.txt')
existing = open(path, encoding='utf-8').read() if os.path.exists(path) else ''
ids = [int(m.group(1)) for m in re.finditer(r'^(\d+)\s', existing, re.M)]
next_id = max(ids, default=0) + 1

# 共同的起點：現在往後幾秒（1970 年起算的秒數），三個視窗都等到這一刻才開始。
t0 = time.time() + lead
lines = [f"{next_id} all t0 {t0:.3f}"]
next_id += 1
for who, script in (('P1', P1), ('P2', P2), ('P3', P3)):
    for command in script.strip().splitlines():
        lines.append(f"{next_id} {who} {command.strip()}")
        next_id += 1

os.makedirs(LOGS, exist_ok=True)
with open(path, 'a', encoding='utf-8') as f:
    f.write("\n".join(lines) + "\n")
print(f"已寫入 {len(lines)} 個指令，{lead:g} 秒後開拍；畫面會存到 Logs/recordings/{take}/")

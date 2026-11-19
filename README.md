# 動物逃脫 — 下載用分支

這個分支只放打包好的遊戲，程式碼在 `main` 分支。目前的版本：2026-11-19。

## Windows

在 PowerShell 貼上這一行：會下載遊戲、解壓縮到桌面的 `AnimalEscape` 資料夾，然後打開遊戲。之後有新版本，再執行一次同一行就會更新。

```powershell
$ProgressPreference='SilentlyContinue'; $zip="$env:TEMP\AnimalEscape-Windows.zip"; Invoke-WebRequest -Uri "https://raw.githubusercontent.com/peggychiux/AnimalEscape/download/AnimalEscape-Windows.zip" -OutFile $zip; $desk=[Environment]::GetFolderPath('Desktop'); Expand-Archive -Path $zip -DestinationPath $desk -Force; Start-Process (Join-Path $desk 'AnimalEscape\AnimalEscape.exe')
```

或直接下載 [AnimalEscape-Windows.zip](https://raw.githubusercontent.com/peggychiux/AnimalEscape/download/AnimalEscape-Windows.zip)，整個解壓縮後執行 `AnimalEscape\AnimalEscape.exe`（不能只拿出 exe）。
第一次打開如果出現「Windows 已保護您的電腦」，按「其他資訊」→「仍要執行」；跳出防火牆詢問就按「允許」。

**大家要用同一個版本**：這一版和 11/18 以前的舊版不能一起玩，請都更新到最新。

一起玩：房主按「開始遊戲 › 建立房間」，連線方式選「線上（給代碼）」，把 6 碼代碼傳給大家；其他人按「開始遊戲 › 加入房間」，輸入代碼後按「加入」。

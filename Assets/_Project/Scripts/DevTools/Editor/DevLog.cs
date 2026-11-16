using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AnimalEscape.DevTools
{
    /// <summary>
    /// 把編譯結果和 Console 訊息另外寫一份到專案的 Logs 資料夾，
    /// 讓 Claude 可以直接讀檔找問題，不用每次請人複製貼上錯誤訊息。
    ///   Logs/claude-compile.txt ：最近一次編譯的錯誤與警告
    ///   Logs/claude-console.txt ：這次開啟編輯器以來的 Console 訊息
    /// 這支程式放在獨立的組件裡，其他程式有編譯錯誤時它仍然可以運作。
    /// </summary>
    [InitializeOnLoad]
    internal static class DevLog
    {
        const string k_Directory = "Logs";
        const string k_CompileFile = "Logs/claude-compile.txt";
        const string k_ConsoleFile = "Logs/claude-console.txt";
        const string k_SessionKey = "AnimalEscape.DevLog.SessionStarted";
        const string k_RetryKey = "AnimalEscape.DevLog.RecompileRequested";
        const long k_MaxConsoleBytes = 1024 * 1024;

        static readonly object s_Lock = new object();
        static readonly StringBuilder s_CompileMessages = new StringBuilder();
        static int s_ErrorCount;
        static int s_WarningCount;

        static DevLog()
        {
            // Unity 會另外開幾個背景程序匯入資源，它們也會載入這支程式；只在主編輯器裡記錄，避免互相蓋掉記錄檔。
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Application.logMessageReceivedThreaded += OnLogMessage;

            if (!SessionState.GetBool(k_SessionKey, false))
            {
                SessionState.SetBool(k_SessionKey, true);
                WriteFile(k_ConsoleFile, "=== 編輯器啟動 " + Timestamp() + "（Unity " + Application.unityVersion + "）===\n", false);
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                // 專案一開啟就有編譯錯誤時，錯誤發生在這支程式載入之前，所以要求重新編譯一次來補抓訊息。
                if (!SessionState.GetBool(k_RetryKey, false))
                {
                    SessionState.SetBool(k_RetryKey, true);
                    EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
                }
            }
            else if (!File.Exists(k_CompileFile) || FirstLine(k_CompileFile).Contains("ERRORS"))
            {
                WriteFile(k_CompileFile, "STATUS: OK\ntime: " + Timestamp() + "\n（編輯器載入時沒有編譯錯誤）\n", false);
            }
        }

        static void OnCompilationStarted(object context)
        {
            s_CompileMessages.Length = 0;
            s_ErrorCount = 0;
            s_WarningCount = 0;
        }

        static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return;
            }

            foreach (CompilerMessage message in messages)
            {
                bool isError = message.type == CompilerMessageType.Error;
                bool isWarning = message.type == CompilerMessageType.Warning;
                bool inProject = message.file != null && message.file.Replace('\\', '/').StartsWith("Assets/");

                // 套件裡的警告不記錄，只記錄錯誤和專案自己的警告。
                if (!isError && !(isWarning && inProject))
                {
                    continue;
                }

                if (isError)
                {
                    s_ErrorCount++;
                }
                else
                {
                    s_WarningCount++;
                }

                s_CompileMessages.Append(isError ? "[error] " : "[warning] ");
                s_CompileMessages.Append(message.message);
                s_CompileMessages.Append('\n');
            }
        }

        static void OnCompilationFinished(object context)
        {
            var report = new StringBuilder();
            report.Append(s_ErrorCount > 0 ? "STATUS: ERRORS\n" : "STATUS: OK\n");
            report.Append("time: ").Append(Timestamp()).Append('\n');
            report.Append("errors: ").Append(s_ErrorCount).Append(", warnings: ").Append(s_WarningCount).Append('\n');
            report.Append('\n');
            report.Append(s_CompileMessages);
            WriteFile(k_CompileFile, report.ToString(), false);
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            var line = new StringBuilder();
            line.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] [").Append(type).Append("] ");
            line.Append(condition).Append('\n');

            bool isProblem = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (isProblem && !string.IsNullOrEmpty(stackTrace))
            {
                string[] frames = stackTrace.Split('\n');
                int count = Math.Min(frames.Length, 12);
                for (int i = 0; i < count; i++)
                {
                    if (frames[i].Length > 0)
                    {
                        line.Append("    ").Append(frames[i]).Append('\n');
                    }
                }
            }

            WriteFile(k_ConsoleFile, line.ToString(), true);
        }

        static void WriteFile(string path, string text, bool append)
        {
            // 記錄檔寫不進去時不能再丟例外或呼叫 Debug.Log，否則會無限循環。
            try
            {
                lock (s_Lock)
                {
                    Directory.CreateDirectory(k_Directory);
                    if (append)
                    {
                        var info = new FileInfo(path);
                        if (info.Exists && info.Length > k_MaxConsoleBytes)
                        {
                            File.WriteAllText(path, "=== 記錄檔過大，已重新開始 " + Timestamp() + " ===\n", Encoding.UTF8);
                        }

                        File.AppendAllText(path, text, Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllText(path, text, Encoding.UTF8);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        static string FirstLine(string path)
        {
            try
            {
                using (var reader = new StreamReader(path))
                {
                    return reader.ReadLine() ?? "";
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        static string Timestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}

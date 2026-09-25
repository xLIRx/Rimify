using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Execution state enum / Перечисление состояния загрузки
    public enum DownloadState
    {
        Idle,
        PreparingTool,
        Downloading,
        Success,
        Error
    }

    // YouTube downloader and converter subsystem / Подсистема скачивания и конвертации аудио с YouTube
    public static class YouTubeDownloader
    {
        public static string ToolsDirectory => RimifyPaths.ToolsFolder;
        public static string TempDirectory => Path.Combine(ToolsDirectory, "Temp");
        public static string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
        public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

        public static bool IsDownloading { get; set; } = false;
        public static float CurrentProgress { get; set; } = 0f;
        public static string CurrentTrackTitle { get; set; } = "";

        public static DownloadState CurrentState { get; private set; } = DownloadState.Idle;
        public static string LastStatusMessage { get; private set; } = "";
        public static float DownloadProgress { get; private set; } = 0f;
        public static string ProgressDetails { get; private set; } = "";
        public static bool IsBusy => CurrentState == DownloadState.PreparingTool || CurrentState == DownloadState.Downloading;

        private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string FfmpegZipUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        // Direct reliable download of yt-dlp core without PyInstaller / Прямое надежное скачивание ядра yt-dlp без PyInstaller
        public static void UpdateYtDlpAsync(Action<bool, string> onComplete)
        {
            if (IsBusy) return;

            IsDownloading = true;
            CurrentState = DownloadState.PreparingTool;
            LastStatusMessage = "Rimify_YT_Updating".Translate();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    EnsureDirectories();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;

                    string tempNewExe = Path.Combine(ToolsDirectory, "yt-dlp.exe.new");
                    if (File.Exists(tempNewExe)) File.Delete(tempNewExe);

                    using (WebClient client = new WebClient())
                    {
                        client.DownloadFile(new Uri(YtDlpDownloadUrl), tempNewExe);
                    }

                    if (File.Exists(tempNewExe) && new FileInfo(tempNewExe).Length > 1000000)
                    {
                        if (File.Exists(YtDlpPath))
                        {
                            File.Delete(YtDlpPath);
                        }
                        File.Move(tempNewExe, YtDlpPath);

                        LongEventHandler.QueueLongEvent(() =>
                        {
                            CurrentState = DownloadState.Success;
                            IsDownloading = false;
                            LastStatusMessage = "Rimify_YT_Status_Success".Translate();
                            onComplete?.Invoke(true, "yt-dlp updated successfully! / Ядро yt-dlp успешно обновлено!");
                        }, null, false, null);
                    }
                    else
                    {
                        throw new Exception("Downloaded file is invalid / Скачанный файл поврежден");
                    }
                }
                catch (Exception ex)
                {
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        CurrentState = DownloadState.Error;
                        IsDownloading = false;
                        LastStatusMessage = ex.Message;
                        onComplete?.Invoke(false, ex.Message);
                    }, null, false, null);
                }
            });
        }

        // Download audio stream using yt-dlp / Запуск скачивания аудиопотока через yt-dlp
        public static void DownloadAudio(string url, bool isCombat, Action<bool> onComplete)
        {
            if (IsBusy || string.IsNullOrEmpty(url)) return;

            DownloadProgress = 0f;
            CurrentProgress = 0f;
            CurrentTrackTitle = "YouTube Track";
            ProgressDetails = "";
            IsDownloading = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    EnsureDirectories();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;

                    // 1. Ensure yt-dlp exists / Проверка наличия yt-dlp
                    if (!File.Exists(YtDlpPath) || new FileInfo(YtDlpPath).Length < 1000000)
                    {
                        CurrentState = DownloadState.PreparingTool;
                        LastStatusMessage = "Rimify_YT_Status_PreparingTool".Translate();

                        using (WebClient client = new WebClient())
                        {
                            client.DownloadProgressChanged += (s, e) =>
                            {
                                DownloadProgress = e.ProgressPercentage / 100f;
                                CurrentProgress = DownloadProgress;
                                ProgressDetails = $"yt-dlp: {e.ProgressPercentage}%";
                            };
                            client.DownloadFile(new Uri(YtDlpDownloadUrl), YtDlpPath);
                        }
                    }

                    // 2. Ensure ffmpeg exists / Проверка наличия FFmpeg
                    if (!File.Exists(FfmpegPath) && !IsFfmpegOnPath())
                    {
                        CurrentState = DownloadState.PreparingTool;
                        LastStatusMessage = "Rimify_YT_Status_DownloadingFfmpeg".Translate();

                        string zipPath = Path.Combine(ToolsDirectory, "ffmpeg.zip");
                        using (WebClient client = new WebClient())
                        {
                            client.DownloadProgressChanged += (s, e) =>
                            {
                                DownloadProgress = e.ProgressPercentage / 100f;
                                CurrentProgress = DownloadProgress;
                                ProgressDetails = $"FFmpeg: {e.ProgressPercentage}%";
                            };
                            client.DownloadFile(new Uri(FfmpegZipUrl), zipPath);
                        }

                        ExtractFfmpeg(zipPath);
                    }

                    CurrentState = DownloadState.Downloading;
                    LastStatusMessage = "Rimify_YT_Status_Downloading".Translate();
                    DownloadProgress = 0.05f;
                    CurrentProgress = 0.05f;

                    string targetFolder = AudioLoader.MusicDirectory;
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                    HashSet<string> filesBefore = new HashSet<string>(Directory.GetFiles(targetFolder));

                    string sanitizedUrl = url.Trim().Replace("\"", "");
                    string outputTemplate = Path.Combine(targetFolder, "%(title)s.%(ext)s").Replace("\\", "/");
                    string ffmpegLocation = File.Exists(FfmpegPath) ? $"--ffmpeg-location \"{ToolsDirectory}\"" : "";

                    string arguments = $"-x --audio-format mp3 --no-playlist --no-part --newline {ffmpegLocation} --paths temp:\"{TempDirectory}\" -o \"{outputTemplate}\" \"{sanitizedUrl}\"";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = YtDlpPath,
                        Arguments = arguments,
                        WorkingDirectory = ToolsDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    psi.EnvironmentVariables["TEMP"] = TempDirectory;
                    psi.EnvironmentVariables["TMP"] = TempDirectory;
                    psi.EnvironmentVariables["TMPDIR"] = TempDirectory;
                    psi.EnvironmentVariables["USERPROFILE"] = ToolsDirectory;

                    using (Process process = new Process())
                    {
                        process.StartInfo = psi;
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                ParseProgress(e.Data);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        string stdErr = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            CurrentState = DownloadState.Success;
                            IsDownloading = false;
                            DownloadProgress = 1f;
                            CurrentProgress = 1f;
                            LastStatusMessage = "Rimify_YT_Status_Success".Translate();
                            ProgressDetails = "100%";

                            LongEventHandler.QueueLongEvent(() =>
                            {
                                if (isCombat)
                                {
                                    string[] filesAfter = Directory.GetFiles(targetFolder);
                                    foreach (string f in filesAfter)
                                    {
                                        if (!filesBefore.Contains(f))
                                        {
                                            string fName = Path.GetFileName(f);
                                            TrackData td = AudioLoader.Config.Tracks.FirstOrDefault(t => t.FileName == fName);
                                            if (td == null)
                                            {
                                                td = new TrackData { FileName = fName, IsCombat = true, TimeOfDay = TimeOfDay.Any };
                                                AudioLoader.Config.Tracks.Add(td);
                                            }
                                            else
                                            {
                                                td.IsCombat = true;
                                            }
                                        }
                                    }
                                    AudioLoader.Config.Save();
                                }

                                CleanTemp();
                                onComplete?.Invoke(true);
                            }, null, false, null);
                        }
                        else
                        {
                            CurrentState = DownloadState.Error;
                            IsDownloading = false;
                            LastStatusMessage = string.IsNullOrEmpty(stdErr) ? "Rimify_YT_Status_Error".Translate() : stdErr;
                            Log.Error($"[Rimify] yt-dlp error: {stdErr}");

                            LongEventHandler.QueueLongEvent(() =>
                            {
                                onComplete?.Invoke(false);
                            }, null, false, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CurrentState = DownloadState.Error;
                    IsDownloading = false;
                    LastStatusMessage = ex.Message;
                    Log.Error($"[Rimify] Download exception: {ex}");

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        onComplete?.Invoke(false);
                    }, null, false, null);
                }
            });
        }

        private static void ParseProgress(string line)
        {
            if (line.Contains("[download]") && line.Contains("%"))
            {
                Match match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
                if (match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                {
                    DownloadProgress = Mathf.Clamp01(pct / 100f);
                    CurrentProgress = DownloadProgress;
                }
                string details = line.Replace("[download]", "").Trim();
                ProgressDetails = details;
                LastStatusMessage = details;
            }
            else if (line.Contains("[download] Destination:"))
            {
                string raw = Path.GetFileNameWithoutExtension(line.Replace("[download] Destination:", "").Trim());
                if (!string.IsNullOrEmpty(raw))
                {
                    CurrentTrackTitle = raw;
                }
            }
            else if (line.Contains("[ExtractAudio]"))
            {
                DownloadProgress = 0.95f;
                CurrentProgress = 0.95f;
                LastStatusMessage = "Rimify_YT_Status_Converting".Translate();
                ProgressDetails = "Converting...";
            }
        }

        private static void EnsureDirectories()
        {
            if (!Directory.Exists(ToolsDirectory)) Directory.CreateDirectory(ToolsDirectory);
            if (!Directory.Exists(TempDirectory)) Directory.CreateDirectory(TempDirectory);
        }

        private static void CleanTemp()
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                {
                    foreach (var d in Directory.GetDirectories(TempDirectory)) Directory.Delete(d, true);
                    foreach (var f in Directory.GetFiles(TempDirectory)) File.Delete(f);
                }
            }
            catch { }
        }

        private static bool IsFfmpegOnPath()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("where.exe", "ffmpeg")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static void ExtractFfmpeg(string zipPath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "tar.exe",
                    Arguments = $"-xf \"{zipPath}\" --strip-components=2 \"*/bin/ffmpeg.exe\"",
                    WorkingDirectory = ToolsDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
            catch
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"Expand-Archive -Path '{zipPath}' -DestinationPath '{ToolsDirectory}\\ffmpeg_temp' -Force\"",
                        WorkingDirectory = ToolsDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit();
                    }

                    string[] found = Directory.GetFiles(ToolsDirectory, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (found.Length > 0 && found[0] != FfmpegPath)
                    {
                        File.Copy(found[0], FfmpegPath, true);
                    }

                    string tempFolder = Path.Combine(ToolsDirectory, "ffmpeg_temp");
                    if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                }
                catch { }
            }
        }

        public static void ResetState()
        {
            CurrentState = DownloadState.Idle;
            IsDownloading = false;
            LastStatusMessage = "";
            DownloadProgress = 0f;
            CurrentProgress = 0f;
            CurrentTrackTitle = "";
            ProgressDetails = "";
        }
    }
}
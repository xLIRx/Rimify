using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Verse;

namespace Rimify
{
    // Storage paths and folder migration manager / Менеджер путей хранилища и миграции папок
    public static class RimifyPaths
    {
        private static string baseFolder;

        public static string BaseFolder
        {
            get
            {
                if (baseFolder == null)
                {
                    string newPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Rimify");
                    string oldPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "YouTubeMusicSync");

                    // Automatic migration from legacy folder / Автоматическая миграция со старой папки
                    if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
                    {
                        try { Directory.Move(oldPath, newPath); } catch { }
                    }

                    if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
                    baseFolder = newPath;
                }
                return baseFolder;
            }
        }

        public static string TracksFolder => Path.Combine(BaseFolder, "Tracks");
        public static string ToolsFolder => Path.Combine(BaseFolder, "Tools");
        public static string MenuMusicFolder
        {
            get
            {
                string path = Path.Combine(BaseFolder, "MenuMusic");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }
        public static string ConfigFile => Path.Combine(BaseFolder, "config.xml");
    }

    // Single track configuration data / Данные конфигурации отдельного трека
    public class TrackData
    {
        public string FileName;
        public bool IsCombat;
        public TimeOfDay TimeOfDay = TimeOfDay.Any;
    }

    // Override settings for vanilla and modded songs / Переопределение тегов оригинальных и сторонних треков
    public class SongOverride
    {
        public string defName;
        public bool tense;
        public TimeOfDay timeOfDay;
        public bool disabled;
    }

    // Playlist structure / Структура пользовательского плейлиста
    public class PlaylistData
    {
        public string Name;
        public List<string> Songs = new List<string>();
    }

    // Global mod configuration with thread-safe serialization / Глобальная конфигурация мода с потокобезопасной записью
    public class ModConfig
    {
        private static readonly object fileLock = new object();

        public List<TrackData> Tracks = new List<TrackData>();
        public List<SongOverride> SongOverrides = new List<SongOverride>();
        public List<PlaylistData> Playlists = new List<PlaylistData>();
        public string ActiveLockedPlaylist = null;
        public float WidgetX = -1f;
        public float WidgetY = -1f;
        public bool ShowWidget = true;

        // Playback timing & mode settings / Настройки воспроизведения и режимов
        public bool ContinuousPlayback = true;
        public float MinPauseSeconds = 60f;
        public float MaxPauseSeconds = 180f;
        public bool ShuffleMode = true;
        public bool RepeatSingle = false;

        // Menu music replacement setting / Настройка замены музыки главного меню
        public bool CustomMenuMusic = true;

        // Thread-safe configuration loader / Потокобезопасная загрузка конфигурации
        public static ModConfig Load()
        {
            lock (fileLock)
            {
                string path = RimifyPaths.ConfigFile;
                if (!File.Exists(path)) return new ModConfig();

                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ModConfig));
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return (ModConfig)serializer.Deserialize(stream);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Rimify] Error loading config.xml / Ошибка чтения config.xml: {ex.Message}");
                    return new ModConfig();
                }
            }
        }

        // Thread-safe configuration saver / Потокобезопасное сохранение конфигурации
        public void Save()
        {
            lock (fileLock)
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ModConfig));
                    using (FileStream stream = new FileStream(RimifyPaths.ConfigFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        serializer.Serialize(stream, this);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Rimify] Error saving config.xml / Ошибка записи config.xml: {ex.Message}");
                }
            }
        }

        public bool IsSongDisabled(string defName)
        {
            var ov = SongOverrides.Find(s => s.defName == defName);
            return ov != null && ov.disabled;
        }

        public void ToggleSongDisabled(string defName)
        {
            var ov = SongOverrides.Find(s => s.defName == defName);
            if (ov == null)
            {
                SongDef song = DefDatabase<SongDef>.GetNamedSilentFail(defName);
                ov = new SongOverride
                {
                    defName = defName,
                    tense = song != null && song.tense,
                    timeOfDay = song != null ? song.allowedTimeOfDay : TimeOfDay.Any,
                    disabled = true
                };
                SongOverrides.Add(ov);
            }
            else
            {
                ov.disabled = !ov.disabled;
            }
            Save();
        }

        public void SetSongOverride(string defName, bool tense, TimeOfDay timeOfDay)
        {
            var ov = SongOverrides.Find(s => s.defName == defName);
            if (ov == null)
            {
                ov = new SongOverride { defName = defName };
                SongOverrides.Add(ov);
            }
            ov.tense = tense;
            ov.timeOfDay = timeOfDay;
            Save();
        }

        public void CreatePlaylist(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Playlists.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;

            Playlists.Add(new PlaylistData { Name = name });
            Save();
        }

        public void DeletePlaylist(string name)
        {
            if (ActiveLockedPlaylist != null && ActiveLockedPlaylist.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                ActiveLockedPlaylist = null;
            }
            Playlists.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public void ToggleSongInPlaylist(string playlistName, string defName)
        {
            var pl = Playlists.Find(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));
            if (pl == null) return;

            if (pl.Songs.Contains(defName))
            {
                pl.Songs.Remove(defName);
            }
            else
            {
                pl.Songs.Add(defName);
            }
            Save();
        }

        public bool IsSongInPlaylist(string playlistName, string defName)
        {
            var pl = Playlists.Find(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));
            return pl != null && pl.Songs.Contains(defName);
        }
    }
}
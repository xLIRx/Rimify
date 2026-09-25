using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimWorld;

namespace Rimify
{
    // Container for loaded track information / Контейнер метаданных загруженного трека
    public class LoadedTrack
    {
        public string FilePath;
        public string FileName;
        public SongDef Def;
        public TrackData Config;
    }

    // Coroutine host singleton / Синглтон для исполнения корутин в фоновом режиме
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("Rimify_CoroutineRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineRunner>();
                }
                return _instance;
            }
        }
    }

    // Audio scanning, streaming, and database registration manager / Менеджер сканирования аудио, стриминга и регистрации дефов
    public static class AudioLoader
    {
        public static string MusicDirectory => RimifyPaths.TracksFolder;
        public static List<LoadedTrack> Tracks = new List<LoadedTrack>();
        public static ModConfig Config;

        public static bool IsReloading { get; private set; } = false;

        // Cached clips for seamless menu music toggling / Кэшированные клипы для мгновенного переключения музыки меню
        public static AudioClip VanillaMenuClip { get; private set; }
        public static AudioClip CustomMenuClip { get; private set; }

        // Initialize audio subsystem on startup / Инициализация аудиосистемы при старте
        public static void Initialize()
        {
            if (!Directory.Exists(MusicDirectory))
            {
                Directory.CreateDirectory(MusicDirectory);
            }

            Config = ModConfig.Load();
            ReloadAllTracksAsync();
            ApplyOverrides();

            // Cache vanilla entry clip before any overrides / Сохраняем ванильный трек до любых изменений
            if (SongDefOf.EntrySong != null)
            {
                VanillaMenuClip = SongDefOf.EntrySong.clip;
            }

            // Load custom menu clip from mod Sounds/Music/MenuSong / Загружаем пользовательский трек из Sounds/Music/MenuSong
            CustomMenuClip = ContentFinder<AudioClip>.Get("Music/MenuSong", reportFailure: false);
        }

        // Apply menu music setting with instant live switch / Применение настройки музыки меню с мгновенным переключением
        public static void ApplyMenuMusicSetting()
        {
            if (SongDefOf.EntrySong == null) return;

            if (VanillaMenuClip == null && SongDefOf.EntrySong.clip != null && SongDefOf.EntrySong.clip != CustomMenuClip)
            {
                VanillaMenuClip = SongDefOf.EntrySong.clip;
            }

            if (CustomMenuClip == null)
            {
                CustomMenuClip = ContentFinder<AudioClip>.Get("Music/MenuSong", reportFailure: false);
            }

            bool useCustom = Config != null && Config.CustomMenuMusic;
            AudioClip targetClip = (useCustom && CustomMenuClip != null) ? CustomMenuClip : VanillaMenuClip;

            if (targetClip != null)
            {
                SongDefOf.EntrySong.clip = targetClip;
            }

            // If in main menu, immediately switch audio source / Если находимся в главном меню — переключаем звук на лету
            if (Current.ProgramState != ProgramState.Playing && Find.MusicManagerEntry != null)
            {
                FieldInfo audioSourceField = typeof(MusicManagerEntry).GetField("audioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                AudioSource source = audioSourceField?.GetValue(Find.MusicManagerEntry) as AudioSource;

                if (source != null && targetClip != null)
                {
                    if (source.clip != targetClip)
                    {
                        source.clip = targetClip;
                        source.time = 0f;
                        source.volume = Prefs.VolumeMusic;
                        source.loop = true;
                        source.spatialBlend = 0f;
                        source.Play();
                    }
                }
            }
        }

        // Restore custom SongDefs if database was cleared / Восстановление дефов, если игра очистила DefDatabase
        public static bool EnsureDefsRegistered()
        {
            if (Tracks == null || Tracks.Count == 0) return false;

            LoadedTrack sample = Tracks.FirstOrDefault(t => t?.Def != null);
            if (sample != null && DefDatabase<SongDef>.GetNamedSilentFail(sample.Def.defName) != null)
            {
                return false;
            }

            bool restoredAny = false;
            for (int i = 0; i < Tracks.Count; i++)
            {
                LoadedTrack t = Tracks[i];
                if (t?.Def != null && DefDatabase<SongDef>.GetNamedSilentFail(t.Def.defName) == null)
                {
                    DefDatabase<SongDef>.Add(t.Def);
                    restoredAny = true;
                }
            }

            if (restoredAny)
            {
                ApplyOverrides();
            }

            return restoredAny;
        }

        // Apply custom tags, combat modes, and rotation exclusions / Применение пользовательских настроек ротации и тегов
        public static void ApplyOverrides()
        {
            if (Config == null || Config.SongOverrides == null) return;
            foreach (var ov in Config.SongOverrides)
            {
                if (ov == null || string.IsNullOrEmpty(ov.defName)) continue;

                SongDef song = DefDatabase<SongDef>.GetNamedSilentFail(ov.defName);
                if (song != null)
                {
                    song.tense = ov.tense;
                    song.allowedTimeOfDay = ov.timeOfDay;

                    if (ov.disabled)
                    {
                        song.commonality = 0f;
                        song.allowedSeasons = new List<Season>();
                    }
                    else
                    {
                        song.commonality = 1f;
                        song.allowedSeasons = null;
                    }
                }
            }
        }

        // Asynchronous disk scanning for audio files / Асинхронное сканирование папки треков на диске
        public static void ReloadAllTracksAsync(Action onComplete = null)
        {
            if (IsReloading) return;
            IsReloading = true;

            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(MusicDirectory))
                    {
                        Directory.CreateDirectory(MusicDirectory);
                    }

                    string[] files = Directory.GetFiles(MusicDirectory);
                    List<(string filePath, TrackData data)> toLoad = new List<(string, TrackData)>();
                    HashSet<string> filesOnDisk = new HashSet<string>();

                    foreach (string file in files)
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".mp3" || ext == ".ogg" || ext == ".wav")
                        {
                            string fileName = Path.GetFileName(file);
                            filesOnDisk.Add(fileName);

                            TrackData data = Config.Tracks.FirstOrDefault(t => t.FileName == fileName);
                            if (data == null)
                            {
                                data = new TrackData { FileName = fileName, IsCombat = false, TimeOfDay = TimeOfDay.Any };
                                Config.Tracks.Add(data);
                            }

                            toLoad.Add((file, data));
                        }
                    }

                    Config.Save();

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        CoroutineRunner.Instance.StartCoroutine(ProcessLoadedFilesRoutine(toLoad, filesOnDisk, onComplete));
                    }, null, false, null);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Rimify] Error scanning tracks asynchronously / Ошибка асинхронного сканирования: {ex}");
                    IsReloading = false;
                }
            });
        }

        // Process disk tracks routine / Корутина пакетной обработки треков
        private static IEnumerator ProcessLoadedFilesRoutine(List<(string filePath, TrackData data)> toLoad, HashSet<string> filesOnDisk, Action onComplete)
        {
            Tracks.RemoveAll(t => !filesOnDisk.Contains(t.FileName));
            EnsureDefsRegistered();

            HashSet<string> loadedFileNames = new HashSet<string>(Tracks.Where(t => t.Def?.clip != null).Select(t => t.FileName));

            foreach (var item in toLoad)
            {
                if (!loadedFileNames.Contains(item.data.FileName))
                {
                    yield return LoadAudioClipRoutine(item.filePath, item.data);
                }
            }

            ApplyOverrides();
            IsReloading = false;
            onComplete?.Invoke();
        }

        public static void ReloadAllTracks()
        {
            ReloadAllTracksAsync();
        }

        // Stream and register audio clip without memory inflation / Потоковая загрузка аудио без перегрузки памяти
        private static IEnumerator LoadAudioClipRoutine(string filePath, TrackData data)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            AudioType audioType = AudioType.UNKNOWN;

            switch (ext)
            {
                case ".mp3": audioType = AudioType.MPEG; break;
                case ".ogg": audioType = AudioType.OGGVORBIS; break;
                case ".wav": audioType = AudioType.WAV; break;
            }

            if (audioType == AudioType.UNKNOWN) yield break;

            string uri = new Uri(filePath).AbsoluteUri;

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                DownloadHandlerAudioClip handler = (DownloadHandlerAudioClip)www.downloadHandler;
                handler.streamAudio = true;

                yield return www.SendWebRequest();

                if (!string.IsNullOrEmpty(www.error))
                {
                    Log.Error($"[Rimify] Audio read error / Ошибка чтения {filePath}: {www.error}");
                    yield break;
                }

                AudioClip clip = handler.audioClip;
                if (clip == null) yield break;

                float timeout = Time.realtimeSinceStartup + 2f;
                while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                if (clip.loadState == AudioDataLoadState.Failed) yield break;

                string trackName = Path.GetFileNameWithoutExtension(filePath);
                clip.name = trackName;

                string safeName = Regex.Replace(trackName, @"[^a-zA-Z0-9_]", "_").Trim('_');
                if (string.IsNullOrEmpty(safeName))
                {
                    safeName = "Track_" + Math.Abs(filePath.GetHashCode());
                }
                string defName = "Rimify_" + safeName;

                bool isDisabled = Config.IsSongDisabled(defName) || Config.IsSongDisabled("YTM_" + safeName);

                SongDef song = DefDatabase<SongDef>.GetNamedSilentFail(defName) ?? DefDatabase<SongDef>.GetNamedSilentFail("YTM_" + safeName);
                if (song == null)
                {
                    song = new SongDef
                    {
                        defName = defName,
                        label = trackName,
                        clip = clip,
                        clipPath = null,
                        volume = 1f,
                        commonality = isDisabled ? 0f : 1f,
                        tense = data.IsCombat,
                        allowedTimeOfDay = data.TimeOfDay,
                        allowedSeasons = isDisabled ? new List<Season>() : null
                    };
                    DefDatabase<SongDef>.Add(song);
                }
                else
                {
                    song.clip = clip;
                    song.clipPath = null;
                    song.label = trackName;
                    song.tense = data.IsCombat;
                    song.allowedTimeOfDay = data.TimeOfDay;
                    song.commonality = isDisabled ? 0f : 1f;
                    song.allowedSeasons = isDisabled ? new List<Season>() : null;
                }

                Tracks.Add(new LoadedTrack
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Def = song,
                    Config = data
                });
            }
        }

        // Mod's Sounds/Music directory for menu theme / Папка Sounds/Music внутри мода для заставки меню
        public static string MenuMusicDirectory
        {
            get
            {
                try
                {
                    RimifyMod mod = LoadedModManager.GetMod<RimifyMod>();
                    if (mod?.Content != null)
                    {
                        string dir = Path.Combine(mod.Content.RootDir, "Sounds", "Music");
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        return dir;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Rimify] Error resolving menu music folder / Ошибка получения папки музыки меню: {ex.Message}");
                }
                return null;
            }
        }

        // Delete track file, release clip memory, and purge def / Удаление трека, освобождение памяти клипа и очистка дефа
        public static void DeleteCustomSong(SongDef song)
        {
            if (song == null || (!song.defName.StartsWith("Rimify_") && !song.defName.StartsWith("YTM_"))) return;

            if (MusicController.CurrentSong == song)
            {
                MusicController.SkipTrack();
            }

            LoadedTrack track = Tracks.FirstOrDefault(t => t.Def == song || t.Def?.defName == song.defName);
            if (track != null)
            {
                if (track.Def?.clip != null)
                {
                    UnityEngine.Object.Destroy(track.Def.clip);
                }

                if (File.Exists(track.FilePath))
                {
                    try { File.Delete(track.FilePath); } catch (Exception ex) { Log.Error($"[Rimify] Delete error / Ошибка удаления: {ex.Message}"); }
                }
                Tracks.Remove(track);
                Config.Tracks.RemoveAll(t => t.FileName == track.FileName);
            }

            foreach (var pl in Config.Playlists)
            {
                pl.Songs.Remove(song.defName);
            }
            Config.SongOverrides.RemoveAll(o => o.defName == song.defName);
            Config.Save();

            try
            {
                var defsListField = typeof(DefDatabase<SongDef>).GetField("defsList", BindingFlags.Static | BindingFlags.NonPublic)
                                    ?? typeof(DefDatabase<SongDef>).GetField("allDefs", BindingFlags.Static | BindingFlags.NonPublic);
                (defsListField?.GetValue(null) as List<SongDef>)?.Remove(song);

                var defsByNameField = typeof(DefDatabase<SongDef>).GetField("defsByName", BindingFlags.Static | BindingFlags.NonPublic);
                (defsByNameField?.GetValue(null) as Dictionary<string, SongDef>)?.Remove(song.defName);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Rimify] Could not completely purge def / Не удалось полностью очистить деф: {ex.Message}");
            }
        }
    }
}
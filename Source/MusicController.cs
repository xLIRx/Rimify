using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Playback control and reflection driver / Контроллер воспроизведения и рефлексии аудио
    public static class MusicController
    {
        private static FieldInfo audioSourceField = typeof(MusicManagerPlay).GetField("audioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static FieldInfo nextSongStartTimeField = typeof(MusicManagerPlay).GetField("nextSongStartTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static FieldInfo songWasQueuedField = typeof(MusicManagerPlay).GetField("songWasQueuedWithSilenceTimer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static FieldInfo lastStartedSongField = typeof(MusicManagerPlay).GetField("lastStartedSong", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static FieldInfo forcedNextSongField = typeof(MusicManagerPlay).GetField("forcedNextSong", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static bool IsUserPaused { get; private set; }
        private static bool wasPlaying = false;
        private static float silenceUntilTime = -1f;

        private static AudioClip lastClip;
        private static SongDef cachedCurrentSong;

        // Retrieve vanilla AudioSource via reflection / Получение ванильного AudioSource через рефлексию
        public static AudioSource GetAudioSource()
        {
            if (Find.MusicManagerPlay == null) return null;
            return audioSourceField?.GetValue(Find.MusicManagerPlay) as AudioSource;
        }

        // Get sanitized song title / Получение очищенного названия трека
        public static string GetSongName(SongDef song)
        {
            if (song == null) return "Rimify_Status_Silence".Translate();
            if (!string.IsNullOrEmpty(song.label)) return song.label;
            if (!string.IsNullOrEmpty(song.defName))
            {
                string name = song.defName;
                if (name.StartsWith("Rimify_")) name = name.Substring(7);
                else if (name.StartsWith("YTM_")) name = name.Substring(4);
                return GenText.SplitCamelCase(name).Replace("_", " ").Trim();
            }
            return "Rimify_Status_Untitled".Translate();
        }

        // Currently playing SongDef / Текущий активный SongDef
        public static SongDef CurrentSong
        {
            get
            {
                AudioSource source = GetAudioSource();
                if (source != null && source.clip != null)
                {
                    if (source.clip == lastClip && cachedCurrentSong != null)
                    {
                        return cachedCurrentSong;
                    }
                    lastClip = source.clip;
                    cachedCurrentSong = DefDatabase<SongDef>.AllDefsListForReading.Find(s => s.clip != null && s.clip == source.clip);
                    return cachedCurrentSong;
                }
                lastClip = null;
                cachedCurrentSong = null;
                return null;
            }
        }

        // Get track display title or status string / Получение строки заголовка или статуса
        public static string GetCurrentTrackTitle()
        {
            AudioSource source = GetAudioSource();
            if (source != null && source.clip != null && source.isPlaying)
            {
                SongDef cur = CurrentSong;
                if (cur != null) return GetSongName(cur);

                string raw = source.clip.name;
                if (raw.StartsWith("Rimify_")) raw = raw.Substring(7);
                else if (raw.StartsWith("YTM_")) raw = raw.Substring(4);
                return raw.Replace("_", " ");
            }

            if (IsUserPaused)
            {
                return "Rimify_Status_Paused".Translate();
            }

            if (silenceUntilTime > Time.time)
            {
                int remaining = Mathf.CeilToInt(silenceUntilTime - Time.time);
                return "Rimify_Status_SilenceTimer".Translate(remaining);
            }

            return "Rimify_Status_Waiting".Translate();
        }

        // Playback progress (0..1) / Прогресс воспроизведения трека (0..1)
        public static float GetTrackProgress()
        {
            AudioSource source = GetAudioSource();
            if (source != null && source.clip != null && source.clip.length > 0f)
            {
                return Mathf.Clamp01(source.time / source.clip.length);
            }
            return 0f;
        }

        // Seek playback position / Перемотка позиции трека
        public static void SeekTo(float progress01)
        {
            AudioSource source = GetAudioSource();
            if (source != null && source.clip != null)
            {
                source.time = Mathf.Clamp(source.clip.length * progress01, 0f, source.clip.length - 0.1f);
            }
        }

        // Formatted timestamp string mm:ss / Форматированная строка времени мм:сс
        public static string GetTrackTimeString()
        {
            AudioSource source = GetAudioSource();
            if (source != null && source.clip != null)
            {
                int cur = (int)source.time;
                int total = (int)source.clip.length;
                return $"{cur / 60:D2}:{cur % 60:D2} / {total / 60:D2}:{total % 60:D2}";
            }
            return "00:00 / 00:00";
        }

        // Toggle play/pause state / Переключение паузы и воспроизведения
        public static void TogglePlayPause()
        {
            AudioSource source = GetAudioSource();
            if (source == null) return;

            if (source.isPlaying)
            {
                source.Pause();
                IsUserPaused = true;
                wasPlaying = false;
            }
            else if (IsUserPaused)
            {
                source.UnPause();
                IsUserPaused = false;
                wasPlaying = true;
            }
            else
            {
                silenceUntilTime = -1f;
                SkipTrack();
            }
        }

        // Restart track or jump to previous / Перезапуск с начала или предыдущий трек
        public static void RestartOrPrevious()
        {
            silenceUntilTime = -1f;
            AudioSource source = GetAudioSource();
            if (source != null && source.time > 3f)
            {
                source.time = 0f;
            }
            else
            {
                SkipTrack();
            }
        }

        // Skip to next track / Переход к следующему треку
        public static void SkipTrack()
        {
            if (Find.MusicManagerPlay == null) return;

            silenceUntilTime = -1f;

            AudioSource source = GetAudioSource();
            source?.Stop();
            wasPlaying = false;
            IsUserPaused = false;

            if (!string.IsNullOrEmpty(AudioLoader.Config?.ActiveLockedPlaylist))
            {
                PlayNextFromLockedPlaylist();
                return;
            }

            PlayNextDefaultSong();
        }

        // Play next song from active playlist (Shuffle or Sequential) / Воспроизведение следующего трека из плейлиста
        public static void PlayNextFromLockedPlaylist()
        {
            var pl = AudioLoader.Config.Playlists.Find(p => p.Name.Equals(AudioLoader.Config.ActiveLockedPlaylist, StringComparison.OrdinalIgnoreCase));
            if (pl == null || pl.Songs.Count == 0)
            {
                AudioLoader.Config.ActiveLockedPlaylist = null;
                AudioLoader.Config.Save();
                PlayNextDefaultSong();
                return;
            }

            var validSongs = pl.Songs
                .Select(defName => DefDatabase<SongDef>.GetNamedSilentFail(defName))
                .Where(s => s != null && s.clip != null && !AudioLoader.Config.IsSongDisabled(s.defName))
                .ToList();

            if (validSongs.Count == 0)
            {
                PlayNextDefaultSong();
                return;
            }

            bool shuffle = AudioLoader.Config == null || AudioLoader.Config.ShuffleMode;
            if (shuffle)
            {
                SongDef next = validSongs.RandomElement();
                PlaySongNow(next);
            }
            else
            {
                int curIdx = validSongs.FindIndex(s => s == cachedCurrentSong);
                int nextIdx = (curIdx + 1) % validSongs.Count;
                PlaySongNow(validSongs[nextIdx]);
            }
        }

        // Play next default song (Shuffle or Sequential) / Воспроизведение следующего трека по умолчанию
        public static void PlayNextDefaultSong()
        {
            if (Find.MusicManagerPlay == null) return;

            var allSongs = DefDatabase<SongDef>.AllDefsListForReading;
            if (allSongs == null || allSongs.Count == 0) return;

            bool isCombat = IsInCombat();
            var candidates = allSongs
                .Where(s => s != null && s.clip != null && !AudioLoader.Config.IsSongDisabled(s.defName) && s.tense == isCombat)
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = allSongs.Where(s => s != null && s.clip != null && !AudioLoader.Config.IsSongDisabled(s.defName)).ToList();
            }

            if (candidates.Count > 0)
            {
                bool shuffle = AudioLoader.Config == null || AudioLoader.Config.ShuffleMode;
                if (shuffle)
                {
                    SongDef chosen = candidates.RandomElement();
                    PlaySongNow(chosen);
                }
                else
                {
                    int curIdx = candidates.FindIndex(s => s == cachedCurrentSong);
                    int nextIdx = (curIdx + 1) % candidates.Count;
                    PlaySongNow(candidates[nextIdx]);
                }
            }
        }

        // Check if map is under raid or combat / Проверка нахождения колонии в бою
        public static bool IsInCombat()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            Map map = Find.CurrentMap;
            if (map == null) return false;
            return map.dangerWatcher != null && map.dangerWatcher.DangerRating == StoryDanger.High;
        }

        // Immediately start specified song / Немедленный запуск указанного трека
        public static void PlaySongNow(SongDef song)
        {
            if (Find.MusicManagerPlay == null || song == null) return;

            silenceUntilTime = -1f;

            AudioSource source = GetAudioSource();
            if (source == null) return;

            source.Stop();
            wasPlaying = false;
            IsUserPaused = false;

            if (song.clip == null && !string.IsNullOrEmpty(song.clipPath) && !song.defName.StartsWith("Rimify_") && !song.defName.StartsWith("YTM_"))
            {
                song.clip = ContentFinder<AudioClip>.Get(song.clipPath);
            }

            if (song.clip == null) return;

            cachedCurrentSong = song;
            lastClip = song.clip;

            try
            {
                nextSongStartTimeField?.SetValue(Find.MusicManagerPlay, Time.time + 99999f);
                songWasQueuedField?.SetValue(Find.MusicManagerPlay, true);
                lastStartedSongField?.SetValue(Find.MusicManagerPlay, song);
                forcedNextSongField?.SetValue(Find.MusicManagerPlay, null);
            }
            catch { }

            source.clip = song.clip;
            source.volume = (song.volume <= 0f ? 1f : song.volume) * Prefs.VolumeMusic;
            source.spatialBlend = 0f;
            source.loop = false;
            source.Play();
            wasPlaying = true;
        }

        // Background update loop called every frame / Фоновый тик управления музыкой
        public static void UpdateTick()
        {
            if (Find.MusicManagerPlay == null || Current.ProgramState != ProgramState.Playing) return;

            AudioLoader.EnsureDefsRegistered();

            AudioSource source = GetAudioSource();
            if (source == null) return;

            bool isPlaying = source.isPlaying;

            // Handle silence timer / Обработка таймера тишины между треками
            if (silenceUntilTime > 0f)
            {
                if (IsInCombat() || Time.time >= silenceUntilTime)
                {
                    silenceUntilTime = -1f;
                    SkipTrack();
                    return;
                }
            }

            // Track finished playing event / Событие завершения воспроизведения трека
            if (wasPlaying && !isPlaying && !IsUserPaused)
            {
                wasPlaying = false;

                // Loop current track if repeat mode is enabled / Повтор текущего трека при включенном повторе
                if (AudioLoader.Config != null && AudioLoader.Config.RepeatSingle && cachedCurrentSong != null)
                {
                    PlaySongNow(cachedCurrentSong);
                    return;
                }

                bool isContinuous = AudioLoader.Config == null || AudioLoader.Config.ContinuousPlayback;
                if (isContinuous || IsInCombat())
                {
                    SkipTrack();
                    return;
                }
                else
                {
                    float minP = AudioLoader.Config != null ? AudioLoader.Config.MinPauseSeconds : 60f;
                    float maxP = AudioLoader.Config != null ? AudioLoader.Config.MaxPauseSeconds : 180f;
                    silenceUntilTime = Time.time + UnityEngine.Random.Range(Mathf.Max(5f, minP), Mathf.Max(10f, maxP));
                }
            }

            wasPlaying = isPlaying;

            if (isPlaying)
            {
                try
                {
                    nextSongStartTimeField?.SetValue(Find.MusicManagerPlay, Time.time + 99999f);
                }
                catch { }
            }

            SongDef current = CurrentSong;
            if (!string.IsNullOrEmpty(AudioLoader.Config?.ActiveLockedPlaylist))
            {
                var pl = AudioLoader.Config.Playlists.Find(p => p.Name.Equals(AudioLoader.Config.ActiveLockedPlaylist, StringComparison.OrdinalIgnoreCase));
                if (pl != null && current != null && !pl.Songs.Contains(current.defName) && !IsUserPaused)
                {
                    PlayNextFromLockedPlaylist();
                    return;
                }
            }

            if (current != null && AudioLoader.Config.IsSongDisabled(current.defName) && !IsUserPaused)
            {
                SkipTrack();
            }
        }
    }
}
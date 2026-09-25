using System;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Static initializer on game startup / Статический конструктор инициализации при старте игры
    [StaticConstructorOnStartup]
    public static class RimifyInit
    {
        static RimifyInit()
        {
            var harmony = new Harmony("LiFraxi.Rimify");
            harmony.PatchAll();

            Log.Message("[Rimify] Initializing audio loader / Инициализация загрузчика...");
            AudioLoader.Initialize();
            AudioLoader.ApplyMenuMusicSetting();
        }
    }

    // Main RimWorld mod class and settings UI / Главный класс мода и интерфейс страницы настроек
    public class RimifyMod : Mod
    {
        public RimifyMod(ModContentPack content) : base(content)
        {
        }

        public override string SettingsCategory() => "Rimify";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // Folder open buttons row / Строка кнопок открытия папок
            Rect btnRow = listing.GetRect(32f);
            float btnW = (btnRow.width - 16f) * 0.5f;

            // 1. Open Tracks library button / Кнопка открытия фонотеки
            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, btnW, 32f), "Rimify_TracksFolder".Translate()))
            {
                Process.Start(new ProcessStartInfo { FileName = AudioLoader.MusicDirectory, UseShellExecute = true });
            }

            // 2. Open Menu Music folder button / Кнопка открытия папки заставки меню
            Rect menuBtnRect = new Rect(btnRow.x + btnW + 16f, btnRow.y, btnW, 32f);
            if (Widgets.ButtonText(menuBtnRect, "Rimify_MenuMusicFolder_Btn".Translate()))
            {
                string dir = AudioLoader.MenuMusicDirectory;
                if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
            }
            TooltipHandler.TipRegion(menuBtnRect, "Rimify_MenuMusicFolder_Tip".Translate());

            listing.Gap(14f);
            listing.GapLine();
            listing.Gap(10f);

            if (AudioLoader.Config != null)
            {
                // Toggle: Custom main menu music / Тумблер: Пользовательская музыка в главном меню
                bool prevMenuMusic = AudioLoader.Config.CustomMenuMusic;
                listing.CheckboxLabeled(
                    "Rimify_Setting_CustomMenuMusic".Translate(),
                    ref AudioLoader.Config.CustomMenuMusic,
                    "Rimify_Setting_CustomMenuMusic_Desc".Translate()
                );

                if (prevMenuMusic != AudioLoader.Config.CustomMenuMusic)
                {
                    AudioLoader.Config.Save();
                    AudioLoader.ApplyMenuMusicSetting();
                }

                listing.Gap(10f);

                // Toggle: Continuous non-stop playback / Тумблер: Непрерывное воспроизведение
                bool prevContinuous = AudioLoader.Config.ContinuousPlayback;
                listing.CheckboxLabeled(
                    "Rimify_Setting_ContinuousMode".Translate(),
                    ref AudioLoader.Config.ContinuousPlayback,
                    "Rimify_Setting_ContinuousMode_Desc".Translate()
                );

                if (prevContinuous != AudioLoader.Config.ContinuousPlayback)
                {
                    AudioLoader.Config.Save();
                }

                listing.Gap(8f);

                // Pause duration sliders between songs / Настройка интервалов тишины между треками
                if (!AudioLoader.Config.ContinuousPlayback)
                {
                    GUI.color = new Color(0.35f, 0.85f, 0.45f);
                    listing.Label("Rimify_Setting_PauseHeader".Translate());
                    GUI.color = Color.white;

                    float minPause = AudioLoader.Config.MinPauseSeconds;
                    listing.Label("Rimify_Setting_MinPause".Translate(Mathf.RoundToInt(minPause)));
                    float newMin = listing.Slider(minPause, 5f, 180f);
                    if (Mathf.Abs(newMin - minPause) > 1f)
                    {
                        AudioLoader.Config.MinPauseSeconds = newMin;
                        if (AudioLoader.Config.MaxPauseSeconds < newMin)
                        {
                            AudioLoader.Config.MaxPauseSeconds = newMin;
                        }
                        AudioLoader.Config.Save();
                    }

                    float maxPause = AudioLoader.Config.MaxPauseSeconds;
                    listing.Label("Rimify_Setting_MaxPause".Translate(Mathf.RoundToInt(maxPause)));
                    float newMax = listing.Slider(maxPause, Mathf.Max(10f, AudioLoader.Config.MinPauseSeconds), 360f);
                    if (Mathf.Abs(newMax - maxPause) > 1f)
                    {
                        AudioLoader.Config.MaxPauseSeconds = newMax;
                        AudioLoader.Config.Save();
                    }

                    listing.Gap(8f);
                }

                listing.GapLine();
                listing.Gap(10f);

                // HUD widget control section / Секция управления HUD-виджетом
                Rect hudRow = listing.GetRect(32f);
                if (Widgets.ButtonText(new Rect(hudRow.x, hudRow.y, 280f, 32f), "Rimify_Setting_ResetWidget".Translate()))
                {
                    AudioLoader.Config.WidgetX = -1f;
                    AudioLoader.Config.WidgetY = -1f;
                    AudioLoader.Config.Save();

                    Widget_MusicPlayer widget = Find.WindowStack.WindowOfType<Widget_MusicPlayer>();
                    if (widget != null)
                    {
                        widget.windowRect = new Rect(UI.screenWidth - widget.InitialSize.x - 170f, 12f, widget.InitialSize.x, widget.InitialSize.y);
                    }
                    Messages.Message("Rimify_Setting_ResetWidget_Tip".Translate(), MessageTypeDefOf.TaskCompletion, false);
                }
                TooltipHandler.TipRegion(new Rect(hudRow.x, hudRow.y, 280f, 32f), "Rimify_Setting_ResetWidget_Tip".Translate());

                listing.Gap(16f);

                // Library statistics info line / Информационная строка статистики медиатеки
                GUI.color = new Color(0.65f, 0.68f, 0.72f);
                int totalTracks = DefDatabase<SongDef>.DefCount;
                int customPlaylists = AudioLoader.Config.Playlists != null ? AudioLoader.Config.Playlists.Count : 0;
                listing.Label("Rimify_Setting_Stats".Translate(totalTracks, customPlaylists));
                GUI.color = Color.white;
            }

            listing.End();
        }
    }
}
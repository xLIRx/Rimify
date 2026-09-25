using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Main music library and control window / Главное окно медиатеки и управления плеером
    public class Dialog_MusicManager : Window
    {
        private static Vector2? savedPosition = null;

        // Header dragging variables / Переменные перетаскивания за шапку
        private bool isDraggingHeader = false;
        private Vector2 dragOffset;

        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 sideScrollPosition = Vector2.zero;
        private string searchFilter = "";
        private string currentCategory = "All";

        // YouTube inline spoiler row / Сворачиваемая панель загрузки YouTube
        private bool isDownloadSectionOpen = false;
        private string inlineDownloadUrl = "";
        private bool inlineIsCombat = false;

        // Caching and metrics / Кэширование и счетчики
        private bool isCacheDirty = true;
        private string lastSearchFilter = null;
        private string lastCategory = null;
        private List<SongDef> cachedFilteredList = new List<SongDef>();

        private int cachedTotal;
        private int cachedCustom;
        private int cachedCore;
        private int cachedMods;
        private int cachedPeaceful;
        private int cachedCombat;
        private int cachedDisabled;

        private const float RowHeight = 36f;

        public override Vector2 InitialSize => new Vector2(980f, 660f);
        protected override float Margin => 0f;

        public Dialog_MusicManager()
        {
            doCloseX = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            forcePause = false;
            doWindowBackground = false;
            draggable = false; // Disabled to prevent button event interference / Отключено во избежание конфликта событий кнопок
        }

        // Mark library cache dirty to recalculate counts / Пометить кэш грязным для пересчёта счетчиков
        public void InvalidateCache()
        {
            isCacheDirty = true;
        }

        // Safe localization getter with English fallback / Безопасное получение локализации с английским резервом
        private static string Tr(string key, string englishFallback, params object[] args)
        {
            if (key.TryTranslate(out TaggedString translated))
            {
                string text = translated.ToString();
                return (args != null && args.Length > 0) ? string.Format(text, args) : text;
            }
            return (args != null && args.Length > 0) ? string.Format(englishFallback, args) : englishFallback;
        }

        // Update list cache and counters / Обновление кэша списка и счетчиков категорий
        private void EnsureCacheUpdated()
        {
            if (!isCacheDirty && lastSearchFilter == searchFilter && lastCategory == currentCategory) return;

            isCacheDirty = false;
            lastSearchFilter = searchFilter;
            lastCategory = currentCategory;

            List<SongDef> allSongs = DefDatabase<SongDef>.AllDefsListForReading;

            cachedTotal = allSongs.Count;
            cachedCustom = allSongs.Count(s => s.defName != null && (s.defName.StartsWith("Rimify_") || s.defName.StartsWith("YTM_")));
            cachedCore = allSongs.Count(s => s.modContentPack == null || s.modContentPack.IsCoreMod || s.modContentPack.IsOfficialMod);
            cachedMods = allSongs.Count(s => s.modContentPack != null && !s.modContentPack.IsCoreMod && !s.modContentPack.IsOfficialMod && !s.defName.StartsWith("Rimify_") && !s.defName.StartsWith("YTM_"));
            cachedPeaceful = allSongs.Count(s => !s.tense);
            cachedCombat = allSongs.Count(s => s.tense);
            cachedDisabled = allSongs.Count(s => AudioLoader.Config != null && AudioLoader.Config.IsSongDisabled(s.defName));

            IEnumerable<SongDef> query = allSongs;

            if (currentCategory == "Custom")
                query = query.Where(s => s.defName != null && (s.defName.StartsWith("Rimify_") || s.defName.StartsWith("YTM_")));
            else if (currentCategory == "Core")
                query = query.Where(s => s.modContentPack == null || s.modContentPack.IsCoreMod || s.modContentPack.IsOfficialMod);
            else if (currentCategory == "Mods")
                query = query.Where(s => s.modContentPack != null && !s.modContentPack.IsCoreMod && !s.modContentPack.IsOfficialMod && !s.defName.StartsWith("Rimify_") && !s.defName.StartsWith("YTM_"));
            else if (currentCategory == "Peaceful")
                query = query.Where(s => !s.tense);
            else if (currentCategory == "Combat")
                query = query.Where(s => s.tense);
            else if (currentCategory == "Disabled")
                query = query.Where(s => AudioLoader.Config != null && AudioLoader.Config.IsSongDisabled(s.defName));
            else if (currentCategory.StartsWith("PL:"))
            {
                string plName = currentCategory.Substring(3);
                var pl = AudioLoader.Config?.Playlists.FirstOrDefault(p => p.Name == plName);
                HashSet<string> songNames = pl != null ? new HashSet<string>(pl.Songs) : new HashSet<string>();
                query = query.Where(s => songNames.Contains(s.defName));
            }

            if (!string.IsNullOrEmpty(searchFilter))
            {
                string sFilter = searchFilter.Trim().ToLowerInvariant();
                query = query.Where(s => (s.label != null && s.label.ToLowerInvariant().Contains(sFilter)) ||
                                         (s.defName != null && s.defName.ToLowerInvariant().Contains(sFilter)));
            }

            cachedFilteredList = query.ToList();
        }

        protected override void SetInitialSizeAndPosition()
        {
            if (savedPosition.HasValue)
            {
                float x = Mathf.Clamp(savedPosition.Value.x, 0f, UI.screenWidth - InitialSize.x);
                float y = Mathf.Clamp(savedPosition.Value.y, 0f, UI.screenHeight - InitialSize.y);
                windowRect = new Rect(x, y, InitialSize.x, InitialSize.y);
            }
            else
            {
                base.SetInitialSizeAndPosition();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsureCacheUpdated();

            // Safe header dragging execution / Безопасное перетаскивание за верхнюю панель
            HandleHeaderDragging(inRect);

            // Window background layer / Фоновая подложка окна
            Widgets.DrawRectFast(inRect, new Color(0.11f, 0.11f, 0.12f, 0.98f));

            float topBarH = 46f;
            float footerH = 68f;
            float sideW = 210f;
            float dlSectionH = isDownloadSectionOpen ? 38f : 0f;

            // --- Top Bar / Верхняя панель ---
            Rect topRect = new Rect(0f, 0f, inRect.width, topBarH);
            Widgets.DrawBoxSolid(topRect, new Color(0.09f, 0.10f, 0.12f));

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(16f, 12f, 110f, 26f), Tr("Rimify_Title", "Rimify"));

            Widgets.Label(new Rect(sideW + 10f, 12f, 55f, 26f), Tr("Rimify_Search", "Search:"));

            Rect searchFieldRect = new Rect(sideW + 68f, 10f, 200f, 26f);
            Widgets.DrawBoxSolid(searchFieldRect, new Color(0.06f, 0.07f, 0.09f));
            GUI.color = new Color(0.28f, 0.32f, 0.38f, 0.7f);
            Widgets.DrawBox(searchFieldRect, 1);
            GUI.color = Color.white;
            searchFilter = Widgets.TextField(new Rect(searchFieldRect.x + 4f, searchFieldRect.y + 1f, searchFieldRect.width - 8f, 24f), searchFilter);

            // YouTube spoiler button / Кнопка спойлера YouTube
            string spoilerArrow = isDownloadSectionOpen ? "▼ " : "► ";
            string ytBtnText = spoilerArrow + Tr("Rimify_DownloadSection", "YouTube");
            Rect ytBtn = new Rect(inRect.width - 388f, 8f, 115f, 30f);
            DrawTextButton(ytBtn, ytBtnText, () =>
            {
                isDownloadSectionOpen = !isDownloadSectionOpen;
            }, isDownloadSectionOpen ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.35f, 0.35f));

            // Tracks folder button / Кнопка открытия папки треков
            Rect folderBtn = new Rect(inRect.width - 265f, 8f, 118f, 30f);
            DrawTextButton(folderBtn, Tr("Rimify_TracksFolder", "Tracks folder"), () =>
            {
                Process.Start(new ProcessStartInfo { FileName = AudioLoader.MusicDirectory, UseShellExecute = true });
            });

            // Refresh library button / Кнопка обновления фонотеки
            string refreshLabel = AudioLoader.IsReloading ? Tr("Rimify_Refreshing", "Refreshing...") : Tr("Rimify_Refresh", "Refresh");
            Rect refreshBtn = new Rect(inRect.width - 140f, 8f, 100f, 30f);
            DrawTextButton(refreshBtn, refreshLabel, () =>
            {
                if (!AudioLoader.IsReloading)
                {
                    AudioLoader.ReloadAllTracksAsync(() => InvalidateCache());
                }
            }, AudioLoader.IsReloading ? new Color(0.6f, 0.6f, 0.6f) : (Color?)null);

            // Close button / Кнопка закрытия окна
            Rect closeBtnRect = new Rect(inRect.width - 34f, 10f, 26f, 26f);
            if (Mouse.IsOver(closeBtnRect)) Widgets.DrawBoxSolid(closeBtnRect, new Color(0.9f, 0.25f, 0.25f, 0.35f));
            GUI.color = Mouse.IsOver(closeBtnRect) ? new Color(1f, 0.4f, 0.4f) : new Color(0.72f, 0.75f, 0.80f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(closeBtnRect, "✕");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(closeBtnRect, Tr("Rimify_Close", "Close"));
            if (Widgets.ButtonInvisible(closeBtnRect))
            {
                Close();
            }

            // --- YouTube Inline Download Row / Встроенная строка скачивания с YouTube ---
            if (isDownloadSectionOpen)
            {
                Rect dlPanelRect = new Rect(0f, topBarH, inRect.width, dlSectionH);
                Widgets.DrawBoxSolid(dlPanelRect, new Color(0.08f, 0.09f, 0.11f));
                Widgets.DrawLineHorizontal(0f, topBarH + dlSectionH - 1f, inRect.width);

                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                Widgets.Label(new Rect(16f, topBarH + 7f, 36f, 24f), "URL:");
                GUI.color = Color.white;

                float fieldX = 56f;
                float combatW = 145f;
                float btnW = 100f;
                float updateBtnW = 44f;
                float spacing = 8f;
                float rightMargin = 16f;

                float fieldW = inRect.width - fieldX - combatW - btnW - updateBtnW - (spacing * 3f) - rightMargin;
                Rect fieldRect = new Rect(fieldX, topBarH + 6f, fieldW, 26f);
                Widgets.DrawBoxSolid(fieldRect, new Color(0.05f, 0.06f, 0.07f));
                GUI.color = new Color(0.30f, 0.34f, 0.40f);
                Widgets.DrawBox(fieldRect, 1);
                GUI.color = Color.white;

                inlineDownloadUrl = Widgets.TextField(new Rect(fieldRect.x + 4f, fieldRect.y + 1f, fieldRect.width - 8f, 24f), inlineDownloadUrl);

                // Combat checkbox / Чекбокс боевого режима
                float combatX = fieldRect.xMax + spacing;
                Rect combatRect = new Rect(combatX, topBarH + 6f, combatW, 26f);
                Widgets.CheckboxLabeled(combatRect, Tr("Rimify_YT_CombatPreset", "Combat track"), ref inlineIsCombat);

                // Download action button / Кнопка скачивания
                float btnX = combatRect.xMax + spacing;
                Rect actionBtnRect = new Rect(btnX, topBarH + 6f, btnW, 26f);

                Action startDownloadAction = () =>
                {
                    if (!string.IsNullOrEmpty(inlineDownloadUrl.Trim()))
                    {
                        string targetUrl = inlineDownloadUrl.Trim();
                        inlineDownloadUrl = "";
                        YouTubeDownloader.DownloadAudio(targetUrl, inlineIsCombat, success =>
                        {
                            if (success)
                            {
                                AudioLoader.ReloadAllTracksAsync(() => InvalidateCache());
                            }
                        });
                    }
                };

                if (!YouTubeDownloader.IsBusy)
                {
                    DrawTextButton(actionBtnRect, Tr("Rimify_DownloadBtn", "Download"), startDownloadAction, new Color(0.35f, 0.85f, 0.45f));
                }
                else
                {
                    int pct = (int)(YouTubeDownloader.DownloadProgress * 100f);
                    GUI.color = new Color(0.40f, 0.90f, 0.50f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(actionBtnRect, $"{pct}%");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                // Update yt-dlp core button / Кнопка обновления ядра yt-dlp (-U)
                float upX = actionBtnRect.xMax + spacing;
                Rect updateBtnRect = new Rect(upX, topBarH + 6f, updateBtnW, 26f);
                Widgets.DrawBoxSolid(updateBtnRect, new Color(0.14f, 0.16f, 0.19f));
                GUI.color = new Color(0.32f, 0.36f, 0.42f);
                Widgets.DrawBox(updateBtnRect, 1);
                GUI.color = Color.white;

                DrawTextButton(updateBtnRect, "UPD", () =>
                {
                    YouTubeDownloader.UpdateYtDlpAsync((ok, res) =>
                    {
                        Messages.Message(res, ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.CautionInput, false);
                    });
                }, new Color(0.75f, 0.82f, 0.92f));
                TooltipHandler.TipRegion(updateBtnRect, Tr("Rimify_YT_BtnUpdate", "Update yt-dlp core (-U)"));

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !YouTubeDownloader.IsBusy)
                {
                    startDownloadAction();
                    Event.current.Use();
                }
            }

            float contentStartY = topBarH + dlSectionH;

            // --- Sidebar / Боковая панель категорий и плейлистов ---
            Rect sideRect = new Rect(0f, contentStartY, sideW, inRect.height - contentStartY - footerH);
            Widgets.DrawBoxSolid(sideRect, new Color(0.10f, 0.11f, 0.13f));

            int plCount = AudioLoader.Config?.Playlists != null ? AudioLoader.Config.Playlists.Count : 0;
            float sideContentH = 260f + plCount * 34f + 50f;
            Rect sideViewRect = new Rect(0f, 0f, sideW - 16f, Mathf.Max(sideContentH, sideRect.height));

            Widgets.BeginScrollView(sideRect, ref sideScrollPosition, sideViewRect);
            try
            {
                float catY = 8f;
                DrawCategoryBtn("All", $"{Tr("Rimify_Cat_All", "All music")} ({cachedTotal})", ref catY, sideViewRect.width);
                DrawCategoryBtn("Custom", $"{Tr("Rimify_Cat_Custom", "My tracks")} ({cachedCustom})", ref catY, sideViewRect.width);
                DrawCategoryBtn("Core", $"Core & DLC ({cachedCore})", ref catY, sideViewRect.width);
                DrawCategoryBtn("Mods", $"{Tr("Rimify_Cat_Mods", "Mods")} ({cachedMods})", ref catY, sideViewRect.width);

                catY += 6f;
                Widgets.DrawLineHorizontal(10f, catY, sideViewRect.width - 20f);
                catY += 6f;

                DrawCategoryBtn("Peaceful", $"{Tr("Rimify_Cat_Peaceful", "Peaceful")} ({cachedPeaceful})", ref catY, sideViewRect.width);
                DrawCategoryBtn("Combat", $"{Tr("Rimify_Cat_Combat", "Combat")} ({cachedCombat})", ref catY, sideViewRect.width);
                DrawCategoryBtn("Disabled", $"{Tr("Rimify_Cat_Disabled", "Disabled")} ({cachedDisabled})", ref catY, sideViewRect.width);

                catY += 6f;
                Widgets.DrawLineHorizontal(10f, catY, sideViewRect.width - 20f);
                catY += 8f;

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(12f, catY + 4f, 120f, 22f), Tr("Rimify_PlaylistsHeader", "PLAYLISTS"));
                GUI.color = Color.white;

                Rect plusBtnRect = new Rect(sideViewRect.width - 28f, catY + 1f, 22f, 22f);
                DrawTextButton(plusBtnRect, "+", () =>
                {
                    Find.WindowStack.Add(new Dialog_CreatePlaylist(name =>
                    {
                        AudioLoader.Config.CreatePlaylist(name);
                        currentCategory = "PL:" + name;
                        InvalidateCache();
                    }));
                });
                catY += 28f;

                if (AudioLoader.Config?.Playlists != null)
                {
                    for (int pIdx = 0; pIdx < AudioLoader.Config.Playlists.Count; pIdx++)
                    {
                        var pl = AudioLoader.Config.Playlists[pIdx];
                        string plId = "PL:" + pl.Name;
                        Rect plRowRect = new Rect(6f, catY, sideViewRect.width - 12f, 28f);

                        bool selected = currentCategory == plId;
                        if (selected) Widgets.DrawBoxSolid(plRowRect, new Color(0.18f, 0.21f, 0.26f));
                        else if (Mouse.IsOver(plRowRect)) Widgets.DrawHighlight(plRowRect);

                        Text.Font = GameFont.Tiny;
                        string plTitle = $"{pl.Name} ({pl.Songs.Count})";
                        if (plTitle.Length > 22) plTitle = plTitle.Substring(0, 20) + "...";
                        Widgets.Label(new Rect(plRowRect.x + 8f, plRowRect.y + 6f, plRowRect.width - 32f, 20f), plTitle);

                        if (Widgets.ButtonInvisible(new Rect(plRowRect.x, plRowRect.y, plRowRect.width - 24f, plRowRect.height)))
                        {
                            currentCategory = plId;
                        }

                        Rect delBtnRect = new Rect(plRowRect.xMax - 20f, plRowRect.y + 4f, 18f, 20f);
                        if (Mouse.IsOver(delBtnRect)) Widgets.DrawHighlight(delBtnRect);
                        GUI.color = new Color(0.85f, 0.40f, 0.40f);
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(delBtnRect, "✕");
                        Text.Anchor = TextAnchor.UpperLeft;
                        GUI.color = Color.white;

                        if (Widgets.ButtonInvisible(delBtnRect))
                        {
                            string toDelete = pl.Name;
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(Tr("Rimify_Dlg_DeleteConfirm", "Delete playlist \"{0}\"?", toDelete), () =>
                            {
                                AudioLoader.Config.DeletePlaylist(toDelete);
                                if (currentCategory == "PL:" + toDelete) currentCategory = "All";
                                InvalidateCache();
                            }, true));
                        }

                        catY += 32f;
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            // --- Central Table Area / Центральная таблица треков ---
            float contentX = sideW;
            float contentW = inRect.width - sideW;
            float contentH = inRect.height - contentStartY - footerH;

            bool isPlaylist = currentCategory.StartsWith("PL:");
            string currentPlaylist = isPlaylist ? currentCategory.Substring(3) : null;
            float playlistBarH = isPlaylist ? 36f : 0f;

            if (isPlaylist)
            {
                Rect lockBarRect = new Rect(contentX, contentStartY, contentW, 36f);
                Widgets.DrawBoxSolid(lockBarRect, new Color(0.11f, 0.12f, 0.14f));
                Widgets.DrawLineHorizontal(contentX, contentStartY + 35f, contentW);

                bool isLocked = AudioLoader.Config.ActiveLockedPlaylist != null &&
                                AudioLoader.Config.ActiveLockedPlaylist.Equals(currentPlaylist, StringComparison.OrdinalIgnoreCase);

                Rect toggleLockBtn = new Rect(contentX + 12f, contentStartY + 4f, 320f, 28f);
                string lockLabel = (isLocked ? "● " : "○ ") + (isLocked ? Tr("Rimify_LockPlaylist_On", "Playing only this playlist (Active)") : Tr("Rimify_LockPlaylist_Off", "Play only this playlist"));
                Color textColor = isLocked ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.78f, 0.82f, 0.86f);

                DrawTextButton(toggleLockBtn, lockLabel, () =>
                {
                    if (isLocked)
                    {
                        AudioLoader.Config.ActiveLockedPlaylist = null;
                        AudioLoader.Config.Save();

                        AudioSource src = MusicController.GetAudioSource();
                        if (src == null || !src.isPlaying)
                        {
                            MusicController.PlayNextDefaultSong();
                        }
                    }
                    else
                    {
                        AudioLoader.Config.ActiveLockedPlaylist = currentPlaylist;
                        AudioLoader.Config.Save();
                        MusicController.PlayNextFromLockedPlaylist();
                    }
                }, textColor);
            }

            float tableY = contentStartY + playlistBarH;
            Rect headerRect = new Rect(contentX, tableY, contentW, 28f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.14f, 0.15f, 0.18f));
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(contentX + 10f, tableY + 6f, 30f, 20f), Tr("Rimify_Col_Num", "#"));
            Widgets.Label(new Rect(contentX + 45f, tableY + 6f, contentW - 430f, 20f), Tr("Rimify_Col_Title", "TITLE"));
            Widgets.Label(new Rect(contentX + contentW - 380f, tableY + 6f, 115f, 20f), Tr("Rimify_Col_Source", "SOURCE"));
            Widgets.Label(new Rect(contentX + contentW - 260f, tableY + 6f, 160f, 20f), Tr("Rimify_Col_Tags", "TAGS"));
            Widgets.Label(new Rect(contentX + contentW - 75f, tableY + 6f, 50f, 20f), Tr("Rimify_Col_Time", "TIME"));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Virtualized row culling / Виртуализированный рендеринг
            Rect tableOutRect = new Rect(contentX, tableY + 28f, contentW, contentH - playlistBarH - 28f);
            float totalHeight = cachedFilteredList.Count * RowHeight;
            Rect tableViewRect = new Rect(0f, 0f, contentW - 16f, Mathf.Max(totalHeight, tableOutRect.height));

            Widgets.BeginScrollView(tableOutRect, ref scrollPosition, tableViewRect);
            try
            {
                int startIndex = Mathf.Max(0, (int)(scrollPosition.y / RowHeight));
                int endIndex = Mathf.Min(cachedFilteredList.Count - 1, (int)((scrollPosition.y + tableOutRect.height) / RowHeight) + 1);

                SongDef curSong = MusicController.CurrentSong;

                for (int i = startIndex; i <= endIndex; i++)
                {
                    SongDef song = cachedFilteredList[i];
                    float rowY = i * RowHeight;
                    Rect rowRect = new Rect(0f, rowY, tableViewRect.width, 34f);

                    bool isCurrent = curSong == song;
                    bool isDis = AudioLoader.Config != null && AudioLoader.Config.IsSongDisabled(song.defName);
                    bool isCust = song.defName != null && (song.defName.StartsWith("Rimify_") || song.defName.StartsWith("YTM_"));

                    if (isCurrent)
                    {
                        Widgets.DrawBoxSolid(rowRect, new Color(0.12f, 0.32f, 0.18f, 0.75f));
                        Widgets.DrawBoxSolid(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), new Color(0.35f, 0.85f, 0.45f));
                    }
                    else if (isDis)
                    {
                        Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.12f, 0.12f, 0.35f));
                    }
                    else if (i % 2 == 0)
                    {
                        Widgets.DrawBoxSolid(rowRect, new Color(0.14f, 0.15f, 0.17f, 0.45f));
                    }

                    if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

                    if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rowRect.Contains(Event.current.mousePosition))
                    {
                        OpenSongContextMenu(song);
                        Event.current.Use();
                    }

                    Rect playAreaRect = new Rect(0f, rowY, contentW - 265f, 34f);
                    if (Widgets.ButtonInvisible(playAreaRect, false))
                    {
                        MusicController.PlaySongNow(song);
                    }
                    TooltipHandler.TipRegion(playAreaRect, Tr("Rimify_Tip_RowPlay", "LMB: Play track\nRMB: Playlist menu"));

                    GUI.color = new Color(0.5f, 0.5f, 0.5f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(10f, rowY + 9f, 28f, 20f), (i + 1).ToString());

                    if (isCurrent) GUI.color = new Color(0.40f, 0.95f, 0.50f);
                    else if (isDis) GUI.color = new Color(0.50f, 0.50f, 0.50f);
                    else GUI.color = Color.white;

                    Text.Font = GameFont.Small;
                    string songTitle = MusicController.GetSongName(song);
                    if (songTitle.Length > 34) songTitle = songTitle.Substring(0, 32) + "...";
                    Widgets.Label(new Rect(45f, rowY + 6f, contentW - 430f, 24f), songTitle);

                    float srcX = contentW - 380f;
                    Texture2D srcIcon = SourceIconCache.GetIcon(song);
                    if (srcIcon != null)
                    {
                        GUI.color = Color.white;
                        GUI.DrawTexture(new Rect(srcX, rowY + 7f, 20f, 20f), srcIcon, ScaleMode.ScaleToFit);
                    }

                    Text.Font = GameFont.Tiny;
                    GUI.color = isDis ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
                    string src = isCust ? Tr("Rimify_Source_Custom", "YouTube / Custom") : (song.modContentPack != null ? song.modContentPack.Name : "Core");
                    if (src.Length > 15) src = src.Substring(0, 13) + "...";
                    Widgets.Label(new Rect(srcX + 24f, rowY + 9f, 90f, 20f), src);

                    float tagX = contentW - 260f;

                    Texture2D modeIcon = song.tense ? TexIcons.Combat : TexIcons.Peaceful;
                    Rect modeBtnRect = new Rect(tagX, rowY + 6f, 22f, 22f);
                    GUI.color = Color.white;
                    if (Widgets.ButtonImage(modeBtnRect, modeIcon, false))
                    {
                        song.tense = !song.tense;
                        AudioLoader.Config.SetSongOverride(song.defName, song.tense, song.allowedTimeOfDay);
                        InvalidateCache();
                    }
                    TooltipHandler.TipRegion(modeBtnRect, song.tense ? Tr("Rimify_Tip_ModeCombat", "Mode: Combat (click to switch to Peaceful)") : Tr("Rimify_Tip_ModePeaceful", "Mode: Peaceful (click to switch to Combat)"));

                    TimeOfDay displayTime = song.allowedTimeOfDay;
                    Texture2D timeIcon = displayTime == TimeOfDay.Any ? TexIcons.AnyTime :
                                         displayTime == TimeOfDay.Day ? TexIcons.Day : TexIcons.Night;
                    Rect timeBtnRect = new Rect(tagX + 26f, rowY + 6f, 22f, 22f);
                    GUI.color = Color.white;
                    if (Widgets.ButtonImage(timeBtnRect, timeIcon, false))
                    {
                        TimeOfDay nextTime = displayTime == TimeOfDay.Any ? TimeOfDay.Day :
                                             displayTime == TimeOfDay.Day ? TimeOfDay.Night : TimeOfDay.Any;
                        song.allowedTimeOfDay = nextTime;
                        AudioLoader.Config.SetSongOverride(song.defName, song.tense, nextTime);
                    }
                    string timeLabel = displayTime == TimeOfDay.Any ? Tr("Rimify_Time_Any", "Any") :
                                       displayTime == TimeOfDay.Day ? Tr("Rimify_Time_Day", "Day") : Tr("Rimify_Time_Night", "Night");
                    TooltipHandler.TipRegion(timeBtnRect, Tr("Rimify_Tip_TimeOfDay", "Time of day: {0} (click to switch)", timeLabel));

                    Texture2D rotIcon = isDis ? TexIcons.Muted : TexIcons.Audible;
                    Rect rotBtnRect = new Rect(tagX + 52f, rowY + 6f, 22f, 22f);
                    GUI.color = Color.white;
                    if (Widgets.ButtonImage(rotBtnRect, rotIcon, false))
                    {
                        AudioLoader.Config.ToggleSongDisabled(song.defName);
                        AudioLoader.ApplyOverrides();
                        InvalidateCache();
                        if (!isDis && MusicController.CurrentSong == song)
                        {
                            MusicController.SkipTrack();
                        }
                    }
                    TooltipHandler.TipRegion(rotBtnRect, isDis ? Tr("Rimify_Tip_RotMuted", "Excluded from rotation (click to enable)") : Tr("Rimify_Tip_RotAudible", "In game rotation (click to disable)"));

                    if (isPlaylist)
                    {
                        Rect remBtnRect = new Rect(tagX + 78f, rowY + 6f, 22f, 22f);
                        if (Widgets.ButtonImage(remBtnRect, TexIcons.RemoveFromPlaylist, false))
                        {
                            AudioLoader.Config.ToggleSongInPlaylist(currentPlaylist, song.defName);
                            InvalidateCache();
                        }
                        TooltipHandler.TipRegion(remBtnRect, Tr("Rimify_Tip_RemoveFromThisPlaylist", "Remove from this playlist"));
                    }
                    else
                    {
                        Rect addBtnRect = new Rect(tagX + 78f, rowY + 6f, 22f, 22f);
                        if (Widgets.ButtonImage(addBtnRect, TexIcons.AddToPlaylist, false))
                        {
                            OpenSongContextMenu(song);
                        }
                        TooltipHandler.TipRegion(addBtnRect, Tr("Rimify_Tip_AddToPlaylist", "Add to playlist"));
                    }

                    if (isCust)
                    {
                        Rect delTrackRect = new Rect(tagX + 104f, rowY + 6f, 22f, 22f);
                        if (Widgets.ButtonImage(delTrackRect, TexIcons.Trash, false))
                        {
                            SongDef toDelete = song;
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                Tr("Rimify_Dlg_DeleteTrackConfirm", "Permanently delete track \"{0}\" from disk?", MusicController.GetSongName(toDelete)), () =>
                            {
                                AudioLoader.DeleteCustomSong(toDelete);
                                InvalidateCache();
                            }, true));
                        }
                        TooltipHandler.TipRegion(delTrackRect, Tr("Rimify_Tip_DeleteTrack", "Delete track from disk"));
                    }

                    GUI.color = isDis ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);
                    float len = song.clip != null ? song.clip.length : 0f;
                    string timeStr = len > 0f ? $"{(int)len / 60:D1}:{(int)len % 60:D2}" : "--:--";
                    Widgets.Label(new Rect(contentW - 75f, rowY + 9f, 45f, 20f), timeStr);

                    GUI.color = Color.white;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            // --- Footer Player Controls / Нижняя панель управления ---
            Rect footerRect = new Rect(0f, inRect.height - footerH, inRect.width, footerH);
            Widgets.DrawBoxSolid(footerRect, new Color(0.08f, 0.09f, 0.10f));
            Widgets.DrawLineHorizontal(0f, footerRect.y, inRect.width);

            // Current track info with Marquee / Информация о текущем треке с бегущей строкой
            Text.Font = GameFont.Small;
            Rect curTitleRect = new Rect(18f, footerRect.y + 12f, 250f, 22f);
            DrawMarqueeText(curTitleRect, MusicController.GetCurrentTrackTitle());

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(18f, footerRect.y + 34f, 250f, 18f), MusicController.GetTrackTimeString());
            GUI.color = Color.white;

            // Player control buttons / Кнопки управления плеером
            float midX = inRect.width * 0.5f;
            float btnY = footerRect.y + 8f;

            if (Widgets.ButtonImage(new Rect(midX - 58f, btnY + 3f, 26f, 26f), TexIcons.Prev, false))
            {
                MusicController.RestartOrPrevious();
            }

            Texture2D playPauseTex = MusicController.IsUserPaused ? TexIcons.Play : TexIcons.Pause;
            if (Widgets.ButtonImage(new Rect(midX - 16f, btnY, 32f, 32f), playPauseTex, false))
            {
                MusicController.TogglePlayPause();
            }

            if (Widgets.ButtonImage(new Rect(midX + 32f, btnY + 3f, 26f, 26f), TexIcons.Next, false))
            {
                MusicController.SkipTrack();
            }

            // Shuffle Toggle Button / Кнопка переключения перемешивания
            Rect shuffleRect = new Rect(midX + 68f, btnY + 3f, 26f, 26f);
            bool isShuffle = AudioLoader.Config == null || AudioLoader.Config.ShuffleMode;
            Texture2D shuffleIcon = isShuffle ? TexIcons.Shuffle : TexIcons.Sequential;
            string shuffleTip = isShuffle ? "Rimify_Tip_ShuffleOn".Translate() : "Rimify_Tip_ShuffleOff".Translate();
            DrawModeToggle(shuffleRect, shuffleIcon, isShuffle, shuffleTip, () =>
            {
                if (AudioLoader.Config != null)
                {
                    AudioLoader.Config.ShuffleMode = !AudioLoader.Config.ShuffleMode;
                    AudioLoader.Config.Save();
                }
            });

            // Repeat Toggle Button / Кнопка переключения повтора трека
            Rect repeatRect = new Rect(midX + 98f, btnY + 3f, 26f, 26f);
            bool isRepeat = AudioLoader.Config != null && AudioLoader.Config.RepeatSingle;
            Texture2D repeatIcon = isRepeat ? TexIcons.Repeat : TexIcons.RepeatAll;
            string repeatTip = isRepeat ? "Rimify_Tip_RepeatOn".Translate() : "Rimify_Tip_RepeatOff".Translate();
            DrawModeToggle(repeatRect, repeatIcon, isRepeat, repeatTip, () =>
            {
                if (AudioLoader.Config != null)
                {
                    AudioLoader.Config.RepeatSingle = !AudioLoader.Config.RepeatSingle;
                    AudioLoader.Config.Save();
                }
            });

            // Continuous Mode Toggle Button / Кнопка непрерывного режима воспроизведения
            Rect modeToggleRect = new Rect(midX + 128f, btnY + 3f, 26f, 26f);
            bool isContinuous = AudioLoader.Config == null || AudioLoader.Config.ContinuousPlayback;
            Texture2D contIcon = isContinuous ? TexIcons.Continuous : TexIcons.Atmospheric;
            string modeTooltip = isContinuous
                ? Tr("Rimify_Setting_ContinuousMode", "Continuous playback: ON (click to enable pauses)")
                : Tr("Rimify_Setting_ContinuousMode_Off", "Pauses between songs: ON (click to play without pauses)");
            DrawModeToggle(modeToggleRect, contIcon, isContinuous, modeTooltip, () =>
            {
                if (AudioLoader.Config != null)
                {
                    AudioLoader.Config.ContinuousPlayback = !AudioLoader.Config.ContinuousPlayback;
                    AudioLoader.Config.Save();

                    if (AudioLoader.Config.ContinuousPlayback)
                    {
                        MusicController.SkipTrack();
                    }
                }
            });

            // Playback progress bar with seek click / Шкала воспроизведения с перемоткой кликом
            Rect barRect = new Rect(midX - 150f, footerRect.y + 46f, 300f, 8f);
            Widgets.DrawBoxSolid(barRect, new Color(0.2f, 0.22f, 0.25f));
            float p = MusicController.GetTrackProgress();
            if (p > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(barRect.x, barRect.y, barRect.width * p, barRect.height), new Color(0.35f, 0.85f, 0.45f));
            }

            if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
            {
                float click01 = (Event.current.mousePosition.x - barRect.x) / barRect.width;
                MusicController.SeekTo(click01);
                Event.current.Use();
            }

            // --- Volume Slider without DragSlider loop / Регулятор громкости без зацикливания DragSlider ---
            float volX = inRect.width - 200f;
            float volY = footerRect.y + 20f;

            GUI.color = new Color(0.75f, 0.78f, 0.82f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(volX, volY, 24f, 24f), "🔊");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Rect volSliderRect = new Rect(volX + 26f, volY + 3f, 150f, 20f);
            float curVol = Prefs.VolumeMusic;
            
            // roundTo = -1f eliminates cyclical DragSlider sound spam / roundTo = -1f исключает циклический спам звука DragSlider
            float newVol = Widgets.HorizontalSlider(volSliderRect, curVol, 0f, 1f, true, null, null, null, -1f);
            if (Mathf.Abs(newVol - curVol) > 0.001f)
            {
                Prefs.VolumeMusic = newVol;
                AudioSource src = MusicController.GetAudioSource();
                if (src != null)
                {
                    SongDef curSongDef = MusicController.CurrentSong;
                    float baseVol = (curSongDef != null && curSongDef.volume > 0f) ? curSongDef.volume : 1f;
                    src.volume = baseVol * newVol;
                }
            }
            TooltipHandler.TipRegion(new Rect(volX, volY, 180f, 26f), Tr("Rimify_VolumeTip", "Music volume: {0}%", Mathf.RoundToInt(newVol * 100f)));

            Text.Font = GameFont.Small;
        }

        // Smooth header dragging without button event interception / Плавное перетаскивание за свободную область шапки без перехвата кнопок
        private void HandleHeaderDragging(Rect inRect)
        {
            Event ev = Event.current;

            // Dragging zone: left header area and search surroundings / Зона перетаскивания: левая часть шапки и область вокруг поиска
            float safeWidth = inRect.width - 400f;
            Rect dragBarRect = new Rect(0f, 0f, Mathf.Max(240f, safeWidth), 44f);

            if (ev.type == EventType.MouseDown && ev.button == 0 && Mouse.IsOver(dragBarRect))
            {
                Rect searchRect = new Rect(278f, 10f, 200f, 26f);
                if (!searchRect.Contains(ev.mousePosition))
                {
                    isDraggingHeader = true;
                    dragOffset = ev.mousePosition;
                    ev.Use();
                }
            }

            if (isDraggingHeader)
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 mousePos = UI.MousePositionOnUIInverted;
                    float newX = Mathf.Clamp(mousePos.x - dragOffset.x, 0f, UI.screenWidth - windowRect.width);
                    float newY = Mathf.Clamp(mousePos.y - dragOffset.y, 0f, UI.screenHeight - windowRect.height);

                    windowRect = new Rect(newX, newY, windowRect.width, windowRect.height);
                    savedPosition = new Vector2(newX, newY);
                }
                else
                {
                    isDraggingHeader = false;
                }
            }
        }

        // Marquee scrolling text renderer / Отрисовка бегущей строки
        private static void DrawMarqueeText(Rect rect, string text, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(text)) return;

            Vector2 textSize = Text.CalcSize(text);
            if (textSize.x <= rect.width)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Widgets.Label(rect, text);
                GUI.color = Color.white;
                return;
            }

            float excess = textSize.x - rect.width + 10f;
            float scrollSpeed = 26f;
            float scrollDuration = excess / scrollSpeed;
            float pause = 1.8f;
            float totalCycle = (pause * 2f) + scrollDuration;

            float timeInCycle = Time.realtimeSinceStartup % totalCycle;
            float offsetX = 0f;

            if (timeInCycle < pause)
            {
                offsetX = 0f;
            }
            else if (timeInCycle < pause + scrollDuration)
            {
                offsetX = (timeInCycle - pause) * scrollSpeed;
            }
            else
            {
                offsetX = excess;
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.BeginGroup(rect);
            Widgets.Label(new Rect(-offsetX, 0f, textSize.x + 10f, rect.height), text);
            GUI.EndGroup();
            GUI.color = Color.white;
        }

        // Draw category button in sidebar / Отрисовка кнопки категории в боковой панели
        private void DrawCategoryBtn(string id, string text, ref float curY, float width)
        {
            Rect btnRect = new Rect(6f, curY, width - 12f, 28f);
            bool selected = currentCategory == id;
            if (selected) Widgets.DrawBoxSolid(btnRect, new Color(0.18f, 0.21f, 0.26f));
            else if (Mouse.IsOver(btnRect)) Widgets.DrawHighlight(btnRect);

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(btnRect.x + 8f, btnRect.y + 6f, btnRect.width - 8f, 20f), text);
            if (Widgets.ButtonInvisible(btnRect, false))
            {
                currentCategory = id;
            }
            curY += 32f;
        }

        // Draw styled text button / Отрисовка стилизованной текстовой кнопки
        private void DrawTextButton(Rect rect, string text, Action onClick, Color? textColor = null)
        {
            bool hover = Mouse.IsOver(rect);
            if (hover)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.07f));
            }

            Color col = textColor ?? (hover ? Color.white : new Color(0.78f, 0.82f, 0.86f));
            GUI.color = col;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rect, false))
            {
                onClick?.Invoke();
            }
        }

        // Context menu for track playlist actions and deletion / Контекстное меню управления треком и добавления в плейлисты
        private void OpenSongContextMenu(SongDef song)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (AudioLoader.Config?.Playlists == null || AudioLoader.Config.Playlists.Count == 0)
            {
                options.Add(new FloatMenuOption(Tr("Rimify_Menu_NoPlaylists", "No playlists (create one using \"+\" on the left)"), null));
            }
            else
            {
                foreach (var pl in AudioLoader.Config.Playlists)
                {
                    bool inPl = pl.Songs.Contains(song.defName);
                    string label = inPl ? Tr("Rimify_Menu_RemoveFrom", "✓ Remove from: {0}", pl.Name) : Tr("Rimify_Menu_AddTo", "+ Add to: {0}", pl.Name);
                    string plName = pl.Name;

                    options.Add(new FloatMenuOption(label, () =>
                    {
                        AudioLoader.Config.ToggleSongInPlaylist(plName, song.defName);
                        InvalidateCache();
                    }));
                }
            }

            options.Add(new FloatMenuOption(Tr("Rimify_Menu_CreateNew", "+ Create new playlist..."), () =>
            {
                Find.WindowStack.Add(new Dialog_CreatePlaylist(name =>
                {
                    AudioLoader.Config.CreatePlaylist(name);
                    AudioLoader.Config.ToggleSongInPlaylist(name, song.defName);
                    InvalidateCache();
                }));
            }));

            if (song.defName != null && (song.defName.StartsWith("Rimify_") || song.defName.StartsWith("YTM_")))
            {
                options.Add(new FloatMenuOption(Tr("Rimify_Menu_DeleteTrack", "✕ Delete track from disk"), () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        Tr("Rimify_Dlg_DeleteTrackConfirm", "Permanently delete track \"{0}\" from disk?", MusicController.GetSongName(song)), () =>
                    {
                        AudioLoader.DeleteCustomSong(song);
                        InvalidateCache();
                    }, true));
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Mode toggle button: pure icon and color transformation / Кнопка переключения режима со сменой иконки и цвета
        private void DrawModeToggle(Rect rect, Texture2D icon, bool isActive, string tooltip, Action onToggle)
        {
            bool hover = Mouse.IsOver(rect);
            if (hover)
            {
                Widgets.DrawHighlight(rect);
            }

            Color col = isActive
                ? (hover ? new Color(0.55f, 1.0f, 0.65f) : new Color(0.35f, 0.85f, 0.45f))
                : (hover ? new Color(0.85f, 0.88f, 0.92f) : new Color(0.48f, 0.52f, 0.56f));

            if (icon != null)
            {
                GUI.color = col;
                GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            TooltipHandler.TipRegion(rect, tooltip);

            if (Widgets.ButtonInvisible(rect, false))
            {
                onToggle?.Invoke();
            }
        }
    }

    // Modal dialog for new playlist creation / Модальный диалог создания нового плейлиста
    public class Dialog_CreatePlaylist : Window
    {
        private string playlistName = "";
        private readonly Action<string> onCreated;

        public override Vector2 InitialSize => new Vector2(380f, 150f);
        protected override float Margin => 0f;

        public Dialog_CreatePlaylist(Action<string> onCreated)
        {
            this.onCreated = onCreated;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            doWindowBackground = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, new Color(0.12f, 0.13f, 0.16f, 0.98f));
            GUI.color = new Color(0.28f, 0.32f, 0.38f, 0.85f);
            Widgets.DrawBox(inRect, 1);
            GUI.color = Color.white;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(18f, 16f, inRect.width - 36f, 30f), "Rimify_Dlg_NewPlaylistTitle".Translate());
            Text.Font = GameFont.Small;

            Rect fieldRect = new Rect(18f, 54f, inRect.width - 36f, 30f);
            Widgets.DrawBoxSolid(fieldRect, new Color(0.08f, 0.09f, 0.11f));
            GUI.color = new Color(0.35f, 0.40f, 0.45f, 0.8f);
            Widgets.DrawBox(fieldRect, 1);
            GUI.color = Color.white;

            playlistName = Widgets.TextField(new Rect(fieldRect.x + 4f, fieldRect.y + 2f, fieldRect.width - 8f, 26f), playlistName);

            float btnW = (inRect.width - 48f) * 0.5f;
            float btnY = 98f;
            float btnH = 34f;

            Rect createRect = new Rect(18f, btnY, btnW, btnH);
            DrawTextButton(createRect, "Rimify_Dlg_Create".Translate(), () =>
            {
                if (!string.IsNullOrEmpty(playlistName.Trim()))
                {
                    onCreated?.Invoke(playlistName.Trim());
                    Close();
                }
            }, new Color(0.40f, 0.90f, 0.50f));

            Rect cancelRect = new Rect(inRect.width - 18f - btnW, btnY, btnW, btnH);
            DrawTextButton(cancelRect, "Rimify_Dlg_Cancel".Translate(), () =>
            {
                Close();
            }, new Color(0.75f, 0.78f, 0.82f));

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                if (!string.IsNullOrEmpty(playlistName.Trim()))
                {
                    onCreated?.Invoke(playlistName.Trim());
                    Close();
                    Event.current.Use();
                }
            }
        }

        // Draw styled action button inside dialog / Отрисовка кнопки действия в модальном диалоге
        private void DrawTextButton(Rect rect, string text, Action onClick, Color normalColor)
        {
            bool hover = Mouse.IsOver(rect);
            if (hover)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.07f));
            }

            GUI.color = hover ? Color.white : normalColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rect, false))
            {
                onClick?.Invoke();
            }
        }
    }
}
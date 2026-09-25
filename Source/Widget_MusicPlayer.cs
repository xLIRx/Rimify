using RimWorld;
using UnityEngine;
using Verse;

namespace Rimify
{
    // KeyBindingDefOf registration / Регистрация горячих клавиш в DefOf
    [DefOf]
    public static class RimifyKeyBindingDefOf
    {
        public static KeyBindingDef Rimify_PlayPause;
        public static KeyBindingDef Rimify_NextTrack;
        public static KeyBindingDef Rimify_PrevTrack;

        static RimifyKeyBindingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RimifyKeyBindingDefOf));
        }
    }

    // Minimalist floating HUD music player / Минималистичный плавающий HUD-плеер над картой
    public class Widget_MusicPlayer : Window
    {
        private const float BaseHeight = 56f;
        private const float DownloadingHeight = 90f;
        private const float FullWidth = 365f;

        private float lastSavedX = -1f;
        private float lastSavedY = -1f;
        private float lastMoveTime = -1f;

        public override Vector2 InitialSize => new Vector2(FullWidth, BaseHeight);
        protected override float Margin => 0f;

        public Widget_MusicPlayer()
        {
            layer = WindowLayer.GameUI;
            soundAppear = null;
            soundClose = null;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true; // Native RimWorld Window dragging in both X and Y / Ванильное перетаскивание окна во всех направлениях
            drawShadow = false;
            focusWhenOpened = false;
            doWindowBackground = false;
        }

        protected override void SetInitialSizeAndPosition()
        {
            float posX = AudioLoader.Config.WidgetX;
            float posY = AudioLoader.Config.WidgetY;

            if (posX < 0f || posY < 0f || posX > UI.screenWidth || posY > UI.screenHeight)
            {
                posX = UI.screenWidth - FullWidth - 170f;
                posY = 12f;
            }

            windowRect = new Rect(posX, posY, FullWidth, BaseHeight);
            lastSavedX = posX;
            lastSavedY = posY;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            // Dynamic height during active download / Динамическая высота во время скачивания
            float targetH = YouTubeDownloader.IsBusy ? DownloadingHeight : BaseHeight;
            if (Mathf.Abs(windowRect.height - targetH) > 0.5f) windowRect.height = targetH;

            // Debounced position saving / Отложенное сохранение координат окна
            if (Mathf.Abs(windowRect.x - lastSavedX) > 1f || Mathf.Abs(windowRect.y - lastSavedY) > 1f)
            {
                lastSavedX = windowRect.x;
                lastSavedY = windowRect.y;
                lastMoveTime = Time.realtimeSinceStartup;
            }

            if (lastMoveTime > 0f && Time.realtimeSinceStartup - lastMoveTime > 0.5f)
            {
                lastMoveTime = -1f;
                if (AudioLoader.Config != null)
                {
                    AudioLoader.Config.WidgetX = windowRect.x;
                    AudioLoader.Config.WidgetY = windowRect.y;
                    AudioLoader.Config.Save();
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (AudioLoader.Config != null && !AudioLoader.Config.ShowWidget) return;

            // RMB anywhere on the widget opens music library / ПКМ в любой точке виджета открывает медиатеку
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && inRect.Contains(Event.current.mousePosition))
            {
                if (Find.WindowStack.WindowOfType<Dialog_MusicManager>() == null)
                {
                    Find.WindowStack.Add(new Dialog_MusicManager());
                }
                Event.current.Use();
            }

            // Clickable track title area / Клик по заголовку открывает медиатеку
            Rect titleRect = new Rect(8f, 5f, 235f, 18f);
            if (Mouse.IsOver(titleRect)) Widgets.DrawHighlight(titleRect);
            TooltipHandler.TipRegion(titleRect, "Rimify_Widget_Tip".Translate());

            if (Widgets.ButtonInvisible(titleRect))
            {
                if (Find.WindowStack.WindowOfType<Dialog_MusicManager>() == null)
                {
                    Find.WindowStack.Add(new Dialog_MusicManager());
                }
            }

            // Track title with Marquee scroll / Название трека с бегущей строкой
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            DrawMarqueeText(titleRect, MusicController.GetCurrentTrackTitle());

            // Playback timer (proper 20px clearance) / Таймер без усечения шрифта
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Widgets.Label(new Rect(8f, 24f, 235f, 20f), MusicController.GetTrackTimeString());
            GUI.color = Color.white;

            // Track playback progress line / Линия прогресса песни
            Rect barRect = new Rect(8f, 46f, 235f, 3f);
            Widgets.DrawBoxSolid(barRect, new Color(0.2f, 0.22f, 0.25f, 0.6f));
            float p = MusicController.GetTrackProgress();
            if (p > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(barRect.x, barRect.y, barRect.width * p, barRect.height), new Color(0.35f, 0.85f, 0.45f));
            }

            // Control buttons / Кнопки управления плеером
            float btnY = 12f;

            // Previous / С начала или предыдущий
            Rect prevR = new Rect(254f, btnY + 2f, 26f, 26f);
            if (Widgets.ButtonImage(prevR, TexIcons.Prev))
            {
                MusicController.RestartOrPrevious();
            }
            TooltipHandler.TipRegion(prevR, "Rimify_Widget_PrevTip".Translate());

            // Play/Pause / Воспроизведение или пауза
            Texture2D playPauseTex = MusicController.IsUserPaused ? TexIcons.Play : TexIcons.Pause;
            Rect playR = new Rect(288f, btnY, 30f, 30f);
            if (Widgets.ButtonImage(playR, playPauseTex))
            {
                MusicController.TogglePlayPause();
            }
            TooltipHandler.TipRegion(playR, "Rimify_Widget_PlayPauseTip".Translate());

            // Next / Следующий трек
            Rect nextR = new Rect(326f, btnY + 2f, 26f, 26f);
            if (Widgets.ButtonImage(nextR, TexIcons.Next))
            {
                MusicController.SkipTrack();
            }
            TooltipHandler.TipRegion(nextR, "Rimify_Widget_NextTip".Translate());

            // --- Download status toolbar / Нижняя панель скачивания ---
            if (YouTubeDownloader.IsBusy)
            {
                GUI.color = new Color(0.35f, 0.85f, 0.45f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(6f, BaseHeight + 4f, 18f, 20f), "⤓");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Text.Font = GameFont.Tiny;
                string rawTitle = !string.IsNullOrEmpty(YouTubeDownloader.CurrentTrackTitle)
                    ? YouTubeDownloader.CurrentTrackTitle
                    : YouTubeDownloader.LastStatusMessage;

                int pct = (int)(YouTubeDownloader.DownloadProgress * 100f);
                string text = "Rimify_DownloadingToolbar".Translate(rawTitle, pct);

                Rect dlTextRect = new Rect(28f, BaseHeight + 5f, inRect.width - 34f, 18f);
                DrawMarqueeText(dlTextRect, text, 0.95f);

                Rect pLine = new Rect(8f, inRect.height - 5f, inRect.width - 16f, 2f);
                Widgets.DrawBoxSolid(pLine, new Color(0.2f, 0.22f, 0.25f, 0.6f));
                float curP = Mathf.Clamp01(YouTubeDownloader.DownloadProgress);
                if (curP > 0f)
                {
                    Widgets.DrawBoxSolid(new Rect(pLine.x, pLine.y, pLine.width * curP, pLine.height), new Color(0.35f, 0.85f, 0.45f));
                }
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

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
    }

    // Auto-launch widget on map load / Автозапуск HUD-виджета при загрузке карты
    public class MusicWidgetMapComponent : MapComponent
    {
        public MusicWidgetMapComponent(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (Find.WindowStack.WindowOfType<Widget_MusicPlayer>() == null)
            {
                Find.WindowStack.Add(new Widget_MusicPlayer());
            }
        }
    }

    // WorldComponent for independent music ticking and hotkeys / Компонент мира для независимого тика музыки и горячих клавиш
    public class MusicWorldComponent : RimWorld.Planet.WorldComponent
    {
        public MusicWorldComponent(RimWorld.Planet.World world) : base(world)
        {
        }

        public override void WorldComponentUpdate()
        {
            base.WorldComponentUpdate();

            MusicController.UpdateTick();

            if (RimifyKeyBindingDefOf.Rimify_PlayPause != null && RimifyKeyBindingDefOf.Rimify_PlayPause.JustPressed)
            {
                MusicController.TogglePlayPause();
            }

            if (RimifyKeyBindingDefOf.Rimify_NextTrack != null && RimifyKeyBindingDefOf.Rimify_NextTrack.JustPressed)
            {
                MusicController.SkipTrack();
            }

            if (RimifyKeyBindingDefOf.Rimify_PrevTrack != null && RimifyKeyBindingDefOf.Rimify_PrevTrack.JustPressed)
            {
                MusicController.RestartOrPrevious();
            }
        }
    }
}
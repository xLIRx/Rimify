using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimify
{
    // Seamless injection into vanilla PlaySettings toggle bar / Бесшовное добавление переключателя в ванильную панель PlaySettings
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings_DoPlaySettingsGlobalControls
    {
        private static readonly FieldInfo curYField = typeof(WidgetRow).GetField("curY", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null) return;

            bool prev = AudioLoader.Config != null && AudioLoader.Config.ShowWidget;
            string curTitle = MusicController.GetCurrentTrackTitle();
            string tip = "Rimify_ToggleIcon_Tip".Translate(curTitle);

            // Add toggleable icon into vanilla grid / Добавление кнопки в ванильную сетку переключателей
            row.ToggleableIcon(ref AudioLoader.Config.ShowWidget, TexIcons.MusicToggle, tip, SoundDefOf.Mouseover_ButtonToggle, null);

            // Check RMB over the toggle button to open/close manager / ПКМ по кнопке открывает/закрывает медиатеку
            float curY = curYField != null ? (float)curYField.GetValue(row) : (UI.screenHeight - 35f);
            Rect iconRect = new Rect(row.FinalX, curY, 28f, 26f);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(iconRect))
            {
                Dialog_MusicManager existing = Find.WindowStack.WindowOfType<Dialog_MusicManager>();
                if (existing != null)
                {
                    existing.Close();
                }
                else
                {
                    Find.WindowStack.Add(new Dialog_MusicManager());
                }
                Event.current.Use();
            }

            if (prev != AudioLoader.Config.ShowWidget)
            {
                AudioLoader.Config.Save();
            }
        }
    }
}
using System;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HidePlayerInput
{
    /// <summary>
    /// Прячет из игрового лога то, что написали игроки, не трогая промт для ИИ.
    ///
    /// Как это работает. В игре есть ровно одна функция, которая собирает серую строчку
    /// «Имя: что игрок написал» — GameLogViewObj.MakePlayerInputInfoTxt(actorName, playerInput).
    /// Её результат уходит только в текст на экране (GameLogViewObj.UpdateGraphics,
    /// GameplayManager.LogTryStrWCurrentActor). Промт для ИИ собирается в другом месте и из
    /// другой переменной (UnifiedPromptBuilder.BuildUnifiedUserPrompt из ActionPrep.TryStr()),
    /// поэтому подмена этой строчки на ИИ не влияет никак.
    ///
    /// Мы ищем функцию по имени через рефлексию: если игра обновится и что-то переименуют,
    /// мод просто напишет в лог, что не нашёл цель, и не станет ничего ломать.
    /// </summary>
    [BepInPlugin(Guid, "Скрыть ввод игроков", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "shantargon.airoguelite.hideplayerinput";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> CfgMode;
        internal static ConfigEntry<string> CfgFormat;
        internal static ConfigEntry<bool> CfgBlockClickToEdit;
        internal static ConfigEntry<KeyboardShortcut> CfgToggleKey;
        internal static ConfigEntry<bool> CfgDebugLog;

        /// <summary>Временный показ по горячей клавише.</summary>
        internal static bool Revealed;

        private static Type _glvoType;
        private static Type _editableType;

        private void Awake()
        {
            Log = Logger;

            CfgMode = Config.Bind("Основное", "Режим", "все",
                new ConfigDescription(
                    "Что скрывать. «все» — ввод всех игроков, включая твой собственный. "
                    + "«чужие» — только ввод других игроков, свой виден. "
                    + "«выкл» — не скрывать ничего (мод стоит, но ничего не делает)."));

            CfgFormat = Config.Bind("Основное", "Формат", "{имя} совершает действие",
                new ConfigDescription(
                    "Что показывать вместо написанного. «{имя}» подставляется само. "
                    + "Примеры: «{имя} совершает действие», «{имя}: …», «{имя} что-то задумал», "
                    + "просто «…» — тогда имя не показывается вообще."));

            CfgBlockClickToEdit = Config.Bind("Основное", "ЗапрещатьПравкуКликом", true,
                new ConfigDescription(
                    "В игре по скрытой строчке можно щёлкнуть и начать её править — тогда она покажет "
                    + "настоящий текст. Если true, по скрытым строчкам щёлкать нельзя."));

            CfgToggleKey = Config.Bind("Основное", "КлавишаПоказать", new KeyboardShortcut(KeyCode.F9),
                new ConfigDescription(
                    "Клавиша «показать / снова скрыть». Перерисовывает лог целиком, "
                    + "удобно, если надо глянуть, что вообще ушло ИИ."));

            CfgDebugLog = Config.Bind("Отладка", "ПисатьВЛог", false,
                new ConfigDescription(
                    "Писать в BepInEx\\LogOutput.log, что именно было спрятано. "
                    + "Нужно только чтобы проверить, что мод работает: текст игроков попадёт в файл лога."));

            var harmony = new Harmony(Guid);

            _glvoType = AccessTools.TypeByName("GameLogViewObj");
            if (_glvoType == null)
            {
                Log.LogError("Не нашёл класс GameLogViewObj — мод ничего не делает. Видимо, игра обновилась.");
                return;
            }

            var target = AccessTools.Method(_glvoType, "MakePlayerInputInfoTxt",
                new[] { typeof(string), typeof(string) });
            if (target == null)
            {
                Log.LogError("Не нашёл GameLogViewObj.MakePlayerInputInfoTxt(string, string) — "
                             + "мод ничего не делает. Видимо, игра обновилась.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(Patches), nameof(Patches.MaskPostfix))));
            Log.LogInfo("Патч на MakePlayerInputInfoTxt поставлен.");

            // Необязательная вторая заплатка: запретить правку скрытой строчки кликом.
            _editableType = AccessTools.TypeByName("EditableTextField");
            var wire = AccessTools.Method(_glvoType, "MaybeWireEditableTextField");
            if (_editableType != null && wire != null)
            {
                harmony.Patch(wire, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patches), nameof(Patches.BlockEditPostfix))));
                Log.LogInfo("Патч на правку кликом поставлен.");
            }
            else
            {
                Log.LogWarning("Не нашёл EditableTextField/MaybeWireEditableTextField — "
                               + "скрытую строчку можно будет раскрыть кликом по ней.");
            }
        }

        private void Update()
        {
            try
            {
                if (CfgToggleKey.Value.IsDown())
                {
                    Revealed = !Revealed;
                    Log.LogInfo(Revealed ? "Показываю ввод игроков." : "Снова скрываю ввод игроков.");
                    RedrawLog();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Горячая клавиша: " + e);
            }
        }

        /// <summary>Перерисовать все строчки лога, чтобы показ/скрытие применились к уже написанному.</summary>
        private static void RedrawLog()
        {
            if (_glvoType == null) return;
            try
            {
                var update = AccessTools.Method(_glvoType, "UpdateGraphics", new[] { typeof(bool) });
                if (update == null) return;
                var all = UnityEngine.Object.FindObjectsOfType(_glvoType);
                foreach (var o in all)
                {
                    try { update.Invoke(o, new object[] { true }); }
                    catch { /* отдельная строчка не перерисовалась — не беда */ }
                }
            }
            catch (Exception e)
            {
                Log.LogError("Перерисовка лога: " + e);
            }
        }

        internal static bool HidingActive()
        {
            if (Revealed) return false;
            var mode = (CfgMode.Value ?? "").Trim().ToLowerInvariant();
            return mode != "выкл" && mode != "off";
        }

        internal static bool OnlyOthers()
        {
            var mode = (CfgMode.Value ?? "").Trim().ToLowerInvariant();
            return mode == "чужие" || mode == "others";
        }

        /// <summary>
        /// Имя своего персонажа — SS.I.Manager().GetLocalUiActor().GetPrettyName().
        /// Только через рефлексию и только для режима «чужие». Не получилось — прячем всё.
        /// </summary>
        internal static string LocalActorName()
        {
            try
            {
                var ssType = AccessTools.TypeByName("SS");
                if (ssType == null) return null;

                var ssI = AccessTools.Property(ssType, "I")?.GetValue(null)
                          ?? AccessTools.Field(ssType, "I")?.GetValue(null);
                if (ssI == null) return null;

                var manager = AccessTools.Method(ssI.GetType(), "Manager")?.Invoke(ssI, null);
                if (manager == null) return null;

                var actor = AccessTools.Method(manager.GetType(), "GetLocalUiActor")?.Invoke(manager, null);
                if (actor == null) return null;

                return AccessTools.Method(actor.GetType(), "GetPrettyName")?.Invoke(actor, null) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class Patches
    {
        private const string GreyOpen = "<color=#ccccccff>";
        private const string GreyClose = "</color>";

        /// <summary>
        /// Подменяем готовую строчку лога. actorName — имя игрока (может быть пустым),
        /// playerInput — то, что он написал. В мультиплеере игра кладёт в playerInput сразу
        /// все ходы разом, по строчке на игрока в виде «Имя: действие», а actorName оставляет пустым.
        /// </summary>
        public static void MaskPostfix(string actorName, string playerInput, ref string __result)
        {
            try
            {
                if (!Plugin.HidingActive()) return;

                var masked = Build(actorName, playerInput);
                if (masked == null) return; // нечего скрывать (например, это свой ход в режиме «чужие»)

                if (Plugin.CfgDebugLog.Value)
                    Plugin.Log.LogInfo("Спрятал: [" + actorName + "] " + playerInput);

                __result = GreyOpen + masked + GreyClose;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Подмена строчки: " + e);
            }
        }

        private static string Build(string actorName, string playerInput)
        {
            var onlyOthers = Plugin.OnlyOthers();
            var me = onlyOthers ? Plugin.LocalActorName() : null;

            // Обычный случай: игра сама передала имя, в тексте только действие.
            if (!string.IsNullOrEmpty(actorName))
            {
                if (onlyOthers && Same(actorName, me)) return null;
                return Fill(actorName);
            }

            // Мультиплеерный случай: несколько ходов разом, по строчке на игрока.
            var lines = (playerInput ?? "").Split('\n');
            var sb = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var name = ExtractName(line);

                if (name != null && onlyOthers && Same(name, me))
                    sb.Append(line);        // свой ход оставляем как есть
                else
                    sb.Append(Fill(name));  // имя не разобрали — подставится пустое, текст спрячется целиком

                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Подставить имя в шаблон из настроек.</summary>
        private static string Fill(string name)
        {
            var fmt = Plugin.CfgFormat.Value ?? "";
            return fmt
                .Replace("{имя}", name ?? "")
                .Replace("{name}", name ?? "")
                .Trim();
        }

        /// <summary>
        /// Вытащить имя из строчки «Имя: действие». Если непохоже на имя — возвращаем null,
        /// и тогда строчка прячется целиком: лучше спрятать лишнее, чем показать чужой ход.
        /// </summary>
        private static string ExtractName(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var idx = line.IndexOf(": ", StringComparison.Ordinal);
            if (idx <= 0 || idx > 48) return null;

            var name = line.Substring(0, idx);
            if (name.IndexOf('.') >= 0 || name.IndexOf(',') >= 0 || name.IndexOf('!') >= 0
                || name.IndexOf('?') >= 0 || name.IndexOf(':') >= 0) return null;

            return name;
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                   && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// По строчке лога в игре можно щёлкнуть и начать её править — поле правки показывает
        /// настоящий текст. Для скрытых строчек запрещаем начинать правку.
        /// </summary>
        public static void BlockEditPostfix(object __instance)
        {
            try
            {
                if (!Plugin.CfgBlockClickToEdit.Value || !Plugin.HidingActive()) return;

                var trav = Traverse.Create(__instance);
                var chatTurnUuid = trav.Field("playerInputChatTurnUuid").GetValue<string>();
                if (string.IsNullOrEmpty(chatTurnUuid)) return;

                var unityText = trav.Field("unityText").GetValue() as Component;
                if (unityText == null) return;

                var editable = unityText.GetComponent(AccessTools.TypeByName("EditableTextField"));
                if (editable == null) return;

                var field = AccessTools.Field(editable.GetType(), "canBeginEdit");
                if (field == null) return;

                Func<bool> never = () => false;
                field.SetValue(editable, never);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Запрет правки кликом: " + e);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    [BepInPlugin(Guid, "Скрыть ввод игроков", "1.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "shantargon.airoguelite.hideplayerinput";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<string> CfgMode;
        internal static ConfigEntry<string> CfgFormat;
        internal static ConfigEntry<bool> CfgBlockClickToEdit;
        internal static ConfigEntry<bool> CfgHideTooltip;
        internal static ConfigEntry<bool> CfgPerKind;

        /// <summary>Заглушка для каждого вида действия: вид -> настройка.</summary>
        internal static readonly Dictionary<string, ConfigEntry<string>> CfgKinds =
            new Dictionary<string, ConfigEntry<string>>();

        private static readonly string[][] KindDefaults =
        {
            new[] { ActionKinds.Free, "{имя} совершает действие" },
            new[] { "разговор", "{имя} с кем-то говорит" },
            new[] { "предмет", "{имя} использует предмет" },
            new[] { "умение", "{имя} применяет умение" },
            new[] { "атака", "{имя} атакует" },
            new[] { "ломает", "{имя} что-то ломает" },
            new[] { "крафт", "{имя} что-то создаёт" },
            new[] { "обыск", "{имя} кого-то обыскивает" },
            new[] { "сбор", "{имя} что-то собирает" },
            new[] { "взаимодействие", "{имя} с чем-то взаимодействует" },
            new[] { "предложение", "{имя} что-то предлагает" },
            new[] { "побег", "{имя} пытается убежать" },
            new[] { "переход", "{имя} куда-то направляется" }
        };
        internal static ConfigEntry<KeyboardShortcut> CfgToggleKey;
        internal static ConfigEntry<KeyboardShortcut> CfgPanelKey;
        internal static ConfigEntry<bool> CfgStartHint;
        internal static ConfigEntry<float> CfgStartHintSec;
        internal static ConfigEntry<bool> CfgDebugLog;
        internal static ConfigEntry<bool> CfgDebugShot;

        /// <summary>Временный показ по горячей клавише.</summary>
        internal static bool Revealed;

        private static Type _glvoType;
        private static Type _editableType;
        private SettingsPanel _panel;
        private bool _shotDone;

        private void Awake()
        {
            Log = Logger;

            CfgEnabled = Config.Bind("Основное", "Включено", true,
                new ConfigDescription(
                    "Главный выключатель. Если false, мод стоит на месте, но ничего не прячет. "
                    + "Переключается в игре в окне настроек."));

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

            CfgHideTooltip = Config.Bind("Основное", "ЗакрыватьПодсказку", true,
                new ConfigDescription(
                    "Если навести мышь на персонажа, игра показывает в подсказке, какое действие он "
                    + "уже выбрал (ItemTooltip.cs:716). С этой настройкой там тоже будет заглушка."));

            CfgPerKind = Config.Bind("Заглушки", "ПоТипуДействия", true,
                new ConfigDescription(
                    "Разные заглушки под разные действия: «говорит», «использует предмет» и так далее. "
                    + "Если false, на всё будет одна заглушка из настройки «Формат»."));

            foreach (var row in KindDefaults)
            {
                var kind = row[0];
                var key = char.ToUpperInvariant(kind[0]) + kind.Substring(1);
                CfgKinds[kind] = Config.Bind("Заглушки", key, row[1],
                    new ConfigDescription("Заглушка для действий вида «" + kind + "». «{имя}» подставляется само."));
            }

            CfgToggleKey = Config.Bind("Основное", "КлавишаПоказать", new KeyboardShortcut(KeyCode.F9),
                new ConfigDescription(
                    "Клавиша «показать / снова скрыть». Перерисовывает лог целиком, "
                    + "удобно, если надо глянуть, что вообще ушло ИИ."));

            CfgPanelKey = Config.Bind("Основное", "КлавишаНастроек", new KeyboardShortcut(KeyCode.F10),
                new ConfigDescription(
                    "Клавиша, которая открывает окно настроек мода прямо в игре."));

            CfgStartHint = Config.Bind("Основное", "ПодсказкаПриЗапуске", true,
                new ConfigDescription(
                    "Показывать в углу экрана после запуска игры, какой клавишей открыть "
                    + "настройки мода. Если мешает — выключите."));

            CfgStartHintSec = Config.Bind("Основное", "ПодсказкаСекунд", 12f,
                new ConfigDescription(
                    "Сколько секунд держать эту подсказку на экране.",
                    new AcceptableValueRange<float>(0f, 120f)));

            CfgDebugLog = Config.Bind("Отладка", "ПисатьВЛог", false,
                new ConfigDescription(
                    "Писать в BepInEx\\LogOutput.log, что именно было спрятано. "
                    + "Нужно только чтобы проверить, что мод работает: текст игроков попадёт в файл лога."));

            CfgDebugShot = Config.Bind("Отладка", "СнимокЭкранаПриЗапуске", false,
                new ConfigDescription(
                    "Отладка: через несколько секунд после запуска само открыть окно настроек и "
                    + "сохранить снимок экрана в папку игры. Нужно только разработчику мода."));

            // Окно и горячие клавиши живут на своём объекте, а не на объекте BepInEx.
            // Объект, созданный до загрузки первой сцены, Unity может не сохранить даже
            // с DontDestroyOnLoad, поэтому проверяем его при каждой загрузке сцены.
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureHost();

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

            // Третья заплатка: подсказка при наведении на персонажа тоже выдаёт чужой ход.
            var coordType = AccessTools.TypeByName("MpTurnCoordinator");
            var queued = coordType == null ? null : AccessTools.Method(coordType, "QueuedTryStrForChar");
            if (queued != null)
            {
                harmony.Patch(queued, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patches), nameof(Patches.TooltipPostfix))));
                Log.LogInfo("Патч на подсказку при наведении поставлен.");
            }
            else
            {
                Log.LogWarning("Не нашёл MpTurnCoordinator.QueuedTryStrForChar — подсказка при "
                               + "наведении на персонажа может показать, что он задумал.");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureHost();
        }

        /// <summary>
        /// Создаёт объект с окном настроек, если его ещё (или уже) нет.
        /// </summary>
        private void EnsureHost()
        {
            if (_panel != null) return;

            var host = new GameObject("HidePlayerInput_UI");
            DontDestroyOnLoad(host);
            _panel = host.AddComponent<SettingsPanel>();
            Log.LogInfo("Окно настроек готово, клавиша " + CfgPanelKey.Value + ".");

            if (CfgDebugShot.Value && !_shotDone)
            {
                // Unity не рисует кадры, когда окно не в фокусе, и снимок так не сделать.
                Application.runInBackground = true;
                _panel.StartCoroutine(DebugShot());
                Log.LogInfo("Отладка: корутина снимка запущена.");
            }
        }

        /// <summary>
        /// Своя проверка нажатия вместо KeyboardShortcut.IsDown().
        /// Штатная, во-первых, читает клавиатуру только старым способом (а игра собрана
        /// с новой системой ввода), во-вторых, требует, чтобы НИ ОДНА другая клавиша не
        /// была нажата, перебирая весь список KeyCode вместе с кнопками мыши и геймпада:
        /// с подключённым контроллером она не срабатывает никогда. Здесь смотрим только
        /// саму клавишу и её модификаторы, через <see cref="Keys"/>.
        /// </summary>
        internal static bool Pressed(ConfigEntry<KeyboardShortcut> cfg)
        {
            if (cfg == null) return false;
            var sc = cfg.Value;
            if (sc.MainKey == KeyCode.None) return false;
            if (!Keys.Down(sc.MainKey)) return false;
            foreach (var mod in sc.Modifiers)
                if (!Keys.Held(mod)) return false;
            return true;
        }


        /// <summary>Перерисовать все строчки лога, чтобы показ/скрытие применились к уже написанному.</summary>
        internal static void RedrawLog()
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

        /// <summary>Отладка: открыть окно и снять экран, чтобы проверить вид окна без участия игрока.</summary>
        private System.Collections.IEnumerator DebugShot()
        {
            // Ждём по настоящим часам: в меню игра может держать время остановленным,
            // и обычное WaitForSeconds тогда не закончится никогда.
            yield return WaitRealtime(14f);
            if (_panel != null && !_panel.Open) _panel.Toggle();
            yield return WaitRealtime(1.5f);
            _shotDone = true;
            var path = System.IO.Path.Combine(Paths.GameRootPath, "hideplayerinput-panel.png");
            ScreenCapture.CaptureScreenshot(path);
            Log.LogInfo("Снимок окна настроек: " + path);
            ActionKinds.Dump();
        }

        private static System.Collections.IEnumerator WaitRealtime(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        internal static bool HidingActive()
        {
            if (Revealed) return false;
            if (!CfgEnabled.Value) return false;
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
        /// <summary>Какую заглушку брать для этого вида действия.</summary>
        internal static string FormatFor(string kind)
        {
            ConfigEntry<string> e;
            if (CfgPerKind.Value && kind != null && CfgKinds.TryGetValue(kind, out e)) return e.Value;
            return CfgFormat.Value;
        }

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
                return Fill(actorName, ActionKinds.Classify(playerInput));
            }

            // Мультиплеерный случай: несколько ходов разом, по строчке на игрока.
            var lines = (playerInput ?? "").Split('\n');
            var sb = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var name = ExtractName(line);

                if (name != null && onlyOthers && Same(name, me))
                {
                    sb.Append(line);        // свой ход оставляем как есть
                }
                else
                {
                    // имя не разобрали — подставится пустое, текст спрячется целиком
                    sb.Append(Fill(name, ActionKinds.Classify(ActionPart(line, name))));
                }

                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Из строчки «Имя: действие» взять только действие.</summary>
        private static string ActionPart(string line, string name)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(name)) return line;
            var idx = line.IndexOf(": ", StringComparison.Ordinal);
            return idx > 0 ? line.Substring(idx + 2) : line;
        }

        /// <summary>Подставить имя в шаблон из настроек.</summary>
        private static string Fill(string name, string kind)
        {
            var fmt = Plugin.FormatFor(kind) ?? "";
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
        /// Подсказка при наведении на персонажа показывает, какое действие он уже выбрал.
        /// Отдаём туда ту же заглушку. Имя не подставляем: подсказка и так про этого персонажа.
        /// </summary>
        public static void TooltipPostfix(object[] __args, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (!Plugin.CfgHideTooltip.Value || !Plugin.HidingActive()) return;

                if (Plugin.OnlyOthers())
                {
                    var ch = (__args != null && __args.Length > 0) ? __args[0] : null;
                    var name = ch == null
                        ? null
                        : AccessTools.Method(ch.GetType(), "GetPrettyName")?.Invoke(ch, null) as string;
                    if (Same(name, Plugin.LocalActorName())) return;
                }

                __result = Fill(null, ActionKinds.Classify(__result));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Подсказка при наведении: " + e);
            }
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

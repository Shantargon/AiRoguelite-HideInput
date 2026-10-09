using System;
using BepInEx.Configuration;
using UnityEngine;

namespace HidePlayerInput
{
    /// <summary>
    /// Окно настроек прямо в игре. Открывается горячей клавишей (по умолчанию F10).
    /// Всё, что меняется в окне, сразу пишется в файл настроек и сразу применяется к логу.
    /// </summary>
    internal class SettingsPanel : MonoBehaviour
    {
        private const int WindowId = 0x48504931;

        internal bool Open;

        private Rect _rect;
        private bool _placed;
        private GUISkin _skin;
        private Vector2 _scroll;
        private bool _prevCursorVisible;
        private CursorLockMode _prevCursorLock;

        private static readonly string[] ModeLabels =
        {
            "Ходы всех игроков, и мои тоже",
            "Только чужие ходы, свой вижу"
        };

        private static readonly string[] ModeValues = { "все", "чужие" };

        private static readonly string[] PresetLabels =
        {
            "{имя} совершает действие",
            "{имя}: …",
            "{имя} что-то задумал",
            "без имени, просто …"
        };

        private static readonly string[] PresetValues =
        {
            "{имя} совершает действие",
            "{имя}: …",
            "{имя} что-то задумал",
            "…"
        };

        internal void Toggle()
        {
            Open = !Open;
            if (Open)
            {
                _prevCursorVisible = Cursor.visible;
                _prevCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLock;
            }
        }

        private void OnGUI()
        {
            if (!Open) return;

            if (!_placed)
            {
                var w = Mathf.Clamp(Screen.width * 0.42f, 460f, 760f);
                _rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.16f, w, 10f);
                _placed = true;
            }

            var prev = GUI.skin;
            GUI.skin = _skin ?? (_skin = BuildSkin());
            _rect = GUILayout.Window(WindowId, _rect, Draw, "Скрыть ввод игроков");
            GUI.skin = prev;
            GUI.enabled = true;
        }

        private GUISkin BuildSkin()
        {
            var skin = Instantiate(GUI.skin);
            var size = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 58f), 13, 26);
            skin.label.fontSize = size;
            skin.button.fontSize = size;
            skin.toggle.fontSize = size;
            skin.textField.fontSize = size;
            skin.box.fontSize = size;
            skin.window.fontSize = size + 2;
            skin.label.wordWrap = true;
            skin.button.wordWrap = true;
            skin.window.padding = new RectOffset(14, 14, size + 16, 14);
            return skin;
        }

        private void Draw(int id)
        {
            GUILayout.Space(4);

            // Главный выключатель.
            var on = GUILayout.Toggle(Plugin.CfgEnabled.Value, "  Мод включён — прятать написанное");
            if (on != Plugin.CfgEnabled.Value)
            {
                Plugin.CfgEnabled.Value = on;
                Plugin.RedrawLog();
            }

            if (!on)
                GUILayout.Label("Сейчас всё видно как в обычной игре.");

            GUILayout.Space(10);

            // Пока мод выключен, остальное показываем серым и не даём трогать.
            GUI.enabled = on;

            GUILayout.Label("Что скрывать");
            var current = Array.IndexOf(ModeValues, (Plugin.CfgMode.Value ?? "").Trim().ToLowerInvariant());
            if (current < 0) current = 0;
            var picked = GUILayout.SelectionGrid(current, ModeLabels, 1);
            if (picked != current)
            {
                Plugin.CfgMode.Value = ModeValues[picked];
                Plugin.RedrawLog();
            }

            GUILayout.Space(10);

            GUILayout.Label("Что показывать вместо написанного");

            var perKind = GUILayout.Toggle(Plugin.CfgPerKind.Value,
                "  Разные заглушки под разные действия");
            if (perKind != Plugin.CfgPerKind.Value)
            {
                Plugin.CfgPerKind.Value = perKind;
                Plugin.RedrawLog();
            }

            if (perKind)
            {
                // Игра сама подскажет вид действия, а тут для каждого вида своя заглушка.
                _scroll = GUILayout.BeginScrollView(_scroll,
                    GUILayout.Height(Mathf.Min(Screen.height * 0.34f, 320f)));
                foreach (var kind in ActionKinds.AllKinds)
                {
                    ConfigEntry<string> entry;
                    if (!Plugin.CfgKinds.TryGetValue(kind, out entry)) continue;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(kind, GUILayout.Width(Mathf.Min(_rect.width * 0.33f, 190f)));
                    var kv = GUILayout.TextField(entry.Value ?? "");
                    GUILayout.EndHorizontal();
                    if (kv != entry.Value)
                    {
                        entry.Value = kv;
                        Plugin.RedrawLog();
                    }
                }
                GUILayout.EndScrollView();
            }
            else
            {
                var fmt = GUILayout.TextField(Plugin.CfgFormat.Value ?? "");
                if (fmt != Plugin.CfgFormat.Value)
                {
                    Plugin.CfgFormat.Value = fmt;
                    Plugin.RedrawLog();
                }

                GUILayout.BeginHorizontal();
                for (var i = 0; i < PresetLabels.Length; i++)
                {
                    if (GUILayout.Button(PresetLabels[i]))
                    {
                        Plugin.CfgFormat.Value = PresetValues[i];
                        GUI.FocusControl(null);
                        Plugin.RedrawLog();
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            var block = GUILayout.Toggle(Plugin.CfgBlockClickToEdit.Value,
                "  Не давать раскрыть строчку щелчком по ней");
            if (block != Plugin.CfgBlockClickToEdit.Value)
                Plugin.CfgBlockClickToEdit.Value = block;

            var tip = GUILayout.Toggle(Plugin.CfgHideTooltip.Value,
                "  Скрывать и в подсказке при наведении на персонажа");
            if (tip != Plugin.CfgHideTooltip.Value)
                Plugin.CfgHideTooltip.Value = tip;

            GUI.enabled = true;

            GUILayout.Space(10);

            GUILayout.Label("Подсмотреть, что ушло ИИ — " + Plugin.CfgToggleKey.Value
                            + (Plugin.Revealed ? "   (сейчас показано)" : ""));
            GUILayout.Label("Это окно — " + Plugin.CfgPanelKey.Value);
            GUILayout.Label(@"Клавиши меняются в файле BepInEx\config\" + Plugin.Guid + ".cfg");

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("  Закрыть  ")) Toggle();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 100000, 10000));
        }
    }
}

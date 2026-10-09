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
        private GUIStyle _check;
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

        private float _pulse;
        private float _hintUntil;

        private void Start()
        {
            if (Plugin.CfgStartHint.Value)
                _hintUntil = Time.realtimeSinceStartup + Mathf.Max(0f, Plugin.CfgStartHintSec.Value);
        }

        /// <summary>
        /// Горячие клавиши. Живут здесь, а не в классе плагина: этот объект создан модом
        /// и никем, кроме мода, не трогается.
        /// </summary>
        private void Update()
        {
            try
            {
                if (Plugin.Pressed(Plugin.CfgPanelKey))
                {
                    Toggle();
                    Plugin.Log.LogInfo(Open ? "Окно настроек открыто." : "Окно настроек закрыто.");
                }

                if (Plugin.Pressed(Plugin.CfgToggleKey))
                {
                    Plugin.Revealed = !Plugin.Revealed;
                    Plugin.Log.LogInfo(Plugin.Revealed
                        ? "Показываю ввод игроков."
                        : "Снова скрываю ввод игроков.");
                    Plugin.RedrawLog();
                }

                // Отладка: раз в 5 секунд отмечаемся в логе, чтобы было видно, что кадры идут.
                if (Plugin.CfgDebugLog.Value && Time.realtimeSinceStartup - _pulse > 5f)
                {
                    _pulse = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo("Мод жив, кадры идут. Окно: " + (Open ? "открыто" : "закрыто")
                                       + ". Клавиатура: " + Keys.Diag());
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Горячая клавиша: " + e);
            }
        }

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
            if (Time.realtimeSinceStartup < _hintUntil) DrawHint();
            if (!Open) return;

            if (!_placed)
            {
                var w = Mathf.Clamp(Screen.width * 0.42f, 520f, 1000f);
                _rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.07f, w, 10f);
                _placed = true;
            }

            // Меньшая глубина — рисуется поверх остальных окон IMGUI.
            GUI.depth = -1000;
            var prev = GUI.skin;
            GUI.skin = _skin ?? (_skin = BuildSkin());
            try
            {
                _rect = GUILayout.Window(WindowId, _rect, Draw, "Скрыть ввод игроков");
                // Чтобы нижние кнопки не уезжали за край экрана.
                if (_rect.height < Screen.height)
                    _rect.y = Mathf.Clamp(_rect.y, 0f, Screen.height - _rect.height);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Окно настроек не нарисовалось: " + e);
                Open = false;
            }
            GUI.skin = prev;
            GUI.enabled = true;
        }

        /// <summary>
        /// Короткая подсказка в углу экрана после запуска: какой клавишей открыть настройки.
        /// Заодно сразу видно, что мод жив.
        /// </summary>
        private void DrawHint()
        {
            var txt = "Мод «Скрыть ввод игроков»: настройки — " + Plugin.CfgPanelKey.Value;
            GUI.skin = _skin ?? (_skin = BuildSkin());
            var size = GUI.skin.label.CalcSize(new GUIContent(txt));
            var pad = Mathf.Max(16f, Screen.height * 0.02f);
            var box = new Rect(pad, pad, size.x + 28f, size.y + 18f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(box.x + 14f, box.y + 9f, size.x, size.y), txt);
        }

        private GUISkin BuildSkin()
        {
            var skin = Instantiate(GUI.skin);
            var size = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 45f), 14, 34);
            skin.label.fontSize = size;
            skin.button.fontSize = size;
            skin.toggle.fontSize = size;
            skin.textField.fontSize = size;
            skin.box.fontSize = size;
            skin.window.fontSize = size + 2;
            skin.label.wordWrap = true;
            skin.button.wordWrap = true;
            skin.window.padding = new RectOffset(18, 18, size + 34, 16);

            // Родной скин Unity полупрозрачный: сквозь окно видно игру и читать тяжело.
            // Поэтому подкладываем свои однотонные фоны.
            var back = Solid(new Color(0.10f, 0.10f, 0.12f, 0.98f));
            skin.window.normal.background = back;
            skin.window.onNormal.background = back;
            skin.window.border = new RectOffset(0, 0, 0, 0);
            skin.window.normal.textColor = Color.white;
            skin.window.onNormal.textColor = Color.white;

            var field = Solid(new Color(0.18f, 0.18f, 0.21f, 1f));
            skin.textField.normal.background = field;
            skin.textField.focused.background = field;
            skin.textField.hover.background = field;
            skin.textField.normal.textColor = new Color(0.92f, 0.92f, 0.95f);
            skin.textField.focused.textColor = Color.white;
            skin.textField.padding = new RectOffset(8, 8, 4, 4);

            _check = new GUIStyle(skin.button);
            var plain = Solid(Color.white);
            _check.normal.background = plain;
            _check.hover.background = plain;
            _check.active.background = plain;
            _check.focused.background = plain;
            _check.normal.textColor = Color.white;
            _check.hover.textColor = Color.white;
            _check.active.textColor = Color.white;
            _check.border = new RectOffset(0, 0, 0, 0);
            _check.margin = new RectOffset(0, 0, 3, 3);

            skin.label.normal.textColor = new Color(0.88f, 0.88f, 0.92f);
            skin.toggle.normal.textColor = new Color(0.92f, 0.92f, 0.95f);
            skin.toggle.onNormal.textColor = Color.white;
            return skin;
        }

        /// <summary>Однотонная текстура 1x1 — подложка для окна и полей ввода.</summary>
        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>
        /// Переключатель «да/нет». Родную галочку Unity на тёмном фоне почти не видно,
        /// поэтому рисуем цветную кнопку: зелёная — включено, серая — выключено.
        /// </summary>
        private bool Check(bool value, string label)
        {
            GUILayout.BeginHorizontal();
            var back = GUI.backgroundColor;
            GUI.backgroundColor = value
                ? new Color(0.24f, 0.62f, 0.31f)
                : new Color(0.40f, 0.40f, 0.45f);
            var hit = GUILayout.Button(value ? "да" : "нет", _check,
                GUILayout.Width(Mathf.Max(74f, _check.fontSize * 3.2f)));
            GUI.backgroundColor = back;
            GUILayout.Space(8);
            GUILayout.Label(label);
            GUILayout.EndHorizontal();
            return hit ? !value : value;
        }

        private void Draw(int id)
        {
            GUILayout.Space(4);

            // Главный выключатель.
            var on = Check(Plugin.CfgEnabled.Value, "Мод включён — прятать написанное");
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

            var perKind = Check(Plugin.CfgPerKind.Value, "Разные заглушки под разные действия");
            if (perKind != Plugin.CfgPerKind.Value)
            {
                Plugin.CfgPerKind.Value = perKind;
                Plugin.RedrawLog();
            }

            if (perKind)
            {
                // Игра сама подскажет вид действия, а тут для каждого вида своя заглушка.
                _scroll = GUILayout.BeginScrollView(_scroll,
                    GUILayout.Height(Mathf.Min(Screen.height * 0.34f, 430f)));
                foreach (var kind in ActionKinds.AllKinds)
                {
                    ConfigEntry<string> entry;
                    if (!Plugin.CfgKinds.TryGetValue(kind, out entry)) continue;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(kind, GUILayout.Width(Mathf.Clamp(_rect.width * 0.3f, 170f, 320f)));
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

            var block = Check(Plugin.CfgBlockClickToEdit.Value,
                "Не давать раскрыть строчку щелчком по ней");
            if (block != Plugin.CfgBlockClickToEdit.Value)
                Plugin.CfgBlockClickToEdit.Value = block;

            var tip = Check(Plugin.CfgHideTooltip.Value,
                "Скрывать и в подсказке при наведении на персонажа");
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

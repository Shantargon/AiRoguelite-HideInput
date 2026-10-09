using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HidePlayerInput
{
    /// <summary>
    /// Чтение клавиатуры. В Unity две системы ввода, и игра может быть собрана с любой.
    /// AI Roguelite собрана с новой (Input System package) — там старый класс
    /// UnityEngine.Input вообще запрещён и бросает исключение при любом обращении.
    /// Поэтому сначала пробуем старый способ, а если он ругается — переходим на новый,
    /// причём через отражение: прямая ссылка на Unity.InputSystem.dll сломала бы мод
    /// на сборке игры со старым вводом.
    /// </summary>
    internal static class Keys
    {
        private enum Way { Unknown, Old, New, None }

        private static Way _way = Way.Unknown;

        private static PropertyInfo _current;   // Keyboard.current
        private static PropertyInfo _item;      // Keyboard[Key]
        private static PropertyInfo _wasDown;   // KeyControl.wasPressedThisFrame
        private static PropertyInfo _isDown;    // KeyControl.isPressed
        private static Type _keyEnum;           // UnityEngine.InputSystem.Key

        private static readonly Dictionary<KeyCode, object> Cache = new Dictionary<KeyCode, object>();

        /// <summary>Имена, которые в новой системе называются иначе, чем в KeyCode.</summary>
        private static readonly Dictionary<KeyCode, string> Renamed = new Dictionary<KeyCode, string>
        {
            { KeyCode.LeftControl, "LeftCtrl" },
            { KeyCode.RightControl, "RightCtrl" },
            { KeyCode.Return, "Enter" },
            { KeyCode.BackQuote, "Backquote" },
            { KeyCode.Print, "PrintScreen" },
            { KeyCode.LeftWindows, "LeftMeta" },
            { KeyCode.RightWindows, "RightMeta" },
            { KeyCode.LeftApple, "LeftMeta" },
            { KeyCode.RightApple, "RightMeta" },
            { KeyCode.KeypadEnter, "NumpadEnter" },
            { KeyCode.KeypadPlus, "NumpadPlus" },
            { KeyCode.KeypadMinus, "NumpadMinus" },
            { KeyCode.KeypadMultiply, "NumpadMultiply" },
            { KeyCode.KeypadDivide, "NumpadDivide" },
            { KeyCode.KeypadPeriod, "NumpadPeriod" },
            { KeyCode.KeypadEquals, "NumpadEquals" }
        };

        /// <summary>Строка для лога: как читаем клавиатуру и что при этом видим.</summary>
        internal static string Diag()
        {
            if (_way == Way.Unknown) Detect();
            if (_way != Way.New) return "способ=" + _way;
            try
            {
                var kb = _current.GetValue(null, null);
                if (kb == null) return "способ=New, Keyboard.current=null";
                var any = kb.GetType().GetProperty("anyKey");
                var ctl = any == null ? null : any.GetValue(kb, null);
                var pressed = ctl == null ? "?" :
                    ((bool)_isDown.GetValue(ctl, null)).ToString();
                var dev = kb.GetType().GetProperty("enabled");
                var en = dev == null ? "?" : dev.GetValue(kb, null).ToString();
                return "способ=New, устройство=" + kb + ", включено=" + en
                       + ", что-то нажато=" + pressed;
            }
            catch (Exception e)
            {
                return "способ=New, ошибка опроса: " + e.Message;
            }
        }

        /// <summary>Клавиша нажата именно в этом кадре.</summary>
        internal static bool Down(KeyCode code)
        {
            return Read(code, true);
        }

        /// <summary>Клавиша удерживается.</summary>
        internal static bool Held(KeyCode code)
        {
            return Read(code, false);
        }

        private static bool Read(KeyCode code, bool thisFrame)
        {
            if (_way == Way.Unknown) Detect();
            if (_way == Way.None || code == KeyCode.None) return false;

            if (_way == Way.Old)
                return thisFrame ? Input.GetKeyDown(code) : Input.GetKey(code);

            try
            {
                var keyboard = _current.GetValue(null, null);
                if (keyboard == null) return false;

                var key = KeyOf(code);
                if (key == null) return false;

                var control = _item.GetValue(keyboard, new[] { key });
                if (control == null) return false;

                var prop = thisFrame ? _wasDown : _isDown;
                return (bool)prop.GetValue(control, null);
            }
            catch (Exception e)
            {
                _way = Way.None;
                Plugin.Log.LogError("Не получилось прочитать клавиатуру, горячие клавиши "
                                    + "работать не будут: " + e.Message);
                return false;
            }
        }

        /// <summary>KeyCode -> значение перечисления UnityEngine.InputSystem.Key.</summary>
        private static object KeyOf(KeyCode code)
        {
            object cached;
            if (Cache.TryGetValue(code, out cached)) return cached;

            string name;
            if (!Renamed.TryGetValue(code, out name))
            {
                name = code.ToString();
                // Alpha1 -> Digit1, Keypad1 -> Numpad1
                if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
                else if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "Numpad" + name.Substring(6);
            }

            object value = null;
            try
            {
                value = Enum.Parse(_keyEnum, name, false);
            }
            catch
            {
                Plugin.Log.LogWarning("Клавиша " + code + " в новой системе ввода не нашлась. "
                                      + "Выберите другую в файле настроек.");
            }

            Cache[code] = value;
            return value;
        }

        private static void Detect()
        {
            // Старый способ: если он отключён, уже само обращение бросит исключение.
            try
            {
                Input.GetKey(KeyCode.None);
                _way = Way.Old;
                Plugin.Log.LogInfo("Ввод: старая система (UnityEngine.Input).");
                return;
            }
            catch (Exception)
            {
                // Значит игра собрана с новой системой ввода — идём туда.
            }

            try
            {
                var kb = HarmonyLib.AccessTools.TypeByName("UnityEngine.InputSystem.Keyboard");
                _keyEnum = HarmonyLib.AccessTools.TypeByName("UnityEngine.InputSystem.Key");
                if (kb == null || _keyEnum == null) throw new Exception("нет классов Keyboard/Key");

                _current = kb.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                if (_current == null) throw new Exception("нет Keyboard.current");

                var keyControl = HarmonyLib.AccessTools.TypeByName("UnityEngine.InputSystem.Controls.KeyControl");
                _item = kb.GetProperty("Item", new[] { _keyEnum });
                if (_item == null) throw new Exception("нет Keyboard[Key]");

                var ctrlType = keyControl ?? _item.PropertyType;
                _wasDown = ctrlType.GetProperty("wasPressedThisFrame");
                _isDown = ctrlType.GetProperty("isPressed");
                if (_wasDown == null || _isDown == null) throw new Exception("нет wasPressedThisFrame/isPressed");

                _way = Way.New;
                Plugin.Log.LogInfo("Ввод: новая система (Input System package).");
            }
            catch (Exception e)
            {
                _way = Way.None;
                Plugin.Log.LogError("Не нашёл, как читать клавиатуру — горячие клавиши "
                                    + "работать не будут: " + e.Message);
            }
        }
    }
}

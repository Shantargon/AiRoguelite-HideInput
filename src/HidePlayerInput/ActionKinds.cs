using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace HidePlayerInput
{
    /// <summary>
    /// Определяет, что за действие совершил игрок, по тексту строчки лога.
    ///
    /// Зачем так. Тип действия (разговор, предмет, умение...) известен только на том компьютере,
    /// где это действие выбрали: по сети уходит уже готовый текст (MpTurnSeat.tryStr,
    /// ChatTurnV2), а сам тип не передаётся. Поэтому вид действия приходится узнавать из текста.
    ///
    /// Как. Все такие строчки игра собирает из своих же шаблонов перевода («use-item-on-char-trystr»
    /// и прочие). LS.GetLocStr (LS.cs:3303) отдаёт шаблон как есть, если не передавать подстановки.
    /// Мы просим у игры шаблон, подставив вместо имён предметов и персонажей метку, и превращаем
    /// его в выражение для поиска. Дальше сверяем строчку лога с этими выражениями.
    ///
    /// Плюс способа: работает на любом языке игры и на любом компьютере. Если ни один шаблон не
    /// совпал — считаем это свободным действием (игрок написал текст сам).
    /// </summary>
    internal static class ActionKinds
    {
        internal const string Free = "свободное";

        /// <summary>Метка вместо подставляемых имён. В настоящем тексте её быть не может.</summary>
        private const string Mark = "\u0001";

        /// <summary>Все названия подстановок, какие игра использует в этих шаблонах.</summary>
        private static readonly string[] ArgNames =
        {
            "ability_name", "blocking_thing_name", "character_name", "entity_name", "item_name",
            "items_str", "location_name", "place_name", "place_name1", "place_name2", "str",
            "thing_name", "num", "name"
        };

        /// <summary>Шаблон игры -> наш вид действия.</summary>
        private static readonly string[][] Table =
        {
            new[] { "say-to-char-trystr", "разговор" },

            new[] { "offer-item-to-char-trystr", "предложение" },

            new[] { "use-item-on-char-trystr", "предмет" },
            new[] { "use-item-on-thing-trystr", "предмет" },
            new[] { "use-item-on-place-trystr", "предмет" },
            new[] { "use-item-on-self-trystr", "предмет" },
            new[] { "use-item-to-clear-trystr", "предмет" },
            new[] { "use-item-wo-targ-trystr", "предмет" },
            new[] { "use-item-and-run-for-life-trystr", "предмет" },
            new[] { "consume-use-item-trystr", "предмет" },

            new[] { "use-abil-on-char-noquotes-trystr", "умение" },
            new[] { "use-abil-on-thing-noquotes-trystr", "умение" },
            new[] { "use-abil-on-place-noquotes-trystr", "умение" },
            new[] { "use-abil-on-self-noquotes-trystr", "умение" },
            new[] { "use-abil-to-clear-noquotes-trystr", "умение" },
            new[] { "use-abil-wo-targ-noquotes-trystr", "умение" },
            new[] { "use-abil-and-run-for-life-noquotes-trystr", "умение" },

            new[] { "atk-w-bare-hands-trystr", "атака" },

            new[] { "bash-thing-with-hands-trystr", "ломает" },
            new[] { "bash-thing-with-item-trystr", "ломает" },

            new[] { "craft-via-story-trystr", "крафт" },
            new[] { "craft-via-story-trystr-w-proposed-item", "крафт" },
            new[] { "craft-combine-trystr", "крафт" },
            new[] { "craft-combine-trystr-named", "крафт" },
            new[] { "craft-process-trystr", "крафт" },
            new[] { "craft-process-trystr-named", "крафт" },

            new[] { "loot-trystr", "обыск" },

            new[] { "forage-via-story-trystr", "сбор" },
            new[] { "harvest-via-story-trystr", "сбор" },
            new[] { "harvest-wo-story-trystr", "сбор" },
            new[] { "harvest-wo-story-trystr-named", "сбор" },

            new[] { "interact-with-thing-trystr", "взаимодействие" },

            new[] { "attempt-run-trystr", "побег" },

            new[] { "grd-movement-trystr", "переход" },
            new[] { "move-through-portkey-trystr", "переход" },
            new[] { "move-through-voodoo-portkey-trystr", "переход" },
            new[] { "return-through-portkey-trystr", "переход" }
        };

        /// <summary>Все виды в том порядке, в каком показываем их в настройках.</summary>
        internal static readonly string[] AllKinds =
        {
            Free, "разговор", "предмет", "умение", "атака", "ломает", "крафт",
            "обыск", "сбор", "взаимодействие", "предложение", "побег", "переход"
        };

        private static List<KeyValuePair<Regex, string>> _rules;
        private static bool _ready;
        private static readonly Dictionary<string, string> Memo = new Dictionary<string, string>();

        /// <summary>Узнать вид действия по строчке лога. Не узнали — значит свободное действие.</summary>
        internal static string Classify(string text)
        {
            if (string.IsNullOrEmpty(text)) return Free;

            try
            {
                string hit;
                if (Memo.TryGetValue(text, out hit)) return hit;

                Build();
                var kind = Free;
                if (_rules != null)
                {
                    foreach (var rule in _rules)
                    {
                        if (rule.Key.IsMatch(text)) { kind = rule.Value; break; }
                    }
                }

                if (Memo.Count > 512) Memo.Clear();
                Memo[text] = kind;
                return kind;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Определение вида действия: " + e);
                return Free;
            }
        }

        /// <summary>Забыть разобранные шаблоны — например, если игрок сменил язык игры.</summary>
        internal static void Forget()
        {
            _ready = false;
            _rules = null;
            Memo.Clear();
        }

        private static void Build()
        {
            if (_ready) return;
            _ready = true;

            object ls;
            var getLocStr = FindGetLocStr(out ls);
            if (getLocStr == null || ls == null)
            {
                Plugin.Log.LogWarning("Не нашёл LS.GetLocStr — виды действий различать не получится, "
                                      + "для всего будет общая заглушка.");
                return;
            }

            var map = new Dictionary<string, object>();
            foreach (var n in ArgNames) map[n] = Mark;

            var rules = new List<KeyValuePair<Regex, string>>();
            foreach (var row in Table)
            {
                string tpl = null;
                try { tpl = getLocStr.Invoke(ls, new object[] { row[0], map, false, null }) as string; }
                catch { /* нет такого шаблона в этой версии игры — пропускаем */ }

                if (string.IsNullOrEmpty(tpl)) continue;

                var rx = ToRegex(tpl);
                if (rx != null) rules.Add(new KeyValuePair<Regex, string>(rx, row[1]));
            }

            // Сначала самые длинные шаблоны: они точнее, короткий может совпасть случайно.
            rules.Sort((a, b) => b.Key.ToString().Length.CompareTo(a.Key.ToString().Length));
            _rules = rules;

            Plugin.Log.LogInfo("Разобрано шаблонов действий: " + rules.Count + " из " + Table.Length);
        }

        /// <summary>Отладка: разобрать шаблоны и написать в лог, что получилось.</summary>
        internal static void Dump()
        {
            Build();
            if (_rules == null) return;
            Plugin.Log.LogInfo("Правил распознавания действий: " + _rules.Count);
            var shown = 0;
            foreach (var rule in _rules)
            {
                if (shown++ >= 8) break;
                Plugin.Log.LogInfo("  " + rule.Value + " <- " + rule.Key);
            }
        }

        /// <summary>Шаблон с метками -> выражение для поиска.</summary>
        private static Regex ToRegex(string tpl)
        {
            var parts = tpl.Split(new[] { Mark }, StringSplitOptions.None);
            var sb = new System.Text.StringBuilder("^");
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(".+?");
                sb.Append(Regex.Escape(parts[i].Trim()));
            }
            // Конец не закрепляем: игра может дописать в хвост указание игрока в скобках.
            try
            {
                return new Regex(sb.ToString(),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }
            catch
            {
                return null;
            }
        }

        private static System.Reflection.MethodInfo FindGetLocStr(out object ls)
        {
            ls = null;
            var lsType = AccessTools.TypeByName("LS");
            if (lsType == null) return null;

            ls = AccessTools.Property(lsType, "I")?.GetValue(null)
                 ?? AccessTools.Field(lsType, "I")?.GetValue(null);
            if (ls == null) return null;

            return AccessTools.Method(lsType, "GetLocStr",
                new[] { typeof(string), typeof(Dictionary<string, object>), typeof(bool), typeof(string) });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SvajpKlaviatura.Engine
{
    public sealed class WordEntry
    {
        public string Word;
        public double BaseCount;
        public int UserCount;
        public bool Blocked;
        /// <summary>Центры клавиш слова без повторов подряд («класс» → к-л-а-с).</summary>
        public Pt[] Keys;
        public double PathLength;
        public char FirstKey, LastKey;
        public override string ToString() { return Word; }
    }

    /// <summary>Словарь одного языка: базовые частоты + слова пользователя.</summary>
    public sealed class Lexicon
    {
        public const int MaxUserCount = 30;
        public readonly string Language;
        public readonly KeyboardLayout Layout;
        private readonly Dictionary<string, WordEntry> byWord = new Dictionary<string, WordEntry>(StringComparer.Ordinal);
        private readonly Dictionary<char, List<WordEntry>> byFirst = new Dictionary<char, List<WordEntry>>();
        private readonly List<WordEntry> all = new List<WordEntry>();
        private double total;
        /// <summary>«Вес» одного использования словом пользователя — частота слова примерно на 1500-м месте.</summary>
        public double UserUnit { get; private set; }
        public bool UserChanged { get; set; }

        public Lexicon(string language, KeyboardLayout layout)
        {
            Language = language;
            Layout = layout;
        }

        public int Count { get { return all.Count; } }
        public IEnumerable<WordEntry> Entries { get { return all; } }

        public static char TemplateChar(char c)
        {
            c = char.ToLowerInvariant(c);
            return c == 'ё' ? 'е' : c;
        }

        /// <summary>Можно ли набрать слово свайпом на этой раскладке (все буквы есть, хотя бы 2 разные клавиши).</summary>
        public bool BuildTemplate(WordEntry e)
        {
            var keys = new List<Pt>();
            char prev = '\0', first = '\0', last = '\0';
            foreach (char raw in e.Word)
            {
                if (raw == '-' || raw == '\'') continue;
                char c = TemplateChar(raw);
                Pt p;
                if (!Layout.TryLetterCenter(c, out p)) { e.Keys = null; return false; }
                if (first == '\0') first = c;
                last = c;
                if (c == prev) continue;
                prev = c;
                keys.Add(p);
            }
            e.FirstKey = first; e.LastKey = last;
            e.Keys = keys.ToArray();
            double len = 0;
            for (int i = 1; i < e.Keys.Length; i++) len += Pt.Dist(e.Keys[i - 1], e.Keys[i]);
            e.PathLength = len;
            return keys.Count > 0;
        }

        public void LoadBase(TextReader r)
        {
            string line;
            var counts = new List<double>();
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.LastIndexOf(' ');
                if (sp <= 0) continue;
                string w = line.Substring(0, sp).Trim().ToLowerInvariant();
                double c;
                if (!double.TryParse(line.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out c) || c <= 0) continue;
                if (byWord.ContainsKey(w)) continue;
                var e = new WordEntry { Word = w, BaseCount = c, Blocked = Profanity.IsBlocked(w, Language) };
                Insert(e);
                counts.Add(c);
            }
            total = 0;
            foreach (var c in counts) total += c;
            counts.Sort((a, b) => b.CompareTo(a));
            UserUnit = counts.Count == 0 ? 100 : counts[Math.Min(counts.Count - 1, 1500)];
            if (total <= 0) total = 1;
        }

        private void Insert(WordEntry e)
        {
            byWord[e.Word] = e;
            all.Add(e);
            if (BuildTemplate(e) && e.Keys.Length >= 1)
            {
                List<WordEntry> list;
                if (!byFirst.TryGetValue(e.FirstKey, out list)) byFirst[e.FirstKey] = list = new List<WordEntry>();
                list.Add(e);
            }
        }

        public WordEntry Find(string word)
        {
            WordEntry e;
            return word != null && byWord.TryGetValue(word.ToLowerInvariant(), out e) ? e : null;
        }

        public List<WordEntry> StartingWith(char firstKey)
        {
            List<WordEntry> list;
            return byFirst.TryGetValue(firstKey, out list) ? list : null;
        }

        /// <summary>Логарифм вероятности слова с учётом обучения.</summary>
        public double LogPrior(WordEntry e)
        {
            double c = e.BaseCount + Math.Min(e.UserCount, MaxUserCount) * UserUnit;
            if (c <= 0) c = 0.5;
            return Math.Log(c / total);
        }

        public static bool IsWordChar(char c) { return char.IsLetter(c) || c == '-' || c == '\''; }

        public static string Normalize(string word)
        {
            if (word == null) return null;
            word = word.Trim().ToLowerInvariant();
            if (word.Length == 0 || word.Length > 40) return null;
            foreach (char c in word) if (!IsWordChar(c)) return null;
            if (!char.IsLetter(word[0])) return null;
            return word;
        }

        /// <summary>Учесть использование слова (delta &gt; 0) или отказ от него (delta &lt; 0). Новые слова добавляются.</summary>
        public WordEntry Learn(string word, int delta)
        {
            string w = Normalize(word);
            if (w == null) return null;
            var e = Find(w);
            if (e == null)
            {
                if (delta <= 0) return null;
                e = new WordEntry { Word = w, BaseCount = 0 };
                Insert(e);
            }
            int before = e.UserCount;
            e.UserCount = Math.Max(e.BaseCount > 0 ? 0 : 1, Math.Min(MaxUserCount, e.UserCount + delta));
            if (e.UserCount != before) UserChanged = true;
            return e;
        }

        /// <summary>Добавить слово вручную: сразу заметный вес.</summary>
        public WordEntry AddUserWord(string word)
        {
            var e = Learn(word, 5);
            if (e != null) e.Blocked = false;
            return e;
        }

        /// <summary>Удалить слово пользователя (базовые слова только «забываются»).</summary>
        public bool RemoveUserWord(string word)
        {
            var e = Find(word);
            if (e == null) return false;
            UserChanged = true;
            if (e.BaseCount > 0) { e.UserCount = 0; return true; }
            byWord.Remove(e.Word);
            all.Remove(e);
            List<WordEntry> list;
            if (e.Keys != null && byFirst.TryGetValue(e.FirstKey, out list)) list.Remove(e);
            return true;
        }

        public List<WordEntry> UserWords()
        {
            var r = new List<WordEntry>();
            foreach (var e in all) if (e.UserCount > 0) r.Add(e);
            r.Sort((a, b) => b.UserCount != a.UserCount ? b.UserCount.CompareTo(a.UserCount) : string.CompareOrdinal(a.Word, b.Word));
            return r;
        }

        /// <summary>Формат файла слов пользователя: «слово<TAB>число» в строке.</summary>
        public void LoadUser(TextReader r)
        {
            string line;
            while ((line = r.ReadLine()) != null)
            {
                line = line.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                int n = 1;
                if (p.Length > 1 && !int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) n = 1;
                string w = Normalize(p[0]);
                if (w == null || n <= 0) continue;
                var e = Find(w);
                if (e == null) { e = new WordEntry { Word = w, BaseCount = 0 }; Insert(e); }
                e.UserCount = Math.Min(MaxUserCount, n);
                e.Blocked = false;
            }
            UserChanged = false;
        }

        public void SaveUser(TextWriter w)
        {
            w.WriteLine("# Свайп-клавиатура: слова пользователя (" + Language + "). Формат: слово<TAB>сколько раз использовано");
            foreach (var e in UserWords()) w.WriteLine(e.Word + "\t" + e.UserCount.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Самые частые слова, начинающиеся с префикса (для подсказок при наборе по буквам).</summary>
        public List<string> Completions(string prefix, int n, bool hideBlocked)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(prefix)) return res;
            string p = prefix.ToLowerInvariant();
            string pe = p.Replace('ё', 'е');
            var best = new List<KeyValuePair<double, WordEntry>>();
            foreach (var e in all)
            {
                if (e.Word.Length < p.Length || (hideBlocked && e.Blocked && e.UserCount == 0)) continue;
                if (!e.Word.StartsWith(p, StringComparison.Ordinal) && !e.Word.Replace('ё', 'е').StartsWith(pe, StringComparison.Ordinal)) continue;
                double s = LogPrior(e);
                if (e.Word.Length == p.Length) s += 1.5; // точное совпадение — первым
                best.Add(new KeyValuePair<double, WordEntry>(s, e));
            }
            best.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int i = 0; i < best.Count && res.Count < n; i++) res.Add(best[i].Value.Word);
            return res;
        }
    }

    /// <summary>Скрытие самых грубых бранных слов из базового словаря (как в Gboard по умолчанию).
    /// Слово, которое пользователь сам добавил или набрал, показывается.</summary>
    public static class Profanity
    {
        private static readonly string[] RuAnywhere = { "хуй", "хуя", "хуе", "хуё", "пизд", "пезд", "бляд", "залуп", "мудак", "мудил", "пидор", "пидар", "гандон", "шлюх", "долбоёб", "долбоеб" };
        private static readonly string[] RuPrefixes = { "", "за", "на", "от", "отъ", "по", "вы", "съ", "про", "до", "разъ", "у", "въ", "пере", "подъ", "при", "изъ", "объ", "недо" };
        private static readonly string[] RuRoots = { "еба", "ебл", "ебу", "ебн", "ебё", "еби", "ёба", "ёбн", "ёбы", "ёб" };
        private static readonly string[] EnAnywhere = { "fuck", "shit", "cunt", "nigg", "whore", "slut", "asshole", "motherf", "bitch", "dickhead", "faggot", "pussy", "wank", "bollock" };
        private static readonly HashSet<string> EnExact = new HashSet<string> { "cock", "cocks", "dick", "dicks", "fag", "fags", "bastard", "bastards" };
        private static readonly HashSet<string> RuExact = new HashSet<string> { "бля", "блять", "хули", "сучара", "сукин" };

        public static bool IsBlocked(string w, string lang)
        {
            if (string.IsNullOrEmpty(w)) return false;
            if (lang == Layouts.En)
            {
                foreach (var r in EnAnywhere) if (w.Contains(r)) return true;
                return EnExact.Contains(w);
            }
            if (RuExact.Contains(w)) return true;
            foreach (var r in RuAnywhere) if (w.Contains(r)) return true;
            foreach (var pre in RuPrefixes)
            {
                if (!w.StartsWith(pre, StringComparison.Ordinal)) continue;
                foreach (var root in RuRoots)
                    if (string.CompareOrdinal(w, pre.Length, root, 0, root.Length) == 0) return true;
            }
            return false;
        }
    }
}

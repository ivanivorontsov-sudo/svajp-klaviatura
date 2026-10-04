using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura
{
    /// <summary>Словари двух языков: встроенные частотные списки + слова пользователя в папке настроек.</summary>
    internal sealed class Dictionaries
    {
        private readonly string dir;
        public Lexicon Ru { get; private set; }
        public Lexicon En { get; private set; }

        public Dictionaries(string configDir) { dir = configDir; }

        public Lexicon For(string lang) { return lang == Layouts.En ? En : Ru; }

        public string UserFile(string lang) { return Path.Combine(dir, "words-" + lang + ".txt"); }

        public void Load()
        {
            var sw = Stopwatch.StartNew();
            Ru = LoadOne(Layouts.Ru);
            En = LoadOne(Layouts.En);
            Log.Write("Словари загружены: ru " + Ru.Count + ", en " + En.Count + " слов, " + sw.ElapsedMilliseconds + " мс");
        }

        private Lexicon LoadOne(string lang)
        {
            var lex = new Lexicon(lang, Layouts.ForLanguage(lang));
            using (var st = typeof(Dictionaries).Assembly.GetManifestResourceStream("SvajpKlaviatura.Dict." + lang + ".txt"))
            {
                if (st == null) throw new InvalidOperationException("в программе нет словаря «" + lang + "»");
                using (var r = new StreamReader(st, Encoding.UTF8)) lex.LoadBase(r);
            }
            string uf = UserFile(lang);
            try
            {
                if (File.Exists(uf)) using (var r = new StreamReader(uf, Encoding.UTF8)) lex.LoadUser(r);
            }
            catch (Exception ex)
            {
                Log.Error("Слова пользователя " + lang, ex);
                try { File.Copy(uf, uf + ".broken", true); } catch { }
            }
            return lex;
        }

        public void SaveIfChanged()
        {
            foreach (var lex in new[] { Ru, En })
            {
                if (lex == null || !lex.UserChanged) continue;
                try
                {
                    var sw = new StringWriter();
                    lex.SaveUser(sw);
                    AtomicFile.WriteAllText(UserFile(lex.Language), sw.ToString());
                    lex.UserChanged = false;
                }
                catch (Exception ex) { Log.Error("Сохранение слов " + lex.Language, ex); }
            }
        }
    }
}

using System;
using System.IO;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura.EngineTests
{
    internal static class Dicts
    {
        private static readonly Lazy<Lexicon> ru = new Lazy<Lexicon>(() => Load(Layouts.Ru));
        private static readonly Lazy<Lexicon> en = new Lazy<Lexicon>(() => Load(Layouts.En));
        public static Lexicon Ru { get { return ru.Value; } }
        public static Lexicon En { get { return en.Value; } }

        /// <summary>Свежий словарь (для тестов обучения, чтобы не портить общий).</summary>
        public static Lexicon Load(string lang)
        {
            var lex = new Lexicon(lang, Layouts.ForLanguage(lang));
            using (var r = new StreamReader(Path.Combine(AppContext.BaseDirectory, "Dict", lang + ".txt"))) lex.LoadBase(r);
            return lex;
        }
    }
}

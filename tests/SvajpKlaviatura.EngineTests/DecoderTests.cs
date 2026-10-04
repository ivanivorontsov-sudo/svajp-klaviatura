using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SvajpKlaviatura.Engine;
using SvajpKlaviatura.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SvajpKlaviatura.EngineTests
{
    public class DecoderTests
    {
        private readonly ITestOutputHelper output;
        public DecoderTests(ITestOutputHelper o) { output = o; }

        public static readonly string[] RuWords =
        {
            "привет", "спасибо", "пожалуйста", "хорошо", "сегодня", "завтра", "вчера", "можно", "нужно", "сейчас",
            "потом", "работа", "дома", "время", "человек", "почему", "когда", "где", "здравствуйте", "добрый",
            "вечер", "утро", "ночь", "день", "неделя", "месяц", "деньги", "машина", "телефон", "компьютер",
            "письмо", "сообщение", "встреча", "давай", "конечно", "отлично", "понятно", "вопрос", "ответ", "проблема",
            "клавиатура", "мышь", "экран", "программа", "москва", "россия", "город", "улица", "магазин", "купить",
            "сделать", "сказать", "думаю", "знаю", "хочу", "могу", "буду", "люблю", "мама", "папа",
            "друг", "друзья", "семья", "ребёнок", "школа", "учитель", "книга", "фильм", "музыка", "игра",
            "погода", "солнце", "дождь", "снег", "зима", "лето", "весна", "осень", "вода", "еда",
            "чай", "кофе", "хлеб", "молоко", "быстро", "медленно", "большой", "маленький", "новый", "старый",
            "только", "очень", "ещё", "тоже", "уже", "всегда", "никогда", "много", "мало", "который",
        };

        public static readonly string[] EnWords =
        {
            "hello", "thanks", "please", "good", "today", "tomorrow", "yesterday", "work", "home", "time",
            "people", "because", "when", "where", "what", "morning", "evening", "night", "week", "month",
            "money", "phone", "computer", "message", "meeting", "sure", "great", "question", "answer", "problem",
            "keyboard", "mouse", "screen", "program", "london", "city", "street", "shop", "buy", "make",
            "think", "know", "want", "love", "mother", "father", "friend", "family", "school", "book",
            "movie", "music", "game", "weather", "rain", "snow", "winter", "summer", "water", "food",
            "coffee", "bread", "quick", "slow", "big", "small", "new", "old", "only", "very",
            "also", "already", "always", "never", "much", "which", "would", "could", "should", "there",
            "their", "about", "after", "before", "again", "world", "life", "house", "car", "help",
            "don't", "it's", "i'm", "can't", "really", "nothing", "something", "everything", "maybe", "right",
        };

        private Lexicon Lex(string lang) { return lang == Layouts.En ? Dicts.En : Dicts.Ru; }

        private static string[] Words(string lang) { return lang == Layouts.En ? EnWords : RuWords; }

        private static void EnsureInDictionary(Lexicon lex, IEnumerable<string> words)
        {
            // редкие для субтитров слова («клавиатура») добавляем как пользовательские — так их добавит и сам пользователь
            foreach (var w in words) if (lex.Find(w) == null) lex.AddUserWord(w);
        }

        private (double top1, double top3, double ms, List<string> fails) Run(Lexicon lex, string[] words, double sigma, int samples, int seed)
        {
            var rng = new Random(seed);
            int n = 0, t1 = 0, t3 = 0; double ms = 0;
            var fails = new List<string>();
            foreach (var w in words)
                for (int s = 0; s < samples; s++)
                {
                    var path = SwipeSynth.Make(lex.Layout, w, rng, sigma);
                    Assert.NotNull(path);
                    var r = GestureDecoder.Decode(lex, path, 3, true);
                    n++; ms += r.Milliseconds;
                    if (Same(r.Best, w)) t1++; else fails.Add(w + "→" + (r.Best ?? "∅"));
                    if (r.Candidates.Any(c => Same(c.Word, w))) t3++;
                }
            return (100.0 * t1 / n, 100.0 * t3 / n, ms / n, fails);
        }

        /// <summary>«ещё»/«еще», «ребёнок»/«ребенок» — одинаковый путь по клавишам; различает только частота, второе — в подсказках.</summary>
        private static bool Same(string a, string b) { return a != null && a.Replace('ё', 'е') == b.Replace('ё', 'е'); }

        [Theory]
        [InlineData("ru")]
        [InlineData("en")]
        public void IdealPath_CorrectWordInTop3(string lang)
        {
            var lex = Dicts.Load(lang);
            EnsureInDictionary(lex, Words(lang));
            var r = Run(lex, Words(lang), 0.0, 1, 1);
            output.WriteLine($"{lang} σ=0: top1 {r.top1:0.0}% top3 {r.top3:0.0}%; ошибки top1: {string.Join(", ", r.fails)}");
            Assert.Equal(100.0, r.top3);
            Assert.True(r.top1 >= 93, "top1 " + r.top1);
        }

        [Theory]
        [InlineData("ru", 0.15, 92, 99)]
        [InlineData("ru", 0.25, 85, 96)]
        [InlineData("en", 0.15, 90, 98)]
        [InlineData("en", 0.25, 82, 95)]
        public void NoisySwipes_Accuracy(string lang, double sigma, double minTop1, double minTop3)
        {
            var lex = Dicts.Load(lang);
            EnsureInDictionary(lex, Words(lang));
            var r = Run(lex, Words(lang), sigma, 5, 42);
            output.WriteLine($"{lang} σ={sigma}: {Words(lang).Length} слов × 5 свайпов: top1 {r.top1:0.0}% top3 {r.top3:0.0}%, {r.ms:0.0} мс на свайп");
            output.WriteLine("ошибки top1: " + string.Join(", ", r.fails.Take(40)));
            Assert.True(r.top1 >= minTop1, $"top1 {r.top1:0.0}% < {minTop1}%");
            Assert.True(r.top3 >= minTop3, $"top3 {r.top3:0.0}% < {minTop3}%");
        }

        [Fact]
        public void AccuracyReport_WrittenToFile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Язык | Набор | Разброс σ | Свайпов | Top-1 | Top-3 | мс/свайп |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var lang in new[] { "ru", "en" })
            {
                var lex = Dicts.Load(lang);
                EnsureInDictionary(lex, Words(lang));
                foreach (var sigma in new[] { 0.15, 0.25, 0.35 })
                {
                    var r = Run(lex, Words(lang), sigma, 5, 7);
                    sb.AppendLine($"| {(lang == "ru" ? "русский" : "английский")} | 100 частых слов | {sigma:0.00} | {Words(lang).Length * 5} | {r.top1:0.0} % | {r.top3:0.0} % | {r.ms:0.0} |");
                }
            }
            string dir = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "accuracy.md"), sb.ToString());
            output.WriteLine(sb.ToString());
        }

        [Fact]
        public void Learning_RareWordWinsAfterUse()
        {
            var lex = Dicts.Load(Layouts.Ru);
            // «пират» и «привет» похожи; после нескольких выборов редкого слова оно выходит первым
            var path = SwipeSynth.Make(lex.Layout, "мираж", new Random(3), 0.12);
            var before = GestureDecoder.Decode(lex, path, 5, true);
            for (int i = 0; i < 6; i++) lex.Learn("мираж", 1);
            var after = GestureDecoder.Decode(lex, path, 5, true);
            output.WriteLine("до: " + string.Join(",", before.Candidates.Select(c => c.Word)) + "; после: " + string.Join(",", after.Candidates.Select(c => c.Word)));
            Assert.Equal("мираж", after.Best);
        }

        [Fact]
        public void UserWord_NotInDictionary_BecomesSwipeable()
        {
            var lex = Dicts.Load(Layouts.Ru);
            Assert.Null(lex.Find("свайпнуть"));
            var path = SwipeSynth.Make(lex.Layout, "свайпнуть", new Random(5), 0.15);
            Assert.NotEqual("свайпнуть", GestureDecoder.Decode(lex, path, 3, true).Best);
            lex.AddUserWord("свайпнуть");
            var r = GestureDecoder.Decode(lex, path, 3, true);
            Assert.Contains(r.Candidates, c => c.Word == "свайпнуть");
            Assert.True(lex.RemoveUserWord("свайпнуть"));
            Assert.DoesNotContain(GestureDecoder.Decode(lex, path, 3, true).Candidates, c => c.Word == "свайпнуть");
        }

        [Fact]
        public void UserWords_SaveAndLoad()
        {
            var a = Dicts.Load(Layouts.En);
            a.AddUserWord("grokbot"); a.Learn("hello", 3);
            var sw = new StringWriter(); a.SaveUser(sw);
            var b = Dicts.Load(Layouts.En);
            b.LoadUser(new StringReader(sw.ToString()));
            Assert.Equal(5, b.Find("grokbot").UserCount);
            Assert.Equal(3, b.Find("hello").UserCount);
            b.LoadUser(new StringReader("мусор\tабв\n\t\n#x\nok-word\t2\n"));
            Assert.Equal(2, b.Find("ok-word").UserCount);
        }

        [Fact]
        public void Profanity_HiddenByDefault_NormalWordsKept()
        {
            var lex = Dicts.Ru;
            foreach (var w in new[] { "себе", "оскорблять", "расслабляться", "хулиган", "команда", "гребень", "убежать", "сука" })
                Assert.False(Profanity.IsBlocked(w, "ru"), w);
            foreach (var w in new[] { "хуй", "пиздец", "ебать", "блядь", "заебись" })
                Assert.True(Profanity.IsBlocked(w, "ru"), w);
            Assert.True(Profanity.IsBlocked("fuck", "en"));
            Assert.False(Profanity.IsBlocked("cocktail", "en"));
            Assert.False(Profanity.IsBlocked("shitake", "ru"));
            var path = SwipeSynth.Make(lex.Layout, "блядь", new Random(1), 0.05);
            Assert.DoesNotContain(GestureDecoder.Decode(lex, path, 5, true).Candidates, c => c.Word == "блядь");
            Assert.Contains(GestureDecoder.Decode(lex, path, 5, false).Candidates, c => c.Word == "блядь");
        }

        [Fact]
        public void Dictionaries_Have50kWords()
        {
            Assert.Equal(50000, Dicts.Ru.Count);
            Assert.Equal(50000, Dicts.En.Count);
            Assert.NotNull(Dicts.Ru.Find("привет"));
            Assert.NotNull(Dicts.En.Find("don't"));
            Assert.True(Dicts.Ru.LogPrior(Dicts.Ru.Find("и")) > Dicts.Ru.LogPrior(Dicts.Ru.Find("привет")));
        }

        [Fact]
        public void Layouts_HaveAllLetters()
        {
            var ru = Layouts.Russian();
            Assert.Equal(33, ru.Letters.Count);
            Assert.Equal(26, Layouts.English().Letters.Count);
            Assert.Equal(13.5, ru.Width);
            Assert.Equal(4, ru.Height);
            // клавиши не перекрываются
            foreach (var l in new[] { ru, Layouts.English(), Layouts.Symbols1("АБВ"), Layouts.Symbols2("АБВ") })
                foreach (var a in l.Keys) foreach (var b in l.Keys)
                        if (a != b) Assert.False(a.X < b.X + b.W - 1e-9 && b.X < a.X + a.W - 1e-9 && a.Y < b.Y + b.H - 1e-9 && b.Y < a.Y + a.H - 1e-9, l.Id + ": " + a + " / " + b);
            Assert.Equal("ф", ru.KeyAt(new Pt(0.3, 1.5)).Text);
            Assert.Equal(KeyKind.Space, ru.KeyAt(new Pt(7, 3.5)).Kind);
        }

        [Fact]
        public void Decode_Robust_ToJunkInput()
        {
            var lex = Dicts.Ru;
            Assert.Empty(GestureDecoder.Decode(lex, new List<Pt>(), 3, true).Candidates);
            Assert.Empty(GestureDecoder.Decode(lex, null, 3, true).Candidates);
            Assert.Empty(GestureDecoder.Decode(lex, new List<Pt> { new Pt(1, 1), new Pt(1, 1) }, 3, true).Candidates);
            var r = GestureDecoder.Decode(lex, new List<Pt> { new Pt(double.NaN, 1), new Pt(-50, -50), new Pt(80, 90) }, 3, true);
            Assert.NotNull(r);
        }

        [Fact]
        public void Decode_IsFast()
        {
            var lex = Dicts.Ru;
            var rng = new Random(9);
            var paths = RuWords.Select(w => SwipeSynth.Make(lex.Layout, w, rng, 0.2)).ToList();
            GestureDecoder.Decode(lex, paths[0], 3, true);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var p in paths) GestureDecoder.Decode(lex, p, 3, true);
            double avg = sw.Elapsed.TotalMilliseconds / paths.Count;
            output.WriteLine($"среднее время распознавания: {avg:0.0} мс");
            Assert.True(avg < 60, avg + " мс");
        }
    }
}

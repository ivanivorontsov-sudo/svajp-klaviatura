using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SvajpKlaviatura.Engine;
using SvajpKlaviatura.Testing;

static class Program
{
    static string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    static Lexicon Load(string lang)
    {
        var lex = new Lexicon(lang, Layouts.ForLanguage(lang));
        using (var r = new StreamReader(Path.Combine(Root, "src/SvajpKlaviatura/Dict", lang + ".txt"))) lex.LoadBase(r);
        return lex;
    }

    static List<string> Sample(Lexicon lex, int top, int count, int seed, bool byFreq)
    {
        var words = lex.Entries.Where(e => e.Keys != null && e.Keys.Length >= 2 && !e.Blocked).Take(top).ToList();
        var rng = new Random(seed);
        var res = new List<string>();
        double tot = words.Sum(w => Math.Sqrt(w.BaseCount));
        for (int i = 0; i < count; i++)
        {
            if (!byFreq) { res.Add(words[rng.Next(words.Count)].Word); continue; }
            double x = rng.NextDouble() * tot;
            foreach (var w in words) { x -= Math.Sqrt(w.BaseCount); if (x <= 0) { res.Add(w.Word); break; } }
        }
        return res;
    }

    static (double t1, double t3, double ms) Eval(Lexicon lex, List<string> words, double sigma, int seed, DecoderParams p, List<string> fails = null)
    {
        var rng = new Random(seed);
        var paths = new List<(string w, List<Pt> path)>();
        foreach (var w in words) { var path = SwipeSynth.Make(lex.Layout, w, rng, sigma); if (path != null) paths.Add((w, path)); }
        var results = new DecodeResult[paths.Count];
        System.Threading.Tasks.Parallel.For(0, paths.Count, i => results[i] = GestureDecoder.Decode(lex, paths[i].path, 3, true, p));
        int ok1 = 0, ok3 = 0; double ms = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            var r = results[i]; string w = paths[i].w; ms += r.Milliseconds;
            if (Same(r.Best, w)) ok1++;
            else if (fails != null) fails.Add(w + "→" + (r.Best ?? "∅") + " [" + string.Join(",", r.Candidates.Select(c => c.Word)) + "]");
            if (r.Candidates.Any(c => Same(c.Word, w))) ok3++;
        }
        int n = paths.Count;
        return (100.0 * ok1 / n, 100.0 * ok3 / n, ms / n);
    }

    /// <summary>Е/Ё-варианты с одинаковым шаблоном (ещё/еще) считаем одним словом.</summary>
    static bool Same(string a, string b) { return a != null && a.Replace('ё', 'е') == b.Replace('ё', 'е'); }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "report";
        var ru = Load("ru"); var en = Load("en");
        Console.WriteLine($"Словари: ru {ru.Count}, en {en.Count}");
        if (mode == "debug")
        {
            var lex = args[1] == "en" ? en : ru;
            var rng = new Random(int.Parse(args[3]));
            var path = SwipeSynth.Make(lex.Layout, args[2], rng, double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine(string.Join(" ", path.Where((q, i) => i % 4 == 0)));
            var r = GestureDecoder.Decode(lex, path, 8, true);
            foreach (var c in r.Candidates) Console.WriteLine($"{c.Word,-14} score {c.Score:0.000} geo {c.Geo:0.000} prior {c.Prior:0.00} {GestureDecoder.Explain(c.Entry, path)}");
            var tw = lex.Find(args[2]);
            Console.WriteLine($"верное: {GestureDecoder.Explain(tw, path)} prior {lex.LogPrior(tw):0.00}; кандидатов {r.Pruned}/{r.Considered}");
            return;
        }
        if (mode == "tune")
        {
            var best = new DecoderParams(); double bestScore = 0; if (args.Length > 1) foreach (var kv in args.Skip(1)) { var p2 = kv.Split('='); typeof(DecoderParams).GetField(p2[0]).SetValue(best, double.Parse(p2[1], System.Globalization.CultureInfo.InvariantCulture)); }
            var tr = Sample(ru, 3000, 500, 1, false); var te = Sample(en, 3000, 500, 2, false);
            Func<DecoderParams, double> score = p => { var a = Eval(ru, tr, 0.28, 11, p); var b = Eval(en, te, 0.28, 12, p); return a.t1 + b.t1 + 0.3 * (a.t3 + b.t3); };
            bestScore = score(best);
            Console.WriteLine($"старт {bestScore:0.0}");
            var rnd = new Random(5);
            string[] names = { "LocPow", "WLoc", "WCov", "WProp", "WShape", "WLen", "LmWeight" };
            for (int round = 0; round < 3; round++)
                foreach (var name in names)
                    foreach (var mul in new[] { 0.0, 0.5, 0.75, 1.33, 2.0 })
                    {
                        var p = best.Clone();
                        var f = typeof(DecoderParams).GetField(name);
                        double v = (double)f.GetValue(p);
                        if (v == 0 && mul == 0) continue;
                        f.SetValue(p, v == 0 ? 0.1 : v * mul);
                        double s = score(p);
                        if (s > bestScore + 0.05) { bestScore = s; best = p; Console.WriteLine($"  {name}={f.GetValue(p):0.###} → {s:0.0}"); }
                    }
            foreach (var name in names) Console.WriteLine($"{name} = {typeof(DecoderParams).GetField(name).GetValue(best):0.####}");
            return;
        }
        var prm = new DecoderParams();
        foreach (var (lex, name) in new[] { (ru, "Русский"), (en, "English") })
        {
            Console.WriteLine($"== {name} ==");
            var uni = Sample(lex, 3000, 1000, 101, false);
            var freq = Sample(lex, 20000, 1000, 202, true);
            foreach (var sigma in new[] { 0.15, 0.25, 0.35 })
            {
                var fails = new List<string>();
                var a = Eval(lex, uni, sigma, 7, prm, fails);
                var b = Eval(lex, freq, sigma, 8, prm);
                Console.WriteLine($"σ={sigma:0.00}: 3000 частых равномерно top1 {a.t1:0.0}% top3 {a.t3:0.0}% | 20000 по частоте top1 {b.t1:0.0}% top3 {b.t3:0.0}% | {a.ms:0.0} мс");
                if (sigma == 0.25) Console.WriteLine("   ошибки: " + string.Join("; ", fails.Take(25)));
            }
        }
    }
}

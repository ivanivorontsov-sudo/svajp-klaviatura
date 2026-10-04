using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SvajpKlaviatura.Engine
{
    /// <summary>Параметры распознавания. Подобраны на синтетических свайпах (tools/Bench).</summary>
    public sealed class DecoderParams
    {
        public int Samples = 64;
        /// <summary>Насколько далеко (в клавишах) от первой/последней буквы может начаться/закончиться свайп.</summary>
        public double StartRadius = 1.25, EndRadius = 1.7;
        /// <summary>Канал «прохода по буквам»: каждая буква слова по порядку должна быть рядом с траекторией.</summary>
        public double WLoc = 1.0;
        /// <summary>Канал «покрытия»: траектория не должна уходить от идеальной ломаной слова (лишние буквы).</summary>
        public double WCov = 2.66;
        /// <summary>Канал местоположения SHARK2: точки, равномерно взятые по длине, сравниваются попарно.</summary>
        public double WProp = 0.4;
        /// <summary>Канал формы SHARK2: то же после нормализации положения и масштаба.</summary>
        public double WShape = 0.225;
        /// <summary>Штраф за несовпадение длины траектории и идеальной ломаной.</summary>
        public double WLen = 0.5;
        /// <summary>Вес языковой модели (частоты слова) против геометрии.</summary>
        public double LmWeight = 0.12;
        /// <summary>Нормировка канала букв: сумма квадратов промахов делится на (число букв)^LocPow.</summary>
        public double LocPow = 1.0;

        public DecoderParams Clone() { return (DecoderParams)MemberwiseClone(); }
    }

    public sealed class Candidate
    {
        public string Word;
        public WordEntry Entry;
        public double Score, Geo, Prior;
        public override string ToString() { return Word + " " + Score.ToString("0.00"); }
    }

    public sealed class DecodeResult
    {
        public readonly List<Candidate> Candidates = new List<Candidate>();
        public int Considered, Pruned;
        public double Milliseconds;
        public string Best { get { return Candidates.Count > 0 ? Candidates[0].Word : null; } }
    }

    /// <summary>Распознавание слова по траектории свайпа.
    /// Основа — SHARK2 (Kristensson &amp; Zhai, UIST 2004): ресэмплинг траектории и шаблонов слов,
    /// отсев по первой/последней клавише и длине, каналы формы и местоположения,
    /// объединение с частотой слова. Добавлен канал упорядоченного прохода по буквам
    /// (динамическое программирование, как эластичное сопоставление в декодерах жестового набора).</summary>
    public static class GestureDecoder
    {
        public static DecodeResult Decode(Lexicon lex, IList<Pt> raw, int max, bool hideBlocked, DecoderParams prm = null)
        {
            prm = prm ?? new DecoderParams();
            var sw = Stopwatch.StartNew();
            var res = new DecodeResult();
            var pts = Clean(raw);
            if (pts.Count < 2) return res;
            int n = Math.Max(16, prm.Samples);
            double lg = PathLength(pts);
            Pt[] g = Resample(pts, n);
            Pt[] gs = NormalizeShape(g);
            Pt s = pts[0], t = pts[pts.Count - 1];

            var first = new List<char>();
            foreach (var kv in lex.Layout.Letters)
            {
                char c = Lexicon.TemplateChar(kv.Key);
                if (c != kv.Key) continue; // ё — на месте е
                if (Pt.Dist(s, kv.Value.Center) <= prm.StartRadius) first.Add(c);
            }

            var cands = new List<Candidate>();
            var tpl = new Pt[n];
            var prev = new double[n];
            var cur = new double[n];
            double er2 = prm.EndRadius * prm.EndRadius;
            foreach (char fc in first)
            {
                var list = lex.StartingWith(fc);
                if (list == null) continue;
                foreach (var e in list)
                {
                    res.Considered++;
                    if (e.Keys.Length < 2) continue;
                    if (hideBlocked && e.Blocked && e.UserCount == 0) continue;
                    if (Pt.Dist2(t, e.Keys[e.Keys.Length - 1]) > er2) continue;
                    double lt = e.PathLength;
                    if (lg < 0.45 * lt - 0.8 || lg > 2.2 * lt + 3) continue;
                    res.Pruned++;
                    double geo = Geometry(e.Keys, lt, g, gs, lg, prm, tpl, prev, cur);
                    double prior = lex.LogPrior(e);
                    cands.Add(new Candidate { Word = e.Word, Entry = e, Geo = geo, Prior = prior, Score = -geo + prm.LmWeight * prior });
                }
            }
            cands.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < cands.Count && res.Candidates.Count < max; i++) res.Candidates.Add(cands[i]);
            res.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>Разбор по каналам (для отладки).</summary>
        public static string Explain(WordEntry e, IList<Pt> raw, DecoderParams prm = null)
        {
            prm = prm ?? new DecoderParams();
            var pts = Clean(raw);
            int n = prm.Samples;
            var g = Resample(pts, n);
            var parts = new double[5];
            Geometry(e.Keys, e.PathLength, g, NormalizeShape(g), PathLength(pts), prm, new Pt[n], new double[n], new double[n], parts);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "loc {0:0.000} cov {1:0.000} prop {2:0.000} shape {3:0.000} len {4:0.000}", parts[0], parts[1], parts[2], parts[3], parts[4]);
        }

        internal static double Geometry(Pt[] keys, double lt, Pt[] g, Pt[] gs, double lg, DecoderParams prm, Pt[] tpl, double[] prev, double[] cur, double[] parts = null)
        {
            int n = g.Length, m = keys.Length;
            // 1) упорядоченный проход по буквам: первая буква — начало, последняя — конец, остальные по порядку
            for (int i = 0; i < n; i++) prev[i] = double.PositiveInfinity;
            prev[0] = Pt.Dist2(keys[0], g[0]);
            for (int j = 1; j < m; j++)
            {
                double run = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (prev[i] < run) run = prev[i];
                    cur[i] = run + Pt.Dist2(keys[j], g[i]);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
            double loc = prev[n - 1] / Math.Pow(m, prm.LocPow);

            // 2) упорядоченное покрытие (DTW): точки траектории по порядку ложатся на отрезки ломаной слова
            //    по порядку — лишние повороты и буквы, которых нет в слове, дают штраф
            int segs = m - 1;
            for (int j = 0; j < segs; j++) prev[j] = double.PositiveInfinity;
            prev[0] = SegDist2(g[0], keys[0], keys[1]);
            for (int i = 1; i < n; i++)
            {
                for (int j = 0; j < segs; j++)
                {
                    double best = prev[j];
                    if (j > 0 && prev[j - 1] < best) best = prev[j - 1];
                    double c = best + SegDist2(g[i], keys[j], keys[j + 1]);
                    cur[j] = c;
                }
                var tmp2 = prev; prev = cur; cur = tmp2;
            }
            double cov = prev[segs - 1] / n;

            // 3) SHARK2: попарное сравнение равномерно ресэмплированных траектории и шаблона
            ResampleInto(keys, lt, tpl);
            double prop = 0;
            for (int i = 0; i < n; i++) prop += Pt.Dist2(g[i], tpl[i]);
            prop /= n;
            Pt[] ts = NormalizeShape(tpl);
            double shape = 0;
            for (int i = 0; i < n; i++) shape += Pt.Dist2(gs[i], ts[i]);
            shape /= n;

            double lr = Math.Log((lg + 1) / (lt + 1));
            if (parts != null) { parts[0] = loc; parts[1] = cov; parts[2] = prop; parts[3] = shape; parts[4] = lr * lr; }
            return prm.WLoc * loc + prm.WCov * cov + prm.WProp * prop + prm.WShape * shape + prm.WLen * lr * lr;
        }

        public static List<Pt> Clean(IList<Pt> raw)
        {
            var r = new List<Pt>(raw == null ? 0 : raw.Count);
            if (raw == null) return r;
            foreach (var p in raw)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y)) continue;
                if (r.Count > 0 && Pt.Dist2(r[r.Count - 1], p) < 1e-8) continue;
                r.Add(p);
            }
            return r;
        }

        public static double PathLength(IList<Pt> p)
        {
            double len = 0;
            for (int i = 1; i < p.Count; i++) len += Pt.Dist(p[i - 1], p[i]);
            return len;
        }

        public static Pt[] Resample(IList<Pt> p, int n)
        {
            var r = new Pt[n];
            ResampleInto(p, PathLength(p), r);
            return r;
        }

        /// <summary>n точек на равных расстояниях вдоль ломаной.</summary>
        public static void ResampleInto(IList<Pt> p, double len, Pt[] r)
        {
            int n = r.Length;
            if (p.Count == 1 || len <= 1e-9) { for (int i = 0; i < n; i++) r[i] = p[0]; return; }
            double step = len / (n - 1);
            int seg = 1; double segStart = 0, segLen = Pt.Dist(p[0], p[1]);
            for (int i = 0; i < n; i++)
            {
                double d = i == n - 1 ? len : i * step;
                while (seg < p.Count - 1 && d > segStart + segLen) { segStart += segLen; seg++; segLen = Pt.Dist(p[seg - 1], p[seg]); }
                double u = segLen <= 1e-12 ? 0 : (d - segStart) / segLen;
                if (u < 0) u = 0; if (u > 1) u = 1;
                Pt a = p[seg - 1], b = p[seg];
                r[i] = new Pt(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
            }
        }

        /// <summary>Нормализация SHARK2: центр в 0, большая сторона рамки = 1 (не меньше 1 клавиши, чтобы не раздувать короткие слова).</summary>
        public static Pt[] NormalizeShape(Pt[] p)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, cx = 0, cy = 0;
            foreach (var q in p)
            {
                if (q.X < minX) minX = q.X; if (q.X > maxX) maxX = q.X;
                if (q.Y < minY) minY = q.Y; if (q.Y > maxY) maxY = q.Y;
                cx += q.X; cy += q.Y;
            }
            cx /= p.Length; cy /= p.Length;
            double scale = Math.Max(1.0, Math.Max(maxX - minX, maxY - minY));
            var r = new Pt[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = new Pt((p[i].X - cx) / scale * 4, (p[i].Y - cy) / scale * 4);
            return r;
        }

        public static double SegDist2(Pt p, Pt a, Pt b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y, wx = p.X - a.X, wy = p.Y - a.Y;
            double l2 = vx * vx + vy * vy;
            double u = l2 <= 1e-12 ? 0 : (wx * vx + wy * vy) / l2;
            if (u < 0) u = 0; else if (u > 1) u = 1;
            double dx = wx - u * vx, dy = wy - u * vy;
            return dx * dx + dy * dy;
        }
    }
}

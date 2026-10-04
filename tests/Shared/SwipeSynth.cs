using System;
using System.Collections.Generic;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura.Testing
{
    /// <summary>Генератор «человеческих» свайпов: промахи мимо центров клавиш, общий сдвиг руки,
    /// срезание углов, неравномерная скорость, дрожание. Всё в клавишных единицах.</summary>
    public static class SwipeSynth
    {
        public static double Gauss(Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        public static List<Pt> KeyCenters(KeyboardLayout layout, string word)
        {
            var r = new List<Pt>();
            char prev = '\0';
            foreach (char raw in word)
            {
                if (raw == '-' || raw == '\'') continue;
                char c = Lexicon.TemplateChar(raw);
                Pt p;
                if (!layout.TryLetterCenter(c, out p)) return null;
                if (c == prev) continue;
                prev = c;
                r.Add(p);
            }
            return r;
        }

        /// <param name="sigma">Разброс прицеливания в долях клавиши (0,15 — аккуратно, 0,25 — обычно, 0,35 — небрежно).</param>
        public static List<Pt> Make(KeyboardLayout layout, string word, Random rng, double sigma)
        {
            var c = KeyCenters(layout, word);
            if (c == null || c.Count < 2) return null;
            double gx = Gauss(rng) * sigma * 0.5, gy = Gauss(rng) * sigma * 0.5; // систематический сдвиг руки
            var aim = new List<Pt>();
            foreach (var p in c)
            {
                double dx = Clamp(Gauss(rng) * sigma + gx, 0.75), dy = Clamp(Gauss(rng) * sigma + gy, 0.75);
                aim.Add(new Pt(p.X + dx, p.Y + dy));
            }
            // срезание углов: на каждом повороте путь проходит не через точку, а по дуге
            double cut = 0.15 + rng.NextDouble() * 0.3;
            var path = aim;
            for (int it = 0; it < 2; it++) { path = Round(path, cut); cut *= 0.5; }
            // равномерно по длине с шагом ~0,05–0,1 клавиши, но точки чаще у поворотов (замедление)
            var dense = GestureDecoder.Resample(path, Math.Max(20, (int)(GestureDecoder.PathLength(path) / 0.03)));
            var outp = new List<Pt>();
            double acc = 0, next = 0;
            for (int i = 0; i < dense.Length; i++)
            {
                if (i > 0) acc += Pt.Dist(dense[i - 1], dense[i]);
                if (acc + 1e-9 < next && i != dense.Length - 1) continue;
                double near = double.MaxValue;
                foreach (var a in aim) near = Math.Min(near, Pt.Dist(a, dense[i]));
                double speed = 0.35 + 0.65 * Math.Min(1, near / 1.2);
                next = acc + 0.12 * speed * (0.7 + 0.6 * rng.NextDouble());
                outp.Add(new Pt(dense[i].X + Gauss(rng) * 0.02, dense[i].Y + Gauss(rng) * 0.02));
            }
            return outp;
        }

        private static double Clamp(double v, double m) { return v > m ? m : v < -m ? -m : v; }

        private static List<Pt> Round(List<Pt> p, double r)
        {
            if (p.Count < 3) return p;
            var o = new List<Pt> { p[0] };
            for (int i = 1; i < p.Count - 1; i++)
            {
                Pt a = p[i - 1], b = p[i], c = p[i + 1];
                double la = Pt.Dist(a, b), lc = Pt.Dist(b, c);
                double ra = Math.Min(r, la * 0.4), rc = Math.Min(r, lc * 0.4);
                o.Add(la < 1e-9 ? b : new Pt(b.X + (a.X - b.X) * ra / la, b.Y + (a.Y - b.Y) * ra / la));
                o.Add(lc < 1e-9 ? b : new Pt(b.X + (c.X - b.X) * rc / lc, b.Y + (c.Y - b.Y) * rc / lc));
            }
            o.Add(p[p.Count - 1]);
            return o;
        }
    }
}

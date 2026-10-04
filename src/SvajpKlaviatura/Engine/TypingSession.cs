using System;
using System.Collections.Generic;
using System.Text;

namespace SvajpKlaviatura.Engine
{
    public enum OutKind { Text, Backspace, Key }
    public enum VKey { Enter, Tab, Left, Right, Up, Down }

    /// <summary>Одно действие ввода в активное окно.</summary>
    public struct OutAction
    {
        public OutKind Kind;
        public string Text;
        public int Count;
        public VKey Key;
        public static OutAction Type(string s) { return new OutAction { Kind = OutKind.Text, Text = s }; }
        public static OutAction Back(int n) { return new OutAction { Kind = OutKind.Backspace, Count = n }; }
        public static OutAction Press(VKey k) { return new OutAction { Kind = OutKind.Key, Key = k }; }
        public override string ToString() { return Kind == OutKind.Text ? "«" + Text + "»" : Kind == OutKind.Backspace ? "⌫×" + Count : Key.ToString(); }
    }

    public enum ShiftState { Off, Once, Locked }
    public enum CaseMode { Lower, Capital, Upper }
    public enum SuggestionKind { None, Alternatives, Completions }

    public sealed class Suggestion
    {
        public string Word;
        /// <summary>Слово набрано по буквам и его нет в словаре: выбор добавит его.</summary>
        public bool AddsWord;
        public override string ToString() { return Word; }
    }

    /// <summary>Логика набора: что отправить в активное окно на свайп, нажатие, ⌫, выбор подсказки.
    /// Не зависит от Windows — проверяется юнит-тестами.</summary>
    public sealed class TypingSession
    {
        public Lexicon Lex;
        public bool AutoCapitalize = true;
        public bool SmartPunctuation = true;
        public bool HideBlocked = true;
        public bool Learn = true;
        public ShiftState Shift = ShiftState.Off;

        /// <summary>Последнее слово из свайпа или подсказки, после которого стоит автоматический пробел.</summary>
        public string LastWord { get; private set; }
        public string Composing { get; private set; }
        public bool AutoSpace { get; private set; }
        public bool SentenceStart { get; private set; }
        public SuggestionKind SuggestionKind { get; private set; }
        public readonly List<Suggestion> Suggestions = new List<Suggestion>();

        private readonly List<string> alternatives = new List<string>();
        private CaseMode lastCase;
        private bool sentenceStartBeforeWord;
        private bool lastWordLearned;
        private char lastEmitted = '\0';
        private readonly StringBuilder typedLog = new StringBuilder();

        public TypingSession(Lexicon lex) { Lex = lex; Composing = ""; }

        /// <summary>Сколько последних символов набрано — для проверки в тестах.</summary>
        public string TypedLog { get { return typedLog.ToString(); } }

        public CaseMode CurrentCase
        {
            get
            {
                if (Shift == ShiftState.Locked) return CaseMode.Upper;
                if (Shift == ShiftState.Once || (AutoCapitalize && SentenceStart)) return CaseMode.Capital;
                return CaseMode.Lower;
            }
        }

        /// <summary>Контекст потерян (сменилось окно, щелчок мышью в тексте): ⌫ снова стирает по букве.</summary>
        public void Reset()
        {
            LastWord = null; Composing = ""; AutoSpace = false; SentenceStart = false; lastEmitted = '\0';
            alternatives.Clear();
            SetSuggestions(SuggestionKind.None, null);
        }

        public void ToggleShift()
        {
            Shift = Shift == ShiftState.Off ? ShiftState.Once : Shift == ShiftState.Once ? ShiftState.Locked : ShiftState.Off;
        }

        public static string ApplyCase(string w, CaseMode mode, string lang)
        {
            if (string.IsNullOrEmpty(w)) return w;
            if (mode == CaseMode.Upper) return w.ToUpperInvariant();
            if (lang == Layouts.En && (w == "i" || w.StartsWith("i'", StringComparison.Ordinal))) mode = CaseMode.Capital;
            if (mode == CaseMode.Capital) return char.ToUpperInvariant(w[0]) + w.Substring(1);
            return w;
        }

        private void Emit(List<OutAction> o, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (o.Count > 0 && o[o.Count - 1].Kind == OutKind.Text) o[o.Count - 1] = OutAction.Type(o[o.Count - 1].Text + s);
            else o.Add(OutAction.Type(s));
            typedLog.Append(s);
            lastEmitted = s[s.Length - 1];
        }

        private void EmitBack(List<OutAction> o, int n)
        {
            if (n <= 0) return;
            o.Add(OutAction.Back(n));
            typedLog.Length = Math.Max(0, typedLog.Length - n);
            lastEmitted = typedLog.Length > 0 ? typedLog[typedLog.Length - 1] : '\0';
        }

        private void AfterCharTyped()
        {
            if (Shift == ShiftState.Once) Shift = ShiftState.Off;
            SentenceStart = false;
        }

        private void CommitComposing()
        {
            if (Composing.Length >= 2 && Learn && Lex != null)
            {
                var e = Lex.Find(Composing);
                if (e != null || Composing.Length >= 3) Lex.Learn(Composing, 1);
            }
            Composing = "";
        }

        private void ForgetLastWord()
        {
            if (LastWord != null && lastWordLearned && Learn && Lex != null) Lex.Learn(LastWord, -1);
            lastWordLearned = false;
        }

        // ---------------- свайп ----------------

        public List<OutAction> Swipe(DecodeResult r)
        {
            var o = new List<OutAction>();
            if (r == null || r.Candidates.Count == 0) return o;
            bool needSpace = Composing.Length > 0 || char.IsLetterOrDigit(lastEmitted);
            CommitComposing();
            if (needSpace && !AutoSpace) Emit(o, " ");
            sentenceStartBeforeWord = SentenceStart;
            lastCase = CurrentCase;
            alternatives.Clear();
            foreach (var c in r.Candidates) alternatives.Add(c.Word);
            PutWord(o, alternatives[0], learnDelta: 1);
            return o;
        }

        private void PutWord(List<OutAction> o, string raw, int learnDelta)
        {
            string w = ApplyCase(raw, lastCase, Lex == null ? null : Lex.Language);
            Emit(o, w + " ");
            LastWord = w; AutoSpace = true; Composing = "";
            lastWordLearned = false;
            if (Learn && Lex != null && learnDelta > 0) { Lex.Learn(raw, learnDelta); lastWordLearned = true; }
            AfterCharTyped();
            var alts = new List<string>();
            foreach (var a in alternatives) if (a != raw && alts.Count < 3) alts.Add(a);
            SetSuggestions(SuggestionKind.Alternatives, alts);
        }

        // ---------------- нажатия ----------------

        private const string SentenceEnd = ".!?…";
        private const string AutoSpacedPunct = ".,!?:;…";

        public List<OutAction> TapText(string text, bool isLetter)
        {
            var o = new List<OutAction>();
            if (string.IsNullOrEmpty(text)) return o;
            if (isLetter)
            {
                string c = CurrentCase == CaseMode.Lower ? text.ToLowerInvariant() : text.ToUpperInvariant();
                if (LastWord != null) { LastWord = null; }
                Emit(o, c);
                Composing += c;
                AutoSpace = false;
                AfterCharTyped();
                UpdateCompletions();
                return o;
            }
            char ch = text[0];
            if (text.Length == 1 && SmartPunctuation && AutoSpacedPunct.IndexOf(ch) >= 0)
            {
                if (AutoSpace) EmitBack(o, 1);
                CommitComposing();
                Emit(o, text + " ");
                AutoSpace = true;
                LastWord = null;
                SentenceStart = SentenceEnd.IndexOf(ch) >= 0;
                if (Shift == ShiftState.Once) Shift = ShiftState.Off;
                SetSuggestions(SuggestionKind.None, null);
                return o;
            }
            if ((ch == '-' || ch == '\'') && Composing.Length > 0)
            {
                Emit(o, text);
                Composing += text;
                AutoSpace = false; LastWord = null;
                return o;
            }
            CommitComposing();
            Emit(o, text);
            AutoSpace = false; LastWord = null;
            if (Shift == ShiftState.Once) Shift = ShiftState.Off;
            SetSuggestions(SuggestionKind.None, null);
            return o;
        }

        public List<OutAction> Space()
        {
            var o = new List<OutAction>();
            if (AutoSpace)
            {
                // пробел уже поставлен автоматически — второй не нужен
                AutoSpace = false;
                LastWord = null;
                SetSuggestions(SuggestionKind.None, null);
                return o;
            }
            CommitComposing();
            Emit(o, " ");
            LastWord = null;
            SetSuggestions(SuggestionKind.None, null);
            return o;
        }

        public List<OutAction> Backspace()
        {
            var o = new List<OutAction>();
            if (LastWord != null && AutoSpace)
            {
                EmitBack(o, LastWord.Length + 1);
                ForgetLastWord();
                LastWord = null; AutoSpace = false;
                SentenceStart = sentenceStartBeforeWord;
                SetSuggestions(SuggestionKind.None, null);
                return o;
            }
            EmitBack(o, 1);
            AutoSpace = false; LastWord = null;
            if (Composing.Length > 0)
            {
                Composing = Composing.Substring(0, Composing.Length - 1);
                UpdateCompletions();
            }
            else SetSuggestions(SuggestionKind.None, null);
            return o;
        }

        public List<OutAction> Enter()
        {
            var o = new List<OutAction>();
            CommitComposing();
            if (AutoSpace) EmitBack(o, 1);
            o.Add(OutAction.Press(VKey.Enter));
            typedLog.Append('\n'); lastEmitted = '\n';
            AutoSpace = false; LastWord = null;
            SentenceStart = true;
            SetSuggestions(SuggestionKind.None, null);
            return o;
        }

        public List<OutAction> Special(VKey k)
        {
            CommitComposing();
            Reset();
            return new List<OutAction> { OutAction.Press(k) };
        }

        public List<OutAction> Choose(int index)
        {
            var o = new List<OutAction>();
            if (index < 0 || index >= Suggestions.Count) return o;
            var s = Suggestions[index];
            if (SuggestionKind == SuggestionKind.Alternatives && LastWord != null && AutoSpace)
            {
                EmitBack(o, LastWord.Length + 1);
                ForgetLastWord();
                SentenceStart = sentenceStartBeforeWord;
                string raw = s.Word;
                // выбранное слово — первым, остальные остаются подсказками
                alternatives.Remove(raw);
                alternatives.Insert(0, raw);
                PutWord(o, raw, learnDelta: 2);
                return o;
            }
            if (SuggestionKind == SuggestionKind.Completions && Composing.Length > 0)
            {
                string typed = Composing;
                EmitBack(o, typed.Length);
                CaseMode cm = typed.ToUpperInvariant() == typed && typed.Length > 1 ? CaseMode.Upper : char.IsUpper(typed[0]) ? CaseMode.Capital : CaseMode.Lower;
                lastCase = cm;
                sentenceStartBeforeWord = false;
                alternatives.Clear();
                alternatives.Add(s.Word);
                Composing = "";
                if (s.AddsWord && Lex != null) Lex.AddUserWord(s.Word);
                PutWord(o, s.Word.ToLowerInvariant(), learnDelta: s.AddsWord ? 0 : 1);
                if (s.AddsWord) lastWordLearned = false;
                SetSuggestions(SuggestionKind.None, null);
                return o;
            }
            return o;
        }

        private void UpdateCompletions()
        {
            if (Composing.Length == 0 || Lex == null) { SetSuggestions(SuggestionKind.None, null); return; }
            var lower = Composing.ToLowerInvariant();
            var list = new List<Suggestion>();
            bool known = Lex.Find(lower) != null;
            if (!known && lower.Length >= 2 && Lexicon.Normalize(lower) != null) list.Add(new Suggestion { Word = Composing, AddsWord = true });
            CaseMode cm = Composing.Length > 1 && Composing.ToUpperInvariant() == Composing ? CaseMode.Upper : char.IsUpper(Composing[0]) ? CaseMode.Capital : CaseMode.Lower;
            foreach (var w in Lex.Completions(lower, 3, HideBlocked))
            {
                if (list.Count >= 3) break;
                list.Add(new Suggestion { Word = ApplyCase(w, cm, Lex.Language) });
            }
            Suggestions.Clear(); Suggestions.AddRange(list);
            SuggestionKind = list.Count > 0 ? SuggestionKind.Completions : SuggestionKind.None;
        }

        private void SetSuggestions(SuggestionKind kind, List<string> words)
        {
            Suggestions.Clear();
            if (words != null)
                foreach (var w in words) Suggestions.Add(new Suggestion { Word = ApplyCase(w, lastCase, Lex == null ? null : Lex.Language) });
            SuggestionKind = Suggestions.Count > 0 ? kind : SuggestionKind.None;
        }
    }
}

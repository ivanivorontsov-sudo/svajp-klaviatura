using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SvajpKlaviatura.Engine;
using SvajpKlaviatura.Testing;
using Xunit;

namespace SvajpKlaviatura.EngineTests
{
    /// <summary>Имитация поля ввода: применяет действия сессии к строке.</summary>
    internal sealed class FakeField
    {
        public readonly StringBuilder Text = new StringBuilder();
        public void Apply(List<OutAction> acts)
        {
            foreach (var a in acts)
            {
                if (a.Kind == OutKind.Text) Text.Append(a.Text);
                else if (a.Kind == OutKind.Backspace) Text.Length = Math.Max(0, Text.Length - a.Count);
                else if (a.Key == VKey.Enter) Text.Append('\n');
            }
        }
        public override string ToString() { return Text.ToString(); }
    }

    public class TypingTests
    {
        private static DecodeResult Result(params string[] words)
        {
            var r = new DecodeResult();
            foreach (var w in words) r.Candidates.Add(new Candidate { Word = w });
            return r;
        }

        private static (TypingSession s, FakeField f) New(string lang = "ru")
        {
            var s = new TypingSession(Dicts.Load(lang)) { AutoCapitalize = false };
            return (s, new FakeField());
        }

        [Fact]
        public void Swipe_TypesWordWithSpace_BackspaceDeletesWholeWord()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("привет", "пират", "привез")));
            Assert.Equal("привет ", f.ToString());
            Assert.Equal(new[] { "пират", "привез" }, s.Suggestions.Select(x => x.Word).ToArray());
            f.Apply(s.Swipe(Result("мир")));
            Assert.Equal("привет мир ", f.ToString());
            f.Apply(s.Backspace());
            Assert.Equal("привет ", f.ToString());
            f.Apply(s.Backspace());
            Assert.Equal("привет", f.ToString()); // дальше — по одной букве
        }

        [Fact]
        public void ChooseAlternative_ReplacesLastWord()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("пират", "привет", "привез")));
            f.Apply(s.Choose(0));
            Assert.Equal("привет ", f.ToString());
            Assert.Contains(s.Suggestions, x => x.Word == "пират");
            f.Apply(s.Choose(s.Suggestions.FindIndex(x => x.Word == "привез")));
            Assert.Equal("привез ", f.ToString());
            Assert.True(s.Lex.Find("привез").UserCount >= 2);
        }

        [Fact]
        public void Punctuation_RemovesAutoSpace_AndAddsOne()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("привет")));
            f.Apply(s.TapText(",", false));
            f.Apply(s.Swipe(Result("мир")));
            f.Apply(s.TapText("!", false));
            Assert.Equal("привет, мир! ", f.ToString());
        }

        [Fact]
        public void Space_AfterSwipe_IsNotDoubled()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("да")));
            f.Apply(s.Space());
            Assert.Equal("да ", f.ToString());
            f.Apply(s.Space());
            Assert.Equal("да  ", f.ToString());
        }

        [Fact]
        public void AutoCapitalize_AfterSentenceEnd()
        {
            var (s, f) = New();
            s.AutoCapitalize = true;
            f.Apply(s.Enter());
            f.Apply(s.Swipe(Result("привет")));
            f.Apply(s.TapText(".", false));
            f.Apply(s.Swipe(Result("как")));
            f.Apply(s.Swipe(Result("дела")));
            f.Apply(s.TapText("?", false));
            Assert.Equal("\nПривет. Как дела? ", f.ToString());
        }

        [Fact]
        public void Shift_OnceAndCapsLock()
        {
            var (s, f) = New();
            s.ToggleShift();
            f.Apply(s.Swipe(Result("москва")));
            f.Apply(s.Swipe(Result("город")));
            s.ToggleShift(); s.ToggleShift();
            Assert.Equal(ShiftState.Locked, s.Shift);
            f.Apply(s.Swipe(Result("сша")));
            f.Apply(s.TapText("д", true));
            Assert.Equal("Москва город США Д", f.ToString());
        }

        [Fact]
        public void TapLetters_CompletionsAndAddWord()
        {
            var (s, f) = New();
            foreach (var c in "прив") f.Apply(s.TapText(c.ToString(), true));
            Assert.Equal(SuggestionKind.Completions, s.SuggestionKind);
            Assert.Contains(s.Suggestions, x => x.Word == "привет");
            f.Apply(s.Choose(s.Suggestions.FindIndex(x => x.Word == "привет")));
            Assert.Equal("привет ", f.ToString());
            f.Apply(s.Backspace());
            Assert.Equal("", f.ToString());

            foreach (var c in "грокбот") f.Apply(s.TapText(c.ToString(), true));
            Assert.True(s.Suggestions[0].AddsWord);
            f.Apply(s.Choose(0));
            Assert.Equal("грокбот ", f.ToString());
            Assert.NotNull(s.Lex.Find("грокбот"));
            var path = SwipeSynth.Make(s.Lex.Layout, "грокбот", new Random(2), 0.1);
            Assert.Contains(GestureDecoder.Decode(s.Lex, path, 3, true).Candidates, c => c.Word == "грокбот");
        }

        [Fact]
        public void TappedWord_ThenSwipe_InsertsSpaceBetween()
        {
            var (s, f) = New();
            foreach (var c in "ок") f.Apply(s.TapText(c.ToString(), true));
            f.Apply(s.Swipe(Result("спасибо")));
            Assert.Equal("ок спасибо ", f.ToString());
        }

        [Fact]
        public void HyphenatedWordByTaps()
        {
            var (s, f) = New();
            foreach (var c in "кто") f.Apply(s.TapText(c.ToString(), true));
            f.Apply(s.TapText("-", false));
            foreach (var c in "то") f.Apply(s.TapText(c.ToString(), true));
            Assert.Equal("кто-то", s.Composing);
            f.Apply(s.Space());
            Assert.Equal("кто-то ", f.ToString());
        }

        [Fact]
        public void English_I_IsCapital()
        {
            var (s, f) = New("en");
            f.Apply(s.Swipe(Result("i'm")));
            f.Apply(s.Swipe(Result("sure")));
            f.Apply(s.Swipe(Result("i")));
            Assert.Equal("I'm sure I ", f.ToString());
        }

        [Fact]
        public void Reset_MakesBackspaceSingleChar()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("привет")));
            s.Reset();
            f.Apply(s.Backspace());
            Assert.Equal("привет", f.ToString());
        }

        [Fact]
        public void BackspaceAfterSwipe_UndoesLearning()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("пират")));
            Assert.Equal(1, s.Lex.Find("пират").UserCount);
            f.Apply(s.Backspace());
            Assert.Equal(0, s.Lex.Find("пират").UserCount);
        }

        [Fact]
        public void Enter_RemovesAutoSpace()
        {
            var (s, f) = New();
            f.Apply(s.Swipe(Result("привет")));
            f.Apply(s.Enter());
            Assert.Equal("привет\n", f.ToString());
        }
    }
}

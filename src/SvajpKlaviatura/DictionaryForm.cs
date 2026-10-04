using System;
using System.Drawing;
using System.Windows.Forms;
using SvajpKlaviatura.Engine;

namespace SvajpKlaviatura
{
    /// <summary>«Мой словарь»: добавленные и выученные слова, добавление и удаление.</summary>
    internal sealed class DictionaryForm : Form
    {
        private readonly Dictionaries dicts;
        private readonly ComboBox cbLang;
        private readonly ListView list;
        private readonly TextBox tbWord;
        private readonly Label status;

        public DictionaryForm(Dictionaries d, string lang)
        {
            dicts = d;
            Text = "Свайп-клавиатура — мой словарь";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            KeyPreview = true;
            Size = new Size(480, 520);
            MinimumSize = new Size(380, 360);
            try { Icon = TrayApp.LoadAppIcon(32); } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            top.Controls.Add(new Label { Text = "Язык:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
            cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cbLang.Items.AddRange(new object[] { "Русский", "English" });
            cbLang.SelectedIndex = lang == Layouts.En ? 1 : 0;
            cbLang.SelectedIndexChanged += (s, e) => Reload();
            top.Controls.Add(cbLang);
            root.Controls.Add(top);

            var add = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            tbWord = new TextBox { Width = 250 };
            var btnAdd = new Button { Text = "Добавить слово", AutoSize = true };
            btnAdd.Click += (s, e) => AddWord();
            tbWord.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddWord(); e.SuppressKeyPress = true; } };
            add.Controls.Add(tbWord); add.Controls.Add(btnAdd);
            root.Controls.Add(add);

            list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
            list.Columns.Add("Слово", 260);
            list.Columns.Add("Использовано", 110);
            root.Controls.Add(list);

            var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            var btnRemove = new Button { Text = "Удалить выбранные", AutoSize = true };
            btnRemove.Click += (s, e) => RemoveSelected();
            var btnClose = new Button { Text = "Закрыть", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(btnRemove); bottom.Controls.Add(btnClose);
            root.Controls.Add(bottom);

            status = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 0), MaximumSize = new Size(440, 0) };
            root.Controls.Add(status);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); else if (e.KeyCode == Keys.Delete && list.Focused) RemoveSelected(); };
            Reload();
        }

        private Lexicon Lex { get { return dicts.For(cbLang.SelectedIndex == 1 ? Layouts.En : Layouts.Ru); } }

        private void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in Lex.UserWords())
                list.Items.Add(new ListViewItem(new[] { e.Word, e.UserCount.ToString() }) { Tag = e.Word });
            list.EndUpdate();
            status.Text = "Во встроенном словаре " + Lex.Count + " слов. Здесь — слова, которые вы добавили или часто набираете: их клавиатура предлагает охотнее. Слова, набранные по буквам, запоминаются сами.";
        }

        private void AddWord()
        {
            string w = Lexicon.Normalize(tbWord.Text);
            if (w == null) { status.Text = "Слово может состоять только из букв, дефиса и апострофа."; return; }
            var e = Lex.AddUserWord(w);
            if (e == null) { status.Text = "Не удалось добавить «" + tbWord.Text + "»."; return; }
            if (e.Keys == null) { Reload(); status.Text = "«" + w + "» добавлено, но свайпом его не набрать: в нём есть буквы другой раскладки."; }
            else { Reload(); status.Text = "«" + w + "» добавлено."; }
            tbWord.Clear();
            dicts.SaveIfChanged();
        }

        private void RemoveSelected()
        {
            int n = 0;
            foreach (ListViewItem it in list.SelectedItems) if (Lex.RemoveUserWord((string)it.Tag)) n++;
            if (n > 0) { Reload(); dicts.SaveIfChanged(); status.Text = "Удалено слов: " + n + "."; }
        }
    }
}

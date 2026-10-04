# Лицензии и источники

## Словари

`src/SvajpKlaviatura/Dict/ru.txt` и `en.txt` — по 50 000 самых частых слов, собраны скриптом
`tools/build_dict.py` из частотных списков **Hermit Dave FrequencyWords**
(https://github.com/hermitdave/FrequencyWords, данные OpenSubtitles 2018, http://opus.nlpl.eu/OpenSubtitles2018.php).

* Данные (списки слов): **CC BY-SA 4.0** — https://creativecommons.org/licenses/by-sa/4.0/
  © Hermit Dave; исходный корпус — OpenSubtitles (opus.nlpl.eu, P. Lison, J. Tiedemann, 2016).
  Изменения: оставлены только слова из букв русского/английского алфавита (с дефисом и апострофом),
  убран мусор (три одинаковые буквы подряд, одиночные буквы кроме предлогов), для английского
  восстановлены сокращения (don't, I'm, it's…) — их частоты оценены по частотам частей.
  Изменённые списки распространяются на тех же условиях CC BY-SA 4.0.
* Код генератора списков Hermit Dave — MIT.

## Алгоритм

Распознавание построено по опубликованным методам (код написан с нуля):

* P.-O. Kristensson, S. Zhai. *SHARK2: A Large Vocabulary Shorthand Writing System for Pen-based Computers.* UIST 2004.
  https://pokristensson.com/pubs/KristenssonZhaiUIST2004.pdf — ресэмплинг траектории и шаблонов,
  отсев по первой/последней клавише, каналы формы и местоположения, объединение с языковой моделью.
* S. Zhai, P.-O. Kristensson. *Shorthand Writing on Stylus Keyboard.* CHI 2003.
* Эластичное сопоставление траектории с ломаной слова (DTW) — как в декодерах жестового набора
  (обзор: Ouyang et al., *Mobile Keyboard Input Decoding with Finite-State Transducers*, 2017 — Gboard;
  Alsharif et al., *Long short term memory neural network for keyboard gesture decoding*, ICASSP 2015).

## Программа

© 2026 Иван Воронцов. Внешних библиотек нет: только .NET Framework 4.8 (входит в Windows 10/11).

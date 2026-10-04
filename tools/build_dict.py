#!/usr/bin/env python3
"""Собирает словари для «Свайп-клавиатуры» из частотных списков Hermit Dave FrequencyWords
(https://github.com/hermitdave/FrequencyWords, данные OpenSubtitles 2018, лицензия CC BY-SA 4.0).

Запуск: python3 tools/build_dict.py <папка с ru_full.txt и en_full.txt>
Результат: src/SvajpKlaviatura/Dict/ru.txt и en.txt — «слово частота», по убыванию частоты, по 50 000 слов.
"""
import os, re, sys

LIMIT = 50000
RU = set('абвгдеёжзийклмнопрстуфхцчшщъыьэюя')
EN = set('abcdefghijklmnopqrstuvwxyz')
RU_SINGLE = set('авикосуя')
EN_SINGLE = set('ai')

def valid(w, alpha, singles):
    if len(w) == 1:
        return w in singles
    if not all(c in alpha or c in "-'" for c in w):
        return False
    if w[0] in "-'" or w[-1] in "-'" or '--' in w or "''" in w:
        return False
    letters = [c for c in w if c in alpha]
    if len(letters) < 2:
        return False
    # мусор вида «ааааа», «ммм-м»: три одинаковые буквы подряд
    if re.search(r'(.)\1\1', w):
        return False
    return True

def load(path, alpha, singles, extra_skip=()):
    out = []
    seen = set()
    with open(path, encoding='utf-8') as f:
        for line in f:
            parts = line.split()
            if len(parts) != 2:
                continue
            w, c = parts[0].lower(), int(parts[1])
            if w in seen or w in extra_skip or not valid(w, alpha, singles):
                continue
            seen.add(w)
            out.append((w, c))
    return out

# Английские сокращения: токенизатор OpenSubtitles разрезает «don't» на «don» + «'t».
# Частоты восстанавливаем приблизительно: по частоте основы или доле частоты окончания.
EN_STEM = {"don't": 'don', "didn't": 'didn', "doesn't": 'doesn', "isn't": 'isn', "wasn't": 'wasn',
           "couldn't": 'couldn', "wouldn't": 'wouldn', "shouldn't": 'shouldn', "haven't": 'haven',
           "hasn't": 'hasn', "aren't": 'aren', "weren't": 'weren', "ain't": 'ain', "hadn't": 'hadn',
           "mustn't": 'mustn', "needn't": 'needn'}
EN_SHARE = {"i'm": ("'m", 1.0), "it's": ("'s", .30), "that's": ("'s", .12), "what's": ("'s", .06),
            "he's": ("'s", .06), "she's": ("'s", .03), "there's": ("'s", .04), "let's": ("'s", .04),
            "here's": ("'s", .015), "who's": ("'s", .01), "where's": ("'s", .006), "how's": ("'s", .003),
            "you're": ("'re", .5), "we're": ("'re", .25), "they're": ("'re", .2),
            "i'll": ("'ll", .45), "you'll": ("'ll", .2), "we'll": ("'ll", .15), "he'll": ("'ll", .05),
            "she'll": ("'ll", .03), "it'll": ("'ll", .04), "they'll": ("'ll", .05), "that'll": ("'ll", .02),
            "i've": ("'ve", .5), "you've": ("'ve", .2), "we've": ("'ve", .15), "they've": ("'ve", .05),
            "i'd": ("'d", .4), "you'd": ("'d", .2), "he'd": ("'d", .08), "she'd": ("'d", .05),
            "we'd": ("'d", .08), "they'd": ("'d", .05), "can't": ("'t", .2), "won't": ("'t", .08),
            "o'clock": ("'clock", 1.0), "y'all": ("'all", .5)}

def english(path):
    raw = {}
    with open(path, encoding='utf-8') as f:
        for i, line in enumerate(f):
            p = line.split()
            if len(p) == 2:
                raw.setdefault(p[0].lower(), int(p[1]))
            if i > 400000:
                break
    words = load(path, EN, EN_SINGLE, extra_skip=set(EN_STEM.values()) - {'don'} | {'don', 'didn', 'doesn', 'isn', 'wasn', 'couldn', 'wouldn', 'shouldn', 'haven', 'hasn', 'aren', 'weren', 'ain', 'hadn', 'mustn', 'needn'})
    words = [(w, c) for (w, c) in words if "'" not in w]
    for w, stem in EN_STEM.items():
        if stem in raw:
            words.append((w, raw[stem]))
    for w, (suf, share) in EN_SHARE.items():
        if suf in raw:
            words.append((w, int(raw[suf] * share)))
    d = {}
    for w, c in words:
        d[w] = max(c, d.get(w, 0))
    return sorted(d.items(), key=lambda t: -t[1])

def save(items, path, source):
    items = items[:LIMIT]
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# ' + source + '\n')
        for w, c in items:
            f.write(f'{w} {c}\n')
    print(path, len(items), 'слов, последняя частота', items[-1][1])

if __name__ == '__main__':
    src = sys.argv[1] if len(sys.argv) > 1 else '.'
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'SvajpKlaviatura', 'Dict')
    note = 'Hermit Dave FrequencyWords (OpenSubtitles 2018), CC BY-SA 4.0, https://github.com/hermitdave/FrequencyWords'
    save(load(os.path.join(src, 'ru_full.txt'), RU, RU_SINGLE), os.path.join(root, 'ru.txt'), note)
    save(english(os.path.join(src, 'en_full.txt')), os.path.join(root, 'en.txt'), note + '; сокращения вида don\'t восстановлены по частотам частей')

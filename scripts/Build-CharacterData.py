"""Rebuild the offline Unicode 17 / CLDR 48 character catalogue (standard library only)."""
import gzip
import hashlib
import json
import pathlib
import re
import ssl
import subprocess
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
DEST = ROOT / "Quickfetch.Core" / "Data"
DEST.mkdir(parents=True, exist_ok=True)
sources = {}

def fetch(url):
    cache = ROOT / "artifacts" / "unicode-source-cache" / (hashlib.sha256(url.encode()).hexdigest() + ".txt")
    if cache.exists():
        data = cache.read_bytes()
        sources[url] = hashlib.sha256(data).hexdigest()
        return data.decode("utf-8-sig")
    try:
        data = urllib.request.urlopen(url, timeout=60).read()
    except urllib.error.URLError as error:
        if sys.platform != "win32" or not isinstance(error.reason, ssl.SSLCertVerificationError):
            raise
        # Some bundled Python builds omit the Windows trust store. Schannel curl
        # still validates TLS using the OS certificates; verification stays on.
        data = subprocess.run(["curl.exe", "--fail", "--silent", "--show-error", "--location", "--retry", "3", "--retry-all-errors", "--max-time", "90", url], capture_output=True, check=True).stdout
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_bytes(data)
    sources[url] = hashlib.sha256(data).hexdigest()
    return data.decode("utf-8-sig")

def write_gzip(name, text):
    (DEST / name).write_bytes(gzip.compress(text.encode("utf-8"), mtime=0))

ucd = fetch("https://www.unicode.org/Public/17.0.0/ucd/UnicodeData.txt")
emoji = fetch("https://www.unicode.org/Public/17.0.0/emoji/emoji-test.txt")
annotations, titles = {}, {}
for locale in ("en", "zh"):
    for kind in ("annotations", "annotationsDerived"):
        xml = fetch(f"https://raw.githubusercontent.com/unicode-org/cldr/release-48/common/{kind}/{locale}.xml")
        for item in ET.fromstring(xml).iter("annotation"):
            char = item.attrib["cp"].replace("\ufe0f", "")
            text = "".join(item.itertext()).strip()
            if text in ("↑↑↑", "∅∅∅"):
                continue
            annotations.setdefault(char, []).append(text.replace("|", " "))
            if locale == "zh" and item.attrib.get("type") == "tts":
                titles[char] = text

rows = {int(f[0], 16): f for line in ucd.splitlines() if (f := line.split(";")) and len(f) > 5}
entries = {}
greek_names = dict(zip("ALPHA BETA GAMMA DELTA EPSILON ZETA ETA THETA IOTA KAPPA LAMDA MU NU XI OMICRON PI RHO SIGMA TAU UPSILON PHI CHI PSI OMEGA".split(), "阿尔法 贝塔 伽马 德尔塔 艾普西龙 泽塔 伊塔 西塔 约塔 卡帕 兰姆达 缪 纽 克西 奥米克戎 派 柔 西格玛 陶 宇普西龙 斐 凯 普西 欧米伽".split()))
greek_romanization = dict(zip(greek_names, "a b g d e z e th i k l m n x o p r s t u ph ch ps o".split()))
aliases = {
    "∞": "无穷 无限 无穷大 infinity", "∫": "积分 integral", "∬": "二重积分 double integral", "∭": "三重积分 triple integral", "∮": "曲线积分 环路积分 contour integral", "∑": "求和 总和 sigma sum", "∏": "连乘 乘积 product", "√": "根号 平方根 square root", "∛": "立方根 cube root", "∂": "偏导 偏微分 partial derivative", "∇": "梯度 纳布拉 散度 gradient nabla", "≈": "约等于 近似 approximately", "≠": "不等于 not equal", "≤": "小于等于 less equal", "≥": "大于等于 greater equal", "±": "正负 plus minus", "×": "乘号 multiply times", "÷": "除号 divide", "°": "度 degree", "℃": "摄氏度 celsius", "℉": "华氏度 fahrenheit", "ℏ": "约化普朗克 hbar", "ℝ": "实数 real numbers", "ℂ": "复数 complex numbers", "ℕ": "自然数 natural numbers", "ℤ": "整数 integers", "ℚ": "有理数 rational", "∅": "空集 empty set", "∈": "属于 element of", "∉": "不属于 not element", "⊂": "子集 subset", "⊆": "包含于 subset equal", "∪": "并集 union", "∩": "交集 intersection", "∀": "任意 全称 for all", "∃": "存在 exists", "→": "右箭头 向右 right arrow", "←": "左箭头 向左 left arrow", "↑": "上箭头 向上 up arrow", "↓": "下箭头 向下 down arrow", "↔": "双向箭头 left right arrow", "⇒": "推出 蕴含 implies", "⇔": "等价 等价于 equivalent", "π": "圆周率 pi", "µ": "微 micro", "Ω": "欧姆 ohm", "Å": "埃 angstrom", "′": "角分 撇 prime", "″": "角秒 双撇 double prime", "†": "厄米共轭 dagger", "•": "项目符号 圆点 bullet", "…": "省略号 ellipsis", "©": "版权 copyright", "®": "注册商标 registered", "™": "商标 trademark"
}

for cp, f in rows.items():
    name, cat, decomp = f[1], f[2], f[5]
    if name.startswith("<") or cat[0] == "C" or cat in ("Zl", "Zp"):
        continue
    char = chr(cp)
    group = "其他"
    extra = []
    if "HIRAGANA" in name:
        group = "平假名"; extra += ["日文 日语 假名 hiragana", name.split("LETTER ")[-1].replace("-", " ")]
    elif "KATAKANA" in name:
        group = "片假名"; extra += ["日文 日语 假名 katakana", name.split("LETTER ")[-1].replace("-", " ")]
    elif "GREEK" in name:
        group = "希腊字母"; extra += ["希腊 greek"] + [zh for en, zh in greek_names.items() if re.search(r"\b" + en + r"\b", name)]
    elif "LATIN" in name or "MODIFIER LETTER" in name:
        group = "拉丁与音标"; extra += ["拉丁 注音 音标 latin phonetic ipa"]
    elif cat == "Sm" or "MATHEMATICAL" in name or 0x2100 <= cp <= 0x214F:
        group = "科学符号"; extra += ["数学 科学 符号 math science"]
    elif cat[0] == "P" or "ARROW" in name:
        group = "标点与箭头"; extra += ["标点 符号 punctuation"]
    elif cat[0] == "N":
        group = "数字"; extra += ["数字 number"]
    if group in ("平假名", "片假名"):
        extra.append({"SI": "shi", "TI": "chi", "TU": "tsu", "HU": "fu", "ZI": "ji"}.get(name.split("LETTER ")[-1], ""))
    if "LAMDA" in name:
        extra.append("lambda 拉姆达")
    if group == "希腊字母" or "MATHEMATICAL" in name:
        extra.extend("letter " + roman for greek, roman in greek_romanization.items() if re.search(r"\b" + greek + r"\b", name))
    for keyword, translated in (("SCRIPT", "花体"), ("BOLD", "粗体"), ("ITALIC", "斜体"), ("FRAKTUR", "哥特体"), ("DOUBLE-STRUCK", "双线体")):
        if keyword in name:
            extra.append(translated)
    bits = decomp.split()
    if bits and bits[0].startswith("<"):
        bits = bits[1:]
    if bits and 0x41 <= int(bits[0], 16) <= 0x7A and cat[0] in "LM":
        extra.append("letter " + chr(int(bits[0], 16)).lower())
    if "SUPERSCRIPT" in name or decomp.startswith("<super>"):
        extra.append("上标 superscript")
    if "SUBSCRIPT" in name or decomp.startswith("<sub>"):
        extra.append("下标 subscript")
    key = char.replace("\ufe0f", "")
    extra += annotations.get(key, [])
    extra.append(aliases.get(char, ""))
    entries[char] = [char, titles.get(key, name), name, group, " ".join(extra)]

for line in emoji.splitlines():
    match = re.match(r"^([0-9A-F ]+);\s*fully-qualified\s*#\s*\S+\s+E[\d.]+\s+(.+)$", line)
    if not match:
        continue
    char = "".join(chr(int(cp, 16)) for cp in match[1].split())
    key = char.replace("\ufe0f", "")
    entries[char] = [char, titles.get(key, match[2]), match[2], "Emoji", "表情 emoji " + " ".join(annotations.get(key, []))]

write_gzip("characters.tsv.gz", "\n".join("\t".join(value.replace("\t", " ").replace("\n", " ") for value in row) for row in entries.values()))

styles = []
def add_style(id, name, mapping, note="未定义的字符保留原文"):
    styles.append(dict(Id=id, Name=name, Note=note, Map=mapping))

ascii_set = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
add_style("upper", "大写", {c: c.upper() for c in ascii_set}, "英文字母转大写")
add_style("lower", "小写", {c: c.lower() for c in ascii_set}, "英文字母转小写")
for id, name, offsets in [
    ("bold", "数学粗体", (0x1D400, 0x1D41A, 0x1D7CE)),
    ("italic", "数学斜体", (0x1D434, 0x1D44E, None)),
    ("bold-italic", "数学粗斜体", (0x1D468, 0x1D482, None)),
    ("script", "花体", (0x1D49C, 0x1D4B6, None)),
    ("bold-script", "粗花体", (0x1D4D0, 0x1D4EA, None)),
    ("fraktur", "哥特体", (0x1D504, 0x1D51E, None)),
    ("double", "双线体", (0x1D538, 0x1D552, 0x1D7D8)),
    ("bold-fraktur", "粗哥特体", (0x1D56C, 0x1D586, None)),
    ("sans", "无衬线", (0x1D5A0, 0x1D5BA, 0x1D7E2)),
    ("sans-bold", "无衬线粗体", (0x1D5D4, 0x1D5EE, 0x1D7EC)),
    ("sans-italic", "无衬线斜体", (0x1D608, 0x1D622, None)),
    ("sans-bold-italic", "无衬线粗斜体", (0x1D63C, 0x1D656, None)),
    ("mono", "等宽", (0x1D670, 0x1D68A, 0x1D7F6)),
    ("full", "全角", (0xFF21, 0xFF41, 0xFF10)),
]:
    mapping = {}
    for alphabet, start in zip((ascii_set[:26], ascii_set[26:52], ascii_set[52:]), offsets):
        if start is None:
            continue
        for i, base in enumerate(alphabet):
            cp = start + i
            if cp in rows and rows[cp][5].split()[-1:] == [f"{ord(base):04X}"]:
                mapping[base] = chr(cp)
    exceptions = {"italic": {"h": 0x210E}, "script": dict(zip("BEFHILMRego", (0x212C,0x2130,0x2131,0x210B,0x2110,0x2112,0x2133,0x211B,0x212F,0x210A,0x2134))), "fraktur": dict(zip("CHIRZ", (0x212D,0x210C,0x2111,0x211C,0x2128))), "double": dict(zip("CHNPQRZ", (0x2102,0x210D,0x2115,0x2119,0x211A,0x211D,0x2124)))}
    mapping.update({base: chr(cp) for base, cp in exceptions.get(id, {}).items()})
    add_style(id, name, mapping)

for id, name, tag in (("super", "上标", "<super>"), ("sub", "下标", "<sub>")):
    mapping = {}
    for cp, f in rows.items():
        bits = f[5].split()
        if len(bits) == 2 and bits[0] == tag and (base := chr(int(bits[1], 16))) in ascii_set + "+-=()":
            mapping.setdefault(base, chr(cp))
    add_style(id, name, mapping, "Unicode 上下标字母不完整；缺失字母保留原文")

for id, name, mark in (("acute", "拉丁锐音", "\u0301"), ("grave", "拉丁重音", "\u0300"), ("macron", "拉丁长音", "\u0304"), ("diaeresis", "拉丁分音", "\u0308"), ("caron", "拉丁抑扬 / 拼音声调", "\u030c")):
    add_style(id, name, {c: c + mark for c in ascii_set[:52]}, "组合附加符号，不进行发音或语言翻译")

write_gzip("character-styles.json.gz", json.dumps(styles, ensure_ascii=False, separators=(",", ":")))
license_text = fetch("https://www.unicode.org/license.txt")
(DEST / "UNICODE-LICENSE.txt").write_bytes(license_text.replace("\r\n", "\n").encode("utf-8"))
(DEST / "sources.json").write_bytes((json.dumps({"Unicode": "17.0.0", "CLDR": "48", "Entries": len(entries), "Styles": len(styles), "SHA256": sources}, indent=2) + "\n").encode("utf-8"))
print(f"Built {len(entries)} characters/sequences, {len(styles)} styles; compressed data {sum(p.stat().st_size for p in DEST.glob('*.gz')):,} bytes.")

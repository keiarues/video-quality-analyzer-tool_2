"""フレーム品質グラフと差分ヒートマップのアプリアイコンを生成する。"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "VideoQualityAnalyzer.App" / "Assets"
SIZE = 1024


def scale(value):
    return round(value * SIZE / 256)


def box(*values):
    return tuple(scale(value) for value in values)


def points(values):
    return [tuple(map(scale, pair)) for pair in values]


image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(box(8, 8, 248, 248), radius=scale(48), fill="#17202D")

# 添付の品質グラフに合わせ、左は暗いグラフ面と水色の線にする。
chart = Image.new("RGBA", (SIZE, SIZE), "#182330")
chart_draw = ImageDraw.Draw(chart)
chart_draw.rectangle(box(128, 37, 226, 219), fill="#3A0808")

# ヒートマップ側は赤い地に橙色の広がりを置き、線の周囲を発光させる。
heat = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
heat_draw = ImageDraw.Draw(heat)
heat_draw.ellipse(box(130, 66, 204, 153), fill=(205, 34, 4, 170))
heat_draw.ellipse(box(165, 116, 243, 219), fill=(226, 62, 5, 165))
heat_draw.ellipse(box(119, 147, 178, 211), fill=(173, 29, 2, 135))
heat = heat.filter(ImageFilter.GaussianBlur(scale(13)))
chart.alpha_composite(heat)
chart_draw = ImageDraw.Draw(chart)

for y in (84, 129, 174):
    chart_draw.line(points([(35, y), (222, y)]), fill="#344353", width=scale(2))
chart_draw.line(points([(39, 46), (39, 209), (218, 209)]), fill="#A5BBCB", width=scale(4), joint="curve")

# 実グラフの細かな鋭い変動を、太く少数の山と谷へ簡略化する。
graph = [
    (43, 82), (52, 71), (59, 153), (70, 149), (79, 92),
    (91, 101), (103, 91), (115, 115), (128, 104),
    (140, 120), (150, 93), (163, 109), (174, 75),
    (185, 142), (196, 95), (207, 102), (217, 134),
]
left = [pair for pair in graph if pair[0] <= 128]
right = [pair for pair in graph if pair[0] >= 128]
chart_draw.line(points(left), fill="#91C9FF", width=scale(11), joint="curve")

glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
glow_draw = ImageDraw.Draw(glow)
glow_draw.line(points(right), fill=(255, 67, 0, 245), width=scale(24), joint="curve")
chart.alpha_composite(glow.filter(ImageFilter.GaussianBlur(scale(8))))
chart_draw = ImageDraw.Draw(chart)
chart_draw.line(points(right), fill="#F06412", width=scale(16), joint="curve")
chart_draw.line(points(right), fill="#FFE35C", width=scale(9), joint="curve")

# グラフの左右を同じ位置で切り替え、ワイプの境界を明確にする。
mask = Image.new("L", (SIZE, SIZE), 0)
mask_draw = ImageDraw.Draw(mask)
mask_draw.rounded_rectangle(box(27, 32, 229, 224), radius=scale(24), fill=255)
image.paste(chart, (0, 0), mask)
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(box(27, 32, 229, 224), radius=scale(24), outline="#B6C9D8", width=scale(5))
draw.line(points([(128, 35), (128, 221)]), fill="#EDF5FA", width=scale(5))

ASSETS.mkdir(parents=True, exist_ok=True)
png_path = ASSETS / "AppIcon.png"
ico_path = ASSETS / "AppIcon.ico"
image.resize((256, 256), Image.Resampling.LANCZOS).save(png_path)
image.save(ico_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(png_path)
print(ico_path)

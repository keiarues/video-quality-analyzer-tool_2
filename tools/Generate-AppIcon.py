"""アプリのワイプ比較アイコンを PNG と ICO に描画する。"""

from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "VideoQualityAnalyzer.App" / "Assets"
SIZE = 1024


def scale(value):
    return round(value * SIZE / 256)


def box(*values):
    return tuple(scale(value) for value in values)


image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)

# 外枠は Windows の明暗どちらの背景でも輪郭が見える配色にする。
draw.rounded_rectangle(box(8, 8, 248, 248), radius=scale(48), fill="#172638")
draw.rounded_rectangle(box(20, 20, 236, 236), radius=scale(38), fill="#20364B")

# 左右は同じ映像領域。左は通常映像、右は差分ヒートマップ。
viewport_mask = Image.new("L", (SIZE, SIZE), 0)
mask_draw = ImageDraw.Draw(viewport_mask)
mask_draw.rounded_rectangle(box(32, 49, 224, 207), radius=scale(18), fill=255)

scene = Image.new("RGBA", (SIZE, SIZE), "#12314B")
scene_draw = ImageDraw.Draw(scene)
scene_draw.rectangle(box(32, 49, 128, 207), fill="#75BFE3")
scene_draw.ellipse(box(55, 68, 91, 104), fill="#FFF0B0")
scene_draw.polygon([tuple(map(scale, pair)) for pair in [(32, 169), (77, 111), (128, 166), (128, 207), (32, 207)]], fill="#2D8196")
scene_draw.polygon([tuple(map(scale, pair)) for pair in [(32, 188), (72, 146), (105, 179), (128, 159), (128, 207), (32, 207)]], fill="#226476")

# 色の島は小さいサイズでも残るよう、大きな形だけを使う。
scene_draw.polygon([tuple(map(scale, pair)) for pair in [(128, 166), (166, 108), (224, 156), (224, 207), (128, 207)]], fill="#176C9C")
scene_draw.ellipse(box(143, 71, 211, 132), fill="#26C7DD")
scene_draw.ellipse(box(154, 79, 207, 127), fill="#F4C653")
scene_draw.ellipse(box(164, 86, 201, 120), fill="#F26640")
scene_draw.ellipse(box(180, 92, 197, 111), fill="#E8394F")
scene_draw.ellipse(box(139, 149, 213, 197), fill="#28B4D5")
scene_draw.ellipse(box(156, 157, 204, 191), fill="#F4C653")
scene_draw.ellipse(box(171, 164, 196, 183), fill="#ED6540")
image.paste(scene, (0, 0), viewport_mask)

draw = ImageDraw.Draw(image)
draw.rounded_rectangle(box(32, 49, 224, 207), radius=scale(18), outline="#D8E7F1", width=scale(5))
draw.line([tuple(map(scale, pair)) for pair in [(128, 52), (128, 204)]], fill="#F8FBFF", width=scale(7))
draw.ellipse(box(119, 119, 137, 137), fill="#F8FBFF")

ASSETS.mkdir(parents=True, exist_ok=True)
png_path = ASSETS / "AppIcon.png"
ico_path = ASSETS / "AppIcon.ico"
image.resize((256, 256), Image.Resampling.LANCZOS).save(png_path)
image.save(ico_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(png_path)
print(ico_path)

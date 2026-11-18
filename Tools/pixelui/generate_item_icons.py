# 분류 · 장착 칸 아이콘 생성기 — generate_icons.py 의 도형·스타일 도구를 그대로 쓴다.
# (아이템 아이콘은 pixel_items.py 가 그린다 — 여기 아이템 모양은 분류·칸 실루엣의 바탕으로만 쓴다)
#
# 사용: python generate_item_icons.py --install          ← 게임 아이콘 폴더에 저장(분류·칸만)
#       python generate_item_icons.py <미리보기 png>       ← 확인용 시트만
#
# 분류 배지·탭(TypeIcon_*)과 빈 장착 칸(SlotIcon_*)은 플랫 스타일, 배경은 투명.

import importlib.util
import math
import os
import sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('gi', os.path.join(HERE, 'generate_icons.py'))
gi = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gi)

u = gi.u
c = gi.c
new = gi.new
mix = gi.mix
shade = gi.shade
T = gi.T

ICON_ROOT = gi.ICON_ROOT

def d_of(im):
	return ImageDraw.Draw(im)


def P(x, y):
	return (u(x), u(y))


def poly(im, pts, color):
	d_of(im).polygon([P(x, y) for x, y in pts], fill=color)


def ellipse(im, x0, y0, x1, y1, color):
	d_of(im).ellipse([u(x0), u(y0), u(x1), u(y1)], fill=color)


def box(im, x0, y0, x1, y1, color):
	d_of(im).rectangle([u(x0), u(y0), u(x1), u(y1)], fill=color)


def rbox(im, x0, y0, x1, y1, r, color):
	d_of(im).rounded_rectangle([u(x0), u(y0), u(x1), u(y1)], radius=u(r), fill=color)


# ── 장비 모양 ──────────────────────────────────────────

def helmet_steel():
	im = new()
	ellipse(im, 6, 4, 26, 24, gi.STEEL)
	box(im, 6, 14, 26, 26, gi.STEEL)
	box(im, 9, 15, 23, 17.5, gi.INK if gi.INK[3] else T)
	box(im, 15, 15, 17, 24, gi.STEEL)
	box(im, 14.5, 2, 17.5, 9, gi.RED)
	return im


def armor_steel():
	im = new()
	poly(im, [(8, 5), (13, 7), (19, 7), (24, 5), (29, 10), (25, 14), (24, 27), (8, 27), (7, 14), (3, 10)], gi.STEEL)
	poly(im, [(13, 7), (16, 11), (19, 7)], gi.INK if gi.INK[3] else T)
	box(im, 8, 21, 24, 23, gi.GOLD)
	return im


def glove(color, cuff):
	im = new()
	rbox(im, 9, 9, 23, 22, 3, color)
	for i in range(4):
		x = 9.5 + i * 3.5
		rbox(im, x, 4 + (1 if i in (0, 3) else 0), x + 3, 12, 1.4, color)
	poly(im, [(9, 12), (5, 11), (4, 15), (9, 18)], color)
	box(im, 8, 21, 24, 28, cuff)
	return im


def boot(color, sole):
	im = new()
	poly(im, [(9, 3), (19, 3), (19, 18), (28, 21), (28, 26), (9, 26)], color)
	box(im, 9, 25, 28, 28, sole)
	box(im, 9, 6, 19, 8, shade(color, 0.35))
	return im


def ring_item(gem):
	im = new()
	d = d_of(im)
	d.ellipse([u(6), u(10), u(26), u(29)], outline=gi.GOLD, width=int(u(3.2)))
	poly(im, [(16, 2), (22, 8), (16, 14), (10, 8)], gem)
	poly(im, [(16, 2), (19, 5), (16, 8), (13, 5)], mix(gem, (255, 255, 255, 255), 0.55))
	return im


def amulet_item(gem):
	im = new()
	d = d_of(im)
	d.line([P(5, 2), P(16, 17)], fill=gi.GOLD, width=int(u(1.6)))
	d.line([P(27, 2), P(16, 17)], fill=gi.GOLD, width=int(u(1.6)))
	ellipse(im, 9, 13, 23, 28, gi.GOLD)
	ellipse(im, 11.5, 15.5, 20.5, 25.5, gem)
	ellipse(im, 12.5, 16.5, 15.5, 19.5, mix(gem, (255, 255, 255, 255), 0.6))
	return im


def silhouette_sword():
	return gi.recolor(gi.great_sword(16, 16, 0.78, 45), gi.CREAM)


def silhouette_relic():
	im = new()
	poly(im, [(16, 2), (24, 10), (21, 22), (11, 22), (8, 10)], gi.CREAM)
	poly(im, [(7, 25), (25, 25), (22, 30), (10, 30)], gi.CREAM)
	return im


def silhouette_potion():
	im = new()
	box(im, 12.5, 3, 19.5, 6, gi.CREAM)
	box(im, 13.5, 6, 18.5, 11, gi.CREAM)
	ellipse(im, 6, 9, 26, 29, gi.CREAM)
	return im


def cream(im):
	return gi.recolor(im, gi.CREAM)


# ── 목록 ──────────────────────────────────────────────

def type_icons():
	return [
		('TypeIcon_Weapon', '무기', silhouette_sword()),
		('TypeIcon_Armor', '방어구', cream(armor_steel())),
		('TypeIcon_Accessory', '장신구', cream(ring_item(gi.CREAM))),
		('TypeIcon_Relic', '유물', silhouette_relic()),
		('TypeIcon_Consumable', '소모품', silhouette_potion()),
	]


def slot_icons():
	return [
		('SlotIcon_Weapon', '무기', silhouette_sword()),
		('SlotIcon_Helmet', '투구', cream(helmet_steel())),
		('SlotIcon_Armor', '갑옷', cream(armor_steel())),
		('SlotIcon_Gloves', '장갑', cream(glove(gi.CREAM, gi.CREAM))),
		('SlotIcon_Boots', '신발', cream(boot(gi.CREAM, gi.CREAM))),
		('SlotIcon_Ring', '반지', cream(ring_item(gi.CREAM))),
		('SlotIcon_Amulet', '목걸이', cream(amulet_item(gi.CREAM))),
		('SlotIcon_Relic', '유물', silhouette_relic()),
	]


def render(style, shapes):
	gi.apply_style(style)
	out = []
	for name, label, im in shapes():
		out.append((name, label, gi.finalize(gi.dress(im, True, None))))
	return out


def render_all():
	types = render('flat', type_icons)
	slots = render('flat', slot_icons)
	return types, slots


def install():
	types, slots = render_all()
	for name, label, im in types:
		im.save(os.path.join(ICON_ROOT, 'ItemType', name + '.Png'))
	for name, label, im in slots:
		im.save(os.path.join(ICON_ROOT, 'ItemType', name + '.png'))
	print('installed')


def preview(path):
	types, slots = render_all()
	# 분류·칸 아이콘은 실제처럼 색 바탕 위에 얹어 본다
	badge = []
	for name, label, im in types + slots:
		bg = Image.new('RGBA', im.size, c('#3570C8'))
		bg.alpha_composite(im)
		badge.append((label, bg))
	gi.preview(badge, path)


def main():
	if sys.argv[1] == '--install':
		install()
		return
	preview(sys.argv[1])


if __name__ == '__main__':
	main()

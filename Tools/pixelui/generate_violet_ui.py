# 바이올렛 테마 부품 생성기 — 툴킷 전환 전 UGUI 탭 색(어두운 보라 / 선택 바이올렛)을 기반으로 한 깔끔형 테마.
# 탭·버튼은 Layer Lab GUI Pro-FantasyHero 의 TabMenu 프리펩(BottomFlush_01·BottomFlush_02·Middle_01)과
# 같은 흰 스프라이트를 같은 배치·색으로 겹쳐 한 장으로 굽는다.
#   내비 탭      = TabMenu_BottomFlush_01 (Nomal / Select)
#   분류 탭      = TabMenu_BottomFlush_02 (Nomal / Select)
#   일반 버튼    = TabMenu_Middle_01 (Nomal), 위험 버튼은 같은 모양에 붉은 색
# 구운 버튼은 쓰는 높이 그대로라 USS 에서는 좌우만 9-slice(30px, 배율 1px)로 늘린다.
# 출력: ProjectOne/Assets/Project/UIToolkit/Violet/*.png (Violet.uss·NavigationBar.uss 가 쓴다)
#   slot·nav_bg = Point 필터 4px 배율 / 나머지 = Bilinear 필터
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, r'ProjectOne\Assets\Project\UIToolkit\Violet')
SPRITES = os.path.join(ROOT, r'ProjectOne\Assets\ThirdParty\Layer Lab\GUI Pro-FantasyHero\ResourcesData\Sptites\Components\Button')
PICTO = os.path.join(ROOT, 'ProjectOne', 'Assets', 'ThirdParty', 'Layer Lab', '2D Icons - Fantasy Picto Pack', 'Icon', 'PNG', '512x512')

# 스프라이트 9-slice 테두리 (왼, 아래, 오른, 위) — Unity Sprite.border 그대로
BORDERS = {
	'Tab_BottomFlush_01_White_Bg': (29, 5, 29, 25),
	'Tab_BottomFlush_01_White_Light': (25, 0, 25, 0),
	'Tab_BottomFlush_01_White_InnerShadow': (7, 13, 7, 17),
	'Tab_BottomFlush_01_White_Border': (29, 4, 29, 26),
	'Tab_BottomFlush_02_White_Bg': (30, 3, 29, 27),
	'Tab_BottomFlush_02_White_InnerBorder': (30, 3, 29, 26),
	'Tab_BottomFlush_02_White_Border': (29, 4, 30, 26),
	'Tab_Middle_01_White_Bg': (30, 30, 29, 30),
	'Tab_Middle_01_White_InnerBorder': (25, 26, 26, 26),
	'Tab_Middle_01_White_Border': (30, 30, 29, 30),
}

BAKE_WIDTH = 128		# 좌우 30px 를 9-slice 로 늘려 쓴다
FACE = (0x23, 0x1D, 0x36)


def hexc(s):
	s = s.lstrip('#')
	return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255)


def mix(a, b, t):
	return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def save(img, name):
	img.save(os.path.join(OUT, name + '.png'))
	print('  ' + name)


def load(name):
	return Image.open(os.path.join(SPRITES, name + '.png')).convert('RGBA')


# 흰 스프라이트에 색을 곱한다 (UGUI Image.color 와 같다)
def tint(img, color):
	r, g, b, a = img.split()
	r = r.point(lambda v: v * color[0] // 255)
	g = g.point(lambda v: v * color[1] // 255)
	b = b.point(lambda v: v * color[2] // 255)
	a = a.point(lambda v: v * color[3] // 255)
	return Image.merge('RGBA', (r, g, b, a))


# UGUI Sliced 그리기 — 모서리는 원래 크기(corner 배), 변·가운데는 늘린다.
# corner 를 줄이면 모서리 조각이 작아져 깎인 모서리가 덜 깎인다
def sliced(src, w, h, border, corner=1.0):
	sl, sbt, sr, st = border
	sw, sh = src.size
	l, bt, r, t = [int(round(v * corner)) for v in border]
	l = min(l, w // 2)
	r = min(r, w - l)
	t = min(t, h // 2)
	bt = min(bt, h - t)
	out = Image.new('RGBA', (w, h))
	xs = [(0, sl, 0, l), (sl, sw - sr, l, w - r), (sw - sr, sw, w - r, w)]
	ys = [(0, st, 0, t), (st, sh - sbt, t, h - bt), (sh - sbt, sh, h - bt, h)]
	for (sx0, sx1, dx0, dx1) in xs:
		for (sy0, sy1, dy0, dy1) in ys:
			if dx1 <= dx0 or dy1 <= dy0:
				continue

			# 가운데 폭이 0 인 스프라이트 — Unity 처럼 경계 1px 을 늘린다
			if sx1 <= sx0:
				sx0, sx1 = max(0, sx0 - 1), max(1, sx0)
			if sy1 <= sy0:
				sy0, sy1 = max(0, sy0 - 1), max(1, sy0)
			piece = src.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.BILINEAR)
			out.alpha_composite(piece, (dx0, dy0))
	return out


# 층 하나 — 배치는 프리펩 RectTransform 값 그대로.
#   ('stretch', 폭증감, 높이증감, x, y) : 사방 늘림
#   ('top', 폭증감, 높이, x, y)        : 위에 붙고 좌우 늘림
#   ('fixed', 폭, 높이, x, y)          : 위 가운데에 고정 크기
def layer(canvas, sprite, color, place, simple=False, corner=1.0):
	W, H = canvas.size
	kind, a, b, px, py = place
	if kind == 'stretch':
		w, h = W + a, H + b
		cx, cy = W / 2 + px, H / 2 - py
	elif kind == 'top':
		w, h = W + a, b
		cx, cy = W / 2 + px, -py
	else:
		w, h = a, b
		cx, cy = W / 2 + px, -py
	w, h = int(round(w)), int(round(h))
	if w <= 0 or h <= 0:
		return
	src = tint(load(sprite), hexc(color))
	img = src.resize((w, h), Image.BILINEAR) if simple else sliced(src, w, h, BORDERS[sprite], corner)
	canvas.alpha_composite(img, (int(round(cx - w / 2)), int(round(cy - h / 2))))


# 내비 탭 — TabMenu_BottomFlush_01
def nav_tab(name, height, bg, light, shadow):
	c = Image.new('RGBA', (BAKE_WIDTH, height))
	layer(c, 'Tab_BottomFlush_01_White_Bg', bg, ('stretch', -0.13, -0.61, 0, 0))
	layer(c, 'Tab_BottomFlush_01_White_Light', light, ('top', -8.19, 20, 0, -14.07))
	layer(c, 'Tab_BottomFlush_01_White_InnerShadow', shadow, ('stretch', -8.19, -17.19, 0, -8.29))
	layer(c, 'Tab_BottomFlush_01_White_Border', '#151428', ('stretch', -0.13, -0.61, 0, 0))
	save(c, name)


# 분류 탭 — TabMenu_BottomFlush_02 (위쪽 파인 장식 Deco 는 빼고 윗변을 일자로 둔다)
def flush_tab(name, height, bg, inner, glow):
	c = Image.new('RGBA', (BAKE_WIDTH, height))
	layer(c, 'Tab_BottomFlush_02_White_Bg', bg, ('stretch', 0, 0, 0, 0))
	layer(c, 'Tab_BottomFlush_02_White_InnerBorder', inner, ('stretch', -2, -1, 0, -0.5))
	if glow:
		layer(c, 'Tab_BottomFlush_02_White_Glow', glow, ('stretch', -40, -1.86, 0, 0), simple=True)
	layer(c, 'Tab_BottomFlush_02_White_Border', '#151428', ('stretch', 0, 0, 0, 0))
	save(c, name)


# 일반 버튼 — TabMenu_Middle_01 (위쪽 파인 장식 Deco 는 빼고 윗변을 일자로 둔다)
def middle(name, height, bg, inner, border='#151428', glow=None, corner=1.0):
	c = Image.new('RGBA', (BAKE_WIDTH, height))
	layer(c, 'Tab_Middle_01_White_Bg', bg, ('stretch', 0.23, 0, 0, 0), corner=corner)
	layer(c, 'Tab_Middle_01_White_InnerBorder', inner, ('stretch', -6, -6, 0, 0), corner=corner)
	if glow:
		layer(c, 'Tab_Middle_01_White_Glow', glow, ('stretch', -46.24, -10, 0, 0.15), simple=True)
	layer(c, 'Tab_Middle_01_White_Border', border, ('stretch', 0.23, 0, 0, 0), corner=corner)
	save(c, name)


# 아이템 칸 — 두꺼운 틀(12x12, slice 4, 4px 배율 = 16px), 네 모서리는 45도로 깎는다.
# 회색조로 그려 View 의 등급 틴트(곱하기)로 칠해진다.
#   깊이 0 = 진한 바깥선 / 1 = 틀 빛(왼위 밝게, 오른아래 어둡게) / 2 = 틀 / 3 = 안쪽 그늘선
#   안쪽은 등급 바탕색 판(item-cell__bg)이 덮는다
CHAMFER = 3


def slot():
	n = 12
	img = Image.new('RGBA', (n, n))
	for y in range(n):
		for x in range(n):
			edge = min(x, y, n - 1 - x, n - 1 - y)
			diag = min(x + y, (n - 1 - x) + y, x + (n - 1 - y), (n - 1 - x) + (n - 1 - y)) - CHAMFER
			d = min(edge, diag)
			if d < 0:
				continue
			if d == 0:
				c = 36
			elif d == 1:
				c = 255 if x + y < n - 1 else 150
			elif d == 2:
				c = 205
			elif d == 3:
				c = 60
			else:
				c = 34
			img.putpixel((x, y), (c, c, c, 255))
	save(img, 'slot')


# 내비 바 — 윗선 + 아래로 어두워지는 면 (slice-top 1)
def nav_bg():
	w, h = 8, 40
	img = Image.new('RGBA', (w, h))
	for y in range(h):
		c = (0x5A, 0x4A, 0x80) if y == 0 else mix((0x2C, 0x25, 0x48), (0x1A, 0x15, 0x2E), (y - 1) / (h - 2))
		for x in range(w):
			img.putpixel((x, y), c + (255,))
	save(img, 'nav_bg')


# 제목 판 물들임 — 왼쪽 바이올렛(#7E58E0)이 오른쪽으로 갈수록 투명해져 판 바탕(#322958)에 녹아든다(S 곡선).
# 맨 윗줄은 밝은 띠. 판(.header__frame) 왼쪽에 가로로 늘려 깐다(USS 폭 900px)
def header_wash():
	w, h = 256, 26
	img = Image.new('RGBA', (w, h))
	for x in range(w):
		t = x / (w - 1)
		t = t * t * (3 - 2 * t)
		a = int(255 * (1 - t))
		for y in range(h):
			c = (0x8F, 0x8F, 0xFF) if y == 0 else (0x7E, 0x58, 0xE0)
			img.putpixel((x, y), c + (a,))
	save(img, 'header_wash')


# 제목 판 아래 그림자 — 위가 진하고 아래로 사라지는 검은 띠(가로로 늘려 쓴다)
def header_shadow():
	w, h = 4, 24
	img = Image.new('RGBA', (w, h))
	for y in range(h):
		a = int(150 * (1 - y / h) ** 2)
		for x in range(w):
			img.putpixel((x, y), (0, 0, 0, a))
	save(img, 'header_shadow')


# 흰 둥근 빛 — 색은 USS 틴트(-unity-background-image-tint-color)로 입힌다(캐릭터 뒤 빛을 배경색에 맞추기 위해)
def glow_white():
	size = 128
	img = Image.new('RGBA', (size, size))
	c = (size - 1) / 2
	for y in range(size):
		for x in range(size):
			d = (((x - c) ** 2 + (y - c) ** 2) ** 0.5) / c
			a = max(0.0, 1 - d)
			img.putpixel((x, y), (255, 255, 255, int(110 * a * a)))
	save(img, 'glow_white')


# 아이콘을 내용 영역 기준으로 같은 크기에 맞추는 도구 (분류 탭·기능 버튼 아이콘 공용)
# 모두 내용 영역을 잘라 같은 크기(ICON_FILL)로 맞춰 128 캔버스 가운데에 둔다 → 탭에서 크기·위치가 일정하다.
ICON_SIZE = 128
ICON_FILL = 104


def fit_icon(img):
	box = img.getbbox()
	img = img.crop(box)
	w, h = img.size
	k = ICON_FILL / max(w, h)
	img = img.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)
	out = Image.new('RGBA', (ICON_SIZE, ICON_SIZE))
	out.alpha_composite(img, ((ICON_SIZE - img.size[0]) // 2, (ICON_SIZE - img.size[1]) // 2))
	return out


def picto(name):
	return Image.open(os.path.join(PICTO, 'btn_icon_' + name + '.png')).convert('RGBA')


# 기능 버튼(능력치·재화·코스튬·펫) 아이콘 — Fantasy Picto Pack 흰 실루엣, 같은 크기로 맞춘다
def extra_icons():
	icons = {
		'stat': picto('strength'),
		'currency': picto('money'),
		'costume': picto('hat'),
		'pet': picto('paw'),
	}
	for key in icons:
		save(fit_icon(icons[key]), 'extraicon_' + key)


# 분류 탭 아이콘 — Layer Lab GUI Pro-FantasyHero 의 Icon_Gear_* 결(진한 외곽선 + 연보라 3톤 면).
# 무기·방어구·장신구는 Icon_Gear_Sword/Armor/Ring 을 그대로 쓰고,
# 유물(다이아몬드 + 룬 소용돌이)·소모품(묶은 자루)은 같은 결로 그린다(512 로 그려 fit_icon 으로 맞춘다).
GEAR = os.path.join(ROOT, 'ProjectOne', 'Assets', 'ThirdParty', 'Layer Lab', 'GUI Pro-FantasyHero', 'ResourcesData', 'Sptites', 'Demo', 'Demo_IconMisc')
GEAR_LINE = (30, 28, 54, 255)
GEAR_HI = (240, 238, 255, 255)
GEAR_MID = (204, 197, 233, 255)
GEAR_SHADE = (130, 125, 182, 255)
GEAR_OUTLINE = 26		# 512 기준 외곽선 두께


def gear(name):
	return Image.open(os.path.join(GEAR, name + '.png')).convert('RGBA')


# 모양 마스크(L) → 진한 외곽선을 두른 면 이미지. faces 는 (마스크, 색) 목록으로 면 위에 차례로 칠한다
def gear_style(shape, faces, lines=None):
	from PIL import ImageFilter, ImageChops
	outline = shape
	for _ in range(GEAR_OUTLINE // 4):
		outline = outline.filter(ImageFilter.MaxFilter(9))
	img = Image.new('RGBA', shape.size)
	img.paste(GEAR_LINE, (0, 0), outline)
	img.paste(GEAR_MID, (0, 0), shape)
	for (mask, color) in faces:
		img.paste(color, (0, 0), ImageChops.multiply(mask, shape))
	if lines is not None:
		img.paste(GEAR_LINE, (0, 0), ImageChops.multiply(lines, shape))
	return img


def _mask():
	from PIL import ImageDraw
	m = Image.new('L', (512, 512), 0)
	return m, ImageDraw.Draw(m)


# 유물 — 보석처럼 깎인 다이아몬드(왼위 밝음·오른아래 그늘) 안에 사각 소용돌이 룬을 판다
def pict_relic():
	shape, d = _mask()
	top, right, bottom, left, c = (256, 40), (472, 256), (256, 472), (40, 256), (256, 256)
	d.polygon([top, right, bottom, left], fill=255)
	hi, dh = _mask()
	dh.polygon([left, top, c], fill=255)
	shade, ds = _mask()
	ds.polygon([right, bottom, c], fill=255)
	rune, dr = _mask()
	# 사각 소용돌이 — 똑바로 그린 뒤 45도 돌린다
	dr.line([(176, 176), (336, 176), (336, 336), (206, 336), (206, 236), (286, 236), (286, 296), (246, 296)], fill=255, width=28, joint='curve')
	rune = rune.rotate(45, resample=Image.BICUBIC)
	return gear_style(shape, [(hi, GEAR_HI), (shade, GEAR_SHADE)], rune)


# 소모품 — 묶은 자루(둥근 몸통 + 매듭 + 위 주름). 몸통 오른쪽 아래는 그늘, 왼쪽 위에 빛
def pict_sack():
	shape, d = _mask()
	d.ellipse([90, 196, 422, 476], fill=255)
	d.rectangle([206, 150, 306, 210], fill=255)
	d.polygon([(166, 54), (256, 104), (346, 54), (322, 156), (190, 156)], fill=255)
	shade, ds = _mask()
	ds.ellipse([90, 196, 422, 476], fill=255)
	ds.ellipse([60, 170, 380, 440], fill=0)
	hi, dh = _mask()
	dh.ellipse([150, 250, 230, 320], fill=255)
	tie, dt = _mask()
	dt.rectangle([180, 150, 332, 176], fill=255)
	return gear_style(shape, [(shade, GEAR_SHADE), (hi, GEAR_HI)], tie)


def tab_icons():
	icons = {
		'weapon': gear('Icon_Gear_Sword'),
		'armor': gear('Icon_Gear_Armor'),
		'accessory': gear('Icon_Gear_Ring'),
		'relic': pict_relic(),
		'consumable': pict_sack(),
	}
	for key in icons:
		save(fit_icon(icons[key]), 'tabicon_' + key)


# 아이템 칸 분류 배지 아이콘 — 흰색 단순 실루엣(분류 탭 아이콘과 별개). 무기는 하나, 방어구·장신구는 장착 부위별.
# ItemTypeIcons 가 TypeIcon_<부위> 이름으로 Atlas_Icon 에서 찾는다(아이콘 폴더 전체가 아틀라스에 묶여 새 파일도 자동 포함).
TYPE_ICONS = os.path.join(ROOT, 'ProjectOne', 'Assets', 'Project', 'Art', 'UI', 'Atlas', 'Textures', 'Icons', 'ItemType')


def _white():
	from PIL import ImageDraw
	img = Image.new('RGBA', (512, 512))
	return img, ImageDraw.Draw(img)


W_ON = (255, 255, 255, 255)
W_OFF = (0, 0, 0, 0)


def type_weapon():
	img, d = _white()
	d.polygon([(226, 330), (226, 90), (256, 40), (286, 90), (286, 330)], fill=W_ON)
	d.rectangle([166, 330, 346, 370], fill=W_ON)
	d.rectangle([236, 370, 276, 440], fill=W_ON)
	d.ellipse([226, 434, 286, 494], fill=W_ON)
	return img.rotate(-45, resample=Image.BICUBIC)


def type_helmet():
	img, d = _white()
	d.pieslice([96, 70, 416, 390], 180, 360, fill=W_ON)
	d.rectangle([96, 228, 416, 400], fill=W_ON)
	d.polygon([(96, 400), (190, 400), (190, 450), (96, 420)], fill=W_ON)
	d.polygon([(416, 400), (322, 400), (322, 450), (416, 420)], fill=W_ON)
	# 눈구멍 T 자
	d.rectangle([150, 250, 362, 290], fill=W_OFF)
	d.rectangle([236, 250, 276, 400], fill=W_OFF)
	return img


def type_armor():
	img, d = _white()
	d.polygon([(70, 170), (170, 100), (342, 100), (442, 170), (400, 260), (370, 240), (370, 430), (142, 430), (142, 240), (112, 260)], fill=W_ON)
	d.polygon([(206, 100), (306, 100), (256, 176)], fill=W_OFF)
	d.rectangle([142, 290, 370, 306], fill=W_OFF)
	d.rectangle([142, 356, 370, 372], fill=W_OFF)
	d.rectangle([248, 176, 264, 290], fill=W_OFF)
	return img


def type_gloves():
	img, d = _white()
	# 손가락 + 손바닥 + 엄지 + 소매
	d.rounded_rectangle([150, 60, 380, 330], radius=60, fill=W_ON)
	d.rounded_rectangle([60, 190, 200, 290], radius=48, fill=W_ON)
	d.rectangle([150, 330, 380, 360], fill=W_OFF)
	d.rectangle([140, 370, 390, 460], fill=W_ON)
	d.rectangle([206, 60, 222, 180], fill=W_OFF)
	d.rectangle([266, 60, 282, 180], fill=W_OFF)
	d.rectangle([326, 60, 342, 180], fill=W_OFF)
	return img


def type_boots():
	img, d = _white()
	d.rectangle([150, 60, 300, 300], fill=W_ON)
	d.rounded_rectangle([150, 260, 450, 420], radius=60, fill=W_ON)
	d.rectangle([150, 380, 300, 420], fill=W_ON)
	d.rectangle([130, 430, 460, 470], fill=W_ON)
	d.rectangle([150, 120, 300, 140], fill=W_OFF)
	return img


def type_ring():
	img, d = _white()
	d.ellipse([110, 170, 402, 462], fill=W_ON)
	d.ellipse([170, 230, 342, 402], fill=W_OFF)
	d.polygon([(256, 60), (326, 130), (256, 200), (186, 130)], fill=W_ON)
	return img


def type_amulet():
	img, d = _white()
	# 사슬 V + 큰 펜던트(다이아몬드)
	d.line([(130, 70), (256, 230)], fill=W_ON, width=48)
	d.line([(382, 70), (256, 230)], fill=W_ON, width=48)
	d.polygon([(256, 200), (386, 330), (256, 460), (126, 330)], fill=W_ON)
	d.polygon([(256, 280), (306, 330), (256, 380), (206, 330)], fill=W_OFF)
	return img


def type_relic():
	img, d = _white()
	d.rectangle([116, 116, 396, 396], fill=W_ON)
	d.rectangle([156, 156, 356, 356], fill=W_OFF)
	d.line([(196, 196), (316, 196), (316, 316), (226, 316), (226, 256), (276, 256)], fill=W_ON, width=34, joint='curve')
	return img.rotate(45, resample=Image.BICUBIC)


def type_consumable():
	img, d = _white()
	d.ellipse([96, 190, 416, 470], fill=W_ON)
	d.rectangle([206, 150, 306, 200], fill=W_ON)
	d.polygon([(176, 60), (256, 110), (336, 60), (316, 150), (196, 150)], fill=W_ON)
	d.rectangle([196, 150, 316, 168], fill=W_OFF)
	return img


def type_icons():
	icons = {
		'Weapon': type_weapon(),
		'Helmet': type_helmet(),
		'Armor': type_armor(),
		'Gloves': type_gloves(),
		'Boots': type_boots(),
		'Ring': type_ring(),
		'Amulet': type_amulet(),
		'Relic': type_relic(),
		'Consumable': type_consumable(),
	}
	for key in icons:
		fit_icon(icons[key]).save(os.path.join(TYPE_ICONS, 'TypeIcon_' + key + '.Png'), format='PNG')
		print('  TypeIcon_' + key)


def main():
	os.makedirs(OUT, exist_ok=True)
	print('Violet UI -> ' + OUT)
	slot()
	nav_bg()
	header_wash()
	header_shadow()
	glow_white()
	# 내비 탭 — 152px(바 윗선이 보이게 160 보다 8 낮게), 선택은 색만 바뀐다
	nav_tab('navtab_off', 152, '#3B304E', '#565076', '#332B47')
	nav_tab('navtab_on', 152, '#7550D2', '#8F8FFF', '#593DA1')
	# 분류 탭 — 92px
	flush_tab('tab_off', 92, '#2D2344', '#392E53', None)
	flush_tab('tab_on', 92, '#6D5ACD', '#8D78E1', '#9A7EF4')
	# 일반 버튼 — 기능 버튼·툴바 80px
	middle('btn_face', 80, '#392E53', '#483B65')
	tab_icons()
	type_icons()
	extra_icons()
	# 기능 버튼 — 어떤 버튼인지 색으로 구분(능력치 파랑·재화 호박·코스튬 자홍·펫 초록), 80px.
	# 모서리는 절반만 깎아 더 네모나게
	middle('btn_stat', 80, '#2E57A8', '#4A78D2', corner=0.5)
	middle('btn_currency', 80, '#A06A1E', '#CC9234', corner=0.5)
	middle('btn_costume', 80, '#963A84', '#C05AAA', corner=0.5)
	middle('btn_pet', 80, '#2F8A52', '#4CB474', corner=0.5)
	middle('btn_danger', 80, '#9A2E36', '#C04A4E', '#2A0A0E')
	# 켜진 토글 버튼(보관함 열림) — 일반 버튼 모양에 초록
	middle('btn_active', 80, '#2F8A52', '#4CB474')


if __name__ == '__main__':
	main()

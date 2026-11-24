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


# 내비 탭 — TabMenu_BottomFlush_01. 깎인 위 모서리는 네모로 채워 이웃 탭과 틈 없이 붙인다
def nav_tab(name, height, bg, light, shadow):
	c = Image.new('RGBA', (BAKE_WIDTH, height))
	layer(c, 'Tab_BottomFlush_01_White_Bg', bg, ('stretch', -0.13, -0.61, 0, 0))
	layer(c, 'Tab_BottomFlush_01_White_Light', light, ('top', -8.19, 20, 0, -14.07))
	layer(c, 'Tab_BottomFlush_01_White_InnerShadow', shadow, ('stretch', -8.19, -17.19, 0, -8.29))
	layer(c, 'Tab_BottomFlush_01_White_Border', '#151428', ('stretch', -0.13, -0.61, 0, 0))
	square_top_corners(c, 4, '#151428')
	save(c, name)


# 위 두 모서리의 깎인 부분을 네모로 채운다 — 테두리(굵기 thick)는 그대로 두르고, 안쪽은 모서리 바로 옆 열을 이어 칠한다
def square_top_corners(c, thick, border):
	W, H = c.size
	px = c.load()
	bc = hexc(border)
	cw = 0
	while cw < W // 2 and px[cw, 0][3] == 0:
		cw += 1
	cw += thick		# 깎인 대각선 테두리까지 덮는다
	for y in range(min(cw, H)):
		for x in range(cw):
			for (tx, sx) in ((x, cw), (W - 1 - x, W - 1 - cw)):
				if y < thick or x < thick:
					px[tx, y] = bc
				else:
					px[tx, y] = px[sx, y]


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


# ── 둥근 아이템 칸 — 두툼한 회색조 틀(등급 테두리색으로 틴트)과 등급 바탕 그라데이션(등급 바탕색으로 틴트) ──
# 틀은 아랫변이 더 두꺼운 블록(위 10px · 옆 11px · 아래 15px)이라 칸이 살짝 솟아 보인다.
# 회색 값 = 틴트에 곱해질 밝기: 바깥 테두리 어둡게, 몸통은 위가 밝고 아래가 어둡게, 안쪽 선은 어둡게.
# 96px 로 굽고 USS 에서 9-slice(32px, 배율 1px)로 늘린다. 4배로 그려 줄여 가장자리를 부드럽게 한다.
SLOT_SIZE = 96
SLOT_RADIUS = 24
SLOT_INSET = (11, 10, 11, 15)	# 왼, 위, 오른, 아래 — 안쪽(바탕 판) 경계
SS = 4


def rr_depth(x, y, l, t, r, b, rad):
	# 둥근 사각형 안쪽 깊이(양수 = 안). 경계까지의 거리
	cx = min(max(x, l + rad), r - rad)
	cy = min(max(y, t + rad), b - rad)
	if (x < l + rad or x > r - rad) and (y < t + rad or y > b - rad):
		return rad - ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
	return min(x - l, r - x, y - t, b - y)


def slot_round():
	n = SLOT_SIZE * SS
	img = Image.new('RGBA', (n, n))
	px = img.load()
	il, it, ir, ib = [v * SS for v in SLOT_INSET]
	rad = SLOT_RADIUS * SS
	irad = (SLOT_RADIUS - 12) * SS
	for y in range(n):
		for x in range(n):
			fx, fy = x + 0.5, y + 0.5
			d = rr_depth(fx, fy, 0, 0, n, n, rad)
			if d <= 0:
				continue
			di = rr_depth(fx, fy, il, it, n - ir, n - ib, irad)
			if di > 2 * SS:
				continue		# 안쪽은 비운다(바탕 판이 채운다)
			if di > 0:
				v = 0.30		# 안쪽 선
			elif d < 3 * SS:
				v = 0.22		# 바깥 테두리
			else:
				v = 1.0 - 0.42 * (fy / n)	# 몸통 — 위 밝게, 아래 어둡게
				# 위·아래 변에만 — 9-slice 로 늘어나는 옆변 가운데(32~64px)는 건드리지 않는다
				if d < 6 * SS and fy < n / 3:
					v = 1.0		# 위쪽 빛 테두리
				if di > -2 * SS and fy > n * 2 / 3:
					v *= 0.85	# 아래쪽 안쪽 그늘
			g = int(round(255 * v))
			px[x, y] = (g, g, g, 255)
	save(img.resize((SLOT_SIZE, SLOT_SIZE), Image.LANCZOS), 'slot_round')


# 등급 바탕 — 가운데가 가장 밝고 바깥으로 은은하게 어두워지는 둥근 판(흰색 → 62%)
def cell_glow():
	size = 128
	n = size * SS
	img = Image.new('RGBA', (n, n))
	px = img.load()
	rad = 12 * SS
	half = n / 2
	maxr = (2 ** 0.5) * half
	for y in range(n):
		for x in range(n):
			fx, fy = x + 0.5, y + 0.5
			if rr_depth(fx, fy, 0, 0, n, n, rad) <= 0:
				continue
			r = (((fx - half) ** 2 + (fy - half) ** 2) ** 0.5) / maxr
			v = 1.0 - 0.38 * (r ** 1.4)
			g = int(round(255 * v))
			px[x, y] = (g, g, g, 255)
	save(img.resize((size, size), Image.LANCZOS), 'cell_glow')


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


# 장비 화면 능력치·재화 뱃지 아이콘 + 메뉴 코스튬·펫 아이콘 — Fantasy Picto Pack 흰 실루엣, 같은 크기로 맞춘다
def extra_icons():
	icons = {
		'extraicon_stat': picto('strength'),
		'extraicon_currency': picto('money'),
		'menuicon_costume': picto('hat'),
		'menuicon_pet': picto('paw'),
	}
	for key in icons:
		save(fit_icon(icons[key]), key)


# 분류 탭 아이콘 — Layer Lab GUI Pro-FantasyHero 의 Icon_Gear_* 결(진한 외곽선 + 연보라 3톤 면).
# 무기·방어구·장신구는 Icon_Gear_Sword/Armor/Ring 을 그대로 쓰고,
# 유물(다이아몬드 + 룬 소용돌이)·소모품(묶은 자루)은 같은 결로 그린다(512 로 그려 fit_icon 으로 맞춘다).
GEAR = os.path.join(ROOT, 'ProjectOne', 'Assets', 'ThirdParty', 'Layer Lab', 'GUI Pro-FantasyHero', 'ResourcesData', 'Sptites', 'Demo', 'Demo_IconMisc')
GEAR_LINE = (30, 28, 54, 255)
GEAR_HI = (240, 238, 255, 255)
GEAR_MID = (204, 197, 233, 255)
GEAR_SHADE = (130, 125, 182, 255)
GEAR_OUTLINE = 26		# 512 기준 외곽선 두께


# 닫기 X — Layer Lab Icon_Close_02(끝이 둥근 굵은 크림색 X)를 52px 로 줄이고, 밝은 빨강 버튼 위에서도 읽히게
# 진한 외곽선(3px) + 아래 그림자(4px)를 두른다. 그림자 몫까지 포함해 58x62 로 굽는다(아이콘이 버튼 가운데에 오도록 USS 가 크기를 맞춘다).
def close_icon():
	size, line, drop = 52, 3, 4
	src = Image.open(os.path.join(GEAR, 'Icon_Close_02.png')).convert('RGBA').resize((size, size), Image.LANCZOS)
	alpha = src.split()[3]
	w, h = size + line * 2, size + line * 2 + drop
	out = Image.new('RGBA', (w, h))
	dark = Image.new('RGBA', (size, size), (10, 8, 20, 255))
	for dy in range(-line, line + drop + 1):
		for dx in range(-line, line + 1):
			if dx * dx + min(dy, line) ** 2 > line * line + 1:
				continue
			out.paste(dark, (line + dx, line + dy), alpha)
	out.alpha_composite(src, (line, line))
	save(out, 'icon_close')


# 잠금·장착 표시 — 분류 배지와 같은 어두운 둥근 사각 배지(40px) 안에 기호를 넣는다.
# 잠금은 빨간 자물쇠(PictoIcon_Lock, 몸통 윗줄 광택), 장착은 초록 E. 2배(80px)로 구워 USS 에서 40px 로 쓴다
PICTO_ICONS = os.path.join(ROOT, 'ProjectOne', 'Assets', 'ThirdParty', 'Layer Lab', 'GUI Pro-FantasyHero', 'ResourcesData', 'Sptites', 'Components', 'Icon_PictoIcons', '256')
PRETENDARD_EXTRABOLD = os.path.join(ROOT, 'ProjectOne', 'Assets', 'Project', 'Art', 'UI', 'Fonts', 'Pretendard', 'Pretendard-ExtraBold.ttf')
MARK_SCALE = 2
MARK_SIZE = 40 * MARK_SCALE


def mark_badge():
	from PIL import ImageDraw
	s = MARK_SCALE
	out = Image.new('RGBA', (MARK_SIZE, MARK_SIZE))
	d = ImageDraw.Draw(out)
	d.rounded_rectangle((0, 0, MARK_SIZE - 1, MARK_SIZE - 1), radius=9 * s, fill=(10, 8, 20, 255))
	d.rounded_rectangle((3 * s, 3 * s, MARK_SIZE - 3 * s - 1, MARK_SIZE - 3 * s - 1), radius=7 * s, fill=(42, 35, 64, 255))
	return out


def lock_icon():
	from PIL import ImageChops, ImageDraw
	src = Image.open(os.path.join(PICTO_ICONS, 'PictoIcon_Lock.Png')).convert('RGBA')
	src = src.crop(src.getbbox())
	h = 29 * MARK_SCALE
	w = round(h * src.width / src.height)
	alpha = src.resize((w, h), Image.LANCZOS).split()[3]
	glyph = Image.new('RGBA', (w, h), (240, 64, 72, 255))
	glyph.putalpha(alpha)
	shine = Image.new('RGBA', (w, h), (255, 140, 140, 255))
	band = Image.new('L', (w, h), 0)
	ImageDraw.Draw(band).rectangle((0, round(h * 0.45), w, round(h * 0.62)), fill=255)
	shine.putalpha(ImageChops.multiply(alpha, band))
	glyph.alpha_composite(shine)
	out = mark_badge()
	out.alpha_composite(glyph, ((MARK_SIZE - w) // 2, (MARK_SIZE - h) // 2))
	save(out, 'icon_lock')


def equip_icon():
	from PIL import ImageDraw, ImageFont
	out = mark_badge()
	d = ImageDraw.Draw(out)
	font = ImageFont.truetype(PRETENDARD_EXTRABOLD, 31 * MARK_SCALE)
	box = d.textbbox((0, 0), 'E', font=font)
	x = (MARK_SIZE - (box[2] - box[0])) / 2 - box[0]
	y = (MARK_SIZE - (box[3] - box[1])) / 2 - box[1]
	d.text((x, y), 'E', font=font, fill=(92, 222, 140, 255))
	save(out, 'icon_equip')


# 제작 화면 등록 칸 사이 화살표 — 흰 삼각형(어두운 테두리·아래 그림자). USS 가 모드 색으로 틴트한다. 4배로 그려 줄인다
def arrow_icon():
	from PIL import ImageDraw
	w, h, k = 64, 76, 4
	big = Image.new('RGBA', (w * k, h * k))
	d = ImageDraw.Draw(big)
	tri = [(6 * k, 4 * k), (6 * k, 66 * k), (58 * k, 35 * k)]
	d.polygon([(x, y + 6 * k) for x, y in tri], fill=(10, 8, 20, 255))
	d.polygon(tri, fill=(255, 255, 255, 255), outline=(10, 8, 20, 255), width=3 * k)
	save(big.resize((w, h), Image.LANCZOS), 'icon_arrow')


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


# 빈 장착 칸 부위 아이콘 — 분류 탭 아이콘(Icon_Gear 결)과 같은 모양을 '면만 한 색'으로 굽는다.
# 외곽선·안쪽 선 자리는 비워 칸 바닥이 비치게 해 무늬가 남는다. 흐림(투명도)은 USS(.equip-slot__placeholder)가 준다.
# 무기·갑옷·신발·반지는 Gear 스프라이트, 유물은 탭과 같은 pict_relic, 투구·장갑·목걸이는 같은 결로 그린다.
SLOT_ICON_COLOR = (160, 150, 215, 255)


def _gear_big(name):
	img = gear(name)
	img = img.crop(img.getbbox())
	k = 440 / max(img.size)
	img = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
	out = Image.new('RGBA', (512, 512))
	out.alpha_composite(img, ((512 - img.width) // 2, (512 - img.height) // 2))
	return out


def pict_helmet():
	shape, d = _mask()
	d.pieslice([86, 50, 426, 390], 180, 360, fill=255)
	d.rectangle([86, 220, 426, 400], fill=255)
	d.polygon([(86, 400), (200, 400), (200, 470), (86, 440)], fill=255)
	d.polygon([(426, 400), (312, 400), (312, 470), (426, 440)], fill=255)
	hi, dh = _mask()
	dh.pieslice([86, 50, 426, 390], 180, 250, fill=255)
	shade, ds = _mask()
	ds.polygon([(330, 220), (426, 220), (426, 440), (312, 470), (312, 400)], fill=255)
	lines, dl = _mask()
	dl.rectangle([140, 250, 372, 290], fill=255)
	dl.rectangle([236, 250, 276, 400], fill=255)
	return gear_style(shape, [(hi, GEAR_HI), (shade, GEAR_SHADE)], lines)


def pict_gloves():
	shape, d = _mask()
	d.rounded_rectangle([150, 50, 390, 330], radius=70, fill=255)
	d.rounded_rectangle([54, 180, 210, 290], radius=54, fill=255)
	d.rounded_rectangle([140, 340, 400, 470], radius=24, fill=255)
	hi, dh = _mask()
	dh.rounded_rectangle([170, 70, 250, 300], radius=40, fill=255)
	shade, ds = _mask()
	ds.rounded_rectangle([140, 340, 400, 470], radius=24, fill=255)
	ds.rectangle([320, 60, 390, 330], fill=255)
	lines, dl = _mask()
	dl.rectangle([226, 50, 244, 190], fill=255)
	dl.rectangle([296, 50, 314, 190], fill=255)
	dl.rectangle([140, 330, 400, 346], fill=255)
	return gear_style(shape, [(hi, GEAR_HI), (shade, GEAR_SHADE)], lines)


def pict_amulet():
	shape, d = _mask()
	d.line([(120, 50), (256, 230)], fill=255, width=44)
	d.line([(392, 50), (256, 230)], fill=255, width=44)
	d.ellipse([136, 200, 376, 440], fill=255)
	hi, dh = _mask()
	dh.pieslice([136, 200, 376, 440], 150, 300, fill=255)
	shade, ds = _mask()
	ds.pieslice([136, 200, 376, 440], 330, 120, fill=255)
	lines, dl = _mask()
	dl.ellipse([206, 270, 306, 370], outline=255, width=20)
	return gear_style(shape, [(hi, GEAR_HI), (shade, GEAR_SHADE)], lines)


def slot_icons():
	from PIL import ImageChops
	icons = {
		'weapon': _gear_big('Icon_Gear_Sword'),
		'helmet': pict_helmet(),
		'armor': _gear_big('Icon_Gear_Armor'),
		'gloves': pict_gloves(),
		'boots': _gear_big('Icon_Gear_Boots'),
		'ring': _gear_big('Icon_Gear_Ring'),
		'amulet': pict_amulet(),
		'relic': pict_relic(),
	}
	for key in icons:
		icon = icons[key]
		# 면 = 불투명하고 밝은 곳(외곽선·안쪽 선은 어두워 빠진다)
		alpha = icon.getchannel('A').point(lambda v: 255 if v > 128 else 0)
		bright = icon.convert('L').point(lambda v: 255 if v >= 95 else 0)
		flat = Image.new('RGBA', icon.size, SLOT_ICON_COLOR)
		flat.putalpha(ImageChops.multiply(alpha, bright))
		flat = flat.crop(flat.getbbox())
		k = ICON_SIZE / max(flat.size)
		flat = flat.resize((max(1, round(flat.width * k)), max(1, round(flat.height * k))), Image.LANCZOS)
		out = Image.new('RGBA', (ICON_SIZE, ICON_SIZE))
		out.alpha_composite(flat, ((ICON_SIZE - flat.width) // 2, (ICON_SIZE - flat.height) // 2))
		save(out, 'sloticon_' + key)


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
	slot_round()
	cell_glow()
	nav_bg()
	header_wash()
	header_shadow()
	glow_white()
	# 내비 탭 — 152px(바 윗선이 보이게 160 보다 8 낮게), 선택은 색만 바뀐다
	# 내비 탭 — 모서리를 깎지 않아 탭끼리 틈 없이 붙는다. 선택 탭은 바 높이(160px)에 딱 맞게 솟는다
	nav_tab('navtab_off', 152, '#3B304E', '#565076', '#332B47')
	nav_tab('navtab_on', 160, '#7550D2', '#8F8FFF', '#593DA1')
	# 분류 탭 — 92px
	flush_tab('tab_off', 92, '#2D2344', '#392E53', None)
	flush_tab('tab_on', 92, '#6D5ACD', '#8D78E1', '#9A7EF4')
	# 일반 버튼 — 기능 버튼·툴바 80px
	middle('btn_face', 80, '#392E53', '#483B65')
	tab_icons()
	slot_icons()
	type_icons()
	extra_icons()
	middle('btn_danger', 80, '#9A2E36', '#C04A4E', '#2A0A0E')
	# 켜진 토글 버튼(보관함 열림) — 일반 버튼 모양에 초록
	middle('btn_active', 80, '#2F8A52', '#4CB474')
	# 아이템 정보 팝업 — 큰 버튼 112px(장착 #53C596·강화 #A7C553), 창 밖 아래 보관함 이동 88px(#D45012), 닫기는 프레임 없는 Icon_Close_02 X.
	# 안쪽 테두리는 같은 색을 28% 밝게
	middle('btn_equip', 112, '#53C596', '#83D5B3')
	middle('btn_enchant', 112, '#A7C553', '#BFD583')
	middle('btn_stash', 88, '#D45012', '#E08154')
	# 공용 확인 팝업의 위험 확인(분해·삭제) — 장비 화면 분해 버튼(btn_danger)과 같은 색을 112px 로
	middle('btn_danger_large', 112, '#9A2E36', '#C04A4E', '#2A0A0E')
	# 제작 화면 실행 버튼 — 모드 색(강화 초록·승급 금·전이 파랑) 112px. 취소는 btn_danger_large
	middle('btn_active_large', 112, '#2F8A52', '#4CB474')
	middle('btn_gold_large', 112, '#C98A1C', '#E2AE4A')
	middle('btn_blue_large', 112, '#2E5FB8', '#4F82DC')
	middle('btn_face_large', 112, '#392E53', '#483B65')
	close_icon()
	lock_icon()
	equip_icon()
	arrow_icon()


if __name__ == '__main__':
	main()

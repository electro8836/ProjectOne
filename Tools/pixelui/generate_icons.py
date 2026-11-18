# 아이콘 생성기 — 무기(마스터리) 아이콘과 스킬 아이콘을 단순한 플랫 도형으로 그린다.
#
# 사용: python generate_icons.py --install                  ← 게임 아이콘 폴더에 도트 스타일로 저장
#       python generate_icons.py <출력 폴더> [미리보기 png 또는 -] [스타일]
#   스타일: flat(기본) · pixel(도트) · pastel(파스텔) · neon(네온) · line(라인)
#   모양은 모든 스타일이 같고, 색·배경·마감 처리만 스타일이 바꾼다.
#
# 원칙: 아이콘 하나에 큰 도형 1~2개 · 주색(크림) + 효과색 1개 · 외곽선 없음.
# 도형마다 오른쪽 아래 절반을 살짝 어둡게(면 분할 음영) 하고 아래로 부드러운 그림자를 깐다.
# 좌표·두께는 32 기준으로 적고 U(=16) 배 크기로 그린 뒤 128 로 줄여 가장자리를 매끈하게 한다.

import math
import os
import sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

U = 16
N = 32 * U
OUT_SIZE = 128
T = (0, 0, 0, 0)


def c(h, a=255):
	h = h.lstrip('#')
	return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def u(v):
	return v * U


# 스타일별 색·마감. post: None(그대로) · 'pixel'(32칸 도트로) · 'neon'(어두운 바탕 + 빛) · 'line'(선으로만)
FLAT_COLORS = {
	'CREAM': '#FFF6E6', 'RED': '#FF4D5E', 'PURPLE': '#B07CFF', 'GOLD': '#FFC94A', 'INK': '#241B2E',
	'STEEL': '#E8EEF6', 'GRIP': '#7A4E36', 'GUN': '#58627A', 'GUN_DARK': '#3C4458', 'WOOD': '#C98A4E', 'LENS': '#7FE0FF',
}
FLAT_BG = {'Active': ('#4A8AF0', '#2650A8'), 'Passive': ('#45B868', '#1F7038'), 'Ultimate': ('#F28A34', '#B04E12')}

STYLES = {
	'flat': {'label': '플랫', 'colors': FLAT_COLORS, 'bg': FLAT_BG, 'shade': 0.18, 'shadow': 0.45, 'post': None},
	'pixel': {'label': '도트', 'colors': FLAT_COLORS, 'bg': FLAT_BG, 'shade': 0.22, 'shadow': 0.0, 'post': 'pixel'},
	'pastel': {
		'label': '파스텔',
		'colors': {
			'CREAM': '#FFFFFF', 'RED': '#FF8FA3', 'PURPLE': '#C9A7FF', 'GOLD': '#FFD98A', 'INK': '#6B5A80',
			'STEEL': '#F4F7FC', 'GRIP': '#C79A80', 'GUN': '#A6B0C8', 'GUN_DARK': '#8A93AE', 'WOOD': '#E8B98A', 'LENS': '#B8F0FF',
		},
		'bg': {'Active': ('#A8D0FF', '#7FA8F0'), 'Passive': ('#AEE8C0', '#7CC898'), 'Ultimate': ('#FFCFA6', '#F2A276')},
		'shade': 0.08, 'shadow': 0.22, 'post': None,
	},
	'neon': {
		'label': '네온',
		'colors': {
			'CREAM': '#F4F8FF', 'RED': '#FF3D6E', 'PURPLE': '#B26BFF', 'GOLD': '#FFD23F', 'INK': '#0E1020',
			'STEEL': '#E6F2FF', 'GRIP': '#5A6A8A', 'GUN': '#4A5878', 'GUN_DARK': '#2E3852', 'WOOD': '#3E9CFF', 'LENS': '#7FFFF0',
		},
		'bg': {'Active': ('#2E8BFF', '#0B1026'), 'Passive': ('#25E07A', '#08180F'), 'Ultimate': ('#FF7A1F', '#1E0C04')},
		'shade': 0.12, 'shadow': 0.0, 'post': 'neon',
	},
	'line': {
		'label': '라인',
		'colors': {
			'CREAM': '#FFFFFF', 'RED': '#FFB0BA', 'PURPLE': '#E2CCFF', 'GOLD': '#FFE7A0', 'INK': 'clear',
			'STEEL': '#FFFFFF', 'GRIP': '#FFFFFF', 'GUN': '#FFFFFF', 'GUN_DARK': '#FFFFFF', 'WOOD': '#FFFFFF', 'LENS': '#FFFFFF',
		},
		'bg': FLAT_BG, 'shade': 0.0, 'shadow': 0.0, 'post': 'line',
	},
}

STYLE = None
CREAM = RED = PURPLE = GOLD = INK = STEEL = GRIP = GUN = GUN_DARK = WOOD = LENS = None
BG = {}


def apply_style(name):
	global STYLE, BG
	STYLE = STYLES[name]
	g = globals()
	for key, value in STYLE['colors'].items():
		# 'clear' 는 구멍(눈·코)을 뚫어 라인 스타일에서 선이 생기게 한다
		g[key] = T if value == 'clear' else c(value)
	BG = {}
	for key, (top, bottom) in STYLE['bg'].items():
		BG[key] = (c(top), c(bottom))


def new():
	return Image.new('RGBA', (N, N), T)


def mix(a, b, t):
	t = max(0.0, min(1.0, t))
	return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


# ── 면 분할 음영 · 그림자 ──────────────────────────────

def split_shade(layer, amount=0.18):
	# 도형 중심을 지나는 대각선(↗) 기준 오른쪽 아래 절반을 어둡게
	box = layer.getbbox()
	if box is None:
		return layer
	cx = (box[0] + box[2]) / 2.0
	cy = (box[1] + box[3]) / 2.0
	mask = Image.new('L', layer.size, 0)
	d = ImageDraw.Draw(mask)
	far = N * 2
	d.polygon([(cx - far, cy + far), (cx + far, cy - far), (cx + far, cy + far)], fill=int(255 * amount))
	dark = Image.new('RGBA', layer.size, (20, 10, 40, 0))
	dark.putalpha(ImageChops.multiply(mask, layer.split()[3]))
	out = layer.copy()
	out.alpha_composite(dark)
	return out


def drop_shadow(layer, dx=0.6, dy=1.2, blur=0.7, alpha=0.45):
	a = layer.split()[3]
	a = ImageChops.offset(a, int(u(dx)), int(u(dy))).filter(ImageFilter.GaussianBlur(u(blur)))
	a = a.point(lambda v: int(v * alpha))
	sh = Image.new('RGBA', layer.size, (10, 6, 24, 0))
	sh.putalpha(a)
	return sh


def compose(category, layers, shade=True):
	top, bottom = BG[category]
	if STYLE['post'] == 'neon':
		im = neon_background(top, bottom)
	else:
		im = background(top, bottom)
	for layer in layers:
		im.alpha_composite(dress(layer, shade, top))
	return im


def dress(layer, shade=True, glow_col=None):
	# 도형 하나에 스타일 마감을 입힌다
	post = STYLE['post']
	out = new()
	if post == 'line':
		out.alpha_composite(line_art(layer))
		return out
	if shade and STYLE['shade'] > 0:
		layer = split_shade(layer, STYLE['shade'])
	if post == 'neon' and glow_col is not None:
		out.alpha_composite(soft_glow(layer, glow_col))
	if post == 'pixel':
		out.alpha_composite(outline(layer, U, (20, 12, 30, 255)))
	if STYLE['shadow'] > 0:
		out.alpha_composite(drop_shadow(layer, alpha=STYLE['shadow']))
	out.alpha_composite(layer)
	return out


def hard_alpha(layer):
	return layer.split()[3].point(lambda v: 255 if v > 100 else 0)


def outline(layer, width, color):
	ring = hard_alpha(layer).filter(ImageFilter.MaxFilter(int(width) * 2 + 1))
	out = Image.new('RGBA', layer.size, color[:3] + (0,))
	out.putalpha(ring)
	return out


def line_art(layer):
	# 채움은 옅게, 가장자리만 굵은 선으로
	a = hard_alpha(layer)
	w = int(U * 1.1)
	inner = a.filter(ImageFilter.MinFilter(w * 2 + 1))
	edge = ImageChops.subtract(a, inner).filter(ImageFilter.GaussianBlur(U * 0.08))
	out = Image.new('RGBA', layer.size, T)
	fill = Image.new('RGBA', layer.size, (255, 255, 255, 0))
	fill.putalpha(a.point(lambda v: int(v * 0.16)))
	out.alpha_composite(fill)
	stroke = Image.new('RGBA', layer.size, layer_color(layer)[:3] + (0,))
	stroke.putalpha(edge)
	out.alpha_composite(stroke)
	return out


def layer_color(layer):
	small = layer.resize((24, 24), Image.BILINEAR)
	px = small.load()
	r = g = b = n = 0
	for y in range(24):
		for x in range(24):
			if px[x, y][3] > 200:
				r += px[x, y][0]
				g += px[x, y][1]
				b += px[x, y][2]
				n += 1
	if n == 0:
		return (255, 255, 255, 255)
	return (r // n, g // n, b // n, 255)


def soft_glow(layer, color):
	a = layer.split()[3].filter(ImageFilter.MaxFilter(int(U * 0.5) * 2 + 1)).filter(ImageFilter.GaussianBlur(U * 1.6))
	a = a.point(lambda v: min(255, int(v * 1.8)))
	g = Image.new('RGBA', layer.size, color[:3] + (0,))
	g.putalpha(a)
	return g


def neon_background(accent, dark):
	im = Image.new('RGBA', (N, N), dark)
	light = Image.new('RGBA', (N, N), accent[:3] + (0,))
	mask = Image.new('L', (N, N), 0)
	d = ImageDraw.Draw(mask)
	d.ellipse([u(2), u(2), u(30), u(30)], fill=70)
	light.putalpha(mask.filter(ImageFilter.GaussianBlur(u(4))))
	im.alpha_composite(light)
	# 안쪽 테두리 빛
	frame = Image.new('L', (N, N), 0)
	fd = ImageDraw.Draw(frame)
	fd.rectangle([u(0.6), u(0.6), N - u(0.6), N - u(0.6)], outline=255, width=int(u(0.7)))
	rim = Image.new('RGBA', (N, N), accent[:3] + (0,))
	rim.putalpha(frame.filter(ImageFilter.GaussianBlur(u(0.5))))
	im.alpha_composite(rim)
	return im


def finalize(im):
	# 512 → 128. 도트는 32칸으로 줄였다가 다시 키운다
	if STYLE['post'] == 'pixel':
		small = im.resize((32, 32), Image.BOX)
		px = small.load()
		for y in range(32):
			for x in range(32):
				r, g, b, a = px[x, y]
				px[x, y] = (r, g, b, 255 if a > 110 else 0)
		return small.resize((OUT_SIZE, OUT_SIZE), Image.NEAREST)
	return im.resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS)


def background(top, bottom):
	im = Image.new('RGBA', (N, N), bottom)
	px = im.load()
	for y in range(N):
		col = mix(top, bottom, y / (N - 1))
		for x in range(N):
			px[x, y] = col
	# 가운데 은은한 원 — 도형이 떠 보이게
	disc = Image.new('RGBA', (N, N), (255, 255, 255, 0))
	d = ImageDraw.Draw(disc)
	d.ellipse([u(4), u(3), u(28), u(27)], fill=(255, 255, 255, 34))
	im.alpha_composite(disc.filter(ImageFilter.GaussianBlur(u(1.5))))
	return im


# ── 축 좌표 도형 (s: 축 방향, p: 수직 — 45도 무기용) ─────

def to_xy(cx, cy, ang, s, p):
	a = math.radians(ang)
	x = cx + s * math.cos(a) + p * math.sin(a)
	y = cy - s * math.sin(a) + p * math.cos(a)
	return (u(x), u(y))


def clip_half(pts, keep_negative):
	# p<=0 (또는 p>=0) 반평면으로 다각형 자르기
	out = []
	n = len(pts)
	for i in range(n):
		a = pts[i]
		b = pts[(i + 1) % n]
		ina = a[1] <= 0 if keep_negative else a[1] >= 0
		inb = b[1] <= 0 if keep_negative else b[1] >= 0
		if ina:
			out.append(a)
		if ina != inb:
			t = a[1] / (a[1] - b[1])
			out.append((a[0] + (b[0] - a[0]) * t, 0.0))
	return out


def part(d, cx, cy, ang, pts, light, dark=None):
	# 축 기준 위쪽 절반은 light, 아래쪽 절반은 dark
	if dark is None:
		d.polygon([to_xy(cx, cy, ang, s, p) for s, p in pts], fill=light)
		return
	top = clip_half(pts, True)
	bottom = clip_half(pts, False)
	if len(bottom) >= 3:
		d.polygon([to_xy(cx, cy, ang, s, p) for s, p in bottom], fill=dark)
	if len(top) >= 3:
		d.polygon([to_xy(cx, cy, ang, s, p) for s, p in top], fill=light)


def circle_at(d, cx, cy, ang, s, p, r, color):
	x, y = to_xy(cx, cy, ang, s, p)
	d.ellipse([x - u(r), y - u(r), x + u(r), y + u(r)], fill=color)


def rect(s0, s1, p0, p1):
	return [(s0, p0), (s1, p0), (s1, p1), (s0, p1)]


def shade(col, t=0.22):
	return mix(col, (20, 10, 40, 255), t)


# ── 무기 (추상화된 실루엣) ─────────────────────────────

def great_sword(cx=16, cy=16, scale=1.0, ang=45, blade=None):
	blade = blade or STEEL
	im = new()
	d = ImageDraw.Draw(im)
	k = scale
	part(d, cx, cy, ang, rect(-15 * k, -9 * k, -1.5 * k, 1.5 * k), GRIP, shade(GRIP))
	circle_at(d, cx, cy, ang, -16 * k, 0, 2.4 * k, GOLD)
	part(d, cx, cy, ang, [(-7 * k, -5.2 * k), (13 * k, -5.2 * k), (17 * k, -1.2 * k), (17 * k, 1.2 * k), (13 * k, 5.2 * k), (-7 * k, 5.2 * k)], blade, shade(blade))
	part(d, cx, cy, ang, rect(-9.4 * k, -6.6 * k, -8.2 * k, 8.2 * k), GOLD, shade(GOLD))
	return im


def dagger(cx=16, cy=16, scale=1.0, ang=45, blade=None):
	blade = blade or STEEL
	im = new()
	d = ImageDraw.Draw(im)
	k = scale
	part(d, cx, cy, ang, rect(-12 * k, -5.5 * k, -1.2 * k, 1.2 * k), GRIP, shade(GRIP))
	circle_at(d, cx, cy, ang, -12.8 * k, 0, 1.8 * k, GOLD)
	part(d, cx, cy, ang, [(-4.5 * k, -2.5 * k), (6 * k, -2.8 * k), (14 * k, 0), (6 * k, 2.8 * k), (-4.5 * k, 2.5 * k)], blade, shade(blade))
	part(d, cx, cy, ang, rect(-5.8 * k, -4.2 * k, -4.4 * k, 4.4 * k), GOLD, shade(GOLD))
	return im


def shuriken(cx=16, cy=16, R=13, color=None, hole=True):
	color = color or STEEL
	im = new()
	d = ImageDraw.Draw(im)
	r = R * 0.26
	x = u(cx)
	y = u(cy)
	for i in range(4):
		ang = math.radians(-45 + i * 90)
		tip = (x + u(R) * math.cos(ang), y + u(R) * math.sin(ang))
		left = (x + u(r) * math.cos(ang - math.pi / 2), y + u(r) * math.sin(ang - math.pi / 2))
		right = (x + u(r) * math.cos(ang + math.pi / 2), y + u(r) * math.sin(ang + math.pi / 2))
		d.polygon([(x, y), left, tip], fill=color)
		d.polygon([(x, y), tip, right], fill=shade(color))
	if hole:
		h = u(R * 0.13)
		d.ellipse([x - h, y - h, x + h, y + h], fill=T)
	return im


def rifle(cx=16, cy=16, scale=1.0, ang=35):
	im = new()
	d = ImageDraw.Draw(im)
	k = scale
	part(d, cx, cy, ang, [(-17 * k, -1.6 * k), (-9 * k, -2.2 * k), (-9 * k, 2.4 * k), (-17 * k, 5 * k)], WOOD, shade(WOOD))
	part(d, cx, cy, ang, [(-5.5 * k, 2), (-2.5 * k, 2), (-3.6 * k, 6.8 * k), (-6.6 * k, 6.8 * k)], GUN_DARK)
	part(d, cx, cy, ang, rect(0.2 * k, 2.8 * k, 2 * k, 7.4 * k), GUN_DARK)
	part(d, cx, cy, ang, rect(4 * k, 17 * k, -0.9 * k, 0.9 * k), GUN, shade(GUN))
	part(d, cx, cy, ang, rect(4 * k, 10 * k, -1.9 * k, 1.9 * k), WOOD, shade(WOOD))
	part(d, cx, cy, ang, rect(-9 * k, 4.2 * k, -2.3 * k, 2.3 * k), GUN, shade(GUN))
	part(d, cx, cy, ang, rect(-6.5 * k, 3 * k, -5.8 * k, -3.4 * k), GUN_DARK)
	part(d, cx, cy, ang, rect(2.2 * k, 3.2 * k, -5.6 * k, -3.6 * k), LENS)
	return im


# ── 효과 도형 (좌표는 32 기준) ──────────────────────────

def crescent(cx, cy, r, thick, deg0, deg1, color):
	# 베기 궤적 — 가운데 두껍고 양 끝이 뾰족한 초승달
	im = new()
	px = im.load()
	x0 = u(cx)
	y0 = u(cy)
	ro = u(r)
	w = u(thick)
	total = (deg1 - deg0) % 360
	for y in range(N):
		for x in range(N):
			dist = math.hypot(x - x0, y - y0)
			if dist > ro or dist < ro - w:
				continue
			span = (math.degrees(math.atan2(y - y0, x - x0)) - deg0) % 360
			if span > total:
				continue
			if ro - w * math.sin(span / total * math.pi) <= dist:
				px[x, y] = color
	return im


def slash(x0, y0, x1, y1, w, color):
	# 끝이 뾰족한 렌즈형 베기 자국
	im = new()
	d = ImageDraw.Draw(im)
	mx = (x0 + x1) / 2.0
	my = (y0 + y1) / 2.0
	L = math.hypot(x1 - x0, y1 - y0)
	nx = -(y1 - y0) / L
	ny = (x1 - x0) / L
	d.polygon([(u(x0), u(y0)), (u(mx + nx * w), u(my + ny * w)), (u(x1), u(y1)), (u(mx - nx * w), u(my - ny * w))], fill=color)
	return im


def drop(cx, cy, s, color=RED, highlight=True):
	im = new()
	d = ImageDraw.Draw(im)
	x = u(cx)
	y = u(cy)
	r = u(s)
	d.ellipse([x - r, y - r, x + r, y + r], fill=color)
	d.polygon([(x - r * 0.97, y - r * 0.25), (x, y - r * 2.7), (x + r * 0.97, y - r * 0.25)], fill=color)
	if highlight:
		d.ellipse([x - r * 0.6, y - r * 0.45, x - r * 0.2, y + r * 0.05], fill=(255, 255, 255, 200))
	return im


def burst(cx, cy, ro, ri, n, color, rot=0):
	im = new()
	d = ImageDraw.Draw(im)
	pts = []
	for i in range(n * 2):
		r = ro if i % 2 == 0 else ri
		a = math.radians(rot + i * 180.0 / n)
		pts.append((u(cx) + u(r) * math.cos(a), u(cy) + u(r) * math.sin(a)))
	d.polygon(pts, fill=color)
	return im


def rays(cx, cy, r0, r1, n, w, color, rot=0):
	# 방사형 빛살 — 끝이 뾰족한 쐐기
	im = new()
	d = ImageDraw.Draw(im)
	for i in range(n):
		a = math.radians(rot + i * 360.0 / n)
		ca = math.cos(a)
		sa = math.sin(a)
		px = -sa
		py = ca
		base = (cx + ca * r0, cy + sa * r0)
		tip = (cx + ca * r1, cy + sa * r1)
		d.polygon([(u(base[0] + px * w), u(base[1] + py * w)), (u(tip[0]), u(tip[1])), (u(base[0] - px * w), u(base[1] - py * w))], fill=color)
	return im


def ring(cx, cy, r, w, color):
	im = new()
	d = ImageDraw.Draw(im)
	d.ellipse([u(cx - r), u(cy - r), u(cx + r), u(cy + r)], outline=color, width=int(u(w)))
	return im


def crosshair(cx, cy, r, color, accent):
	im = ring(cx, cy, r, 2.2, color)
	d = ImageDraw.Draw(im)
	for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
		a = (cx + dx * (r - 3.2), cy + dy * (r - 3.2))
		b = (cx + dx * (r + 3.2), cy + dy * (r + 3.2))
		d.line([(u(a[0]), u(a[1])), (u(b[0]), u(b[1]))], fill=color, width=int(u(2.2)))
	d.ellipse([u(cx - 2.6), u(cy - 2.6), u(cx + 2.6), u(cy + 2.6)], fill=accent)
	return im


def arrow_up(cx, top, w, h, color):
	im = new()
	d = ImageDraw.Draw(im)
	d.polygon([(u(cx - w), u(top + w)), (u(cx), u(top)), (u(cx + w), u(top + w))], fill=color)
	d.rectangle([u(cx - w * 0.45), u(top + w - 0.1), u(cx + w * 0.45), u(top + h)], fill=color)
	return im


def speed_lines(x_end, y0, rows, color):
	im = new()
	d = ImageDraw.Draw(im)
	for dy, length in rows:
		y = y0 + dy
		d.polygon([(u(x_end - length), u(y)), (u(x_end), u(y - 1.1)), (u(x_end), u(y + 1.1))], fill=color)
	return im


def skull(cx, cy, s, color, hole=None):
	hole = hole or INK
	im = new()
	d = ImageDraw.Draw(im)
	x = u(cx)
	y = u(cy)
	r = u(s)
	d.rounded_rectangle([x - r * 0.6, y + r * 0.2, x + r * 0.6, y + r * 1.15], radius=r * 0.22, fill=color)
	d.ellipse([x - r, y - r, x + r, y + r * 0.72], fill=color)
	for ex in (-1, 1):
		d.ellipse([x + ex * r * 0.42 - r * 0.3, y - r * 0.1, x + ex * r * 0.42 + r * 0.3, y + r * 0.42], fill=hole)
	d.polygon([(x - r * 0.13, y + r * 0.62), (x + r * 0.13, y + r * 0.62), (x, y + r * 0.42)], fill=hole)
	for tx in (-0.22, 0.22):
		d.rectangle([x + tx * r - r * 0.05, y + r * 0.8, x + tx * r + r * 0.05, y + r * 1.15], fill=hole)
	return im


def curved_arrow(cx, cy, r, w, color):
	# 위로 돌아 내려오는 반격 화살
	im = crescent(cx, cy, r, w, 180, 10, color)
	d = ImageDraw.Draw(im)
	tip_a = math.radians(10)
	bx = cx + (r - w * 0.5) * math.cos(tip_a)
	by = cy + (r - w * 0.5) * math.sin(tip_a)
	d.polygon([(u(bx - 4.2), u(by - 1)), (u(bx + 4.2), u(by - 1)), (u(bx), u(by + 6))], fill=color)
	return im


def flower(cx, cy, r, color, center):
	im = new()
	d = ImageDraw.Draw(im)
	for i in range(6):
		a = math.radians(i * 60 - 90)
		px = cx + r * 0.58 * math.cos(a)
		py = cy + r * 0.58 * math.sin(a)
		pr = r * 0.46
		d.ellipse([u(px - pr), u(py - pr), u(px + pr), u(py + pr)], fill=color)
	d.ellipse([u(cx - r * 0.3), u(cy - r * 0.3), u(cx + r * 0.3), u(cy + r * 0.3)], fill=center)
	return im


def recolor(layer, color):
	out = Image.new('RGBA', layer.size, color[:3] + (0,))
	out.putalpha(layer.split()[3])
	return out


def shifted(layer, dx, dy):
	return ImageChops.offset(layer, int(u(dx)), int(u(dy)))


# ── 단검 스킬 ──────────────────────────────────────────

def dagger_icons():
	icons = {}
	icons['Skill_Dagger_VitalCut'] = ('급소 베기', compose('Active', [
		crescent(15, 19, 12.5, 5.2, 200, 345, CREAM),
		drop(25, 24, 3.6, RED),
	]))
	icons['Skill_Dagger_Ambush'] = ('암습 일격', compose('Active', [
		recolor(shifted(dagger(17, 15, 0.9, 45), 3.2, 3.2), PURPLE),
		dagger(15, 13, 0.9, 45, CREAM),
	], shade=True))
	icons['Skill_Dagger_Agility'] = ('날렵함', compose('Passive', [
		speed_lines(13, 17, [(-4, 8), (1, 11), (6, 7)], CREAM),
		dagger(19, 15, 0.82, 45, CREAM),
	]))
	icons['Skill_Dagger_BloodGale'] = ('혈풍', compose('Active', [
		crescent(10, 22, 20, 5, 275, 15, RED),
		crescent(10, 22, 12.5, 4.2, 275, 15, CREAM),
	]))
	icons['Skill_Dagger_Finisher'] = ('마무리 일격', compose('Active', [
		slash(5, 5, 27, 27, 3.2, CREAM),
		slash(27, 5, 5, 27, 3.2, CREAM),
		burst(16, 16, 4.5, 2.2, 4, RED, 45),
	]))
	icons['Skill_Dagger_Counter'] = ('그림자 반격', compose('Active', [
		curved_arrow(16, 19, 11, 4.2, PURPLE),
		dagger(13, 19, 0.6, 45, CREAM),
	]))
	icons['Skill_Dagger_Precision'] = ('급소 연마', compose('Passive', [
		crosshair(16, 16, 9.5, CREAM, RED),
	]))
	icons['Skill_Dagger_BloodBurst'] = ('피의 폭발', compose('Active', [
		rays(16, 18, 8, 14.5, 8, 1.6, CREAM, 22.5),
		drop(16, 20, 5.2, RED),
	]))
	icons['Skill_Dagger_Rupture'] = ('급소 파열', compose('Active', [
		burst(16, 16, 14, 6.5, 8, CREAM, 0),
		ring(16, 16, 4.5, 1.8, RED),
	]))
	icons['Skill_Dagger_DeepWound'] = ('깊은 상처', compose('Passive', [
		slash(6, 26, 15, 5, 2.4, RED),
		slash(12, 27, 21, 6, 2.4, RED),
		slash(18, 28, 27, 7, 2.4, RED),
	]))
	icons['Skill_Dagger_OpenWound'] = ('상처 벌리기', compose('Active', [
		drop(13, 22, 6, RED),
		arrow_up(25, 6, 4, 13, CREAM),
	]))
	icons['Skill_Dagger_Executioner'] = ('처형자', compose('Passive', [
		skull(16, 15, 10, CREAM),
	]))
	icons['Skill_Dagger_ShadowBlade'] = ('그림자 칼날', compose('Ultimate', [
		speed_lines(13, 21, [(-4, 9), (1, 12), (6, 8)], PURPLE),
		dagger(19, 14, 0.9, 30, PURPLE),
	]))
	icons['Skill_Dagger_BloodBloom'] = ('혈화난무', compose('Ultimate', [
		crescent(16, 16, 15, 3.6, 300, 230, CREAM),
		flower(16, 16, 9, RED, GOLD),
	]))
	return icons


# ── 추가 도형 ──────────────────────────────────────────

def kunai(cx=16, cy=16, scale=1.0, ang=45, blade=None):
	blade = blade or STEEL
	im = new()
	d = ImageDraw.Draw(im)
	k = scale
	part(d, cx, cy, ang, rect(-11 * k, -3 * k, -1.1 * k, 1.1 * k), GRIP, shade(GRIP))
	x, y = to_xy(cx, cy, ang, -13 * k, 0)
	ro = u(2.4 * k)
	ri = u(1.2 * k)
	d.ellipse([x - ro, y - ro, x + ro, y + ro], fill=blade)
	d.ellipse([x - ri, y - ri, x + ri, y + ri], fill=T)
	part(d, cx, cy, ang, [(-3 * k, -1.3 * k), (2 * k, -3.4 * k), (13 * k, 0), (2 * k, 3.4 * k), (-3 * k, 1.3 * k)], blade, shade(blade))
	return im


def shield(cx, cy, s, color, rim=None):
	im = new()
	d = ImageDraw.Draw(im)

	def body(k):
		return [(u(cx - s * k), u(cy - s * 0.9 * k)), (u(cx + s * k), u(cy - s * 0.9 * k)), (u(cx + s * k), u(cy + s * 0.1 * k)),
			(u(cx), u(cy + s * 1.15 * k)), (u(cx - s * k), u(cy + s * 0.1 * k))]
	d.polygon(body(1.0), fill=rim or color)
	if rim is not None:
		d.polygon(body(0.72), fill=color)
	return im


def lightning(cx, cy, s, color):
	im = new()
	d = ImageDraw.Draw(im)
	pts = [(0.25, -1.0), (-0.45, 0.1), (0.0, 0.1), (-0.3, 1.0), (0.5, -0.2), (0.05, -0.2), (0.45, -1.0)]
	d.polygon([(u(cx + px * s), u(cy + py * s)) for px, py in pts], fill=color)
	return im


def shards(pieces, color):
	# pieces: 삼각형 꼭짓점 목록
	im = new()
	d = ImageDraw.Draw(im)
	for tri in pieces:
		d.polygon([(u(x), u(y)) for x, y in tri], fill=color)
	return im


def chevron(cx, cy, w, h, thick, color):
	im = new()
	d = ImageDraw.Draw(im)
	d.polygon([(u(cx - w), u(cy - h)), (u(cx - w + thick), u(cy - h)), (u(cx + thick), u(cy)), (u(cx - w + thick), u(cy + h)),
		(u(cx - w), u(cy + h)), (u(cx), u(cy))], fill=color)
	return im


def bullet(cx, cy, scale=1.0, ang=45, tip=None):
	tip = tip or STEEL
	im = new()
	d = ImageDraw.Draw(im)
	k = scale
	part(d, cx, cy, ang, rect(-6 * k, 1.5 * k, -2.3 * k, 2.3 * k), GOLD, shade(GOLD))
	part(d, cx, cy, ang, [(1.5 * k, -2.3 * k), (4.5 * k, -1.9 * k), (7 * k, 0), (4.5 * k, 1.9 * k), (1.5 * k, 2.3 * k)], tip, shade(tip))
	return im


def grenade(cx, cy, r):
	im = new()
	d = ImageDraw.Draw(im)
	d.ellipse([u(cx - r), u(cy - r * 0.95), u(cx + r), u(cy + r * 1.05)], fill=GUN)
	d.ellipse([u(cx - r * 0.75), u(cy - r * 0.7), u(cx + r * 0.2), u(cy + r * 0.25)], fill=mix(GUN, CREAM, 0.35))
	d.rectangle([u(cx - r * 0.35), u(cy - r * 1.35), u(cx + r * 0.35), u(cy - r * 0.8)], fill=GOLD)
	d.polygon([(u(cx + r * 0.2), u(cy - r * 1.3)), (u(cx + r * 1.0), u(cy - r * 0.9)), (u(cx + r * 0.9), u(cy - r * 0.6)), (u(cx + r * 0.2), u(cy - r * 0.95))], fill=GOLD)
	ro = u(r * 0.38)
	px = u(cx - r * 0.55)
	py = u(cy - r * 1.25)
	d.ellipse([px - ro, py - ro, px + ro, py + ro], outline=CREAM, width=int(u(0.8)))
	return im


def bomb(cx, cy, r, color=None):
	color = color or GUN
	im = new()
	d = ImageDraw.Draw(im)
	d.polygon([(u(cx - r * 0.9), u(cy - r * 1.9)), (u(cx + r * 0.9), u(cy - r * 1.9)), (u(cx + r * 0.4), u(cy - r * 0.8)), (u(cx - r * 0.4), u(cy - r * 0.8))], fill=RED)
	d.ellipse([u(cx - r * 0.75), u(cy - r * 1.3), u(cx + r * 0.75), u(cy + r)], fill=color)
	return im


def drone(cx, cy, s):
	im = new()
	d = ImageDraw.Draw(im)
	d.rectangle([u(cx - s * 1.3), u(cy - s * 0.12), u(cx + s * 1.3), u(cy + s * 0.12)], fill=GUN)
	for ex in (-1, 1):
		x = cx + ex * s * 1.3
		d.ellipse([u(x - s * 0.7), u(cy - s * 0.55), u(x + s * 0.7), u(cy - s * 0.25)], fill=CREAM)
		d.rectangle([u(x - s * 0.08), u(cy - s * 0.4), u(x + s * 0.08), u(cy)], fill=GUN)
	d.rounded_rectangle([u(cx - s * 0.75), u(cy - s * 0.45), u(cx + s * 0.75), u(cy + s * 0.55)], radius=u(s * 0.3), fill=CREAM)
	d.ellipse([u(cx - s * 0.3), u(cy - s * 0.15), u(cx + s * 0.3), u(cy + s * 0.45)], fill=RED)
	return im


def turret(cx, cy, s):
	im = new()
	d = ImageDraw.Draw(im)
	d.polygon([(u(cx - s), u(cy + s)), (u(cx + s), u(cy + s)), (u(cx + s * 0.6), u(cy + s * 0.3)), (u(cx - s * 0.6), u(cy + s * 0.3))], fill=GUN)
	d.rectangle([u(cx), u(cy - s * 0.35), u(cx + s * 1.25), u(cy - s * 0.05)], fill=GUN_DARK)
	d.ellipse([u(cx - s * 0.6), u(cy - s * 0.75), u(cx + s * 0.6), u(cy + s * 0.45)], fill=CREAM)
	d.ellipse([u(cx - s * 0.2), u(cy - s * 0.35), u(cx + s * 0.2), u(cy + s * 0.05)], fill=RED)
	return im


def eye(cx, cy, w, h, color, iris):
	im = new()
	d = ImageDraw.Draw(im)
	pts = []
	for i in range(41):
		t = i / 40.0
		x = cx - w + 2 * w * t
		pts.append((u(x), u(cy - h * math.sin(t * math.pi))))
	for i in range(41):
		t = 1 - i / 40.0
		x = cx - w + 2 * w * t
		pts.append((u(x), u(cy + h * math.sin(t * math.pi))))
	d.polygon(pts, fill=color)
	r = h * 0.75
	d.ellipse([u(cx - r), u(cy - r), u(cx + r), u(cy + r)], fill=iris)
	r2 = h * 0.3
	d.ellipse([u(cx - r2), u(cy - r2), u(cx + r2), u(cy + r2)], fill=INK if INK[3] > 0 else T)
	return im


def bullseye(cx, cy, r):
	im = new()
	d = ImageDraw.Draw(im)
	cols = [RED, CREAM, RED, CREAM]
	for i, col in enumerate(cols):
		rr = r * (1 - i * 0.24)
		d.ellipse([u(cx - rr), u(cy - rr), u(cx + rr), u(cy + rr)], fill=col)
	return im


def link(x0, y0, x1, y1, r, color):
	# 두 점을 잇는 연결 — 노드 원 2개 + 선
	im = new()
	d = ImageDraw.Draw(im)
	d.line([(u(x0), u(y0)), (u(x1), u(y1))], fill=color, width=int(u(1.6)))
	for x, y in ((x0, y0), (x1, y1)):
		d.ellipse([u(x - r), u(y - r), u(x + r), u(y + r)], fill=color)
	return im


# ── 대검 스킬 ──────────────────────────────────────────

def great_sword_icons():
	icons = {}
	icons['Skill_GreatSword_Smash'] = ('거검 낙하', compose('Active', [
		burst(16, 26, 9, 3.5, 6, GOLD, -90),
		great_sword(16, 12, 0.62, -90, CREAM),
	]))
	icons['Skill_GreatSword_ExtraSwing'] = ('추가 평타', compose('Active', [
		crescent(14, 18, 13, 4.5, 200, 340, CREAM),
		crescent(18, 22, 9, 3.5, 200, 340, GOLD),
	]))
	icons['Skill_GreatSword_Brawn'] = ('괴력', compose('Passive', [
		great_sword(13, 18, 0.6, 45, CREAM),
		arrow_up(25, 4, 4, 14, GOLD),
	]))
	icons['Skill_GreatSword_Whirlwind'] = ('선풍참', compose('Active', [
		crescent(16, 16, 13, 4, 0, 100, CREAM),
		crescent(16, 16, 13, 4, 120, 220, CREAM),
		crescent(16, 16, 13, 4, 240, 340, CREAM),
		ring(16, 16, 3, 1.6, GOLD),
	]))
	icons['Skill_GreatSword_HeavyBlow'] = ('강타', compose('Active', [
		burst(23, 22, 8.5, 3.5, 7, GOLD, 10),
		great_sword(13, 13, 0.6, -45, CREAM),
	]))
	icons['Skill_GreatSword_Shatter'] = ('파쇄', compose('Active', [
		shards([[(8, 8), (15, 6), (13, 14)], [(20, 5), (27, 9), (19, 13)], [(6, 19), (13, 16), (10, 25)],
			[(21, 17), (28, 21), (22, 26)], [(14, 22), (19, 20), (17, 28)]], CREAM),
		burst(16, 16, 4.5, 2, 5, RED, 0),
	]))
	icons['Skill_GreatSword_Indomitable'] = ('불굴', compose('Passive', [
		shield(16, 15, 11, CREAM, GOLD),
	]))
	icons['Skill_GreatSword_Destroyer'] = ('파괴의 손', compose('Active', [
		burst(16, 16, 14.5, 6, 8, CREAM, 22.5),
		burst(16, 16, 7.5, 3.2, 8, RED, 0),
	]))
	icons['Skill_GreatSword_ShieldBash'] = ('막고 치기', compose('Active', [
		shield(13, 17, 9.5, CREAM, GOLD),
		burst(24, 9, 6, 2.5, 5, GOLD, -90),
	]))
	icons['Skill_GreatSword_Frenzy'] = ('광분', compose('Passive', [
		speed_lines(12, 17, [(-5, 8), (0, 10), (5, 7)], CREAM),
		lightning(20, 16, 12, RED),
	]))
	icons['Skill_GreatSword_Riposte'] = ('반격 베기', compose('Active', [
		curved_arrow(16, 19, 11, 4.2, CREAM),
		shield(13, 21, 5.5, GOLD),
	]))
	icons['Skill_GreatSword_Decisive'] = ('일도양단', compose('Passive', [
		ring(16, 16, 11, 3, CREAM),
		slash(4, 28, 28, 4, 2.6, RED),
	]))
	icons['Skill_GreatSword_GreatCleave'] = ('대참격', compose('Ultimate', [
		crescent(12, 20, 17, 6.5, 200, 350, CREAM),
		great_sword(20, 14, 0.5, 45, GOLD),
	]))
	icons['Skill_GreatSword_Rampage'] = ('폭주', compose('Ultimate', [
		rays(16, 16, 9, 15.5, 10, 1.8, RED, 0),
		great_sword(16, 16, 0.66, 45, CREAM),
	]))
	return icons


# ── 수리검 스킬 ────────────────────────────────────────

def shuriken_icons():
	icons = {}
	icons['Skill_Shuriken_Split'] = ('분열 수리검', compose('Active', [
		shuriken(10, 10, 7, CREAM),
		shuriken(22, 22, 7, CREAM),
		speed_lines(6, 23, [(-2, 5), (2, 4)], GOLD),
	]))
	icons['Skill_Shuriken_VitalKunai'] = ('급소 쿠나이', compose('Active', [
		crosshair(22, 10, 6.5, CREAM, RED),
		kunai(13, 19, 0.75, 45, CREAM),
	]))
	icons['Skill_Shuriken_QuickHands'] = ('속수', compose('Passive', [
		speed_lines(11, 17, [(-5, 8), (0, 10), (5, 7)], CREAM),
		shuriken(20, 16, 9.5, CREAM),
	]))
	icons['Skill_Shuriken_PierceVolley'] = ('관통 연사', compose('Active', [
		slash(1, 16, 31, 16, 1.4, GOLD),
		shuriken(7, 16, 6.5, CREAM),
		shuriken(16, 16, 6.5, CREAM),
		shuriken(25, 16, 6.5, CREAM),
	]))
	icons['Skill_Shuriken_BlastMark'] = ('폭렬 표식', compose('Active', [
		burst(16, 16, 14, 6, 8, GOLD, 0),
		shards([[(16, 9), (23, 16), (16, 23)], [(16, 9), (9, 16), (16, 23)]], RED),
	]))
	icons['Skill_Shuriken_Haste'] = ('가속', compose('Active', [
		chevron(11, 16, 6, 8, 3.2, CREAM),
		chevron(18, 16, 6, 8, 3.2, CREAM),
		chevron(25, 16, 6, 8, 3.2, GOLD),
	]))
	icons['Skill_Shuriken_KeenEdge'] = ('예리한 날', compose('Passive', [
		shuriken(15, 17, 11, CREAM),
		burst(25, 7, 5, 1.2, 4, GOLD, 0),
	]))
	pts = []
	for i in range(6):
		a = math.radians(i * 60 - 90)
		pts.append(shuriken(16 + 10 * math.cos(a), 16 + 10 * math.sin(a), 5, CREAM))
	pts.append(shuriken(16, 16, 6, GOLD))
	icons['Skill_Shuriken_Barrage'] = ('만천화우', compose('Active', pts))
	icons['Skill_Shuriken_Execute'] = ('처형', compose('Active', [
		skull(12, 17, 7.5, CREAM),
		kunai(21, 12, 0.6, 45, GOLD),
	]))
	icons['Skill_Shuriken_Gale'] = ('질풍', compose('Passive', [
		crescent(10, 30, 22, 3.6, 280, 350, CREAM),
		crescent(8, 32, 15, 3.2, 280, 350, CREAM),
		crescent(6, 34, 8, 2.8, 280, 350, GOLD),
	]))
	icons['Skill_Shuriken_PierceKunai'] = ('관통 쿠나이', compose('Active', [
		ring(20, 13, 5.5, 2, RED),
		kunai(15, 18, 0.95, 45, CREAM),
	]))
	icons['Skill_Shuriken_Ricochet'] = ('연환', compose('Passive', [
		crescent(16, 22, 9, 2.6, 200, 340, GOLD),
		crescent(20, 12, 7, 2.2, 200, 340, GOLD),
		shuriken(9, 21, 5.5, CREAM),
		shuriken(25, 15, 4.5, CREAM),
	]))
	icons['Skill_Shuriken_Storm'] = ('폭풍 투척', compose('Ultimate', [
		crescent(16, 16, 14.5, 3.6, 0, 100, CREAM),
		crescent(16, 16, 14.5, 3.6, 120, 220, CREAM),
		crescent(16, 16, 14.5, 3.6, 240, 340, CREAM),
		shuriken(16, 16, 8, GOLD),
	]))
	icons['Skill_Shuriken_Reaper'] = ('사신의 쿠나이', compose('Ultimate', [
		kunai(16, 17, 0.75, 70, PURPLE),
		kunai(16, 17, 0.75, 20, PURPLE),
		kunai(16, 17, 0.8, 45, CREAM),
	]))
	return icons


# ── 소총 스킬 ──────────────────────────────────────────

def rifle_icons():
	icons = {}
	icons['Skill_Rifle_WeakSpot'] = ('약점 저격', compose('Active', [
		crosshair(16, 16, 10, CREAM, RED),
	]))
	icons['Skill_Rifle_Grenade'] = ('수류탄', compose('Active', [
		grenade(16, 18, 8),
	]))
	icons['Skill_Rifle_SteadyAim'] = ('정조준', compose('Passive', [
		ring(16, 16, 11, 2.2, CREAM),
		slash(16, 4, 16, 28, 0.9, CREAM),
		slash(4, 16, 28, 16, 0.9, CREAM),
		burst(16, 16, 3.5, 1.4, 4, GOLD, 45),
	]))
	icons['Skill_Rifle_MarkTarget'] = ('표적 지정', compose('Active', [
		bullseye(16, 16, 12),
	]))
	icons['Skill_Rifle_LoadedShot'] = ('장전 사격', compose('Active', [
		bullet(9, 17, 1.0, 90),
		bullet(16, 17, 1.0, 90),
		bullet(23, 17, 1.0, 90),
	]))
	icons['Skill_Rifle_Drone'] = ('정찰 드론', compose('Active', [
		drone(16, 17, 10.5),
	]))
	icons['Skill_Rifle_Firepower'] = ('화력 보강', compose('Passive', [
		bullet(12, 18, 1.15, 90),
		arrow_up(23, 5, 4, 14, GOLD),
	]))
	icons['Skill_Rifle_Bombardment'] = ('폭격 요청', compose('Active', [
		burst(16, 25, 8, 3.5, 7, GOLD, 0),
		bomb(10, 11, 3.6),
		bomb(22, 9, 3.6),
	]))
	icons['Skill_Rifle_PierceShot'] = ('관통 저격', compose('Active', [
		slash(2, 26, 30, 6, 1.2, CREAM),
		ring(14, 18, 5, 2, RED),
		bullet(21, 12, 0.95, 35),
	]))
	icons['Skill_Rifle_OverAim'] = ('초과 조준', compose('Passive', [
		crosshair(13, 18, 8.5, CREAM, RED),
		arrow_up(26, 2, 3.5, 10, GOLD),
	]))
	icons['Skill_Rifle_SupportFire'] = ('지원 사격', compose('Active', [
		burst(16, 23, 9, 4, 7, GOLD, 0),
		bullet(16, 9, 1.1, -90, RED),
	]))
	icons['Skill_Rifle_TacticalLink'] = ('전술 연동', compose('Passive', [
		link(9, 23, 23, 9, 3.2, GOLD),
		drone(20, 10, 4.5),
		crosshair(10, 22, 5, CREAM, RED),
	]))
	icons['Skill_Rifle_Turret'] = ('터렛', compose('Ultimate', [
		turret(15, 17, 9),
	]))
	icons['Skill_Rifle_Deadeye'] = ('데드아이', compose('Ultimate', [
		eye(16, 16, 13, 7.5, CREAM, RED),
	]))
	return icons


def all_skill_icons():
	icons = {}
	icons.update(great_sword_icons())
	icons.update(dagger_icons())
	icons.update(shuriken_icons())
	icons.update(rifle_icons())
	return icons


def weapon_icons():
	# 무기 아이콘은 투명 배경 — 칸 틀 위에 얹힌다
	return {
		'Icon_Mastery_01': ('대검', great_sword(16, 16, 0.84)),
		'Icon_Mastery_02': ('단검', dagger(16, 16, 1.1)),
		'Icon_Mastery_03': ('수리검', shuriken(16, 16, 13.5)),
		'Icon_Mastery_04': ('소총', rifle(16, 15, 0.92, 40)),
	}


def weapon_finish(im):
	# 무기는 투명 배경 — 네온은 금빛 번짐, 나머지는 스타일 마감 그대로
	return dress(im, False, GOLD if STYLE['post'] == 'neon' else None)


def render_all(style):
	apply_style(style)
	items = []
	for name, (label, im) in weapon_icons().items():
		items.append((name, label, finalize(weapon_finish(im))))
	for name, (label, im) in all_skill_icons().items():
		items.append((name, label, finalize(im)))
	return items


def preview(items, path):
	font = ImageFont.truetype('E:/GameProjects/ProjectOne/ProjectOne/Assets/Project/Art/UI/Fonts/Galmuri/Galmuri11-Bold.ttf', 24)
	cell_w = 200
	cell_h = 210
	cols = 6
	rows = (len(items) + cols - 1) // cols
	sheet = Image.new('RGBA', (cols * cell_w, rows * cell_h), c('#191A29'))
	d = ImageDraw.Draw(sheet)
	for i, (label, im) in enumerate(items):
		x = (i % cols) * cell_w + 26
		y = (i // cols) * cell_h + 10
		d.rectangle([x - 6, y - 6, x + 128 + 5, y + 128 + 5], fill=c('#12111C'))
		d.rectangle([x - 4, y - 4, x + 128 + 3, y + 128 + 3], fill=c('#1A1B28'))
		sheet.alpha_composite(im, (x, y))
		d.text((x - 10, y + 140), label, font=font, fill=c('#F4E7C8'))
	sheet.save(path)


ICON_ROOT = 'E:/GameProjects/ProjectOne/ProjectOne/Assets/Project/Art/UI/Atlas/Textures/Icons'
INSTALL_STYLE = 'pixel'


def install():
	# 게임에 넣기 — 무기는 기존 마스터리 아이콘 파일을 덮어쓰고, 스킬은 SkillIcon_<무기>_<이름>.png 로 저장(Atlas_Icon 이 폴더째 팩)
	for name, label, im in render_all(INSTALL_STYLE):
		if name.startswith('Icon_Mastery_'):
			path = os.path.join(ICON_ROOT, 'Mastery', name + '.Png')
		else:
			path = os.path.join(ICON_ROOT, 'Skills', name.replace('Skill_', 'SkillIcon_', 1) + '.png')
		im.save(path)
	print('installed ->', ICON_ROOT)


def main():
	if sys.argv[1] == '--install':
		install()
		return
	out = sys.argv[1]
	style = sys.argv[3] if len(sys.argv) > 3 else 'flat'
	os.makedirs(out, exist_ok=True)
	items = render_all(style)
	for name, label, im in items:
		im.save(os.path.join(out, name + '.png'))
	if len(sys.argv) > 2 and sys.argv[2] != '-':
		preview([(label, im) for name, label, im in items], sys.argv[2])
	print('done ->', out)


if __name__ == '__main__':
	main()

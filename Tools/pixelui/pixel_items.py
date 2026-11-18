# 도트 아이템 아이콘 — 24x24 굵은 픽셀 + 진한 외곽선·바깥 테두리 + 단을 끊은 음영, 등급별 재질·장식.
#
# 사용: python pixel_items.py --install        ← 현재 아이템 아이콘을 게임 폴더에 저장
#       python pixel_items.py <미리보기 png>     ← 기본템 + 고급템 예시 확인 시트
#
# 모양 좌표는 32 기준으로 적고 실제로는 N(24) 칸 가운데에 FIT 만큼 줄여 그린다.
# 부품(Part)마다 모양(마스크)과 재질(밝음→어두움 램프)을 정하면
#   - 가장자리까지의 거리로 높이를 만들어 둥근 모서리(베벨)를 왼쪽 위 빛으로 칠한다
#   - 금속은 사선 반사 띠, 보석은 면(facet) 분할 + 반짝임, 나무는 결
#   - 위에 겹친 부품은 아래 부품에 그림자를 드리운다
# 을 자동으로 칠하고, 전체에 재질 색이 남는 1칸 외곽선을 두른다.
# 좋은 아이템일수록 재질(철 → 강철 → 미스릴 → 금 → 신화)과 장식(보석·금테·가시·빛)을 올려 아이콘만으로 차이가 나게 한다.

import math
import sys
from collections import deque
from PIL import Image, ImageDraw, ImageFont

N = 24
SS = 8	# 마스크는 8배로 그려 줄인다 — 사선·곡선 가장자리가 고르게 나온다
FIT = 0.84	# 그림을 칸 가운데로 줄여 외곽선·그림자 테두리 자리를 남긴다
K = SS * N / 32.0	# 32 기준 좌표 → 마스크 픽셀
UNIT = N / 32.0 * FIT	# 32 기준 1칸 = 실제 칸 수
INK = (24, 18, 36, 255)
RIM = (10, 8, 16, 150)	# 외곽선 바깥 어두운 테두리(스티커처럼 떠 보이게)


def fx(v):
	return ((v - 16) * FIT + 16) * K


def c(h, a=255):
	h = h.lstrip('#')
	return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mixc(a, b, t):
	return (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t), 255)


def ramp(*hexes):
	# 5단 그대로 — 그라데이션 없이 단을 끊어 칠한다
	return [c(h) for h in hexes]


# ── 재질 (밝음 → 어두움) ────────────────────────────────

IRON = ramp('#D4D8DE', '#A4AAB2', '#7A808A', '#555A66', '#33363F')
STEEL = ramp('#FFFFFF', '#D6E2F0', '#9FB2CA', '#6A7E9E', '#3E4B66')
MITHRIL = ramp('#F2EAFF', '#BFA8F6', '#8C70D6', '#5C44A8', '#33236E')
GOLD = ramp('#FFFBD6', '#FFDA52', '#EBA42E', '#B06816', '#64340A')
MYTHIC = ramp('#FFC0C2', '#EC5262', '#B02438', '#6C1424', '#2C0A12')
OBSIDIAN = ramp('#8A80A4', '#565070', '#3A3450', '#252033', '#14111E')
LEATHER = ramp('#E2AA74', '#B47A4C', '#8A5632', '#5E3820', '#382012')
WOOD = ramp('#EEB474', '#C2843E', '#925C28', '#663E1A', '#3E240E')
DARKWOOD = ramp('#9C6A50', '#704634', '#4E2E22', '#331C16', '#1C0E0A')
IVORY = ramp('#FFFFFF', '#F6EEDC', '#DACEB2', '#AC9E80', '#6A604C')
GUN = ramp('#C8D0E0', '#919CB2', '#646E86', '#40485C', '#232838')
CLOTH_RED = ramp('#FFA4A4', '#EE5254', '#BE2C3A', '#801A2A', '#4A0E18')
CLOTH_BLUE = ramp('#B0DAFF', '#5EA6F2', '#3470C8', '#224A8E', '#122A56')
VOID = ramp('#4A3C5A', '#2E2440', '#1C1428', '#120C1A', '#08060C')

GEM = {
	'green': ramp('#F0FFE8', '#94F482', '#3CC250', '#1E8638', '#0C4A20'),
	'blue': ramp('#EEF8FF', '#84D0FF', '#2E8CF0', '#1A56B0', '#0A2A66'),
	'purple': ramp('#F8EEFF', '#D09CFF', '#9450F0', '#5E26B0', '#320E66'),
	'orange': ramp('#FFF8E0', '#FFCA62', '#FF8A1E', '#C2520E', '#6C2806'),
	'red': ramp('#FFECEE', '#FF8490', '#F02A42', '#A8122A', '#580614'),
	'cyan': ramp('#EEFFFF', '#90F6FF', '#2ECDE8', '#1A84A8', '#0A4660'),
}

# ── 마스크 ───────────────────────────────────────────

class Mask:
	def __init__(self):
		self.img = Image.new('L', (N * SS, N * SS), 0)
		self.d = ImageDraw.Draw(self.img)

	def poly(self, pts):
		self.d.polygon([(fx(x), fx(y)) for x, y in pts], fill=255)
		return self

	def ellipse(self, x0, y0, x1, y1):
		self.d.ellipse([fx(x0), fx(y0), fx(x1) - 1, fx(y1) - 1], fill=255)
		return self

	def rect(self, x0, y0, x1, y1):
		self.d.rectangle([fx(x0), fx(y0), fx(x1) - 1, fx(y1) - 1], fill=255)
		return self

	def rrect(self, x0, y0, x1, y1, r):
		self.d.rounded_rectangle([fx(x0), fx(y0), fx(x1) - 1, fx(y1) - 1], radius=r * K * FIT, fill=255)
		return self

	def cut_ellipse(self, x0, y0, x1, y1):
		self.d.ellipse([fx(x0), fx(y0), fx(x1) - 1, fx(y1) - 1], fill=0)
		return self

	def cut_rect(self, x0, y0, x1, y1):
		self.d.rectangle([fx(x0), fx(y0), fx(x1) - 1, fx(y1) - 1], fill=0)
		return self

	def line(self, x0, y0, x1, y1, w):
		self.d.line([(fx(x0), fx(y0)), (fx(x1), fx(y1))], fill=255, width=int(w * K * FIT))
		return self

	def grid(self):
		small = self.img.resize((N, N), Image.BOX)
		px = small.load()
		return [[px[x, y] >= 110 for x in range(N)] for y in range(N)]


def axis_poly(cx, cy, ang, pts):
	# 축 좌표(s 길이, p 수직) → 화면 좌표. ang 은 도(오른쪽 위가 +)
	a = math.radians(ang)
	out = []
	for s, p in pts:
		out.append((cx + s * math.cos(a) + p * math.sin(a), cy - s * math.sin(a) + p * math.cos(a)))
	return out


def arect(s0, s1, p0, p1):
	return [(s0, p0), (s1, p0), (s1, p1), (s0, p1)]


# ── 음영 엔진 ────────────────────────────────────────

# 재질 종류별 베벨 반경(실제 칸)과 대비
BEVEL = {'metal': 2.2, 'leather': 3.0, 'wood': 2.0, 'gem': 1.6, 'flat': 1.0, 'hole': 1.0}


class Part:
	def __init__(self, mask, mat, kind='metal'):
		self.grid = mask.grid() if isinstance(mask, Mask) else mask
		self.mat = mat
		self.kind = kind


def dist_field(g):
	# 바깥(이미지 밖 포함)까지의 거리 — 2패스 챔퍼
	big = 999.0
	d = [[big if g[y][x] else 0.0 for x in range(N)] for y in range(N)]

	def at(x, y):
		if 0 <= x < N and 0 <= y < N:
			return d[y][x]
		return 0.0

	for y in range(N):
		for x in range(N):
			if d[y][x] > 0:
				d[y][x] = min(d[y][x], at(x - 1, y) + 1, at(x, y - 1) + 1, at(x - 1, y - 1) + 1.414, at(x + 1, y - 1) + 1.414)
	for y in range(N - 1, -1, -1):
		for x in range(N - 1, -1, -1):
			if d[y][x] > 0:
				d[y][x] = min(d[y][x], at(x + 1, y) + 1, at(x, y + 1) + 1, at(x + 1, y + 1) + 1.414, at(x - 1, y + 1) + 1.414)
	return d


def tone(mat, v):
	# v: 1 = 가장 밝음, 0 = 가장 어두움
	n = len(mat) - 1
	i = int(round((1 - v) * n))
	return mat[max(0, min(n, i))]


def paint(parts):
	img = [[None] * N for _ in range(N)]
	owner = [[-1] * N for _ in range(N)]
	for pi, part in enumerate(parts):
		g = part.grid
		xs = [x for y in range(N) for x in range(N) if g[y][x]]
		ys = [y for y in range(N) for x in range(N) if g[y][x]]
		if not xs:
			continue
		x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
		span = max(1, (x1 - x0) + (y1 - y0))
		kind = part.kind
		d = dist_field(g)
		thick = max(max(row) for row in d)
		r = min(BEVEL[kind] * UNIT / 1.5, max(1.0, thick * 0.8))
		h = [[0.0] * N for _ in range(N)]
		for y in range(N):
			for x in range(N):
				if g[y][x]:
					t = min(d[y][x] / r, 1.0)
					h[y][x] = math.sqrt(1 - (1 - t) * (1 - t))

		def hh(x, y):
			if 0 <= x < N and 0 <= y < N:
				return h[y][x]
			return 0.0

		cx = sum(xs) / len(xs)
		cy = sum(ys) / len(ys)
		rad = max(1.0, max(math.hypot(x - cx, y - cy) for x, y in zip(xs, ys)))
		for y in range(N):
			for x in range(N):
				if g[y][x] == False:
					continue
				gx = (hh(x + 1, y) - hh(x - 1, y)) * 0.5
				gy = (hh(x, y + 1) - hh(x, y - 1)) * 0.5
				slope = gx + gy	# 왼쪽 위를 향한 면이 +
				t = ((x - x0) + (y - y0)) / span
				if kind == 'metal':
					v = 0.66 - 0.42 * t + slope * 0.9
					if h[y][x] > 0.98:
						# 사선 반사 띠 두 줄
						if abs(t - 0.30) < 0.05:
							v += 0.32
				elif kind == 'leather':
					v = 0.60 - 0.36 * t + slope * 0.7
				elif kind == 'wood':
					v = 0.62 - 0.40 * t + slope * 0.8
					if (x + 2 * y + (x * 7 % 3)) % 6 == 0 and h[y][x] > 0.9:
						v -= 0.22
				elif kind == 'gem':
					# 중심 판 + 6면 — 면의 방향이 빛을 향할수록 밝다
					dx = x - cx
					dy = y - cy
					rr = math.hypot(dx, dy) / rad
					if rr < 0.38:
						v = 0.80 - 0.25 * t
					else:
						a = math.atan2(dy, dx)
						sector = math.floor((a + math.pi) / (math.pi / 3))
						mid = -math.pi + (sector + 0.5) * (math.pi / 3)
						v = 0.45 - 0.42 * (math.cos(mid) + math.sin(mid)) * 0.7
					v += slope * 0.6
				elif kind == 'hole':
					# 파인 구멍 — 위쪽은 테두리 그림자로 어둡고 아래로 갈수록 살짝 밝다
					v = 0.15 + 0.35 * t
				else:
					v = 0.55 - 0.15 * t + slope * 0.4
				img[y][x] = tone(part.mat, v)
				owner[y][x] = pi
		# 보석 반짝임 — 중심 판의 왼쪽 위
		if kind == 'gem':
			gx0 = int(round(cx - rad * 0.30))
			gy0 = int(round(cy - rad * 0.30))
			for dx, dy in ((0, 0),):
				if 0 <= gx0 + dx < N and 0 <= gy0 + dy < N and g[gy0 + dy][gx0 + dx]:
					img[gy0 + dy][gx0 + dx] = (255, 255, 255, 255)
	# 위에 겹친 부품이 아래 부품에 드리우는 그림자(오른쪽 아래로)
	shadowed = [[False] * N for _ in range(N)]
	for y in range(N):
		for x in range(N):
			o = owner[y][x]
			if o < 0:
				continue
			for dx, dy in ((-1, 0), (0, -1), (-1, -1)):
				nx = x + dx
				ny = y + dy
				if 0 <= nx < N and 0 <= ny < N and owner[ny][nx] > o:
					shadowed[y][x] = True
					break
	for y in range(N):
		for x in range(N):
			if shadowed[y][x]:
				img[y][x] = mixc(img[y][x], INK, 0.42)
	return img, owner


def finish(img, owner, parts, aura=None, sparkles=()):
	out = Image.new('RGBA', (N, N), (0, 0, 0, 0))
	px = out.load()
	filled = [[img[y][x] is not None for x in range(N)] for y in range(N)]
	# 외곽선 — 이웃 부품의 가장 어두운 색에 먹색을 섞는다(재질 색이 남는다)
	line = [[None] * N for _ in range(N)]
	for y in range(N):
		for x in range(N):
			if filled[y][x]:
				continue
			for dx, dy in ((0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1)):
				nx = x + dx
				ny = y + dy
				if 0 <= nx < N and 0 <= ny < N and filled[ny][nx]:
					mat = parts[owner[ny][nx]].mat
					line[y][x] = mixc(mat[-1], INK, 0.75)
					break
	# 빛(오라) — 외곽선 바깥
	if aura is not None:
		dist = [[99] * N for _ in range(N)]
		q = deque()
		for y in range(N):
			for x in range(N):
				if filled[y][x] or line[y][x] is not None:
					dist[y][x] = 0
					q.append((x, y))
		while q:
			x, y = q.popleft()
			for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
				nx = x + dx
				ny = y + dy
				if 0 <= nx < N and 0 <= ny < N and dist[ny][nx] > dist[y][x] + 1:
					dist[ny][nx] = dist[y][x] + 1
					q.append((nx, ny))
		for y in range(N):
			for x in range(N):
				dd = dist[y][x]
				if 1 <= dd <= 2:
					px[x, y] = aura[:3] + ((210, 90)[dd - 1],)
	for y in range(N):
		for x in range(N):
			if filled[y][x] or line[y][x] is not None:
				continue
			for dx, dy in ((0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1)):
				nx = x + dx
				ny = y + dy
				if 0 <= nx < N and 0 <= ny < N and line[ny][nx] is not None:
					if px[x, y][3] < RIM[3]:
						px[x, y] = RIM
					break
	for y in range(N):
		for x in range(N):
			if line[y][x] is not None:
				px[x, y] = line[y][x]
			if filled[y][x]:
				px[x, y] = img[y][x]
	for sx, sy in sparkles:
		sx = int(round(sx * UNIT))
		sy = int(round(sy * UNIT))
		for dx, dy, col in ((0, 0, (255, 255, 255, 255)), (1, 0, (255, 236, 160, 200)), (-1, 0, (255, 236, 160, 200)), (0, 1, (255, 236, 160, 200)), (0, -1, (255, 236, 160, 200))):
			if 0 <= sx + dx < N and 0 <= sy + dy < N:
				px[sx + dx, sy + dy] = col
	return out


# ── 아이템 공통 ───────────────────────────────────────

def A(cx, cy, ang, pts):
	return Mask().poly(axis_poly(cx, cy, ang, pts))


def build(parts, aura=None, sparkles=(), marks=()):
	img, owner = paint(parts)
	for x, y, col in marks:
		x = int(round(x * UNIT))
		y = int(round(y * UNIT))
		if 0 <= x < N and 0 <= y < N and img[y][x] is not None:
			img[y][x] = col
	return finish(img, owner, parts, aura, sparkles)


def fit(make, *args):
	# 바깥 테두리까지 칸 안에 사방 1칸 여백을 두고 들어오게 한다 — 넘치면 줄여 다시 그리고, 치우치면 가운데로 옮긴다
	global FIT, UNIT
	keep = FIT
	im = make(*args)
	box = im.getbbox()
	while (box[2] - box[0] > N - 2 or box[3] - box[1] > N - 2) and FIT > 0.6:
		FIT -= 0.03
		UNIT = N / 32.0 * FIT
		im = make(*args)
		box = im.getbbox()
	FIT = keep
	UNIT = N / 32.0 * FIT
	out = Image.new('RGBA', (N, N), (0, 0, 0, 0))
	x0 = (N - (box[2] - box[0])) // 2
	y0 = (N - (box[3] - box[1])) // 2
	out.paste(im.crop(box), (x0, y0))
	return out


# ── 무기 ─────────────────────────────────────────────

def _circle(center, r):
	x, y = center
	return (x - r, y - r, x + r, y + r)


def _at(cx, cy, ang, s):
	return axis_poly(cx, cy, ang, [(s, 0)])[0]


def _pol(cx, cy, r, deg):
	a = math.radians(deg)
	return (cx + r * math.cos(a), cy + r * math.sin(a))


def great_sword():
	# 넓은 날 + 날 밑동 홈 + 끝이 말린 가드
	cx, cy, ang = 16.5, 15.5, 45
	return build([
		Part(A(cx, cy, ang, arect(-14, -8.5, -1.3, 1.3)), LEATHER, 'leather'),
		Part(Mask().ellipse(*_circle(_at(cx, cy, ang, -15.4), 2.3)), IRON),
		Part(A(cx, cy, ang, [(-7, -3.4), (-4.5, -3.4), (-3.5, -4.4), (12.5, -4.0), (18, 0), (12.5, 4.0), (-3.5, 4.4), (-4.5, 3.4), (-7, 3.4)]), STEEL),
		Part(A(cx, cy, ang, arect(-3, 10, -0.8, 0.8)), IRON, 'flat'),
		Part(A(cx, cy, ang, [(-10.5, -8), (-7, -5.5), (-7, 5.5), (-10.5, 8), (-8.8, 4), (-8.8, -4)]), IRON),
	])


def dagger():
	# 끝이 휘어 올라간 날 + 고리 가드
	cx, cy, ang = 16, 16, 48
	return build([
		Part(A(cx, cy, ang, arect(-11.5, -4.5, -1.2, 1.2)), DARKWOOD, 'leather'),
		Part(Mask().ellipse(*_circle(_at(cx, cy, ang, -12.3), 1.9)), IRON),
		Part(A(cx, cy, ang, [(-3, -2.4), (4, -2.8), (9, -2.3), (13, -0.6), (15.5, 1.8), (11, 1.8), (5, 2.4), (-3, 2.4)]), STEEL),
		Part(Mask().ellipse(*_circle(_at(cx, cy, ang, -3.6), 2.9)).cut_ellipse(*_circle(_at(cx, cy, ang, -3.6), 1.2)), IRON),
	])


def shuriken():
	# 바람개비처럼 휜 날 4개
	m = Mask()
	for i in range(4):
		a = -45 + i * 90
		m.poly([_pol(16, 16, 4.6, a - 80), _pol(16, 16, 9.5, a - 28), _pol(16, 16, 15, a), _pol(16, 16, 3, a + 55)])
	m.cut_ellipse(14, 14, 18, 18)
	ring = Mask().ellipse(11.6, 11.6, 20.4, 20.4).cut_ellipse(14, 14, 18, 18)
	return build([Part(m, STEEL), Part(ring, IRON)])


def rifle():
	# 휜 개머리 + 노리쇠 손잡이 + 짧은 조준경
	cx, cy, ang = 16, 16.5, 36
	return build([
		Part(A(cx, cy, ang, [(-19, -2.2), (-12, -2.6), (-8.5, -2.4), (-8.5, 2.6), (-12, 3.4), (-17, 6.2), (-19.5, 4.5)]), WOOD, 'wood'),
		Part(A(cx, cy, ang, [(-6.5, 2), (-3.2, 2), (-4.4, 8), (-7.8, 8)]), WOOD, 'wood'),
		Part(A(cx, cy, ang, arect(-9, 6, -2.4, 2.4)), GUN),
		Part(A(cx, cy, ang, arect(6, 19.5, -1.1, 1.1)), STEEL),
		Part(A(cx, cy, ang, arect(6, 13, -2.2, 2.2)), WOOD, 'wood'),
		Part(A(cx, cy, ang, arect(-5.5, 3, -6.2, -3.6)), GUN),
		Part(A(cx, cy, ang, [(-1, 2.4), (0.6, 2.4), (2, 5.5), (0.4, 5.5)]), IRON),
	])


# ── 방어구 ───────────────────────────────────────────

def helmet_steel():
	# 콧대 가드 + 볼 가드가 있는 둥근 투구
	return build([
		Part(Mask().poly([(10, 18), (22, 18), (22, 26), (16, 28.5), (10, 26)]), VOID, 'hole'),
		Part(Mask().ellipse(5, 4, 27, 30).cut_rect(0, 19, 32, 32), STEEL),
		Part(Mask().poly([(5, 17), (11, 17), (11, 26), (8.5, 29.5), (5, 25)]), STEEL),
		Part(Mask().poly([(21, 17), (27, 17), (27, 25), (23.5, 29.5), (21, 26)]), STEEL),
		Part(Mask().rrect(15, 3.4, 17.2, 15.5, 1), STEEL),
		Part(Mask().rrect(4.5, 14.6, 27.5, 18.6, 1), IRON),
		Part(Mask().poly([(14.6, 17), (17.4, 17), (17.2, 26), (16, 28), (14.8, 26)]), IRON),
	], marks=[(7, 16.5, IRON[0]), (25, 16.5, IRON[0])])


def hood_leather():
	# 끝이 옆으로 늘어진 두건 + 망토
	return build([
		Part(Mask().ellipse(5, 5, 25.5, 27.5).poly([(17, 5), (28.5, 2), (24, 11.5)]), LEATHER, 'leather'),
		Part(Mask().ellipse(8.4, 10.5, 20.6, 25.5), VOID, 'hole'),
		Part(Mask().poly([(4, 21.5), (26.5, 21.5), (30, 28.5), (26, 30.8), (6, 30.8), (1.8, 28.5)]), LEATHER, 'leather'),
		Part(Mask().ellipse(15.6, 21.4, 19.4, 25.2), IRON),
	])


def armor_steel():
	# 가운데가 솟은 흉갑 + 큰 왼쪽 어깨 + 사선 가죽 끈
	return build([
		Part(Mask().poly([(9, 9), (23, 9), (25, 15), (23.5, 23), (16, 26), (8.5, 23), (7, 15)]), STEEL),
		Part(Mask().poly([(9, 23), (16, 26), (23, 23), (22.5, 26.5), (16, 29.5), (9.5, 26.5)]), STEEL),
		Part(Mask().poly([(10.5, 26.5), (16, 29.5), (21.5, 26.5), (20.5, 29.5), (16, 31.4), (11.5, 29.5)]), IRON),
		Part(Mask().ellipse(11, 7, 21, 13).cut_ellipse(12.6, 8.2, 19.4, 11.8), IRON),
		Part(Mask().ellipse(12.6, 8.2, 19.4, 11.8), VOID, 'hole'),
		Part(Mask().rrect(9, 18.5, 23, 21.5, 1), LEATHER, 'leather'),
		Part(Mask().rrect(14.4, 18, 17.6, 22, 0.6), IRON),
		Part(Mask().ellipse(20.5, 6.5, 29.5, 14.5), STEEL),
		Part(Mask().ellipse(2.5, 5, 13.5, 15.5), STEEL),
		Part(Mask().rrect(3, 10, 13, 11.4, 0.6), IRON, 'flat'),
	])


def robe_leather():
	# 깃이 선 가죽 코트 + 사선 탄띠와 주머니
	return build([
		Part(Mask().poly([(9.5, 6.5), (3.5, 13), (3, 20.5), (7, 20.5), (9, 15)]), LEATHER, 'leather'),
		Part(Mask().poly([(22.5, 6.5), (28.5, 13), (29, 20.5), (25, 20.5), (23, 15)]), LEATHER, 'leather'),
		Part(Mask().poly([(10, 6), (22, 6), (25, 10), (24, 16), (26, 30), (6, 30), (8, 16), (7, 10)]), LEATHER, 'leather'),
		Part(Mask().poly([(12.5, 5.5), (16, 12), (19.5, 5.5)]), IVORY, 'leather'),
		Part(Mask().poly([(10, 4.5), (13, 5.5), (16, 12.5), (13.5, 13), (9, 8)]), DARKWOOD, 'leather'),
		Part(Mask().poly([(22, 4.5), (19, 5.5), (16, 12.5), (18.5, 13), (23, 8)]), DARKWOOD, 'leather'),
		Part(Mask().rrect(7.6, 19, 24.4, 21.6, 0.6), DARKWOOD, 'leather'),
		Part(Mask().line(21.5, 8, 11, 25, 2.2), DARKWOOD, 'leather'),
		Part(Mask().rrect(15, 13.8, 18.4, 17.2, 0.5), IRON),
		Part(Mask().rrect(8.2, 22.5, 14.2, 28, 1), DARKWOOD, 'leather'),
	])


def _glove(ox, oy, sc, mat, cuff_mat, kind):
	def p(x, y):
		return (ox + x * sc, oy + y * sc)

	def rr(x0, y0, x1, y1, r):
		a = p(x0, y0)
		b = p(x1, y1)
		return Mask().rrect(a[0], a[1], b[0], b[1], r * sc)

	t0 = p(9.5, 19)
	t1 = p(5.2, 13.6)
	tc0 = p(3, 11.2)
	tc1 = p(7.4, 15.6)
	parts = [
		Part(rr(9, 9, 23, 23, 3.5), mat, kind),
		Part(rr(9, 5, 12.4, 14, 1.7), mat, kind),
		Part(rr(12.6, 2.4, 16, 13, 1.7), mat, kind),
		Part(rr(16.2, 2.2, 19.6, 13, 1.7), mat, kind),
		Part(rr(19.8, 4.6, 23, 14, 1.7), mat, kind),
		Part(Mask().line(t0[0], t0[1], t1[0], t1[1], 3.8 * sc).ellipse(tc0[0], tc0[1], tc1[0], tc1[1]), mat, kind),
	]
	if kind == 'metal':
		parts.append(Part(rr(9, 10.5, 23, 13.5, 1), cuff_mat))
		c0 = p(7.5, 20.5)
		c1 = p(24.5, 20.5)
		c2 = p(26, 28.5)
		c3 = p(6, 28.5)
		parts.append(Part(Mask().poly([c0, c1, c2, c3]), cuff_mat, kind))
	else:
		parts.append(Part(rr(7.5, 20.5, 24.5, 29, 2), cuff_mat, kind))
	return parts


def gloves(mat, cuff_mat, kind):
	if kind == 'metal':
		# 손목이 벌어진 건틀릿 한 짝
		return build(_glove(0, 0, 1.0, mat, cuff_mat, kind))
	# 겹쳐 놓은 장갑 한 켤레
	return build(_glove(9, -1, 0.72, mat, cuff_mat, kind) + _glove(-1, 7, 0.78, mat, cuff_mat, kind))


def _boot(ox, oy, sc, mat, sole, kind):
	def rr(x0, y0, x1, y1, r):
		return Mask().rrect(ox + x0 * sc, oy + y0 * sc, ox + x1 * sc, oy + y1 * sc, r * sc)

	def el(x0, y0, x1, y1):
		return Mask().ellipse(ox + x0 * sc, oy + y0 * sc, ox + x1 * sc, oy + y1 * sc)

	body = rr(9, 4, 19, 22, 1.2).ellipse(ox + 10 * sc, oy + 15 * sc, ox + 28 * sc, oy + 27 * sc).rect(ox + 9 * sc, oy + 18 * sc, ox + 19 * sc, oy + 26 * sc)
	parts = [Part(body, mat, kind)]
	if kind == 'metal':
		parts.append(Part(el(18, 16, 28.5, 26.5), mat))
		parts.append(Part(rr(7.5, 2.5, 20.5, 7.5, 1.8), sole))
	else:
		parts.append(Part(rr(7.5, 2.5, 20.5, 7.5, 1.8), sole, kind))
	parts.append(Part(rr(8, 24.5, 28.5, 28.5, 1.5), sole, 'flat' if kind == 'leather' else kind))
	return parts


def boots(mat, sole, kind):
	# 앞뒤로 겹친 신발 한 켤레
	return build(_boot(9.5, -1, 0.72, mat, sole, kind) + _boot(-1.5, 6.5, 0.8, mat, sole, kind))


# ── 장신구·유물 ──────────────────────────────────────

def ring(band, gem):
	# 비스듬히 누운 반지 + 솟은 보석 받침
	return build([
		Part(Mask().ellipse(4.5, 13.5, 27.5, 29).cut_ellipse(8, 17, 24, 25.8), band),
		Part(Mask().poly([(10.5, 11), (21.5, 11), (19.5, 17.5), (12.5, 17.5)]), band),
		Part(Mask().ellipse(10.5, 3, 21.5, 14), GEM[gem], 'gem'),
	])


def amulet(band, gem):
	if gem == 'red':
		# 가죽 끈에 단 송곳니 + 금 고리
		return build([
			Part(Mask().line(5, 2, 9, 9, 1.4).line(9, 9, 13, 13, 1.4).line(27, 2, 23, 9, 1.4).line(23, 9, 19, 13, 1.4), DARKWOOD, 'flat'),
			Part(Mask().poly([(12.5, 15.5), (19.5, 15.5), (19, 21), (16.8, 27.5), (15.4, 30.5), (13.6, 24)]), IVORY, 'leather'),
			Part(Mask().rrect(11.8, 12, 20.2, 16.4, 1), GOLD),
			Part(Mask().ellipse(14, 9.6, 18, 13.6), GEM['red'], 'gem'),
		])
	# 은 사슬 + 날개 달린 물방울 보석
	m = Mask()
	for i in range(5):
		t = i / 4.0
		m.ellipse(4 + t * 9 - 1.1, 2 + t * 10 - 1.1, 4 + t * 9 + 1.1, 2 + t * 10 + 1.1)
		m.ellipse(28 - t * 9 - 1.1, 2 + t * 10 - 1.1, 28 - t * 9 + 1.1, 2 + t * 10 + 1.1)
	return build([
		Part(m, band),
		Part(Mask().poly([(15, 16.5), (4, 12.5), (6, 18.5), (9.5, 21.5), (15, 21.5)]), band),
		Part(Mask().poly([(17, 16.5), (28, 12.5), (26, 18.5), (22.5, 21.5), (17, 21.5)]), band),
		Part(Mask().ellipse(11.2, 17, 20.8, 28.5).poly([(11.8, 21), (16, 12), (20.2, 21)]), GEM[gem], 'gem'),
	])


def relic_melee():
	# 금테 두른 뿔나팔
	horn = Mask()
	pts = []
	for i in range(25):
		t = i / 24.0
		x = 6 + 20 * t
		y = 24 - 18 * t - 6 * math.sin(math.pi * t)
		r = 5.0 * (1 - t) + 0.8
		pts.append((x, y, r))
		horn.ellipse(x - r, y - r, x + r, y + r)

	def band_at(i, grow):
		x, y, r = pts[i]
		return Mask().ellipse(x - r - grow, y - r - grow, x + r + grow, y + r + grow)

	mx, my, mr = pts[0]
	return build([
		Part(Mask().line(5, 28, 15, 29.5, 1.1).line(15, 29.5, 22, 18, 1.1), LEATHER, 'flat'),
		Part(horn, IVORY, 'leather'),
		Part(band_at(2, 0.5), GOLD),
		Part(band_at(13, 0.4), GOLD),
		Part(Mask().ellipse(mx - mr * 0.75, my - mr * 0.75 + 0.6, mx + mr * 0.55, my + mr * 0.75 + 0.6), VOID, 'hole'),
	])


def relic_ranged():
	# 접히는 망원경 — 가죽 몸통 + 황동 마디 + 푸른 렌즈
	cx, cy, ang = 16.5, 16.5, 35
	return build([
		Part(A(cx, cy, ang, arect(-13, -3, -4.6, 4.6)), LEATHER, 'leather'),
		Part(A(cx, cy, ang, arect(-3, 6, -3.5, 3.5)), GOLD),
		Part(A(cx, cy, ang, arect(6, 12.5, -2.6, 2.6)), GOLD),
		Part(A(cx, cy, ang, arect(12.5, 15.5, -1.8, 1.8)), IRON),
		Part(A(cx, cy, ang, arect(-15, -12, -5.4, 5.4)), GOLD),
		Part(A(cx, cy, ang, arect(-4, -2.4, -4.2, 4.2)), GOLD),
		Part(Mask().ellipse(*_circle(_at(cx, cy, ang, -15.4), 3.6)), GEM['blue'], 'gem'),
	])


# ── 기타 ─────────────────────────────────────────────

BRONZE = ramp('#F6C88A', '#D08E4A', '#A0602A', '#6E3E1A', '#40220C')


def key(level):
	if level == 1:
		# 둥근 고리 청동 열쇠
		return build([
			Part(Mask().ellipse(2.5, 2.5, 13, 13).cut_ellipse(5.4, 5.4, 10.1, 10.1), BRONZE),
			Part(Mask().line(11, 11, 27, 27, 3.2), BRONZE),
			Part(Mask().line(22, 22, 18.5, 25.5, 3.2), BRONZE),
		])
	if level == 2:
		# 세잎 고리 은 열쇠 + 두 갈래 이빨
		bow = Mask()
		for a in (-135, -45, 135):
			x, y = _pol(8, 8, 3.2, a)
			bow.ellipse(x - 3.2, y - 3.2, x + 3.2, y + 3.2)
		bow.ellipse(4.5, 4.5, 11.5, 11.5).cut_ellipse(6.8, 6.8, 9.2, 9.2)
		return build([
			Part(bow, STEEL),
			Part(Mask().line(10.5, 10.5, 28, 28, 3), STEEL),
			Part(Mask().line(20.5, 20.5, 17, 24, 3).line(25.5, 25.5, 22, 29, 3), STEEL),
		])
	# 왕관 고리 금 열쇠 + 보석 + 장식 이빨
	return build([
		Part(Mask().poly([(1.5, 5), (4.5, 8), (7.5, 1.5), (10.5, 8), (13.5, 5), (13, 13.5), (2, 13.5)]), GOLD),
		Part(Mask().line(11, 13, 27.5, 29.5, 3), GOLD),
		Part(Mask().ellipse(*_circle((15.5, 17.5), 2.3)), GOLD),
		Part(Mask().line(21, 23, 17.5, 26.5, 3).line(25.5, 27.5, 22, 31, 2.6), GOLD),
		Part(Mask().poly([(7.5, 7.5), (10, 10), (7.5, 12.5), (5, 10)]), GEM['red'], 'gem'),
	], sparkles=[(13, 2)])


def boss_stone():
	# 붉은 룬이 새겨진 흑요석 파편 + 떠 있는 조각
	return build([
		Part(Mask().poly([(14, 1.5), (22, 7), (24.5, 17), (19, 29), (11, 28), (7, 18), (9, 7)]), OBSIDIAN),
		Part(Mask().line(16, 7.5, 15.5, 23, 1.4).line(16, 12, 20, 9, 1.4).line(15.8, 16, 11.8, 13, 1.4).line(15.6, 20, 19.5, 23, 1.4), GEM['red'], 'flat'),
		Part(Mask().poly([(2.5, 10), (6, 8), (6.5, 12.5), (3.5, 13.5)]), OBSIDIAN),
		Part(Mask().poly([(26, 21.5), (29.5, 19.5), (30, 24), (27, 25.5)]), OBSIDIAN),
	], aura=c('#FF4A5E'))


def chest(body, band, lock_mat, gem=None):
	# 옆면이 보이는 상자 — 둥근 뚜껑 + 띠 + 자물쇠
	kind = 'wood' if body in (WOOD, DARKWOOD) else 'metal'
	parts = [
		Part(Mask().poly([(26, 9), (29.5, 6.5), (29.5, 26.5), (26, 29.5)]), body, kind),
		Part(Mask().ellipse(3, 3.5, 27, 15.5).rect(3, 9.5, 27, 15.5), body, kind),
		Part(Mask().rect(3, 15.5, 27, 29.5), body, kind),
		Part(Mask().rect(6.5, 5, 8.5, 29.5).rect(20.5, 5, 22.5, 29.5), band),
		Part(Mask().rect(3, 14.6, 27, 16.6).rect(3, 27.6, 27, 29.8), band),
		Part(Mask().rrect(11.5, 13, 17.5, 21.5, 1), lock_mat),
		Part(Mask().rect(14, 16.5, 15, 19.5), VOID, 'hole'),
	]
	if body is DARKWOOD:
		parts.append(Part(Mask().rect(3, 5, 6, 9).rect(23.5, 5, 27, 9).rect(3, 26, 6, 29.8).rect(23.5, 26, 27, 29.8), band))
	if gem is not None:
		parts.append(Part(Mask().poly([(14.5, 5.5), (17, 8), (14.5, 10.5), (12, 8)]), GEM[gem], 'gem'))
	return build(parts, sparkles=[(27, 4)] if gem is not None else ())


# ── 고급 아이템 예시 (게임에 넣지 않음 — 차이를 보여 주는 용도) ──

def iron_maiden():
	# 가시 왕관을 쓴 검은 철 투구 + 붉게 빛나는 눈구멍
	parts = []
	for i, x in enumerate((7, 11, 16, 21, 25)):
		h = 2 if i in (0, 4) else (0 if i == 2 else 1)
		parts.append(Part(Mask().poly([(x - 2, 8), (x, h), (x + 2, 8)]), GOLD))
	parts += [
		Part(Mask().ellipse(5, 5, 27, 27).rect(5, 15, 27, 26).poly([(5, 25), (27, 25), (21, 31), (11, 31)]), OBSIDIAN),
		Part(Mask().rect(5, 8, 27, 11), GOLD),
		Part(Mask().poly([(16, 11), (18, 14), (16, 30), (14, 14)]), OBSIDIAN),
		Part(Mask().rect(7, 16, 14, 18.5).rect(18, 16, 25, 18.5), GEM['red'], 'flat'),
		Part(Mask().poly([(14.5, 8.5), (16, 6.5), (17.5, 8.5), (16, 10.5)]), GEM['red'], 'gem'),
	]
	marks = []
	for y in range(21, 28, 3):
		marks += [(9, y, GOLD[1]), (22, y, GOLD[1])]
	return build(parts, aura=c('#FF4A5E'), marks=marks)


def royal_plate():
	return build([
		Part(Mask().poly([(8, 7), (24, 7), (26, 14), (24, 28), (8, 28), (6, 14)]), MITHRIL),
		Part(Mask().poly([(2, 9), (6, 5), (12, 5), (12, 12), (3, 14)]), GOLD),
		Part(Mask().poly([(30, 9), (26, 5), (20, 5), (20, 12), (29, 14)]), GOLD),
		Part(Mask().poly([(12, 7), (20, 7), (16, 12)]), VOID, 'hole'),
		Part(Mask().poly([(16, 12), (22, 16), (16, 25), (10, 16)]), GOLD),
		Part(Mask().poly([(16, 14), (19, 17), (16, 21), (13, 17)]), GEM['blue'], 'gem'),
		Part(Mask().rect(8, 24, 24, 27), GOLD),
	], sparkles=[(27, 22)])


def flame_sword():
	cx, cy, ang = 16, 16, 45
	parts = [
		Part(A(cx, cy, ang, [(-6, -5.5), (4, -4), (9, -6), (11, -2.5), (17.5, 0), (11, 4.5), (5, 4), (-6, 5.5)]), GEM['orange'], 'gem'),
		Part(A(cx, cy, ang, [(-7, -3.6), (12, -3.6), (17, 0), (12, 3.6), (-7, 3.6)]), MYTHIC),
		Part(A(cx, cy, ang, arect(-15, -9, -1.4, 1.4)), DARKWOOD, 'leather'),
		Part(Mask().ellipse(*_circle(axis_poly(cx, cy, ang, [(-16, 0)])[0], 2.4)), GOLD),
		Part(A(cx, cy, ang, [(-10, -7), (-7, -5), (-7, 5), (-10, 7)]), GOLD),
		Part(A(cx, cy, ang, [(-8.6, -1.6), (-7.4, 0), (-8.6, 1.6), (-9.8, 0)]), GEM['red'], 'gem'),
	]
	return build(parts, aura=c('#FF8A2A'), sparkles=[(26, 4)])


def ancient_relic():
	return build([
		Part(Mask().ellipse(1, 9, 31, 23).cut_ellipse(3.5, 11.5, 28.5, 20.5), GOLD),
		Part(Mask().poly([(16, 0), (23, 9), (20, 25), (12, 25), (9, 9)]), GEM['cyan'], 'gem'),
		Part(Mask().poly([(9, 25), (23, 25), (21, 31), (11, 31)]), OBSIDIAN),
	], aura=c('#6AF0FF'), sparkles=[(4, 4), (28, 27)])


# ── 목록 ─────────────────────────────────────────────

def item_icons():
	# (파일명, 표시 이름, 그림)
	return [
		('ItemIcon_GreatSword', '대검', fit(great_sword)),
		('ItemIcon_Dagger', '단검', fit(dagger)),
		('ItemIcon_Shuriken', '수리검', fit(shuriken)),
		('ItemIcon_Rifle', '소총', fit(rifle)),
		('ItemIcon_Helmet_Steel', '강철 투구', fit(helmet_steel)),
		('ItemIcon_Hood_Leather', '가죽 후드', fit(hood_leather)),
		('ItemIcon_Armor_Steel', '강철 갑옷', fit(armor_steel)),
		('ItemIcon_Robe_Leather', '가죽 로브', fit(robe_leather)),
		('ItemIcon_Gloves_Steel', '강철 장갑', fit(gloves, STEEL, IRON, 'metal')),
		('ItemIcon_Gloves_Leather', '가죽 장갑', fit(gloves, LEATHER, DARKWOOD, 'leather')),
		('ItemIcon_Boots_Steel', '강철 신발', fit(boots, STEEL, IRON, 'metal')),
		('ItemIcon_Boots_Leather', '가죽 신발', fit(boots, LEATHER, DARKWOOD, 'leather')),
		('ItemIcon_Ring_Melee', '근접 반지', fit(ring, GOLD, 'red')),
		('ItemIcon_Ring_Ranged', '원거리 반지', fit(ring, STEEL, 'blue')),
		('ItemIcon_Amulet_Melee', '근접 목걸이', fit(amulet, GOLD, 'red')),
		('ItemIcon_Amulet_Ranged', '원거리 목걸이', fit(amulet, STEEL, 'blue')),
		('ItemIcon_Relic_Melee', '근접 유물', fit(relic_melee)),
		('ItemIcon_Relic_Ranged', '원거리 유물', fit(relic_ranged)),
		('ItemIcon_Key_01', '일반 열쇠', fit(key, 1)),
		('ItemIcon_Key_02', '고급 열쇠', fit(key, 2)),
		('ItemIcon_Key_03', '최고급 열쇠', fit(key, 3)),
		('ItemIcon_BossStone', '보스소환석', fit(boss_stone)),
		('ItemIcon_Chest_01', '나무 상자', fit(chest, WOOD, IRON, IRON)),
		('ItemIcon_Chest_02', '은 상자', fit(chest, DARKWOOD, STEEL, STEEL)),
		('ItemIcon_Chest_03', '금 상자', fit(chest, MYTHIC, GOLD, GOLD, 'blue')),
	]


def showcase_icons():
	return [
		('아이언메이든', fit(iron_maiden)),
		('왕실 판금 갑옷', fit(royal_plate)),
		('화염 대검', fit(flame_sword)),
		('고대 유물', fit(ancient_relic)),
	]


# ── 저장 · 미리보기 ──────────────────────────────────

ICON_ROOT = 'E:/GameProjects/ProjectOne/ProjectOne/Assets/Project/Art/UI/Atlas/Textures/Icons'
MISC_PREFIX = ('ItemIcon_Key', 'ItemIcon_BossStone', 'ItemIcon_Chest')


def install():
	import os
	for name, label, im in item_icons():
		folder = 'Items' if name.startswith(MISC_PREFIX) else 'Equipments'
		im.resize((N * 6, N * 6), Image.NEAREST).save(os.path.join(ICON_ROOT, folder, name + '.png'))
	print('installed')


def cell(icon, size=160):
	# 등급색 없이 기본 칸 위에 얹어 본다
	im = Image.new('RGBA', (size, size), (18, 17, 28, 255))
	d = ImageDraw.Draw(im)
	d.rectangle([4, 4, size - 5, size - 5], fill=(149, 144, 142, 255))
	d.rectangle([12, 12, size - 13, size - 13], fill=(42, 44, 62, 255))
	im.alpha_composite(icon.resize((N * 6, N * 6), Image.NEAREST), ((size - N * 6) // 2, (size - N * 6) // 2))
	return im


def preview(path):
	font = ImageFont.truetype('E:/GameProjects/ProjectOne/ProjectOne/Assets/Project/Art/UI/Fonts/Galmuri/Galmuri11-Bold.ttf', 22)
	big = ImageFont.truetype('E:/GameProjects/ProjectOne/ProjectOne/Assets/Project/Art/UI/Fonts/Galmuri/Galmuri11-Bold.ttf', 30)
	items = [(label, im) for name, label, im in item_icons()]
	show = showcase_icons()
	cols = 7
	cw = 176
	ch = 200
	rows = (len(items) + cols - 1) // cols
	h = 60 + rows * ch + 70 + ch
	sheet = Image.new('RGBA', (cols * cw + 20, h), (25, 26, 41, 255))
	d = ImageDraw.Draw(sheet)
	d.text((12, 14), '현재 아이템 (기본템)', font=big, fill=(244, 231, 200, 255))
	for i, (label, im) in enumerate(items):
		x = 10 + (i % cols) * cw
		y = 60 + (i // cols) * ch
		sheet.alpha_composite(cell(im), (x, y))
		d.text((x + 6, y + 164), label, font=font, fill=(184, 174, 154, 255))
	y0 = 60 + rows * ch + 10
	d.text((12, y0), '고급 아이템 예시 (게임에 넣지 않음)', font=big, fill=(255, 212, 71, 255))
	for i, (label, im) in enumerate(show):
		x = 10 + i * cw
		sheet.alpha_composite(cell(im), (x, y0 + 50))
		d.text((x + 6, y0 + 50 + 164), label, font=font, fill=(184, 174, 154, 255))
	sheet.save(path)


if __name__ == '__main__':
	if sys.argv[1] == '--install':
		install()
	else:
		preview(sys.argv[1])

# 도트 UI 스프라이트 생성기 — UI Toolkit 9-slice 용 작은 PNG 를 코드로 그린다.
#
# 사용: python generate_pixel_ui.py <출력 폴더>
#   예) python Tools/pixelui/generate_pixel_ui.py ProjectOne/Assets/Project/UIToolkit/Pixel
#
# 모양은 "바깥에서부터의 거리(d)"로 칠한다 — d=1 외곽선, d=2 첫 안쪽 테두리 …
# 그래서 크기를 바꿔도 같은 규칙으로 다시 그려진다. 색을 바꾸려면 PALETTE 만 고친다.
# 출력 PNG 는 Unity 에서 Point 필터·무압축으로 임포트해야 픽셀이 뭉개지지 않는다.

import os
import sys
from collections import deque
from PIL import Image

T = (0, 0, 0, 0)


def c(h, a=255):
	h = h.lstrip('#')
	return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# 다크 네이비 톤 — 바탕은 어둡게 가라앉히고, 선택(탭·행)만 보라로 띄운다(툴킷 전환 전 UGUI 내비 바의 색 체계).
PALETTE = {
	'ink': c('#0A0C16'),		# 외곽선
	'paper': c('#1A2036'),
	'paper_hi': c('#2B3456'),
	'paper_shade': c('#10142A'),
	'row': c('#222A44'),
	'row_line': c('#0A0C16'),
	'row_shade': c('#171D33'),
	'locked': c('#151926'),
	'locked_line': c('#0A0C16'),
	'wood': c('#262C55'),		# 제목 배너 — 남보라 네이비
	'wood_hi': c('#3A4280'),
	'wood_dark': c('#161A38'),
	'rivet': c('#FFD447'),
	'rivet_dark': c('#9C6A2A'),
	'inset': c('#10142A'),
	'inset_shade': c('#0A0D1C'),
	'inset_line': c('#0A0C16'),
	'slot_bg': c('#121729'),
	'slot_shadow': c('#0A0D1C'),
	'gauge_bg': c('#121729'),
	'gauge_shadow': c('#0A0D1C'),
	'tab_off': c('#171C30'),
	'tab_off_hi': c('#252C47'),
	'tab_off_dark': c('#0E1222'),
	'tab_on': c('#5B43B5'),		# 선택 탭 — 선명한 보라
	'tab_on_hi': c('#8E86F0'),
	'tab_on_dark': c('#3B2C80'),
	'white': c('#FFFFFF'),
}

# 버튼·칸·칩 색 세트: (기본, 밝은, 어두운)
TONES = {
	'green': (c('#5CC85A'), c('#A4EC7E'), c('#2F8C45')),
	'blue': (c('#4EA2F0'), c('#94D3FF'), c('#2C62B6')),
	'orange': (c('#FF9C3A'), c('#FFD36E'), c('#C65E28')),
	'red': (c('#EE5A48'), c('#FF9A7E'), c('#A8322E')),
	'gold': (c('#FFD447'), c('#FFF3A3'), c('#C98D1C')),
	'gray': (c('#B9AFA6'), c('#DDD5CD'), c('#857A73')),
	'purple': (c('#A57BF0'), c('#D2B8FF'), c('#6B47B8')),
}


# ── 모양 ─────────────────────────────────────────────

def shape_mask(w, h, r):
	# 모서리를 r 계단만큼 깎은 사각형
	mask = [[True] * w for _ in range(h)]
	for y in range(h):
		for x in range(w):
			for cx, cy in ((x, y), (w - 1 - x, y), (x, h - 1 - y), (w - 1 - x, h - 1 - y)):
				if cx < r and cy < r and cx + cy < r:
					mask[y][x] = False
	return mask


def distance(mask, open_bottom=False):
	# 바깥(마스크 밖 + 이미지 경계 밖)에서 4방향 거리. open_bottom 이면 아래 경계는 바깥으로 치지 않는다(탭).
	h = len(mask)
	w = len(mask[0])
	d = [[0] * w for _ in range(h)]
	q = deque()
	for y in range(h):
		for x in range(w):
			if mask[y][x] == False:
				continue
			edge = x == 0 or x == w - 1 or y == 0 or (y == h - 1 and open_bottom == False)
			near = False
			for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
				if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] == False:
					near = True
			if edge or near:
				d[y][x] = 1
				q.append((x, y))
	while q:
		x, y = q.popleft()
		for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
			if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and d[ny][nx] == 0:
				d[ny][nx] = d[y][x] + 1
				q.append((nx, ny))
	return d


def side(x, y, w, h, open_bottom=False):
	# 가장 가까운 변 — 'top' / 'bottom' / 'left' / 'right'
	dists = [(y, 'top'), (w - 1 - x, 'right'), (x, 'left')]
	if open_bottom == False:
		dists.append((h - 1 - y, 'bottom'))
	dists.sort(key=lambda t: t[0])
	return dists[0][1]


def paint(w, h, r, colorize, open_bottom=False):
	mask = shape_mask(w, h, r)
	d = distance(mask, open_bottom)
	im = Image.new('RGBA', (w, h), T)
	px = im.load()
	for y in range(h):
		for x in range(w):
			if mask[y][x]:
				px[x, y] = colorize(d[y][x], side(x, y, w, h, open_bottom), x, y)
	return im


# ── 스타일 ───────────────────────────────────────────

def bevel(outline, base, hi, dark, rings=None):
	# 볼록: 위·왼쪽 밝게, 아래 2줄·오른쪽 어둡게. rings 는 d 별 고정 색(안쪽 장식선).
	def f(d, s, x, y):
		if d == 1:
			return outline
		if rings and d in rings:
			return rings[d]
		if d == 2:
			return {'top': hi, 'left': hi, 'right': dark, 'bottom': dark}[s]
		if d == 3 and s == 'bottom':
			return dark
		return base
	return f


def inset(outline, base, shadow, light):
	# 오목: 위 2줄 그림자, 아래 밝게
	def f(d, s, x, y):
		if d == 1:
			return outline
		if d == 2:
			return {'top': shadow, 'left': shadow, 'right': light, 'bottom': light}[s]
		if d == 3 and s == 'top':
			return shadow
		return base
	return f


def slot(ring, ring_dark):
	P = PALETTE
	def f(d, s, x, y):
		if d == 1:
			return P['ink']
		if d == 2:
			return ring_dark if s in ('bottom', 'right') else ring
		if d == 3:
			return ring_dark
		if d == 4:
			return P['ink']
		if d == 5 and s in ('top', 'left'):
			return P['slot_shadow']
		return P['slot_bg']
	return f


def save(im, out, name):
	im.save(os.path.join(out, name + '.png'))


def add_shine(im, x, y, color):
	px = im.load()
	px[x, y] = color
	px[x + 1, y] = color


def add_rivets(im, inset_px):
	P = PALETTE
	w, h = im.size
	px = im.load()
	for x, y in ((inset_px, inset_px), (w - 1 - inset_px, inset_px), (inset_px, h - 1 - inset_px), (w - 1 - inset_px, h - 1 - inset_px)):
		px[x, y] = P['rivet']
		px[x, y + 1] = P['rivet_dark']


def lock_icon():
	# 9x11 자물쇠
	P = PALETTE
	art = [
		'..XXXXX..',
		'.XgggggX.',
		'.Xg...gX.',
		'.Xg...gX.',
		'XXXXXXXXX',
		'XyyyyyyyX',
		'XyyyXyyyX',
		'XyyyXyyyX',
		'XYYYYYYYX',
		'XYYYYYYYX',
		'XXXXXXXXX',
	]
	colors = {'X': P['ink'], 'g': c('#B9AFA6'), 'y': c('#FFD447'), 'Y': c('#C98D1C')}
	im = Image.new('RGBA', (9, 11), T)
	px = im.load()
	for y, row in enumerate(art):
		for x, ch in enumerate(row):
			if ch in colors:
				px[x, y] = colors[ch]
	return im


def close_icon():
	# 11x12 닫기 X — 흰 X + 진한 외곽선 + 아래 그림자 한 칸(.text-pop 과 같은 규칙)
	P = PALETTE
	size = 9
	core = set()
	for y in range(size):
		for x in range(size):
			if abs(x - y) <= 1 or abs(x + y - (size - 1)) <= 1:
				core.add((x + 1, y + 1))

	outline = set()
	for x, y in core:
		for dx in (-1, 0, 1):
			for dy in (-1, 0, 1):
				p = (x + dx, y + dy)
				if p not in core:
					outline.add(p)

	im = Image.new('RGBA', (11, 12), T)
	px = im.load()
	for x, y in outline:
		px[x, y + 1] = P['ink']		# 그림자
	for x, y in outline:
		px[x, y] = P['ink']
	for x, y in core:
		px[x, y] = P['white']
	return im


def bg_tile():
	# 16x16 어두운 석벽 벽돌 + 희미한 점
	base = c('#191A29')
	mortar = c('#22243A')
	spark = c('#3A3D5C')
	im = Image.new('RGBA', (16, 16), base)
	px = im.load()
	for y in range(16):
		for x in range(16):
			offset = 4 if (y // 4) % 2 == 1 else 0
			if y % 4 == 3 or (x + offset) % 8 == 7:
				px[x, y] = mortar
	for x, y in ((3, 4), (12, 11)):
		px[x, y] = spark
	return im


def main():
	out = sys.argv[1]
	os.makedirs(out, exist_ok=True)
	P = PALETTE

	save(bg_tile(), out, 'bg_tile')

	# 종이 패널 (24x24, slice 6) — 금색 장식선 없이 명암만
	panel = paint(24, 24, 3, bevel(P['ink'], P['paper'], P['paper_hi'], P['paper_shade']))
	save(panel, out, 'panel_paper')

	# 나무 배너 (24x24, slice 6) + 모서리 못
	wood = paint(24, 24, 3, bevel(P['ink'], P['wood'], P['wood_hi'], P['wood_dark'], {4: P['wood_dark']}))
	add_rivets(wood, 2)
	save(wood, out, 'panel_wood')

	# 오목 띠 (16x16, slice 4) — 스킬 칸 줄 바탕
	save(paint(16, 16, 2, inset(P['inset_line'], P['inset'], P['inset_shade'], P['paper_hi'])), out, 'panel_inset')

	# 목록 행 (16x16, slice 4)
	save(paint(16, 16, 2, bevel(P['row_line'], P['row'], P['paper_hi'], P['row_shade'])), out, 'row')
	save(paint(16, 16, 2, bevel(P['locked_line'], P['locked'], P['paper_hi'], P['row_shade'])), out, 'row_locked')
	g = TONES['gold']
	save(paint(16, 16, 2, bevel(P['ink'], P['row'], g[0], g[2], {3: g[1]})), out, 'row_selected')
	save(paint(16, 16, 2, bevel(P['ink'], c('#463F6E'), g[0], g[2], {3: g[1]})), out, 'row_active')

	# 버튼 (16x16, slice 5) — 왼쪽 위 반짝임
	for name, (base, hi, dark) in TONES.items():
		im = paint(16, 16, 2, bevel(P['ink'], base, hi, dark))
		add_shine(im, 3, 3, P['white'])
		save(im, out, 'btn_' + name)

	# 탭 (16x16, 아래 열림, slice 5)
	save(paint(16, 16, 2, bevel(P['ink'], P['tab_off'], P['tab_off_hi'], P['tab_off_dark']), open_bottom=True), out, 'tab_off')
	save(paint(16, 16, 2, bevel(P['ink'], P['tab_on'], P['tab_on_hi'], P['tab_on_dark']), open_bottom=True), out, 'tab_on')

	# 내비게이션 탭 (16x16, 사방 닫힘, slice 5) — 바 위에 떠 있는 타일이라 바닥을 열지 않는다
	save(paint(16, 16, 2, bevel(P['ink'], P['tab_off'], P['tab_off_hi'], P['tab_off_dark'])), out, 'navtab_off')
	save(paint(16, 16, 2, bevel(P['ink'], P['tab_on'], P['tab_on_hi'], P['tab_on_dark'])), out, 'navtab_on')

	# 스킬 칸 (20x20, slice 6)
	for name, tone in (('passive', 'green'), ('tactic', 'blue'), ('core', 'orange'), ('gold', 'gold'), ('empty', 'gray')):
		base, hi, dark = TONES[tone]
		save(paint(20, 20, 3, slot(base, dark)), out, 'slot_' + name)
	save(paint(20, 20, 3, slot(P['white'], c('#C9C2E8'))), out, 'slot_selected')

	# 칩 (8x8, slice 3)
	for name in ('green', 'blue', 'orange', 'gold', 'gray'):
		base, hi, dark = TONES[name]
		save(paint(8, 8, 1, bevel(P['ink'], base, hi, base)), out, 'chip_' + name)

	# 게이지 바탕 (12x12, slice 4)
	save(paint(12, 12, 1, inset(P['ink'], P['gauge_bg'], P['gauge_shadow'], P['gauge_bg'])), out, 'gauge_bg')

	save(lock_icon(), out, 'icon_lock')
	save(close_icon(), out, 'icon_close')
	print('done ->', out)


if __name__ == '__main__':
	main()

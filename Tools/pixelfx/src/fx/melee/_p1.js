// ProjectOne 추가 헬퍼 — 탑다운(중력 없는) 스킬 이펙트 공용. _ 로 시작하는 파일은 이펙트가 아니다.
// 게임은 위에서 내려다보는 시점이라 불꽃이 아래로 떨어지면 안 된다 — 끌림(drag)만 있는 자체 파편을 쓴다.
import { clamp, TAU } from "../../core/index.js";

/**
 * 끌림만 있는 줄무늬 파편. 속도 방향으로 늘어나고, 수명에 따라 ramp(어두움→밝음)를 거꾸로 식어간다.
 * add(x, y, vx, vy, life, len, drag, delay, glow)
 */
export class Streaks {
	constructor(ramp) {
		this.ramp = ramp;
		this.list = [];
	}
	add(x, y, vx, vy, life, len = 4, drag = 6, delay = 0, glow = 0.25) {
		this.list.push({ x, y, vx, vy, life, len, drag, delay, glow, age: 0 });
	}
	update(dt) {
		for (const s of this.list) {
			if (s.delay > 0) {
				s.delay -= dt;
				continue;
			}
			s.age += dt;
			const k = Math.exp(-s.drag * dt);
			s.vx *= k;
			s.vy *= k;
			s.x += s.vx * dt;
			s.y += s.vy * dt;
		}
	}
	draw(p) {
		const R = this.ramp, n = R.length;
		for (const s of this.list) {
			if (s.delay > 0 || s.age >= s.life) continue;
			const u = s.age / s.life, sp = Math.hypot(s.vx, s.vy) || 1;
			const L = Math.min(s.len * (1 - u * 0.6), sp * 0.06 + 1);
			const x0 = s.x - (s.vx / sp) * L, y0 = s.y - (s.vy / sp) * L;
			const head = R[clamp(Math.round((n - 1) * (1 - u * 0.85)), 0, n - 1)];
			const tail = R[clamp(Math.round((n - 1) * (1 - u) * 0.6), 0, n - 1)];
			p.line(x0, y0, s.x, s.y, (v) => (v > 0.6 ? head : tail), u > 0.75 ? 1 - (u - 0.75) / 0.25 : 1);
			if (s.glow > 0) p.glow(s.x, s.y, 4, R[n - 2], s.glow * (1 - u));
		}
	}
}

/** 시전자 쪽 짧은 섬광: 흰 팔 4개짜리 별이 빠르게 줄어든다. */
export function flash(p, x, y, arm, c, rim, k) {
	if (arm < 0.5) return;
	for (let d = -arm; d <= arm; d += 0.5) {
		const w = Math.max(0, 1.2 * (1 - Math.abs(d) / arm));
		for (let o = -w; o <= w; o += 0.5) {
			p.px(x + d, y + o, Math.abs(d) < arm * 0.45 ? c : rim);
			p.px(x + o, y + d, Math.abs(d) < arm * 0.45 ? c : rim);
		}
	}
	p.glow(x, y, arm * 2.5, rim, k);
}

/** 반지름 r 의 원 둘레 점선 — 범위 경계를 잠깐 그려 준다. */
export function dottedRing(p, x, y, r, c, a, phase = 0, gap = 3) {
	const n = Math.max(12, Math.round((TAU * r) / gap));
	for (let i = 0; i < n; i++) {
		const g = (i / n) * TAU + phase;
		p.dot(x + Math.cos(g) * r, y + Math.sin(g) * r, c, a);
	}
}

/**
 * 곧은 렌즈형 베기 한 줄: (x0,y0)→(x1,y1). head 0..1 칼끝이 그은 정도, tail 0..1 꼬리가 사라진 정도.
 * 가운데가 가장 두껍고(W) 양 끝은 머리카락처럼 가늘다. 중심선은 흰색, 바깥으로 ramp 를 따라 어두워지고 age 로 식는다.
 */
export function lensSlash(p, x0, y0, x1, y1, W, head, tail, age, R) {
	if (head - tail < 0.01 || W < 0.3) return;
	const n = R.length, dx = x1 - x0, dy = y1 - y0, L = Math.hypot(dx, dy), ux = dx / L, uy = dy / L;
	const pad = W + 1;
	p.shade(Math.min(x0, x1) - pad, Math.min(y0, y1) - pad, Math.max(x0, x1) + pad, Math.max(y0, y1) + pad, (x, y, out) => {
		const rx = x - x0, ry = y - y0, v = (rx * ux + ry * uy) / L, d = Math.abs(-rx * uy + ry * ux);
		if (v < tail || v > head) return;
		const s = (v - tail) / (head - tail), w = W * Math.pow(Math.sin(Math.PI * s), 0.6) * (1 - tail * 0.5);
		if (w < 0.4 || d > w) return;
		const q = d / w;
		const ci = q < 0.3 && age < 0.45 ? 0 : Math.round(q * (n - 1) * 0.7 + age * 3);
		out.c = R[n - 1 - clamp(ci, 0, n - 1)];
	});
	const k = 0.3 * (1 - age);
	if (k > 0.01) p.glowLine(x0 + dx * tail, y0 + dy * tail, x0 + dx * head, y0 + dy * head, W + 4, R[n - 3], k);
}

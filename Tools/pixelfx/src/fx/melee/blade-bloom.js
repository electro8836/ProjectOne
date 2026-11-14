// ProjectOne 추가 이펙트 — 원형 범위 연속 베기 후 피꽃 (탑다운, 원점 = 시전자 몸 중앙)
// 반지름 radius 의 원 안을 곧은 베기 cuts 번(gap 초 간격)이 서로 다른 각도로 가로지르고, 마지막에 꽃잎 모양으로 피가 핀다.
import { Rng, fit, clamp, smooth, ease, TAU, WHITE } from "../../core/index.js";
import { Streaks, flash, lensSlash, dottedRing } from "./_p1.js";

export const meta = {
	name: "Blade bloom",
	desc: "Seen from above, straight cuts cross a circle one after another at turning angles, each spraying blood; on the last one the blood opens into a flower of petals and mist.",
	tags: ["melee", "slash", "flurry", "aoe", "circle", "blood", "skill", "one-shot"],
	size: [184, 184],
	origin: [92, 92],
	duration: 1.45,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#14040a", "#3a0a14", "#6a101c", "#a01824", "#d42a30", "#ff5a50", "#ff9a8a", "#ffffff"],
		radius: 80,
		cuts: 6,
		gap: 0.15,
		seed: 17,
	},
};

const CUT = 0.05; // 한 줄을 긋는 시간

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed), R = o.radius;
	const drops = new Streaks(C.slice(1, 7)), sparks = new Streaks(C.slice(3));
	const cuts = [];
	let a = Rg.range(0, TAU);
	for (let k = 0; k < o.cuts; k++) {
		a += Math.PI / o.cuts * 2 + Rg.range(0.25, 0.6); // 매번 꺾이는 각도
		const off = Rg.range(-0.25, 0.25) * R, ca = Math.cos(a), sa = Math.sin(a);
		const half = Math.sqrt(R * R - off * off) * 0.95;
		const ox = -sa * off, oy = ca * off;
		cuts.push({ t0: k * o.gap, x0: ox - ca * half, y0: oy - sa * half, x1: ox + ca * half, y1: oy + sa * half, nx: -sa, ny: ca, W: Math.max(4, R * 0.08), fired: false });
	}
	const bloomAt = cuts[cuts.length - 1].t0 + CUT + 0.04;
	const petals = [];
	for (let i = 0; i < 6; i++) petals.push({ a: (i / 6) * TAU + Rg.range(-0.15, 0.15), len: R * Rg.range(0.45, 0.6) });
	const mists = [];
	let t = 0, bloomed = false;

	return {
		update(dt) {
			t += dt;
			for (const c of cuts) {
				if (c.fired || t < c.t0 + CUT * 0.7) continue;
				c.fired = true;
				// 베인 선 양옆으로 피가 튄다
				for (let i = 0; i < 12; i++) {
					const v = Rg.range(0.15, 0.85), x = c.x0 + (c.x1 - c.x0) * v, y = c.y0 + (c.y1 - c.y0) * v, sg = Rg.sign(), sp = Rg.range(40, 120);
					drops.add(x, y, c.nx * sg * sp + Rg.range(-20, 20), c.ny * sg * sp + Rg.range(-20, 20), Rg.range(0.2, 0.4), 3, 6, 0, 0.08);
				}
				for (let i = 0; i < 5; i++) {
					const sp = Rg.range(100, 200), dx = c.x1 - c.x0, dy = c.y1 - c.y0, l = Math.hypot(dx, dy);
					sparks.add(c.x1, c.y1, (dx / l) * sp + Rg.range(-30, 30), (dy / l) * sp + Rg.range(-30, 30), Rg.range(0.1, 0.2), 4, 9);
				}
				const v = Rg.range(0.3, 0.7);
				mists.push({ x: c.x0 + (c.x1 - c.x0) * v, y: c.y0 + (c.y1 - c.y0) * v, r: Rg.range(6, 10), at: t, life: 0.5 });
			}
			if (!bloomed && t >= bloomAt) {
				bloomed = true;
				for (let i = 0; i < 30; i++) {
					const g = Rg.range(0, TAU), sp = Rg.range(60, 180);
					drops.add(Math.cos(g) * 4, Math.sin(g) * 4, Math.cos(g) * sp, Math.sin(g) * sp, Rg.range(0.25, 0.5), 4, 5, 0, 0.1);
				}
				for (const pt of petals) mists.push({ x: Math.cos(pt.a) * pt.len * 0.6, y: Math.sin(pt.a) * pt.len * 0.6, r: R * 0.14, at: t, life: 0.55 });
			}
			drops.update(dt);
			sparks.update(dt);
		},
		draw(p) {
			// 범위 경계 — 처음에 번지고, 베는 동안 은은히 남는다
			const ringA = smooth(0, 0.08, t) * (1 - smooth(bloomAt, bloomAt + 0.3, t));
			if (ringA > 0.02) dottedRing(p, 0, 0, R - 1, C[3], 0.6 * ringA, t * 0.8, 3);

			for (const m of mists) {
				const u = (t - m.at) / m.life;
				if (u < 0 || u >= 1) continue;
				const r = m.r * (0.6 + 0.6 * ease.outQuad(u));
				p.disc(m.x, m.y, r, C[3], 0.3 * (1 - u) * (1 - u));
				p.disc(m.x - r * 0.2, m.y - r * 0.2, r * 0.5, C[5], 0.18 * (1 - u) * (1 - u));
			}

			// 피꽃 — 꽃잎이 중심에서 벌어졌다가 가장자리부터 말라 간다
			const bd = t - bloomAt;
			if (bd > 0 && bd < 0.5) {
				const u = bd / 0.5, open = ease.outBack(clamp(bd / 0.12)), age = clamp(u * 1.3);
				for (const pt of petals) {
					const L = pt.len * open, ca = Math.cos(pt.a), sa = Math.sin(pt.a), W = R * 0.09 * (1 - u * 0.4);
					lensSlash(p, ca * 4, sa * 4, ca * (4 + L), sa * (4 + L), W, 1, smooth(0.25, 1, u), age, C);
				}
				if (bd < 0.12) flash(p, 0, 0, 14 * (1 - bd / 0.12), bd < 0.04 ? WHITE : C[n - 2], C[n - 3], 0.6 * (1 - bd / 0.12));
				p.glow(0, 0, R * 0.7, C[4], 0.3 * (1 - u));
			}

			for (const c of cuts) {
				const u = t - c.t0;
				if (u < 0 || u > 0.3) continue;
				const head = ease.outExpo(clamp(u / CUT)), tail = smooth(CUT + 0.03, 0.26, u);
				lensSlash(p, c.x0, c.y0, c.x1, c.y1, c.W, head, tail, clamp(u / 0.28), C);
			}

			drops.draw(p);
			sparks.draw(p);
		},
	};
}

// ProjectOne 추가 이펙트 — 원형 범위 회전베기 (탑다운, 원점 = 시전자 몸 중앙)
// 반지름 radius 의 원을 칼날이 spins 번(gap 초 간격) 휘돈다. 피해 횟수·간격과 맞춘다. parry 면 시작에 막기 섬광이 터진다.
import { Rng, fit, clamp, ease, TAU, WHITE } from "../../core/index.js";
import { swing, slash, onSwing } from "./_lib.js";
import { Streaks, flash, dottedRing } from "./_p1.js";

export const meta = {
	name: "Whirl ring",
	desc: "Seen from above, a blade whirls a full circle round the caster one or more times: crescent smears with an inner echo, wind streaks, sparks flung off the rim and a pulse of the ring's edge.",
	tags: ["melee", "slash", "spin", "aoe", "circle", "skill", "one-shot"],
	size: [184, 184],
	origin: [92, 92],
	duration: 0.8,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#0c1430", "#162c5c", "#1f4f94", "#2f7fcc", "#58b4ec", "#9bdcff", "#d8f4ff", "#ffffff"],
		radius: 80,
		spins: 3,
		gap: 0.15,
		parry: false,
		seed: 9,
	},
};

const SWEEP = 0.09;

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed), R = o.radius;
	const sparks = new Streaks(C.slice(2)), winds = new Streaks(C.slice(1, 7));
	const t0 = o.parry ? 0.05 : 0;
	const spins = [];
	for (let k = 0; k < o.spins; k++) {
		const a0 = Rg.range(0, TAU), at = t0 + k * o.gap;
		const W = Math.max(8, R * 0.2);
		const main = swing({ cx: 0, cy: 0, R: R - 1, sy: 1, rot: 0, a0, dir: -1, span: TAU * 0.8, W, t0: at, sweep: SWEEP, hold: 0.015, trail: 0.12, ramp: C, hot: k === 0 ? 0.5 : 0.2 });
		const echo = { ...main, R: R * 0.72, W: W * 0.4, t0: at + 0.03, span: TAU * 0.6, trail: 0.1, ramp: C.slice(0, 7), hot: 0 };
		spins.push({ at, main, echo, fired: false });
	}
	const lastAt = spins[spins.length - 1].at;

	let t = 0;
	return {
		update(dt) {
			t += dt;
			for (const s of spins) {
				if (s.fired || t < s.at + SWEEP * 0.5) continue;
				s.fired = true;
				// 테두리에서 접선 방향으로 튀는 불꽃, 안쪽을 휘감는 바람줄
				for (let i = 0; i < 14; i++) {
					const g = Rg.range(0, TAU), [x, y] = onSwing({ ...s.main, a0: 0, dir: 1 }, R, g);
					const tx = Math.sin(g), ty = -Math.cos(g), sp = Rg.range(80, 200), out = Rg.range(20, 60);
					sparks.add(x, y, tx * sp + Math.cos(g) * out, ty * sp + Math.sin(g) * out, Rg.range(0.12, 0.26), 5, 7);
				}
				for (let i = 0; i < 10; i++) {
					const g = Rg.range(0, TAU), r = R * Rg.range(0.3, 0.85), sp = Rg.range(120, 220);
					winds.add(Math.cos(g) * r, Math.sin(g) * r, Math.sin(g) * sp, -Math.cos(g) * sp, Rg.range(0.1, 0.2), 9, 5, 0, 0.1);
				}
			}
			sparks.update(dt);
			winds.update(dt);
		},
		draw(p) {
			// 막기 성공 섬광 — 반격의 시작
			if (o.parry && t < 0.16) {
				const f = t / 0.16;
				flash(p, 0, 0, 16 * (1 - ease.inQuad(f)), t < 0.04 ? WHITE : C[n - 2], C[n - 3], 0.6 * (1 - f));
				p.ring(0, 0, 4 + 14 * ease.outQuart(f), f < 0.4 ? 2 : 1, f < 0.5 ? C[n - 1] : C[n - 3], 1 - f);
			}

			// 범위 경계가 회전마다 한 번씩 빛난다
			for (const s of spins) {
				const d = t - s.at - SWEEP * 0.6;
				if (d < 0 || d > 0.22) continue;
				const u = d / 0.22;
				dottedRing(p, 0, 0, R - 0.5 + u * 3, u < 0.3 ? C[n - 2] : C[n - 4], 1 - u, s.at * 7, 2.5);
				p.glowRing(0, 0, R, 5, C[n - 3], 0.18 * (1 - u));
			}

			winds.draw(p);
			for (const s of spins) {
				slash(p, s.echo, t);
				slash(p, s.main, t);
			}

			// 시전자 중심의 빛
			const live = t - t0 < lastAt + 0.25;
			if (live) p.glow(0, 0, R * 0.6, C[n - 4], 0.25 * (1 - clamp((t - lastAt) / 0.3)));
			sparks.draw(p);
		},
	};
}

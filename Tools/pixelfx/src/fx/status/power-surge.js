// ProjectOne 추가 이펙트 — 자기 강화(버프) 발동 (탑다운, 원점 = 시전자 몸 중앙)
// style:
//   rage   — 바닥 충격 고리 두 겹, 몸에서 열기 불길이 치솟고 불티가 튄다 (폭주)
//   storm  — 바람 띠가 몸을 휘감으며 조여 들었다가 회오리로 터진다 (폭풍 투척)
//   focus  — 조준경 고리가 좁혀 들어와 고정되며 번쩍인다 (데드아이)
//   shadow — 그림자 기운이 빨려 들었다가 칼날 셋이 몸 주위를 돌며 퍼진다 (그림자 칼날)
import { Rng, fit, clamp, smooth, ease, TAU, WHITE } from "../../core/index.js";
import { Streaks, flash, dottedRing } from "../melee/_p1.js";

export const meta = {
	name: "Power surge",
	desc: "A self buff goes off round the caster, seen from above: a rage shock with rising heat, a tightening wind spiral, a scope locking on, or shadow thrown out as orbiting blades.",
	tags: ["buff", "aura", "power", "cast", "skill", "one-shot"],
	size: [128, 128],
	origin: [64, 64],
	duration: 0.9,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#1f0b10", "#8c1d1c", "#cc3a1a", "#f2701f", "#ffae35", "#ffe27a", "#fff8d6", "#ffffff"],
		style: "rage",
		radius: 44,
		seed: 21,
	},
};

const GATHER = { rage: 0, storm: 0.22, focus: 0.24, shadow: 0.22 };

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed), R = o.radius, st = o.style;
	const sparks = new Streaks(C.slice(2)), rise = new Streaks(C.slice(1, 7)), pull = new Streaks(C.slice(1, 6));
	const T0 = GATHER[st] ?? 0; // 모였다가 터지는 시각
	let t = 0, burst = false;

	// 모으기 단계: 바깥에서 중심으로 빨려 드는 줄
	if (st === "shadow" || st === "storm") {
		for (let i = 0; i < 18; i++) {
			const g = Rg.range(0, TAU), r = R * Rg.range(0.9, 1.3), d = Rg.range(0, T0 * 0.6), life = T0 - d;
			const sw = st === "storm" ? 1.1 : 0; // 폭풍은 비스듬히 휘감아 든다
			const sp = (r / life) * 1.6, dir = g + Math.PI + sw;
			pull.add(Math.cos(g) * r, Math.sin(g) * r, Math.cos(dir) * sp, Math.sin(dir) * sp, life, 8, 2, d, 0.1);
		}
	}

	const blades = [0, 1, 2].map((i) => ({ a: (i / 3) * TAU + Rg.range(-0.2, 0.2) }));

	return {
		update(dt) {
			t += dt;
			if (!burst && t >= T0) {
				burst = true;
				for (let i = 0; i < 20; i++) {
					const g = Rg.range(0, TAU), sp = Rg.range(60, 170);
					sparks.add(Math.cos(g) * 4, Math.sin(g) * 4, Math.cos(g) * sp, Math.sin(g) * sp, Rg.range(0.15, 0.35), 4, 6);
				}
			}
			// 폭주: 몸에서 열기가 계속 솟는다
			if (st === "rage" && t < 0.5 && Rg.chance(dt * 140)) {
				const x = Rg.range(-12, 12);
				rise.add(x, Rg.range(-4, 10), Rg.range(-8, 8), -Rg.range(90, 160), Rg.range(0.2, 0.35), 7, 2.5, 0, 0.15);
			}
			sparks.update(dt);
			rise.update(dt);
			pull.update(dt);
		},
		draw(p) {
			const d = t - T0;

			if (st === "rage") {
				// 바닥 충격 고리 두 겹
				for (const [t0, rr, w] of [[0, R, 4], [0.1, R * 0.7, 3]]) {
					const u = clamp((t - t0) / 0.3);
					if (t < t0 || u >= 1) continue;
					const r = 6 + (rr - 6) * ease.outQuart(u);
					p.ring(0, 0, r, Math.max(1, w * (1 - u)), u < 0.25 ? C[n - 1] : u < 0.55 ? C[n - 3] : C[n - 5], 1 - u * 0.7);
					p.glowRing(0, 0, r, 6, C[n - 4], 0.25 * (1 - u));
				}
				const k = 1 - smooth(0.2, 0.7, t);
				if (k > 0) p.glow(0, 0, 30, C[3], 0.4 * k);
			}

			if (st === "storm") {
				// 바람 띠 세 줄이 돌며 조여 든다 → 터지면 바깥으로 풀린다
				for (let i = 0; i < 3; i++) {
					const ph = (i / 3) * TAU;
					let r, a0, k;
					if (d < 0) {
						const u = t / T0;
						(r = R * (1 - 0.65 * ease.inQuad(u))), (a0 = ph + u * 9), (k = smooth(0, 0.08, t));
					} else {
						const u = clamp(d / 0.35);
						(r = R * 0.35 + R * 0.75 * ease.outQuart(u)), (a0 = ph + 9 + u * 5), (k = 1 - u);
					}
					if (k <= 0) continue;
					const span = 1.6;
					p.arc(0, 0, r, d < 0 ? 2 : Math.max(1, 3 * k), a0, a0 + span, (x, y) => C[n - 2], k);
					p.arc(0, 0, r - 3, 1, a0 + 0.3, a0 + span * 0.8, C[n - 4], k * 0.8);
				}
			}

			if (st === "focus") {
				// 조준경: 바깥 고리와 눈금 네 개가 좁혀 들며 돈다 → 고정되면 번쩍
				const u = clamp(t / T0), lock = d >= 0;
				const r = lock ? 12 + 4 * ease.outQuart(clamp(d / 0.1)) : R - (R - 12) * ease.inOutCubic(u);
				const rot = lock ? Math.PI / 4 : (1 - u) * 1.2 + Math.PI / 4;
				const k = lock ? 1 - smooth(0.25, 0.55, d) : smooth(0, 0.06, t);
				if (k > 0) {
					const c = lock && d < 0.06 ? WHITE : C[n - 2];
					p.ring(0, 0, r, 1, c, k);
					for (let i = 0; i < 4; i++) {
						const g = rot + (i * TAU) / 4, ca = Math.cos(g), sa = Math.sin(g);
						p.line(ca * (r - 3), sa * (r - 3), ca * (r + 6), sa * (r + 6), c, k);
					}
					dottedRing(p, 0, 0, r + 9, C[n - 4], 0.7 * k, rot, 3);
				}
				if (lock && d < 0.3) {
					const f = d / 0.3;
					p.ring(0, 0, 14 + R * ease.outQuart(f), f < 0.3 ? 2 : 1, f < 0.3 ? C[n - 1] : C[n - 3], 1 - f);
				}
			}

			if (st === "shadow") {
				// 터지면 그림자 칼날 셋이 몸 주위를 돌며 바깥으로 벌어진다
				if (d >= 0 && d < 0.55) {
					const u = d / 0.55, r = 10 + (R - 10) * ease.outQuart(u), k = 1 - smooth(0.45, 1, u);
					for (const b of blades) {
						const g = b.a + d * 9, x = Math.cos(g) * r, y = Math.sin(g) * r, tx = -Math.sin(g), ty = Math.cos(g);
						// 진행 방향으로 뾰족한 초승달 칼날 + 뒤따르는 궤적
						for (let j = 0; j < 10; j++) {
							const back = g - j * 0.06, bx = Math.cos(back) * r, by = Math.sin(back) * r;
							p.dot(bx, by, j < 2 ? C[n - 1] : j < 5 ? C[n - 3] : C[n - 5], k * (1 - j / 10));
						}
						p.tri(x + tx * 5, y + ty * 5, x - tx * 3 + Math.cos(g) * 2.5, y - ty * 3 + Math.sin(g) * 2.5, x - tx * 3 - Math.cos(g) * 2.5, y - ty * 3 - Math.sin(g) * 2.5, C[n - 2], k);
						p.glow(x, y, 8, C[n - 3], 0.3 * k);
					}
				}
				if (d >= 0 && d < 0.25) {
					const f = d / 0.25;
					p.ring(0, 0, 6 + R * 0.8 * ease.outQuart(f), f < 0.3 ? 2 : 1, f < 0.35 ? C[n - 2] : C[n - 4], 1 - f);
				}
				if (d < 0) p.glow(0, 0, 10 + 14 * (t / T0), C[3], 0.35 * (t / T0));
			}

			pull.draw(p);
			rise.draw(p);

			// 터지는 순간 중심 섬광
			if (d >= 0 && d < 0.14) {
				const f = d / 0.14;
				flash(p, 0, 0, 13 * (1 - ease.inQuad(f)), d < 0.04 ? WHITE : C[n - 2], C[n - 3], 0.6 * (1 - f));
			}
			sparks.draw(p);
		},
	};
}

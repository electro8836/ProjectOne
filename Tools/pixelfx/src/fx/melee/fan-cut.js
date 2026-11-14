// ProjectOne 추가 이펙트 — 부채꼴 범위 베기 (탑다운, 원점 = 시전자 몸 중앙, 오른쪽 = 전방)
// 반지름 radius, 각도 spread(도)의 부채꼴을 덮는다. style:
//   gale  — 칼바람 세 줄기가 부채꼴을 번갈아 휩쓸고 피안개가 흩날린다 (혈풍)
//   cross — 두 줄 베기가 X 자로 교차하고 교점에서 터진다 (마무리 일격)
//   burst — 부채꼴 안 곳곳에서 피가 연달아 터진다 (피의 폭발)
import { Rng, fit, clamp, ease, smooth, TAU, WHITE } from "../../core/index.js";
import { swing, slash } from "./_lib.js";
import { Streaks, flash, lensSlash } from "./_p1.js";

export const meta = {
	name: "Fan cut",
	desc: "Seen from above, a cone in front of the caster is cut: sweeping wind blades, a crossing X slash or chained blood bursts, each sized to the cone's radius and spread.",
	tags: ["melee", "slash", "cone", "sector", "directional", "skill", "one-shot"],
	size: [80, 96],
	origin: [14, 48],
	duration: 0.6,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#14040a", "#3a0a14", "#6a101c", "#a01824", "#d42a30", "#ff5a50", "#ff9a8a", "#ffffff"],
		radius: 48,
		spread: 90,
		style: "gale",
		seed: 11,
	},
};

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed), R = o.radius, half = (o.spread * Math.PI) / 360;
	const sparks = new Streaks(C.slice(2)), drops = new Streaks(C.slice(1, 6));
	const mists = [];
	let t = 0;
	const events = []; // { t, fn } 한 번만 실행

	// 부채꼴 안 임의의 점 (가장자리 쪽에 더 많이)
	const inFan = (rMin = 0.35) => {
		const a = Rg.range(-half, half) * 0.9, r = R * Math.sqrt(Rg.range(rMin * rMin, 0.95));
		return [Math.cos(a) * r, Math.sin(a) * r];
	};
	const spray = (x, y, cnt, sp0, sp1, dirA, cone, life = 0.3) => {
		for (let i = 0; i < cnt; i++) {
			const a = dirA + Rg.range(-cone, cone), sp = Rg.range(sp0, sp1);
			drops.add(x, y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(life * 0.5, life), 3, 7, 0, 0.12);
		}
	};
	const mist = (x, y, r, at, life) => mists.push({ x, y, r, at, life });

	// ---- 스타일별 구성
	const swings = [], lines = [], pops = [];
	if (o.style === "gale") {
		for (let k = 0; k < 3; k++) {
			const dir = k % 2 ? -1 : 1, r = R * (0.98 - k * 0.17);
			swings.push(swing({ cx: 0, cy: 0, R: r, sy: 1, rot: 0, a0: -dir * half * 1.1, dir, span: half * 2.2, W: Math.max(4, r * 0.16), t0: k * 0.05, sweep: 0.07, hold: 0.01, trail: 0.12, ramp: C, hot: k === 0 ? 0.4 : 0 }));
			events.push({ t: k * 0.05 + 0.05, fn: () => {
				for (let i = 0; i < 5; i++) {
					const [x, y] = inFan(0.5);
					spray(x, y, 3, 40, 110, Math.atan2(y, x), 0.6);
				}
				const [mx, my] = inFan(0.4);
				mist(mx, my, Rg.range(5, 8), t, 0.4);
			} });
		}
	} else if (o.style === "cross") {
		const cx = R * 0.55, L = R * 0.62;
		lines.push({ a: -0.75, t0: 0, cx, L, W: Math.max(4, R * 0.12) });
		lines.push({ a: 0.75, t0: 0.07, cx, L, W: Math.max(4, R * 0.12) });
		events.push({ t: 0.1, fn: () => {
			for (let i = 0; i < 14; i++) {
				const a = Rg.range(0, TAU), sp = Rg.range(70, 170);
				sparks.add(cx, 0, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.12, 0.26), 4, 8);
			}
			mist(cx, 0, R * 0.22, t, 0.35);
		} });
	} else {
		// burst: 먼저 부채꼴로 붉은 맥동이 퍼지고, 그 안에서 피가 연달아 터진다
		for (let k = 0; k < 6; k++) {
			const [x, y] = inFan(0.4);
			pops.push({ x, y, t0: 0.06 + k * 0.035 + Rg.range(0, 0.02), r: Rg.range(5, 8) });
		}
		for (const pp of pops) {
			events.push({ t: pp.t0, fn: () => {
				spray(pp.x, pp.y, 7, 50, 130, 0, Math.PI, 0.34);
				mist(pp.x, pp.y, pp.r, t, 0.45);
			} });
		}
	}

	return {
		update(dt) {
			t += dt;
			for (const e of events) {
				if (!e.done && t >= e.t) {
					e.done = true;
					e.fn();
				}
			}
			sparks.update(dt);
			drops.update(dt);
		},
		draw(p) {
			// 반투명 피안개
			for (const m of mists) {
				const u = (t - m.at) / m.life;
				if (u < 0 || u >= 1) continue;
				const r = m.r * (0.6 + 0.7 * ease.outQuad(u));
				p.disc(m.x, m.y, r, C[3], 0.35 * (1 - u) * (1 - u));
				p.disc(m.x - r * 0.2, m.y - r * 0.2, r * 0.55, C[5], 0.22 * (1 - u) * (1 - u));
			}

			if (o.style === "burst") {
				// 범위를 알리는 맥동 — 부채꼴 호가 바깥으로 번진다
				const u = clamp(t / 0.16);
				if (u < 1) {
					const r = 6 + (R - 6) * ease.outQuart(u);
					p.arc(0, 0, r, u < 0.5 ? 2 : 1, -half, half, u < 0.4 ? C[n - 2] : C[n - 4], 1 - u * 0.6);
					for (let g = -half; g <= half; g += 0.2) p.glow(Math.cos(g) * r, Math.sin(g) * r, 6, C[4], 0.12 * (1 - u));
				}
				for (const pp of pops) {
					const d = t - pp.t0;
					if (d < 0 || d > 0.22) continue;
					const f = d / 0.22;
					if (d < 0.06) p.circle(pp.x, pp.y, pp.r * 0.6 * (1 - d / 0.06) + 1, d < 0.025 ? WHITE : C[n - 2]);
					p.ring(pp.x, pp.y, 2 + pp.r * ease.outQuart(f), f < 0.4 ? 2 : 1, f < 0.35 ? C[n - 2] : C[n - 4], 1 - f);
					p.glow(pp.x, pp.y, pp.r * 2, C[5], 0.35 * (1 - f));
				}
			}

			for (const s of swings) slash(p, s, t);

			for (const l of lines) {
				const u = t - l.t0;
				if (u < 0) continue;
				const head = ease.outExpo(clamp(u / 0.06)), tail = smooth(0.09, 0.24, u), age = clamp(u / 0.26);
				const ca = Math.cos(l.a), sa = Math.sin(l.a);
				lensSlash(p, l.cx - ca * l.L, -sa * l.L, l.cx + ca * l.L, sa * l.L, l.W, head, tail, age, C);
			}
			if (o.style === "cross") {
				const d = t - 0.09;
				if (d > 0 && d < 0.16) {
					const f = d / 0.16;
					flash(p, R * 0.55, 0, 13 * (1 - ease.inQuad(f)), d < 0.035 ? WHITE : C[n - 2], C[n - 3], 0.6 * (1 - f));
					p.ring(R * 0.55, 0, 3 + R * 0.3 * ease.outQuart(f), 1, C[n - 3], 1 - f);
				}
			}

			// 시전 순간 손 앞 작은 섬광
			if (t < 0.07) flash(p, 6, 0, 6 * (1 - t / 0.07), WHITE, C[n - 3], 0.3);

			drops.draw(p);
			sparks.draw(p);
		},
	};
}

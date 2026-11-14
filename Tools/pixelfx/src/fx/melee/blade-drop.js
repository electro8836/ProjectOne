// ProjectOne 추가 이펙트 — 거대한 검이 직선 범위에 내리꽂힌다 (탑다운, 원점 = 시전자 몸 중앙, 오른쪽 = 전방)
// 길이 length, 폭 width 의 직선 범위를 덮는다. hits 번 충격이 gap 초 간격으로 땅을 울린다(피해 횟수와 맞춘다).
import { Rng, fit, clamp, smooth, ease, mix, scale, hex, WHITE } from "../../core/index.js";
import { Streaks, flash } from "./_p1.js";

export const meta = {
	name: "Blade drop",
	desc: "A giant spectral blade falls onto a straight lane and slams flat: a white flash, then shock fronts, dust and stone chips on every pulse while the floor cracks glow and cool.",
	tags: ["melee", "slam", "sword", "line", "directional", "skill", "one-shot"],
	size: [140, 104],
	origin: [14, 52],
	duration: 0.95,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#0c1430", "#162c5c", "#1f4f94", "#2f7fcc", "#58b4ec", "#9bdcff", "#d8f4ff", "#ffffff"],
		stone: "#6a6378",
		length: 112,
		width: 64,
		hits: 3,
		gap: 0.2,
		seed: 7,
	},
};

const FALL = 0.07; // 검이 떨어지는 시간

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed);
	const L = o.length, h = o.width / 2, hits = o.hits, gap = o.gap;
	const st = hex(o.stone), stoneRamp = [scale(st, 0.45), scale(st, 0.7), st, mix(scale(st, 1.35), WHITE, 0.1)];
	const chips = new Streaks(stoneRamp), sparks = new Streaks(C.slice(2));
	const bx0 = 6, bx1 = L - 2, bw = Math.max(3, h * 0.26); // 칼날 구간과 반폭
	const hitT = (k) => FALL + k * gap;
	const lastHit = hitT(hits - 1);

	// 칼날 축을 따라 갈라지는 균열(지그재그) + 잔가지
	const cracks = [];
	{
		const main = [[bx0 + 8, 0]];
		let x = bx0 + 8, y = 0;
		while (x < bx1 - 4) {
			x += Rg.range(3, 6);
			y = clamp(y + Rg.range(-2.2, 2.2), -bw * 0.6, bw * 0.6);
			main.push([x, y]);
			if (Rg.chance(0.35)) {
				const br = [[x, y]], sg = Rg.sign();
				let fx = x, fy = y;
				for (let j = Rg.int(2, 4); j > 0; j--) {
					fx += Rg.range(1, 4);
					fy += sg * Rg.range(2, 4.5);
					br.push([fx, clamp(fy, -h + 2, h - 2)]);
				}
				cracks.push({ pts: br, at: (x - bx0) / (bx1 - bx0) });
			}
		}
		cracks.unshift({ pts: main, at: 0 });
	}

	const puffs = [];
	let t = 0, fired = 0;

	function pulse(k) {
		// 충격마다 범위 양쪽 가장자리로 먼지와 돌조각이 튄다
		const big = k === 0 ? 1.4 : 1;
		for (let i = 0; i < 10 * big; i++) {
			const x = Rg.range(bx0 + 6, bx1), sg = Rg.sign();
			chips.add(x, sg * Rg.range(0, bw), Rg.range(-30, 30), sg * Rg.range(60, 150), Rg.range(0.18, 0.35), 3, 7, 0, 0);
		}
		for (let i = 0; i < 6 * big; i++) {
			const x = Rg.range(bx0 + 4, bx1), sg = Rg.sign();
			sparks.add(x, sg * bw * 0.5, Rg.range(-40, 40), sg * Rg.range(80, 200), Rg.range(0.1, 0.22), 5, 8);
		}
		if (k === 0 || k === hits - 1) {
			for (let i = 0; i < 6; i++) {
				const x = bx0 + ((i + Rg.range(0.2, 0.8)) / 6) * (bx1 - bx0), sg = i % 2 ? 1 : -1;
				puffs.push({ x, y: sg * Rg.range(bw * 1.2, h * 0.7), vy: sg * Rg.range(14, 26), r: Rg.range(3, 5.5) * big, t0: t, life: Rg.range(0.3, 0.45) });
			}
		}
	}

	// 검 실루엣 — 손잡이는 시전자 쪽, 칼끝은 범위 끝. sc: 떨어지며 커졌다 줄어드는 배율, a: 불투명도, hot: 흰 정도
	function blade(p, sc, a, hot, age) {
		if (a <= 0.02) return;
		const cx = (bx0 + bx1) / 2;
		p.shade(bx0 - 12, -h * 0.8, bx1 + 12, h * 0.8, (x, y, out) => {
			const lx = cx + (x - cx) / sc, ly = y / sc;
			if (lx < bx0 || lx > bx1) return;
			const v = (lx - bx0) / (bx1 - bx0);
			// 칼몸: 끝 25% 에서 뾰족해진다. 코등이: 손잡이 쪽 막대
			const guard = lx < bx0 + 7 && lx > bx0 + 3 && Math.abs(ly) < bw * 1.9;
			const grip = lx <= bx0 + 3 && Math.abs(ly) < 2;
			const w = bw * (v > 0.75 ? 1 - (v - 0.75) / 0.25 : 1);
			const body = lx >= bx0 + 7 && Math.abs(ly) < w;
			if (!guard && !grip && !body) return;
			// 면을 칠하지 않는다 — 테두리·능선만 단단하게, 칼몸은 옅은 빛으로 비친다
			let ci, al;
			if (body) {
				const q = Math.abs(ly) / Math.max(0.5, w);
				if (q > 0.78) (ci = ly < 0 ? 0 : 1), (al = 1);
				else if (Math.abs(ly) < 0.9) (ci = 0), (al = 1);
				else (ci = ly < 0 ? 3 : 4), (al = 0.32 + hot * 0.5);
			} else {
				(ci = guard ? 1 : 3), (al = 1);
			}
			ci = Math.round(ci + age * 3 - hot * 3);
			p.blend(x, y, C[n - 1 - clamp(ci, 0, n - 1)], a * al);
			p.light(x, y, C[n - 3], 0.06 * a);
			return;
		});
	}

	return {
		update(dt) {
			t += dt;
			while (fired < hits && t >= hitT(fired)) {
				pulse(fired);
				fired++;
			}
			chips.update(dt);
			sparks.update(dt);
		},
		draw(p) {
			// 균열 — 첫 충격에 칼끝 쪽으로 번져 나가고, 충격마다 다시 달아올랐다가 식는다
			if (t >= FALL) {
				const reveal = ease.outQuart(clamp((t - FALL) / 0.12));
				let heat = 0;
				for (let k = 0; k < hits; k++) {
					const d = t - hitT(k);
					if (d >= 0) heat = Math.max(heat, Math.exp(-d * 9));
				}
				const fade = 1 - smooth(lastHit + 0.15, o.duration - 0.12, t);
				for (const c of fade > 0.02 ? cracks : []) {
					if (c.at > reveal) continue;
					const col = C[clamp(Math.round(2 + heat * 5), 0, n - 1)];
					for (let i = 1; i < c.pts.length; i++) {
						const [x0, y0] = c.pts[i - 1], [x1, y1] = c.pts[i];
						if ((x0 - bx0) / (bx1 - bx0) > reveal) break;
						p.line(x0, y0, x1, y1, i === 1 && c.at === 0 ? C[n - 3] : col, fade);
					}
				}
				if (heat > 0.05) p.glowLine(bx0 + 8, 0, bx1 - 4, 0, 6, C[n - 3], 0.3 * heat * fade);
			}

			// 충격 전선 — 축에서 범위 가장자리까지 두 줄이 벌어진다
			for (let k = 0; k < hits; k++) {
				const d = t - hitT(k);
				if (d < 0 || d > 0.16) continue;
				const u = d / 0.16, y = bw * 0.5 + (h - bw * 0.5) * ease.outQuart(u), col = u < 0.3 ? C[n - 1] : u < 0.6 ? C[n - 3] : C[n - 5];
				const x0 = bx0 + 6, x1 = bx1 - (1 - u) * 6;
				for (const sg of [-1, 1]) {
					p.line(x0, sg * y, x1, sg * y, col, 1 - u * 0.5);
					p.glowLine(x0, sg * y, x1, sg * y, 4, C[n - 3], 0.2 * (1 - u));
				}
			}

			// 흙먼지(반투명)
			for (const f of puffs) {
				const u = (t - f.t0) / f.life;
				if (u < 0 || u >= 1) continue;
				const r = f.r * (0.6 + ease.outQuad(u) * 0.8);
				p.disc(f.x, f.y + f.vy * u * f.life, r, stoneRamp[2], 0.28 * (1 - u) * (1 - u));
				p.disc(f.x - 1, f.y + f.vy * u * f.life - 1, r * 0.55, stoneRamp[3], 0.18 * (1 - u) * (1 - u));
			}

			// 검 — 위에서 크게 떨어지며 내려앉고, 충격마다 번쩍인 뒤 점점 투명해진다
			const fall = clamp(t / FALL);
			let hot = 0;
			for (let k = 0; k < hits; k++) {
				const d = t - hitT(k);
				if (d >= 0) hot = Math.max(hot, Math.exp(-d * 25) * (k === 0 ? 1 : 0.6));
			}
			const sc = 1 + 0.35 * (1 - ease.inQuad(fall));
			const ba = t < FALL ? 0.45 + 0.55 * fall : 1 - smooth(lastHit, lastHit + 0.22, t);
			blade(p, sc, ba, hot, clamp((t - FALL) / (lastHit + 0.2)));
			if (ba > 0.05) p.glowLine(bx0 + 8, 0, bx1, 0, bw * 2 + 4, C[n - 4], 0.25 * ba + 0.3 * hot);

			// 착지 섬광 — 충격마다 칼날 가운데에서
			for (let k = 0; k < hits; k++) {
				const d = t - hitT(k);
				if (d < 0 || d > 0.1) continue;
				const f = d / 0.1, mx = bx0 + (bx1 - bx0) * (0.35 + 0.2 * k);
				flash(p, mx, 0, (k === 0 ? 12 : 8) * (1 - ease.inQuad(f)), d < 0.03 ? WHITE : C[n - 2], C[n - 3], 0.5 * (1 - f));
			}

			chips.draw(p);
			sparks.draw(p);
		},
	};
}

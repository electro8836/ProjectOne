// ProjectOne 추가 이펙트 — 저격 탄도선 (탑다운, 원점 = 시전자 몸 중앙, 오른쪽 = 전방)
// 길이 length, 폭 width 의 직선 범위를 한순간에 꿰뚫는다: 총구 섬광, 흰 탄도선, 충격 고리, 공기를 비트는 나선, 남는 연기.
import { Rng, fit, clamp, smooth, ease, WHITE } from "../../core/index.js";
import { Streaks, flash } from "../melee/_p1.js";

export const meta = {
	name: "Snipe line",
	desc: "A sniper round cuts a straight lane in an instant: muzzle flash, a white-hot tracer that cools and retracts, shock rings and a twisting air wake along it, smoke left hanging.",
	tags: ["projectile", "bullet", "sniper", "beam", "line", "directional", "skill", "one-shot"],
	size: [352, 96],
	origin: [14, 48],
	duration: 0.5,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#1e1206", "#4a2c08", "#86520c", "#c48412", "#f0b424", "#ffe066", "#fff6c4", "#ffffff"],
		smoke: "#8a8296",
		length: 320,
		width: 48,
		rings: 5,
		seed: 13,
	},
};

const SHOT = 0.035; // 탄도선이 끝까지 뻗는 시간

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed);
	const L = o.length, h = o.width / 2, mx = 10;
	const core = Math.max(1, Math.round(h * 0.12)), amp = Math.max(2, h * 0.45);
	const sparks = new Streaks(C.slice(2));
	const headAt = (t) => mx + (L - mx) * ease.outQuad(clamp(t / SHOT));
	const passAt = (x) => SHOT * (1 - Math.sqrt(1 - clamp((x - mx) / (L - mx)))); // 탄이 x 를 지난 시각

	const rings = [];
	for (let i = 0; i < o.rings; i++) {
		const x = mx + 18 + ((L - mx - 30) * (i + Rg.range(0.1, 0.6))) / o.rings;
		rings.push({ x, t0: passAt(x), s: 1 - i * 0.08 });
	}
	const smoke = [];
	for (let x = mx + 6; x < L - 6; x += Rg.range(7, 12)) {
		smoke.push({ x, y: Rg.range(-2, 2), r: Rg.range(2, 3.5), vy: Rg.range(-6, 6), t0: passAt(x), life: Rg.range(0.3, 0.5) });
	}
	let t = 0, hit = false;

	return {
		update(dt) {
			t += dt;
			if (!hit && t >= SHOT) {
				hit = true;
				// 범위 끝에 탄이 박히며 튀는 불꽃
				for (let i = 0; i < 12; i++) {
					const a = Math.PI + Rg.range(-1.1, 1.1), sp = Rg.range(60, 170);
					sparks.add(L - 2, Rg.range(-2, 2), Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.12, 0.28), 4, 8);
				}
				// 총구 옆으로 빠지는 화약 불티
				for (let i = 0; i < 6; i++) {
					const sg = Rg.sign(), sp = Rg.range(40, 100);
					sparks.add(mx + 2, 0, Rg.range(-20, 30), sg * sp, Rg.range(0.1, 0.2), 3, 9);
				}
			}
			sparks.update(dt);
		},
		draw(p) {
			// 연기 — 탄이 지나간 자리에 반투명하게 걸렸다가 퍼지며 사라진다
			for (const s of smoke) {
				const u = (t - s.t0 - 0.02) / s.life;
				if (u < 0 || u >= 1) continue;
				const r = s.r * (0.6 + u * 1.2);
				p.disc(s.x, s.y + s.vy * u, r, o.smoke, 0.4 * (1 - u) * (1 - u));
			}

			// 나선 — 탄 주위 공기가 비틀린 자국 (두 가닥, 뒤에서부터 흩어진다)
			const head = headAt(t), helixA = 1 - smooth(SHOT, SHOT + 0.25, t);
			if (helixA > 0.02) {
				const tail = mx + (L - mx) * smooth(SHOT * 0.5, SHOT + 0.25, t);
				for (const ph of [0, Math.PI]) {
					for (let x = Math.max(mx + 4, tail); x < head; x += 0.5) {
						const v = (x - mx) / (L - mx), w = amp * Math.min(1, (x - mx) / 20) * (0.9 + 0.1 * v);
						const y = Math.sin(x * 0.16 + ph - t * 30) * w, front = Math.cos(x * 0.16 + ph - t * 30) > 0;
						p.dot(x, y, front ? C[n - 3] : C[n - 5], helixA * (front ? 1 : 0.6));
					}
				}
			}

			// 충격 고리 — 탄이 지날 때 세로 타원이 벌어진다
			for (const r of rings) {
				const d = t - r.t0;
				if (d < 0 || d > 0.2) continue;
				const u = d / 0.2, ry = h * r.s * (0.45 + 0.6 * ease.outQuart(u)), rx = 1.5 + 3 * u;
				p.shade(r.x - rx - 1, -ry - 1, r.x + rx + 1, ry + 1, (x, y, out) => {
					const e = Math.hypot((x - r.x) / rx, y / ry);
					if (Math.abs(e - 1) > 0.18 / Math.max(0.5, rx * 0.25)) return;
					out.c = u < 0.3 ? C[n - 1] : u < 0.6 ? C[n - 3] : C[n - 5];
					out.a = 1 - u;
				});
				p.glow(r.x, 0, ry, C[n - 4], 0.15 * (1 - u));
			}

			// 탄도선 — 흰 심이 번쩍 뻗고, 두께가 줄며 식은 뒤 총구 쪽부터 걷힌다
			const cool = smooth(SHOT, SHOT + 0.18, t), tail = mx + (L - mx) * smooth(SHOT + 0.04, SHOT + 0.2, t);
			if (tail < head - 1) {
				const outer = (core + 1.5) * (1 - cool * 0.7), ci = Math.round(cool * 4);
				for (let x = tail; x < head; x += 0.5) {
					const fade = Math.min(1, (x - tail) / 30);
					for (let y = -outer; y <= outer; y += 0.5) {
						const q = Math.abs(y) / Math.max(0.5, outer), c = q < 0.35 ? C[n - 1 - clamp(ci, 0, n - 1)] : C[n - 3 - clamp(ci, 0, n - 3)];
						p.dot(x, y, c, fade * (q < 0.35 ? 1 : 0.8));
					}
				}
				p.glowLine(tail, 0, head, 0, h * 0.6 + 4, C[n - 3], 0.35 * (1 - cool));
				p.glowLine(tail, 0, head, 0, 4, C[n - 2], 0.3 * (1 - cool));
			}

			// 총구 섬광 — 앞으로 길게, 옆으로 짧게
			if (t < 0.09) {
				const f = t / 0.09, k = 1 - ease.inQuad(f);
				flash(p, mx, 0, (h * 0.35 + 5) * k, t < 0.03 ? WHITE : C[n - 2], C[n - 3], 0.6 * k);
				p.tri(mx, -core - 2, mx, core + 2, mx + (h * 0.8 + 10) * k, 0, t < 0.03 ? WHITE : C[n - 2]);
			}
			// 착탄 섬광
			const d = t - SHOT;
			if (d > 0 && d < 0.1) flash(p, L - 2, 0, 7 * (1 - d / 0.1), d < 0.03 ? WHITE : C[n - 2], C[n - 3], 0.5 * (1 - d / 0.1));

			sparks.draw(p);
		},
	};
}

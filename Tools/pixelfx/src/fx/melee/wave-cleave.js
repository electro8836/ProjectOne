// ProjectOne 추가 이펙트 — 직선 범위 참격파 (탑다운, 원점 = 시전자 몸 중앙, 오른쪽 = 전방)
// 길이 length, 폭 width 의 직선 범위를 정확히 덮는다: 초승달 칼날 파동이 앞으로 날아가고, 뒤에 베인 자국·바람줄이 남는다.
import { Rng, fit, clamp, smooth, ease, WHITE } from "../../core/index.js";
import { Streaks, flash } from "./_p1.js";

export const meta = {
	name: "Wave cleave",
	desc: "A crescent blade of force races forward down a straight lane, an echo and a translucent wake behind it, a glowing scar and wind lines left in its path; it breaks in shavings at the end.",
	tags: ["melee", "slash", "wave", "line", "directional", "skill", "one-shot"],
	size: [196, 120],
	origin: [16, 60],
	duration: 0.6,
	loop: false,
	fps: 30,
	bg: "dark",
	options: {
		palette: ["#1f0b10", "#8c1d1c", "#cc3a1a", "#f2701f", "#ffae35", "#ffe27a", "#fff8d6", "#ffffff"],
		length: 160, // 범위 길이(px, 1유닛 = 32px)
		width: 80, // 범위 폭(px)
		thick: 14, // 칼날 두께(px)
		travel: 0.2, // 끝까지 날아가는 시간
		seed: 4,
	},
};

const BREAK = 0.16; // 끝에서 부서지는 시간

export function create(o) {
	const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed);
	const L = o.length, h = o.width / 2, B = Math.min(h * 0.55, L * 0.25), T = o.thick, TR = o.travel;
	const x0 = 4, x1 = L - B; // 칼날 중심이 지나는 구간 — 앞 테두리가 정확히 L 에서 멈춘다
	const sparks = new Streaks(C.slice(1));
	const dust = new Streaks(C.slice(0, 5));

	// 뒤따르는 바람줄 — 폭 안에서 높이·지연·길이를 제각각
	const winds = [];
	for (let i = 0; i < 9; i++) {
		winds.push({ y: Rg.range(-h * 0.8, h * 0.8), lag: Rg.range(6, 20), len: Rg.range(12, 34), life: Rg.range(0.16, 0.3) });
	}

	const posAt = (t) => x0 + (x1 - x0) * ease.outQuad(clamp(t / TR));
	let t = 0, emit = 0, broke = false;

	// 초승달 칼날 하나 — cx: 중심, th: 두께, age: 0..1 식음, a: 불투명도
	function blade(p, cx, th, age, a, hot) {
		if (th < 0.6 || a <= 0) return;
		p.shade(cx - th - 2, -h - 1, cx + B + 2, h + 1, (x, y, out) => {
			const s = 1 - (y / h) * (y / h);
			if (s <= 0) return;
			const r = Math.sqrt(s), front = cx + B * r, w = th * Math.pow(r, 0.7);
			if (w < 0.5) return;
			const q = (front - x) / w;
			if (q < 0 || q > 1) return;
			const ci = q < 0.2 && hot ? 0 : Math.round(q * (n - 1) * 0.7 + age * 3 + (1 - r) * 1.5);
			out.c = C[n - 1 - clamp(ci, 0, n - 1)];
			out.a = a * Math.min(1, r * 3);
		});
	}

	return {
		update(dt) {
			t += dt;
			// 칼날 양 끝에서 대팻밥이 뒤로 흩날린다
			if (t < TR) {
				emit += dt;
				while (emit > 0.018) {
					emit -= 0.018;
					const cx = posAt(t);
					for (const sg of [-1, 1]) {
						sparks.add(cx + Rg.range(-2, 2), sg * h * Rg.range(0.8, 1), Rg.range(-60, 10), sg * Rg.range(20, 70), Rg.range(0.15, 0.3), 4, 7);
					}
				}
			}
			if (!broke && t >= TR) {
				broke = true;
				// 끝에서 부서지며 앞으로 퍼지는 파편
				for (let i = 0; i < 16; i++) {
					const y = Rg.range(-h, h) * 0.9, a = Rg.range(-0.8, 0.8) + (y / h) * 0.6;
					const sp = Rg.range(60, 180);
					sparks.add(L - 4, y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.12, 0.3), 5, 8);
				}
				for (let i = 0; i < 8; i++) {
					dust.add(L - Rg.range(0, 10), Rg.range(-h, h) * 0.8, Rg.range(10, 40), Rg.range(-15, 15), Rg.range(0.2, 0.35), 2, 4, 0, 0);
				}
			}
			sparks.update(dt);
			dust.update(dt);
		},
		draw(p) {
			const cx = posAt(t);
			const brk = smooth(TR, TR + BREAK, t); // 0 날아가는 중 → 1 다 부서짐

			// 지나간 자리의 베인 자국 — 흰 줄이 식어 어두운 점선이 된다
			const scarA = 1 - smooth(TR, TR + 0.3, t);
			if (scarA > 0) {
				const end = cx - T * 0.5;
				for (let x = 6; x < end; x += 0.5) {
					const v = (x - 6) / Math.max(1, end - 6), age = clamp(t / (TR + 0.25) + (1 - v) * 0.4);
					p.dot(x, 0, C[n - 1 - clamp(Math.round(age * 5), 0, n - 1)], scarA * (0.4 + 0.6 * v));
				}
				p.glowLine(6, 0, Math.max(6, end), 0, 4, C[n - 3], 0.25 * scarA);
			}

			// 바람줄 — 칼날 뒤를 바짝 따라오다 흩어진다
			for (const w of winds) {
				const u = t / w.life;
				if (u >= 1) continue;
				const hx = posAt(t) - w.lag, len = w.len * (1 - u * 0.5);
				if (hx - len < 4) continue;
				p.line(hx - len, w.y, hx, w.y, (v) => (v > 0.7 ? C[n - 2] : C[n - 4]), 1 - u);
			}

			// 칼날 뒤 반투명 잔상 — 칼날 모양을 따라 뒤로 옅어진다
			const wakeA = 0.4 * (1 - brk);
			if (wakeA > 0.02) {
				const wl = Math.min(40, cx - 2);
				p.shade(cx - wl, -h, cx + B, h, (x, y, out) => {
					const s = 1 - (y / h) * (y / h);
					if (s <= 0) return;
					const back = cx + B * Math.sqrt(s) - T * Math.pow(Math.sqrt(s), 0.7);
					const d = back - x;
					if (d < 0 || d > wl) return;
					p.blend(x, y, C[clamp(Math.round(4 - (d / wl) * 3), 0, n - 1)], wakeA * (1 - d / wl) * Math.sqrt(s));
				});
			}

			// 메아리 칼날(뒤, 얇고 어둡게) → 본 칼날
			const age = clamp(t / (TR + BREAK));
			const echoX = posAt(Math.max(0, t - 0.05)) - 10;
			blade(p, echoX, T * 0.5 * (1 - brk), Math.min(1, age + 0.5), 0.8 * (1 - brk), false);
			blade(p, cx + brk * 8, T * (1 - brk * 0.8), age, 1 - brk * brk, t < TR + 0.04);

			// 칼날 머리의 빛과 양 끝의 반짝임
			if (brk < 1) {
				const g = 3 * (1 - brk) * (0.7 + 0.3 * Math.sin(t * 60));
				flash(p, cx + 1, -h + 1, g, C[n - 1], C[n - 3], 0.2 * (1 - brk));
				flash(p, cx + 1, h - 1, g, C[n - 1], C[n - 3], 0.2 * (1 - brk));
				p.glow(cx + B * 0.6, 0, h * 0.9, C[n - 3], 0.42 * (1 - brk));
				p.glow(cx + B * 0.8, 0, h * 0.4, C[n - 2], 0.3 * (1 - brk));
			}

			// 시전 순간 손 앞 섬광
			if (t < 0.1) {
				const f = t / 0.1;
				flash(p, 8, 0, (h * 0.35 + 4) * (1 - ease.inQuad(f)), t < 0.035 ? WHITE : C[n - 2], C[n - 3], 0.5 * (1 - f));
			}

			dust.draw(p);
			sparks.draw(p);
		},
	};
}

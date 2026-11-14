// ProjectOne 추가 이펙트 — 작은 무방향 타격 (원점 = 프레임 중앙, 투사체·총탄 피격용)
import { Rng, clamp, ease, fit, sparkle, TAU, WHITE } from "../../core/index.js";

export const meta = {
  name: "Spark pop",
  desc: "A small hit with no direction: a white four-point flare pops, a thin ring snaps out round it and short sparks fly off every way, gone in a third of a second.",
  tags: ["impact", "hit", "spark", "small", "combat", "one-shot"],
  size: [56, 56],
  origin: [28, 28],
  duration: 0.36,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1c2230", "#4a5468", "#9aa6ba", "#d0d8e6", "#ffffff"],
    sparks: 7,
    seed: 5,
  },
};

const FLARE = 0.12; // 별 섬광이 사그라드는 시간
const RING = 0.13; // 고리가 퍼지는 시간

export function create(o) {
  const C = fit(o.palette, 5), Rg = new Rng(o.seed);
  // 불꽃마다 방향·속도·수명을 따로 줘서 기계적으로 보이지 않게 한다
  const sparks = [];
  for (let i = 0; i < o.sparks; i++) {
    const a = (i / o.sparks) * TAU + Rg.range(-0.35, 0.35);
    sparks.push({ a, v: Rg.range(70, 120), l: Rg.range(0.16, 0.3), len: Rg.range(3, 6) });
  }

  let t = 0;
  return {
    update(dt) {
      t += dt;
    },
    draw(p) {
      // 첫 프레임은 흰 섬광, 이후 팔 길이가 빠르게 줄어든다
      const f = clamp(t / FLARE);
      if (f < 1) {
        const arm = 7 * (1 - ease.inQuad(f)) + (t < 0.034 ? 2 : 0);
        sparkle(p, 0, 0, arm, t < 0.034 ? WHITE : C[3], C[2], 0.5 * (1 - f));
      }

      const r = clamp(t / RING);
      if (r < 1) {
        const R = 3 + 9 * ease.outQuart(r);
        p.ring(0, 0, R, r < 0.4 ? 2 : 1, r < 0.3 ? C[4] : r < 0.7 ? C[3] : C[2]);
      }

      for (const s of sparks) {
        const u = t / s.l;
        if (u >= 1) continue;
        const d = s.v * s.l * ease.outQuart(u);
        const dx = Math.cos(s.a), dy = Math.sin(s.a);
        const x1 = dx * (3 + d), y1 = dy * (3 + d);
        const x0 = x1 - dx * s.len * (1 - u), y0 = y1 - dy * s.len * (1 - u);
        p.line(x0, y0, x1, y1, u < 0.4 ? C[4] : u < 0.75 ? C[3] : C[2]);
      }
    },
  };
}

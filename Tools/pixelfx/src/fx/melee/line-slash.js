// ProjectOne 추가 이펙트 — 피격 지점을 가로지르는 얇고 긴 직선 베기 한 줄 (원점 = 선의 중앙)
import { clamp, ease, fit, WHITE } from "../../core/index.js";

export const meta = {
  name: "Line slash",
  desc: "One thin, long straight cut flicks across the target: the line shoots out end to end, flashes white, then thins and draws away from the tail.",
  tags: ["melee", "slash", "hit", "line", "one-shot"],
  size: [104, 56],
  origin: [52, 28],
  duration: 0.4,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1b2a4a", "#3d6fb0", "#8cc8ff", "#e6f6ff", "#ffffff"],
    length: 92,
    tilt: -0.42,
  },
};

const GROW = 0.05; // 선이 끝까지 뻗는 시간
const HOLD = 0.1; // 흰 섬광이 끝나는 시점

export function create(o) {
  const C = fit(o.palette, 5), half = o.length / 2;
  const dx = Math.cos(o.tilt), dy = Math.sin(o.tilt);
  const at = (u) => [dx * half * (2 * u - 1), dy * half * (2 * u - 1)];
  let t = 0;
  return {
    update(dt) {
      t += dt;
    },
    draw(p) {
      // 머리는 빠르게 뻗고, 섬광 뒤에는 꼬리가 머리 쪽으로 따라붙으며 사라진다
      const head = ease.outExpo(clamp(t / GROW));
      const tail = ease.inQuad(clamp((t - HOLD) / (meta.duration - HOLD)));
      if (head - tail <= 0.01) return;
      const [x0, y0] = at(tail), [x1, y1] = at(head);
      const flash = t < HOLD;
      const fade = 1 - tail;
      p.glowLine(x0, y0, x1, y1, 6, C[2], (flash ? 0.35 : 0.2) * fade);
      // 가운데만 3px 몸통, 양 끝은 1px 심 — 칼날처럼 가늘어지는 모양
      if (flash || tail < 0.35) {
        const [bx0, by0] = at(tail + (head - tail) * 0.25), [bx1, by1] = at(head - (head - tail) * 0.25);
        p.beam(bx0, by0, bx1, by1, 3, flash ? C[3] : C[2], 0.9 * fade);
      }
      p.line(x0, y0, x1, y1, flash ? WHITE : C[3], (u) => clamp(fade * (0.4 + 0.6 * Math.sin(Math.PI * u) + 0.3)));
    },
  };
}

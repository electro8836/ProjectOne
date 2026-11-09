# pixelfx 고정 사본

- 출처: https://github.com/patch-gg/pixelfx
- 버전: 0.3.0
- 커밋: `a84159688af3ca37a4be05a0d422b94451b2e24e` (2026-11-09 기준 main 최신, 업스트림에 태그 없음)
- 라이선스: MIT — `LICENSE`, `NOTICE.md` 원문 유지. 저장소 루트 `THIRD_PARTY_NOTICES.md` 에도 고지

## 원본에서 뺀 것

- `gallery/` 전체 — Patch Pixel 폰트(`gallery/fonts/PatchPixel-Regular.woff2`)는 MIT 대상이 아니라 쓰지 않는다
- `tools/build-gallery.mjs`, `tools/serve.mjs` — 위 갤러리 전용
- `docs/readme/` (README 용 GIF), `.github/`, `.claude-plugin/`, `package-lock.json`

`package.json` 의 `gallery` 스크립트와 `esbuild` devDependency 도 갤러리 전용이라 남아 있어도 쓰지 않는다.
나머지 파일은 원본 그대로이며 수정하지 않는다.

## 갱신 방법

1. 업스트림 새 커밋을 정해 위 "원본에서 뺀 것"을 제외하고 `git archive` 로 덮어쓴다
2. 이 파일의 버전·커밋을 고친다
3. `node tools/check.mjs fire/torch` 로 동작 확인 후, 이미 가져온 이펙트는 `Tools/pixelfx-unity.mjs` 로 다시 뽑는다

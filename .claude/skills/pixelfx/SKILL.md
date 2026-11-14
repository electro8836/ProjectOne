---
name: pixelfx
description: 픽셀 아트 이펙트·스프라이트 애니메이션을 pixelfx 라이브러리(코드로 그리는 244종 — 불, 폭발, 물, 얼음, 번개, 근접, 타격, 투사체, 비전, 신성, 암흑, 상태이상, 자연, 날씨, UI, 획득, 전환, 생물)로 찾고·색을 바꾸고·새로 만들어 이 게임(Unity)에 VFX 프리펩으로 넣는다. "픽셀 이펙트", "타격 이펙트 만들어", "pixelfx", "스프라이트 시트 이펙트", "이 스킬에 이펙트 붙여", "이펙트가 밋밋해" 같은 요청이나, 게임 VFX를 새로 만들거나 개선할 때 사용한다.
license: MIT (pixelfx 원본 스킬 기반 — Copyright (c) 2026 Patch, Hugo Duprez)
compatibility: Node 18 이상. ffmpeg 는 선택(GIF·MP4 미리보기).
---

# pixelfx (ProjectOne)

pixelfx 는 이펙트를 이미지가 아니라 코드로 그린다. 렌더해서 눈으로 보고 고치는 반복이 핵심이다.
**방법론·작성 규약·품질 기준은 [UPSTREAM.md](UPSTREAM.md)(원본 스킬 전문)를 반드시 먼저 읽는다.**
아래는 이 저장소에서 원본 지침을 덮어쓰는 규칙이다.

## 이 저장소의 규칙

1. **클론하지 않는다.** 고정 버전 사본이 `Tools/pixelfx/` 에 있다(0.3.0, 커밋 `a841596`, `Tools/pixelfx/VENDOR.md`).
   원본 지침의 `tools/...`, `src/...`, `docs/...` 경로는 전부 `Tools/pixelfx/` 기준이고, 명령은 그 폴더에서 실행한다.
   설치 단계 없음 — Node 만 있으면 된다. 이 사본의 원본 파일은 수정하지 않는다(새 이펙트 파일 추가는 허용).
2. **Patch Pixel 폰트를 쓰지 않는다.** MIT 대상이 아니라 사본에서 뺐다(`gallery/` 없음 → `tools/serve.mjs` 갤러리 없음).
   이펙트 안 글자는 `src/core/font.js` 의 코드 폰트(MIT)만 쓴다.
3. **게임으로 가져오기는 전용 파이프라인으로만 한다.** 원본의 `--sheet` 결과를 손으로 옮기지 않는다.
4. 새 이펙트를 만들면 `Tools/pixelfx/src/fx/<category>/<name>.js` 에 둔다. 특정 게임의 스프라이트·캐릭터·로고를 재현하지 않는다.
5. 플러그인(`pixelfx@pixelfx`)이 설치돼 있어도 이 저장소에서는 이 스킬과 `Tools/pixelfx` 사본을 쓴다(버전 고정 때문).

## 게임으로 가져오기 (렌더 → PNG+JSON → Unity 프리펩)

```bash
node Tools/pixelfx-unity.mjs impacts/hit-spark
node Tools/pixelfx-unity.mjs fire/torch --name torch_blue --opts '{"palette":["#102","#215","#43a","#8cf","#fff"]}'
```

옵션: `--name`(에셋 이름, 기본은 id 의 뒷부분 — 색 바꾼 변형은 반드시 다른 이름), `--fps`(기본 이펙트 값 30 — 프레임이
많으면 20~24 로 낮춘다), `--seed`, `--opts JSON`, `--pivot x,y`(프레임 좌상단 기준 픽셀 — 기본은 이펙트 원점),
`--random-rotation`(재생마다 0~360도 랜덤 회전 — 파티클이 프레임 중심을 축으로 돌기 때문에 기준점이 프레임 중심인 이펙트만 허용),
`--margin N`(캔버스를 사방으로 N px 늘림 — `radius`·`reach` 같은 크기 옵션을 키워 그림이 원래 캔버스를 넘을 때. `--pivot` 은 늘어난 프레임 기준).

프리펩 파티클은 로컬 정렬이라 `SkillVFXFacing` 회전을 그대로 따른다. 방향성 SkillVFX 는 기준점을 시전자 손(이펙트 원점보다 약 20px 위)에 둔다.
이 저장소에서 추가한 `impacts/spark-pop`(작은 무방향 타격, 원점 = 중앙)은 투사체·총탄 피격용이다(`--random-rotation` 가능).

**스킬 시전 FX 는 범위에 맞춰 뽑는다.** 아래 이펙트는 탑다운·중력 없음·원점 = 시전자 `HitCenter`(=범위 판정 기준점)라
`--pivot` 없이 그대로 쓰고, 크기 옵션에 테이블 범위 × 32px 를 넣으면 판정 범위와 정확히 겹친다(공용 헬퍼 `melee/_p1.js`).

| 이펙트 | 범위 | 크기 옵션 | 비고 |
|---|---|---|---|
| `melee/wave-cleave` | Line | `length`, `width`, `thick` | 초승달 참격파가 전방으로 날아감 |
| `melee/blade-drop` | Line | `length`, `width`, `hits`, `gap` | 거대 검 낙하 + 충격 hits 회 (피해 횟수·간격과 맞춤) |
| `melee/whirl-ring` | Circle | `radius`, `spins`, `gap`, `parry` | 원형 회전베기 |
| `melee/blade-bloom` | Circle | `radius`, `cuts`, `gap` | 연속 직선 베기 후 피꽃 |
| `melee/fan-cut` | Sector | `radius`, `spread`, `style`(gale/cross/burst) | 부채꼴 베기 |
| `projectiles/snipe-line` | Line | `length`, `width`, `rings` | 저격 탄도선 |
| `status/power-surge` | Self | `radius`, `style`(rage/storm/focus/shadow) | 버프 발동 |

방향성 범위(Line·Sector)는 테이블 `SkillVFXFacing` TRUE, `SkillVFXOffset` 0 으로 둔다.

**피격 이펙트는 `--pivot` 을 확인한다.** 게임은 피격 VFX 를 맞은 유닛의 `HitCenter`(몸 중앙)에 재생한다. 그런데 라이브러리의
타격 이펙트는 원점이 바닥이거나(예: `impacts/blunt-hit` 원점 (64,72), 그림 중심은 `height` 22px 위인 (64,50)) 한쪽으로
튀는 방향성 이펙트다(`directional` 태그, 예: `hit-spark`). 피격용은 방향성 없는 것을 고르고, 그림 중심을 `--pivot` 으로 준다.
이 저장소에서 추가한 `melee/line-slash`(얇은 직선 베기 한 줄, 단검 평타 피격용)는 원점이 선 중앙이라 `--pivot` 이 필요 없고, `--random-rotation` 으로 뽑아 맞을 때마다 방향이 바뀐다.
새 이펙트의 고유 옵션 이름으로 `x`·`y`·`flip`·`angle`·`light`·`actor` 는 쓰지 않는다 — 렌더러가 배치·회전용으로 가로챈다(`angle` 을 썼다가 이펙트 전체가 회전된 적 있음).

결과:

- `ProjectOne/Assets/Project/Art/VFX/PixelFx/<name>/<name>.png` + `<name>.json` (Aseprite 형식, `meta.pixelfx` 에 fps·loop·pivot·재생성용 `source`)
- `Tools/pixelfx/records/<id>/contact.png` — 눈으로 확인할 접촉 시트(git 무시)

그다음 Unity 에서 **Tools > VFX > PixelFx 시트 가져오기**(PNG 나 JSON 선택 후 실행, 또는 창에서 id 를 넣고 "렌더 + 가져오기")를 하면:

- 텍스처: Point 필터·무압축·밉맵 없음·PPU 32(유닛과 동일), JSON 프레임대로 스프라이트 슬라이스
- 머티리얼: `<name>.mat` (2D Sprite-Unlit-Default)
- 프리펩: `ProjectOne/Assets/Project/Prefabs/Effects/PixelFx/PixelFx_<name>.prefab` — 루트 `VFXItem` + 자식 `Sheet` 파티클(Texture Sheet Animation 그리드, 1장 버스트), Sorting Layer `Effect`
- 주소: `AddressableAutoMarker` 가 `Prefabs_Effects` 그룹에 `PixelFx_<name>` 으로 자동 등록

게임 코드에서는 다른 VFX 와 똑같이 재생한다:

```csharp
VFXManager.Instance.PlayOneShot("PixelFx_hit-spark", position);   // 루프 이펙트는 PlayLooping/Release
```

스킬 테이블의 `SkillVFX`/`EffectVFX` 칸에 `PixelFx_<name>` 을 넣어도 된다.

### 주의

- 시트는 이펙트 캔버스 전체 크기라 크고 긴 이펙트(예: `explosions/small-explosion` 300×240, 163프레임)는 텍스처가 커진다.
  `--fps` 를 낮추거나 더 작은 이펙트를 고른다. 가져오기 창이 8192 를 넘으면 거부한다.
- 캐릭터에 그려지는 이펙트(`act` 가 있거나 `o.actor` 를 쓰는 상태이상류)는 스톡 더미 위에 그려지므로 그대로 쓰지 않는다.
  `meta.pixelfx.act` 의 타이밍(`windup`, `hits[].freeze`)만 참고해 우리 히어로 애니메이션·히트스톱에 맞춘다.
- 방향성 이펙트는 오른쪽을 본다. 왼쪽은 `VFXManager` 회전/좌우 반전으로 처리하거나 `--opts '{"flip":true}'` 로 따로 뽑는다.
- 프레임 크기 1px = 1/32 유닛. 화면에서 너무 크거나 작으면 이펙트 `size`/`radius` 옵션으로 다시 뽑는다(스케일로 키우지 않는다 — 픽셀 크기가 어긋남).

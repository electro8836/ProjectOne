#!/usr/bin/env node
// pixelfx 이펙트를 렌더해 Unity 로 가져갈 스프라이트 시트(PNG + Aseprite 형식 JSON)를 만든다.
//
//   node Tools/pixelfx-unity.mjs impacts/hit-spark
//   node Tools/pixelfx-unity.mjs fire/torch --name torch_blue --fps 24 --opts '{"palette":["#102","#215","#43a","#8cf","#fff"]}'
//   node Tools/pixelfx-unity.mjs impacts/blunt-hit --pivot 64,50
//   node Tools/pixelfx-unity.mjs melee/line-slash --random-rotation
//
// --pivot x,y : 프레임 좌상단 기준 픽셀. 기본은 이펙트 원점(바닥 이펙트는 발밑)이다.
//               피격 이펙트를 유닛의 HitCenter 에 맞출 때처럼 그림 중심을 기준점으로 쓰고 싶으면 지정한다.
// --random-rotation : 재생할 때마다 0~360도 랜덤 회전. 파티클은 프레임 중심을 축으로 돌므로
//               기준점이 프레임 중심인 이펙트만 허용한다 (아니면 돌 때 위치가 어긋난다).
//
// 출력
//   ProjectOne/Assets/Project/Art/VFX/PixelFx/<name>/<name>.png, <name>.json   — Unity 가 가져갈 시트
//   Tools/pixelfx/records/<id>/contact.png                                      — 눈으로 확인할 접촉 시트 (git 무시)
//
// 시트를 만든 뒤 Unity 의 Tools > VFX > PixelFx 시트 가져오기 로 프리펩까지 만든다.
// 렌더 자체는 고정 사본(Tools/pixelfx)의 함수를 그대로 쓴다 — 여기서는 출력 위치와 재생성 정보만 정한다.

import { readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { resolveIds, loadEffect, renderFrames, contactSheet, spriteSheet, save } from "./pixelfx/tools/lib/fxlib.mjs";

const TOOLS = dirname(fileURLToPath(import.meta.url));
const PIXELFX = join(TOOLS, "pixelfx");
const ASSET_ROOT = join(TOOLS, "..", "ProjectOne", "Assets", "Project", "Art", "VFX", "PixelFx");

const SWITCHES = ["random-rotation"]; // 값 없이 쓰는 플래그
const args = process.argv.slice(2), flags = {}, ids = [];
for (let i = 0; i < args.length; i++) {
	const a = args[i];
	if (SWITCHES.includes(a.slice(2))) {
		flags[a.slice(2)] = true;
	} else if (!a.startsWith("--")) {
		ids.push(a);
	} else {
		flags[a.slice(2)] = args[++i];
	}
}

if (ids.length !== 1) {
	console.error("usage: node Tools/pixelfx-unity.mjs <category/name> [--name N] [--fps N] [--seed N] [--opts JSON] [--pivot X,Y] [--random-rotation]");
	process.exit(1);
}

// 한 번에 이펙트 하나만 — 패턴으로 여러 개가 걸리면 이름이 겹치므로 막는다
const matched = resolveIds(ids);
if (matched.length !== 1) {
	console.error(`이펙트 하나만 지정해야 합니다: ${ids[0]} → ${matched.length}개 (${matched.slice(0, 5).join(", ")})`);
	process.exit(1);
}

const id = matched[0];
const name = flags.name ?? id.split("/")[1];
if (!/^[a-z0-9][a-z0-9_-]*$/i.test(name)) {
	console.error(`--name 은 영문·숫자·-·_ 만 쓸 수 있습니다: ${name}`);
	process.exit(1);
}

const fps = flags.fps ? +flags.fps : undefined;
const seed = flags.seed ? +flags.seed : undefined;
const options = flags.opts ? JSON.parse(flags.opts) : undefined;
const version = JSON.parse(readFileSync(join(PIXELFX, "package.json"), "utf8")).version;

const fx = await loadEffect(id);
const r = renderFrames(fx, { fps, seed, options });
const sheet = spriteSheet(fx, r, name);

let pivot = null;
if (flags.pivot) {
	pivot = flags.pivot.split(",").map(Number);
	if (pivot.length !== 2 || pivot.some(Number.isNaN) || pivot[0] < 0 || pivot[1] < 0 || pivot[0] > r.w || pivot[1] > r.h) {
		console.error(`--pivot 은 프레임(${r.w}x${r.h}) 안의 x,y 여야 합니다: ${flags.pivot}`);
		process.exit(1);
	}

	sheet.json.meta.pixelfx.pivot = pivot;
}

const randomRotation = !!flags["random-rotation"];
if (randomRotation) {
	const [px, py] = sheet.json.meta.pixelfx.pivot;
	if (Math.abs(px - r.w / 2) > 0.5 || Math.abs(py - r.h / 2) > 0.5) {
		console.error(`--random-rotation 은 기준점이 프레임 중심(${r.w / 2},${r.h / 2})이어야 합니다: 현재 ${px},${py}`);
		process.exit(1);
	}
}

sheet.json.meta.pixelfx.randomRotation = randomRotation;

// 같은 시트를 다시 뽑을 수 있도록 입력값을 남긴다
sheet.json.meta.pixelfx.source = { id, version, fps: r.fps, seed: seed ?? null, opts: options ?? null, pivot, randomRotation };

const dir = join(ASSET_ROOT, name);
const png = save(join(dir, `${name}.png`), sheet.png);
const json = save(join(dir, `${name}.json`), JSON.stringify(sheet.json, null, 1));
const contact = save(join(PIXELFX, "records", id, "contact.png"), contactSheet(fx, r, {}));

const m = sheet.json.meta;
console.log(`${id} → ${name}  ${r.n}프레임 @${r.fps}fps  프레임 ${r.w}x${r.h}  시트 ${m.size.w}x${m.size.h}  ${fx.meta.loop ? "루프" : "1회"}`);
console.log(png);
console.log(json);
console.log(contact);
if (fx.mod.act) {
	console.log("주의: act 가 있는 이펙트입니다. 시트에는 이펙트만 들어가며 타이밍은 meta.pixelfx.act 에 있습니다.");
}

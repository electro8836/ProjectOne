# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 작업 원칙

### 1. 코딩 전 사고

- 가정은 명시적으로 밝힌다. 불확실하면 묻는다.
- 해석이 여러 가지라면 조용히 선택하지 말고 제시한다.
- 더 단순한 방법이 있으면 먼저 말한다. 필요하면 반론한다.
- 모호한 부분이 있으면 멈추고 무엇이 불명확한지 이름 붙여 질문한다.

### 2. 단순함 우선

- 요청된 것만 구현한다. 투기적 기능 추가 금지.
- 단일 사용 코드에 추상화 금지.
- 요청되지 않은 "유연성"이나 "설정 가능성" 추가 금지.
- 200줄이 50줄로 될 수 있다면 다시 작성한다.

### 3. 최소 변경

- 반드시 필요한 것만 수정한다. 인접한 코드·주석·포맷 "개선" 금지.
- 기존 스타일을 그대로 유지한다 (다르게 하고 싶어도).
- 관련 없는 데드코드를 발견하면 언급만 한다 — 삭제하지 않는다.
- 내 변경으로 생긴 미사용 import/변수/함수는 직접 제거한다.
- 변경된 모든 줄은 사용자 요청으로 직접 추적 가능해야 한다.

### 4. 목표 기반 실행

다단계 작업은 사전에 검증 기준을 포함한 계획을 제시한다:

```
1. [단계] → 검증: [확인 방법]
2. [단계] → 검증: [확인 방법]
```

## 프로젝트 개요

Unity 6.4 / URP 2D 기반 게임 프로젝트. 유니티 프로젝트 루트는 `ProjectOne/`, 실제 작업 코드는 전부 `ProjectOne/Assets/Project/` 아래에 있다.

- **네임스페이스 루트**: `ProjectOne`
- **언어**: C# / 주석은 한국어
- **들여쓰기**: 2칸 스페이스
- **비동기**: 시간 기반 단순 대기는 코루틴, 외부 I/O·Addressables 등 실제 비동기 작업은 UniTask

## 코딩 규칙

- 변수명/함수명: camelCase
- 비공개 필드: `_` 접두사 (예: `_ownerPool`, `_isReleased`)
- 인스펙터 노출: `public` 대신 `[SerializeField]` 사용
- 조건문: 단일 라인이어도 `{}` 항상 붙이기
- 람다식: 꼭 필요한 경우 외 사용 금지 — 콜백은 메서드 그룹으로 연결
- `foreach` 사용 금지 — 인덱스 `for` 사용

## 폴더 구조

```
ProjectOne/Assets/Project/Scripts/
  Core/
    Audio/      AudioManager, AudioChannel, AudioSourcePool, AudioSourceItem
    Event/      EventManager, EventChannel, Events.cs
    Managers/   ResourceManager (+ Editor/AddressableHelper)
  GamePlay/
    Combat/     Damage, HitDetector (IDamageable)
    Projectile/ Projectile, ProjectileData, ProjectilePool, ProjectileLauncher, Trajectory/
    Unit/       UnitBase, Hero, Attribute/, Movement/, Animation/
  Utils/
    Pooling/    PoolBase<T>, IPoolable
    Singleton/  Singleton<T>, MonoSingleton<T>
```

## 아키텍처

### 싱글톤 (`Utils/Singleton/`)

매니저 계열은 모두 CRTP 싱글톤 위에 얹혀 있다. 두 종류가 있으니 용도에 맞게 선택한다.

- `Singleton<T>` — 순수 C# 싱글톤. Holder 중첩 클래스 + `Activator.CreateInstance(nonPublic: true)`로 지연 초기화. 생성자는 `protected`로 막는다. 예: `EventManager`.
- `MonoSingleton<T>` — MonoBehaviour 싱글톤. `Awake`에서 중복 인스턴스 자동 파괴 + `DontDestroyOnLoad`. 씬에 없으면 런타임에 `GameObject`를 생성해 붙인다. 하위 클래스가 `Awake/OnDestroy`를 오버라이드할 땐 반드시 `base.Awake()`/`base.OnDestroy()` 호출. 예: `ResourceManager`, `AudioManager`.

### 이벤트 시스템 (`Core/Event/`)

타입 기반 pub/sub. `EventManager`(순수 C# 싱글톤)가 `Dictionary<Type, object>`로 `EventChannel<T>`를 관리한다.

```csharp
// 이벤트 정의 — Events.cs에 readonly struct로 추가
public readonly struct SomeEvent { public readonly int Value; ... }

// 구독 / 발행 / 해제
EventManager.Instance.Subscribe<SomeEvent>(OnSomeEvent);
EventManager.Instance.Publish(new SomeEvent(...));
EventManager.Instance.Unsubscribe<SomeEvent>(OnSomeEvent);
```

- 모든 이벤트는 `Events.cs`에 `readonly struct`로 정의
- 핸들러는 반드시 `OnDestroy`에서 `Unsubscribe` 호출 (메서드 그룹으로 등록 — 람다 금지)

### 오브젝트 풀 (`Core/Pooling/`)

`PoolBase<T>`는 Unity `ObjectPool<T>` 래퍼. 새 풀 클래스는 `PoolBase<T>`를 상속하고 `CreateItem()`만 구현한다.

```csharp
public class MyPool : PoolBase<MyItem>
{
  [SerializeField] private MyItem _prefab;
  protected override MyItem CreateItem() => Instantiate(_prefab, transform);

  public MyItem Spawn(...)
  {
    MyItem item = GetFromPool();
    item.Initialize(...);
    item.OnActivate();
    return item;
  }
}
```

풀링 대상 MonoBehaviour는 `IPoolable`을 구현해야 한다 (`OnActivate` / `OnDeactivate`).  
`PoolBase`는 Awake에서 `capacity`만큼 예열(prewarm)한다.

### 투사체 시스템 (`Gameplay/Projectile/`)

- `ProjectileData` (struct): 속도·수명·방향·시작위치 등 값 타입으로 전달
- `Projectile`: `IPoolable` 구현, Update에서 직접 이동, 수명은 코루틴으로 관리
- `ProjectilePool`: `PoolBase<Projectile>` 구현, `Spawn()` 외부 API 제공
- `ProjectileLauncher`: 씬에 배치되어 `ProjectilePool.Spawn()`을 호출하는 진입점
- `Trajectory/`: 직선·유도·포물선 이동 방식 변형 (구현 예정)

### 리소스/Addressables (`Core/Managers/ResourceManager.cs`)

Addressables 위에 얹은 참조카운트 캐시. 같은 `address`를 여러 곳에서 `Acquire`/`Release` 해도 핸들은 한 번만 로드/해제된다. 동시 로드는 내부 `UniTaskCompletionSource`로 직렬화된다.

```csharp
T asset = await ResourceManager.Instance.AcquireAsync<T>(address, ct);
// ... 사용 ...
ResourceManager.Instance.Release(address); // 참조카운트 0이 되면 실제 해제
```

- 인스턴스(GameObject)는 캐시 대상이 아님 → `AddressableHelper.InstantiateAsync` 직접 사용
- 라벨 단위 사전로드는 `PreloadByLabelAsync` + `ReleasePreloaded` 쌍으로
- 씬 전환 시 `ReleaseAll()`로 일괄 정리

### 오디오 (`Core/Audio/`)

`AudioManager`(`MonoSingleton`)가 진입점. 구조는 다음과 같다.

- BGM: `_bgmSourceA/B` 두 개를 번갈아가며 코루틴으로 크로스페이드 (`PlayBGM(clip, fade)`, `StopBGM(fade)`)
- SFX: `AudioSourcePool`(`PoolBase` 기반)에서 `AudioSourceItem`을 꺼내 재생 (`PlaySFX(clip, baseVolume)`)
- 볼륨: `AudioChannel` 3개 (Master/BGM/SFX). 실효 볼륨 = baseVolume × 그룹 × Master. SFX 그룹은 신규 재생부터 적용, 기재생 SFX는 그대로
- 볼륨은 `PlayerPrefs`(`MasterVolume` / `BGMVolume` / `SFXVolume`)로 자동 저장/복원

### Unit / Combat (`GamePlay/Unit/`, `GamePlay/Combat/`)

- `UnitBase`(abstract, `IDamageable`) → `Hero` 등 구체 유닛. 컴포지션으로 `Attribute`, `UnitMover`, `UnitAnimator`를 둔다.
- 데미지는 `DamageInfo`를 `in` 매개변수로 전달 (`TakeDamage(in DamageInfo)`).
- 충돌 판정은 `HitDetector`가 담당.
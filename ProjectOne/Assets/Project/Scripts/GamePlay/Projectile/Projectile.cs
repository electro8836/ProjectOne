using System.Collections;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOF.Projectile
{
  public class Projectile : MonoBehaviour, IPoolable
  {
    // ── 비공개 필드 ────────────────────────────────────────────────

    private PoolBase<Projectile> _ownerPool;
    private ProjectileData _data;
    private int _ownerId;
    private Coroutine _lifeCoroutine;
    // 수명 만료와 충돌이 같은 프레임에 발생할 때 이중 반환 방지
    private bool _isReleased;
    private float _traveledDistance;

    // ── Unity 생명주기 ─────────────────────────────────────────────

    private void Update()
    {
      float step = _data.speed * Time.deltaTime;
      transform.position += _data.direction * step;
      _traveledDistance += step;
	  
      if (_data.maxDistance > 0f && _traveledDistance >= _data.maxDistance)
      {
        returnToPool();
      }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
      returnToPool();
    }

    // ── 풀 인터페이스 ──────────────────────────────────────────────

    // PoolBase의 Spawn()이 GetFromPool() → Initialize() → OnActivate() 순서로 호출
    public void Initialize(ProjectileData data, int ownerId, PoolBase<Projectile> pool)
    {
      _data = data;
      _ownerId = ownerId;
      _ownerPool = pool;
      _isReleased = false;
      _traveledDistance = 0f;
      transform.position = data.startPos;
      // 2D 회전 — 스프라이트가 +X 방향 기준
      float angle = Mathf.Atan2(data.direction.y, data.direction.x) * Mathf.Rad2Deg;
      transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    public void OnActivate()
    {
      _lifeCoroutine = StartCoroutine(lifetimeRoutine());
    }

    public void OnDeactivate()
    {
      if (_lifeCoroutine != null)
      {
        StopCoroutine(_lifeCoroutine);
        _lifeCoroutine = null;
      }
      _data = default;
    }

    // ── 비공개 메서드 ──────────────────────────────────────────────

    private void returnToPool()
    {
      if (_isReleased)
      {
        return;
      }
      _isReleased = true;
      _ownerPool.Release(this);
    }

    private IEnumerator lifetimeRoutine()
    {
      yield return new WaitForSeconds(_data.lifeTime);
      returnToPool();
    }
  }
}

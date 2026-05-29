using UnityEngine;

namespace ProjectOF.Projectile
{
  public class ProjectileLauncher : MonoBehaviour
  {
    [SerializeField] private ProjectilePool _projectilePool;

    // ── 공개 메서드 ────────────────────────────────────────────────

    // missileId: 향후 다중 풀 분기(풀별 프리팹) 또는 이펙트 선택에 활용
    // ownerId:   서버 동기화, 피격 판정 귀속 등에 활용
    public void Launch(int missileId, ProjectileData data, int ownerId)
    {
      _projectilePool.Spawn(data, ownerId);
    }
  }
}

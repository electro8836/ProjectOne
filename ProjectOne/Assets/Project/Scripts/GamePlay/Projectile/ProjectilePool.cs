using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOF.Projectile
{
  // PoolBase를 상속하면 CreateItem()과 Spawn() 파라미터 정의만 하면 됨
  public class ProjectilePool : PoolBase<Projectile>
  {
    [SerializeField] private Projectile _prefab;

    protected override Projectile CreateItem()
    {
      return Instantiate(_prefab, transform);
    }

    // 외부 API - 순서(Get → Initialize → OnActivate)를 내부에서 보장
    public Projectile Spawn(ProjectileData data, int ownerId)
    {
      Projectile proj = GetFromPool();
      proj.Initialize(data, ownerId, this);
      proj.OnActivate();
      return proj;
    }
  }
}

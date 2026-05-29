using UnityEngine;

namespace ProjectOF.Projectile
{
  public struct ProjectileData
  {
    public float speed;
    public float lifeTime;
    public float maxDistance; // 0이면 거리 제한 없음
    public int damage;
    public Vector3 direction;
    public Vector3 startPos;
  }
}

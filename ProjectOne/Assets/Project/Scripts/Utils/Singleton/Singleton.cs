using UnityEngine;

namespace ProjectOne.Utils
{
  // ── 순수 C# 싱글톤 ──────────────────────────────────────────────
  // CRTP 방식. Holder 중첩 클래스를 이용한 지연 초기화 + 스레드 안전.
  // CLR은 Holder가 처음 참조될 때 Value를 단 한 번 초기화함 (락 불필요).
  // Activator.CreateInstance(nonPublic: true) 덕분에 new() 제약 없이
  // protected 생성자만으로 외부 직접 생성을 억제할 수 있다.
  //
  // 사용법:
  //   public class MyService : Singleton<MyService>
  //   {
  //     protected MyService() { }
  //   }
  public abstract class Singleton<T> where T : Singleton<T>
  {
    private static class Holder
    {
      internal static readonly T Value =
        (T)System.Activator.CreateInstance(typeof(T), nonPublic: true);
    }

    public static T Instance => Holder.Value;

    protected Singleton() { }
  }

  // ── MonoBehaviour 싱글톤 ─────────────────────────────────────────
  // CRTP 방식. 씬에 배치된 인스턴스를 우선 사용하고,
  // 없으면 런타임에 GameObject를 생성해 붙인다.
  //
  // 사용법:
  //   public class GameManager : MonoSingleton<GameManager>
  //   {
  //     protected override void Awake()
  //     {
  //       base.Awake();
  //       // 초기화 로직
  //     }
  //   }
  public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
  {
    private static T _instance;

    public static T Instance
    {
      get
      {
        if (_instance != null)
        {
          return _instance;
        }

        // 씬에 이미 배치된 인스턴스 탐색 (비용은 최초 1회)
        _instance = FindAnyObjectByType<T>();
        if (_instance != null)
        {
          return _instance;
        }

        // 씬에 없으면 런타임 생성
        GameObject go = new GameObject(typeof(T).Name);
        _instance = go.AddComponent<T>();
        DontDestroyOnLoad(go);
        return _instance;
      }
    }

    protected virtual void Awake()
    {
      if (_instance != null && _instance != this)
      {
        Destroy(gameObject);
        return;
      }
      _instance = (T)this;
      DontDestroyOnLoad(gameObject);
    }

    protected virtual void OnDestroy()
    {
      if (_instance == this)
      {
        _instance = null;
      }
    }
  }
}

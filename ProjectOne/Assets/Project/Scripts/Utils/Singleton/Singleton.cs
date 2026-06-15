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

		// 애플리케이션(또는 에디터 플레이) 종료 중 여부.
		// 종료 시점에 Instance가 새 GameObject를 만들면 정리되지 못한 채 남아
		// "Some objects were not cleaned up..." 경고가 발생하므로 이를 막는다.
		private static bool _isQuitting;

		// 새 인스턴스를 생성하지 않고 살아있는 인스턴스가 있는지만 확인.
		// OnDisable/OnDestroy 등 종료 흐름에서 Instance 접근 전에 사용한다.
		public static bool HasInstance => _instance != null && _isQuitting == false;

		// 씬 전환에도 살아남길지 여부. 기본은 영속(DontDestroyOnLoad).
		// 전투 전용 매니저 등은 false 로 오버라이드해 현재 씬 수명에만 존재하게 한다.
		protected virtual bool Persistent => true;

		public static T Instance
		{
			get
			{
				if (_instance != null)
				{
					return _instance;
				}

				// 종료 중이면 새 인스턴스를 만들지 않는다 (정리되지 못한 객체 잔류 방지)
				if (_isQuitting == true)
				{
					return null;
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
				if (_instance.Persistent == true)
				{
					DontDestroyOnLoad(go);
				}

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
			if (Persistent == true)
			{
				DontDestroyOnLoad(gameObject);
			}
		}

		// 종료 진입을 가장 먼저 포착 (OnApplicationQuit → OnDisable → OnDestroy 순서).
		// 이후 종료 흐름의 Instance 접근이 새 객체를 만들지 않도록 한다.
		protected virtual void OnApplicationQuit()
		{
			_isQuitting = true;
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

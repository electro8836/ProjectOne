using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Pets;
using ProjectOne.Resources;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	// 장착한 펫의 모델을 히어로 옆에 세우고 치우는 담당.
	//
	// **Aspect 가 아니라 별도 클래스인 이유** — Aspect 는 무상태여야 하는데 여기는 Addressable 핸들과
	// 인스턴스를 들고 있어야 한다. 게다가 Reapply 는 Remove→Apply 를 연달아 부르므로, 강화 한 번에
	// 펫이 사라졌다 다시 로드되며 깜빡인다. 스탯 재적용과 모델 수명을 분리해 둔다.
	//
	// **씬 전환은 생존이 아니라 재생성으로 푼다** — 인스턴스에 부모를 두지 않아 활성 씬에 속하므로
	// 씬이 바뀌면 히어로와 함께 사라지고, 새 히어로가 만들어질 때 UnitFactory 가 Refresh 를 부른다.
	public sealed class PetSpawner : Singleton<PetSpawner>
	{
		private GameObject _instance;

		// 현재 들고 있는 프리팹 주소 (Acquire/Release 짝 맞춤용). 비어 있으면 잡은 핸들이 없다.
		private string _address = string.Empty;

		private CancellationTokenSource _cts;

		private PetSpawner()
		{
		}

		// 장착이 바뀌었거나 히어로가 새로 만들어졌을 때 호출한다.
		// 장착 펫이 없으면 치우기만 한다.
		public void Refresh()
		{
			Hero hero = findHero();
			if (hero == null)
			{
				release();
				return;
			}

			string address = getEquippedModelAddress();
			if (string.IsNullOrEmpty(address) == true)
			{
				release();
				return;
			}

			// 같은 펫을 그대로 쓰는데 인스턴스도 살아 있으면 주인만 새로 맞춘다 —
			// 강화할 때마다 핸들을 반납했다 다시 잡으면 로드가 헛돈다.
			if (_address == address && _instance != null)
			{
				attach(hero);
				return;
			}

			release();

			_cts = new CancellationTokenSource();
			spawnAsync(hero, address, _cts.Token).Forget();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private async UniTaskVoid spawnAsync(Hero hero, string address, CancellationToken ct)
		{
			(bool cancelled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			if (prefab == null)
			{
				Debug.LogError($"[PetSpawner] 펫 프리팹 로드 실패: {address}");
				return;
			}

			_address = address;

			// await 사이에 히어로가 사라졌으면(씬 전환 등) 붙일 대상이 없다 — 핸들만 돌려준다.
			if (hero == null)
			{
				release();
				return;
			}

			// 부모를 두지 않는다 — 활성 씬에 속하므로 씬 전환 시 함께 파괴된다.
			_instance = Object.Instantiate(prefab);

			attach(hero);
		}

		private void attach(Hero hero)
		{
			if (_instance == null)
			{
				return;
			}

			Pet pet = _instance.GetComponent<Pet>();
			if (pet == null)
			{
				Debug.LogError($"[PetSpawner] {_address} 에 Pet 컴포넌트가 없다 — 펫이 따라다니지 않는다.");
				return;
			}

			pet.SetOwner(hero);
		}

		// 인스턴스 파괴 + 프리팹 핸들 반납. 재스폰과 해제 양쪽에서 쓴다.
		private void release()
		{
			if (_cts != null)
			{
				_cts.Cancel();
				_cts.Dispose();
				_cts = null;
			}

			if (_instance != null)
			{
				Object.Destroy(_instance);
				_instance = null;
			}

			// 종료 중에는 ResourceManager 가 먼저 사라져 있을 수 있다.
			if (string.IsNullOrEmpty(_address) == true || ResourceManager.HasInstance == false)
			{
				_address = string.Empty;
				return;
			}

			ResourceManager.Instance.Release(_address);
			_address = string.Empty;
		}

		private static string getEquippedModelAddress()
		{
			// 이 네임스페이스의 Pet 은 자석 펫 MonoBehaviour 다 — 테이블 enum 은 항상 정규화한다.
			EDT.Pet equipped = Account.Instance.Pet.Equipped;
			if (equipped == EDT.Pet.None)
			{
				return string.Empty;
			}

			Table_Pet.Row row = PetCatalog.Get(equipped);
			return (row != null) ? row.Model : string.Empty;
		}

		private static Hero findHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			System.Collections.Generic.IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero != null)
				{
					return hero;
				}
			}

			return null;
		}
	}
}

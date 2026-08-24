using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Resources;
using ProjectOne.Unit;

namespace ProjectOne.Avatar
{
	// 아바타 파츠 세트(ScriptableObject) 정적 캐시.
	//
	// 장착 경로(Loadout.reapplyHero)가 완전 동기라 그 자리에서 await 할 수 없다.
	// 그래서 부트에서 테이블이 참조하는 주소를 전부 미리 잡아 두고 동기로 꺼내 쓴다 —
	// MasteryCatalog 의 애니메이터 오버라이드와 같은 이유·같은 방식이다.
	//
	// 해제하지 않는다. 세션 내내 필요하고 종류도 장비·코스튬 수만큼뿐이다.
	public static class AvatarCatalog
	{
		static readonly Dictionary<string, AvatarWeaponSet> _weaponSets = new Dictionary<string, AvatarWeaponSet>();

		static readonly Dictionary<string, AvatarBodySet> _bodySets = new Dictionary<string, AvatarBodySet>();

		// 장비·코스튬 테이블이 참조하는 파츠 세트를 전부 미리 로드한다.
		// 테이블 로드 이후에 호출해야 한다 — 주소 목록을 테이블에서 뽑는다.
		public static async UniTask PreloadAsync(CancellationToken ct = default(CancellationToken))
		{
			_weaponSets.Clear();
			_bodySets.Clear();

			// 장비 무기 — 전부 무기 세트다.
			Dictionary<int, Table_Equipment.Row> equips = Table_Equipment.All();
			Dictionary<int, Table_Equipment.Row>.Enumerator ee = equips.GetEnumerator();
			while (ee.MoveNext() == true)
			{
				await loadWeaponSet(ee.Current.Value.WeaponSetAddress, ct);
			}

			// 코스튬 — CostumeType 이 어느 세트인지 정한다.
			Dictionary<int, Table_Costume.Row> costumes = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator ce = costumes.GetEnumerator();
			while (ce.MoveNext() == true)
			{
				Table_Costume.Row row = ce.Current.Value;
				if (row.CostumeType == CostumeType.Weapon)
				{
					await loadWeaponSet(row.SetAddress, ct);
				}
				else if (row.CostumeType == CostumeType.Body)
				{
					await loadBodySet(row.SetAddress, ct);
				}
			}

			Debug.Log($"[AvatarCatalog] 파츠 세트 프리로드 완료 — 무기 {_weaponSets.Count}종 / 바디 {_bodySets.Count}종");
		}

		static async UniTask loadWeaponSet(string address, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(address) == true || _weaponSets.ContainsKey(address) == true)
			{
				return;
			}

			// 구상 타입으로 요청한다 — Addressables 의 타입 필터가 베이스 타입에서 미스할 여지를 없앤다.
			AvatarWeaponSet set = await ResourceManager.Instance.AcquireAsync<AvatarWeaponSet>(address, ct);
			if (set == null)
			{
				// 조용히 넘기면 무기를 껴도 손이 비는 무음 실패가 된다.
				Debug.LogError($"[AvatarCatalog] 무기 세트 로드 실패 — address:{address}");
				return;
			}

			_weaponSets.Add(address, set);
		}

		static async UniTask loadBodySet(string address, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(address) == true || _bodySets.ContainsKey(address) == true)
			{
				return;
			}

			AvatarBodySet set = await ResourceManager.Instance.AcquireAsync<AvatarBodySet>(address, ct);
			if (set == null)
			{
				// 기본 코스튬이 여기서 실패하면 히어로가 프리팹 원본 외형으로 남는다.
				Debug.LogError($"[AvatarCatalog] 바디 세트 로드 실패 — address:{address}");
				return;
			}

			_bodySets.Add(address, set);
		}

		// 프리로드된 무기 세트를 동기로 꺼낸다. 주소가 비어 있으면 null (방어구 등 외형이 없는 장비의 정상 경로).
		public static AvatarWeaponSet GetWeaponSet(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			AvatarWeaponSet set;
			if (_weaponSets.TryGetValue(address, out set) == false)
			{
				Debug.LogError($"[AvatarCatalog] 프리로드되지 않은 무기 세트 — address:{address}");
				return null;
			}

			return set;
		}

		// 프리로드된 바디 세트를 동기로 꺼낸다. 주소가 비어 있으면 null.
		public static AvatarBodySet GetBodySet(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			AvatarBodySet set;
			if (_bodySets.TryGetValue(address, out set) == false)
			{
				Debug.LogError($"[AvatarCatalog] 프리로드되지 않은 바디 세트 — address:{address}");
				return null;
			}

			return set;
		}
	}
}

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Resources;
using ProjectOne.Unit;

namespace ProjectOne.Avatar
{
	// 아바타 바디 세트(ScriptableObject) 정적 캐시 + 무기 모델 스프라이트 조회.
	//
	// 장착 경로(Loadout.reapplyHero)가 완전 동기라 그 자리에서 await 할 수 없다.
	// 그래서 부트에서 테이블이 참조하는 주소를 전부 미리 잡아 두고 동기로 꺼내 쓴다 —
	// MasteryCatalog 의 애니메이터 오버라이드와 같은 이유·같은 방식이다.
	//
	// 해제하지 않는다. 세션 내내 필요하고 종류도 코스튬 수만큼뿐이다.
	// 무기 모델 스프라이트는 부트에서 로드한 Atlas_Parts 에 들어 있어 따로 잡지 않는다.
	public static class AvatarCatalog
	{
		static readonly Dictionary<string, AvatarBodySet> _bodySets = new Dictionary<string, AvatarBodySet>();

		static readonly Dictionary<string, WeaponFxSet> _weaponFxSets = new Dictionary<string, WeaponFxSet>();

		static readonly Dictionary<string, WeaponHoldData> _weaponHolds = new Dictionary<string, WeaponHoldData>();

		// 코스튬 테이블이 참조하는 바디 세트와 장비 테이블이 참조하는 무기 FX 세트·쥐는 자세를 전부 미리 로드한다.
		// 테이블 로드 이후에 호출해야 한다 — 주소 목록을 테이블에서 뽑는다.
		public static async UniTask PreloadAsync(CancellationToken ct = default(CancellationToken))
		{
			_bodySets.Clear();
			_weaponFxSets.Clear();
			_weaponHolds.Clear();

			Dictionary<int, Table_Costume.Row> costumes = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator ce = costumes.GetEnumerator();
			while (ce.MoveNext() == true)
			{
				Table_Costume.Row row = ce.Current.Value;
				if (row.CostumeType == CostumeType.Body)
				{
					await loadBodySet(row.SetAddress, ct);
				}
			}

			Dictionary<int, Table_Equipment.Row> equipments = Table_Equipment.All();
			Dictionary<int, Table_Equipment.Row>.Enumerator ee = equipments.GetEnumerator();
			while (ee.MoveNext() == true)
			{
				await loadWeaponFxSet(ee.Current.Value.WeaponFX, ct);
				await loadWeaponHold(ee.Current.Value.WeaponHold, ct);
			}

			Debug.Log($"[AvatarCatalog] 프리로드 완료 — 바디 세트 {_bodySets.Count}종, 무기 FX 세트 {_weaponFxSets.Count}종, 무기 자세 {_weaponHolds.Count}종");
		}

		static async UniTask loadWeaponHold(string address, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(address) == true || _weaponHolds.ContainsKey(address) == true)
			{
				return;
			}

			WeaponHoldData hold = await ResourceManager.Instance.AcquireAsync<WeaponHoldData>(address, ct);
			if (hold == null)
			{
				Debug.LogError($"[AvatarCatalog] 무기 자세 로드 실패 — address:{address}");
				return;
			}

			_weaponHolds.Add(address, hold);
		}

		// 프리로드된 무기 자세를 동기로 꺼낸다. 주소가 비어 있으면 null (자세 회전·오프셋 0).
		public static WeaponHoldData GetWeaponHold(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			WeaponHoldData hold;
			if (_weaponHolds.TryGetValue(address, out hold) == false)
			{
				Debug.LogError($"[AvatarCatalog] 프리로드되지 않은 무기 자세 — address:{address}");
				return null;
			}

			return hold;
		}

		static async UniTask loadWeaponFxSet(string address, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(address) == true || _weaponFxSets.ContainsKey(address) == true)
			{
				return;
			}

			WeaponFxSet set = await ResourceManager.Instance.AcquireAsync<WeaponFxSet>(address, ct);
			if (set == null)
			{
				Debug.LogError($"[AvatarCatalog] 무기 FX 세트 로드 실패 — address:{address}");
				return;
			}

			_weaponFxSets.Add(address, set);
		}

		// 프리로드된 무기 FX 세트를 동기로 꺼낸다. 주소가 비어 있으면 null (FX 없는 무기).
		public static WeaponFxSet GetWeaponFxSet(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			WeaponFxSet set;
			if (_weaponFxSets.TryGetValue(address, out set) == false)
			{
				Debug.LogError($"[AvatarCatalog] 프리로드되지 않은 무기 FX 세트 — address:{address}");
				return null;
			}

			return set;
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

		// 무기 모델 스프라이트를 동기로 꺼낸다(Equipment.Model / 무기 코스튬 SetAddress = 스프라이트 이름).
		// 이름이 비어 있으면 null (방어구 등 외형이 없는 장비의 정상 경로).
		public static Sprite GetWeaponModel(string name)
		{
			if (string.IsNullOrEmpty(name) == true)
			{
				return null;
			}

			Sprite sprite = AtlasManager.Instance.Get(name);
			if (sprite == null)
			{
				// 조용히 넘기면 무기를 껴도 무기가 안 보이는 무음 실패가 된다.
				Debug.LogError($"[AvatarCatalog] Atlas_Parts 에 없는 무기 모델 — name:{name}");
			}

			return sprite;
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

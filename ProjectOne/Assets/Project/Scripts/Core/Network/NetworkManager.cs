using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using EDT;
using ProjectOne.Field;
using ProjectOne.Mastery;
using ProjectOne.Shared;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Network
{
	// 네트워크 진입점(순수 C# 싱글톤) — 뒤끝 초기화 / 로그인 / 백엔드 함수 호출을 모두 관리한다.
	// 백엔드 함수는 외부에서 직접 호출하지 않고 여기 래핑된 Request* 메서드로만 호출한다(서버 권위).
	public sealed class NetworkManager : Singleton<NetworkManager>
	{
		private readonly BackndFunctionCaller _caller = new BackndFunctionCaller();

		private bool _initialized;

		// 장착 저장 전송 진행 중 가드 — 중복 flush(닫기+pause 동시 등) 방지.
		private bool _loadoutFlushing;
		private bool _stashFlushing;
		private readonly List<long> _stashUidBuffer = new List<long>();
		private bool _lockFlushing;
		private readonly List<long> _lockUidBuffer = new List<long>();
		private bool _appearanceFlushing;
		private bool _questProgressFlushing;

		// 스킬트리 저장 전송 중인 트리 — 실패하면 다시 dirty 로 되돌린다. null 이면 전송 중이 아니다.
		private MasteryProgressDto[] _masteryInFlight;

		// 현재 로그인 성공 상태 — 실패해도 게임은 로컬 데이터로 진행 가능.
		public bool IsLoggedIn { get; private set; }

		// 로그인을 한 번이라도 시도해 결과가 나왔는가(성공/실패 무관).
		//
		// 타이틀이 IsLoggedIn 만 기다리면 서버가 죽었을 때 영원히 못 넘어간다.
		// 실패도 "결정된 상태"이므로 흐름은 진행시키고, 서버 데이터 없이 도는 것은 각 상태가 감당한다.
		public bool LoginAttempted { get; private set; }

		// 계정 UUID 보관값 — GetGamerId 참고.
		private string _gamerId = string.Empty;

		private NetworkManager() { }

		// 뒤끝 SDK 초기화(1회, 멱등) — TheBackendSettings 의 키로 로컬 초기화(네트워크 아님).
		public bool Init()
		{
			if (_initialized == true)
			{
				return true;
			}

			BackendReturnObject bro = Backend.Initialize();
			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[Backnd] 초기화 실패: {bro.GetMessage()}");
				return false;
			}

			_initialized = true;
			Debug.Log("[Backnd] 초기화 성공");
			return true;
		}

		// 로그인 — 타입별 핸들러로 위임한다. 초기화가 안 됐으면 먼저 수행.
		public void Login(LoginType type, LoginCallback callback)
		{
			loginAsync(type, callback).Forget();
		}

		private async UniTaskVoid loginAsync(LoginType type, LoginCallback callback)
		{
			if (Init() == false)
			{
				LoginAttempted = true;
				callback?.Invoke(false, "초기화 실패");
				return;
			}

			ILoginHandler handler = createLoginHandler(type);
			if (handler == null)
			{
				LoginAttempted = true;
				callback?.Invoke(false, $"미지원 로그인 타입: {type}");
				return;
			}

			(bool success, string error) = await handler.LoginAsync();
			IsLoggedIn = success;
			LoginAttempted = true;
			if (success == true)
			{
				Debug.Log($"[Backnd] 로그인 성공({type}) - user: {Backend.UserInDate}");
			}
			else
			{
				Debug.LogError($"[Backnd] 로그인 실패({type}): {error}");
			}

			callback?.Invoke(success, error);
		}

		// 로그인 상태를 지운다 — 타이틀로 돌려보낼 때 쓴다.
		// 지우지 않으면 TitleState 가 이미 로그인된 것으로 보고 곧바로 다음 상태로 넘어간다.
		public void ResetLogin()
		{
			IsLoggedIn = false;
			LoginAttempted = false;
			_gamerId = string.Empty;
		}

		// 계정 UUID(gamerId). SDK 가 프로퍼티로 주지 않아 유저 정보를 한 번 조회해 보관한다.
		// 비로그인이거나 조회에 실패하면 빈 문자열 — 실패는 보관하지 않으므로 다음 호출에 다시 조회한다.
		public string GetGamerId()
		{
			if (IsLoggedIn == false)
			{
				return string.Empty;
			}

			if (string.IsNullOrEmpty(_gamerId) == false)
			{
				return _gamerId;
			}

			BackendReturnObject bro = Backend.BMember.GetUserInfo();
			if (bro.IsSuccess() == false)
			{
				Debug.LogWarning($"[Backnd] 유저 정보 조회 실패: {bro.GetMessage()}");
				return string.Empty;
			}

			LitJson.JsonData row = bro.GetReturnValuetoJSON()["row"];
			if (row == null || row.ContainsKey("gamerId") == false || row["gamerId"] == null)
			{
				Debug.LogWarning("[Backnd] 유저 정보에 gamerId 가 없다.");
				return string.Empty;
			}

			_gamerId = row["gamerId"].ToString();
			return _gamerId;
		}

		// 로그인 타입 → 핸들러. (Apple/Facebook 은 핸들러 추가 시 case 만 늘린다.)
		private ILoginHandler createLoginHandler(LoginType type)
		{
			switch (type)
			{
				case LoginType.Guest:
					return new GuestLoginHandler();
				case LoginType.Google:
					return new GoogleLoginHandler();
				default:
					return null;
			}
		}

		// ── 백엔드 함수 래퍼 ──────────────────────────────────────────────

		// 계정 데이터 요청 — 로그인 후 전체 계정 로드.
		public void RequestGetUserData(ResponseCallback<GetUserDataResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			// 로그인 후 데이터 로드는 이미 로딩 화면(LoadingManager) 흐름이라 네트워크 딤은 띄우지 않는다.
			_caller.Invoke<GetUserDataRequest, GetUserDataResponse>(FunctionName.GetUserData, new GetUserDataRequest(), callback, false);
		}

		// 던전 클리어 — 서버가 보상(exp 등)을 가산 저장 후 반환.
		public void RequestDungeonClear(DungeonClearRequest request, ResponseCallback<DungeonClearResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			// 필드 처치 배치를 먼저 큐에 넣는다 — SendQueue 가 순서대로 처리하므로 서버는 배치를 먼저 반영한 뒤 클리어를 계산한다.
			FlushFieldBatch();

			// 종료 정산은 DungeonDirector 가 클리어 메시지로 대기를 연출한다 — 딤을 띄우지 않는다.
			_caller.Invoke<DungeonClearRequest, DungeonClearResponse>(FunctionName.DungeonClear, request, callback, false);
		}

		// 장착 저장 — 8슬롯 전체를 서버가 보유 검증 후 갱신.
		public void RequestSaveLoadout(SaveLoadoutRequest request, ResponseCallback<SaveLoadoutResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			// 필드에서 주운 장비를 장착했을 수 있다 — 배치를 먼저 보내 서버 인벤토리에 넣어 둔다.
			FlushFieldBatch();

			// 장착 저장은 화면 닫기·일시정지 시점의 백그라운드 flush — 딤으로 입력을 막지 않는다.
			_caller.Invoke<SaveLoadoutRequest, SaveLoadoutResponse>(FunctionName.SaveLoadout, request, callback, false);
		}

		// ── 장착 flush 코디네이터 ─────────────────────────────────────────

		// dirty(미저장 장착 변경)면 8슬롯을 1회 전송한다(화면 닫기·앱 일시정지/종료 트리거).
		// 클릭은 이미 로컬에 낙관적 반영돼 있으므로, 여기선 묶인 변경을 한 번에 보낸다(패킷 절약).
		public void FlushLoadoutIfDirty()
		{
			if (IsLoggedIn == false)
			{
				return;	// 미로그인(Dev/오프라인) — 조용히 무시
			}

			if (_loadoutFlushing == true)
			{
				return;	// 전송 진행 중 — 응답 후 dirty 면 다음 트리거가 재시도
			}

			Loadout loadout = Account.Instance.Loadout;
			if (loadout.IsDirty == false)
			{
				return;	// 변경 없음 — 패킷 0
			}

			SaveLoadoutRequest request = new SaveLoadoutRequest();
			for (int i = 1; i < LoadoutDto.SlotCount; i++)
			{
				request.slots[i] = loadout.GetSlot((EquipSlotTypes)i);
			}

			_loadoutFlushing = true;
			RequestSaveLoadout(request, onLoadoutFlushed);
		}

		// flush 응답 — 성공 시 dirty 해제, 실패 시 dirty 유지(다음 트리거에서 재시도).
		private void onLoadoutFlushed(bool success, SaveLoadoutResponse data, string error)
		{
			_loadoutFlushing = false;
			if (success == true)
			{
				Account.Instance.Loadout.MarkSynced();
			}
			else
			{
				Debug.LogWarning($"[NetworkManager] 장착 저장 실패 — dirty 유지: {error}");
			}
		}

		// ── 보관함 flush 코디네이터 ───────────────────────────────────────

		// dirty(미저장 보관함 이동)면 보관함 UID 전체를 1회 전송한다(화면 닫기·앱 일시정지/종료 트리거).
		public void FlushStashIfDirty()
		{
			if (IsLoggedIn == false || _stashFlushing == true)
			{
				return;
			}

			Inventory inventory = Account.Instance.Inventory;
			if (inventory.IsStashDirty == false)
			{
				return;
			}

			// 필드에서 주운 장비를 보관했을 수 있다 — 배치를 먼저 보내 서버 인벤토리에 넣어 둔다.
			FlushFieldBatch();

			inventory.CollectStashUids(_stashUidBuffer);

			SaveStashRequest request = new SaveStashRequest();
			request.stashUids = _stashUidBuffer.ToArray();

			_stashFlushing = true;
			_caller.Invoke<SaveStashRequest, SaveStashResponse>(FunctionName.SaveStash, request, onStashFlushed, false);
		}

		// flush 응답 — 성공 시 dirty 해제, 실패 시 dirty 유지(다음 트리거에서 재시도).
		private void onStashFlushed(bool success, SaveStashResponse data, string error)
		{
			_stashFlushing = false;
			if (success == true)
			{
				Account.Instance.Inventory.MarkStashSynced();
			}
			else
			{
				Debug.LogWarning($"[NetworkManager] 보관함 저장 실패 — dirty 유지: {error}");
			}
		}

		// ── 잠금 flush 코디네이터 ─────────────────────────────────────────

		// dirty(미저장 잠금 변경)면 잠근 장비 UID 전체를 1회 전송한다(화면 닫기·앱 일시정지/종료·분해 직전 트리거).
		public void FlushLockIfDirty()
		{
			if (IsLoggedIn == false || _lockFlushing == true)
			{
				return;
			}

			Inventory inventory = Account.Instance.Inventory;
			if (inventory.IsLockDirty == false)
			{
				return;
			}

			// 필드에서 주운 장비를 잠갔을 수 있다 — 배치를 먼저 보내 서버 인벤토리에 넣어 둔다.
			FlushFieldBatch();

			inventory.CollectLockedUids(_lockUidBuffer);

			SaveEquipmentLockRequest request = new SaveEquipmentLockRequest();
			request.lockedUids = _lockUidBuffer.ToArray();

			_lockFlushing = true;
			_caller.Invoke<SaveEquipmentLockRequest, SaveEquipmentLockResponse>(FunctionName.SaveEquipmentLock, request, onLockFlushed, false);
		}

		// flush 응답 — 성공 시 dirty 해제, 실패 시 dirty 유지(다음 트리거에서 재시도).
		private void onLockFlushed(bool success, SaveEquipmentLockResponse data, string error)
		{
			_lockFlushing = false;
			if (success == true)
			{
				Account.Instance.Inventory.MarkLockSynced();
			}
			else
			{
				Debug.LogWarning($"[NetworkManager] 잠금 저장 실패 — dirty 유지: {error}");
			}
		}

		// ── 마스터리 ──────────────────────────────────────────────────────

		// 바뀐 스킬트리의 최종 상태를 저장한다(마스터리 화면 닫기·앱 일시정지/종료 트리거).
		// 서버는 서버 경험치 기준으로 가용 포인트를 검증한다 — 아직 올라가지 않은 필드 경험치로 찍었을 수 있어 배치를 먼저 보낸다.
		public void FlushMasteryIfDirty()
		{
			if (IsLoggedIn == false || _masteryInFlight != null)
			{
				return;
			}

			MasteryBook book = Account.Instance.Mastery;
			if (book.IsTreeDirty == false)
			{
				return;
			}

			FlushFieldBatch();

			SaveMasteryTreeRequest request = new SaveMasteryTreeRequest();
			request.trees = book.TakeDirtyTrees();
			_masteryInFlight = request.trees;
			_caller.Invoke<SaveMasteryTreeRequest, SaveMasteryTreeResponse>(FunctionName.SaveMasteryTree, request, onMasteryFlushed, false);
		}

		// 실패하면(포인트 부족 등) 다시 dirty 로 표시해 다음 트리거에 재전송한다 — 필드 경험치가 정산되면 통과한다.
		private void onMasteryFlushed(bool success, SaveMasteryTreeResponse data, string error)
		{
			if (success == false)
			{
				Debug.LogWarning($"[NetworkManager] 스킬트리 저장 실패 — 다음에 재전송: {error}");
				Account.Instance.Mastery.RestoreDirtyTrees(_masteryInFlight);
			}

			_masteryInFlight = null;
		}

		// 지식의 서 사용 — 로컬에서 이미 차감·적용했다. 서버가 같은 차감·포인트 증가를 저장한다.
		public void RequestUseSkillPointItem(int itemId, WeaponMastery target)
		{
			if (IsLoggedIn == false)
			{
				return;
			}

			// 필드에서 주운 책일 수 있다 — 배치를 먼저 보내 서버 인벤토리에 넣어 둔다.
			FlushFieldBatch();

			UseSkillPointItemRequest request = new UseSkillPointItemRequest();
			request.itemId = itemId;
			request.masteryId = (int)target;
			_caller.Invoke<UseSkillPointItemRequest, UseSkillPointItemResponse>(FunctionName.UseSkillPointItem, request, onSkillPointItemUsed, false);
		}

		private void onSkillPointItemUsed(bool success, UseSkillPointItemResponse data, string error)
		{
			if (success == false)
			{
				Debug.LogWarning($"[NetworkManager] 지식의 서 서버 반영 실패 — 다음 로그인에 서버값으로 정리된다: {error}");
			}
		}

		// 스택 아이템 차감(파괴) — 로컬은 이미 뺐다. 묶음 전송은 ItemSpendSender 가 한다(딤 없음).
		// 필드에서 주운 아이템일 수 있다 — 배치를 먼저 보내 서버 인벤토리에 넣어 둔다.
		public void RequestItemSpend(ItemSpendRequest request, ResponseCallback<ItemSpendResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<ItemSpendRequest, ItemSpendResponse>(FunctionName.ItemSpend, request, callback, false);
		}

		// ── 던전 런 ───────────────────────────────────────────────────────

		// 던전 입장 — 서버가 해금·남은 횟수를 확인해 차감하고 런(시드)을 발급한다.
		public void RequestDungeonEnter(DungeonEnterRequest request, ResponseCallback<DungeonEnterResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<DungeonEnterRequest, DungeonEnterResponse>(FunctionName.DungeonEnter, request, callback);
		}

		// 균열 소탕 — 입장 1회 + 입장보상.
		public void RequestDungeonSweep(DungeonSweepRequest request, ResponseCallback<DungeonSweepResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<DungeonSweepRequest, DungeonSweepResponse>(FunctionName.DungeonSweep, request, callback);
		}

		// 던전 유료 부활 — 서버가 부활 상한을 판정하고 재화를 차감한다.
		public void RequestDungeonRevive(DungeonReviveRequest request, ResponseCallback<DungeonReviveResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<DungeonReviveRequest, DungeonReviveResponse>(FunctionName.DungeonRevive, request, callback);
		}

		// ── 필드보스 ──────────────────────────────────────────────────────

		// 필드보스 처치 즉시 정산 — 하루 1회 제한·경험치·보상을 서버가 처리한다.
		// 마스터리 적립 대상 무기를 필드에서 주웠을 수 있어 배치를 먼저 보낸다(SendQueue 순서).
		public void RequestFieldBossKill(FieldBossKillRequest request, ResponseCallback<FieldBossKillResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<FieldBossKillRequest, FieldBossKillResponse>(FunctionName.FieldBossKill, request, callback, false);
		}

		// ── 상점 ──────────────────────────────────────────────────────────

		// 상품 구매(지금은 보물상자만) — 서버가 가격 차감·추첨·지급을 모두 하고 결과를 내려준다.
		public void RequestShopBuy(ShopBuyRequest request, ResponseCallback<ShopBuyResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<ShopBuyRequest, ShopBuyResponse>(FunctionName.ShopBuy, request, callback);
		}

		// ── 우편 ──────────────────────────────────────────────────────────

		// 관리자·랭킹 우편 목록 — 최신순. 배지 확인에도 쓰여 딤을 띄우지 않는다.
		public void RequestMailList(ResponseCallback<MailListResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<MailListRequest, MailListResponse>(FunctionName.MailList, new MailListRequest(), callback, false);
		}

		// 우편 수령 — 서버가 받으면서 첨부를 지급한다. 받은 우편은 목록에서 사라진다.
		public void RequestMailReceive(MailReceiveRequest request, ResponseCallback<MailReceiveResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<MailReceiveRequest, MailReceiveResponse>(FunctionName.MailReceive, request, callback);
		}

		// ── 장비 성장 ─────────────────────────────────────────────────────

		// 강화·승급·전이 — 서버가 공유 규칙으로 검증하고 재화 차감·장비 변경을 저장한다.
		// 필드에서 주운 재화가 서버에 먼저 반영되도록 처치 배치를 앞에 보낸다(SendQueue 순서).
		// 강화는 클라가 미리 적용하고 묶음으로 보낸다(EnhanceBatcher) — 딤을 띄우지 않는다.
		public void RequestEquipmentEnhance(EquipmentEnhanceRequest request, ResponseCallback<EquipmentGrowthResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<EquipmentEnhanceRequest, EquipmentGrowthResponse>(FunctionName.EquipmentEnhance, request, callback, false);
		}

		public void RequestEquipmentPromote(EquipmentPromoteRequest request, ResponseCallback<EquipmentGrowthResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<EquipmentPromoteRequest, EquipmentGrowthResponse>(FunctionName.EquipmentPromote, request, callback);
		}

		public void RequestEquipmentTransfer(EquipmentTransferRequest request, ResponseCallback<EquipmentGrowthResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<EquipmentTransferRequest, EquipmentGrowthResponse>(FunctionName.EquipmentTransfer, request, callback);
		}

		// 분해 — 서버가 장비를 없애고 주 재화 일부를 돌려준다.
		// 서버는 저장된 착용·보관 상태로 판정하므로, 아직 보내지 않은 장착·보관함 변경을 앞에 보낸다(SendQueue 순서).
		public void RequestEquipmentDecompose(EquipmentDecomposeRequest request, ResponseCallback<EquipmentDecomposeResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			FlushLoadoutIfDirty();
			FlushStashIfDirty();
			FlushLockIfDirty();
			_caller.Invoke<EquipmentDecomposeRequest, EquipmentDecomposeResponse>(FunctionName.EquipmentDecompose, request, callback);
		}

		// 일괄 분해 — 서버가 장비마다 착용·잠금을 다시 확인하므로 단일 분해와 같은 것을 앞에 보낸다.
		public void RequestEquipmentDecomposeAll(EquipmentDecomposeAllRequest request, ResponseCallback<EquipmentDecomposeAllResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			FlushLoadoutIfDirty();
			FlushStashIfDirty();
			FlushLockIfDirty();
			_caller.Invoke<EquipmentDecomposeAllRequest, EquipmentDecomposeAllResponse>(FunctionName.EquipmentDecomposeAll, request, callback);
		}

		// 분해 조건 저장 — 서버가 이후 지급부터 이 조건으로 자동 분해를 판정한다.
		// 조건을 바꾸기 전의 처치는 옛 조건으로 정산돼야 하므로 처치 배치를 앞에 보낸다(SendQueue 순서).
		// 분해 팝업을 닫을 때의 백그라운드 저장이라 딤을 띄우지 않는다.
		public void RequestSaveDecomposeSetting(SaveDecomposeSettingRequest request, ResponseCallback<SaveDecomposeSettingResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<SaveDecomposeSettingRequest, SaveDecomposeSettingResponse>(FunctionName.SaveDecomposeSetting, request, callback, false);
		}

		// ── 펫·외형 ───────────────────────────────────────────────────────

		// 펫 강화는 클라가 미리 적용하고 묶음으로 보낸다(PetEnhanceBatcher) — 딤을 띄우지 않는다.
		public void RequestPetEnhance(PetEnhanceRequest request, ResponseCallback<PetGrowthResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<PetEnhanceRequest, PetGrowthResponse>(FunctionName.PetEnhance, request, callback, false);
		}

		// 펫 승급 — 서버가 검증·차감·변경을 저장하고, 클라는 응답을 받은 뒤 반영한다.
		public void RequestPetPromote(PetPromoteRequest request, ResponseCallback<PetGrowthResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<PetPromoteRequest, PetGrowthResponse>(FunctionName.PetPromote, request, callback);
		}

		// 펫 장착·코스튬 착용이 dirty 면 세 값을 1회 전송한다(펫·코스튬 창 닫기·앱 일시정지/종료 트리거).
		// 클릭은 이미 로컬에 반영돼 있으므로 묶인 변경을 한 번에 보낸다(장착 flush 와 같은 패턴).
		public void FlushAppearanceIfDirty()
		{
			if (IsLoggedIn == false || _appearanceFlushing == true)
			{
				return;
			}

			if (Account.Instance.Pet.IsEquipDirty == false && Account.Instance.Costume.IsDirty == false)
			{
				return;
			}

			SaveAppearanceRequest request = new SaveAppearanceRequest();
			request.equippedPetId = (int)Account.Instance.Pet.Equipped;
			request.costumeWeaponId = Account.Instance.Costume.EquippedWeaponId;
			request.costumeBodyId = Account.Instance.Costume.EquippedBodyId;

			// 전송 시점 값으로 dirty 를 먼저 내린다 — 응답 전에 또 바뀌면 다시 dirty 가 되어 다음 트리거에 보낸다.
			Account.Instance.Pet.MarkEquipSynced();
			Account.Instance.Costume.MarkSynced();

			_appearanceFlushing = true;
			_caller.Invoke<SaveAppearanceRequest, SaveAppearanceResponse>(FunctionName.SaveAppearance, request, onAppearanceFlushed, false);
		}

		// 실패하면 dirty 를 되살려 다음 트리거에 재전송한다.
		private void onAppearanceFlushed(bool success, SaveAppearanceResponse data, string error)
		{
			_appearanceFlushing = false;
			if (success == false)
			{
				Debug.LogWarning($"[NetworkManager] 외형 저장 실패 — 다음에 재전송: {error}");
				Account.Instance.Pet.MarkEquipDirty();
				Account.Instance.Costume.MarkDirty();
			}
		}

		// ── 퀘스트·출석 ──────────────────────────────────────────────────

		// 퀘스트 완료 — 서버가 목표를 판정하고 보상을 굴려 지급한다.
		// 자동 완료는 전투 중에 일어나므로 딤으로 입력을 막지 않는다. 레벨 목표·주운 장비를 위해 배치를 먼저 보낸다.
		public void RequestQuestComplete(QuestCompleteRequest request, ResponseCallback<QuestCompleteResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<QuestCompleteRequest, QuestCompleteResponse>(FunctionName.QuestComplete, request, callback, false);
		}

		// 처치 카운터가 dirty 면 1회 저장한다(앱 일시정지/종료 트리거). 완료 요청은 카운터를 직접 싣는다.
		public void FlushQuestProgressIfDirty()
		{
			if (IsLoggedIn == false || _questProgressFlushing == true)
			{
				return;
			}

			ProjectOne.Quests.QuestBook book = Account.Instance.Quests;
			if (book.IsCounterDirty == false || book.Current.IsActive == false)
			{
				return;
			}

			SaveQuestProgressRequest request = new SaveQuestProgressRequest();
			request.questId = book.Current.questId;
			request.counter = book.Current.counter;

			// 전송 시점 값으로 dirty 를 먼저 내린다 — 응답 전에 또 바뀌면 다시 dirty 가 되어 다음 트리거에 보낸다.
			book.MarkCounterSynced();

			_questProgressFlushing = true;
			_caller.Invoke<SaveQuestProgressRequest, SaveQuestProgressResponse>(FunctionName.SaveQuestProgress, request, onQuestProgressFlushed, false);
		}

		private void onQuestProgressFlushed(bool success, SaveQuestProgressResponse data, string error)
		{
			_questProgressFlushing = false;
			if (success == false)
			{
				Debug.LogWarning($"[NetworkManager] 퀘스트 진행 저장 실패 — 다음에 재전송: {error}");
				Account.Instance.Quests.MarkCounterDirty();
			}
		}

		// 출석 수령 — 서버가 오늘 수령 여부·일차를 확인하고 보상을 굴려 지급한다.
		public void RequestDailyBonusClaim(DailyBonusClaimRequest request, ResponseCallback<DailyBonusClaimResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<DailyBonusClaimRequest, DailyBonusClaimResponse>(FunctionName.DailyBonusClaim, request, callback);
		}

		// 히어로패스 레벨 보상 수령 — 서버가 센 경험치로 판정한다. 밀린 처치를 먼저 보내 서버 레벨을 따라잡게 한다.
		public void RequestHeroPassClaim(HeroPassClaimRequest request, ResponseCallback<HeroPassClaimResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FlushFieldBatch();
			_caller.Invoke<HeroPassClaimRequest, HeroPassClaimResponse>(FunctionName.HeroPassClaim, request, callback);
		}

		// ── 랭킹 ──────────────────────────────────────────────────────────

		// 랭킹 목록 — offset 부터 50명. 첫 페이지(offset 0)에는 내 순위가 함께 온다.
		// 스크롤 중에 이어 받는 다음 페이지는 딤을 띄우지 않는다.
		public void RequestRankList(int offset, ResponseCallback<RankListResponse> callback, bool showOverlay)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			RankListRequest request = new RankListRequest();
			request.offset = offset;
			_caller.Invoke<RankListRequest, RankListResponse>(FunctionName.RankList, request, callback, showOverlay);
		}

		// 다른 유저의 정보 스냅샷.
		public void RequestRankProfile(RankProfileRequest request, ResponseCallback<RankProfileResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<RankProfileRequest, RankProfileResponse>(FunctionName.RankProfile, request, callback);
		}

		// ── 닉네임 ────────────────────────────────────────────────────────

		// 닉네임 변경 — 서버가 규칙·중복을 판정하고 비용을 차감한다.
		public void RequestChangeNickname(ChangeNicknameRequest request, ResponseCallback<ChangeNicknameResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			// 거절 사유(중복 등)는 닉네임 팝업이 직접 안내한다.
			_caller.Invoke<ChangeNicknameRequest, ChangeNicknameResponse>(FunctionName.ChangeNickname, request, callback, true, false);
		}

		// ── 필드 처치 배치 정산 ───────────────────────────────────────────

		// 정리가 끝난 처치를 앞에서부터 묶어 보낸다(주기·일시정지·종료·다른 펑션 직전 트리거).
		// 원장이 전송 중 표시를 들고 있으므로 응답 전 재호출은 빈 배치가 되어 아무것도 보내지 않는다.
		public void FlushFieldBatch()
		{
			if (IsLoggedIn == false)
			{
				return;
			}

			FieldKillLedger ledger = FieldKillLedger.Instance;
			FieldKillDto[] kills = ledger.BuildBatch();
			if (kills == null)
			{
				return;
			}

			FieldSettleRequest request = new FieldSettleRequest();
			request.sessionSeed = ledger.Seed;
			request.kills = kills;
			_caller.Invoke<FieldSettleRequest, FieldSettleResponse>(FunctionName.FieldSettle, request, onFieldSettled, false);
		}

		// 로딩 시점 세션 교체 — 남은 처치 정산 + 새 시드. 주기 배치(FieldSettle)와 action 이 달라 중복 차단에 걸리지 않는다.
		public void RequestFieldRotate(long sessionSeed, FieldKillDto[] kills, ResponseCallback<FieldRotateResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			FieldRotateRequest request = new FieldRotateRequest();
			request.sessionSeed = sessionSeed;
			request.kills = kills;
			_caller.Invoke<FieldRotateRequest, FieldRotateResponse>(FunctionName.FieldSessionRotate, request, callback, false);
		}

		// 성공이면 반영분을 지운다. 거절(검증 실패·인덱스 불일치)도 nextKillIndex 를 실어 오므로 그 기준으로 정리한다.
		// 통신 자체가 실패했으면(응답 없음) 원장을 그대로 두고 다음 트리거에 다시 보낸다.
		private void onFieldSettled(bool success, FieldSettleResponse data, string error)
		{
			if (data == null)
			{
				Debug.LogWarning($"[NetworkManager] 필드 정산 통신 실패 — 다음에 재전송: {error}");
				FieldKillLedger.Instance.OnSendFailed();
				return;
			}

			if (success == false)
			{
				Debug.LogError($"[NetworkManager] 필드 정산 거절 — 서버 기준으로 정리(nextKillIndex {data.nextKillIndex}): {error}");
			}

			// 서버가 nextKillIndex 를 모르는 실패(파싱 오류 등)는 0 으로 온다 — 이때는 지우지 않고 재전송한다.
			if (data.nextKillIndex <= 0)
			{
				FieldKillLedger.Instance.OnSendFailed();
				return;
			}

			FieldKillLedger.Instance.OnSettled(data.nextKillIndex);
		}

		// 미로그인 상태면 즉시 실패 콜백 — 오프라인/Dev 경로(DevTester 는 Account 직접 설정이라 무관).
		private bool ensureLoggedIn<TResponse>(ResponseCallback<TResponse> callback)
			where TResponse : ServerResponse
		{
			if (IsLoggedIn == false)
			{
				Debug.LogWarning("[NetworkManager] 미로그인 — 함수 호출 무시");
				callback?.Invoke(false, null, "not logged in");
				return false;
			}

			return true;
		}
	}
}

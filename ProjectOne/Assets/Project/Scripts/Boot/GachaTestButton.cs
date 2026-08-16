using System.Text;
using UnityEngine;
using EDT;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;
using ProjectOne.UI;

namespace ProjectOne.Boot
{
	// 임시 가챠 테스트 — 같은 오브젝트의 UIButton(OnClickEvent) 클릭 시 장비 가챠를 요청하고 결과를 로그/반영한다.
	// (정식 가챠 UI 전환 시 제거)
	public class GachaTestButton : MonoBehaviour
	{
		[SerializeField] private int _gachaId = 1;
		[SerializeField] private int _count = 1;

		private UIBaseInteractable _button;

		private void Awake()
		{
			// 이 프로젝트는 UnityEngine.UI.Button 이 아니라 커스텀 UIButton(UIBaseInteractable)을 쓴다 → OnClickEvent 구독.
			_button = GetComponent<UIBaseInteractable>();
			if (_button != null)
			{
				_button.OnClickEvent += onClickGacha;
			}
			else
			{
				Debug.LogWarning("[GachaTest] UIButton(UIBaseInteractable) 컴포넌트를 찾지 못했습니다.");
			}
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onClickGacha;
			}
		}

		// 버튼 클릭 — 장비 가챠 요청
		private void onClickGacha()
		{
			Debug.Log("[GachaTest] 클릭 — 장비 가챠 요청 (gachaId:" + _gachaId + ", count:" + _count + ")");
			GachaDrawRequest request = new GachaDrawRequest();
			request.gachaId = _gachaId;
			request.count = _count;
			NetworkManager.Instance.RequestEquipmentGacha(request, onGachaResult);
		}

		// 결과 콜백 — 서버 결과를 클라 인벤토리/재화에 반영 후 로그
		private void onGachaResult(bool isSuccess, GachaDrawResponse data, string errorMsg)
		{
			if (isSuccess == false || data == null)
			{
				Debug.LogError("[GachaTest] 실패: " + errorMsg);
				return;
			}

			// 서버 권위 결과를 클라에 반영 — 서버 USER_INVENTORY/USER_CURRENCY 엔 이미 저장됨(클라 메모리 동기화).
			for (int i = 0; i < data.itemIds.Length; i++)
			{
				Account.Instance.Inventory.Add(data.itemIds[i]);
			}

			Account.Instance.Wallet.SetAmount((EDT.Currency)data.currencyId, data.remainingAmount);

			StringBuilder sb = new StringBuilder();
			sb.Append("[GachaTest] 성공 — 소모:").Append(data.spentAmount);
			sb.Append(" 잔여:").Append(data.remainingAmount).Append(" / 결과: ");
			for (int i = 0; i < data.itemIds.Length; i++)
			{
				sb.Append("[grade ").Append(data.grades[i]).Append(" item ").Append(data.itemIds[i]).Append("] ");
			}

			Debug.Log(sb.ToString());
		}
	}
}

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 캐릭터 디테일 팝업의 특성(트레잇) 슬롯 1칸. 특성그룹 하나를 표시한다.
	// - 잠김(레벨 부족): Lock 활성 + LockCondition 에 "Lv {ReqLv}"
	// - 해금: Skill 에 현재 슬롯 레벨의 스킬 아이콘 표시
	public class CharacterSkillSlot : MonoBehaviour
	{
		[Header("클릭")]
		[SerializeField] private UIButton _button;

		[Header("상태 오브젝트")]
		[SerializeField] private Image _skill;				// 현재 레벨 스킬 아이콘
		[SerializeField] private GameObject _lock;			// 잠김 표시
		[SerializeField] private TMP_Text _lockCondition;	// "Lv 45"

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 클릭 시 전달할 특성그룹 ID와 구독 이벤트
		private int _traitGroupId;
		public event Action<int> OnClicked;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		// 특성그룹 상태 표시. 해금 시 skillId(현재 슬롯 레벨의 스킬) 아이콘을 보여준다.
		public async UniTask Bind(int traitGroupId, Table_CharacterTrait.Row trait, int charLevel, int skillId, CancellationToken ct)
		{
			_traitGroupId = traitGroupId;

			bool locked = charLevel < trait.ReqLv;

			_lock.SetActive(locked);
			_skill.gameObject.SetActive(!locked);

			if (locked)
			{
				_lockCondition.text = "Lv " + trait.ReqLv;
				await setIcon(null, ct);
				return;
			}

			Table_SkillInfo.Row skill = Table_SkillInfo.Get((SkillInfo)skillId);
			await setIcon(skill != null ? skill.Icon : null, ct);
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_traitGroupId);
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				_skill.sprite = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_skill.sprite = icon;
			}
		}

		private void releaseIcon()
		{
			if (!string.IsNullOrEmpty(_iconAddress) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
			releaseIcon();
		}
	}
}

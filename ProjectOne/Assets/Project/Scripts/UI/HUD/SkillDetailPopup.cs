using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;
using ProjectOne.Skill;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 특성 스킬 디테일 팝업의 View. UIManager.ShowSkillDetailPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 상태 변경 없이 특성/스킬 정보를 표시만 하므로 Presenter 없이 자체적으로 테이블을 조회한다.
	public class SkillDetailPopup : UIScreen
	{
		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _titleText;	// Text_Title
		[SerializeField] private TMP_Text _descText;	// Text_Description

		[Header("스킬 아이콘")]
		[SerializeField] private Image _skillIcon;		// SkillIcon

		[Header("레벨별 스킬 정보")]
		[SerializeField] private StepInfoSlot[] _stepInfos;	// StepInfo1 ~ StepInfo5

		[Header("버튼")]
		[SerializeField] private UIButton _returnButton;	// Button_Return

		// StepInfo 하위의 설명 텍스트(Info_Text)와 현재 레벨 강조 오브젝트(Focus) 쌍.
		[System.Serializable]
		private class StepInfoSlot
		{
			public TMP_Text infoText;	// Info_Text
			public GameObject focus;	// Focus (현재 레벨일 때만 활성)
		}

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private UniTaskCompletionSource<bool> _tcs;

		// 첫 렌더(아이콘 로드)가 끝날 때까지 숨겼다가 한 번에 보여주기 위한 그룹
		private CanvasGroup _canvasGroup;

		private void Awake()
		{
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			// 로드 완료 전까지 숨김 — Reveal 에서 보여준다.
			setVisible(false);

			_returnButton.OnClickEvent += onReturnClicked;
		}

		private void OnDestroy()
		{
			_returnButton.OnClickEvent -= onReturnClicked;
			releaseIcon();
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		// slotLevel 은 클릭한 슬롯의 현재 특성 레벨(1~5). 현재 레벨 스킬 아이콘과 강조 줄을 결정한다.
		public async UniTask ShowAsync(int traitGroupId, int slotLevel, CancellationToken ct)
		{
			Table_CharacterTrait.Row trait = Table_CharacterTrait.Get(traitGroupId);
			if (trait == null)
			{
				Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				await WaitForCloseAsync(ct);
				return;
			}

			_titleText.text = trait.Name;
			_descText.text = trait.Desc;

			setInfoGrid(trait, slotLevel);

			SkillInfo cur = TraitSkillResolver.SkillForLevel(trait, slotLevel);
			Table_SkillInfo.Row skill = Table_SkillInfo.Get(cur);
			await setIcon(skill != null ? skill.Icon : null, ct);

			Reveal();
			await WaitForCloseAsync(ct);
		}

		// 레벨별 요약을 각 StepInfo 의 Info_Text 에 채우고, 현재 슬롯 레벨(slotLevel) 의 Focus 만 활성화한다.
		// 스킬이 없는 레벨은 Info_Text 를 비워 비활성화한다.
		private void setInfoGrid(Table_CharacterTrait.Row trait, int slotLevel)
		{
			// 레벨 1~5 스킬을 한 번에 레벨별 압축 요약 5줄로 변환한다. 트레잇 개념 설명은 상단 _descText 가 담당.
			string[] lines = SkillDescriptionBuilder.BuildLevelSummaries(trait.SkillID_1, trait.SkillID_2, trait.SkillID_3, trait.SkillID_4, trait.SkillID_5);

			for (int i = 0; i < _stepInfos.Length; i++)
			{
				StepInfoSlot slot = _stepInfos[i];
				string line = i < lines.Length ? lines[i] : string.Empty;

				if (slot.focus != null)
				{
					slot.focus.SetActive(i == slotLevel - 1);
				}

				if (slot.infoText == null)
				{
					continue;
				}

				if (string.IsNullOrEmpty(line))
				{
					slot.infoText.gameObject.SetActive(false);
					continue;
				}

				slot.infoText.gameObject.SetActive(true);
				slot.infoText.text = line;
			}
		}

		// Presenter/내부가 첫 렌더(아이콘 로드)를 끝낸 뒤 호출 — 채워진 상태로 한 번에 표시.
		public void Reveal()
		{
			setVisible(true);
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다. (CharacterSkillSlot.setIcon 과 동일 규칙)
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
				_skillIcon.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = IconAtlasCache.Instance.Get(address);
			if (atlasSprite != null)
			{
				_skillIcon.sprite = atlasSprite;
				_iconAddress = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 세팅되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_skillIcon.sprite = icon;
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

		// 닫힘 대기 — ShowAsync 가 마지막에 await 한다.
		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 입력(Return)으로 닫힘을 확정한다.
		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		private void onReturnClicked()
		{
			CloseFromInput();
		}
	}
}

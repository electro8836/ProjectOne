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
	// 로비 카드스킬 그리드의 슬롯 1칸. 대표아이콘/등급프레임/보유개수 슬라이더/레벨/잠금/강화알림을 표시한다.
	// 미해금(미보유) 카드는 아이콘을 흐리게(알파40) + Lock 활성 + 슬라이더/레벨 숨김. 표시 전용.
	public class CardSkillSlot : MonoBehaviour
	{
		// 미해금 아이콘 알파 (40/255)
		private const float LockedIconAlpha = 40f / 255f;

		[Header("클릭")]
		[SerializeField] private UIButton _button;	// 슬롯 클릭 입력 (이번엔 미연결)

		[Header("내용")]
		[SerializeField] private Image _iconImage;		// Image (대표아이콘)
		[SerializeField] private Image _gradeFrame;		// Frame (등급별 이미지)
		[SerializeField] private Sprite[] _gradeFrames;	// CardSkill_Frame_01~05 (인덱스 = (int)grade-1). 추후 색상 방식으로 교체 가능

		[Header("보유개수")]
		[SerializeField] private GameObject _cardCountRoot;	// CardCountSlider (미해금 시 비활성)
		[SerializeField] private Slider _cardCountSlider;	// CardCountSlider (Slider)
		[SerializeField] private TMP_Text _cardCountText;	// Text_CardCount

		[Header("상태")]
		[SerializeField] private GameObject _levelRoot;	// Level (미해금 시 비활성)
		[SerializeField] private TMP_Text _levelText;	// Level/Text
		[SerializeField] private GameObject _lockObject;	// Lock (미해금 시 활성)
		[SerializeField] private GameObject _alertObject;	// Alert (강화 가능 시 활성)

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 클릭 시 전달할 카드스킬 ID
		private int _cardSkillId;
		public event Action<int> OnClicked;

		private void Awake()
		{
			if (_button != null)
			{
				_button.OnClickEvent += onClicked;
			}
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onClicked;
			}

			releaseIcon();
		}

		// 슬롯 바인딩. unlocked=false(미해금) 면 잠금 표시(아이콘 흐림/레벨·슬라이더 숨김).
		// 슬라이더 채움/강화알림은 보유개수(ownedCount) 기준, level/requiredCount/maxed 는 호출자가 산출해 전달.
		public async UniTask Bind(Table_CardSkill.Row card, bool unlocked, int ownedCount, int level, int requiredCount, bool maxed, CancellationToken ct)
		{
			_cardSkillId = card.ID;

			if (_gradeFrame != null)
			{
				_gradeFrame.sprite = gradeFrameFor(card.CardGrade);
			}

			if (_lockObject != null)
			{
				_lockObject.SetActive(unlocked == false);
			}

			if (_levelRoot != null)
			{
				_levelRoot.SetActive(unlocked);
			}

			if (unlocked == true && _levelText != null)
			{
				_levelText.text = level.ToString();
			}

			if (_cardCountRoot != null)
			{
				_cardCountRoot.SetActive(unlocked);
			}

			if (unlocked == true)
			{
				applyCardCount(ownedCount, requiredCount, maxed);
			}

			if (_alertObject != null)
			{
				_alertObject.SetActive(unlocked == true && maxed == false && requiredCount > 0 && ownedCount >= requiredCount);
			}

			await setIcon(card.Icon, unlocked, ct);
		}

		// 보유개수 표시 — 최대레벨이면 "최대강화", 아니면 "보유/필요" + 슬라이더 비율
		private void applyCardCount(int ownedCount, int requiredCount, bool maxed)
		{
			if (maxed == true || requiredCount <= 0)
			{
				if (_cardCountText != null)
				{
					_cardCountText.text = "최대강화";
				}

				if (_cardCountSlider != null)
				{
					_cardCountSlider.value = 1f;
				}

				return;
			}

			if (_cardCountText != null)
			{
				_cardCountText.text = ownedCount.ToString() + "/" + requiredCount.ToString();
			}

			if (_cardCountSlider != null)
			{
				_cardCountSlider.value = Mathf.Clamp01((float)ownedCount / requiredCount);
			}
		}

		// 등급 → 프레임 스프라이트 (None 제외, 인덱스 = (int)grade-1)
		private Sprite gradeFrameFor(CardSkillGrade grade)
		{
			int index = (int)grade - 1;
			if (_gradeFrames == null || index < 0 || index >= _gradeFrames.Length)
			{
				return null;
			}

			return _gradeFrames[index];
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_cardSkillId);
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다. 로드 후 해금 여부로 알파 적용.
		private async UniTask setIcon(string address, bool unlocked, CancellationToken ct)
		{
			applyIconAlpha(unlocked);

			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				if (_iconImage != null)
				{
					_iconImage.sprite = null;
				}

				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				if (_iconImage != null)
				{
					_iconImage.sprite = atlasSprite;
				}

				_iconAddress = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 슬롯이 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null && _iconImage != null)
			{
				_iconImage.sprite = icon;
			}
		}

		// 해금 여부로 아이콘 알파만 조절 (미해금 흐리게)
		private void applyIconAlpha(bool unlocked)
		{
			if (_iconImage == null)
			{
				return;
			}

			Color c = _iconImage.color;
			c.a = unlocked ? 1f : LockedIconAlpha;
			_iconImage.color = c;
		}

		private void releaseIcon()
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}
	}
}

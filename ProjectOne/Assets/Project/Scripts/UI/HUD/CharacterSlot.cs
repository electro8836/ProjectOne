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
	// 캐릭터 그리드의 슬롯 1칸. 등급 색상 + 아이콘 + 클래스/이름/등급별 + 잠김(Unlock)/레벨/선택(Select)을 표시한다.
	// 클릭하면 캐릭터 ID 를 OnClicked 로 통지한다(디테일 팝업 진입).
	public class CharacterSlot : MonoBehaviour
	{
		[Header("클릭")]
		[SerializeField] private UIButton _button;	// 슬롯 클릭 입력

		[Header("등급 색상 대상")]
		[SerializeField] private Image _bgMask;
		[SerializeField] private Image _bgGradient;

		[Header("내용")]
		[SerializeField] private Image _characterIcon;
		[SerializeField] private Image _classIcon;
		[SerializeField] private TMP_Text _nameText;
		[SerializeField] private TMP_Text _levelText;
		[SerializeField] private Image[] _stars;	// Grade_Stars 하위 별 (등급만큼 활성)

		[Header("상태 오브젝트")]
		[SerializeField] private GameObject _unlock;	// 잠김(미보유) 표시
		[SerializeField] private GameObject _levelRoot;	// 보유 시 레벨 표시
		[SerializeField] private GameObject _select;	// 주캐릭터 표시

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 클릭 시 전달할 캐릭터 ID와 구독 이벤트
		private int _characterId;
		public event Action<int> OnClicked;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		public async UniTask Bind(Table_Character.Row row, bool owned, int level, bool selected, CharacterGradeColorTable colors, Sprite classIcon, CancellationToken ct)
		{
			_characterId = row.ID;

			CharacterGradeColorTable.GradeColor gc = colors.Get(row.Grade);
			_bgMask.color = gc.bgMask;
			_bgGradient.color = gc.gradient;

			_unlock.SetActive(!owned);
			_levelRoot.SetActive(owned);
			if (owned)
			{
				_levelText.text = level.ToString();
			}

			_classIcon.sprite = classIcon;
			_nameText.text = row.Name;
			_select.SetActive(selected);

			applyStars(row.Grade);

			await setIcon(row.Icon, ct);
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_characterId);
			}
		}

		// 등급만큼 별을 활성화한다.
		private void applyStars(CharacterGrade grade)
		{
			int count = (int)grade;
			for (int i = 0; i < _stars.Length; i++)
			{
				_stars[i].gameObject.SetActive(i < count);
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		// 호출자가 await 하므로 로드 완료 시점에 아이콘이 채워진다.
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
				_characterIcon.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_characterIcon.sprite = atlasSprite;
				_iconAddress = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 슬롯이 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_characterIcon.sprite = icon;
			}
		}

		private void releaseIcon()
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
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

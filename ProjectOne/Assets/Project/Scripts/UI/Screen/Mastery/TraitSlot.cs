using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 마스터리 스킬 트리 보드의 칸 1개(UIPrefab_TraitSlot).
	//
	// 격자는 MaxRow × MaxColumn 로 고정이고 노드가 없는 칸도 자리를 차지해야 한다 —
	// 끄면 HorizontalLayoutGroup 이 그 칸을 무시해 뒤 칸이 앞으로 밀리므로 스케일만 0으로 만든다.
	public class TraitSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;		// 루트 자신
		[SerializeField] private GameObject _line;		// Line — 선행 노드로 이어지는 연결선
		[SerializeField] private GameObject _fill;		// Line/Fill — 투자 완료 표시
		[SerializeField] private Image _icon;			// Icon
		[SerializeField] private TMP_Text _pointText;	// Point/PointText
		[SerializeField] private Image _border;			// Border — Line 아래 것이 아니라 슬롯 테두리다

		[Header("투자 표시 색")]
		[SerializeField] private Color _borderEmptyColor = new Color32(0x49, 0x3D, 0x6D, 0xFF);
		[SerializeField] private Color _borderInvestedColor = new Color32(0xFF, 0xBB, 0x40, 0xFF);

		// 클릭된 슬롯 자신을 통지한다. 무엇을 할지는 Presenter 가 정한다.
		// 노드 ID 만으로는 부족하다 — 팝업이 이 칸 옆에 떠야 해서 RectTransform 도 필요하다.
		public event Action<TraitSlot> OnClicked;

		private int _nodeId;

		public int NodeId
		{
			get { return _nodeId; }
		}

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
			releaseIcon();
		}

		public UniTask BindAsync(TraitSlotData data, CancellationToken ct)
		{
			_nodeId = data.nodeId;

			transform.localScale = Vector3.one;
			_button.interactable = true;

			_line.SetActive(data.hasPrev);

			// 연결선의 Fill 과 같은 조건이다. 선행이 없는 노드는 Line 자체가 꺼져 있어
			// 테두리가 유일한 투자 표시가 된다.
			_fill.SetActive(data.invested);
			_border.color = data.invested ? _borderInvestedColor : _borderEmptyColor;
			_pointText.text = $"{data.level}/{data.maxLevel}";

			return setIcon(data.iconAddress, ct);
		}

		// 노드가 없는 칸. 자리는 남기고 보이지만 않게 한다.
		public void SetEmpty()
		{
			_nodeId = 0;

			transform.localScale = Vector3.zero;
			_button.interactable = false;
		}

		private void onClicked()
		{
			if (_nodeId == 0)
			{
				return;
			}

			if (OnClicked != null) { OnClicked.Invoke(this); }
		}

		// ── 내부: 아이콘 ───────────────────────────────────────────────────

		// ItemSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_icon.sprite = null;
				_icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
				_icon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}

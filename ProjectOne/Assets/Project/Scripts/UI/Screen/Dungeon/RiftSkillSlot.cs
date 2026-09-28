using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 균열 스킬 한 칸 — 스킬 아이콘과 선택 테두리(녹색). 누르면 자기가 눌렸다는 것만 알린다.
	// 어느 칸이 선택됐는지는 RiftDungeonPopup 이 SetSelected 로 지시한다.
	public class RiftSkillSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;	// 루트
		[SerializeField] private Image _icon;		// Icon
		[SerializeField] private Image _border;		// Border

		[SerializeField] private Color _selectedColor = Color.green;

		public event Action<RiftSkillSlot> OnClicked;

		// 선택 해제 시 되돌릴 프리팹의 원래 테두리 색
		private Color _normalColor;
		private SpriteBinder _iconBinder;
		private int _riftSkillId;

		public int RiftSkillId
		{
			get { return _riftSkillId; }
		}

		private void Awake()
		{
			_normalColor = _border.color;
			_iconBinder = new SpriteBinder(_icon);
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
			_iconBinder.Release();
		}

		public void Bind(int riftSkillId, string iconAddress)
		{
			_riftSkillId = riftSkillId;
			_iconBinder.SetAsync(iconAddress, this.GetCancellationTokenOnDestroy()).Forget();
		}

		public void SetSelected(bool selected)
		{
			_border.color = selected ? _selectedColor : _normalColor;
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}
	}
}

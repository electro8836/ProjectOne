using System;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 포탈 UI의 필드 1칸. 잠긴 필드는 요구 레벨을 보여주고 클릭을 막는다.
	public class FieldSelectSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;
		[SerializeField] private TMP_Text _nameText;
		[SerializeField] private TMP_Text _reqLevelText;
		[SerializeField] private GameObject _lockObject;

		private int _fieldId;
		private Action<int> _onClicked;

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
		}

		public void Bind(int fieldId, string displayName, int reqLevel, bool unlocked, Action<int> onClicked)
		{
			_fieldId = fieldId;
			_onClicked = onClicked;

			if (_nameText != null)
			{
				_nameText.text = displayName;
			}

			if (_reqLevelText != null)
			{
				_reqLevelText.text = (reqLevel > 0) ? ("Lv." + reqLevel) : string.Empty;
			}

			if (_lockObject != null)
			{
				_lockObject.SetActive(unlocked == false);
			}

			_button.interactable = unlocked;
		}

		private void onClicked()
		{
			if (_onClicked != null)
			{
				_onClicked.Invoke(_fieldId);
			}
		}
	}
}

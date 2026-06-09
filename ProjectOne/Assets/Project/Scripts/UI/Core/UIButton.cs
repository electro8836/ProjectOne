using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectOne.Audio;

namespace ProjectOne.UI
{
	// Button 상속으로 SFX를 자동 처리한다.
	// 씬에서 Button 대신 UIButton을 선택하면 onClick 콜백 등록은 기존과 동일하게 사용.
	public class UIButton : Button
	{
		// 빈 문자열이면 AudioManager 기본 버튼음 사용
		[SerializeField] private string _sfxAddress = string.Empty;

		// onClick 리스너 대신 override로 처리 — 부모 onClick 콜백과 순서 충돌 없음
		public override void OnPointerClick(PointerEventData eventData)
		{
			base.OnPointerClick(eventData);

			if (IsActive() && IsInteractable())
			{
				playSfx();
			}
		}

		// 게임패드/키보드 Submit 경로도 동일하게 처리
		public override void OnSubmit(BaseEventData eventData)
		{
			base.OnSubmit(eventData);

			if (IsActive() && IsInteractable())
			{
				playSfx();
			}
		}

		private void playSfx()
		{
			if (!AudioManager.HasInstance)
			{
				return;
			}

			AudioManager.Instance.PlaySFX(_sfxAddress);
		}
	}
}

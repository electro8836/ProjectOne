using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 시스템 로그 한 줄. 텍스트가 줄바꿈되면 줄 수만큼 슬롯 높이를 늘린다.
	//
	// 부모 VerticalLayoutGroup 이 자식 높이를 제어하지 않으므로(ChildControlHeight 꺼짐) 높이는 여기서 직접 정한다.
	public class SystemLogSlot : MonoBehaviour
	{
		// 한 줄 높이 — 두 줄이면 80, 세 줄이면 120.
		private const float LineHeight = 40f;

		// 표시 후 사라지기까지의 시간(초)
		private const float LifeSeconds = 5f;

		[SerializeField] private TMP_Text _logText;

		// 이 시각(Time.time)이 지나면 SystemLogInfo 가 거둔다.
		public float ExpireTime { get; private set; }

		public void SetText(string text)
		{
			ExpireTime = Time.time + LifeSeconds;
			_logText.text = text;

			// 줄 수는 메시 생성 뒤에야 확정된다 — 레이아웃 갱신을 기다리지 않고 즉시 계산한다.
			// HUD 가 맥락으로 꺼져 있을 때 들어온 로그도 줄 수를 알아야 하므로 비활성 상태에서도 강제한다.
			_logText.ForceMeshUpdate(true);
			int lineCount = Mathf.Max(1, _logText.textInfo.lineCount);

			// Awake 에서 캐시하지 않는다 — 부모가 꺼진 채 Instantiate 되면 Awake 가 아직 불리지 않는다.
			RectTransform rectTransform = (RectTransform)this.transform;
			Vector2 size = rectTransform.sizeDelta;
			size.y = LineHeight * lineCount;
			rectTransform.sizeDelta = size;
		}
	}
}

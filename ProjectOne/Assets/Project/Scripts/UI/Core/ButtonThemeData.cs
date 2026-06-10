using UnityEngine;

namespace ProjectOne.UI
{
	// 버튼 연출(눌림 오프셋·사운드·이펙트)을 묶은 테마 데이터.
	// 여러 버튼이 같은 테마를 공유해 일관된 피드백을 갖게 한다.
	[CreateAssetMenu(menuName = "Custom UI/Button Theme Data", fileName = "NewButtonTheme")]
	public class ButtonThemeData : ScriptableObject
	{
		[Header("Animation (Offset)")]
		public Vector2 pressedOffset = new Vector2(0f, -5f);
		public float animationDuration = 0.1f;

		[Header("Audio (Addressable 주소)")]
		public string sfxAddress;

		[Header("Effect (Addressable 주소)")]
		public string vfxAddress;
	}
}

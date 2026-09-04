#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace ProjectOne.Map
{
	// 스폰 반경 씬뷰 표시 — DungeonSpawnSlot / MonsterSpawnPoint 공용.
	//
	// 둘이 같은 모양으로 보여야 하는데 각자 그리면 한쪽만 고쳐져 조용히 어긋난다.
	//
	// Gizmos.DrawWireSphere 가 아니라 Handles.DrawWireDisc 를 쓰는 이유 —
	// 실제 스폰은 RandomPosition() 의 Random.insideUnitCircle 로 **XY 평면의 원** 안에서 뽑는다.
	// 구를 그리면 실제로는 뽑히지 않는 영역까지 그려지고, 2D 씬뷰에서는 원 세 개가 겹쳐 지저분하다.
	internal static class SpawnRadiusGizmo
	{
		// 선택 시 안쪽 채움 투명도. 겹치는 슬롯을 구분할 정도만 준다.
		private const float SelectedFillAlpha = 0.12f;

		// 선택하지 않은 슬롯의 선 투명도. 배치를 훑는 용도라 시야를 가리면 안 된다.
		private const float IdleLineAlpha = 0.35f;

		// 중심 십자표 크기(월드 단위)
		private const float CrossSize = 0.2f;

		// 라벨을 원 위로 띄우는 여백
		private const float LabelPadding = 0.3f;

		public static void Draw(Transform owner, float radius, Color color, bool selected, string label)
		{
			Vector3 pos = owner.position;

			if (selected == true && radius > 0f)
			{
				Color fill = color;
				fill.a = SelectedFillAlpha;
				Handles.color = fill;
				Handles.DrawSolidDisc(pos, Vector3.forward, radius);
			}

			Color line = color;
			line.a = (selected == true) ? 1f : IdleLineAlpha;
			Handles.color = line;

			if (radius > 0f)
			{
				Handles.DrawWireDisc(pos, Vector3.forward, radius);
			}

			// 십자표는 반경과 무관하게 항상 그린다.
			// 반경 0 은 "이 자리에 정확히" 라는 유효한 설정인데, 원만 그리면 자리가 통째로 안 보인다.
			Handles.DrawLine(pos + Vector3.left * CrossSize, pos + Vector3.right * CrossSize);
			Handles.DrawLine(pos + Vector3.down * CrossSize, pos + Vector3.up * CrossSize);

			// 라벨은 선택 여부와 무관하게 그린다 — "어느 게 0번인가" 는 선택하기 전에 알아야 한다.
			Handles.Label(pos + Vector3.up * (radius + LabelPadding), label);
		}
	}
}
#endif

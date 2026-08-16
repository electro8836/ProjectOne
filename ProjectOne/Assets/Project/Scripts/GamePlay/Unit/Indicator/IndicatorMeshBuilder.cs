using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.Unit
{
	// ScanType 모양별 인디케이터 메시 생성 — 모양마다 서브클래스가 Build 를 구현한다.
	// 공통 기하(부채꼴/링/직선)는 베이스의 protected static 헬퍼로 공유한다.
	// 정점은 모두 로컬 원점 기준. 파라미터 해석은 Scanner / TargetResolver 판정과 일치시킨다.
	public abstract class IndicatorMeshBuilder
	{
		public abstract Mesh Build(float param1, float param2, int segments, float ringThickness);

		// ScanType → 빌더 매핑. stateless 이므로 인스턴스를 하나씩만 두고 재사용한다.
		private static readonly Dictionary<SkillScanTypes, IndicatorMeshBuilder> _builders = new Dictionary<SkillScanTypes, IndicatorMeshBuilder>
		{
			{ SkillScanTypes.Circle, new CircleMeshBuilder() },
			{ SkillScanTypes.Target, new TargetMeshBuilder() },
			{ SkillScanTypes.Sector, new SectorMeshBuilder() },
			{ SkillScanTypes.Line,   new LineMeshBuilder() },
		};

		// 미지원/None 이면 null (표시 대상 아님)
		public static IndicatorMeshBuilder Get(SkillScanTypes type)
		{
			IndicatorMeshBuilder builder;
			_builders.TryGetValue(type, out builder);
			return builder;
		}

		// 부채꼴/원 메시. centeredOnX=true 면 +X축 중심으로 ±fullAngle/2,
		// false 면 0~fullAngle 전체.
		protected static Mesh BuildFan(float radius, float fullAngleDeg, bool centeredOnX, int segments)
		{
			int seg = Mathf.Max(3, segments);
			float startRad = (centeredOnX ? -fullAngleDeg * 0.5f : 0f) * Mathf.Deg2Rad;
			float stepRad = fullAngleDeg * Mathf.Deg2Rad / seg;

			Vector3[] verts = new Vector3[seg + 2];
			verts[0] = Vector3.zero;
			for (int i = 0; i <= seg; i++)
			{
				float a = startRad + stepRad * i;
				verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
			}

			int[] tris = new int[seg * 3];
			for (int i = 0; i < seg; i++)
			{
				tris[i * 3] = 0;
				tris[i * 3 + 1] = i + 1;
				tris[i * 3 + 2] = i + 2;
			}

			return CreateMesh(verts, tris);
		}

		// 도넛/링 메시. innerR~outerR 사이를 채운다.
		protected static Mesh BuildRing(float innerR, float outerR, int segments)
		{
			if (innerR < 0f)
			{
				innerR = 0f;
			}

			int seg = Mathf.Max(3, segments);
			float stepRad = Mathf.PI * 2f / seg;

			Vector3[] verts = new Vector3[(seg + 1) * 2];
			for (int i = 0; i <= seg; i++)
			{
				float a = stepRad * i;
				float cos = Mathf.Cos(a);
				float sin = Mathf.Sin(a);
				verts[i * 2] = new Vector3(cos * innerR, sin * innerR, 0f);
				verts[i * 2 + 1] = new Vector3(cos * outerR, sin * outerR, 0f);
			}

			int[] tris = new int[seg * 6];
			for (int i = 0; i < seg; i++)
			{
				int inner0 = i * 2;
				int outer0 = i * 2 + 1;
				int inner1 = (i + 1) * 2;
				int outer1 = (i + 1) * 2 + 1;
				int t = i * 6;
				tris[t] = inner0;
				tris[t + 1] = outer0;
				tris[t + 2] = outer1;
				tris[t + 3] = inner0;
				tris[t + 4] = outer1;
				tris[t + 5] = inner1;
			}

			return CreateMesh(verts, tris);
		}

		// 직선(사각형) 메시. origin 이 시작점, +X 로 length, 폭 ±width/2.
		protected static Mesh BuildLineMesh(float length, float width)
		{
			float half = width * 0.5f;
			Vector3[] verts = new Vector3[4];
			verts[0] = new Vector3(0f, -half, 0f);
			verts[1] = new Vector3(0f, half, 0f);
			verts[2] = new Vector3(length, half, 0f);
			verts[3] = new Vector3(length, -half, 0f);
			int[] tris = new int[6] { 0, 1, 2, 0, 2, 3 };
			return CreateMesh(verts, tris);
		}

		protected static Mesh CreateMesh(Vector3[] verts, int[] tris)
		{
			Mesh mesh = new Mesh();
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	// 원형 — param1 = 반지름
	public sealed class CircleMeshBuilder : IndicatorMeshBuilder
	{
		public override Mesh Build(float param1, float param2, int segments, float ringThickness)
		{
			return BuildFan(param1, 360f, false, segments);
		}
	}

	// 타겟 — param1 = 사거리. 링 바깥 가장자리를 param1 에 맞춰(안쪽 = param1 - 두께) 사거리 기준을 명확히 한다
	public sealed class TargetMeshBuilder : IndicatorMeshBuilder
	{
		public override Mesh Build(float param1, float param2, int segments, float ringThickness)
		{
			return BuildRing(param1 - ringThickness, param1, segments);
		}
	}

	// 부채꼴 — param1 = 반지름, param2 = 각도
	public sealed class SectorMeshBuilder : IndicatorMeshBuilder
	{
		public override Mesh Build(float param1, float param2, int segments, float ringThickness)
		{
			return BuildFan(param1, param2, true, segments);
		}
	}

	// 직선 — param1 = 길이, param2 = 폭
	public sealed class LineMeshBuilder : IndicatorMeshBuilder
	{
		public override Mesh Build(float param1, float param2, int segments, float ringThickness)
		{
			return BuildLineMesh(param1, param2);
		}
	}
}

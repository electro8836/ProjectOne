using UnityEngine;
using UnityEngine.Tilemaps;

namespace ProjectOne.Map
{
	public class TilemapGrid : MonoBehaviour
	{
		private static readonly Vector2[] _sampleDirections =
		{
			new Vector2( 1f,      0f),
			new Vector2(-1f,      0f),
			new Vector2( 0f,      1f),
			new Vector2( 0f,     -1f),
			new Vector2( 0.7071f,  0.7071f),
			new Vector2(-0.7071f,  0.7071f),
			new Vector2( 0.7071f, -0.7071f),
			new Vector2(-0.7071f, -0.7071f),
		};

		// 맵 프리팹에서 인스펙터로 직접 연결 — 자식 이름 매칭 대신 명시적 참조
		[SerializeField] private Grid _grid;
		[SerializeField] private Tilemap _groundMap;
		[SerializeField] private Tilemap _obstacleMap;

		private FlowField _flowField = new FlowField();
		private Vector3Int _boundsMin;

		[SerializeField] bool DEV_ShowDrawGizmos = false;

		public void InitializeFlowField()
		{
			if (_flowField == null || _groundMap == null)
			{
				return;
			}

			BoundsInt bounds = _groundMap.cellBounds;
			_boundsMin = bounds.min;

			int width  = bounds.size.x;
			int height = bounds.size.y;
			bool[] walkable = new bool[width * height];

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Vector3Int cell = new Vector3Int(_boundsMin.x + x, _boundsMin.y + y, 0);
					walkable[y * width + x] = IsCellWalkable(cell);
				}
			}

			_flowField.Initialize(width, height, walkable);
		}

		public void BakeFlowField(Vector2 worldPos)
		{
			Vector3Int cell = WorldToCell(worldPos);
			int fx = cell.x - _boundsMin.x;
			int fy = cell.y - _boundsMin.y;
			_flowField.Bake(fx, fy);
		}

		public Vector2 GetFlowDirection(Vector2 worldPos)
		{
			Vector3Int cell = WorldToCell(worldPos);
			int fx = cell.x - _boundsMin.x;
			int fy = cell.y - _boundsMin.y;
			return _flowField.GetDirection(fx, fy);
		}

		public Vector3Int WorldToCell(Vector2 worldPos)
		{
			return _grid.WorldToCell(new Vector3(worldPos.x, worldPos.y, 0f));
		}

		public Vector2 CellToWorld(Vector3Int cellPos)
		{
			return _grid.GetCellCenterWorld(cellPos);
		}

		public TileBase GetGroundTile(Vector3Int cellPos)
		{
			return _groundMap != null ? _groundMap.GetTile(cellPos) : null;
		}

		public TileBase GetObstacleTile(Vector3Int cellPos)
		{
			return _obstacleMap != null ? _obstacleMap.GetTile(cellPos) : null;
		}

		public bool IsCellWalkable(Vector3Int cellPos)
		{
			if (_obstacleMap == null)
			{
				return true;
			}

			return _obstacleMap.GetTile(cellPos) == null;
		}

		public bool IsWalkable(Vector2 position, float radius)
		{
			if (!IsCellWalkable(WorldToCell(position)))
			{
				return false;
			}

			for (int i = 0; i < _sampleDirections.Length; i++)
			{
				if (!IsCellWalkable(WorldToCell(position + _sampleDirections[i] * radius)))
				{
					return false;
				}
			}

			return true;
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (_grid == null || _flowField == null || _flowField.Width == 0)
			{
				return;
			}

			if(DEV_ShowDrawGizmos == false)
			{
				return;
			}

			Vector3 cellSize  = _grid.cellSize;
			float   halfW     = cellSize.x * 0.5f;
			float   halfH     = cellSize.y * 0.5f;
			float   innerHalf = Mathf.Min(cellSize.x, cellSize.y) * 0.4f;

			for (int y = 0; y < _flowField.Height; y++)
			{
				for (int x = 0; x < _flowField.Width; x++)
				{
					Vector3Int cellPos = new Vector3Int(_boundsMin.x + x, _boundsMin.y + y, 0);
					Vector3 center = _grid.GetCellCenterWorld(cellPos);

					DEV_DrawGizmosBox(center, new Vector2(halfW, halfH), Color.white);

					if (_flowField.GetCostAt(x, y) == false)
					{
						DEV_DrawGizmosX(center, innerHalf, Color.red);
					}
					else
					{
						Vector2 dir = _flowField.GetDirection(x, y);
						if (dir != Vector2.zero)
						{
							DEV_DrawGizmosArrow(center, dir, innerHalf * 2f, Color.yellow);
						}
					}
				}
			}
		}

		private void DEV_DrawGizmosX(Vector3 center, float size, Color color)
		{
			Gizmos.color = color;
			Gizmos.DrawLine(center + new Vector3(-size, -size, 0f), center + new Vector3(size, size, 0f));
			Gizmos.DrawLine(center + new Vector3(-size,  size, 0f), center + new Vector3(size, -size, 0f));
		}

		private void DEV_DrawGizmosArrow(Vector3 center, Vector2 dir, float length, Color color)
		{
			Gizmos.color = color;
			Vector3 d     = new Vector3(dir.x, dir.y, 0f);
			float   half  = length * 0.5f;
			Vector3 start = center - d * half;
			Vector3 end   = center + d * half;
			Gizmos.DrawLine(start, end);

			float   headSize = length * 0.3f;
			Vector3 right    = new Vector3(-d.y, d.x, 0f);
			Gizmos.DrawLine(end, end - d * headSize + right * headSize);
			Gizmos.DrawLine(end, end - d * headSize - right * headSize);
		}

		private void DEV_DrawGizmosBox(Vector3 center, Vector2 size, Color color)
		{
			Gizmos.color = color;
			float left = center.x - size.x;
			float right = center.x + size.x;
			float up = center.y - size.y;
			float down = center.y + size.y;

			Gizmos.DrawLine(new Vector2(left, up), new Vector2(right, up));
			Gizmos.DrawLine(new Vector2(left, down), new Vector2(right, down));
			Gizmos.DrawLine(new Vector2(left, up), new Vector2(left, down));
			Gizmos.DrawLine(new Vector2(right, up), new Vector2(right, down));
		}
#endif
	}
}

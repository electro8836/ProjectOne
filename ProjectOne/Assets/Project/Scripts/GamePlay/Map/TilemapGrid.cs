using ProjectOne.Utils;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ProjectOne.Map
{
	public class TilemapGrid : MonoSingleton<TilemapGrid>
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

		private Grid _grid;
		private Tilemap _groundMap;
		private Tilemap _obstacleMap;
		private FlowField _flowField = new FlowField();
		private Vector3Int _boundsMin;

		public void Setup(Grid grid, Tilemap groundMap, Tilemap obstacleMap)
		{
			_grid = grid;
			_groundMap = groundMap;
			_obstacleMap = obstacleMap;
			InitializeFlowField();
		}

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
	}
}

using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Map
{
	public class FlowField
	{
		private static readonly Vector2Int[] _neighborOffsets =
		{
			new Vector2Int( 0,  1),
			new Vector2Int( 1,  0),
			new Vector2Int( 0, -1),
			new Vector2Int(-1,  0),
			new Vector2Int( 1,  1),
			new Vector2Int( 1, -1),
			new Vector2Int(-1, -1),
			new Vector2Int(-1,  1),
		};

		// _neighborOffsets 를 미리 정규화한 단위벡터 — 인덱스가 1:1로 대응한다.
		// 이웃 방향은 항상 -1/0/1 조합이라 흐름 벡터에 normalized 가 필요 없다(셀 수만큼의 sqrt 제거).
		private static readonly Vector2[] _neighborDirections =
		{
			new Vector2( 0f,  1f),
			new Vector2( 1f,  0f),
			new Vector2( 0f, -1f),
			new Vector2(-1f,  0f),
			new Vector2( 0.70710678f,  0.70710678f),
			new Vector2( 0.70710678f, -0.70710678f),
			new Vector2(-0.70710678f, -0.70710678f),
			new Vector2(-0.70710678f,  0.70710678f),
		};

		private bool[]    _costField;
		private ushort[]  _integrationField;
		private Vector2[] _flowField;

		private readonly Queue<int> _bfsQueue = new Queue<int>();

		public int Width  { get; private set; }
		public int Height { get; private set; }

		public void Initialize(int width, int height, bool[] walkable)
		{
			Width  = width;
			Height = height;

			int size = width * height;
			_costField        = new bool[size];
			_integrationField = new ushort[size];
			_flowField        = new Vector2[size];

			System.Array.Copy(walkable, _costField, size);
		}

		public void Bake(int targetX, int targetY)
		{
			BuildIntegrationField(targetX, targetY);
			BuildFlowField();
		}

		public Vector2 GetDirection(int x, int y)
		{
			if (IsInBounds(x, y) == false)
			{
				return Vector2.zero;
			}

			return _flowField[GetIndex(x, y)];
		}

		public bool IsInBounds(int x, int y)
		{
			return x >= 0 && x < Width && y >= 0 && y < Height;
		}

		public bool GetCostAt(int x, int y)
		{
			if (_costField == null || IsInBounds(x, y) == false)
			{
				return false;
			}

			return _costField[GetIndex(x, y)];
		}

		private void BuildIntegrationField(int targetX, int targetY)
		{
			int size = Width * Height;
			for (int i = 0; i < size; i++)
			{
				_integrationField[i] = ushort.MaxValue;
			}

			if (IsInBounds(targetX, targetY) == false)
			{
				return;
			}

			_bfsQueue.Clear();
			int targetIndex = GetIndex(targetX, targetY);
			_integrationField[targetIndex] = 0;
			_bfsQueue.Enqueue(targetIndex);

			while (_bfsQueue.Count > 0)
			{
				int current = _bfsQueue.Dequeue();
				int cx      = current % Width;
				int cy      = current / Width;
				ushort nextCost = (ushort)(_integrationField[current] + 1);

				for (int i = 0; i < _neighborOffsets.Length; i++)
				{
					int nx = cx + _neighborOffsets[i].x;
					int ny = cy + _neighborOffsets[i].y;

					if (IsInBounds(nx, ny) == false)
					{
						continue;
					}

					int neighborIndex = GetIndex(nx, ny);

					if (_costField[neighborIndex] == false)
					{
						continue;
					}

					if (_integrationField[neighborIndex] <= nextCost)
					{
						continue;
					}

					_integrationField[neighborIndex] = nextCost;
					_bfsQueue.Enqueue(neighborIndex);
				}
			}
		}

		private void BuildFlowField()
		{
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int index = GetIndex(x, y);

					if (_costField[index] == false)
					{
						_flowField[index] = Vector2.zero;
						continue;
					}

					int    bestDir  = -1;
					ushort bestCost = _integrationField[index];

					for (int i = 0; i < _neighborOffsets.Length; i++)
					{
						int nx = x + _neighborOffsets[i].x;
						int ny = y + _neighborOffsets[i].y;

						if (IsInBounds(nx, ny) == false)
						{
							continue;
						}

						int neighborIndex = GetIndex(nx, ny);
						if (_integrationField[neighborIndex] < bestCost)
						{
							bestCost = _integrationField[neighborIndex];
							bestDir  = i;
						}
					}

					if (bestDir < 0)
					{
						_flowField[index] = Vector2.zero;
						continue;
					}

					_flowField[index] = _neighborDirections[bestDir];
				}
			}
		}

		private int GetIndex(int x, int y)
		{
			return y * Width + x;
		}
	}
}

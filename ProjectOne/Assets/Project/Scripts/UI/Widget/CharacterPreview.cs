using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ProjectOne.Event;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 착용 외형이 반영된 내 캐릭터를 UI 에 띄우는 위젯.
	//
	// UI 캔버스가 전부 ScreenSpaceOverlay 라 월드 스프라이트를 UI 사이에 끼워 넣을 수 없고,
	// 히어로는 SpriteRenderer 30개짜리 파츠 캐릭터라 Image 한 장으로 대체할 수도 없다.
	// 그래서 시야 밖에 더미 히어로를 세우고 전용 카메라로 RenderTexture 에 찍어 RawImage 로 받는다.
	//
	// 수명이 곧 창 수명이다 — 창이 닫히면 이 오브젝트가 파괴되면서 카메라도 함께 사라져 렌더 비용이 0이 된다.
	public class CharacterPreview : MonoBehaviour
	{
		private const string RigAddress = "Prefab_HeroPreview";

		// RawImage 의 RectTransform 비율에 맞춘 크기. 어긋나면 캐릭터가 가로로 늘어난다.
		private const int TextureWidth = 512;
		private const int TextureHeight = 495;

		// 더미를 세울 월드 좌표. 메인 카메라 시야 밖이어야 인게임 화면에 찍히지 않는다.
		// 맵은 +X/+Y 로 뻗어 나가므로 1사분면 좌표는 필드에서 맵을 옮겨 다니다 보면 시야에 걸린다 — -Y 로 내린다.
		private static readonly Vector3 SlotOrigin = new Vector3(0f, -10000f, 0f);
		private const float SlotStride = 50f;

		// 프리뷰가 동시에 둘 이상 떠도 서로의 카메라에 찍히지 않도록 슬롯을 하나씩 밀어 쓴다.
		private static int _slotCounter;

		[SerializeField] private RawImage _target;

		private RenderTexture _texture;
		private CharacterPreviewRig _rig;

		// 리그를 실제로 얻었을 때만 Release 하기 위한 표시. 취소로 끊기면 짝이 안 맞는다.
		private bool _acquired;

		private void Start()
		{
			initAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
			EventManager.Instance.Unsubscribe<MasteryChangeEvent>(onMasteryChanged);

			if (_rig != null)
			{
				// 텍스처를 먼저 떼어낸다 — 카메라가 물고 있는 동안 해제하면 렌더 대상이 사라진다.
				if (_rig.Camera != null)
				{
					_rig.Camera.targetTexture = null;
				}

				Destroy(_rig.gameObject);
				_rig = null;
			}

			if (_texture != null)
			{
				_texture.Release();
				Destroy(_texture);
				_texture = null;
			}

			if (_acquired == true)
			{
				ResourceManager.Instance.Release(RigAddress);
				_acquired = false;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private async UniTaskVoid initAsync(CancellationToken ct)
		{
			(bool cancelled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(RigAddress, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			if (prefab == null)
			{
				Debug.LogError($"[CharacterPreview] 프리뷰 리그를 불러오지 못했습니다 — {RigAddress}");
				return;
			}

			_acquired = true;

			GameObject instance = Instantiate(prefab, nextSlot(), Quaternion.identity);
			_rig = instance.GetComponent<CharacterPreviewRig>();
			if (_rig == null)
			{
				Debug.LogError($"[CharacterPreview] {RigAddress} 에 CharacterPreviewRig 가 없습니다");
				Destroy(instance);
				return;
			}

			_texture = new RenderTexture(TextureWidth, TextureHeight, 16, RenderTextureFormat.ARGB32);
			_texture.name = "RT_CharacterPreview";

			_rig.Camera.targetTexture = _texture;

			_target.texture = _texture;
			_target.raycastTarget = false;
			_target.enabled = true;

			_rig.Refresh();

			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(onPresetChanged);
			EventManager.Instance.Subscribe<MasteryChangeEvent>(onMasteryChanged);
		}

		// 슬롯을 계속 밀기만 한다 — 반납하지 않아도 float 정밀도 안에서 한참 쓸 수 있고,
		// 되돌려 쓰면 파괴 직전 인스턴스와 좌표가 겹칠 위험만 생긴다.
		private static Vector3 nextSlot()
		{
			Vector3 position = SlotOrigin;
			position.x += SlotStride * _slotCounter;
			_slotCounter++;
			return position;
		}

		private void onEquipmentChanged(EquipmentChangeEvent e)
		{
			refresh();
		}

		private void onPresetChanged(PresetChangeEvent e)
		{
			refresh();
		}

		private void onMasteryChanged(MasteryChangeEvent e)
		{
			refresh();
		}

		private void refresh()
		{
			if (_rig == null)
			{
				return;
			}

			_rig.Refresh();
		}
	}
}

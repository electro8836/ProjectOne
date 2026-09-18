using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 버프 슬롯 1칸(UIPrefab_BuffSlot) 렌더 데이터.
	// 무엇을 보여줄지(디버프 제외·중첩 합산)는 MainHudPresenter 가 끝내고, 슬롯은 받은 값을 그리기만 한다.
	public struct BuffSlotData
	{
		public EDT.Buff buff;
		public string iconAddress;		// Table_Buff.Row.Icon — 비어 있는 버프가 있어 빈 문자열을 허용한다
		public int stack;				// 1 이면 표시하지 않는다
		public float remainingSeconds;	// isInfinite 가 true 면 의미 없다
		public bool isInfinite;
	}

	// MainHUD 의 BuffInfo 아래에 깔리는 버프 아이콘 1칸.
	//
	// 주기 폴링으로 0.1초마다 다시 Bind 되므로, 문자열은 값이 바뀔 때만 만든다 —
	// 매번 찍으면 가만히 서 있어도 GC 가 돈다.
	public class BuffSlot : MonoBehaviour
	{
		[SerializeField] private Image _icon;			// Image
		[SerializeField] private TMP_Text _topText;		// TopText — 중첩 수
		[SerializeField] private TMP_Text _bottomText;	// BottomText — 남은 시간

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 마지막으로 찍은 값. 같은 값이면 문자열을 다시 만들지 않는다.
		private int _lastStack = -1;
		private int _lastRemainSeconds = -1;

		private void OnDestroy()
		{
			releaseIcon();
		}

		public UniTask BindAsync(BuffSlotData data, CancellationToken ct)
		{
			applyStack(data.stack);
			applyRemain(data.remainingSeconds, data.isInfinite);

			return setIcon(data.iconAddress, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void applyStack(int stack)
		{
			if (_lastStack == stack)
			{
				return;
			}

			_lastStack = stack;
			_topText.text = (stack > 1) ? stack.ToString() : string.Empty;
		}

		// 무한 지속 버프는 남은 시간이 없다 — 칸을 비운다.
		private void applyRemain(float seconds, bool isInfinite)
		{
			if (isInfinite == true)
			{
				if (_lastRemainSeconds == -1)
				{
					return;
				}

				_lastRemainSeconds = -1;
				_bottomText.text = string.Empty;
				return;
			}

			int remain = Mathf.CeilToInt(seconds);
			if (remain < 0)
			{
				remain = 0;
			}

			if (_lastRemainSeconds == remain)
			{
				return;
			}

			_lastRemainSeconds = remain;
			_bottomText.text = formatRemain(remain);
		}

		// 가장 큰 단위 하나만 — 75 폭 슬롯 하단에 들어가야 한다.
		private static string formatRemain(int totalSeconds)
		{
			if (totalSeconds >= 86400)
			{
				return (totalSeconds / 86400) + "일";
			}

			if (totalSeconds >= 3600)
			{
				return (totalSeconds / 3600) + "시간";
			}

			if (totalSeconds >= 60)
			{
				return (totalSeconds / 60) + "분";
			}

			return totalSeconds + "초";
		}

		// PetInfoSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			// Icon 이 비어 있는 버프가 테이블에 있다 — 아이콘 없이 칸만 뜬다.
			if (string.IsNullOrEmpty(address) == true)
			{
				_icon.sprite = null;
				_icon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_icon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
				_icon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}

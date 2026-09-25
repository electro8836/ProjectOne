using System.Collections;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 히어로·마스터리 레벨업 알림. 레벨업 이벤트를 직접 구독한다.
	//
	// 새 레벨업이 오면 떠 있던 것을 지우고 바로 새로 띄운다.
	// 단 **같은 프레임**에 온 것은 뒤에 대기시킨다 — 경험치 한 번에 히어로와 마스터리가 함께 오르면
	// 히어로 메시지가 한 프레임 만에 덮여 보이지 않기 때문이다(Account.AddExp 가 히어로 → 마스터리 순으로 발행).
	//
	// **루트는 끄지 않고 CanvasGroup 알파로 숨긴다.** 루트를 끄면 코루틴이 멈추고 구독도 무의미해진다.
	public class LevelUpMessage : MonoBehaviour
	{
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _levelUpText;
		[SerializeField] private TMP_Text _levelText;
		[SerializeField] private TMP_Text _descText;

		[Header("연출")]
		[SerializeField] private float _popSeconds = 0.25f;
		[SerializeField] private float _popOvershoot = 1.15f;
		[SerializeField] private float _holdSeconds = 2f;
		[SerializeField] private float _fadeSeconds = 0.5f;

		private const float PopStartScale = 0.5f;

		private const string HeroDesc = "히어로 능력이 강화되었습니다!";
		private const string MasteryDesc = "숙련도 능력이 강화되었습니다!";

		// 표시 1회분 문구
		private struct Entry
		{
			public string levelUp;
			public int level;
			public string desc;
		}

		private Coroutine _playRoutine;

		// 현재 표시를 시작한 프레임 — 같은 프레임에 온 요청을 대기시키는 기준이다.
		private int _shownFrame = -1;

		// 같은 프레임에 온 뒤 순번. 한 칸이면 충분하다(히어로 + 마스터리 두 종류뿐).
		private bool _hasPending;
		private Entry _pending;

		private void Awake()
		{
			_canvasGroup.alpha = 0f;

			EventManager.Instance.Subscribe<HeroLevelUpEvent>(onHeroLevelUp);
			EventManager.Instance.Subscribe<MasteryLevelUpEvent>(onMasteryLevelUp);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<HeroLevelUpEvent>(onHeroLevelUp);
			EventManager.Instance.Unsubscribe<MasteryLevelUpEvent>(onMasteryLevelUp);
		}

		private void onHeroLevelUp(HeroLevelUpEvent evt)
		{
			Entry entry = new Entry();
			entry.levelUp = "히어로 레벨업!";
			entry.level = evt.Level;
			entry.desc = HeroDesc;
			request(entry);
		}

		private void onMasteryLevelUp(MasteryLevelUpEvent evt)
		{
			Table_WeaponMastery.Row row = Table_WeaponMastery.Get(evt.Mastery);
			if (row == null)
			{
				Debug.LogError($"[LevelUpMessage] WeaponMastery 테이블에 없는 마스터리입니다: {evt.Mastery}");
				return;
			}

			Entry entry = new Entry();
			entry.levelUp = row.Name + " 숙련도 레벨업!";
			entry.level = evt.Level;
			entry.desc = MasteryDesc;
			request(entry);
		}

		private void request(Entry entry)
		{
			if (_playRoutine != null && Time.frameCount == _shownFrame)
			{
				_pending = entry;
				_hasPending = true;
				return;
			}

			// 다른 프레임의 새 레벨업 — 떠 있던 것과 그 대기분을 버리고 바로 띄운다.
			_hasPending = false;
			if (_playRoutine != null)
			{
				StopCoroutine(_playRoutine);
			}

			_playRoutine = StartCoroutine(play(entry));
		}

		private IEnumerator play(Entry entry)
		{
			while (true)
			{
				apply(entry);
				_shownFrame = Time.frameCount;
				_canvasGroup.alpha = 1f;

				// 작게 시작해 한 번 넘쳤다가 제 크기로 — 튀어나오는 느낌
				float half = _popSeconds * 0.5f;
				float elapsed = 0f;
				while (elapsed < _popSeconds)
				{
					elapsed += Time.unscaledDeltaTime;

					float scale;
					if (elapsed < half)
					{
						scale = Mathf.Lerp(PopStartScale, _popOvershoot, elapsed / half);
					}
					else
					{
						scale = Mathf.Lerp(_popOvershoot, 1f, (elapsed - half) / half);
					}

					transform.localScale = new Vector3(scale, scale, 1f);
					yield return null;
				}

				transform.localScale = Vector3.one;

				yield return new WaitForSecondsRealtime(_holdSeconds);

				elapsed = 0f;
				while (elapsed < _fadeSeconds)
				{
					elapsed += Time.unscaledDeltaTime;
					_canvasGroup.alpha = Mathf.Clamp01(1f - elapsed / _fadeSeconds);
					yield return null;
				}

				_canvasGroup.alpha = 0f;

				if (_hasPending == false)
				{
					break;
				}

				entry = _pending;
				_hasPending = false;
			}

			_playRoutine = null;
		}

		private void apply(Entry entry)
		{
			_levelUpText.text = entry.levelUp;
			_levelText.text = "레벨 " + entry.level.ToString();
			_descText.text = entry.desc;
		}
	}
}

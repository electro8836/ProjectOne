using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 현재 위치 표시 — 아이콘 + 지역명.
	//
	// HeroInfo 와 같은 자율 HUD 위젯이다(MVP 를 쓰지 않는다).
	//   필드 : Icon_Field + "액트-순서 필드명"   (예: 1-3 이끼 낀 바위굴)
	//   던전 : 던전 아이콘 + "던전명 - 단계"      (예: 고블린의 보물창고 - 3)
	//
	// 마을에서 숨기는 것은 MainHud 의 ContextGroup 이 맡는다.
	public class MapInfo : MonoBehaviour
	{
		// 필드는 테이블에 아이콘 컬럼이 없어 공용 아이콘 하나를 쓴다.
		private const string FieldIconAddress = "Icon_Field";

		[SerializeField] private Image _iconImage;
		[SerializeField] private TMP_Text _mapText;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			EventManager.Instance.Subscribe<FieldChangedEvent>(onFieldChanged);
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(onDungeonStageStarted);

			refreshCurrent();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<FieldChangedEvent>(onFieldChanged);
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(onDungeonStageStarted);

			releaseIcon();
		}

		// 처음 켜질 때 현재 위치를 직접 읽는다.
		//
		// MainHud 가 부트 직후 맥락 None 으로 이 오브젝트를 꺼 두므로 Awake(=구독)는 필드·던전에서 처음 켜질 때 불린다.
		// 그 시점엔 디렉터가 진입 이벤트를 이미 발행한 뒤라 첫 이벤트를 놓친다.
		// 두 디렉터는 씬 수명이라 동시에 존재하지 않는다.
		private void refreshCurrent()
		{
			if (ProjectOne.Field.FieldDirector.HasInstance == true && ProjectOne.Field.FieldDirector.Instance.CurrentFieldId > 0)
			{
				showField(ProjectOne.Field.FieldDirector.Instance.CurrentFieldId);
				return;
			}

			if (ProjectOne.Dungeon.DungeonDirector.HasInstance == true && ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType != EDT.Dungeon.None)
			{
				showDungeon(ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType, ProjectOne.Dungeon.DungeonDirector.Instance.Stage);
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onFieldChanged(FieldChangedEvent e)
		{
			showField(e.FieldId);
		}

		private void onDungeonStageStarted(DungeonStageStartedEvent e)
		{
			showDungeon(e.DungeonType, e.Stage);
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void showField(int fieldId)
		{
			Table_Field.Row field = Table_Field.Get(fieldId);
			if (field == null)
			{
				Debug.LogError($"[MapInfo] Table_Field.Get({fieldId}) == null");
				return;
			}

			_mapText.text = field.ActID.ToString() + "-" + field.Order.ToString() + " " + field.Name;
			setIconAsync(FieldIconAddress, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void showDungeon(EDT.Dungeon dungeonType, int stage)
		{
			Table_Dungeon.Row dungeon = Table_Dungeon.Get(dungeonType);
			if (dungeon == null)
			{
				Debug.LogError($"[MapInfo] Table_Dungeon.Get({dungeonType}) == null");
				return;
			}

			_mapText.text = dungeon.Name + " - " + stage.ToString();
			setIconAsync(dungeon.Icon, this.GetCancellationTokenOnDestroy()).Forget();
		}

		// ── 아이콘 ────────────────────────────────────────────────────

		// CurrencyDisplaySlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIconAsync(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_iconImage.sprite = null;
				_iconImage.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_iconImage.sprite = atlasSprite;
				_iconImage.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_iconImage.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 지역으로 바뀌었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_iconImage.sprite = icon;
				_iconImage.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}

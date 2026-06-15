using System.IO;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Settings
{
	// 게임 옵션 관리 — JSON 파일(persistentDataPath/settings.json)로 로드/저장하는 순수 C# 싱글톤.
	// 옵션 변경은 Set* 로만 — 즉시 저장 + 변경 이벤트 발행. 설정 UI 는 이 API 만 호출하면 된다.
	public sealed class SettingsManager : Singleton<SettingsManager>
	{
		const string FileName = "settings.json";

		GameSettingsData _data;

		// 플레이어(내 캐릭터) 스킬 인디케이터 표시 여부 변경 알림
		public event System.Action<bool> PlayerSkillIndicatorChanged;

		// 조이스틱 유동성(floating) 사용 여부 변경 알림
		public event System.Action<bool> FloatingJoystickChanged;

		public SettingsManager()
		{
		}

		static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

		public bool ShowPlayerSkillIndicator
		{
			get
			{
				EnsureLoaded();
				return _data.showPlayerSkillIndicator;
			}
		}

		public void SetShowPlayerSkillIndicator(bool enabled)
		{
			EnsureLoaded();
			if (_data.showPlayerSkillIndicator == enabled)
			{
				return;
			}

			_data.showPlayerSkillIndicator = enabled;
			Save();
			if (PlayerSkillIndicatorChanged != null)
			{
				PlayerSkillIndicatorChanged(enabled);
			}
		}

		public bool UseFloatingJoystick
		{
			get
			{
				EnsureLoaded();
				return _data.useFloatingJoystick;
			}
		}

		public void SetUseFloatingJoystick(bool enabled)
		{
			EnsureLoaded();
			if (_data.useFloatingJoystick == enabled)
			{
				return;
			}

			_data.useFloatingJoystick = enabled;
			Save();
			if (FloatingJoystickChanged != null)
			{
				FloatingJoystickChanged(enabled);
			}
		}

		// 로드 전 접근 시 NRE 방지용 안전망 — 정상 경로는 BootState 의 명시적 Load
		void EnsureLoaded()
		{
			if (_data == null)
			{
				Load();
			}
		}

		// 파일이 있으면 읽어서 역직렬화, 없으면 기본값. BootState 등에서 명시적으로 1회 호출.
		public void Load()
		{
			string path = FilePath;
			if (File.Exists(path) == true)
			{
				string json = File.ReadAllText(path);
				_data = JsonUtility.FromJson<GameSettingsData>(json);
			}

			if (_data == null)
			{
				_data = new GameSettingsData();
			}
		}

		void Save()
		{
			string json = JsonUtility.ToJson(_data, true);
			File.WriteAllText(FilePath, json);
		}
	}
}

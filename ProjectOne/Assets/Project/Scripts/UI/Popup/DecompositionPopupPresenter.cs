using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shared;
using ProjectOne.Upgrade;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 일괄 분해 팝업 Presenter — 분해 조건(등급·품질·자동)을 고치고, 조건에 맞는 장비를 한 번에 분해한다.
	//
	// 조건은 팝업 안에서 사본을 고치다가 닫을 때 바뀌었으면 한 번만 저장한다.
	// 자동 분해는 저장된 조건으로 서버와 클라가 각자 판정하므로, 저장 전까지는 옛 조건이 그대로 쓰인다.
	public sealed class DecompositionPopupPresenter : Presenter<DecompositionPopup>
	{
		// 팝업에서 고치는 사본
		private DecomposeSettingDto _setting = new DecomposeSettingDto();

		private readonly List<long> _targets = new List<long>();

		protected override void OnInitialize()
		{
			view.OnGradeClicked += onGradeClicked;
			view.OnQualityCheckClicked += onQualityCheckClicked;
			view.OnQualityChanged += onQualityChanged;
			view.OnAutoCheckClicked += onAutoCheckClicked;
			view.OnDecompositionClicked += onDecompositionClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnGradeClicked -= onGradeClicked;
			view.OnQualityCheckClicked -= onQualityCheckClicked;
			view.OnQualityChanged -= onQualityChanged;
			view.OnAutoCheckClicked -= onAutoCheckClicked;
			view.OnDecompositionClicked -= onDecompositionClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 저장된 조건으로 채우고 닫힘까지 기다린 뒤, 바뀐 조건을 저장한다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			_setting = copy(Account.Instance.Inventory.DecomposeSetting);
			render();

			await view.WaitForCloseAsync(ct);

			saveIfChanged();
		}

		// ── 입력 ──────────────────────────────────────────────────────────

		private void onGradeClicked(ItemGradeType grade)
		{
			bool selected = EquipmentDecomposeRules.IsGradeSelected(_setting, grade) == false;
			EquipmentDecomposeRules.SetGradeSelected(_setting, grade, selected);
			view.SetGradeChecked(grade, selected);
		}

		private void onQualityCheckClicked()
		{
			_setting.useQuality = (_setting.useQuality == false);
			view.SetQualityChecked(_setting.useQuality);
		}

		private void onQualityChanged(int quality)
		{
			_setting.quality = quality;
			view.SetQuality(quality);
		}

		private void onAutoCheckClicked()
		{
			_setting.auto = (_setting.auto == false);
			view.SetAutoChecked(_setting.auto);
		}

		private void onDecompositionClicked()
		{
			decomposeAsync(view.GetDestroyToken()).Forget();
		}

		private void onExitClicked()
		{
			view.Close();
		}

		// 여러 장비가 한 번에 사라져 되돌릴 수 없으므로 개수를 보여주고 확인받는다.
		private async UniTaskVoid decomposeAsync(CancellationToken ct)
		{
			EquipmentDecompose.CollectTargets(_setting, _targets);

			CommonPopupData data;
			data.title = "분해";

			if (_targets.Count == 0)
			{
				data.desc = "분해할 장비가 없습니다.";
				data.button1Text = "확인";
				data.button2Text = null;
				await UIManager.Instance.ShowCommonPopupAsync(data, ct).SuppressCancellationThrow();
				return;
			}

			data.desc = "조건에 맞는 장비 " + _targets.Count.ToString() + "개를 분해합니다.\n계속하시겠습니까?";
			data.button1Text = "취소";
			data.button2Text = "분해";

			// 취소·닫기·Dim 은 확인 팝업만 닫는다.
			(bool cancelled, CommonPopupResult result) = await UIManager.Instance.ShowCommonPopupAsync(data, ct).SuppressCancellationThrow();
			if (cancelled == true || result != CommonPopupResult.Button2)
			{
				return;
			}

			// 확인 팝업이 떠 있는 사이 인벤토리가 바뀌었을 수 있어 다시 모은다.
			EquipmentDecompose.CollectTargets(_setting, _targets);
			if (_targets.Count == 0)
			{
				return;
			}

			// 결과(획득 재화)는 시스템 로그가 보여준다 — 응답을 기다리지 않고 이 팝업도 닫는다.
			EquipmentDecompose.RequestAll(_targets);
			view.Close();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void render()
		{
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				ItemGradeType grade = (ItemGradeType)g;
				view.SetGradeChecked(grade, EquipmentDecomposeRules.IsGradeSelected(_setting, grade));
			}

			view.SetQualityChecked(_setting.useQuality);
			view.SetQuality(_setting.quality);
			view.SetAutoChecked(_setting.auto);
		}

		private void saveIfChanged()
		{
			DecomposeSettingDto saved = Account.Instance.Inventory.DecomposeSetting;
			if (saved.gradeMask == _setting.gradeMask && saved.useQuality == _setting.useQuality
				&& saved.quality == _setting.quality && saved.auto == _setting.auto)
			{
				return;
			}

			EquipmentDecompose.SaveSetting(copy(_setting));
		}

		private static DecomposeSettingDto copy(DecomposeSettingDto source)
		{
			DecomposeSettingDto result = new DecomposeSettingDto();
			result.gradeMask = source.gradeMask;
			result.useQuality = source.useQuality;
			result.quality = source.quality;
			result.auto = source.auto;
			return result;
		}
	}
}

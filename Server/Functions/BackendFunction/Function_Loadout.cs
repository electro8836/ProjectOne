using System;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Loadout
	{
		// SaveLoadout — 선택 캐릭터의 장착 프리셋 저장. 보유 검증 후 USER_CHARACTER 의 해당 캐릭터 preset 만 갱신(서버 권위).
		// 클라는 클릭마다 보내지 않고 화면 닫기·앱 종료 시 1회만 호출한다(패킷 절약).
		public Stream SaveLoadout()
		{
			try
			{
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				SaveLoadoutRequest req = JsonConvert.DeserializeObject<SaveLoadoutRequest>(reqJson);
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 1. 인벤토리 로드 → 장착하려는 아이템 보유 검증(0 = 미장착이라 검증 제외 — 클라 값 불신).
				if (loadInventory(out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (isOwned(inventory, req.weaponItemId) == false
					|| isOwned(inventory, req.armorItemId) == false
					|| isOwned(inventory, req.accessoryItemId) == false)
				{
					return FuncResult.Error("not owned item in request");
				}

				// 2. 캐릭터 로드 → 대상 캐릭터의 preset 만 갱신(나머지 캐릭터/필드 보존).
				if (loadCharacter(out CharacterDto character, out string charErr) == false)
				{
					return FuncResult.Error(charErr);
				}

				OwnedCharacterDto target = findCharacter(character, req.characterId);
				if (target == null)
				{
					return FuncResult.Error("character not owned: " + req.characterId);
				}

				target.preset.weaponItemId = req.weaponItemId;
				target.preset.armorItemId = req.armorItemId;
				target.preset.accessoryItemId = req.accessoryItemId;

				// 3. USER_CHARACTER Data 덮어쓰기. 펑션 컨텍스트는 owner 인자가 비어 UndefinedParameterException 나므로 new Where() 사용(Dungeon 과 동일).
				Param param = new Param();
				param.Add("Data", JsonConvert.SerializeObject(character));
				var updateResult = Backend.GameData.Update("USER_CHARACTER", new Where(), param);
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("Failed to update USER_CHARACTER: " + updateResult.GetErrorCode());
				}

				SaveLoadoutResponse response = new SaveLoadoutResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 0 은 미장착(검증 통과). 그 외엔 보유(count>=1) 여야 한다.
		private static bool isOwned(InventoryDto inventory, int itemId)
		{
			if (itemId == 0)
			{
				return true;
			}

			for (int i = 0; i < inventory.items.Count; i++)
			{
				OwnedItemDto item = inventory.items[i];
				if (item != null && item.itemId == itemId && item.count >= 1)
				{
					return true;
				}
			}

			return false;
		}

		private static bool loadInventory(out InventoryDto inventory, out string err)
		{
			inventory = null;
			var getResult = Backend.GameData.GetMyData("USER_INVENTORY", new Where());
			if (!getResult.IsSuccess())
			{
				err = "USER_INVENTORY Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				err = "USER_INVENTORY row not found";
				return false;
			}

			inventory = JsonConvert.DeserializeObject<InventoryDto>(rows[0]["Data"].ToString());
			if (inventory == null)
			{
				inventory = new InventoryDto();
			}

			err = null;
			return true;
		}

		private static bool loadCharacter(out CharacterDto character, out string err)
		{
			character = null;
			var getResult = Backend.GameData.GetMyData("USER_CHARACTER", new Where());
			if (!getResult.IsSuccess())
			{
				err = "USER_CHARACTER Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				err = "USER_CHARACTER row not found";
				return false;
			}

			character = JsonConvert.DeserializeObject<CharacterDto>(rows[0]["Data"].ToString());
			if (character == null)
			{
				err = "USER_CHARACTER parse failed";
				return false;
			}

			err = null;
			return true;
		}

		private static OwnedCharacterDto findCharacter(CharacterDto character, int characterId)
		{
			for (int i = 0; i < character.characters.Count; i++)
			{
				OwnedCharacterDto oc = character.characters[i];
				if (oc != null && oc.characterId == characterId)
				{
					return oc;
				}
			}

			return null;
		}
	}
}

using System;
using System.IO;
using Amazon.Lambda.Core;
using BackEnd;
using ProjectOne.Shared;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BackendFunction
{
	// 단일 진입점 — 뒤끝 펑션에 등록된 Lambda 핸들러(BackendFunction.BFunc::Function)와 클래스/메서드 이름이 일치해야 한다.
	// (이름이 다르면 'Unable to load type BackendFunction.BFunc' 로 런타임 로드 실패.)
	// 어셈블리 내에서 (public + 리턴 Stream + 정확히 (Stream, ILambdaContext)) 조건을 만족하는 메서드는
	// 이 하나뿐이어야 한다(다른 핸들러는 인자 없는 내부 메서드로 둔다).
	//
	// 동작: Initialize 후 클라가 보낸 action 키로 실제 처리 핸들러로 분기한다(서버 권위).
	public class BFunc
	{
		public Stream Function(Stream stream, ILambdaContext context)
		{
			try
			{
				Backend.Initialize(ref stream);
			}
			catch (Exception e)
			{
				return FuncResult.Error("Initialize Failed: " + e.ToString());
			}

			// 분기 키(action) 확인 — 클라 BackndFunctionCaller 가 body 의 "action" 에 담아 보낸다.
			if (Backend.HasKey("action") == false)
			{
				return FuncResult.Error("action key is not exist");
			}

			string action = Backend.Content["action"].ToString();
			switch (action)
			{
				case FunctionName.GetUserData:
					return new Auth().GetUserData();
				case FunctionName.DungeonClear:
					return new Dungeon().DungeonClear();
				case FunctionName.EquipmentGachaDraw:
					return new Gacha().EquipmentGachaDraw();
				case FunctionName.SkillGachaDraw:
					return new Gacha().SkillGachaDraw();
				default:
					return FuncResult.Error("unknown action: " + action);
			}
		}
	}
}

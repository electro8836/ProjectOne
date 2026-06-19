using System;
using System.IO;
using Amazon.Lambda.Core;
using BackEnd;
using LitJson;
using Newtonsoft.Json.Linq;

namespace BackendFunction
{
	public class Common
	{
		public static Stream ReturnErrorObject(string err)
		{
			JObject error = new JObject();
			error.Add("status", "fail");
			error.Add("error", err);
			return Backend.JsonToStream(error.ToString());
		}
	}
}
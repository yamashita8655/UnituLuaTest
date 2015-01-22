using UnityEngine;
using System.Collections;
using System.IO;
using System.Text;
using System;
using LuaInterface;

public class luatraning : MonoBehaviour {

	// Use this for initialization
	void Start () {
		//test();
		test2();
	}

	void test()
	{
		// 当然スタックには何も溜まっていないのでcountは0が表示される
		IntPtr L = LuaLib.LuaLNewState();
		printStack (L);
		LuaLib.LuaClose(L);
	}

	void test2()
	{
		IntPtr L = LuaLib.LuaLNewState();
		LuaLib.LuaLOpenLibs(L);// Luaの標準ライブラリをLua側で使えるようにする記述

		LuaLib.LuaPushString(L, "LUAINTERFACE LOADED");
		LuaLib.LuaPushBoolean(L, true);
		LuaLib.LuaSetTable(L, (int) LuaIndexes.LUA_REGISTRYINDEX);// これでテーブルに上記2つがテーブルに登録されるので、スタックから消える

		LuaLib.LuaNewTable(L);
		LuaLib.LuaSetGlobal(L, "luanet");// グローバルを表すテーブルに格納するけど、内部でやってる事がイマイチよくわからん
		// 多分、luanetというキーにNewTableで作ったテーブルを格納したのかと思われる

		LuaLib.LuaGetGlobal(L, "luanet");
		LuaLib.LuaPushString(L, "getmetatable");
		LuaLib.LuaGetGlobal(L, "getmetatable");
		LuaLib.LuaSetTable(L, -3);
		// そのテーブルに、getmetatableというキーでgetmetatableという機能をつかえるんだろうと思われるFunctionを登録している

		LuaLib.LuaReplace(L, (int)LuaIndexes.LUA_GLOBALSINDEX);
		// 今スタックに積まれている色々いじったテーブルを、グローバルの場所に上書きしている



		printStack (L);
		LuaLib.LuaClose(L);
	}


	void printStack(System.IntPtr L)
	{
		int num = LuaLib.LuaGetTop (L);
		Debug.Log ("count : " + num);
		if(num==0)
		{
			return;
		}
		
		for(int i = num; i >= 1; i--)
		{
			LuaTypes type = LuaLib.LuaType(L, i);

			switch(type) {
			case LuaTypes.LUA_TNIL:
				break;
			case LuaTypes.LUA_TBOOLEAN:
				bool res_b = LuaLib.LuaToBoolean(L, i);
				Debug.Log ("LUA_TBOOLEAN : " + res_b);
				break;
			case LuaTypes.LUA_TLIGHTUSERDATA:
				break;
			case LuaTypes.LUA_TNUMBER:
				double res_d = LuaLib.LuaToNumber(L, i);
				Debug.Log ("LUA_TNUMBER : " + res_d);
				break;
			case LuaTypes.LUA_TSTRING:
				string res_s = LuaLib.LuaToString(L, i);
				Debug.Log ("LUA_TSTRING : " + res_s);
				break;
			case LuaTypes.LUA_TTABLE:
				Debug.Log ("LUA_TTABLE : ");
				break;
			case LuaTypes.LUA_TFUNCTION:
				Debug.Log ("LUA_TFUNCTION : ");
				break;
			case LuaTypes.LUA_TUSERDATA:
				Debug.Log ("LUA_TUSERDATA : ");
				break;
			case LuaTypes.LUA_TTHREAD:
				Debug.Log ("LUA_TTHREAD : ");
				break;
			}
		}
	}
}

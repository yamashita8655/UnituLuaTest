namespace LuaInterface
{
	using System;
	using System.IO;
	using System.Collections;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.Reflection;
	using System.Threading;
	using UnityEngine;
	
	public class LuaStatic
	{
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int panic(IntPtr L)
		{
			string reason = String.Format("unprotected error in call to Lua API ({0})", LuaLib.LuaToString(L, -1));
			throw new LuaException(reason);
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int traceback(IntPtr L)
		{
			LuaLib.LuaGetGlobal(L,"debug");
			LuaLib.LuaGetField(L,-1,"traceback");
			LuaLib.LuaPushValue(L,1);
			LuaLib.LuaPushNumber(L,2);
			LuaLib.LuaCall (L,2,1);
			return 1;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int print(IntPtr L)
		{
			// For each argument we'll 'tostring' it
			int n = LuaLib.LuaGetTop(L);
			string s = String.Empty;
			
			LuaLib.LuaGetGlobal(L, "tostring");
			
			for( int i = 1; i <= n; i++ ) 
			{
				LuaLib.LuaPushValue(L, -1);  /* function to be called */
				LuaLib.LuaPushValue(L, i);   /* value to print */
				LuaLib.LuaCall(L, 1, 1);
				s += LuaLib.LuaToString(L, -1);
				
				if( i > 1 ) 
				{
					s += "\t";
				}
				
				LuaLib.LuaPop(L, 1);  /* pop result */
			}
			Debug.Log("LUA: " + s);
			return 0;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int loader(IntPtr L)
		{
			// Get script to load
			string fileName = String.Empty;
			fileName = LuaLib.LuaToString(L, 1);
			fileName = fileName.Replace('.', '/');
			fileName += ".lua";
			
			// Load with Unity3D resources
			TextAsset file = (TextAsset)Resources.Load(fileName);
			if( file == null )
			{
				return 0;
			}
			
			LuaLib.LuaL_LoadBuffer(L, file.text, file.bytes.Length, fileName);
			
			return 1;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int dofile(IntPtr L)
		{
			// Get script to load
			string fileName = String.Empty;
			fileName = LuaLib.LuaToString(L, 1);
			fileName.Replace('.', '/');
			fileName += ".lua";
			
			int n = LuaLib.LuaGetTop(L);
			
			// Load with Unity3D resources
			TextAsset file = (TextAsset)Resources.Load(fileName);
			if( file == null )
			{
				return LuaLib.LuaGetTop(L) - n;
			}
			
			if( LuaLib.LuaL_LoadBuffer(L, file.text, file.bytes.Length, fileName) == 0 )
			{
				LuaLib.LuaCall(L, 0, LuaLib.LUA_MULTRET);
			}
			
			return LuaLib.LuaGetTop(L) - n;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int loadfile(IntPtr L)
		{
			return loader(L);
		}
		
		public static string init_luanet =
			@"local metatable = {}
            local rawget = rawget
            local import_type = luanet.import_type
            local load_assembly = luanet.load_assembly
            luanet.error, luanet.type = error, type
            -- Lookup a .NET identifier component.
            function metatable:__index(key) -- key is e.g. 'Form'
            -- Get the fully-qualified name, e.g. 'System.Windows.Forms.Form'
            local fqn = rawget(self,'.fqn')
            fqn = ((fqn and fqn .. '.') or '') .. key

            -- Try to find either a luanet function or a CLR type
            local obj = rawget(luanet,key) or import_type(fqn)

            -- If key is neither a luanet function or a CLR type, then it is simply
            -- an identifier component.
            if obj == nil then
                -- It might be an assembly, so we load it too.
                    pcall(load_assembly,fqn)
                    obj = { ['.fqn'] = fqn }
            setmetatable(obj, metatable)
            end

            -- Cache this lookup
            rawset(self, key, obj)
            return obj
            end

            -- A non-type has been called; e.g. foo = System.Foo()
            function metatable:__call(...)
            error('No such type: ' .. rawget(self,'.fqn'), 2)
            end

            -- This is the root of the .NET namespace
            luanet['.fqn'] = false
            setmetatable(luanet, metatable)

            -- Preload the mscorlib assembly
            luanet.load_assembly('mscorlib')
            luanet.load_assembly('UnityEngine')
            luanet.load_assembly('Assembly-CSharp')
		";
	}
}
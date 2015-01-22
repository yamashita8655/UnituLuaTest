namespace LuaInterface
{

	using System;
	using System.Runtime.InteropServices;
	using System.Reflection;
	using System.Collections;
	using System.Text;
    using System.Security;

	#pragma warning disable 414
	public class MonoPInvokeCallbackAttribute : System.Attribute
	{
		private Type type;
		internal MonoPInvokeCallbackAttribute( Type t ) { type = t; }
	}
	#pragma warning restore 414

    public enum LuaTypes
	{
		LUA_TNONE=-1,
		LUA_TNIL=0,
		LUA_TBOOLEAN=1,
		LUA_TLIGHTUSERDATA=2,
		LUA_TNUMBER=3,
		LUA_TSTRING=4,
		LUA_TTABLE=5,
		LUA_TFUNCTION=6,
		LUA_TUSERDATA=7,
		LUA_TTHREAD=8,
	}

    public enum LuaGCOptions
	{
		LUA_GCSTOP = 0,
		LUA_GCRESTART = 1,
		LUA_GCCOLLECT = 2,
		LUA_GCCOUNT = 3,
		LUA_GCCOUNTB = 4,
		LUA_GCSTEP = 5,
		LUA_GCSETPAUSE = 6,
		LUA_GCSETSTEPMUL = 7,
	}

    public enum LuaThreadStatus
    {
        LUA_YIELD       = 1,
        LUA_ERRRUN      = 2,
        LUA_ERRSYNTAX   = 3,
        LUA_ERRMEM      = 4,
        LUA_ERRERR      = 5,
    }

	sealed class LuaIndexes
	{
		internal static int LUA_REGISTRYINDEX=-10000;
		internal static int LUA_ENVIRONINDEX=-10001;
		internal static int LUA_GLOBALSINDEX=-10002;
	}

	[StructLayout(LayoutKind.Sequential)]
    public struct ReaderInfo
	{
		internal string chunkData;
		internal bool finished;
	}

    public delegate int LuaCSFunction(IntPtr luaState);
    public delegate string LuaChunkReader(IntPtr luaState, ref ReaderInfo data, ref uint size);

    public delegate int LuaFunctionCallback(IntPtr luaState);
	sealed class LuaLib
	{
        internal static int LUA_MULTRET = -1;
#if UNITY_IPHONE
		internal const string LIBNAME = "__Internal";
#else
		internal const string LIBNAME = "lua";
#endif

        internal static int LuaLGetN(IntPtr luaState, int i)
        {
            return (int)LuaLib.LuaObjLen(luaState, i);
        }

        internal static int LuaLDoString(IntPtr luaState, string chunk)
        {
            int result = LuaLib.LuaLLoadString(luaState, chunk);
            if (result != 0)
                return result;

            return LuaLib.LuaPCall(luaState, 0, -1, 0);
        }       
  
        internal static void LuaNewTable(IntPtr luaState)
        {
            LuaLib.LuaCreateTable(luaState, 0, 0);
        }

        internal static void LuaGetGlobal(IntPtr luaState, string name)
        {
            LuaLib.LuaPushString(luaState, name);
            LuaLib.LuaGetTable(luaState, LuaIndexes.LUA_GLOBALSINDEX);
        }

        internal static void LuaSetGlobal(IntPtr luaState, string name)
        {
            LuaLib.LuaPushString(luaState, name);
            LuaLib.LuaInsert(luaState, -2);
            LuaLib.LuaSetTable(luaState, LuaIndexes.LUA_GLOBALSINDEX);
        }

        internal static void LuaPop(IntPtr luaState, int amount)
        {
            LuaLib.LuaSetTop(luaState, -(amount) - 1);
        }

        internal static void LuaLGetMetaTable(IntPtr luaState, string meta)
        {
            LuaLib.LuaGetField(luaState, LuaIndexes.LUA_REGISTRYINDEX, meta);
        }

        internal static string LuaToString(IntPtr luaState, int index)
        {
            int strlen;
            IntPtr str = LuaToLString(luaState, index, out strlen);
            if (str != IntPtr.Zero)
            {
				var ret = Marshal.PtrToStringAnsi(str, strlen);
				if (ret == null)
				{
					var data = new byte[strlen];
					Marshal.Copy(str, data, 0, strlen);
					ret = Encoding.UTF8.GetString(data);
				}
				return ret;
            }
            else
            {
                return null;
            }
        }

        internal static void LuaPushStdCallCFunction(IntPtr luaState, LuaCSFunction function)
        {
            IntPtr fn = Marshal.GetFunctionPointerForDelegate(function);
            LuaPushStdCallCFunction(luaState, fn);
        }

        internal static bool LuaIsNil(IntPtr luaState, int index)
        {
            return (LuaLib.LuaType(luaState, index) == LuaTypes.LUA_TNIL);
        }

        internal static bool LuaIsBoolean(IntPtr luaState, int index)
        {
            return LuaLib.LuaType(luaState, index) == LuaTypes.LUA_TBOOLEAN;
        }

        internal static void lua_getref(IntPtr luaState, int reference)
        {
            LuaLib.lua_rawgeti(luaState, LuaIndexes.LUA_REGISTRYINDEX, reference);
        }

        internal static void lua_unref(IntPtr luaState, int reference)
        {
            LuaLib.luaL_unref(luaState, LuaIndexes.LUA_REGISTRYINDEX, reference);
        }

        internal static IntPtr LuaNewThread(IntPtr luaState)
        {
            return lua_newthread(luaState);
        }

        internal static int LuaResume(IntPtr luaState, int arg)
        {
            return lua_resume(luaState, arg);
        }

        internal static int LuaStatus(IntPtr luaState)
		{
			return lua_status(luaState);
		}

		internal static string LuaTypeName(IntPtr luaState, LuaTypes type)
        {
            return lua_typename(luaState, type);
        }

		internal static int LuaSetFEnv(IntPtr luaState, int stackPos)
        {
            return lua_setfenv(luaState, stackPos);
        }

		internal static void LuaSetField(IntPtr luaState, int stackPos, string name)
        {
            lua_setfield(luaState, stackPos, name);
        }

		internal static IntPtr LuaLNewState()
        {
            return luaL_newstate();
        }

		internal static void LuaClose(IntPtr luaState)
        {
            lua_close(luaState);
        }

		internal static void LuaLOpenLibs(IntPtr luaState)
        {
            luaL_openlibs(luaState);
        }

		internal static int LuaObjLen(IntPtr luaState, int stackPos)
        {
            return lua_objlen(luaState, stackPos);
        }

		internal static int LuaLLoadString(IntPtr luaState, string chunk)
        {
            return luaL_loadstring(luaState, chunk);
        }

		internal static void LuaCreateTable(IntPtr luaState, int narr, int nrec)
        {
            lua_createtable(luaState, narr, nrec);
        }

		internal static void LuaSetTop(IntPtr luaState, int newTop)
        {
            lua_settop(luaState, newTop);
        }

		internal static void LuaInsert(IntPtr luaState, int newTop)
        {
            lua_insert(luaState, newTop);
        }

        internal static void LuaRemove(IntPtr luaState, int index)
        {
            lua_remove(luaState, index);
        }

		internal static void LuaGetTable(IntPtr luaState, int index)
        {
            lua_gettable(luaState, index);
        }

		internal static void Lua_RawGet(IntPtr luaState, int index)
        {
            lua_rawget(luaState, index);
        }

		internal static void LuaSetTable(IntPtr luaState, int index)
        {
            lua_settable(luaState, index);
        }

		internal static void LuaRawSet(IntPtr luaState, int index)
        {
            lua_rawset(luaState, index);
        }

		internal static void LuaSetMetaTable(IntPtr luaState, int objIndex)
        {
            lua_setmetatable(luaState, objIndex);
        }

        internal static int LuaGetMetaTable(IntPtr luaState, int objIndex)
        {
            return lua_getmetatable(luaState, objIndex);
        }

		internal static int LuaEqual(IntPtr luaState, int index1, int index2)
        {
            return lua_equal(luaState, index1, index2);
        }

		internal static void LuaPushValue(IntPtr luaState, int index)
        {
            lua_pushvalue(luaState, index);
        }

		internal static void LuaReplace(IntPtr luaState, int index)
        {
            lua_replace(luaState, index);
        }

        internal static int LuaGetTop(IntPtr luaState)
        {
            return lua_gettop(luaState);
        }

		internal static LuaTypes LuaType(IntPtr luaState, int index)
	    {
            return lua_type(luaState, index);
        }

		internal static bool LuaIsNumber(IntPtr luaState, int index)
        {
            return lua_isnumber(luaState, index);
        }

		internal static int LuaLRef(IntPtr luaState, int registryIndex)
        {
            return luaL_ref(luaState, registryIndex);
        }

		internal static void LuaRawGetI(IntPtr luaState, int tableIndex, int index)
        {
            lua_rawgeti(luaState, tableIndex, index);
        }

		internal static void LuaRawSetI(IntPtr luaState, int tableIndex, int index)
        {
            lua_rawseti(luaState, tableIndex, index);
        }

		internal static IntPtr LuaToUserdata(IntPtr luaState, int index)
        {
            return lua_touserdata(luaState, index);
        }

		internal static void LuaLUnRef(IntPtr luaState, int registryIndex, int reference)
        {
            luaL_unref(luaState, registryIndex, reference);
        }

		internal static bool LuaIsString(IntPtr luaState, int index)
        {
            return lua_isstring(luaState, index);
        }

		internal static void LuaPushNil(IntPtr luaState)
        {
            lua_pushnil(luaState);
        }

		internal static void LuaPushStdCallCFunction(IntPtr luaState, IntPtr wrapper)
        {
            lua_pushstdcallcfunction(luaState, wrapper);
        }

		internal static int LuaCall(IntPtr luaState, int nArgs, int nResults)
        {
            return lua_call(luaState, nArgs, nResults);
        }

		internal static int LuaPCall(IntPtr luaState, int nArgs, int nResults, int errfunc)
        {
            return lua_pcall(luaState, nArgs, nResults, errfunc);
        }

		internal static double LuaToNumber(IntPtr luaState, int index)
        {
            return lua_tonumber(luaState, index);
        }

		internal static bool LuaToBoolean(IntPtr luaState, int index)
        {
            return lua_toboolean(luaState, index);
        }

		internal static IntPtr LuaToLString(IntPtr luaState, int index, out int strLen)
        {
            return lua_tolstring(luaState, index, out strLen);
        }

		internal static void LuaAtPanic(IntPtr luaState, LuaCSFunction panicf)
        {
            lua_atpanic(luaState, panicf);
        }

		internal static void LuaPushNumber(IntPtr luaState, double number)
        {
            lua_pushnumber(luaState, number);
        }

		internal static void LuaPushBoolean(IntPtr luaState, bool value)
        {
            lua_pushboolean(luaState, value);
        }

		internal static void LuaPushString(IntPtr luaState, string str)
        {
            lua_pushstring(luaState, str);
        }

		internal static int LuaLNewMetaTable(IntPtr luaState, string meta)
        {
            return luaL_newmetatable(luaState, meta);
        }

		internal static void LuaGetField(IntPtr luaState, int stackPos, string meta)
        {
            lua_getfield(luaState, stackPos, meta);
        }

		internal static bool LuaLGetMetaField(IntPtr luaState, int stackPos, string field)
        {
            return luaL_getmetafield(luaState, stackPos, field);
        }

        internal static int LuaL_LoadBuffer(IntPtr luaState, string buff, int size, string name)
        {
            return luaL_loadbuffer(luaState, buff, size, name);
        }

		internal static bool LuaLCheckMetaTable(IntPtr luaState,int obj)
        {
            return luaL_checkmetatable(luaState, obj);
        }

		internal static void LuaError(IntPtr luaState)
        {
            lua_error(luaState);
        }

		internal static bool LuaCheckStack(IntPtr luaState, int extra)   
        {
            return lua_checkstack(luaState, extra);
        }

		internal static int LuaNext(IntPtr luaState,int index)
        {
            return lua_next(luaState, index);
        }

		internal static void LuaPushLightUserdata(IntPtr luaState, IntPtr udata)
        {
            lua_pushlightuserdata(luaState, udata);
        }

        internal static void LuaLWhere(IntPtr luaState, int level)
        {
            luaL_where(luaState, level);
        }

        internal static IntPtr LuaNetGetTag()
        {
            return luanet_gettag();
        }

		internal static int LuaNetToNetObject(IntPtr luaState,int obj)
        {
            return luanet_tonetobject(luaState, obj);
        }

		internal static int LuaNetNewUdata(IntPtr luaState,int val)
        {
            return luanet_newudata(luaState, val);
        }

		internal static int LuaNetRawNetObj(IntPtr luaState,int obj)
        {
            return luanet_rawnetobj(luaState, obj);
        }

		internal static int LuaNetCheckUdata(IntPtr luaState,int obj,string meta)
        {
            return luanet_checkudata(luaState, obj, meta);
        }

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_newthread")]
        static extern IntPtr lua_newthread(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_resume")]
        static extern int lua_resume(IntPtr luaState, int arg);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_status")]
        static extern int lua_status(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "lua_typename")]
        static extern string lua_typename(IntPtr luaState, LuaTypes type);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_setfenv")]
        static extern int lua_setfenv(IntPtr luaState, int stackPos);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "lua_setfield")]
        static extern void lua_setfield(IntPtr luaState, int stackPos, string name);

		[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_newstate")]
        static extern IntPtr luaL_newstate();

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_close")]
        static extern void lua_close(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_openlibs")]
        static extern void luaL_openlibs(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_objlen")]
        static extern int lua_objlen(IntPtr luaState, int stackPos);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luaL_loadstring")]
        static extern int luaL_loadstring(IntPtr luaState, string chunk);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_createtable")]
        static extern void lua_createtable(IntPtr luaState, int narr, int nrec);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_settop")]
        static extern void lua_settop(IntPtr luaState, int newTop);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_insert")]
        static extern void lua_insert(IntPtr luaState, int newTop);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_remove")]
        static extern void lua_remove(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_gettable")]
        static extern void lua_gettable(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_rawget")]
        static extern void lua_rawget(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_settable")]
        static extern void lua_settable(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_rawset")]
        static extern void lua_rawset(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_setmetatable")]
        static extern void lua_setmetatable(IntPtr luaState, int objIndex);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_getmetatable")]
        static extern int lua_getmetatable(IntPtr luaState, int objIndex);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_equal")]
        static extern int lua_equal(IntPtr luaState, int index1, int index2);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushvalue")]
        static extern void lua_pushvalue(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_replace")]
        static extern void lua_replace(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_gettop")]
        static extern int lua_gettop(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_type")]
        static extern LuaTypes lua_type(IntPtr luaState, int index);
	
        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_isnumber")]
        static extern bool lua_isnumber(IntPtr luaState, int index);
	
        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_ref")]
        static extern int luaL_ref(IntPtr luaState, int registryIndex);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_rawgeti")]
        static extern void lua_rawgeti(IntPtr luaState, int tableIndex, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_rawseti")]
        static extern void lua_rawseti(IntPtr luaState, int tableIndex, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_touserdata")]
        static extern IntPtr lua_touserdata(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_unref")]
        static extern void luaL_unref(IntPtr luaState, int registryIndex, int reference);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_isstring")]
        static extern bool lua_isstring(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushnil")]
        static extern void lua_pushnil(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushstdcallcfunction")]
        static extern void lua_pushstdcallcfunction(IntPtr luaState, IntPtr wrapper);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_call")]
        static extern int lua_call(IntPtr luaState, int nArgs, int nResults);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pcall")]
        static extern int lua_pcall(IntPtr luaState, int nArgs, int nResults, int errfunc);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_tonumber")]
        static extern double lua_tonumber(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_toboolean")]
        static extern bool lua_toboolean(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_tolstring")]
        static extern IntPtr lua_tolstring(IntPtr luaState, int index, out int strLen);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_atpanic")]
        static extern void lua_atpanic(IntPtr luaState, LuaCSFunction panicf);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushnumber")]
        static extern void lua_pushnumber(IntPtr luaState, double number);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushboolean")]
        static extern void lua_pushboolean(IntPtr luaState, bool value);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "lua_pushstring")]
        static extern void lua_pushstring(IntPtr luaState, string str);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luaL_newmetatable")]
        static extern int luaL_newmetatable(IntPtr luaState, string meta);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "lua_getfield")]
        static extern void lua_getfield(IntPtr luaState, int stackPos, string meta);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luaL_getmetafield")]
        static extern bool luaL_getmetafield(IntPtr luaState, int stackPos, string field);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luaL_loadbuffer")]
        static extern int luaL_loadbuffer(IntPtr luaState, string buff, int size, string name);

        //[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luaL_loadbuffer")]
		//internal static extern int LuaL_LoadBuffer(IntPtr luaState, byte[] buff, int size, string name);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_checkmetatable")]
        static extern bool luaL_checkmetatable(IntPtr luaState, int obj);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_error")]
        static extern void lua_error(IntPtr luaState);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_checkstack")]
        static extern bool lua_checkstack(IntPtr luaState, int extra);             

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_next")]
        static extern int lua_next(IntPtr luaState, int index);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lua_pushlightuserdata")]
        static extern void lua_pushlightuserdata(IntPtr luaState, IntPtr udata);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luaL_where")]
        static extern void luaL_where(IntPtr luaState, int level);

        // luanet
        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luanet_gettag")]
        static extern IntPtr luanet_gettag();

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luanet_tonetobject")]
        static extern int luanet_tonetobject(IntPtr luaState, int obj);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luanet_newudata")]
        static extern int luanet_newudata(IntPtr luaState, int val);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "luanet_rawnetobj")]
        static extern int luanet_rawnetobj(IntPtr luaState, int obj);

        [DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "luanet_checkudata")]
        static extern int luanet_checkudata(IntPtr luaState, int obj, string meta);
	}
}

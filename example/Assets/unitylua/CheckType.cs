using System;
using System.Collections.Generic;
using System.Reflection;

namespace LuaInterface
{
    /*
     * Type checking and conversion functions.
     *
     * Author: Fabio Mascarenhas
     * Version: 1.0
     */
    sealed class CheckType
    {
        private ObjectTranslator translator;

        ExtractValue extractNetObject;
        Dictionary<long, ExtractValue> extractValues = new Dictionary<long, ExtractValue>();

        public CheckType(ObjectTranslator translator)
        {
            this.translator = translator;

            extractValues.Add(typeof(object).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsObject));
            extractValues.Add(typeof(sbyte).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsSbyte));
            extractValues.Add(typeof(byte).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsByte));
            extractValues.Add(typeof(short).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsShort));
            extractValues.Add(typeof(ushort).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUshort));
            extractValues.Add(typeof(int).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsInt));
            extractValues.Add(typeof(uint).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUint));
            extractValues.Add(typeof(long).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsLong));
            extractValues.Add(typeof(ulong).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUlong));
            extractValues.Add(typeof(double).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsDouble));
            extractValues.Add(typeof(char).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsChar));
            extractValues.Add(typeof(float).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsFloat));
            extractValues.Add(typeof(decimal).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsDecimal));
            extractValues.Add(typeof(bool).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsBoolean));
            extractValues.Add(typeof(string).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsString));
            extractValues.Add(typeof(LuaFunction).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsFunction));
            extractValues.Add(typeof(LuaTable).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsTable));
            extractValues.Add(typeof(LuaUserData).TypeHandle.Value.ToInt64(), new ExtractValue(GetAsUserdata));

            extractNetObject = new ExtractValue(GetAsNetObject);
        }

        /*
         * Checks if the value at Lua stack index stackPos matches paramType,
         * returning a conversion function if it does and null otherwise.
         */
        internal ExtractValue GetExtractor(IReflect paramType)
        {
            return GetExtractor(paramType.UnderlyingSystemType);
        }
        internal ExtractValue GetExtractor(Type paramType)
        {
            if (paramType.IsByRef)
                paramType = paramType.GetElementType();

            long runtimeHandleValue = paramType.TypeHandle.Value.ToInt64();
            if (extractValues.ContainsKey(runtimeHandleValue))
                return extractValues[runtimeHandleValue];
            else
                return extractNetObject;
        }

        internal ExtractValue CheckLuaType(IntPtr luaState, int stackPos, Type paramType)
        {
            LuaTypes luatype = LuaLib.LuaType(luaState, stackPos);

            if (paramType.IsByRef) paramType = paramType.GetElementType();

            Type underlyingType = Nullable.GetUnderlyingType(paramType);
            if (underlyingType != null)
            {
                paramType = underlyingType;     // Silently convert nullable types to their non null requics
            }

            long runtimeHandleValue = GetExtractDictionaryKey(paramType);

            if (paramType.Equals(typeof(object)))
                return extractValues[runtimeHandleValue];

            //CP: Added support for generic parameters
            if (paramType.IsGenericParameter)
            {
                if (luatype == LuaTypes.LUA_TBOOLEAN)
                    return extractValues[GetExtractDictionaryKey(typeof(bool))];
                else if (luatype == LuaTypes.LUA_TSTRING)
                    return extractValues[GetExtractDictionaryKey(typeof(string))];
                else if (luatype == LuaTypes.LUA_TTABLE)
                    return extractValues[GetExtractDictionaryKey(typeof(LuaTable))];
                else if (luatype == LuaTypes.LUA_TUSERDATA)
                    return extractValues[GetExtractDictionaryKey(typeof(object))];
                else if (luatype == LuaTypes.LUA_TFUNCTION)
                    return extractValues[GetExtractDictionaryKey(typeof(LuaFunction))];
                else if (luatype == LuaTypes.LUA_TNUMBER)
                    return extractValues[GetExtractDictionaryKey(typeof(double))];
            }

            if (LuaLib.LuaIsNumber(luaState, stackPos))
                return extractValues[runtimeHandleValue];

            if (paramType == typeof(bool))
            {
                if (LuaLib.LuaIsBoolean(luaState, stackPos))
                    return extractValues[runtimeHandleValue];
            }
            else if (paramType == typeof(string) || paramType == typeof(char[]))
            {
                if (LuaLib.LuaIsString(luaState, stackPos))
                    return extractValues[runtimeHandleValue];
                else if (luatype == LuaTypes.LUA_TNIL)
                    return extractNetObject; // kevinh - silently convert nil to a null string pointer
            }
            else if (paramType == typeof(LuaTable))
            {
                if (luatype == LuaTypes.LUA_TTABLE)
                    return extractValues[runtimeHandleValue];
            }
            else if (paramType == typeof(LuaUserData))
            {
                if (luatype == LuaTypes.LUA_TUSERDATA)
                    return extractValues[runtimeHandleValue];
            }
            else if (paramType == typeof(LuaFunction))
            {
                if (luatype == LuaTypes.LUA_TFUNCTION)
                    return extractValues[runtimeHandleValue];
            }
            else if (typeof(Delegate).IsAssignableFrom(paramType) && luatype == LuaTypes.LUA_TFUNCTION)
            {
                return new ExtractValue(new DelegateGenerator(translator, paramType).extractGenerated);
            }
            else if (paramType.IsInterface && luatype == LuaTypes.LUA_TTABLE)
            {
                return new ExtractValue(new ClassGenerator(translator, paramType).extractGenerated);
            }
            else if ((paramType.IsInterface || paramType.IsClass) && luatype == LuaTypes.LUA_TNIL)
            {
                // kevinh - allow nil to be silently converted to null - extractNetObject will return null when the item ain't found
                return extractNetObject;
            }
            else if (LuaLib.LuaType(luaState, stackPos) == LuaTypes.LUA_TTABLE)
            {
                if (LuaLib.LuaLGetMetaField(luaState, stackPos, "__index"))
                {
                    object obj = translator.getNetObject(luaState, -1);
                    LuaLib.LuaSetTop(luaState, -2);
                    if (obj != null && paramType.IsAssignableFrom(obj.GetType()))
                        return extractNetObject;
                }
                else
                    return null;
            }
            else
            {
                object obj = translator.getNetObject(luaState, stackPos);
                if (obj != null && paramType.IsAssignableFrom(obj.GetType()))
                    return extractNetObject;
            }

            return null;
        }

        private long GetExtractDictionaryKey(Type targetType)
        {
            return targetType.TypeHandle.Value.ToInt64();
        }
        /*
         * The following functions return the value in the Lua stack
         * index stackPos as the desired type if it can, or null
         * otherwise.
         */
        private object GetAsSbyte(IntPtr luaState, int stackPos)
        {
            sbyte retVal = (sbyte)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsByte(IntPtr luaState, int stackPos)
        {
            byte retVal = (byte)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsShort(IntPtr luaState, int stackPos)
        {
            short retVal = (short)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsUshort(IntPtr luaState, int stackPos)
        {
            ushort retVal = (ushort)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsInt(IntPtr luaState, int stackPos)
        {
            int retVal = (int)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsUint(IntPtr luaState, int stackPos)
        {
            uint retVal = (uint)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsLong(IntPtr luaState, int stackPos)
        {
            long retVal = (long)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsUlong(IntPtr luaState, int stackPos)
        {
            ulong retVal = (ulong)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsDouble(IntPtr luaState, int stackPos)
        {
            double retVal = LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsChar(IntPtr luaState, int stackPos)
        {
            char retVal = (char)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsFloat(IntPtr luaState, int stackPos)
        {
            float retVal = (float)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsDecimal(IntPtr luaState, int stackPos)
        {
            decimal retVal = (decimal)LuaLib.LuaToNumber(luaState, stackPos);
			if (retVal == 0 && !LuaLib.LuaIsNumber (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsBoolean(IntPtr luaState, int stackPos)
        {
            return LuaLib.LuaToBoolean(luaState, stackPos);
        }
		private object GetAsCharArray (IntPtr luaState, int stackPos)
		{
			string retVal = LuaLib.LuaToString (luaState, stackPos).ToString ();
			if (string.IsNullOrEmpty(retVal) && !LuaLib.LuaIsString (luaState, stackPos))
				return null;

			return retVal.ToCharArray();
		}
        private object GetAsString(IntPtr luaState, int stackPos)
        {
			string retVal = LuaLib.LuaToString (luaState, stackPos).ToString ();
			if (string.IsNullOrEmpty(retVal) && !LuaLib.LuaIsString (luaState, stackPos))
				return null;
            return retVal;
        }
        private object GetAsTable(IntPtr luaState, int stackPos)
        {
            return translator.getTable(luaState, stackPos);
        }
        private object GetAsFunction(IntPtr luaState, int stackPos)
        {
            return translator.getFunction(luaState, stackPos);
        }
        private object GetAsUserdata(IntPtr luaState, int stackPos)
        {
            return translator.getUserData(luaState, stackPos);
        }
        public object GetAsObject(IntPtr luaState, int stackPos)
        {
            if (LuaLib.LuaType(luaState, stackPos) == LuaTypes.LUA_TTABLE)
            {
                if (LuaLib.LuaLGetMetaField(luaState, stackPos, "__index"))
                {
                    if (LuaLib.LuaLCheckMetaTable(luaState, -1))
                    {
                        LuaLib.LuaInsert(luaState, stackPos);
                        LuaLib.LuaRemove(luaState, stackPos + 1);
                    }
                    else
                    {
                        LuaLib.LuaSetTop(luaState, -2);
                    }
                }
            }
            object obj = translator.getObject(luaState, stackPos);
            return obj;
        }
        public object GetAsNetObject(IntPtr luaState, int stackPos)
        {
            object obj = translator.getNetObject(luaState, stackPos);
            if (obj == null && LuaLib.LuaType(luaState, stackPos) == LuaTypes.LUA_TTABLE)
            {
                if (LuaLib.LuaLGetMetaField(luaState, stackPos, "__index"))
                {
                    if (LuaLib.LuaLCheckMetaTable(luaState, -1))
                    {
                        LuaLib.LuaInsert(luaState, stackPos);
                        LuaLib.LuaRemove(luaState, stackPos + 1);
                        obj = translator.getNetObject(luaState, stackPos);
                    }
                    else
                    {
                        LuaLib.LuaSetTop(luaState, -2);
                    }
                }
            }
            return obj;
        }
    }
}

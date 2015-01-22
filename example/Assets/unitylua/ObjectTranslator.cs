namespace LuaInterface
{
	using System;
	using System.IO;
	using System.Collections;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using System.Collections.Generic;
	using System.Diagnostics;
	
	/*
     * Passes objects from the CLR to Lua and vice-versa
     *
     * Author: Fabio Mascarenhas
     * Version: 1.0
     */
	public class ObjectTranslator
	{
		internal CheckType typeChecker;
		
		// object # to object (FIXME - it should be possible to get object address as an object #)
		public readonly Dictionary<int, object> objects = new Dictionary<int, object>();
		// object to object #
		public readonly Dictionary<object, int> objectsBackMap = new Dictionary<object, int>();
		internal LuaState interpreter;
		public MetaFunctions metaFunctions;
		public List<Assembly> assemblies;
		private LuaCSFunction registerTableFunction,unregisterTableFunction,getMethodSigFunction,
		getConstructorSigFunction,importTypeFunction,loadAssemblyFunction, ctypeFunction, enumFromIntFunction;
		
		internal EventHandlerContainer pendingEvents = new EventHandlerContainer();
		
		public static ObjectTranslator FromState(IntPtr luaState)
		{
			LuaLib.LuaGetGlobal(luaState, "_translator");
			IntPtr thisptr = LuaLib.LuaToUserdata(luaState, -1);
			LuaLib.LuaPop(luaState, 1);
			
			GCHandle handle = GCHandle.FromIntPtr(thisptr);
			ObjectTranslator translator = (ObjectTranslator)handle.Target;
			
			return translator;
		}
		
		public ObjectTranslator(LuaState interpreter,IntPtr luaState)
		{	
			this.interpreter=interpreter;
			typeChecker=new CheckType(this);
			metaFunctions=new MetaFunctions(this);
			assemblies=new List<Assembly>();
			assemblies.Add(Assembly.GetExecutingAssembly());

			importTypeFunction=new LuaCSFunction(this.importType);
			loadAssemblyFunction=new LuaCSFunction(this.loadAssembly);
			registerTableFunction=new LuaCSFunction(this.registerTable);
			unregisterTableFunction=new LuaCSFunction(this.unregisterTable);
			getMethodSigFunction=new LuaCSFunction(this.getMethodSignature);
			getConstructorSigFunction=new LuaCSFunction(this.getConstructorSignature);
			
			ctypeFunction = new LuaCSFunction(this.ctype);
			enumFromIntFunction = new LuaCSFunction(this.enumFromInt);
			
			createLuaObjectList(luaState);
			createIndexingMetaFunction(luaState);
			createBaseClassMetatable(luaState);
			createClassMetatable(luaState);
			createFunctionMetatable(luaState);
			setGlobalFunctions(luaState);
		}
		
		/*
         * Sets up the list of objects in the Lua side
         */
		private void createLuaObjectList(IntPtr luaState)
		{
			LuaLib.LuaPushString(luaState,"luaNet_objects");
			LuaLib.LuaNewTable(luaState);
			LuaLib.LuaNewTable(luaState);
			LuaLib.LuaPushString(luaState,"__mode");
			LuaLib.LuaPushString(luaState,"v");
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaSetMetaTable(luaState,-2);
			LuaLib.LuaSetTable(luaState, (int) LuaIndexes.LUA_REGISTRYINDEX);
		}
		/*
         * Registers the indexing function of CLR objects
         * passed to Lua
         */
		private void createIndexingMetaFunction(IntPtr luaState)
		{
			LuaLib.LuaPushString(luaState,"luaNet_indexfunction");
			LuaLib.LuaLDoString(luaState,MetaFunctions.luaIndexFunction);
			//LuaDLL.lua_pushstdcallcfunction(luaState,indexFunction);
			LuaLib.LuaRawSet(luaState, (int) LuaIndexes.LUA_REGISTRYINDEX);
		}
		/*
         * Creates the metatable for superclasses (the base
         * field of registered tables)
         */
		private void createBaseClassMetatable(IntPtr luaState)
		{
			LuaLib.LuaLNewMetaTable(luaState,"luaNet_searchbase");
			LuaLib.LuaPushString(luaState,"__gc");
			LuaLib.LuaPushStdCallCFunction(luaState, metaFunctions.gcFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__tostring");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.toStringFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__index");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.baseIndexFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__newindex");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.newindexFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaSetTop(luaState,-2);
		}
		/*
         * Creates the metatable for type references
         */
		private void createClassMetatable(IntPtr luaState)
		{
			LuaLib.LuaLNewMetaTable(luaState,"luaNet_class");
			LuaLib.LuaPushString(luaState,"__gc");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.gcFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__tostring");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.toStringFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__index");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.classIndexFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__newindex");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.classNewindexFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__call");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.callConstructorFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaSetTop(luaState,-2);
		}
		/*
         * Registers the global functions used by LuaInterface
         */
		private void setGlobalFunctions(IntPtr luaState)
		{
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.indexFunction);
			LuaLib.LuaSetGlobal(luaState,"get_object_member");
			LuaLib.LuaPushStdCallCFunction(luaState,importTypeFunction);
			LuaLib.LuaSetGlobal(luaState,"import_type");
			LuaLib.LuaPushStdCallCFunction(luaState,loadAssemblyFunction);
			LuaLib.LuaSetGlobal(luaState,"load_assembly");
			LuaLib.LuaPushStdCallCFunction(luaState,registerTableFunction);
			LuaLib.LuaSetGlobal(luaState,"make_object");
			LuaLib.LuaPushStdCallCFunction(luaState,unregisterTableFunction);
			LuaLib.LuaSetGlobal(luaState,"free_object");
			LuaLib.LuaPushStdCallCFunction(luaState,getMethodSigFunction);
			LuaLib.LuaSetGlobal(luaState,"get_method_bysig");
			LuaLib.LuaPushStdCallCFunction(luaState,getConstructorSigFunction);
			LuaLib.LuaSetGlobal(luaState,"get_constructor_bysig");
			LuaLib.LuaPushStdCallCFunction(luaState,ctypeFunction);
			LuaLib.LuaSetGlobal(luaState,"ctype");
			LuaLib.LuaPushStdCallCFunction(luaState,enumFromIntFunction);
			LuaLib.LuaSetGlobal(luaState,"enum");
			
		}
		
		/*
         * Creates the metatable for delegates
         */
		private void createFunctionMetatable(IntPtr luaState)
		{
			LuaLib.LuaLNewMetaTable(luaState,"luaNet_function");
			LuaLib.LuaPushString(luaState,"__gc");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.gcFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaPushString(luaState,"__call");
			LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.execDelegateFunction);
			LuaLib.LuaSetTable(luaState,-3);
			LuaLib.LuaSetTop(luaState,-2);
		}
		/*
         * Passes errors (argument e) to the Lua interpreter
         */
		internal void throwError(IntPtr luaState, object e)
		{
			// We use this to remove anything pushed by luaL_where
			int oldTop = LuaLib.LuaGetTop(luaState);
			
			// Stack frame #1 is our C# wrapper, so not very interesting to the user
			// Stack frame #2 must be the lua code that called us, so that's what we want to use
			LuaLib.LuaLWhere(luaState, 1);
			object[] curlev = popValues(luaState, oldTop);
			
			// Determine the position in the script where the exception was triggered
			string errLocation = "";
			if (curlev.Length > 0)
				errLocation = curlev[0].ToString();
			
			string message = e as string;
			if (message != null)
			{
				// Wrap Lua error (just a string) and store the error location
				e = new LuaScriptException(message, errLocation);
			}
			else
			{
				Exception ex = e as Exception;
				if (ex != null)
				{
					// Wrap generic .NET exception as an InnerException and store the error location
					e = new LuaScriptException(ex, errLocation);
				}
			}
			
			push(luaState, e);
			LuaLib.LuaError(luaState);
		}
		/*
         * Implementation of load_assembly. Throws an error
         * if the assembly is not found.
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int loadAssembly(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			try
			{
				string assemblyName=LuaLib.LuaToString(luaState,1);
				
				Assembly assembly = null;
				
				//assembly = Assembly.GetExecutingAssembly();
				
				try
				{
					assembly = Assembly.Load(assemblyName);
				}
				catch (BadImageFormatException)
				{
					// The assemblyName was invalid.  It is most likely a path.
				}
				
				if (assembly == null)
				{
					assembly = Assembly.Load(AssemblyName.GetAssemblyName(assemblyName));
				}
				
				if (assembly != null && !translator.assemblies.Contains(assembly))
				{
					translator.assemblies.Add(assembly);
				}
			}
			catch(Exception e)
			{
				translator.throwError(luaState,e);
			}
			
			return 0;
		}
		
		internal Type FindType(string className)
		{
			foreach(Assembly assembly in assemblies)
			{
				Type klass=assembly.GetType(className);
				if(klass!=null)
				{
					return klass;
				}
			}
			return null;
		}
		
		/*
         * Implementation of import_type. Returns nil if the
         * type is not found.
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int importType(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			string className=LuaLib.LuaToString(luaState,1);
			Type klass=translator.FindType(className);
			if(klass!=null)
				translator.pushType(luaState,klass);
			else
				LuaLib.LuaPushNil(luaState);
			return 1;
		}
		/*
         * Implementation of make_object. Registers a table (first
         * argument in the stack) as an object subclassing the
         * type passed as second argument in the stack.
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int registerTable(IntPtr luaState)
        {
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
#if UNITY_IPHONE
			translator.throwError(luaState,"Tables as Objects not implemnented");
#else            
			if(LuaLib.LuaType(luaState,1)==LuaTypes.LUA_TTABLE)
			{
				LuaTable luaTable=translator.getTable(luaState,1);
				string superclassName = LuaLib.LuaToString(luaState, 2);
				if (superclassName != null)
				{
					Type klass = translator.FindType(superclassName);
					if (klass != null)
					{
						// Creates and pushes the object in the stack, setting
						// it as the  metatable of the first argument
						object obj = CodeGeneration.Instance.GetClassInstance(klass, luaTable);
						translator.pushObject(luaState, obj, "luaNet_metatable");
						LuaLib.LuaNewTable(luaState);
						LuaLib.LuaPushString(luaState, "__index");
						LuaLib.LuaPushValue(luaState, -3);
						LuaLib.LuaSetTable(luaState, -3);
						LuaLib.LuaPushString(luaState, "__newindex");
						LuaLib.LuaPushValue(luaState, -3);
						LuaLib.LuaSetTable(luaState, -3);
						LuaLib.LuaSetMetaTable(luaState, 1);
						// Pushes the object again, this time as the base field
						// of the table and with the luaNet_searchbase metatable
						LuaLib.LuaPushString(luaState, "base");
						int index = translator.addObject(obj);
						translator.pushNewObject(luaState, obj, index, "luaNet_searchbase");
						LuaLib.LuaRawSet(luaState, 1);
					}
					else
						translator.throwError(luaState, "register_table: can not find superclass '" + superclassName + "'");
				}
				else
					translator.throwError(luaState, "register_table: superclass name can not be null");
			}
			else translator.throwError(luaState,"register_table: first arg is not a table");
			#endif
			return 0;
		}
		/*
         * Implementation of free_object. Clears the metatable and the
         * base field, freeing the created object for garbage-collection
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int unregisterTable(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			try
			{
				if(LuaLib.LuaGetMetaTable(luaState,1)!=0)
				{
					LuaLib.LuaPushString(luaState,"__index");
					LuaLib.LuaGetTable(luaState,-2);
					object obj=translator.getRawNetObject(luaState,-1);
					if(obj==null) translator.throwError(luaState,"unregister_table: arg is not valid table");
					FieldInfo luaTableField=obj.GetType().GetField("__luaInterface_luaTable");
					if(luaTableField==null) translator.throwError(luaState,"unregister_table: arg is not valid table");
					luaTableField.SetValue(obj,null);
					LuaLib.LuaPushNil(luaState);
					LuaLib.LuaSetMetaTable(luaState,1);
					LuaLib.LuaPushString(luaState,"base");
					LuaLib.LuaPushNil(luaState);
					LuaLib.LuaSetTable(luaState,1);
				}
				else translator.throwError(luaState,"unregister_table: arg is not valid table");
			}
			catch(Exception e)
			{
				translator.throwError(luaState,e.Message);
			}
			return 0;
		}
		/*
         * Implementation of get_method_bysig. Returns nil
         * if no matching method is not found.
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int getMethodSignature(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			IReflect klass; object target;
			int udata=LuaLib.LuaNetCheckUdata(luaState,1,"luaNet_class");
			if(udata!=-1)
			{
				klass=(IReflect)translator.objects[udata];
				target=null;
			}
			else
			{
				target=translator.getRawNetObject(luaState,1);
				if(target==null)
				{
					translator.throwError(luaState,"get_method_bysig: first arg is not type or object reference");
					LuaLib.LuaPushNil(luaState);
					return 1;
				}
				klass=target.GetType();
			}
			string methodName=LuaLib.LuaToString(luaState,2);
			Type[] signature=new Type[LuaLib.LuaGetTop(luaState)-2];
			for(int i=0;i<signature.Length;i++)
				signature[i]=translator.FindType(LuaLib.LuaToString(luaState,i+3));
			try
			{
				//CP: Added ignore case
				MethodInfo method=klass.GetMethod(methodName,BindingFlags.Public | BindingFlags.Static |
				                                  BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase, null, signature, null);
				translator.pushFunction(luaState,new LuaCSFunction((new LuaMethodWrapper(translator,target,klass,method)).call));
			}
			catch(Exception e)
			{
				translator.throwError(luaState,e);
				LuaLib.LuaPushNil(luaState);
			}
			return 1;
		}
		/*
         * Implementation of get_constructor_bysig. Returns nil
         * if no matching constructor is found.
         */
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int getConstructorSignature(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			IReflect klass=null;
			int udata=LuaLib.LuaNetCheckUdata(luaState,1,"luaNet_class");
			if(udata!=-1)
			{
				klass=(IReflect)translator.objects[udata];
			}
			if(klass==null)
			{
				translator.throwError(luaState,"get_constructor_bysig: first arg is invalid type reference");
			}
			Type[] signature=new Type[LuaLib.LuaGetTop(luaState)-1];
			for(int i=0;i<signature.Length;i++)
				signature[i]=translator.FindType(LuaLib.LuaToString(luaState,i+2));
			try
			{
				ConstructorInfo constructor=klass.UnderlyingSystemType.GetConstructor(signature);
				translator.pushFunction(luaState,new LuaCSFunction((new LuaMethodWrapper(translator,null,klass,constructor)).call));
			}
			catch(Exception e)
			{
				translator.throwError(luaState,e);
				LuaLib.LuaPushNil(luaState);
			}
			return 1;
		}
		
		private Type typeOf(IntPtr luaState, int idx)
		{
			int udata=LuaLib.LuaNetCheckUdata(luaState,1,"luaNet_class");
			if (udata == -1) {
				return null;
			} else {
				ProxyType pt = (ProxyType)objects[udata];
				return pt.UnderlyingSystemType;
			}
		}
		
		public int pushError(IntPtr luaState, string msg)
		{
			LuaLib.LuaPushNil(luaState);
			LuaLib.LuaPushString(luaState,msg);
			return 2;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int ctype(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			Type t = translator.typeOf(luaState,1);
			if (t == null) {
				return translator.pushError(luaState,"not a CLR class");
			}
			translator.pushObject(luaState,t,"luaNet_metatable");
			return 1;
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
		public static int enumFromInt(IntPtr luaState)
		{
			ObjectTranslator translator = ObjectTranslator.FromState(luaState);
			Type t = translator.typeOf(luaState,1);
			if (t == null || ! t.IsEnum) {
				return translator.pushError(luaState,"not an enum");
			}
			object res = null;
			LuaTypes lt = LuaLib.LuaType(luaState,2);
			if (lt == LuaTypes.LUA_TNUMBER) {
				int ival = (int)LuaLib.LuaToNumber(luaState,2);
				res = Enum.ToObject(t,ival);
			} else
			if (lt == LuaTypes.LUA_TSTRING) {
				string sflags = LuaLib.LuaToString(luaState,2);
				string err = null;
				try {
					res = Enum.Parse(t,sflags);
				} catch (ArgumentException e) {
					err = e.Message;
				}
				if (err != null) {
					return translator.pushError(luaState,err);
				}
			} else {
				return translator.pushError(luaState,"second argument must be a integer or a string");
			}
			translator.pushObject(luaState,res,"luaNet_metatable");
			return 1;
		}
		
		/*
         * Pushes a type reference into the stack
         */
		internal void pushType(IntPtr luaState, Type t)
		{
			pushObject(luaState,new ProxyType(t),"luaNet_class");
		}
		/*
         * Pushes a delegate into the stack
         */
		internal void pushFunction(IntPtr luaState, LuaCSFunction func)
		{
			pushObject(luaState,func,"luaNet_function");
		}
		/*
         * Pushes a CLR object into the Lua stack as an userdata
         * with the provided metatable
         */
		internal void pushObject(IntPtr luaState, object o, string metatable)
		{
			int index = -1;
			// Pushes nil
			if(o==null)
			{
				LuaLib.LuaPushNil(luaState);
				return;
			}
			
			// Object already in the list of Lua objects? Push the stored reference.
			bool found = objectsBackMap.TryGetValue(o, out index);
			if(found)
			{
				LuaLib.LuaLGetMetaTable(luaState,"luaNet_objects");
				LuaLib.LuaRawGetI(luaState,-1,index);
				
				// Note: starting with lua5.1 the garbage collector may remove weak reference items (such as our luaNet_objects values) when the initial GC sweep
				// occurs, but the actual call of the __gc finalizer for that object may not happen until a little while later.  During that window we might call
				// this routine and find the element missing from luaNet_objects, but collectObject() has not yet been called.  In that case, we go ahead and call collect
				// object here
				// did we find a non nil object in our table? if not, we need to call collect object
				LuaTypes type = LuaLib.LuaType(luaState, -1);
				if (type != LuaTypes.LUA_TNIL)
				{
					LuaLib.LuaRemove(luaState, -2);     // drop the metatable - we're going to leave our object on the stack
					
					return;
				}
				
				// MetaFunctions.dumpStack(this, luaState);
				LuaLib.LuaRemove(luaState, -1);    // remove the nil object value
				LuaLib.LuaRemove(luaState, -1);    // remove the metatable
				
				collectObject(o, index);            // Remove from both our tables and fall out to get a new ID
			}
			index = addObject(o);
			
			pushNewObject(luaState,o,index,metatable);
		}
		
		
		/*
         * Pushes a new object into the Lua stack with the provided
         * metatable
         */
		private void pushNewObject(IntPtr luaState,object o,int index,string metatable)
		{
			if(metatable=="luaNet_metatable")
			{
				// Gets or creates the metatable for the object's type
				LuaLib.LuaLGetMetaTable(luaState,o.GetType().AssemblyQualifiedName);
				
				if(LuaLib.LuaIsNil(luaState,-1))
				{
					LuaLib.LuaSetTop(luaState,-2);
					LuaLib.LuaLNewMetaTable(luaState,o.GetType().AssemblyQualifiedName);
					LuaLib.LuaPushString(luaState,"cache");
					LuaLib.LuaNewTable(luaState);
					LuaLib.LuaRawSet(luaState,-3);
					LuaLib.LuaPushLightUserdata(luaState,LuaLib.LuaNetGetTag());
					LuaLib.LuaPushNumber(luaState,1);
					LuaLib.LuaRawSet(luaState,-3);
					LuaLib.LuaPushString(luaState,"__index");
					LuaLib.LuaPushString(luaState,"luaNet_indexfunction");
					LuaLib.Lua_RawGet(luaState, (int) LuaIndexes.LUA_REGISTRYINDEX);
					LuaLib.LuaRawSet(luaState,-3);
					LuaLib.LuaPushString(luaState,"__gc");
					LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.gcFunction);
					LuaLib.LuaRawSet(luaState,-3);
					LuaLib.LuaPushString(luaState,"__tostring");
					LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.toStringFunction);
					LuaLib.LuaRawSet(luaState,-3);
					LuaLib.LuaPushString(luaState,"__newindex");
					LuaLib.LuaPushStdCallCFunction(luaState,metaFunctions.newindexFunction);
					LuaLib.LuaRawSet(luaState,-3);
				}
			}
			else
			{
				LuaLib.LuaLGetMetaTable(luaState,metatable);
			}
			
			// Stores the object index in the Lua list and pushes the
			// index into the Lua stack
			LuaLib.LuaLGetMetaTable(luaState,"luaNet_objects");
			LuaLib.LuaNetNewUdata(luaState,index);
			LuaLib.LuaPushValue(luaState,-3);
			LuaLib.LuaRemove(luaState,-4);
			LuaLib.LuaSetMetaTable(luaState,-2);
			LuaLib.LuaPushValue(luaState,-1);
			LuaLib.LuaRawSetI(luaState,-3,index);
			LuaLib.LuaRemove(luaState,-2);
		}
		/*
         * Gets an object from the Lua stack with the desired type, if it matches, otherwise
         * returns null.
         */
		internal object getAsType(IntPtr luaState,int stackPos,Type paramType)
		{
			ExtractValue extractor=typeChecker.CheckLuaType(luaState,stackPos,paramType);
			if(extractor!=null) return extractor(luaState,stackPos);
			return null;
		}
		
		
		/// <summary>
		/// Given the Lua int ID for an object remove it from our maps
		/// </summary>
		/// <param name="udata"></param>
		internal void collectObject(int udata)
		{
			object o;
			bool found = objects.TryGetValue(udata, out o);
			
			// The other variant of collectObject might have gotten here first, in that case we will silently ignore the missing entry
			if (found)
			{
				// Debug.WriteLine("Removing " + o.ToString() + " @ " + udata);
				
				objects.Remove(udata);
				objectsBackMap.Remove(o);
			}
		}
		
		
		/// <summary>
		/// Given an object reference, remove it from our maps
		/// </summary>
		/// <param name="udata"></param>
		void collectObject(object o, int udata)
		{
			// Debug.WriteLine("Removing " + o.ToString() + " @ " + udata);
			
			objects.Remove(udata);
			objectsBackMap.Remove(o);
		}
		
		
		/// <summary>
		/// We want to ensure that objects always have a unique ID
		/// </summary>
		int nextObj = 0;
		
		int addObject(object obj)
		{
			// New object: inserts it in the list
			int index = nextObj++;
			
			// Debug.WriteLine("Adding " + obj.ToString() + " @ " + index);
			
			objects[index] = obj;
			objectsBackMap[obj] = index;
			
			return index;
		}
		
		
		
		/*
         * Gets an object from the Lua stack according to its Lua type.
         */
		internal object getObject(IntPtr luaState,int index)
		{
			LuaTypes type=LuaLib.LuaType(luaState,index);
			switch(type)
			{
			case LuaTypes.LUA_TNUMBER:
			{
				return LuaLib.LuaToNumber(luaState,index);
			}
			case LuaTypes.LUA_TSTRING:
			{
				return LuaLib.LuaToString(luaState,index);
			}
			case LuaTypes.LUA_TBOOLEAN:
			{
				return LuaLib.LuaToBoolean(luaState,index);
			}
			case LuaTypes.LUA_TTABLE:
			{
				return getTable(luaState,index);
			}
			case LuaTypes.LUA_TFUNCTION:
			{
				return getFunction(luaState,index);
			}
			case LuaTypes.LUA_TUSERDATA:
			{
				int udata=LuaLib.LuaNetToNetObject(luaState,index);
				if(udata!=-1)
					return objects[udata];
				else
					return getUserData(luaState,index);
			}
			default:
				return null;
			}
		}
		/*
         * Gets the table in the index positon of the Lua stack.
         */
		internal LuaTable getTable(IntPtr luaState,int index)
		{
			LuaLib.LuaPushValue(luaState,index);
			return new LuaTable(LuaLib.LuaLRef(luaState,LuaIndexes.LUA_REGISTRYINDEX),interpreter);
		}
		/*
         * Gets the userdata in the index positon of the Lua stack.
         */
		internal LuaUserData getUserData(IntPtr luaState,int index)
		{
			LuaLib.LuaPushValue(luaState,index);
			return new LuaUserData(LuaLib.LuaLRef(luaState,LuaIndexes.LUA_REGISTRYINDEX),interpreter);
		}
		/*
         * Gets the function in the index positon of the Lua stack.
         */
		internal LuaFunction getFunction(IntPtr luaState,int index)
		{
			LuaLib.LuaPushValue(luaState,index);
			return new LuaFunction(LuaLib.LuaLRef(luaState,LuaIndexes.LUA_REGISTRYINDEX),interpreter);
		}
		/*
         * Gets the CLR object in the index positon of the Lua stack. Returns
         * delegates as Lua functions.
         */
		internal object getNetObject(IntPtr luaState,int index)
		{
			int idx=LuaLib.LuaNetToNetObject(luaState,index);
			if(idx!=-1)
				return objects[idx];
			else
				return null;
		}
		/*
         * Gets the CLR object in the index positon of the Lua stack. Returns
         * delegates as is.
         */
		internal object getRawNetObject(IntPtr luaState,int index)
		{
			int udata=LuaLib.LuaNetRawNetObj(luaState,index);
			if(udata!=-1)
			{
				return objects[udata];
			}
			return null;
		}
		/*
         * Pushes the entire array into the Lua stack and returns the number
         * of elements pushed.
         */
		internal int returnValues(IntPtr luaState, object[] returnValues)
		{
			if(LuaLib.LuaCheckStack(luaState,returnValues.Length+5))
			{
				for(int i=0;i<returnValues.Length;i++)
				{
					push(luaState,returnValues[i]);
				}
				return returnValues.Length;
			} else
				return 0;
		}
		/*
         * Gets the values from the provided index to
         * the top of the stack and returns them in an array.
         */
		internal object[] popValues(IntPtr luaState,int oldTop)
		{
			int newTop=LuaLib.LuaGetTop(luaState);
			if(oldTop==newTop)
			{
				return null;
			}
			else
			{
				ArrayList returnValues=new ArrayList();
				for(int i=oldTop+1;i<=newTop;i++)
				{
					returnValues.Add(getObject(luaState,i));
				}
				LuaLib.LuaSetTop(luaState,oldTop);
				return returnValues.ToArray();
			}
		}
		/*
         * Gets the values from the provided index to
         * the top of the stack and returns them in an array, casting
         * them to the provided types.
         */
		internal object[] popValues(IntPtr luaState,int oldTop,Type[] popTypes)
		{
			int newTop=LuaLib.LuaGetTop(luaState);
			if(oldTop==newTop)
			{
				return null;
			}
			else
			{
				int iTypes;
				ArrayList returnValues=new ArrayList();
				if(popTypes[0] == typeof(void))
					iTypes=1;
				else
					iTypes=0;
				for(int i=oldTop+1;i<=newTop;i++)
				{
					returnValues.Add(getAsType(luaState,i,popTypes[iTypes]));
					iTypes++;
				}
				LuaLib.LuaSetTop(luaState,oldTop);
				return returnValues.ToArray();
			}
		}
		
		// kevinh - the following line doesn't work for remoting proxies - they always return a match for 'is'
		// else if(o is ILuaGeneratedType)
		static bool IsILua(object o)
        {
#if ! UNITY_IPHONE
            if (o is ILuaGeneratedType)
			{
				// Make sure we are _really_ ILuaGenerated
				Type typ = o.GetType();
				
				return (typ.GetInterface("ILuaGeneratedType") != null);
			}
			else
#endif
				return false;
		}
		
		/*
         * Pushes the object into the Lua stack according to its type.
         */
		internal void push(IntPtr luaState, object o)
		{
			if((object)o==(object)null)
			{
				LuaLib.LuaPushNil(luaState);
			}
			else if (o is UnityEngine.GameObject && (UnityEngine.GameObject)o==null)
			{
				LuaLib.LuaPushNil(luaState);
			}
			else if(o is sbyte || o is byte || o is short || o is ushort ||
			        o is int || o is uint || o is long || o is float ||
			        o is ulong || o is decimal || o is double)
			{
				double d=Convert.ToDouble(o);
				LuaLib.LuaPushNumber(luaState,d);
			}
			else if(o is char)
			{
				double d = (char)o;
				LuaLib.LuaPushNumber(luaState,d);
			}
			else if(o is string)
			{
				string str=(string)o;
				LuaLib.LuaPushString(luaState,str);
			}
			else if(o is bool)
			{
				bool b=(bool)o;
				LuaLib.LuaPushBoolean(luaState,b);
			}
			else if(IsILua(o))
            {
#if ! UNITY_IPHONE
                (((ILuaGeneratedType)o).__luaInterface_getLuaTable()).push(luaState);
#endif
			}
			else if(o is LuaTable)
			{
				((LuaTable)o).push(luaState);
			}
			else if(o is LuaCSFunction)
			{
				pushFunction(luaState,(LuaCSFunction)o);
			}
			else if(o is LuaFunction)
			{
				((LuaFunction)o).push(luaState);
			}
			else
			{
				pushObject(luaState,o,"luaNet_metatable");
			}
		}
		/*
         * Checks if the method matches the arguments in the Lua stack, getting
         * the arguments if it does.
         */
		internal bool matchParameters(IntPtr luaState,MethodBase method,ref MethodCache methodCache)
		{
			return metaFunctions.matchParameters(luaState,method,ref methodCache);
		}
		
		internal Array tableToArray(object luaParamValue, Type paramArrayType) {
			return metaFunctions.TableToArray(luaParamValue,paramArrayType);
		}
	}
}
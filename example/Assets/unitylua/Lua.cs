
namespace LuaInterface
{
    using System;
    using System.IO;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Reflection;
	using System.Security;
	using System.Runtime.InteropServices;
    using System.Threading;
    using UnityEngine;

    public class LuaState : IDisposable
    {
        public IntPtr L;

        internal LuaCSFunction tracebackFunction;
        internal ObjectTranslator translator;

		internal LuaCSFunction panicCallback;

        // Overrides
        internal LuaCSFunction printFunction;
        internal LuaCSFunction loadfileFunction;
        internal LuaCSFunction loaderFunction;
        internal LuaCSFunction dofileFunction;

        public LuaState()
        {
            // Create State
            L = LuaLib.LuaLNewState();

            // Create LuaInterface library
            LuaLib.LuaLOpenLibs(L);// Luaの標準ライブラリをLua側で使えるようにする記述
            LuaLib.LuaPushString(L, "LUAINTERFACE LOADED");
            LuaLib.LuaPushBoolean(L, true);
            LuaLib.LuaSetTable(L, (int) LuaIndexes.LUA_REGISTRYINDEX);
            LuaLib.LuaNewTable(L);
            LuaLib.LuaSetGlobal(L, "luanet");
            LuaLib.LuaPushValue(L, (int)LuaIndexes.LUA_GLOBALSINDEX);
            LuaLib.LuaGetGlobal(L, "luanet");
            LuaLib.LuaPushString(L, "getmetatable");
            LuaLib.LuaGetGlobal(L, "getmetatable");
            LuaLib.LuaSetTable(L, -3);

            // Set luanet as global for object translator
            LuaLib.LuaReplace(L, (int)LuaIndexes.LUA_GLOBALSINDEX);
            translator = new ObjectTranslator(this,L);
            LuaLib.LuaReplace(L, (int)LuaIndexes.LUA_GLOBALSINDEX);

			GCHandle handle = GCHandle.Alloc(translator, GCHandleType.Pinned);
			IntPtr thisptr = GCHandle.ToIntPtr(handle);
			LuaLib.LuaPushLightUserdata(L, thisptr);
			LuaLib.LuaSetGlobal(L, "_translator");

            tracebackFunction = new LuaCSFunction(LuaStatic.traceback);

            // We need to keep this in a managed reference so the delegate doesn't get garbage collected
			panicCallback = new LuaCSFunction(LuaStatic.panic);
            LuaLib.LuaAtPanic(L, panicCallback);

            printFunction = new LuaCSFunction(LuaStatic.print);
            LuaLib.LuaPushStdCallCFunction(L, printFunction);
            LuaLib.LuaSetField(L, LuaIndexes.LUA_GLOBALSINDEX, "print");

            loadfileFunction = new LuaCSFunction(LuaStatic.loadfile);
            LuaLib.LuaPushStdCallCFunction(L, loadfileFunction);
            LuaLib.LuaSetField(L, LuaIndexes.LUA_GLOBALSINDEX, "loadfile");

            dofileFunction = new LuaCSFunction(LuaStatic.dofile);
            LuaLib.LuaPushStdCallCFunction(L, dofileFunction);
            LuaLib.LuaSetField(L, LuaIndexes.LUA_GLOBALSINDEX, "dofile");

            // Insert our loader FIRST
            loaderFunction = new LuaCSFunction(LuaStatic.loader);
            LuaLib.LuaPushStdCallCFunction(L, loaderFunction);
            int loaderFunc = LuaLib.LuaGetTop( L );

            LuaLib.LuaGetField( L, LuaIndexes.LUA_GLOBALSINDEX, "package" );
            LuaLib.LuaGetField( L, -1, "loaders" );
            int loaderTable = LuaLib.LuaGetTop( L );

            // Shift table elements right
            for( int e = LuaLib.LuaLGetN( L, loaderTable ) + 1; e > 1; e-- ) 
            {
                LuaLib.LuaRawGetI( L, loaderTable, e-1 );
                LuaLib.LuaRawSetI( L, loaderTable, e );
            }
            LuaLib.LuaPushValue( L, loaderFunc );
            LuaLib.LuaRawSetI( L, loaderTable, 1 );
            LuaLib.LuaSetTop( L, 0 );

            DoString(LuaStatic.init_luanet);
        }

        public void Close()
        {
            if (L != IntPtr.Zero)
            {
                LuaLib.LuaClose(L);
            }
        }

        /// <summary>
        /// Assuming we have a Lua error string sitting on the stack, throw a C# exception out to the user's app
        /// </summary>
        /// <exception cref="LuaScriptException">Thrown if the script caused an exception</exception>
        internal void ThrowExceptionFromError(int oldTop)
        {
            object err = translator.getObject(L, -1);
            LuaLib.LuaSetTop(L, oldTop);

            // A pre-wrapped exception - just rethrow it (stack trace of InnerException will be preserved)
            LuaScriptException luaEx = err as LuaScriptException;
            if (luaEx != null) throw luaEx;

            // A non-wrapped Lua error (best interpreted as a string) - wrap it and throw it
            if (err == null) err = "Unknown Lua Error";
            throw new LuaScriptException(err.ToString(), "");
        }



        /// <summary>
        /// Convert C# exceptions into Lua errors
        /// </summary>
        /// <returns>num of things on stack</returns>
        /// <param name="e">null for no pending exception</param>
        internal int SetPendingException(Exception e)
        {
            Exception caughtExcept = e;

            if (caughtExcept != null)
            {
                translator.throwError(L, caughtExcept);
                LuaLib.LuaPushNil(L);

                return 1;
            }
            else
                return 0;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="chunk"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public LuaFunction LoadString(string chunk, string name, LuaTable env)
        {
            int oldTop = LuaLib.LuaGetTop(L);

            if (LuaLib.LuaL_LoadBuffer(L, chunk, chunk.Length, name) != 0)
                ThrowExceptionFromError(oldTop);

            if (env != null)
            {
                env.push(L);
                LuaLib.LuaSetFEnv(L, -2);
            }

            LuaFunction result = translator.getFunction(L, -1);
            translator.popValues(L, oldTop);

            return result;
        }

        public LuaFunction LoadString(string chunk, string name)
        {
            return LoadString(chunk, name, null);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public LuaFunction LoadFile(string fileName)
        {
            int oldTop = LuaLib.LuaGetTop(L);

            // Load with Unity3D resources
            TextAsset file = (TextAsset)Resources.Load(fileName);
            if( file == null )
            {
                ThrowExceptionFromError(oldTop);
            }

            if( LuaLib.LuaL_LoadBuffer(L, file.text, file.bytes.Length, fileName) != 0 )
            {
                ThrowExceptionFromError(oldTop);
            }

            LuaFunction result = translator.getFunction(L, -1);
            translator.popValues(L, oldTop);

            return result;
        }


        /*
         * Excutes a Lua chunk and returns all the chunk's return
         * values in an array
         */
        public object[] DoString(string chunk)
        {
            return DoString(chunk,"chunk", null);
        }

        /// <summary>
        /// Executes a Lua chnk and returns all the chunk's return values in an array.
        /// </summary>
        /// <param name="chunk">Chunk to execute</param>
        /// <param name="chunkName">Name to associate with the chunk</param>
        /// <returns></returns>
        public object[] DoString(string chunk, string chunkName, LuaTable env)
        {
            int oldTop = LuaLib.LuaGetTop(L);
            if (LuaLib.LuaL_LoadBuffer(L, chunk, chunk.Length, chunkName) == 0)
            {
                if (env != null)
                {
                    env.push(L);
                    //LuaDLL.lua_setfenv(L, -1);
					LuaLib.LuaSetFEnv(L, -2);
                }

                if (LuaLib.LuaPCall(L, 0, -1, 0) == 0)
                    return translator.popValues(L, oldTop);
                else
                    ThrowExceptionFromError(oldTop);
            }
            else
                ThrowExceptionFromError(oldTop);

            return null;            // Never reached - keeps compiler happy
        }

        public object[] DoFile(string fileName)
        {
            return DoFile(fileName, null);
        }

        /*
         * Excutes a Lua file and returns all the chunk's return
         * values in an array
         */
        public object[] DoFile(string fileName, LuaTable env)
        {
            LuaLib.LuaPushStdCallCFunction(L,tracebackFunction);
            int oldTop=LuaLib.LuaGetTop(L);

            // Load with Unity3D resources
			TextAsset file = Resources.Load<TextAsset>(fileName);
            if( file == null )
            {
                ThrowExceptionFromError(oldTop);
            }

            if( LuaLib.LuaL_LoadBuffer(L, file.text, file.bytes.Length, fileName) == 0 )
            {
                if (env != null)
                {
                    env.push(L);
                    //LuaDLL.lua_setfenv(L, -1);
					LuaLib.LuaSetFEnv(L, -2);
                }

                if (LuaLib.LuaPCall(L, 0, -1, -2) == 0)
				{
					object[] results = translator.popValues(L, oldTop);
					LuaLib.LuaPop(L, 1);
                    return results;
				}
                else
				{
                        ThrowExceptionFromError(oldTop);
				}
            }
			else
			{
				ThrowExceptionFromError(oldTop);
			}

            return null;            // Never reached - keeps compiler happy
        }


        /*
         * Indexer for global variables from the LuaInterpreter
         * Supports navigation of tables by using . operator
         */
        public object this[string fullPath]
        {
            get
            {
                object returnValue=null;
                int oldTop=LuaLib.LuaGetTop(L);
                string[] path=fullPath.Split(new char[] { '.' });
                LuaLib.LuaGetGlobal(L,path[0]);
                returnValue=translator.getObject(L,-1);
                if(path.Length>1)
                {
                    string[] remainingPath=new string[path.Length-1];
                    Array.Copy(path,1,remainingPath,0,path.Length-1);
                    returnValue=getObject(remainingPath);
                }
                LuaLib.LuaSetTop(L,oldTop);
                return returnValue;
            }
            set
            {
                int oldTop=LuaLib.LuaGetTop(L);
                string[] path=fullPath.Split(new char[] { '.' });
                if(path.Length==1)
                {
                    translator.push(L,value);
                    LuaLib.LuaSetGlobal(L,fullPath);
                }
                else
                {
                    LuaLib.LuaGetGlobal(L,path[0]);
                    string[] remainingPath=new string[path.Length-1];
                    Array.Copy(path,1,remainingPath,0,path.Length-1);
                    setObject(remainingPath,value);
                }
                LuaLib.LuaSetTop(L,oldTop);

                // Globals auto-complete
                if (value == null)
                {
                    // Remove now obsolete entries
                    globals.Remove(fullPath);
                }
                else
                {
                    // Add new entries
                    if (!globals.Contains(fullPath))
                        registerGlobal(fullPath, value.GetType(), 0);
                }
            }
        }

        #region Globals auto-complete
        private readonly List<string> globals = new List<string>();
        private bool globalsSorted;

        /// <summary>
        /// An alphabetically sorted list of all globals (objects, methods, etc.) externally added to this Lua instance
        /// </summary>
        /// <remarks>Members of globals are also listed. The formatting is optimized for text input auto-completion.</remarks>
        public IEnumerable<string> Globals
        {
            get
            {
                // Only sort list when necessary
                if (!globalsSorted)
                {
                    globals.Sort();
                    globalsSorted = true;
                }

                return globals;
            }
        }

        /// <summary>
        /// Adds an entry to <see cref="globals"/> (recursivley handles 2 levels of members)
        /// </summary>
        /// <param name="path">The index accessor path ot the entry</param>
        /// <param name="type">The type of the entry</param>
        /// <param name="recursionCounter">How deep have we gone with recursion?</param>
        private void registerGlobal(string path, Type type, int recursionCounter)
        {
            // If the type is a global method, list it directly
            if (type == typeof(LuaCSFunction))
            {
                // Format for easy method invocation
                globals.Add(path + "(");
            }
            // If the type is a class or an interface and recursion hasn't been running too long, list the members
            else if ((type.IsClass || type.IsInterface) && type != typeof(string) && recursionCounter < 2)
            {
                #region Methods
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (method.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (method.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0) &&
                        // Exclude some generic .NET methods that wouldn't be very usefull in Lua
                        method.Name != "GetType" && method.Name != "GetHashCode" && method.Name != "Equals" &&
                        method.Name != "ToString" && method.Name != "Clone" && method.Name != "Dispose" &&
                        method.Name != "GetEnumerator" && method.Name != "CopyTo" &&
                        !method.Name.StartsWith("get_", StringComparison.Ordinal) &&
                        !method.Name.StartsWith("set_", StringComparison.Ordinal) &&
                        !method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                        !method.Name.StartsWith("remove_", StringComparison.Ordinal))
                    {
                        // Format for easy method invocation
                        string command = path + ":" + method.Name + "(";
                        if (method.GetParameters().Length == 0) command += ")";
                        globals.Add(command);
                    }
                }
                #endregion

                #region Fields
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (field.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (field.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0))
                    {
                        // Go into recursion for members
                        registerGlobal(path + "." + field.Name, field.FieldType, recursionCounter + 1);
                    }
                }
                #endregion

                #region Properties
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (
                        // Check that the LuaHideAttribute and LuaGlobalAttribute were not applied
                        (property.GetCustomAttributes(typeof(LuaHideAttribute), false).Length == 0) &&
                        (property.GetCustomAttributes(typeof(LuaGlobalAttribute), false).Length == 0)
                        // Exclude some generic .NET properties that wouldn't be very usefull in Lua
                        && property.Name != "Item")
                    {
                        // Go into recursion for members
                        registerGlobal(path + "." + property.Name, property.PropertyType, recursionCounter + 1);
                    }
                }
                #endregion
            }
            // Otherwise simply add the element to the list
            else globals.Add(path);

            // List will need to be sorted on next access
            globalsSorted = false;
        }
        #endregion

        /*
         * Navigates a table in the top of the stack, returning
         * the value of the specified field
         */
        internal object getObject(string[] remainingPath)
        {
            object returnValue=null;
            for(int i=0;i<remainingPath.Length;i++)
            {
                LuaLib.LuaPushString(L,remainingPath[i]);
                LuaLib.LuaGetTable(L,-2);
                returnValue=translator.getObject(L,-1);
                if(returnValue==null) break;
            }
            return returnValue;
        }
        /*
         * Gets a numeric global variable
         */
        public double GetNumber(string fullPath)
        {
            return (double)this[fullPath];
        }
        /*
         * Gets a string global variable
         */
        public string GetString(string fullPath)
        {
            return (string)this[fullPath];
        }
        /*
         * Gets a table global variable
         */
        public LuaTable GetTable(string fullPath)
        {
            return (LuaTable)this[fullPath];
        }

#if ! UNITY_IPHONE
        /*
         * Gets a table global variable as an object implementing
         * the interfaceType interface
         */
        public object GetTable(Type interfaceType, string fullPath)
        {
				translator.throwError(L,"Tables as interfaces not implemnented");
            return CodeGeneration.Instance.GetClassInstance(interfaceType,GetTable(fullPath));
        }
#endif
        /*
         * Gets a function global variable
         */
        public LuaFunction GetFunction(string fullPath)
        {
            object obj=this[fullPath];
            return (obj is LuaCSFunction ? new LuaFunction((LuaCSFunction)obj,this) : (LuaFunction)obj);
        }

		public void RegisterLuaDelegateType (Type delegateType, Type luaDelegateType)
		{
			CodeGeneration.Instance.RegisterLuaDelegateType (delegateType, luaDelegateType);
		}

        /*
         * Gets a function global variable as a delegate of
         * type delegateType
         */
        public Delegate GetFunction(Type delegateType,string fullPath)
        {
            return CodeGeneration.Instance.GetDelegate(delegateType,GetFunction(fullPath));
        }
        /*
         * Calls the object as a function with the provided arguments,
         * returning the function's returned values inside an array
         */
        internal object[] callFunction(object function,object[] args)
        {
            return callFunction(function, args, null);
        }


        /*
         * Calls the object as a function with the provided arguments and
         * casting returned values to the types in returnTypes before returning
         * them in an array
         */
        internal object[] callFunction(object function,object[] args,Type[] returnTypes)
        {
            int nArgs=0;
            int oldTop=LuaLib.LuaGetTop(L);
            if(!LuaLib.LuaCheckStack(L,args.Length+6))
                throw new LuaException("Lua stack overflow");
            translator.push(L,function);
            if(args!=null)
            {
                nArgs=args.Length;
                for(int i=0;i<args.Length;i++)
                {
                    translator.push(L,args[i]);
                }
            }
            int error = LuaLib.LuaPCall(L, nArgs, -1, 0);
            if (error != 0)
                ThrowExceptionFromError(oldTop);

            if(returnTypes != null)
                return translator.popValues(L,oldTop,returnTypes);
            else
                return translator.popValues(L, oldTop);
        }
        /*
         * Navigates a table to set the value of one of its fields
         */
        internal void setObject(string[] remainingPath, object val)
        {
            for(int i=0; i<remainingPath.Length-1;i++)
            {
                LuaLib.LuaPushString(L,remainingPath[i]);
                LuaLib.LuaGetTable(L,-2);
            }
            LuaLib.LuaPushString(L,remainingPath[remainingPath.Length-1]);
            translator.push(L,val);
            LuaLib.LuaSetTable(L,-3);
        }
        /*
         * Creates a new table as a global variable or as a field
         * inside an existing table
         */
        public void NewTable(string fullPath)
        {
            string[] path=fullPath.Split(new char[] { '.' });
            int oldTop=LuaLib.LuaGetTop(L);
            if(path.Length==1)
            {
                LuaLib.LuaNewTable(L);
                LuaLib.LuaSetGlobal(L,fullPath);
            }
            else
            {
                LuaLib.LuaGetGlobal(L,path[0]);
                for(int i=1; i<path.Length-1;i++)
                {
                    LuaLib.LuaPushString(L,path[i]);
                    LuaLib.LuaGetTable(L,-2);
                }
                LuaLib.LuaPushString(L,path[path.Length-1]);
                LuaLib.LuaNewTable(L);
                LuaLib.LuaSetTable(L,-3);
            }
            LuaLib.LuaSetTop(L,oldTop);
        }
		
		public LuaTable NewTable()
        {
            int oldTop=LuaLib.LuaGetTop(L);
			
            LuaLib.LuaNewTable(L);
			LuaTable returnVal = (LuaTable)translator.getObject(L,-1);
			
            LuaLib.LuaSetTop(L,oldTop);
			return returnVal;
        }

        public ListDictionary GetTableDict(LuaTable table)
        {
            ListDictionary dict = new ListDictionary();

            int oldTop = LuaLib.LuaGetTop(L);
            translator.push(L, table);
            LuaLib.LuaPushNil(L);
            while (LuaLib.LuaNext(L, -2) != 0)
            {
                dict[translator.getObject(L, -2)] = translator.getObject(L, -1);
                LuaLib.LuaSetTop(L, -2);
            }
            LuaLib.LuaSetTop(L, oldTop);

            return dict;
        }

        /*
         * Lets go of a previously allocated reference to a table, function
         * or userdata
         */

        internal void dispose(int reference)
        {
            if (L != IntPtr.Zero) //Fix submitted by Qingrui Li
                LuaLib.lua_unref(L,reference);
        }
        /*
         * Gets a field of the table corresponding to the provided reference
         * using rawget (do not use metatables)
         */
        internal object rawGetObject(int reference,string field)
        {
            int oldTop=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,reference);
            LuaLib.LuaPushString(L,field);
            LuaLib.Lua_RawGet(L,-2);
            object obj=translator.getObject(L,-1);
            LuaLib.LuaSetTop(L,oldTop);
            return obj;
        }
        /*
         * Gets a field of the table or userdata corresponding to the provided reference
         */
        internal object getObject(int reference,string field)
        {
            int oldTop=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,reference);
            object returnValue=getObject(field.Split(new char[] {'.'}));
            LuaLib.LuaSetTop(L,oldTop);
            return returnValue;
        }
        /*
         * Gets a numeric field of the table or userdata corresponding the the provided reference
         */
        internal object getObject(int reference,object field)
        {
            int oldTop=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,reference);
            translator.push(L,field);
            LuaLib.LuaGetTable(L,-2);
            object returnValue=translator.getObject(L,-1);
            LuaLib.LuaSetTop(L,oldTop);
            return returnValue;
        }
        /*
         * Sets a field of the table or userdata corresponding the the provided reference
         * to the provided value
         */
        internal void setObject(int reference, string field, object val)
        {
            int oldTop=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,reference);
            setObject(field.Split(new char[] {'.'}),val);
            LuaLib.LuaSetTop(L,oldTop);
        }
        /*
         * Sets a numeric field of the table or userdata corresponding the the provided reference
         * to the provided value
         */
        internal void setObject(int reference, object field, object val)
        {
            int oldTop=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,reference);
            translator.push(L,field);
            translator.push(L,val);
            LuaLib.LuaSetTable(L,-3);
            LuaLib.LuaSetTop(L,oldTop);
        }

        /*
         * Registers an object's method as a Lua function (global or table field)
         * The method may have any signature
         */
        public LuaFunction RegisterFunction(string path, object target, MethodBase function /*MethodInfo function*/)  //CP: Fix for struct constructor by Alexander Kappner (link: http://luaforge.net/forum/forum.php?thread_id=2859&forum_id=145)
        {
            // We leave nothing on the stack when we are done
            int oldTop = LuaLib.LuaGetTop(L);

            LuaMethodWrapper wrapper=new LuaMethodWrapper(translator,target,function.DeclaringType,function);
            translator.push(L,new LuaCSFunction(wrapper.call));

            this[path]=translator.getObject(L,-1);
            LuaFunction f = GetFunction(path);

            LuaLib.LuaSetTop(L, oldTop);

            return f;
        }
		
		public LuaFunction CreateFunction(object target, MethodBase function /*MethodInfo function*/)  //CP: Fix for struct constructor by Alexander Kappner (link: http://luaforge.net/forum/forum.php?thread_id=2859&forum_id=145)
        {
            // We leave nothing on the stack when we are done
            int oldTop = LuaLib.LuaGetTop(L);

            LuaMethodWrapper wrapper=new LuaMethodWrapper(translator,target,function.DeclaringType,function);
            translator.push(L,new LuaCSFunction(wrapper.call));
			
			object obj = translator.getObject(L,-1);
			LuaFunction f = (obj is LuaCSFunction ? new LuaFunction((LuaCSFunction)obj,this) : (LuaFunction)obj);

            LuaLib.LuaSetTop(L, oldTop);

            return f;
        }


        /*
         * Compares the two values referenced by ref1 and ref2 for equality
         */
        internal bool compareRef(int ref1, int ref2)
        {
            int top=LuaLib.LuaGetTop(L);
            LuaLib.lua_getref(L,ref1);
            LuaLib.lua_getref(L,ref2);
            int equal=LuaLib.LuaEqual(L,-1,-2);
            LuaLib.LuaSetTop(L,top);
            return (equal!=0);
        }

        internal void pushCSFunction(LuaCSFunction function)
        {
            translator.pushFunction(L,function);
        }

        #region IDisposable Members

        public void Dispose()
        {
			Dispose(true);

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }
		
		public virtual void Dispose(bool dispose)
		{
			if( dispose )
			{
			    if (translator != null)
	            {
	                translator.pendingEvents.Dispose();
	                translator = null;
	            }	
			}
		}

        #endregion


    }

    public class LuaThread : LuaState
    {
        // Tracks if thread is running or not
        private bool start = false;

        // Keeps reference of thread in registry to prevent GC
        private int threadRef;

        // Hold on to parent for later
        private LuaState parent;

        // Func running on
        private LuaFunction func;

        public LuaThread( LuaState parentState, LuaFunction threadFunc )
        {
			// Copy from parent
			this.tracebackFunction = parentState.tracebackFunction;			
			this.translator = parentState.translator;
			this.translator.interpreter = this;
			
			this.panicCallback = parentState.panicCallback;

			this.printFunction = parentState.printFunction;
			this.loadfileFunction = parentState.loadfileFunction;
			this.loaderFunction = parentState.loaderFunction;
			this.dofileFunction = parentState.dofileFunction;
			
            // Assign to store
            func = threadFunc;
            parent = parentState;

            // Create Thread
            L = LuaLib.LuaNewThread( parent.L );

            // Store thread in registry
            threadRef = LuaLib.LuaLRef( parent.L, LuaIndexes.LUA_REGISTRYINDEX );
        }

		#region IDisposable Members	
		public override void Dispose(bool dispose)
		{
			if( dispose )
			{
			    LuaLib.LuaLUnRef( parent.L, LuaIndexes.LUA_REGISTRYINDEX, threadRef );
			}
		}
        #endregion

        public void Start()
        {
            if(IsInactive() && !start)
            {
                start = true;
            }
        }
		
		public int Resume()
		{
			return Resume(null, null);	
		}

        public int Resume(object[] args, LuaTable env)
        {
            int result = 0;
			int oldTop = LuaLib.LuaGetTop(L);

            // If thread isn't started, it needs to be restarted
            if( start )
            {
				start = false;
                func.push( L );
				
				if (env != null)
	            {
	                env.push(L);
	                LuaLib.LuaSetFEnv(L, -2);
	            }
				
                result = resume(args, oldTop);

            }
            // If thread is suspended, resume it
            else if( IsSuspended() )
            {
                result = resume(args, oldTop);
            }

            return result;
        }

        private int resume(object[] args, int oldTop)
        {
			int nArgs=0;
			
			// Push args
            if(args!=null)
            {
                nArgs=args.Length;
                for(int i=0;i<args.Length;i++)
                {
                    translator.push(L,args[i]);
                }
            }			
			
			// Call func
			int r = 0;
            r = LuaLib.LuaResume( L, nArgs );
			
			if( r > (int)LuaThreadStatus.LUA_YIELD )
            {
                // Error
                int top = LuaLib.LuaGetTop(L);
                ThrowExceptionFromError(top);
            }

            return r;
		}

        public bool IsStarted()
        {
            return start;
        }

        public bool IsSuspended()
        {
            int status = LuaLib.LuaStatus( L );
            
            return (status == (int)LuaThreadStatus.LUA_YIELD);
        }
		
		public bool IsDead()
        {
            int status = LuaLib.LuaStatus( L );
            
            return (status > (int)LuaThreadStatus.LUA_YIELD);
        }
        
        public bool IsInactive()
        {
            int status = LuaLib.LuaStatus( L );
            
            return (status == 0);
        }

    }
}

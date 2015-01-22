using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using LuaInterface;
using System.Text;
using System.IO;
using System.Collections.Generic;


namespace X
{
    public class Example : MonoBehaviour
    {
        public string LuaFile;
        public string Parameter;
		private LuaTable Libs;

        void Start()
        {
            if (string.IsNullOrEmpty(LuaFile))
            {
                LuaFile = string.Format("{0}.lua", this.gameObject.name.ToLower());
            }
			var ret = mLuaState.DoFile(LuaFile);
			if (ret != null && ret.Length == 1)
				Libs = ret[0] as LuaTable;

			Call("Start", this);
        }

        void Update()
        {
			Call("Update", this);
        }

        public object[] Call(string function, params object[] args)
        {
			if (Libs == null)
				return null;

			var fn = Libs[function] as LuaFunction;
			if (fn == null)
				return null;

			return fn.Call(args);
        }

        public GameObject FindChildrenObject(string name)
        {
            return FindChildrenObject(this.gameObject, name);
        }

        GameObject FindChildrenObject(GameObject go, string name)
        {
            var items = go.GetComponentsInChildren(typeof(Transform));
            foreach (var item in items)
            {
                if (item.name == name)
                {
                    return item.gameObject;
                }
            }

            foreach (var item in items)
            {
                var ret = FindChildrenObject(item.gameObject, name);
                if (ret != null)
                    return ret;
            }

            return null;
        }

        /// <summary>
        /// core!
        /// </summary>
        static LuaState mLuaState;
		static LuaFunction mGetFunction;

		static Example()
        {
            mLuaState = new LuaState();
        }

		public static void RegisterLuaDelegateType(Type delegateType, Type luaDelegateType)
		{
			mLuaState.RegisterLuaDelegateType(delegateType, luaDelegateType);
		}
    }
}

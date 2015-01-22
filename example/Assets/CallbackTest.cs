using UnityEngine;
using System.Collections;

public class CallbackTest : MonoBehaviour
{
    class LuaBoolDelegateEventArgsHandler : LuaInterface.LuaDelegate
    {
        bool CallFunction(GameObject go)
        {
            object[] args = new object[] { go };
            object[] inArgs = new object[] { go };
            int[] outArgs = new int[] { };
            return (bool)base.callFunction(args, inArgs, outArgs);
        }
    }

    public delegate bool BoolDelegate(GameObject go);
    public BoolDelegate OnStart;

    void Awake()
    {
        // for ios!
		X.Example.RegisterLuaDelegateType(typeof(CallbackTest.BoolDelegate), typeof(LuaBoolDelegateEventArgsHandler));
    }

	void Start () 
    {
        if (OnStart != null)
        {
            bool ret = OnStart(this.gameObject);
            Debug.Log(string.Format("CallbackTest Return:{0}", ret));
        }
	}
	
	void Update ()
    {
	
	}
}

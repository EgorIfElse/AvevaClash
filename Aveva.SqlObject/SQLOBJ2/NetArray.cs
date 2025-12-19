using System;
using System.Collections;
using Aveva.Core.PMLNet;

namespace SQLOBJ2;

[PMLNetCallable]
public class NetArray
{
	private Hashtable mval;

	[PMLNetCallable]
	public Hashtable Val
	{
		get
		{
			return mval;
		}
		set
		{
			mval = value;
		}
	}

	[PMLNetCallable]
	public NetArray()
	{
		mval = new Hashtable();
	}

	[PMLNetCallable]
	public NetArray(Hashtable val)
	{
		mval = val;
	}

	[PMLNetCallable]
	public void Assign(NetArray that)
	{
		mval = that.mval;
	}

	[PMLNetCallable]
	public double Count()
	{
		return mval.Count;
	}

	[PMLNetCallable]
	public void Append(Hashtable val)
	{
		IDictionaryEnumerator enumerator = val.GetEnumerator();
		while (enumerator.MoveNext())
		{
			mval.Add(enumerator.Key, enumerator.Value);
		}
	}

	[PMLNetCallable]
	public void Copy(ref Hashtable val)
	{
		IDictionaryEnumerator enumerator = mval.GetEnumerator();
		while (enumerator.MoveNext())
		{
			Console.WriteLine(enumerator.Value.ToString());
			val.Add(enumerator.Key, enumerator.Value);
		}
	}

	[PMLNetCallable]
	public override string ToString()
	{
		string text = "";
		IDictionaryEnumerator enumerator = mval.GetEnumerator();
		while (enumerator.MoveNext())
		{
			text += "\n";
			string text2 = text;
			text = text2 + " [" + enumerator.Key.ToString() + "] <" + enumerator.Value.GetType().ToString() + ">" + enumerator.Value.ToString();
		}
		return text;
	}
}

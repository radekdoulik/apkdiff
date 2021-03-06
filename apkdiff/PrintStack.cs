﻿using System;
using System.Collections.Generic;
using System.Text;

namespace apkdiff
{
	class PrintStack
	{
		List<Action> headers = new List<Action> ();
		public bool Quiet { get; set; }

		public int Push (Action action)
		{
			headers.Add (action);

			return headers.Count;
		}

		public void Pop (int count)
		{
			if (headers.Count == count)
				headers.RemoveAt (headers.Count - 1);
		}

		public void Invoke ()
		{
			if (!Quiet)
				foreach (var d in headers)
					d.DynamicInvoke ();

			headers.Clear ();
		}
	}
}

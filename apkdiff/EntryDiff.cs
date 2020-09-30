using System;

namespace apkdiff {
	public abstract class EntryDiff {

		public abstract string Name { get; }

		public abstract void Compare (string file, string other, string padding = null);
	}
}

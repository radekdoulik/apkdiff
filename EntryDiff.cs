using System;

namespace apkdiff {
	public abstract class EntryDiff {
		public static EntryDiff ForExtension (string extension)
		{
			switch (extension) {
			case ".so":
				return new SharedLibraryDiff ();
			}

			return null;
		}

		public abstract void Compare (string file, string other);
	}
}
